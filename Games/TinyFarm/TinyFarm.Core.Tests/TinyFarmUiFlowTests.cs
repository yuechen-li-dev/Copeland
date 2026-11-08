using Machina.Runtime.State;
using TinyFarm.InputMan;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmUiFlowTests
{
    [Fact]
    public void DominatusPopDisposesChildAndResumesTheSameParentScope()
    {
        using var flow = new TinyFarmUiFlow();
        UiStateScope title = flow.Scope;
        CancellationToken titleLifetime = title.Lifetime;
        flow.Reset(TinyFarmScreen.Playing);
        Assert.True(titleLifetime.IsCancellationRequested);
        flow.Push(TinyFarmScreen.Paused);
        UiStateScope pause = flow.Scope;
        object parentState = pause.GetOrCreate("selection", () => new object());
        flow.Push(TinyFarmScreen.Inventory);
        UiStateScope inventory = flow.Scope;
        CancellationToken inventoryLifetime = inventory.Lifetime;
        inventory.GetOrCreate("search", () => new object());

        flow.Back();

        Assert.Equal(TinyFarmScreen.Paused, flow.Screen);
        Assert.Same(pause, flow.Scope);
        Assert.Same(parentState, flow.Scope.GetOrCreate("selection", () => new object()));
        Assert.True(inventory.IsDisposed);
        Assert.True(inventoryLifetime.IsCancellationRequested);
        Assert.Equal(0, inventory.Count);
        flow.Push(TinyFarmScreen.Inventory);
        Assert.NotSame(inventory, flow.Scope);
    }

    [Fact]
    public void ImmediateEntryExitAndSameScreenResetHaveExactLifetimes()
    {
        using var flow = new TinyFarmUiFlow();
        flow.Reset(TinyFarmScreen.Playing);
        UiStateScope playing = flow.Scope;
        flow.Push(TinyFarmScreen.Stats);
        UiStateScope stats = flow.Scope;
        flow.Back();
        Assert.True(stats.IsDisposed);
        Assert.Same(playing, flow.Scope);
        flow.Reset(TinyFarmScreen.Playing);
        Assert.True(playing.IsDisposed);
        Assert.NotSame(playing, flow.Scope);
        Assert.Equal([TinyFarmScreen.Playing], flow.Path);
    }

    [Fact]
    public void ShutdownExitsAllFramesAndCancelsTheirWork()
    {
        var flow = new TinyFarmUiFlow();
        var root = flow.Scope;
        flow.Push(TinyFarmScreen.Inventory);
        var child = flow.Scope;
        flow.Dispose();
        flow.Dispose();
        Assert.True(root.Lifetime.IsCancellationRequested);
        Assert.True(child.Lifetime.IsCancellationRequested);
        Assert.True(root.IsDisposed);
        Assert.True(child.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => flow.Reset(TinyFarmScreen.Playing));
    }
}
