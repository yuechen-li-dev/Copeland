using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Explicit synchronous qualification only. The ordinary frame never calls these readbacks.</summary>
internal static unsafe class Vulkan3DQualificationReadback
{
    internal static float[] ReadBuffer(AurelianVulkanPlant plant, VulkanFrameInFlightSet frames, AurelianVulkanBuffer source)
    {
        byte[] bytes = Read(plant, frames, source.SizeBytes, (command, destination) =>
        {
            BufferCopy copy = new(0, 0, source.SizeBytes);
            plant.Vk.CmdCopyBuffer(command.CommandBuffer, source.NativeBuffer, destination.NativeBuffer, 1, &copy);
        });
        return MemoryMarshal.Cast<byte, float>(bytes).ToArray();
    }

    internal static ReferenceImage3D ReadTexture(AurelianVulkanPlant plant, VulkanFrameInFlightSet frames, AurelianVulkanTexture texture)
    {
        if (texture.Format != VulkanTextureFormat.Rgba16Float) throw new NotSupportedException("Qualification expects RGBA16F lighting.");
        byte[] bytes = Read(plant, frames, (ulong)texture.Width * texture.Height * 8, (command, destination) =>
        {
            Transition(VulkanResourceLayout.TransferSource);
            BufferImageCopy copy = new() { ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new(texture.Width, texture.Height, 1) };
            plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, texture.NativeImage, ImageLayout.TransferSrcOptimal,
                destination.NativeBuffer, 1, &copy);
            Transition(VulkanResourceLayout.ShaderResourceFragment);
            void Transition(VulkanResourceLayout layout)
            {
                var result = texture.LayoutTracker.Transition("qualification-lighting", 0, 0, layout);
                if (!result.Success || result.Plan is not null && !VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(texture, result.Plan)]).Success)
                    throw new InvalidOperationException("Qualification lighting transition failed.");
            }
        });
        var half = MemoryMarshal.Cast<byte, ushort>(bytes);
        float[] rgb = new float[texture.Width * texture.Height * 3];
        for (int pixel = 0; pixel < rgb.Length / 3; pixel++)
            for (int channel = 0; channel < 3; channel++)
                rgb[pixel * 3 + channel] = (float)BitConverter.UInt16BitsToHalf(half[pixel * 4 + channel]);
        return new((int)texture.Width, (int)texture.Height, 1, rgb);
    }

    private static byte[] Read(AurelianVulkanPlant plant, VulkanFrameInFlightSet frames, ulong size,
        Action<VulkanCommandBufferLease, AurelianVulkanBuffer> record)
    {
        frames.Drain();
        using var allocator = new RawVulkanMemoryAllocator(plant);
        using var commands = VulkanCommandBufferPool.Create(plant);
        using var fences = VulkanFenceBundle.Create(plant);
        using var submitter = new VulkanCommandSubmitter(plant, commands, fences);
        using var destination = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, size,
            VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "lighting-qualification");
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        if (!command.Begin().Success) throw new InvalidOperationException("Qualification readback begin failed.");
        Barrier(AccessFlags.MemoryWriteBit, AccessFlags.TransferReadBit);
        record(command, destination);
        Barrier(AccessFlags.TransferWriteBit | AccessFlags.TransferReadBit,
            AccessFlags.HostReadBit | AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
        if (!command.End().Success || !submitter.Submit(new(command, DebugName: "lighting-qualification")).Success)
            throw new InvalidOperationException("Qualification readback submission failed.");
        return destination.ReadBytes(checked((int)size));

        void Barrier(AccessFlags before, AccessFlags after)
        {
            MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = before, DstAccessMask = after };
            plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit | PipelineStageFlags.HostBit,
                0, 1, &barrier, 0, null, 0, null);
        }
    }
}
