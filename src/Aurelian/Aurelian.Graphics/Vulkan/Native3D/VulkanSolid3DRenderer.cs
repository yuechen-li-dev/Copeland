using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>
/// Bounded opaque triangle renderer with a camera uniform and D32 depth buffer.
/// Uses the shared native color target, draw encoder, allocator, and submission path.
/// </summary>
public sealed unsafe class VulkanSolid3DRenderer : IDisposable
{
    private const int VertexStride = 40;
    private const int MaximumVertices = 65_536;
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanNativeFrameTarget target;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commandPool;
    private readonly VulkanCommandSubmitter submitter;
    private readonly AurelianVulkanTexture depth;
    private readonly AurelianVulkanRenderPass renderPass;
    private readonly AurelianVulkanFramebuffer framebuffer;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly AurelianVulkanBuffer vertices;
    private readonly AurelianVulkanBuffer camera;
    private readonly DescriptorSetLayout setLayout;
    private readonly DescriptorPool descriptorPool;
    private readonly DescriptorSet descriptorSet;
    private readonly VulkanModel3DBatches? modelRenderer;
    private bool disposed;

    public VulkanSolid3DRenderer(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        VulkanNativeFrameTarget target,
        bool enableDepth = true,
        CompiledGraphicsProgram? modelProgram = null)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(target);
        target.ValidateExternalPass(plant);
        ValidateProgram(program);
        this.plant = plant;
        this.target = target;
        allocator = new RawVulkanMemoryAllocator(plant);
        fences = VulkanFenceBundle.Create(plant);
        commandPool = VulkanCommandBufferPool.Create(plant);
        submitter = new VulkanCommandSubmitter(plant, commandPool, fences);
        try
        {
            depth = VulkanNativeForwardTexturedRenderer.CreateTexture(
                plant, allocator, target.Width, target.Height,
                VulkanTextureUsage.DepthAttachment, VulkanMemoryUsage.GpuOnly, "solid3d.depth", VulkanTextureFormat.D32Float);
            var passResult = VulkanRenderPassFactory.Create(plant, new VulkanRenderPassDescriptor(
                [new VulkanRenderPassAttachmentDescriptor(
                    "solid3d.color", target.TextureFormat, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store, VulkanResourceLayout.Undefined, VulkanResourceLayout.TransferSource)],
                new VulkanRenderPassAttachmentDescriptor(
                    "solid3d.depth", VulkanTextureFormat.D32Float, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.DontCare, VulkanResourceLayout.Undefined, VulkanResourceLayout.DepthStencilAttachment)));
            Require(passResult.Success, string.Join("; ", passResult.Diagnostics.Select(item => item.Message)));
            renderPass = passResult.RenderPass!;
            var framebufferResult = VulkanFramebufferFactory.Create(plant, renderPass,
                new VulkanFramebufferDescriptor(target.Width, target.Height, [target.Texture], depth));
            Require(framebufferResult.Success, string.Join("; ", framebufferResult.Diagnostics.Select(item => item.Message)));
            framebuffer = framebufferResult.Framebuffer!;
            setLayout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
            var descriptorResult = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
                program.Shaders,
                [new VulkanVertexBufferLayoutDescriptor(0, VertexStride)],
                [
                    new VulkanVertexAttributeDescriptor(0, 0, VulkanVertexAttributeFormat.Float3, 0),
                    new VulkanVertexAttributeDescriptor(1, 0, VulkanVertexAttributeFormat.Float3, 12),
                    new VulkanVertexAttributeDescriptor(2, 0, VulkanVertexAttributeFormat.Float4, 24),
                ],
                enableDepthTest: enableDepth,
                enableDepthWrite: enableDepth);
            Require(descriptorResult.Success, string.Join("; ", descriptorResult.Diagnostics.Select(item => item.Message)));
            var pipelineResult = VulkanGraphicsPipelineFactory.Create(plant, renderPass, descriptorResult.Descriptor!, [setLayout]);
            Require(pipelineResult.Success, string.Join("; ", pipelineResult.Diagnostics.Select(item => item.Message)));
            pipeline = pipelineResult.Pipeline!;
            vertices = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
                plant, allocator, MaximumVertices * VertexStride, VulkanBufferUsage.Vertex,
                VulkanMemoryUsage.CpuToGpu, "solid3d.vertices");
            camera = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
                plant, allocator, 64, VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "solid3d.camera");
            (descriptorPool, descriptorSet) = CreateCameraDescriptor();
            if (modelProgram is not null)
                modelRenderer = new(plant, allocator, commandPool, fences, renderPass, modelProgram);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Prepare(StaticModel model)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (modelRenderer is null) throw new InvalidOperationException("StaticModel3D shader was not configured.");
        modelRenderer.Prepare(model);
    }

    public Native3DFrameResult Render(Native3DScene scene, Matrix4x4 worldToClip, Vector3 eye,
        NativeFrameClearColor clearColor, bool capture = false)
    {
        return Render(scene.Geometry, worldToClip, clearColor, capture, scene.Models, eye);
    }

    public Native3DFrameResult Render(
        ReadOnlySpan<Native3DVertex> geometry,
        Matrix4x4 worldToClip,
        NativeFrameClearColor clearColor,
        bool capture = false,
        IReadOnlyList<NativeModel3DBatch>? models = null,
        Vector3 eye = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        target.ValidateExternalPass(plant);
        models ??= [];
        if ((geometry.IsEmpty && models.Count == 0) || geometry.Length % 3 != 0 || geometry.Length > MaximumVertices)
        {
            throw new ArgumentException("Geometry must contain 1..65536 vertices in complete triangles.", nameof(geometry));
        }
        foreach (Native3DVertex vertex in geometry)
        {
            if (!Finite(vertex.Position) || !Finite(vertex.Normal) || !Finite(vertex.Color))
            {
                throw new ArgumentException("3D vertices must be finite.", nameof(geometry));
            }
        }
        float[] cameraRows =
        [
            worldToClip.M11, worldToClip.M21, worldToClip.M31, worldToClip.M41,
            worldToClip.M12, worldToClip.M22, worldToClip.M32, worldToClip.M42,
            worldToClip.M13, worldToClip.M23, worldToClip.M33, worldToClip.M43,
            worldToClip.M14, worldToClip.M24, worldToClip.M34, worldToClip.M44,
        ];
        if (cameraRows.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException("Camera transform must be finite.", nameof(worldToClip));
        }
        float[] channels = [clearColor.Red, clearColor.Green, clearColor.Blue, clearColor.Alpha];
        if (channels.Any(value => !float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(clearColor));
        }
        if (!geometry.IsEmpty) Require(vertices.Write(MemoryMarshal.AsBytes(geometry)).Success, "Vertex upload failed.");
        Require(camera.Write(MemoryMarshal.AsBytes(cameraRows.AsSpan())).Success, "Camera upload failed.");
        if (models.Count > 0)
        {
            if (modelRenderer is null) throw new InvalidOperationException("StaticModel3D shader was not configured.");
        }
        modelRenderer?.Upload(models, worldToClip, eye);
        VulkanCommandBufferLease command = commandPool.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "3D command begin failed.");
        var encoder = new VulkanRenderPassCommandEncoder();
        var begin = encoder.Begin(plant, command, new VulkanRenderPassBeginRequest(
            renderPass, framebuffer, target.PrepareClearColor(clearColor)));
        Require(begin.Success, "3D render pass begin failed.");
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            pipeline.NativePipelineLayout, 0, 1, &set, 0, null);
        if (!geometry.IsEmpty)
        {
            var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
                new VulkanDrawVerticesRequest(pipeline, vertices, (uint)geometry.Length, 0,
                    VulkanViewportScissor.FromFramebuffer(framebuffer)));
            Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
        }
        if (models.Count > 0) modelRenderer!.Draw(command, begin.Scope!.Value, framebuffer, models);
        Require(encoder.End(plant, command, begin.Scope!.Value).Success, "3D render pass end failed.");
        Require(command.End().Success, "3D command end failed.");
        var submit = submitter.Submit(new VulkanCommandSubmitRequest(command,
            WaitForCompletion: true, TimeoutNanoseconds: 5_000_000_000, DebugName: "solid3d.draw"));
        Require(submit.Success, string.Join("; ", submit.Diagnostics.Select(item => item.Message)));
        if (capture)
        {
            var readback = target.Capture();
            return new Native3DFrameResult((geometry.Length + models.Sum(item => item.Vertices.Length)) / 3, readback.Pixels, readback.Hash);
        }
        return new Native3DFrameResult((geometry.Length + models.Sum(item => item.Vertices.Length)) / 3, null, null);
    }

    private (DescriptorPool Pool, DescriptorSet Set) CreateCameraDescriptor()
    {
        DescriptorPoolSize poolSize = new(DescriptorType.UniformBuffer, 1);
        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        Require(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out DescriptorPool pool) == Result.Success,
            "Camera descriptor pool creation failed.");
        try
        {
            DescriptorSetLayout layout = setLayout;
            DescriptorSetAllocateInfo info = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            Require(plant.Vk.AllocateDescriptorSets(plant.Device, &info, out DescriptorSet set) == Result.Success,
                "Camera descriptor allocation failed.");
            DescriptorBufferInfo buffer = new(camera.NativeBuffer, 0, 64);
            WriteDescriptorSet write = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &buffer,
            };
            plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
            return (pool, set);
        }
        catch
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
            throw;
        }
    }

    private static void ValidateProgram(CompiledGraphicsProgram program)
    {
        CompiledVertexInput[] inputs = program.VertexInputs.OrderBy(input => input.Order).ToArray();
        string[] expectedFields = ["clipX", "clipY", "clipZ", "clipW"];
        bool valid = inputs.Length == 3
            && inputs[0].Location == 0 && inputs[0].PhysicalType == "float3"
            && inputs[1].Location == 1 && inputs[1].PhysicalType == "float3"
            && inputs[2].Location == 2 && inputs[2].PhysicalType == "float4"
            && program.Resources.Count == 1
            && program.Resources[0].Set == 0 && program.Resources[0].Binding == 0
            && program.Resources[0].Kind == CompiledGraphicsResourceKind.UniformBuffer
            && program.Resources[0].Visibility.SequenceEqual([CompiledGraphicsStage.Vertex])
            && program.Material is { Size: 64 } material
            && material.Fields.Count == 4
            && material.Fields.Select(field => field.Name).SequenceEqual(expectedFields)
            && material.Fields.All(field => field.PhysicalType == "float4")
            && material.Fields.Select(field => field.Offset).SequenceEqual([0, 16, 32, 48]);
        if (!valid)
        {
            throw new ArgumentException("Solid3D requires position/normal/color and the 64-byte vertex-visible camera at set 0 binding 0.", nameof(program));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        modelRenderer?.Dispose();
        pipeline?.Dispose();
        if (descriptorPool.Handle != 0)
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        }
        if (setLayout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        }
        camera?.Dispose();
        vertices?.Dispose();
        framebuffer?.Dispose();
        renderPass?.Dispose();
        depth?.Dispose();
        submitter.Dispose();
        commandPool.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
