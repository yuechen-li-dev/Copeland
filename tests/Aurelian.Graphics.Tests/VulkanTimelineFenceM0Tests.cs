using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Sync;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanTimelineFenceM0Tests
{
    [Fact]
    public void VulkanTimelineFence_CreateBundle_WhenVulkanUnavailable_SkipsCleanly()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (init.Success)
            {
                return;
            }

            Assert.NotEmpty(init.Diagnostics);
        }
    }

    [Fact]
    public void VulkanTimelineFence_CreateBundle_WhenPlantCreated_CreatesThreeFences()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var fences = VulkanFenceBundle.Create(init.Plant!);

            Assert.Equal(PlantId.Zero, fences.FrameFence.PlantId);
            Assert.Equal(PlantId.Zero, fences.CommandListFence.PlantId);
            Assert.Equal(PlantId.Zero, fences.CopyFence.PlantId);
            Assert.NotEqual(0UL, fences.FrameFence.Semaphore.Handle);
            Assert.NotEqual(0UL, fences.CommandListFence.Semaphore.Handle);
            Assert.NotEqual(0UL, fences.CopyFence.Semaphore.Handle);
        }
    }

    [Fact]
    public void VulkanTimelineFence_AllocateSignalValue_IncrementsMonotonically()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var fences = VulkanFenceBundle.Create(init.Plant!);

            ulong first = fences.FrameFence.AllocateSignalValue();
            ulong second = fences.FrameFence.AllocateSignalValue();
            ulong third = fences.FrameFence.AllocateSignalValue();

            Assert.Equal(1UL, first);
            Assert.Equal(2UL, second);
            Assert.Equal(3UL, third);
            Assert.Equal(4UL, fences.FrameFence.NextValue);
        }
    }

    [Fact]
    public void VulkanTimelineFence_QueryCompletedValue_DoesNotThrowWhenCreated()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var fences = VulkanFenceBundle.Create(init.Plant!);
            VulkanFenceOperationResult result = fences.FrameFence.QueryCompletedValue();

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            Assert.Equal(0UL, result.Value);
        }
    }

    [Fact]
    public void VulkanTimelineFence_Dispose_IsIdempotent()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            var fences = VulkanFenceBundle.Create(init.Plant!);
            fences.Dispose();
            fences.Dispose();
        }
    }
}
