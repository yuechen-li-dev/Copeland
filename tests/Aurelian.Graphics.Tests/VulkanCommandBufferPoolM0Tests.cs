using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanCommandBufferPoolM0Tests
{
    [Fact]
    public void VulkanCommandBufferPool_Create_WhenVulkanUnavailable_SkipsCleanly()
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
    public void VulkanCommandBufferPool_Create_WhenPlantCreated_CreatesPoolForPlantZero()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var pool = VulkanCommandBufferPool.Create(init.Plant!);

            Assert.Equal(PlantId.Zero, pool.PlantId);
            Assert.Equal(init.Plant!.QueueFamilyIndex, pool.QueueFamilyIndex);
            Assert.Equal(PlantId.Zero, pool.Telemetry.PlantId);
        }
    }

    [Fact]
    public void VulkanCommandBufferPool_Rent_ReturnsReadyLease()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var pool = VulkanCommandBufferPool.Create(init.Plant!);
            VulkanCommandBufferLease lease = pool.Rent(completedFenceValue: 0);

            Assert.Equal(PlantId.Zero, lease.PlantId);
            Assert.True(lease.IsReady);
            Assert.True(lease.CommandBuffer.Handle != 0);
            Assert.Equal(1UL, pool.Telemetry.CreatedCount);
            Assert.Equal(1UL, pool.Telemetry.RentedCount);
        }
    }

    [Fact]
    public void VulkanCommandBufferLease_BeginEndReset_SucceedsWhenPlantCreated()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var pool = VulkanCommandBufferPool.Create(init.Plant!);
            VulkanCommandBufferLease lease = pool.Rent(completedFenceValue: 0);

            VulkanCommandBufferOperationResult begin = lease.Begin();
            VulkanCommandBufferOperationResult end = lease.End();
            VulkanCommandBufferOperationResult reset = lease.Reset();

            Assert.True(begin.Success, FormatDiagnostics(begin));
            Assert.True(end.Success, FormatDiagnostics(end));
            Assert.True(reset.Success, FormatDiagnostics(reset));
            Assert.True(lease.IsReady);
        }
    }

    [Fact]
    public void VulkanCommandBufferPool_RetireFutureFence_DoesNotReuseBeforeFenceCompletes()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var pool = VulkanCommandBufferPool.Create(init.Plant!);
            VulkanCommandBufferLease first = pool.Rent(completedFenceValue: 0);
            pool.Retire(first, retireFenceValue: 2);

            VulkanCommandBufferLease second = pool.Rent(completedFenceValue: 1);

            Assert.NotSame(first, second);
            Assert.Equal(2UL, pool.Telemetry.CreatedCount);
            Assert.Equal(0UL, pool.Telemetry.ReusedCount);
            Assert.Equal(1, pool.Telemetry.QueuedCount);
        }
    }

    [Fact]
    public void VulkanCommandBufferPool_RetireReadyFence_ReusesInFifoOrder()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var pool = VulkanCommandBufferPool.Create(init.Plant!);
            VulkanCommandBufferLease first = pool.Rent(completedFenceValue: 0);
            VulkanCommandBufferLease second = pool.Rent(completedFenceValue: 0);

            pool.Retire(first, retireFenceValue: 3);
            pool.Retire(second, retireFenceValue: 3);

            VulkanCommandBufferLease reusedFirst = pool.Rent(completedFenceValue: 3);
            VulkanCommandBufferLease reusedSecond = pool.Rent(completedFenceValue: 3);

            Assert.Same(first, reusedFirst);
            Assert.Same(second, reusedSecond);
            Assert.True(reusedFirst.IsReady);
            Assert.True(reusedSecond.IsReady);
            Assert.Equal(2UL, pool.Telemetry.ReusedCount);
            Assert.Equal(0, pool.Telemetry.QueuedCount);
        }
    }

    [Fact]
    public void VulkanCommandBufferPool_Dispose_IsIdempotent()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            var pool = VulkanCommandBufferPool.Create(init.Plant!);
            _ = pool.Rent(completedFenceValue: 0);

            pool.Dispose();
            pool.Dispose();
        }
    }

    [Fact]
    public void VulkanCommandBufferPool_UsesPlantQueueFamilyNotHardcodedZero()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var pool = VulkanCommandBufferPool.Create(init.Plant!);

            Assert.Equal(init.Plant!.QueueFamilyIndex, pool.QueueFamilyIndex);
            Assert.Equal(init.Plant.Facts.QueueFamilyIndex, pool.QueueFamilyIndex);
        }
    }

    private static string FormatDiagnostics(VulkanCommandBufferOperationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message));
}
