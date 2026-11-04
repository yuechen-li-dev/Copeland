using System.Runtime.InteropServices;
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
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Offline comparison output through the ordinary ToneMap3D program.
/// Uses fixed exposure, filmic mapping and sRGB encoding, with zero bloom.</summary>
public static unsafe class VulkanReferenceOutput3D
{
    public static byte[] ToneMap(AurelianVulkanPlant plant, CompiledGraphicsProgram program, ReferenceImage3D image, float exposure = 1)
    {
        long pixels = (long)image.Width * image.Height;
        if (image.Width < 1 || image.Height < 1 || pixels > 1_048_576 || image.LinearRgb.Length != pixels * 3
            || image.LinearRgb.Any(value => !float.IsFinite(value) || value < 0)
            || !float.IsFinite(exposure) || exposure is < .001f or > 1000)
            throw new ArgumentException("AUR-REF-013: Invalid reference output image or exposure.");
        using var allocator = new RawVulkanMemoryAllocator(plant);
        using var fences = VulkanFenceBundle.Create(plant);
        using var commands = VulkanCommandBufferPool.Create(plant);
        using var uploader = new VulkanTextureUploader(plant, allocator, commands, fences);
        using var source = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, (uint)image.Width, (uint)image.Height,
            VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
            "reference.hdr", VulkanTextureFormat.Rgba32Float);
        float[] rgba = new float[pixels * 4];
        for (int pixel = 0; pixel < pixels; pixel++)
        {
            rgba[pixel * 4] = image.LinearRgb[pixel * 3];
            rgba[pixel * 4 + 1] = image.LinearRgb[pixel * 3 + 1];
            rgba[pixel * 4 + 2] = image.LinearRgb[pixel * 3 + 2];
            rgba[pixel * 4 + 3] = 1;
        }
        Require(uploader.Upload(new(source, MemoryMarshal.AsBytes(rgba.AsSpan()).ToArray(), "reference.hdr")).Success, "HDR upload failed.");
        using var bloom = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, 1, 1,
            VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
            "reference.zero-bloom", VulkanTextureFormat.Rgba32Float);
        Require(uploader.Upload(new(bloom, new byte[16], "reference.zero-bloom")).Success, "Zero bloom upload failed.");
        using var output = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, (uint)image.Width, (uint)image.Height,
            VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
            VulkanMemoryUsage.GpuOnly, "reference.display", VulkanTextureFormat.Rgba8Unorm);
        using var pass = new VulkanPostProcess3D(plant, allocator, program, output, 2);
        using var triangle = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 24,
            VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "reference.output-triangle");
        float[] points = [-1, -1, 3, -1, -1, 3];
        Require(triangle.Write(MemoryMarshal.AsBytes(points.AsSpan())).Success, "Output triangle upload failed.");
        using var readback = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, (ulong)pixels * 4,
            VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "reference.display-readback");
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Reference output command begin failed.");
        pass.Configure([exposure, 1, 1, 0], source, bloom);
        pass.Record(command, triangle);
        var transition = output.LayoutTracker.Transition("reference.display", 0, 0, VulkanResourceLayout.TransferSource);
        Require(transition.Success, "Reference display transition failed.");
        if (transition.Plan is not null)
            Require(VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(output, transition.Plan)]).Success, "Output barrier failed.");
        BufferImageCopy region = new()
        {
            ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new((uint)image.Width, (uint)image.Height, 1),
        };
        plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, output.NativeImage, ImageLayout.TransferSrcOptimal, readback.NativeBuffer, 1, &region);
        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier, SrcAccessMask = AccessFlags.TransferWriteBit, DstAccessMask = AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = readback.NativeBuffer, Size = readback.SizeBytes,
        };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit,
            0, 0, null, 1, &barrier, 0, null);
        Require(command.End().Success, "Reference output command end failed.");
        using var submitter = new VulkanCommandSubmitter(plant, commands, fences);
        Require(submitter.Submit(new(command, WaitForCompletion: true)).Success, "Reference display submission failed.");
        return readback.ReadBytes((int)pixels * 4);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
