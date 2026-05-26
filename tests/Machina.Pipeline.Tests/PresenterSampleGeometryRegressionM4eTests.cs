using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Presenter.Sample;
using Machina.Runtime.Input;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class PresenterSampleGeometryRegressionM4eTests
{
    [Fact]
    public void PresenterSample_ResolvedGeometry_MatchesExpectedControlRects()
    {
        var pipeline = new MachinaRasterPipeline();
        var frame = pipeline.Render(DemoDocumentFactory.Build(new DemoState(0, false, false)), DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);

        var cardRect = frame.Resolved.Nodes[new NodeId("settings-card")].Rect;
        var cardContentRect = frame.Resolved.Nodes[new NodeId("settings-card/settings-card-content.content")].Rect;
        Assert.Equal(500, cardRect.Width);
        Assert.Equal(292, cardRect.Height);
        Assert.True(cardContentRect.X > cardRect.X);

        var buttonShell = frame.Resolved.Nodes[new NodeId("settings-card/increment")].Rect;
        Assert.InRange(buttonShell.Width, 80, 140);
        Assert.Equal(32, buttonShell.Height);

        var checkboxBox = frame.Resolved.Nodes[new NodeId("settings-card/email-updates.box")].Rect;
        var checkboxLabel = frame.Resolved.Nodes[new NodeId("settings-card/email-updates.label")].Rect;
        Assert.Equal(18, checkboxBox.Width);
        Assert.True(checkboxLabel.X > checkboxBox.X + checkboxBox.Width);

        var switchTrack = frame.Resolved.Nodes[new NodeId("settings-card/notifications.track")].Rect;
        var switchThumb = frame.Resolved.Nodes[new NodeId("settings-card/notifications.thumb")].Rect;
        Assert.Equal(42, switchTrack.Width);
        Assert.True(switchThumb.X >= switchTrack.X && switchThumb.X + switchThumb.Width <= switchTrack.X + switchTrack.Width);
    }

    [Fact]
    public void PresenterSample_LoweredRows_SnapshotIncludesControlInternals()
    {
        var pipeline = new MachinaRasterPipeline();
        var frame = pipeline.Render(DemoDocumentFactory.Build(new DemoState(0, true, true)), DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);
        var ids = frame.Lowering.Rows.Select(row => row.Id.Value).ToArray();

        Assert.Contains("settings-card/increment", ids);
        Assert.Contains("settings-card/increment.label-region", ids);
        Assert.Contains("settings-card/email-updates.box", ids);
        Assert.Contains("settings-card/email-updates.mark", ids);
        Assert.Contains("settings-card/notifications.track", ids);
        Assert.Contains("settings-card/notifications.thumb", ids);
    }

    [Fact]
    public void PresenterSample_HitTesting_CoversFullButtonCheckboxAndSwitchTargets()
    {
        var pipeline = new MachinaRasterPipeline();
        var frame = pipeline.Render(DemoDocumentFactory.Build(new DemoState(0, true, false)), DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);

        var button = frame.Resolved.Nodes[new NodeId("settings-card/increment")].Rect;
        foreach (var x in new[] { button.X + 2, button.X + button.Width / 2, button.X + button.Width - 2 })
        {
            var hit = frame.HitTest.HitTest(new PointerPoint((float)x, (float)(button.Y + button.Height / 2)));
            Assert.NotNull(hit);
            Assert.Equal("counter.increment", hit!.Action.Name);
        }

        var checkboxLabel = frame.Resolved.Nodes[new NodeId("settings-card/email-updates.label")].Rect;
        var checkboxHit = frame.HitTest.HitTest(new PointerPoint((float)(checkboxLabel.X + 4), (float)(checkboxLabel.Y + 4)));
        Assert.NotNull(checkboxHit);
        Assert.Equal("settings.emailUpdates.toggle", checkboxHit!.Action.Name);

        var switchTrack = frame.Resolved.Nodes[new NodeId("settings-card/notifications.track")].Rect;
        var switchHit = frame.HitTest.HitTest(new PointerPoint((float)(switchTrack.X + 4), (float)(switchTrack.Y + 4)));
        Assert.NotNull(switchHit);
        Assert.Equal("settings.notifications.toggle", switchHit!.Action.Name);
    }
}
