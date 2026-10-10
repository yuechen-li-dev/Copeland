using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

/// <summary>Generate a retained mip chain on the GPU after uploading level zero.</summary>
public static unsafe class VulkanTextureMipGenerator
{
    public static void Generate(AurelianVulkanPlant plant, VulkanCommandBufferPool commands,
        VulkanFenceBundle fences, AurelianVulkanTexture texture)
    {
        if (texture.MipLevels == 1) return;
        if (texture.PlantId != plant.Context.Id || texture.IsDisposed
            || (texture.Usage & (VulkanTextureUsage.TransferSource | VulkanTextureUsage.TransferDestination))
                != (VulkanTextureUsage.TransferSource | VulkanTextureUsage.TransferDestination))
            throw new ArgumentException("GPU mip generation requires a live source/destination texture on this plant.");
        Format format = texture.Format switch
        {
            VulkanTextureFormat.Rgba8Srgb => Format.R8G8B8A8Srgb,
            VulkanTextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
            _ => throw new NotSupportedException("GPU mip generation currently supports RGBA8 material textures."),
        };
        plant.Vk.GetPhysicalDeviceFormatProperties(plant.PhysicalDevice, format, out var properties);
        var required = FormatFeatureFlags.BlitSrcBit | FormatFeatureFlags.BlitDstBit | FormatFeatureFlags.SampledImageFilterLinearBit;
        if ((properties.OptimalTilingFeatures & required) != required)
            throw new NotSupportedException($"{format} lacks linear GPU mip generation on {plant.Facts.PhysicalDeviceName}.");
        VulkanCommandBufferLease command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Mip command begin failed.");
        for (uint level = 1; level < texture.MipLevels; level++)
        {
            Transition(level - 1, VulkanResourceLayout.TransferSource);
            Transition(level, VulkanResourceLayout.TransferDestination);
            ImageBlit blit = new()
            {
                SrcSubresource = new(ImageAspectFlags.ColorBit, level - 1, 0, 1),
                DstSubresource = new(ImageAspectFlags.ColorBit, level, 0, 1),
            };
            blit.SrcOffsets[0] = new(0, 0, 0);
            blit.SrcOffsets[1] = new((int)Math.Max(texture.Width >> (int)(level - 1), 1),
                (int)Math.Max(texture.Height >> (int)(level - 1), 1), 1);
            blit.DstOffsets[0] = new(0, 0, 0);
            blit.DstOffsets[1] = new((int)Math.Max(texture.Width >> (int)level, 1),
                (int)Math.Max(texture.Height >> (int)level, 1), 1);
            plant.Vk.CmdBlitImage(command.CommandBuffer, texture.NativeImage, ImageLayout.TransferSrcOptimal,
                texture.NativeImage, ImageLayout.TransferDstOptimal, 1, &blit, Filter.Linear);
        }
        for (uint level = 0; level < texture.MipLevels; level++)
            Transition(level, VulkanResourceLayout.ShaderResourceFragment);
        Require(command.End().Success, "Mip command end failed.");
        using var submitter = new VulkanCommandSubmitter(plant, commands, fences);
        var result = submitter.Submit(new(command, WaitForCompletion: true, DebugName: "texture.generate-mips"));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));

        void Transition(uint level, VulkanResourceLayout layout)
        {
            var transition = texture.LayoutTracker.Transition("texture.mip", level, 0, layout);
            Require(transition.Success, "Mip subresource transition rejected.");
            if (transition.Plan is not null)
            {
                var barrier = VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(texture, transition.Plan)]);
                Require(barrier.Success, string.Join("; ", barrier.Diagnostics.Select(item => item.Message)));
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
