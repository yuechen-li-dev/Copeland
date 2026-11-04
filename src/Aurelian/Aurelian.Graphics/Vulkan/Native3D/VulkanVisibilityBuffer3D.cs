using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
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
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record VisibilityBufferPrograms(CompiledGraphicsProgram Raster, CompiledGraphicsProgram Resolve)
{
    public CompiledGraphicsProgram? AnalyticRaster { get; init; }
    public CompiledGraphicsProgram? HybridResolve { get; init; }
}
public sealed record NativeVisibilityBufferReadback(uint Width, uint Height, uint[] Ids);

/// <summary>Typed identity rasterization and ID-driven material resolve for a static resident revision.</summary>
internal sealed unsafe class VulkanVisibilityBuffer3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanFrameInFlightSet frames;
    private readonly VulkanBindlessMaterials3D table;
    private readonly Stack<IDisposable> owned = new();
    private readonly VulkanClusterVisibility3D[] cullers;
    private readonly ulong[] commandOffsets;
    private readonly AurelianVulkanBuffer[] drawData = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanTexture identities;
    private readonly AurelianVulkanTexture depth;
    private readonly VulkanHzbPyramid3D? hzb;
    private readonly AurelianVulkanRenderPass rasterPass;
    private readonly AurelianVulkanFramebuffer rasterFramebuffer;
    private readonly AurelianVulkanGraphicsPipeline rasterPipeline;
    private readonly AurelianVulkanGraphicsPipeline resolvePipeline;
    public AurelianVulkanGraphicsPipeline? AnalyticRasterPipeline { get; }

    public ulong IdentityBytes => (ulong)identities.Width * identities.Height * 8;
    public ulong HzbBytes => hzb?.Bytes ?? 0;
    public ulong AtlasBytes { get; private set; }
    public ulong CommandCopyBytes => cullers.Aggregate(0UL, (sum, culler) => sum + culler.Arguments.SizeBytes);

    public VulkanVisibilityBuffer3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, VulkanFrameInFlightSet frames,
        VulkanBindlessMaterials3D table, VisibilityBufferPrograms programs, AurelianVulkanRenderPass geometryPass,
        uint width, uint height, IReadOnlyList<ImmutableArray<SceneInstance3D>> runs, Vector4[] transforms,
        IReadOnlyList<VulkanClusterVisibility3D> visibility,
        Func<AurelianVulkanRenderPass, CompiledGraphicsProgram, AurelianVulkanGraphicsPipeline> createRasterPipeline, byte[]? hzbReduce = null)
    {
        this.plant = plant;
        this.allocator = allocator;
        this.commands = commands;
        this.fences = fences;
        this.frames = frames;
        this.table = table;
        cullers = visibility.ToArray();
        commandOffsets = new ulong[runs.Count];
        ValidateTargets(programs.Raster, [VulkanTextureFormat.Rg32UInt]);
        ValidateTargets(programs.Resolve, geometryPass.Descriptor.ColorAttachments.Select(attachment => attachment.Format).ToArray());
        try
        {
            identities = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
                VulkanMemoryUsage.GpuOnly, "visibility.identities", VulkanTextureFormat.Rg32UInt));
            depth = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                VulkanTextureUsage.DepthAttachment | (hzbReduce is null ? 0 : VulkanTextureUsage.TransferSource),
                VulkanMemoryUsage.GpuOnly, "visibility.depth", VulkanTextureFormat.D32Float));
            var pass = VulkanRenderPassFactory.Create(plant, new(
                [new("visibility.ids", VulkanTextureFormat.Rg32UInt, VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined, VulkanResourceLayout.ShaderResourceFragment)],
                new("visibility.depth", VulkanTextureFormat.D32Float, VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined, VulkanResourceLayout.DepthStencilAttachment)));
            Require(pass.Success, "Identity render pass rejected.");
            rasterPass = Own(pass.RenderPass!);
            var framebuffer = VulkanFramebufferFactory.Create(plant, rasterPass, new(width, height, [identities], depth));
            Require(framebuffer.Success, "Identity framebuffer rejected.");
            rasterFramebuffer = Own(framebuffer.Framebuffer!);
            rasterPipeline = Own(createRasterPipeline(rasterPass, programs.Raster));
            if (programs.AnalyticRaster is { } analytic)
            {
                ValidateTargets(analytic, [VulkanTextureFormat.Rg32UInt]);
                AnalyticRasterPipeline = Own(createRasterPipeline(rasterPass, analytic));
            }
            var descriptor = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(programs.Resolve.Shaders,
                [new(0, 8)], [new(0, 0, VulkanVertexAttributeFormat.Float2, 0)],
                enableDepthTest: true, enableDepthWrite: true);
            Require(descriptor.Success, "Resolve pipeline declaration rejected.");
            var pipeline = VulkanGraphicsPipelineFactory.Create(plant, geometryPass, descriptor.Descriptor!, [table.Layout]);
            Require(pipeline.Success, string.Join("; ", pipeline.Diagnostics.Select(diagnostic => diagnostic.Message)));
            resolvePipeline = Own(pipeline.Pipeline!);
            BuildTables(runs, transforms);
            table.BindVisibilityImage(identities);
            if (hzbReduce is not null)
            {
                hzb = Own(new VulkanHzbPyramid3D(plant, allocator, commands, fences, frames, hzbReduce, width, height));
                foreach (var culler in cullers) culler.ConfigureHzb(hzb);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static void ValidateTargets(CompiledGraphicsProgram program, IReadOnlyList<VulkanTextureFormat> formats)
    {
        if (!program.PixelTargets.Select(target => target.Target).Order().SequenceEqual(Enumerable.Range(0, formats.Count)))
            throw new ArgumentException("AUR-VBUFFER-002: Target locations disagree with the render pass.");
        foreach (var target in program.PixelTargets)
        {
            if (target.Target < 0 || target.Target >= formats.Count) throw new ArgumentException("AUR-VBUFFER-002: Invalid target location.");
            string type = formats[target.Target] switch
            {
                VulkanTextureFormat.Rg32UInt => "uint2",
                VulkanTextureFormat.R32UInt => "u32",
                _ => "float4",
            };
            if (target.PhysicalType != type) throw new ArgumentException("AUR-VBUFFER-002: Shader target scalar type disagrees with its attachment.");
        }
    }

    internal static void ValidateStorageLayouts(CompiledGraphicsProgram program)
    {
        var vertexFields = new List<CompiledStorageField>();
        AddVector(vertexFields, "position", 3);
        AddVector(vertexFields, "normal", 3);
        AddVector(vertexFields, "color", 4);
        AddVector(vertexFields, "uv", 2);
        AddVector(vertexFields, "tangent", 4);
        var instanceFields = new List<CompiledStorageField>();
        foreach (string name in new[] { "worldX", "worldY", "worldZ", "previousX", "previousY", "previousZ", "normalX", "normalY", "normalZ" })
            AddVector(instanceFields, name, 4);
        foreach (string name in new[] { "vertexBase", "indexBase", "commandBase", "primitiveKind" })
            instanceFields.Add(new(name, "u32", instanceFields.Count * 4));
        var resources = program.Resources.ToDictionary(resource => resource.Binding);
        if (!resources[5].StorageLayout!.Fields.SequenceEqual(vertexFields)
            || !resources[7].StorageLayout!.Fields.SequenceEqual(instanceFields)
            || !resources[6].StorageLayout!.Fields.SequenceEqual([new CompiledStorageField("", "u32", 0)])
            || !resources[8].StorageLayout!.Fields.SequenceEqual([new CompiledStorageField("", "u32", 0)])
            || resources.Where(pair => pair.Key is not (3 or 4)).Any(pair => pair.Value.DescriptorCount.HasValue))
            throw new ArgumentException("AUR-VBUFFER-001: Visibility transport field order or scalar encoding disagrees with the native packer.");

        static void AddVector(List<CompiledStorageField> fields, string name, int width)
        {
            foreach (char lane in "xyzw"[..width]) fields.Add(new(name + "." + lane, "f32", fields.Count * 4));
        }
    }

    private void BuildTables(IReadOnlyList<ImmutableArray<SceneInstance3D>> runs, Vector4[] transforms)
    {
        var vertices = new List<byte>();
        var indices = new List<uint>();
        var meshes = new Dictionary<NativeMesh3D, (uint Vertex, uint Index)>();
        foreach (var mesh in runs.SelectMany(run => run).Select(instance => instance.Mesh).Distinct())
        {
            meshes.Add(mesh, ((uint)vertices.Count / 64, (uint)indices.Count));
            vertices.AddRange(MemoryMarshal.AsBytes(mesh.Vertices.AsSpan()).ToArray());
            indices.AddRange(mesh.PackedIndices);
            if ((ulong)vertices.Count + (ulong)indices.Count * 4 > 256UL * 1024 * 1024)
                throw new InvalidDataException("AUR-VBUFFER-003: Visibility transport atlas exceeds 256 MiB.");
        }
        AtlasBytes = (ulong)vertices.Count + (ulong)indices.Count * 4;
        byte[] instanceData = new byte[runs.Sum(run => run.Length) * 160];
        int instanceIndex = 0;
        ulong commandBytes = 0;
        for (int runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            commandOffsets[runIndex] = commandBytes;
            foreach (var instance in runs[runIndex])
            {
                Span<byte> record = instanceData.AsSpan(instanceIndex * 160, 160);
                MemoryMarshal.AsBytes(transforms.AsSpan(instanceIndex * 9, 9)).CopyTo(record);
                Span<uint> metadata = MemoryMarshal.Cast<byte, uint>(record[144..]);
                metadata[0] = meshes[instance.Mesh].Vertex;
                metadata[1] = meshes[instance.Mesh].Index;
                metadata[2] = checked((uint)(commandBytes / 4));
                metadata[3] = (uint)instance.Mesh.PrimitiveKind;
                instanceIndex++;
            }
            commandBytes += cullers.Length == 0 ? 20UL : cullers[runIndex].Arguments.SizeBytes;
        }
        var vertexBuffer = Upload(vertices.ToArray());
        var indexBuffer = Upload(MemoryMarshal.AsBytes(indices.ToArray().AsSpan()).ToArray());
        var instanceBuffer = Upload(instanceData);
        for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
        {
            drawData[slot] = Upload(new byte[checked((int)commandBytes)]);
            table.BindVisibilityStorage(5, vertexBuffer, slot);
            table.BindVisibilityStorage(6, indexBuffer, slot);
            table.BindVisibilityStorage(7, instanceBuffer, slot);
            table.BindVisibilityStorage(8, drawData[slot], slot);
        }
    }

    private AurelianVulkanBuffer Upload(byte[] bytes)
    {
        var created = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, (ulong)bytes.Length,
            VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly, "visibility.transport"));
        Require(created.Success, "Transport allocation failed.");
        var buffer = Own(created.Buffer!);
        using var uploader = new VulkanBufferUploader(plant, allocator, commands, fences);
        Require(uploader.Upload(new(buffer, bytes, DebugName: "visibility.transport")).Success, "Transport upload failed.");
        return buffer;
    }

    public void RecordRaster(VulkanCommandBufferLease command,
        Action<VulkanRenderPassScope, AurelianVulkanFramebuffer, AurelianVulkanGraphicsPipeline> draw)
    {
        if (cullers.Length > 0)
        {
            Barrier(command, AccessFlags.ShaderReadBit, AccessFlags.TransferWriteBit,
                PipelineStageFlags.VertexShaderBit, PipelineStageFlags.TransferBit);
            Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
            for (int index = 0; index < cullers.Length; index++)
            {
                var source = cullers[index].Arguments;
                BufferCopy copy = new(0, commandOffsets[index], source.SizeBytes);
                plant.Vk.CmdCopyBuffer(command.CommandBuffer, source.NativeBuffer, drawData[frames.Slot].NativeBuffer, 1, &copy);
            }
            Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.TransferBit, PipelineStageFlags.VertexShaderBit);
        }
        var encoder = new VulkanRenderPassCommandEncoder();
        var begin = encoder.Begin(plant, command, new(rasterPass, rasterFramebuffer, VulkanColorClearValue.UInt(0, 0)));
        Require(begin.Success, string.Join("; ", begin.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Bind(command, rasterPipeline);
        draw(begin.Scope!.Value, rasterFramebuffer, rasterPipeline);
        Require(encoder.End(plant, command, begin.Scope.Value).Success, "Identity raster end failed.");
    }

    public void UpdateHzbFrame(Matrix4x4 clip) => hzb?.UpdateView(clip);
    public void RecordHzb(VulkanCommandBufferLease command, bool history) => hzb!.Record(command, depth, history);
    public HzbPyramidReadback ReadHzbForQualification() => hzb!.ReadForQualification();

    public void DrawResolve(VulkanCommandBufferLease command, VulkanRenderPassScope scope,
        AurelianVulkanFramebuffer framebuffer, AurelianVulkanBuffer triangle)
    {
        Bind(command, resolvePipeline);
        var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, scope,
            new(resolvePipeline, triangle, 3, 0, VulkanViewportScissor.FromFramebuffer(framebuffer)));
        Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    private void Bind(VulkanCommandBufferLease command, AurelianVulkanGraphicsPipeline pipeline)
    {
        DescriptorSet set = table.Set;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            pipeline.NativePipelineLayout, 0, 1, &set, 0, null);
    }

    public NativeVisibilityBufferReadback ReadForQualification()
    {
        frames.Drain();
        var created = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, IdentityBytes,
            VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "visibility.readback", MapOnCreate: true));
        Require(created.Success, "Identity readback allocation failed.");
        using var staging = created.Buffer!;
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Identity readback begin failed.");
        Transition(command, ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferSrcOptimal,
            AccessFlags.ShaderReadBit, AccessFlags.TransferReadBit, PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.TransferBit);
        BufferImageCopy copy = new() { ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new(identities.Width, identities.Height, 1) };
        plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, identities.NativeImage, ImageLayout.TransferSrcOptimal,
            staging.NativeBuffer, 1, &copy);
        Transition(command, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal,
            AccessFlags.TransferReadBit, AccessFlags.ShaderReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit);
        Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.HostReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit);
        Require(command.End().Success, "Identity readback end failed.");
        using var submitter = new VulkanCommandSubmitter(plant, commands, fences);
        Require(submitter.Submit(new(command, DebugName: "visibility.identity-readback")).Success, "Identity readback submit failed.");
        return new(identities.Width, identities.Height, MemoryMarshal.Cast<byte, uint>(staging.ReadBytes((int)IdentityBytes)).ToArray());
    }

    private void Transition(VulkanCommandBufferLease command, ImageLayout before, ImageLayout after,
        AccessFlags source, AccessFlags destination, PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        ImageMemoryBarrier barrier = new() { SType = StructureType.ImageMemoryBarrier, SrcAccessMask = source,
            DstAccessMask = destination, OldLayout = before, NewLayout = after, SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored, Image = identities.NativeImage,
            SubresourceRange = new(ImageAspectFlags.ColorBit, 0, 1, 0, 1) };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private void Barrier(VulkanCommandBufferLease command, AccessFlags source, AccessFlags destination,
        PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = source, DstAccessMask = destination };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    public void Dispose()
    {
        while (owned.TryPop(out var resource)) resource.Dispose();
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException("AUR-VBUFFER-004: " + message);
    }
}
