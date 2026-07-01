using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Compositor;
using Silk.NET.Vulkan;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed unsafe class VulkanPlantOutputImageM0Tests
{
    [Fact]
    public void VulkanPlantOutputImageSet_WrapsTextureWithoutOwningIt()
    {
        using AurelianVulkanTexture texture = CreateTexture(VulkanTextureUsage.TransferSource | VulkanTextureUsage.ColorAttachment);
        PlantOutputRef outputRef = new(texture.PlantId.Value, 7, "final");

        VulkanPlantOutputImage output = new(outputRef, texture);
        VulkanPlantOutputImageSet imageSet = new([output]);

        Assert.True(imageSet.TryGet(outputRef, out VulkanPlantOutputImage resolved));
        Assert.Same(output, resolved);
        Assert.Same(texture, output.Texture);
        Assert.False(texture.IsDisposed);
    }

    [Fact]
    public void VulkanPlantOutputImageSet_RejectsDisposedTexture()
    {
        AurelianVulkanTexture texture = CreateTexture(VulkanTextureUsage.TransferSource);
        texture.Dispose();

        Assert.Throws<ObjectDisposedException>(() => new VulkanPlantOutputImage(new PlantOutputRef(3, 7, "final"), texture));
    }

    [Fact]
    public void VulkanPlantOutputImageSet_RejectsTextureWithoutTransferSourceUsage()
    {
        using AurelianVulkanTexture texture = CreateTexture(VulkanTextureUsage.ColorAttachment);

        Assert.Throws<ArgumentException>(() => new VulkanPlantOutputImage(new PlantOutputRef(3, 7, "final"), texture));
    }

    [Fact]
    public void VulkanPlantOutputResolver_ResolvesMatchingOutputRef()
    {
        using AurelianVulkanTexture texture = CreateTexture(VulkanTextureUsage.TransferSource);
        PlantOutputRef outputRef = new(texture.PlantId.Value, 7, "final");
        VulkanPlantOutputImage output = new(outputRef, texture);
        VulkanPlantOutputImageSet imageSet = new([output]);

        VulkanPlantOutputResolutionResult result = VulkanPlantOutputResolver.Resolve(imageSet, outputRef);

        Assert.True(result.Success);
        Assert.Equal(VulkanPlantOutputStatus.Resolved, result.Status);
        Assert.Same(output, result.Output);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void VulkanPlantOutputResolver_RejectsMissingOutput()
    {
        using AurelianVulkanTexture texture = CreateTexture(VulkanTextureUsage.TransferSource);
        VulkanPlantOutputImageSet imageSet = new([new VulkanPlantOutputImage(new PlantOutputRef(texture.PlantId.Value, 7, "final"), texture)]);

        VulkanPlantOutputResolutionResult result = VulkanPlantOutputResolver.Resolve(imageSet, new PlantOutputRef(texture.PlantId.Value, 8, "missing"));

        Assert.False(result.Success);
        Assert.Equal(VulkanPlantOutputStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanPlantOutputDiagnosticCodes.OutputMissing);
    }

    [Fact]
    public void VulkanPlantOutputResolver_RejectsPlantMismatch()
    {
        using AurelianVulkanTexture texture = CreateTexture(VulkanTextureUsage.TransferSource, new PlantId(3));
        VulkanPlantOutputImageSet imageSet = new([new VulkanPlantOutputImage(new PlantOutputRef(3, 7, "final"), texture)]);

        VulkanPlantOutputResolutionResult result = VulkanPlantOutputResolver.Resolve(imageSet, new PlantOutputRef(4, 7, "final"));

        Assert.False(result.Success);
        Assert.Equal(VulkanPlantOutputStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanPlantOutputDiagnosticCodes.PlantMismatch);
    }

    internal static AurelianVulkanTexture CreateTexture(
        VulkanTextureUsage usage,
        PlantId? plantId = null,
        uint width = 64,
        uint height = 64,
        VulkanTextureFormat format = VulkanTextureFormat.Bgra8Srgb,
        VulkanResourceLayout initialLayout = VulkanResourceLayout.ColorAttachment)
    {
        PlantId resolvedPlant = plantId ?? new PlantId(3);
        VulkanMemoryAllocation allocation = new(
            resolvedPlant,
            VulkanAllocationBackendKind.RawVulkan,
            default,
            0,
            Math.Max(1UL, width * height * 4UL),
            VulkanMemoryUsage.GpuOnly,
            null,
            _ => { });

        return new AurelianVulkanTexture(
            null!,
            default,
            default,
            null,
            allocation,
            resolvedPlant,
            width,
            height,
            1,
            1,
            format,
            usage,
            initialLayout);
    }
}
