using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>
/// One immutable, bounded GPU compilation batch. Submission does not wait or read back.
/// The caller polls completion and retains this owner for as long as its output is sampled.
/// All operations run on the plant's owning submission thread; this is not a second queue.
/// </summary>
public sealed unsafe class VulkanLightingBake : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly Stack<IDisposable> owned = new();
    private readonly AurelianVulkanTexture texture;
    private readonly AurelianVulkanBuffer uniform;
    private readonly AurelianVulkanBuffer triangle;
    private readonly AurelianVulkanRenderPass pass;
    private readonly AurelianVulkanFramebuffer framebuffer;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly DescriptorSetLayout layout;
    private readonly DescriptorPool descriptorPool;
    private readonly DescriptorSet descriptorSet;
    private readonly Vulkan3DGpuTimings timings;
    private LightingBakeTicket? ticket;
    private bool disposed;
    private int viewCount;
    private readonly string completionDomain = Guid.NewGuid().ToString("N");
    private bool qualified;

    public VulkanLightingBake(AurelianVulkanPlant plant, LightingCompilation compilation,
        CompiledGraphicsProgram program, ReadOnlySpan<float> parameters)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(compilation);
        Validate(program, parameters);
        if (compilation.Inputs.ShaderSources != LightingProgramIdentity.Compute(program)
            || compilation.Inputs.ReceiverDomain != ParameterIdentity(parameters))
        {
            throw new ArgumentException("Compilation identities must match the actual typed shader and all baked uniform values.");
        }
        this.plant = plant;
        Compilation = compilation;
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        try
        {
            texture = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator,
                (uint)compilation.Width, (uint)compilation.Height,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
                VulkanMemoryUsage.GpuOnly, "lighting.compilation", VulkanTextureFormat.Rgba16Float));
            var createdPass = VulkanRenderPassFactory.Create(plant, new(
                [new("lighting.output", texture.Format, VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined, VulkanResourceLayout.ColorAttachment)]));
            Require(createdPass.Success, "Lighting compilation pass creation failed.");
            pass = Own(createdPass.RenderPass!);
            var createdFramebuffer = VulkanFramebufferFactory.Create(plant, pass,
                new((uint)compilation.Width, (uint)compilation.Height, [texture]));
            Require(createdFramebuffer.Success, "Lighting compilation framebuffer creation failed.");
            framebuffer = Own(createdFramebuffer.Framebuffer!);
            layout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
            var descriptor = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(program.Shaders,
                [new(0, 8)], [new(0, 0, VulkanVertexAttributeFormat.Float2, 0)], enableDepthTest: false, enableDepthWrite: false);
            Require(descriptor.Success, "Lighting compilation pipeline descriptor rejected.");
            var createdPipeline = VulkanGraphicsPipelineFactory.Create(plant, pass, descriptor.Descriptor!, [layout]);
            Require(createdPipeline.Success, "Lighting compilation pipeline creation failed.");
            pipeline = Own(createdPipeline.Pipeline!);
            uniform = Own(VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 96,
                VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "lighting.inputs"));
            Require(uniform.Write(MemoryMarshal.AsBytes(parameters)).Success, "Lighting input upload failed.");
            triangle = Own(VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 24,
                VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "lighting.triangle"));
            float[] vertices = [-1, -1, 3, -1, -1, 3];
            Require(triangle.Write(MemoryMarshal.AsBytes(vertices.AsSpan())).Success, "Lighting triangle upload failed.");
            (descriptorPool, descriptorSet) = Vulkan3DPass.AllocateSet(plant, layout, uniform, 0);
            timings = Own(new Vulkan3DGpuTimings(plant, ["lighting-compilation"]));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public LightingCompilation Compilation { get; }
    public int SubmissionCount { get; private set; }
    public bool IsQualified => !disposed && qualified;

    public static string ParameterIdentity(ReadOnlySpan<float> values)
        => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(values))).ToLowerInvariant();

    public LightingBakeTicket Submit(long generation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ticket is not null || generation <= 0)
        {
            throw new InvalidOperationException("Each lighting batch is immutable and may be submitted once with a positive generation.");
        }
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Lighting command begin failed.");
        timings.Reset(command);
        timings.Mark(command, 0);
        var encoder = new VulkanRenderPassCommandEncoder();
        var begin = encoder.Begin(plant, command, new(pass, framebuffer, new(0, 0, 0, 0)));
        Require(begin.Success, "Lighting pass begin failed.");
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            pipeline.NativePipelineLayout, 0, 1, &set, 0, null);
        var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
            new(pipeline, triangle, 3, 0, VulkanViewportScissor.FromFramebuffer(framebuffer)));
        Require(draw.Success, "Lighting compilation draw failed.");
        Require(encoder.End(plant, command, begin.Scope.Value).Success, "Lighting pass end failed.");
        Vulkan3DPass.SampleAfterRendering(plant, command, texture);
        timings.Mark(command, 1);
        Require(command.End().Success, "Lighting command end failed.");
        var submitted = submitter.Submit(new(command, WaitForCompletion: false, DebugName: "lighting.compile"));
        Require(submitted.Success, string.Join("; ", submitted.Diagnostics.Select(item => item.Message)));
        ticket = new(plant.Context.Id.Value, submitted.SignalFenceValue!.Value, Compilation.ContentKey, generation, completionDomain);
        SubmissionCount++;
        return ticket;
    }

    public bool IsComplete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ticket is null)
        {
            return false;
        }
        var query = fences.CommandListFence.QueryCompletedValue();
        Require(query.Success, "Lighting completion query failed.");
        return query.Value >= ticket.CompletionValue;
    }

    public double GpuMilliseconds()
    {
        Require(IsComplete(), "Lighting output is not complete.");
        return timings.Read().FirstOrDefault()?.Milliseconds ?? 0;
    }

    /// <summary>Diagnostic readback after completion. Alpha is the shader's explicit resolution mask.</summary>
    public float[] ReadLinear()
    {
        Require(IsComplete(), "Cannot read unfinished lighting output.");
        int bytes = checked(Compilation.Width * Compilation.Height * 8);
        using var readback = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, (ulong)bytes,
            VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "lighting.readback");
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Lighting readback begin failed.");
        Transition(command, VulkanResourceLayout.TransferSource);
        BufferImageCopy region = new()
        {
            ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new((uint)Compilation.Width, (uint)Compilation.Height, 1),
        };
        plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, texture.NativeImage, ImageLayout.TransferSrcOptimal,
            readback.NativeBuffer, 1, &region);
        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier, SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit, SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored, Buffer = readback.NativeBuffer, Size = (ulong)bytes,
        };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit,
            0, 0, null, 1, &barrier, 0, null);
        Transition(command, VulkanResourceLayout.ShaderResourceFragment);
        Require(command.End().Success, "Lighting readback end failed.");
        Require(submitter.Submit(new(command, DebugName: "lighting.readback")).Success, "Lighting readback submission failed.");
        float[] values = MemoryMarshal.Cast<byte, Half>(readback.ReadBytes(bytes)).ToArray().Select(value => (float)value).ToArray();
        qualified = values.All(value => float.IsFinite(value) && value >= 0)
            && Enumerable.Range(0, values.Length / 4).All(index => values[index * 4 + 3] is >= .999f and <= 1.001f);
        return values;
    }

    public VulkanLightingBakeView CreateView(VulkanNativeFrameTarget target, CompiledGraphicsProgram program)
    {
        Require(IsComplete(), "Cannot sample unfinished lighting output.");
        Require(qualified, "Diagnostic resolution-mask validation must qualify this batch before sampling.");
        var view = new VulkanLightingBakeView(plant, allocator, this, texture, target, program);
        viewCount++;
        view.AttachOwner();
        return view;
    }

    internal void ReleaseView() => viewCount--;

    private void Transition(VulkanCommandBufferLease command, VulkanResourceLayout desired)
    {
        var transition = texture.LayoutTracker.Transition("lighting.readback", 0, 0, desired);
        Require(transition.Success, "Lighting texture transition failed.");
        if (transition.Plan is not null)
        {
            var emitted = VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(texture, transition.Plan)]);
            Require(emitted.Success, "Lighting texture barrier failed.");
        }
    }

    private static void Validate(CompiledGraphicsProgram program, ReadOnlySpan<float> values)
    {
        string[] fields = ["origin", "axisU", "axisV", "parameters", "light", "eye"];
        bool valid = program.Material is { Size: 96, Set: 0, Binding: 0 } material
            && material.Fields.Select(item => item.Name).SequenceEqual(fields)
            && material.Fields.All(item => item.PhysicalType == "float4")
            && material.Fields.Select(item => item.Offset).SequenceEqual(Enumerable.Range(0, 6).Select(index => index * 16))
            && program.VertexInputs.Count == 1 && program.VertexInputs[0].Location == 0 && program.VertexInputs[0].PhysicalType == "float2"
            && program.Resources.Count == 1 && program.Resources[0].Kind == CompiledGraphicsResourceKind.UniformBuffer
            && program.Resources[0].Set == 0 && program.Resources[0].Binding == 0
            && program.Resources[0].Visibility.SequenceEqual([CompiledGraphicsStage.Fragment]);
        if (!valid || values.Length != 24 || values.ToArray().Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException("Lighting bake requires float2 positions and the finite 96-byte compilation input contract.");
        }
    }

    private T Own<T>(T value) where T : IDisposable
    {
        owned.Push(value);
        return value;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (viewCount != 0)
        {
            throw new InvalidOperationException("Dispose lighting views before retiring their bake owner.");
        }
        if (ticket is not null)
        {
            Require(fences.CommandListFence.WaitForValue(ticket.CompletionValue, 5_000_000_000).Success,
                "Lighting resource retirement could not establish completion.");
        }
        disposed = true;
        if (descriptorPool.Handle != 0)
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        }
        while (owned.TryPop(out IDisposable? value))
        {
            value.Dispose();
        }
        if (layout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, layout, null);
        }
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

/// <summary>Retained texture lookup. Dispose the view before its bake owner; no field tracing occurs here.</summary>
public sealed unsafe class VulkanLightingBakeView : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanLightingBake owner;
    private readonly VulkanNativeFrameTarget target;
    private readonly Vulkan3DPass pass;
    private readonly AurelianVulkanBuffer triangle;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly Vulkan3DGpuTimings timings;
    private bool disposed;
    private bool ownerAttached;
    private ulong? lastSubmission;

    internal VulkanLightingBakeView(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanLightingBake owner, AurelianVulkanTexture texture, VulkanNativeFrameTarget target, CompiledGraphicsProgram program)
    {
        target.ValidateExternalPass(plant);
        this.plant = plant;
        this.owner = owner;
        this.target = target;
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        try
        {
            pass = new(plant, allocator, program, target.Texture, target.Width, target.Height, false, texture);
            triangle = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 24,
                VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "lighting.lookup.triangle");
            float[] vertices = [-1, -1, 3, -1, -1, 3];
            if (!triangle.Write(MemoryMarshal.AsBytes(vertices.AsSpan())).Success)
            {
                throw new InvalidOperationException("Lookup vertex upload failed.");
            }
            timings = new(plant, ["lighting-lookup"]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void AttachOwner() => ownerAttached = true;

    public Native3DFrameResult Render(ReadOnlySpan<float> parameters, bool capture = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!owner.IsComplete() || parameters.Length != 4 || parameters.ToArray().Any(value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException("Lookup requires a completed owner and four finite parameters.");
        }
        pass.Upload(parameters);
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        if (!command.Begin().Success)
        {
            throw new InvalidOperationException("Lookup begin failed.");
        }
        timings.Reset(command);
        timings.Mark(command, 0);
        var encoder = new VulkanRenderPassCommandEncoder();
        var begin = encoder.Begin(plant, command, new(pass.Pass, pass.Framebuffer, new(0, 0, 0, 1)));
        if (!begin.Success)
        {
            throw new InvalidOperationException("Lookup pass begin failed.");
        }
        pass.Draw(command, begin.Scope!.Value, triangle, 3);
        if (!encoder.End(plant, command, begin.Scope.Value).Success)
        {
            throw new InvalidOperationException("Lookup pass end failed.");
        }
        timings.Mark(command, 1);
        if (!command.End().Success)
        {
            throw new InvalidOperationException("Lookup command end failed.");
        }
        var submitted = submitter.Submit(new(command, DebugName: "lighting.lookup"));
        lastSubmission = submitted.SignalFenceValue;
        if (!submitted.Success)
        {
            throw new InvalidOperationException("Lookup submission failed.");
        }
        var times = timings.Read();
        if (capture)
        {
            var pixels = target.Capture();
            return new(1, pixels.Pixels, pixels.Hash) { GpuPassTimes = times };
        }
        return new(1, null, null) { GpuPassTimes = times };
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (lastSubmission is { } signal && !fences.CommandListFence.WaitForValue(signal, 5_000_000_000).Success)
        {
            throw new InvalidOperationException("Lookup retirement could not establish completion.");
        }
        disposed = true;
        timings?.Dispose();
        pass?.Dispose();
        triangle?.Dispose();
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        if (ownerAttached)
        {
            owner.ReleaseView();
        }
    }
}
