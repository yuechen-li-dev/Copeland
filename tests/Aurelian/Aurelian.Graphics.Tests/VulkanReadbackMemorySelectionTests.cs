using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Silk.NET.Vulkan;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanReadbackMemorySelectionTests
{
    [Theory]
    [InlineData(VulkanMemoryUsage.GpuToCpu, true, 7u, 1u)]
    [InlineData(VulkanMemoryUsage.GpuToCpu, false, 7u, 0u)]
    [InlineData(VulkanMemoryUsage.CpuToGpu, true, 7u, 0u)]
    [InlineData(VulkanMemoryUsage.GpuOnly, true, 7u, 2u)]
    [InlineData(VulkanMemoryUsage.GpuToCpu, true, 1u, 0u)]
    public void SelectionPrefersCachedReadbackAndPreservesOtherUsageAndFallback(
        VulkanMemoryUsage usage, bool preferCached, uint compatibleBits, uint expected)
    {
        var properties = Properties();
        Assert.True(RawVulkanMemoryAllocator.TrySelectMemoryType(properties, compatibleBits, usage, preferCached, out uint selected));
        Assert.Equal(expected, selected);
    }

    [Fact]
    public void CachedNoncoherentMemoryCannotBypassTheExistingCoherencyContract()
    {
        var properties = Properties();
        properties.MemoryTypes[1] = new() { PropertyFlags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit };
        Assert.True(RawVulkanMemoryAllocator.TrySelectMemoryType(properties, 7, VulkanMemoryUsage.GpuToCpu, true, out uint selected));
        Assert.Equal(0u, selected);
    }

    [Fact]
    public void NoCompatibleHostMemoryIsRejected()
    {
        Assert.False(RawVulkanMemoryAllocator.TrySelectMemoryType(Properties(), 4, VulkanMemoryUsage.GpuToCpu, true, out _));
    }

    private static PhysicalDeviceMemoryProperties Properties()
    {
        var properties = new PhysicalDeviceMemoryProperties { MemoryTypeCount = 3 };
        var coherent = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        properties.MemoryTypes[0] = new() { PropertyFlags = coherent };
        properties.MemoryTypes[1] = new() { PropertyFlags = coherent | MemoryPropertyFlags.HostCachedBit };
        properties.MemoryTypes[2] = new() { PropertyFlags = MemoryPropertyFlags.DeviceLocalBit };
        return properties;
    }
}
