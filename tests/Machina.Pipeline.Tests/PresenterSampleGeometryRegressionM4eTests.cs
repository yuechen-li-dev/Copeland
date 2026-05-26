using Machina.Layout.Rows;
using Machina.Layout.Geometry;
using Machina.Pipeline;
using Machina.Presenter.Sample;
using Machina.Dominatus.Rendering.Commands;
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

    [Fact]
    public void PresenterSample_IncrementButton_HasSingleTextDraw()
    {
        var pipeline = new MachinaRasterPipeline();
        var frame = pipeline.Render(DemoDocumentFactory.Build(new DemoState(0, false, false)), DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);

        var incrementTextCommands = frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Text == "Increment")
            .ToList();

        Assert.Single(incrementTextCommands);
        Assert.Equal("settings-card/increment.label", incrementTextCommands[0].Id);
    }

    [Fact]
    public void PresenterSample_GeometryStableAcrossStateToggles()
    {
        var pipeline = new MachinaRasterPipeline();
        var offFrame = pipeline.Render(DemoDocumentFactory.Build(new DemoState(0, false, false)), DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);
        var onFrame = pipeline.Render(DemoDocumentFactory.Build(new DemoState(0, true, true)), DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);

        Assert.Equal(GetRect(offFrame, "settings-card"), GetRect(onFrame, "settings-card"));
        Assert.Equal(GetRect(offFrame, "settings-card/increment"), GetRect(onFrame, "settings-card/increment"));
        Assert.Equal(GetRect(offFrame, "settings-card/increment.label"), GetRect(onFrame, "settings-card/increment.label"));
        AssertTextRectAnchorStable(GetRect(offFrame, "settings-card/email-updates.label"), GetRect(onFrame, "settings-card/email-updates.label"));
        AssertTextRectAnchorStable(GetRect(offFrame, "settings-card/notifications.label"), GetRect(onFrame, "settings-card/notifications.label"));
        Assert.Equal(GetRowIds(offFrame), GetRowIds(onFrame));
        Assert.Equal(GetRect(offFrame, "settings-card/email-updates.mark"), GetRect(onFrame, "settings-card/email-updates.mark"));
        Assert.Equal(GetRect(offFrame, "settings-card/notifications.track"), GetRect(onFrame, "settings-card/notifications.track"));

        var offThumb = GetRect(offFrame, "settings-card/notifications.thumb");
        var onThumb = GetRect(onFrame, "settings-card/notifications.thumb");
        Assert.Equal(offThumb.Y, onThumb.Y);
        Assert.Equal(offThumb.Width, onThumb.Width);
        Assert.Equal(offThumb.Height, onThumb.Height);
        Assert.True(onThumb.X > offThumb.X);
    }

    private static Rect GetRect(MachinaFrame frame, string id)
    {
        return frame.Resolved.Nodes[new NodeId(id)].Rect;
    }

    private static IReadOnlyList<string> GetRowIds(MachinaFrame frame)
    {
        return frame.Lowering.Rows.Select(row => row.Id.Value).ToArray();
    }

    private static void AssertTextRectAnchorStable(Rect first, Rect second)
    {
        Assert.Equal(first.X, second.X);
        Assert.Equal(first.Y, second.Y);
        Assert.Equal(first.Height, second.Height);
    }
}
