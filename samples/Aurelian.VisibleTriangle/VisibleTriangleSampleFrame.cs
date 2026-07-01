using Aurelian.Core.Compositor;
using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Engine.Graphics;
using Aurelian.Core.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.VisibleTriangle;

internal sealed class VisibleTriangleSampleFrame : IDisposable
{
    private const ulong FenceWaitTimeoutNanoseconds = 5_000_000_000;

    private readonly AurelianVulkanPlant plant;
    private readonly AurelianVulkanSurface? surface;
    private readonly AurelianVulkanSwapchain swapchain;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commandPool;
    private readonly VulkanBufferUploader uploader;
    private readonly VulkanCommandSubmitter submitter;
    private readonly VulkanCompositorPassthrough compositor;
    private readonly AurelianVulkanTexture offscreenColor;
    private readonly AurelianVulkanRenderPass renderPass;
    private readonly AurelianVulkanFramebuffer framebuffer;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly AurelianVulkanBuffer vertexBuffer;
    private bool disposed;

    private VisibleTriangleSampleFrame(
        AurelianVulkanPlant plant,
        AurelianVulkanSurface? surface,
        AurelianVulkanSwapchain swapchain,
        RawVulkanMemoryAllocator allocator,
        VulkanFenceBundle fences,
        VulkanCommandBufferPool commandPool,
        VulkanBufferUploader uploader,
        VulkanCommandSubmitter submitter,
        VulkanCompositorPassthrough compositor,
        AurelianVulkanTexture offscreenColor,
        AurelianVulkanRenderPass renderPass,
        AurelianVulkanFramebuffer framebuffer,
        AurelianVulkanGraphicsPipeline pipeline,
        AurelianVulkanBuffer vertexBuffer,
        AurelianEngine engine,
        AurelianFramePump framePump,
        AurelianFrameId startFrameId,
        VisibleTriangleFrameInputProvider inputProvider,
        VisibleTriangleSamplePresentationMechanism presentationMechanism,
        VisibleTriangleWindowState windowState)
    {
        this.plant = plant;
        this.surface = surface;
        this.swapchain = swapchain;
        this.allocator = allocator;
        this.fences = fences;
        this.commandPool = commandPool;
        this.uploader = uploader;
        this.submitter = submitter;
        this.compositor = compositor;
        this.offscreenColor = offscreenColor;
        this.renderPass = renderPass;
        this.framebuffer = framebuffer;
        this.pipeline = pipeline;
        this.vertexBuffer = vertexBuffer;
        Engine = engine;
        FramePump = framePump;
        StartFrameId = startFrameId;
        InputProvider = inputProvider;
        PresentationMechanism = presentationMechanism;
        WindowState = windowState;
    }

    public AurelianEngine Engine { get; }

    public AurelianFramePump FramePump { get; }

    public AurelianFrameId StartFrameId { get; }

    public VisibleTriangleFrameInputProvider InputProvider { get; }

    public VisibleTriangleSamplePresentationMechanism PresentationMechanism { get; }

    public VisibleTriangleWindowState WindowState { get; }

    public bool CloseRequested => WindowState.CloseRequested;

    public string SwapchainDescription => $"{swapchain.Facts.Width}x{swapchain.Facts.Height} {swapchain.Facts.SelectedFormat} {swapchain.Facts.SelectedPresentMode}";

    public static VisibleTriangleSampleFrame Create(bool enableValidation, int frameCount, CompiledShaderProgram shaderProgram)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Visible triangle sample requires at least one planned frame.");
        }

        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: enableValidation, EnablePresentation: true));

        if (!init.Success)
        {
            using (init.Plant)
            {
                throw new VisibleTriangleSampleException($"Vulkan presentation plant creation failed: {FormatDiagnostics(init)}");
            }
        }

        AurelianVulkanPlant plant = init.Plant!;
        try
        {
            VulkanSwapchainCreateResult swapchainResult = VulkanSwapchainFactory.Create(
                plant,
                new VulkanSwapchainCreateOptions(
                    Width: 640,
                    Height: 480,
                    VSync: true,
                    Title: "Aurelian Visible Triangle",
                    Visible: true));

            if (!swapchainResult.Success)
            {
                using (swapchainResult.Surface)
                using (swapchainResult.Swapchain)
                {
                    throw new VisibleTriangleSampleException($"Vulkan swapchain/window creation failed with status {swapchainResult.Status}: {FormatDiagnostics(swapchainResult)}");
                }
            }

            AurelianVulkanSwapchain swapchain = swapchainResult.Swapchain!;
            try
            {
                if (!TryMapTextureFormat(swapchain.Facts.SelectedFormat, out VulkanTextureFormat offscreenFormat))
                {
                    throw new VisibleTriangleSampleException($"Swapchain format '{swapchain.Facts.SelectedFormat}' is not mapped to an offscreen texture format by the A67 sample.");
                }

                var allocator = new RawVulkanMemoryAllocator(plant);
                var fences = VulkanFenceBundle.Create(plant);
                var commandPool = VulkanCommandBufferPool.Create(plant);
                var uploader = new VulkanBufferUploader(plant, allocator, commandPool, fences);
                var submitter = new VulkanCommandSubmitter(plant, commandPool, fences);
                var compositor = new VulkanCompositorPassthrough(plant, commandPool, submitter);
                AurelianVulkanTexture offscreenColor = CreateOffscreenColorTarget(plant, allocator, swapchain, offscreenFormat);
                AurelianVulkanRenderPass renderPass = CreateRenderPass(plant, offscreenFormat);
                AurelianVulkanFramebuffer framebuffer = CreateFramebuffer(plant, renderPass, offscreenColor, swapchain);
                AurelianVulkanGraphicsPipeline pipeline = CreatePipeline(plant, renderPass, shaderProgram);
                AurelianVulkanBuffer vertexBuffer = CreateVertexBuffer(plant, allocator);

                VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(
                    vertexBuffer,
                    CreateTriangleVertexBytes(),
                    DebugName: "a67.visible-triangle.vertices"));
                Ensure(upload.Success, $"Vertex upload failed: {FormatDiagnostics(upload)}");

                VulkanFenceOperationResult uploadWait = fences.CommandListFence.WaitForValue(upload.SignalFenceValue!.Value, FenceWaitTimeoutNanoseconds);
                Ensure(uploadWait.Success, $"Vertex upload fence wait failed: {FormatDiagnostics(uploadWait)}");

                RecordAndSubmitOffscreenTriangle(plant, commandPool, submitter, renderPass, framebuffer, pipeline, vertexBuffer);

                var startFrameId = new AurelianFrameId(67);
                const string outputImageId = "triangle.offscreen";
                VulkanPlantOutputImageSet outputs = CreateFinitePlantOutputImageSet(plant.Context.Id.Value, startFrameId, frameCount, outputImageId, offscreenColor);
                VulkanPresentationTargetImageSet presentationTargets = swapchain.CreatePresentationTargetImageSet();
                var pendingPresentImageIndices = new Queue<uint>();
                var windowState = new VisibleTriangleWindowState();
                var inputProvider = new VisibleTriangleFrameInputProvider(swapchain, plant.Context.Id.Value, outputImageId, pendingPresentImageIndices, windowState, swapchainResult.Surface, frameCount);
                var presentationMechanism = new VisibleTriangleSamplePresentationMechanism(swapchain, pendingPresentImageIndices, windowState, swapchainResult.Surface);
                var adapter = new VulkanCompositorMechanismAdapter(compositor, outputs, presentationTargets);
                var preparedGraphics = new AurelianPreparedGraphicsSubsystem(
                    AurelianEngineGraphicsOptions.PreparedVisible,
                    adapter,
                    presentationMechanism);
                AurelianPreparedGraphicsSubsystemResult preparedResult = AurelianPreparedGraphicsSubsystemValidation.Validate(preparedGraphics);
                Ensure(preparedResult.Success, $"Prepared visible graphics validation failed: {FormatDiagnostics(preparedResult)}");

                var bridge = new CompositorActuationBridge(adapter);
                var engine = new AurelianEngine(new AurelianEngineOptions(
                    Name: "Aurelian Visible Triangle",
                    Graphics: AurelianEngineGraphicsOptions.PreparedVisible));
                AurelianEngineResult engineStart = engine.Start();
                Ensure(engineStart.Success, $"Engine start failed: {FormatDiagnostics(engineStart)}");

                var framePump = new AurelianFramePump(engine, bridge);

                return new VisibleTriangleSampleFrame(
                    plant,
                    swapchainResult.Surface,
                    swapchain,
                    allocator,
                    fences,
                    commandPool,
                    uploader,
                    submitter,
                    compositor,
                    offscreenColor,
                    renderPass,
                    framebuffer,
                    pipeline,
                    vertexBuffer,
                    engine,
                    framePump,
                    startFrameId,
                    inputProvider,
                    presentationMechanism,
                    windowState);
            }
            catch
            {
                swapchain.Dispose();
                swapchainResult.Surface?.Dispose();
                throw;
            }
        }
        catch
        {
            plant.Dispose();
            throw;
        }
    }

    public void PumpEvents() => WindowState.Pump(surface);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (Engine.Status == AurelianEngineStatus.Started)
        {
            _ = Engine.Stop();
        }

        vertexBuffer.Dispose();
        pipeline.Dispose();
        framebuffer.Dispose();
        renderPass.Dispose();
        offscreenColor.Dispose();
        compositor.Dispose();
        uploader.Dispose();
        submitter.Dispose();
        commandPool.Dispose();
        fences.Dispose();
        allocator.Dispose();
        swapchain.Dispose();
        surface?.Dispose();
        plant.Dispose();
    }

    private static VulkanPlantOutputImageSet CreateFinitePlantOutputImageSet(
        uint plantId,
        AurelianFrameId startFrameId,
        int frameCount,
        string outputImageId,
        AurelianVulkanTexture offscreenColor)
    {
        var outputs = new List<VulkanPlantOutputImage>(frameCount);
        AurelianFrameId frameId = startFrameId;
        for (int i = 0; i < frameCount; i++)
        {
            outputs.Add(new VulkanPlantOutputImage(new PlantOutputRef(plantId, frameId.Value, outputImageId), offscreenColor));
            frameId = frameId.Next();
        }

        return new VulkanPlantOutputImageSet(outputs);
    }

    private static AurelianVulkanTexture CreateOffscreenColorTarget(
        AurelianVulkanPlant plant,
        RawVulkanMemoryAllocator allocator,
        AurelianVulkanSwapchain swapchain,
        VulkanTextureFormat format)
    {
        VulkanTextureCreateResult result = VulkanTextureFactory.Create(
            plant,
            allocator,
            new VulkanTextureCreatePlan(
                plant.Context.Id,
                swapchain.Facts.Width,
                swapchain.Facts.Height,
                format,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.TransferSource | VulkanTextureUsage.TransferDestination,
                VulkanMemoryUsage.GpuOnly,
                VulkanResourceLayout.Undefined,
                DebugName: "a66.visible-triangle.offscreen-color"));

        Ensure(result.Success, $"Offscreen color target creation failed: {FormatDiagnostics(result)}");
        return result.Texture!;
    }

    private static AurelianVulkanRenderPass CreateRenderPass(AurelianVulkanPlant plant, VulkanTextureFormat format)
    {
        VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
            plant,
            new VulkanRenderPassDescriptor([
                new VulkanRenderPassAttachmentDescriptor(
                    "Color0",
                    format,
                    VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined,
                    VulkanResourceLayout.TransferSource),
            ]));

        Ensure(result.Success, $"Render pass creation failed: {FormatDiagnostics(result)}");
        return result.RenderPass!;
    }

    private static AurelianVulkanFramebuffer CreateFramebuffer(
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        AurelianVulkanTexture colorAttachment,
        AurelianVulkanSwapchain swapchain)
    {
        VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
            plant,
            renderPass,
            new VulkanFramebufferDescriptor(swapchain.Facts.Width, swapchain.Facts.Height, [colorAttachment]));

        Ensure(result.Success, $"Framebuffer creation failed: {FormatDiagnostics(result)}");
        return result.Framebuffer!;
    }

    private static AurelianVulkanGraphicsPipeline CreatePipeline(
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        CompiledShaderProgram shaderProgram)
    {
        VulkanCompiledGraphicsPipelineCreateResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreatePipeline(
            plant,
            renderPass,
            shaderProgram,
            [new VulkanVertexBufferLayoutDescriptor(Binding: 0, Stride: 24)],
            [
                new VulkanVertexAttributeDescriptor(Location: 0, Binding: 0, VulkanVertexAttributeFormat.Float2, Offset: 0),
                new VulkanVertexAttributeDescriptor(Location: 1, Binding: 0, VulkanVertexAttributeFormat.Float4, Offset: 8),
            ]);

        Ensure(result.Success, $"Graphics pipeline creation failed: {FormatDiagnostics(result)}");
        return result.Pipeline!;
    }

    private static AurelianVulkanBuffer CreateVertexBuffer(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
    {
        VulkanBufferCreateResult result = VulkanBufferFactory.Create(
            plant,
            allocator,
            new VulkanBufferCreatePlan(
                plant.Context.Id,
                SizeBytes: 72,
                VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferDestination,
                VulkanMemoryUsage.GpuOnly,
                "a66.visible-triangle.vertex-buffer"));

        Ensure(result.Success, $"Vertex buffer creation failed: {FormatDiagnostics(result)}");
        return result.Buffer!;
    }

    private static void RecordAndSubmitOffscreenTriangle(
        AurelianVulkanPlant plant,
        VulkanCommandBufferPool commandPool,
        VulkanCommandSubmitter submitter,
        AurelianVulkanRenderPass renderPass,
        AurelianVulkanFramebuffer framebuffer,
        AurelianVulkanGraphicsPipeline pipeline,
        AurelianVulkanBuffer vertexBuffer)
    {
        VulkanCommandBufferLease commandBuffer = commandPool.Rent(completedFenceValue: 0);
        VulkanCommandBufferOperationResult beginCommandBuffer = commandBuffer.Begin();
        Ensure(beginCommandBuffer.Success, $"Begin command buffer failed: {FormatDiagnostics(beginCommandBuffer)}");

        var renderPassEncoder = new VulkanRenderPassCommandEncoder();
        VulkanRenderPassBeginResult beginRenderPass = renderPassEncoder.Begin(
            plant,
            commandBuffer,
            new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.OpaqueBlack));
        Ensure(beginRenderPass.Success, $"Begin render pass failed: {FormatDiagnostics(beginRenderPass)}");

        var drawEncoder = new VulkanDrawCommandEncoder();
        VulkanDrawCommandResult draw = drawEncoder.DrawVertices(
            plant,
            commandBuffer,
            beginRenderPass.Scope!.Value,
            new VulkanDrawVerticesRequest(
                pipeline,
                vertexBuffer,
                VertexCount: 3,
                FirstVertex: 0,
                VulkanViewportScissor.FromFramebuffer(framebuffer)));
        Ensure(draw.Success, $"Triangle draw command failed: {FormatDiagnostics(draw)}");

        VulkanRenderPassCommandResult endRenderPass = renderPassEncoder.End(plant, commandBuffer, beginRenderPass.Scope.Value);
        Ensure(endRenderPass.Success, $"End render pass failed: {FormatDiagnostics(endRenderPass)}");

        VulkanCommandBufferOperationResult endCommandBuffer = commandBuffer.End();
        Ensure(endCommandBuffer.Success, $"End command buffer failed: {FormatDiagnostics(endCommandBuffer)}");

        VulkanCommandSubmitResult submit = submitter.Submit(new VulkanCommandSubmitRequest(
            commandBuffer,
            WaitForCompletion: true,
            TimeoutNanoseconds: FenceWaitTimeoutNanoseconds,
            DebugName: "a66.visible-triangle.offscreen-submit"));
        Ensure(submit.Success, $"Offscreen triangle submit failed: {FormatDiagnostics(submit)}");
    }

    private static byte[] CreateTriangleVertexBytes()
    {
        float[] vertices =
        [
            -0.5f, -0.5f, 1.0f, 0.0f, 0.0f, 1.0f,
             0.5f, -0.5f, 0.0f, 1.0f, 0.0f, 1.0f,
             0.0f,  0.5f, 0.0f, 0.0f, 1.0f, 1.0f,
        ];

        byte[] bytes = new byte[vertices.Length * sizeof(float)];
        Buffer.BlockCopy(vertices, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static bool TryMapTextureFormat(string swapchainFormat, out VulkanTextureFormat textureFormat)
    {
        textureFormat = swapchainFormat switch
        {
            "B8G8R8A8Srgb" => VulkanTextureFormat.Bgra8Srgb,
            "B8G8R8A8Unorm" => VulkanTextureFormat.Bgra8Unorm,
            "R8G8B8A8Srgb" => VulkanTextureFormat.Rgba8Srgb,
            "R8G8B8A8Unorm" => VulkanTextureFormat.Rgba8Unorm,
            _ => default,
        };

        return swapchainFormat is "B8G8R8A8Srgb" or "B8G8R8A8Unorm" or "R8G8B8A8Srgb" or "R8G8B8A8Unorm";
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new VisibleTriangleSampleException(message);
        }
    }

    private static string FormatDiagnostics(VulkanInitResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanSwapchainCreateResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanSwapchainAcquireResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(AurelianPreparedGraphicsSubsystemResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(AurelianEngineResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanBufferUploadResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanFenceOperationResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanTextureCreateResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassCreateResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanFramebufferCreateResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCompiledGraphicsPipelineCreateResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanBufferCreateResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCommandBufferOperationResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassBeginResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanDrawCommandResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassCommandResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCommandSubmitResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
