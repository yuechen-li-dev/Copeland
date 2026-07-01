using Aurelian.Graphics.Vulkan.Resources;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class FenceTaggedResourcePoolM0Tests
{
    [Fact]
    public void FenceTaggedResourcePool_Rent_CreatesWhenNoRetiredResourceIsReady()
    {
        int next = 0;
        var pool = new FenceTaggedResourcePool<int>(() => ++next);

        int rented = pool.Rent(completedFenceValue: 0);

        Assert.Equal(1, rented);
        Assert.Equal(1UL, pool.Telemetry.CreatedCount);
        Assert.Equal(1UL, pool.Telemetry.RentedCount);
        Assert.Equal(0UL, pool.Telemetry.ReusedCount);
    }

    [Fact]
    public void FenceTaggedResourcePool_Rent_ReusesReadyResourceInFifoOrder()
    {
        int next = 100;
        var resetOrder = new List<int>();
        var pool = new FenceTaggedResourcePool<int>(() => ++next, resetOrder.Add);

        pool.Retire(1, retireFenceValue: 4);
        pool.Retire(2, retireFenceValue: 4);

        int first = pool.Rent(completedFenceValue: 4);
        int second = pool.Rent(completedFenceValue: 4);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(new[] { 1, 2 }, resetOrder);
        Assert.Equal(2UL, pool.Telemetry.ReusedCount);
        Assert.Equal(0, pool.Telemetry.QueuedCount);
    }

    [Fact]
    public void FenceTaggedResourcePool_Rent_DoesNotReuseFutureFenceResource()
    {
        int next = 10;
        var pool = new FenceTaggedResourcePool<int>(() => ++next);

        pool.Retire(1, retireFenceValue: 5);
        int rented = pool.Rent(completedFenceValue: 4);

        Assert.Equal(11, rented);
        Assert.Equal(1, pool.Telemetry.QueuedCount);
        Assert.Equal(1UL, pool.Telemetry.CreatedCount);
        Assert.Equal(0UL, pool.Telemetry.ReusedCount);
    }

    [Fact]
    public void FenceTaggedResourcePool_Retire_UpdatesTelemetry()
    {
        var pool = new FenceTaggedResourcePool<object>(() => new object());
        object first = new();
        object second = new();

        pool.Retire(first, retireFenceValue: 1);
        pool.Retire(second, retireFenceValue: 2);

        ResourcePoolTelemetry telemetry = pool.Telemetry;
        Assert.Equal(2UL, telemetry.RetiredCount);
        Assert.Equal(2, telemetry.QueuedCount);
        Assert.Equal(2, telemetry.HighWaterQueuedCount);
        Assert.True(telemetry.Generation >= 2);
    }

    [Fact]
    public async Task FenceTaggedResourcePool_Rent_DoesNotHoldLockDuringCreate()
    {
        using var createStarted = new ManualResetEventSlim(false);
        using var allowCreateToFinish = new ManualResetEventSlim(false);
        var pool = new FenceTaggedResourcePool<int>(() =>
        {
            createStarted.Set();
            Assert.True(allowCreateToFinish.Wait(TimeSpan.FromSeconds(5)));
            return 7;
        });

        Task<int> rentTask = Task.Run(() => pool.Rent(completedFenceValue: 0));
        Assert.True(createStarted.Wait(TimeSpan.FromSeconds(5)));

        Task retireTask = Task.Run(() => pool.Retire(3, retireFenceValue: 1));
        Task completedRetire = await Task.WhenAny(retireTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(retireTask, completedRetire);
        await retireTask;

        allowCreateToFinish.Set();
        Task<int> completedRent = await Task.WhenAny(rentTask, Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(static _ => -1));
        Assert.Same(rentTask, completedRent);
        Assert.Equal(7, await rentTask);
        Assert.Equal(1, pool.Telemetry.QueuedCount);
    }
}
