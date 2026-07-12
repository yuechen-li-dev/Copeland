using System.Security.Cryptography;
using Aurelian.Core.Compositor;
using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Tests.Fixtures.Spirv;
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
using Aurelian.Runtime.Compositor;
using Xunit;

namespace Aurelian.Integration.Tests.Support;

internal sealed class VulkanVisibleFrameTestFixture : IDisposable
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

    private VulkanVisibleFrameTestFixture(
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
        uint acquiredImageIndex,
        AurelianFrameId frameId,
        PlantOutputRef outputRef,
        VulkanPresentationTargetImageSet presentationTargets,
        AurelianEngine engine,
        VulkanCompositorMechanismAdapter adapter,
        CompositorActuationBridge bridge,
        AurelianFramePump framePump,
        AurelianFrameInput input)
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
        AcquiredImageIndex = acquiredImageIndex;
        FrameId = frameId;
        OutputRef = outputRef;
        PresentationTargets = presentationTargets;
        Engine = engine;
        Adapter = adapter;
        Bridge = bridge;
        FramePump = framePump;
        Input = input;
    }

    public uint AcquiredImageIndex { get; }

    public AurelianFrameId FrameId { get; }

    public PlantOutputRef OutputRef { get; }

    public VulkanPresentationTargetImageSet PresentationTargets { get; }

    public AurelianEngine Engine { get; }

    public VulkanCompositorMechanismAdapter Adapter { get; }

    public CompositorActuationBridge Bridge { get; }

    public AurelianFramePump FramePump { get; }

    public AurelianFrameInput Input { get; }

    public static bool TryCreate(ulong frameIdValue, out VulkanVisibleFrameTestFixture? fixture)
    {
        fixture = null;
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));

        if (!init.Success)
        {
            using (init.Plant)
            {
                Assert.NotEmpty(init.Diagnostics);
            }

            return false;
        }

        AurelianVulkanPlant plant = init.Plant!;
        VulkanSwapchainCreateResult swapchainResult = VulkanSwapchainFactory.Create(
            plant,
            new VulkanSwapchainCreateOptions(Width: 128, Height: 128, VSync: true, Title: "A60 visible frame pump"));

        if (!swapchainResult.Success)
        {
            using (plant)
            using (swapchainResult.Surface)
            using (swapchainResult.Swapchain)
            {
                Assert.NotEmpty(swapchainResult.Diagnostics);
                Assert.Contains(swapchainResult.Status, new[] { VulkanPresentationStatus.Unavailable, VulkanPresentationStatus.Rejected, VulkanPresentationStatus.Failed });
            }

            return false;
        }

        AurelianVulkanSwapchain swapchain = swapchainResult.Swapchain!;
        VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();
        if (acquire.Status is not (VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal) || acquire.ImageIndex is null)
        {
            using (plant)
            using (swapchainResult.Surface)
            using (swapchain)
            {
                Assert.Contains(acquire.Status, new[]
                {
                    VulkanSwapchainAcquireStatus.OutOfDate,
                    VulkanSwapchainAcquireStatus.Unavailable,
                    VulkanSwapchainAcquireStatus.Failed,
                });
                Assert.NotEmpty(acquire.Diagnostics);
            }

            return false;
        }

        if (!TryMapTextureFormat(swapchain.Facts.SelectedFormat, out VulkanTextureFormat offscreenFormat))
        {
            using (plant)
            using (swapchainResult.Surface)
            using (swapchain)
            {
                Assert.NotEmpty(swapchain.Facts.SelectedFormat);
            }

            return false;
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
        AurelianVulkanGraphicsPipeline pipeline = CreatePipeline(plant, renderPass);
        AurelianVulkanBuffer vertexBuffer = CreateVertexBuffer(plant, allocator);

        VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(
            vertexBuffer,
            CreateTriangleVertexBytes(),
            DebugName: "a60.visible-frame-pump-triangle-vertices"));
        Assert.True(upload.Success, FormatDiagnostics(upload));

        VulkanFenceOperationResult uploadWait = fences.CommandListFence.WaitForValue(upload.SignalFenceValue!.Value, FenceWaitTimeoutNanoseconds);
        Assert.True(uploadWait.Success, FormatDiagnostics(uploadWait));

        RecordAndSubmitOffscreenTriangle(plant, commandPool, submitter, renderPass, framebuffer, pipeline, vertexBuffer);
        Assert.Equal(VulkanResourceLayout.TransferSource, offscreenColor.LayoutTracker.Get(0, 0));

        var frameId = new AurelianFrameId(frameIdValue);
        PlantOutputRef outputRef = new(plant.Context.Id.Value, frameId.Value, "triangle.offscreen");
        VulkanPlantOutputImageSet outputs = new([new VulkanPlantOutputImage(outputRef, offscreenColor)]);
        VulkanPresentationTargetImageSet presentationTargets = swapchain.CreatePresentationTargetImageSet();
        PresentationTargetRef target = new(plant.Context.Id.Value, acquire.ImageIndex.Value, frameId.Value);
        CompositorPolicyFacts facts = Facts(frameId.Value, outputRef, target, PlantOutputReadinessStatus.Ready);
        var adapter = new VulkanCompositorMechanismAdapter(compositor, outputs, presentationTargets);
        var bridge = new CompositorActuationBridge(adapter);
        var engine = new AurelianEngine();
        AurelianEngineResult engineStart = engine.Start();
        Assert.True(engineStart.Success, FormatDiagnostics(engineStart));
        var framePump = new AurelianFramePump(engine, bridge);
        var input = new AurelianFrameInput(frameId, facts);

        fixture = new VulkanVisibleFrameTestFixture(
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
            acquire.ImageIndex.Value,
            frameId,
            outputRef,
            presentationTargets,
            engine,
            adapter,
            bridge,
            framePump,
            input);
        return true;
    }

    public VulkanSwapchainPresentResult Present() => swapchain.Present(AcquiredImageIndex);

    public void Dispose()
    {
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

    private static CompositorPolicyFacts Facts(
        ulong frameId,
        PlantOutputRef output,
        PresentationTargetRef target,
        PlantOutputReadinessStatus status)
    {
        var readiness = new PlantOutputReadiness(
            output,
            status,
            CompletedFenceValue: status is PlantOutputReadinessStatus.Ready or PlantOutputReadinessStatus.Reused ? frameId : null);
        var frameFacts = new CompositorFrameFacts(frameId, [readiness], CompositorDiagnostics.Empty);
        var required = new RequiredPlantOutputSet(frameId, CompositorPolicyKind.Passthrough, [output]);
        return new CompositorPolicyFacts(frameFacts, required, target, CompositorPolicyKind.Passthrough);
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
                DebugName: "a60.visible-frame-pump.offscreen-color"));

        Assert.True(result.Success, FormatDiagnostics(result));
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

        Assert.True(result.Success, FormatDiagnostics(result));
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

        Assert.True(result.Success, FormatDiagnostics(result));
        return result.Framebuffer!;
    }

    private static AurelianVulkanGraphicsPipeline CreatePipeline(AurelianVulkanPlant plant, AurelianVulkanRenderPass renderPass)
    {
        VulkanCompiledGraphicsPipelineCreateResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreatePipeline(
            plant,
            renderPass,
            CreateTriangleCompiledShaderProgram(),
            [new VulkanVertexBufferLayoutDescriptor(Binding: 0, Stride: 24)],
            [
                new VulkanVertexAttributeDescriptor(Location: 0, Binding: 0, VulkanVertexAttributeFormat.Float2, Offset: 0),
                new VulkanVertexAttributeDescriptor(Location: 1, Binding: 0, VulkanVertexAttributeFormat.Float4, Offset: 8),
            ]);

        Assert.True(result.Success, FormatDiagnostics(result));
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
                "a60.visible-frame-pump.vertex-buffer"));

        Assert.True(result.Success, FormatDiagnostics(result));
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
        Assert.True(beginCommandBuffer.Success, FormatDiagnostics(beginCommandBuffer));

        var renderPassEncoder = new VulkanRenderPassCommandEncoder();
        VulkanRenderPassBeginResult beginRenderPass = renderPassEncoder.Begin(
            plant,
            commandBuffer,
            new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.OpaqueBlack));
        Assert.True(beginRenderPass.Success, FormatDiagnostics(beginRenderPass));

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
        Assert.True(draw.Success, FormatDiagnostics(draw));

        VulkanRenderPassCommandResult endRenderPass = renderPassEncoder.End(plant, commandBuffer, beginRenderPass.Scope.Value);
        Assert.True(endRenderPass.Success, FormatDiagnostics(endRenderPass));

        VulkanCommandBufferOperationResult endCommandBuffer = commandBuffer.End();
        Assert.True(endCommandBuffer.Success, FormatDiagnostics(endCommandBuffer));

        VulkanCommandSubmitResult submit = submitter.Submit(new VulkanCommandSubmitRequest(
            commandBuffer,
            WaitForCompletion: true,
            TimeoutNanoseconds: FenceWaitTimeoutNanoseconds,
            DebugName: "a60.visible-frame-pump.offscreen-submit"));
        Assert.True(submit.Success, FormatDiagnostics(submit));
    }

    private static CompiledShaderProgram CreateTriangleCompiledShaderProgram()
        => new(
            CompiledShaderProgram.CurrentFormatVersion,
            [
                new CompiledShaderStage(
                    CompiledShaderStageKind.Vertex,
                    TriangleSpirvFixtures.VertexEntryPoint,
                    TriangleSpirvFixtures.VertexProfile,
                    TriangleSpirvFixtures.VertexBytes,
                    Sha256(TriangleSpirvFixtures.VertexBytes),
                    TriangleSpirvFixtures.VertexSourceName),
                new CompiledShaderStage(
                    CompiledShaderStageKind.Fragment,
                    TriangleSpirvFixtures.FragmentEntryPoint,
                    TriangleSpirvFixtures.FragmentProfile,
                    TriangleSpirvFixtures.FragmentBytes,
                    Sha256(TriangleSpirvFixtures.FragmentBytes),
                    TriangleSpirvFixtures.FragmentSourceName),
            ]);

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

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FormatDiagnostics(AurelianEngineResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanBufferUploadResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanFenceOperationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCommandSubmitResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanTextureCreateResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassCreateResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanFramebufferCreateResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCompiledGraphicsPipelineCreateResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanBufferCreateResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCommandBufferOperationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassBeginResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanDrawCommandResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassCommandResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
