using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Explicit authoring operation. GPU integration and one artifact readback; never called during drawing.</summary>
public static unsafe class VulkanEnvironmentCompiler
{
    public static EnvironmentLighting Compile(AurelianVulkanPlant plant, CompiledGraphicsProgram program,
        int width, int height, ReadOnlySpan<float> linearRgba)
    {
        if (width < 1 || height < 1 || width > 4096 || height > 2048 || linearRgba.Length != width * height * 4)
        {
            throw new ArgumentException("Environment source must be bounded linear RGBA equirectangular data.");
        }
        foreach (float value in linearRgba)
        {
            if (!float.IsFinite(value) || value < 0 || value > 60000)
            {
                throw new ArgumentException("Environment source radiance must be finite and within the HDR attachment range.");
            }
        }
        string sourceKey = width + "x" + height + ":" + Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(linearRgba)));
        using var allocator = new RawVulkanMemoryAllocator(plant);
        using var fences = VulkanFenceBundle.Create(plant);
        using var commands = VulkanCommandBufferPool.Create(plant);
        using var uploader = new VulkanTextureUploader(plant, allocator, commands, fences);
        using var source = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, (uint)width, (uint)height,
            VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
            "environment.source", VulkanTextureFormat.Rgba32Float);
        var upload = uploader.Upload(new(source, MemoryMarshal.AsBytes(linearRgba).ToArray(), "environment.source"));
        Require(upload.Success, "Environment source upload failed.");
        using var atlas = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator,
            EnvironmentLighting.Size, EnvironmentLighting.Size * EnvironmentLighting.Bands,
            VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
            VulkanMemoryUsage.GpuOnly, "environment.compiled", VulkanTextureFormat.Rgba32Float);
        using var pass = new VulkanPostProcess3D(plant, allocator, program, atlas, 1);
        using var triangle = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 24,
            VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "environment.triangle");
        float[] points = [-1, -1, 3, -1, -1, 3];
        Require(triangle.Write(MemoryMarshal.AsBytes(points.AsSpan())).Success, "Environment triangle upload failed.");
        int byteCount = EnvironmentLighting.Size * EnvironmentLighting.Size * EnvironmentLighting.Bands * 16;
        using var readback = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, (ulong)byteCount,
            VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "environment.artifact-readback");
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Environment command begin failed.");
        pass.Configure([0, 0, 0, 0], source);
        pass.Record(command, triangle);
        var transition = atlas.LayoutTracker.Transition("environment.capture", 0, 0, VulkanResourceLayout.TransferSource);
        Require(transition.Success, "Environment capture transition failed.");
        if (transition.Plan is not null)
        {
            Require(VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(atlas, transition.Plan)]).Success,
                "Environment capture barrier failed.");
        }
        BufferImageCopy region = new()
        {
            ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new(atlas.Width, atlas.Height, 1),
        };
        plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, atlas.NativeImage, ImageLayout.TransferSrcOptimal,
            readback.NativeBuffer, 1, &region);
        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = readback.NativeBuffer,
            Size = readback.SizeBytes,
        };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit,
            0, 0, null, 1, &barrier, 0, null);
        Require(command.End().Success, "Environment command end failed.");
        using var submitter = new VulkanCommandSubmitter(plant, commands, fences);
        Require(submitter.Submit(new(command, WaitForCompletion: true)).Success, "Environment compilation submission failed.");
        return new EnvironmentLighting(MemoryMarshal.Cast<byte, float>(readback.ReadBytes(byteCount)).ToArray(), sourceKey);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
