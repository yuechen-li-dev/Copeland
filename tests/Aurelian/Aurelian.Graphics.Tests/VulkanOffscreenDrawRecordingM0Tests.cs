using System.Security.Cryptography;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Tests.Fixtures.Spirv;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Shaders;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanOffscreenDrawRecordingM0Tests
{
    private const ulong FenceWaitTimeoutNanoseconds = 5_000_000_000;

    [Fact]
    public void TriangleSpirvFixtures_VertexAndFragment_HaveSpirvMagic()
    {
        AssertSpirvMagic(TriangleSpirvFixtures.VertexBytes);
        AssertSpirvMagic(TriangleSpirvFixtures.FragmentBytes);
    }

    [Fact]
    public void TriangleSpirvFixtures_CanMapToVulkanShaderStages()
    {
        VulkanCompiledShaderStageMappingResult result = VulkanCompiledShaderStageMapper.ToVulkanShaderStages(
            CreateTriangleCompiledShaderProgram());

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Collection(
            result.Stages,
            vertex =>
            {
                Assert.Equal(VulkanShaderStageKind.Vertex, vertex.Stage);
                Assert.Equal(TriangleSpirvFixtures.VertexEntryPoint, vertex.EntryPoint);
                Assert.Equal(0x07230203u, vertex.SpirvWords[0]);
            },
            fragment =>
            {
                Assert.Equal(VulkanShaderStageKind.Fragment, fragment.Stage);
                Assert.Equal(TriangleSpirvFixtures.FragmentEntryPoint, fragment.EntryPoint);
                Assert.Equal(0x07230203u, fragment.SpirvWords[0]);
            });
    }

    [Fact]
    public void VulkanOffscreenDrawRecording_RecordAndSubmitTriangleCommands_WhenVulkanAvailable_SucceedsOrCleanlySkips()
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            AurelianVulkanPlant plant = init.Plant!;
            using var allocator = new RawVulkanMemoryAllocator(plant);
            using var fences = VulkanFenceBundle.Create(plant);
            using var commandPool = VulkanCommandBufferPool.Create(plant);
            using var uploader = new VulkanBufferUploader(plant, allocator, commandPool, fences);
            using var submitter = new VulkanCommandSubmitter(plant, commandPool, fences);

            using AurelianVulkanTexture colorAttachment = CreateColorAttachment(plant, allocator);
            using AurelianVulkanRenderPass renderPass = CreateRenderPass(plant);
            using AurelianVulkanFramebuffer framebuffer = CreateFramebuffer(plant, renderPass, colorAttachment);
            using AurelianVulkanGraphicsPipeline pipeline = CreatePipeline(plant, renderPass);
            using AurelianVulkanBuffer vertexBuffer = CreateVertexBuffer(plant, allocator);

            VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(
                vertexBuffer,
                CreateTriangleVertexBytes(),
                DebugName: "a46.triangle-vertices"));
            Assert.True(upload.Success, FormatDiagnostics(upload));

            VulkanFenceOperationResult uploadWait = fences.CommandListFence.WaitForValue(upload.SignalFenceValue!.Value, FenceWaitTimeoutNanoseconds);
            Assert.True(uploadWait.Success, FormatDiagnostics(uploadWait));

            VulkanCommandBufferLease commandBuffer = commandPool.Rent(fences.CommandListFence.LastKnownCompletedValue);
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
            Assert.True(commandBuffer.IsExecutable);

            VulkanCommandSubmitResult submit = submitter.Submit(new VulkanCommandSubmitRequest(
                commandBuffer,
                WaitForCompletion: true,
                TimeoutNanoseconds: FenceWaitTimeoutNanoseconds,
                DebugName: "a47.offscreen-triangle-submit"));
            Assert.True(submit.Success, FormatDiagnostics(submit));
            Assert.NotNull(submit.SignalFenceValue);

            VulkanFenceOperationResult completed = fences.CommandListFence.QueryCompletedValue();
            Assert.True(completed.Success, FormatDiagnostics(completed));
            Assert.True(completed.Value >= submit.SignalFenceValue.Value);
        }
    }

    private static AurelianVulkanTexture CreateColorAttachment(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
    {
        VulkanTextureCreateResult result = VulkanTextureFactory.Create(
            plant,
            allocator,
            new VulkanTextureCreatePlan(
                plant.Context.Id,
                Width: 16,
                Height: 16,
                VulkanTextureFormat.Rgba8Unorm,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.TransferSource,
                VulkanMemoryUsage.GpuOnly,
                VulkanResourceLayout.Undefined,
                DebugName: "a46.offscreen-color"));

        Assert.True(result.Success, FormatDiagnostics(result));
        return result.Texture!;
    }

    private static AurelianVulkanRenderPass CreateRenderPass(AurelianVulkanPlant plant)
    {
        VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
            plant,
            new VulkanRenderPassDescriptor([
                new VulkanRenderPassAttachmentDescriptor(
                    "Color0",
                    VulkanTextureFormat.Rgba8Unorm,
                    VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined,
                    VulkanResourceLayout.ColorAttachment),
            ]));

        Assert.True(result.Success, FormatDiagnostics(result));
        return result.RenderPass!;
    }

    private static AurelianVulkanFramebuffer CreateFramebuffer(
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        AurelianVulkanTexture colorAttachment)
    {
        VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
            plant,
            renderPass,
            new VulkanFramebufferDescriptor(16, 16, [colorAttachment]));

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
                "a46.triangle-vertex-buffer"));

        Assert.True(result.Success, FormatDiagnostics(result));
        return result.Buffer!;
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

    private static void AssertSpirvMagic(byte[] bytes)
    {
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > sizeof(uint));
        Assert.Equal(0, bytes.Length % sizeof(uint));
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(bytes, 0));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FormatDiagnostics(VulkanCompiledShaderStageMappingResult result)
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
