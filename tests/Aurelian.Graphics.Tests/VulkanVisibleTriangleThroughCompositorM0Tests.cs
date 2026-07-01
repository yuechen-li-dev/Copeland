using System.Security.Cryptography;
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
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanVisibleTriangleThroughCompositorM0Tests
{
    private const ulong FrameId = 54;
    private const ulong FenceWaitTimeoutNanoseconds = 5_000_000_000;

    [Fact]
    public void VulkanVisibleTriangleThroughCompositor_WhenAvailable_AcquiresDrawsCompositesAndPresents()
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            AurelianVulkanPlant plant = init.Plant!;
            VulkanSwapchainCreateResult swapchainResult = VulkanSwapchainFactory.Create(
                plant,
                new VulkanSwapchainCreateOptions(Width: 128, Height: 128, VSync: true, Title: "A54 visible triangle"));
            using (swapchainResult.Surface)
            using (swapchainResult.Swapchain)
            {
                if (!swapchainResult.Success)
                {
                    Assert.NotEmpty(swapchainResult.Diagnostics);
                    Assert.Contains(swapchainResult.Status, new[] { VulkanPresentationStatus.Unavailable, VulkanPresentationStatus.Rejected, VulkanPresentationStatus.Failed });
                    return;
                }

                AurelianVulkanSwapchain swapchain = swapchainResult.Swapchain!;
                VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();
                if (acquire.Status is not (VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal) || acquire.ImageIndex is null)
                {
                    Assert.Contains(acquire.Status, new[]
                    {
                        VulkanSwapchainAcquireStatus.OutOfDate,
                        VulkanSwapchainAcquireStatus.Unavailable,
                        VulkanSwapchainAcquireStatus.Failed,
                    });
                    Assert.NotEmpty(acquire.Diagnostics);
                    return;
                }

                if (!TryMapTextureFormat(swapchain.Facts.SelectedFormat, out VulkanTextureFormat offscreenFormat))
                {
                    return;
                }

                uint acquiredImageIndex = acquire.ImageIndex.Value;
                using var allocator = new RawVulkanMemoryAllocator(plant);
                using var fences = VulkanFenceBundle.Create(plant);
                using var commandPool = VulkanCommandBufferPool.Create(plant);
                using var uploader = new VulkanBufferUploader(plant, allocator, commandPool, fences);
                using var submitter = new VulkanCommandSubmitter(plant, commandPool, fences);
                using var compositor = new VulkanCompositorPassthrough(plant, commandPool, submitter);

                using AurelianVulkanTexture offscreenColor = CreateOffscreenColorTarget(plant, allocator, swapchain, offscreenFormat);
                using AurelianVulkanRenderPass renderPass = CreateRenderPass(plant, offscreenFormat);
                using AurelianVulkanFramebuffer framebuffer = CreateFramebuffer(plant, renderPass, offscreenColor, swapchain);
                using AurelianVulkanGraphicsPipeline pipeline = CreatePipeline(plant, renderPass);
                using AurelianVulkanBuffer vertexBuffer = CreateVertexBuffer(plant, allocator);

                VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(
                    vertexBuffer,
                    CreateTriangleVertexBytes(),
                    DebugName: "a54.visible-triangle-vertices"));
                Assert.True(upload.Success, FormatDiagnostics(upload));

                VulkanFenceOperationResult uploadWait = fences.CommandListFence.WaitForValue(upload.SignalFenceValue!.Value, FenceWaitTimeoutNanoseconds);
                Assert.True(uploadWait.Success, FormatDiagnostics(uploadWait));

                RecordAndSubmitOffscreenTriangle(plant, commandPool, submitter, renderPass, framebuffer, pipeline, vertexBuffer);
                Assert.Equal(VulkanResourceLayout.TransferSource, offscreenColor.LayoutTracker.Get(0, 0));

                PlantOutputRef outputRef = new(plant.Context.Id.Value, FrameId, "triangle.offscreen");
                VulkanPlantOutputImageSet outputs = new([new VulkanPlantOutputImage(outputRef, offscreenColor)]);
                VulkanPresentationTargetImageSet presentationTargets = swapchain.CreatePresentationTargetImageSet();
                CompositorDispatchRequest request = new(
                    FrameId,
                    CompositorPolicyKind.Passthrough,
                    [outputRef],
                    new PresentationTargetRef(plant.Context.Id.Value, acquiredImageIndex, FrameId));

                VulkanCompositorResult dispatch = compositor.Dispatch(request, outputs, presentationTargets);
                Assert.True(dispatch.Success, FormatDiagnostics(dispatch));
                Assert.Equal(VulkanResourceLayout.Present, presentationTargets.Images[(int)acquiredImageIndex].LayoutTracker.Get(0, 0));

                VulkanSwapchainPresentResult present = swapchain.Present(acquiredImageIndex);
                if (present.Status is VulkanSwapchainPresentStatus.OutOfDate or VulkanSwapchainPresentStatus.Unavailable)
                {
                    Assert.NotEmpty(present.Diagnostics);
                    return;
                }

                Assert.True(present.Success, FormatDiagnostics(present));
            }
        }
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
                DebugName: "a54.visible-triangle.offscreen-color"));

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
                "a54.visible-triangle.vertex-buffer"));

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
            DebugName: "a54.visible-triangle.offscreen-submit"));
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

    private static string FormatDiagnostics(VulkanSwapchainPresentResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCompositorResult result)
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
