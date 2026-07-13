using Machina.Core.Actions;
using Machina.Core.Lowering;
using Machina.Dominatus.Runtime;
using Machina.Layout.Compilation;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Runtime.Input;
using Xunit;

namespace Machina.Dominatus.Tests;

public sealed class CounterUiRuntimeTests
{
    [Fact]
    public void StartsAtZero()
    {
        var runtime = new CounterUiRuntime();

        Assert.Equal(0, runtime.Count);
    }

    [Fact]
    public void BuildUi_ContainsCountZeroText()
    {
        var runtime = new CounterUiRuntime();

        var lowering = UiLowerer.Lower(runtime.BuildUi());

        Assert.Contains(lowering.Semantics.Values, semantics => semantics.Label == "Count: 0");
    }

    [Fact]
    public void IncrementAction_UpdatesCount()
    {
        var runtime = new CounterUiRuntime();

        runtime.SendAction(UiAction.Named("increment"));
        runtime.TickUntilIdle();

        Assert.Equal(1, runtime.Count);
    }

    [Fact]
    public void UnknownAction_DoesNotChangeCount()
    {
        var runtime = new CounterUiRuntime();

        runtime.SendAction(UiAction.Named("unknown"));
        runtime.TickUntilIdle();

        Assert.Equal(0, runtime.Count);
    }

    [Fact]
    public void MultipleIncrements_Accumulate()
    {
        var runtime = new CounterUiRuntime();

        runtime.SendAction(UiAction.Named("increment"));
        runtime.TickUntilIdle();
        runtime.SendAction(UiAction.Named("increment"));
        runtime.TickUntilIdle();
        runtime.SendAction(UiAction.Named("increment"));
        runtime.TickUntilIdle();

        Assert.Equal(3, runtime.Count);
    }

    [Fact]
    public void RepeatedTicks_DoNotReprocessHistoricalIncrement()
    {
        var runtime = new CounterUiRuntime();

        runtime.SendAction(UiAction.Named("increment"));
        runtime.TickUntilIdle();
        runtime.TickUntilIdle(maxTicks: 8);

        Assert.Equal(1, runtime.Count);
    }

    [Fact]
    public void BuildUi_ReflectsUpdatedCount()
    {
        var runtime = new CounterUiRuntime();
        runtime.SendAction(UiAction.Named("increment"));
        runtime.TickUntilIdle();

        var lowering = UiLowerer.Lower(runtime.BuildUi());

        Assert.Contains(lowering.Semantics.Values, semantics => semantics.Label == "Count: 1");
    }

    [Fact]
    public void RuntimeUi_RemainsHitTestCompatible()
    {
        var runtime = new CounterUiRuntime();
        var ui = runtime.BuildUi();
        var lowering = UiLowerer.Lower(ui);
        var document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 640, 360));
        var index = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);

        var buttonRect = resolved.Nodes[new NodeId("increment")].Rect;
        var hit = index.HitTest(new PointerPoint(buttonRect.X + 1, buttonRect.Y + 1));

        Assert.NotNull(hit);
        Assert.Equal("increment", hit.Action.Name);
    }
}
