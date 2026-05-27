using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Machina.Testing;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class PresenterSampleGeometryRegressionM4eTests
{
    [Fact]
    public void PresenterSample_ResolvedGeometry_MatchesExpectedControlRects()
    {
        var frame = Render(new DemoState(0, false, false));

        var cardRect = frame.RectOf("settings-card");
        var cardContentRect = frame.RectOf("settings-card/settings-card-content.content");
        Assert.Equal(500, cardRect.Width);
        Assert.Equal(292, cardRect.Height);
        Assert.True(cardContentRect.X > cardRect.X);

        var buttonShell = frame.RectOf("settings-card/increment");
        Assert.InRange(buttonShell.Width, 80, 140);
        Assert.Equal(32, buttonShell.Height);

        var checkboxBox = frame.RectOf("settings-card/email-updates.box");
        var checkboxLabel = frame.RectOf("settings-card/email-updates.label");
        Assert.Equal(18, checkboxBox.Width);
        Assert.True(checkboxLabel.X > checkboxBox.X + checkboxBox.Width);

        var switchTrack = frame.RectOf("settings-card/notifications.track");
        var switchThumb = frame.RectOf("settings-card/notifications.thumb");
        Assert.Equal(42, switchTrack.Width);
        Assert.True(switchThumb.X >= switchTrack.X && switchThumb.X + switchThumb.Width <= switchTrack.X + switchTrack.Width);
    }

    [Fact]
    public void PresenterSample_LoweredRows_SnapshotIncludesControlInternals()
    {
        var frame = Render(new DemoState(0, true, true));

        frame.AssertContainsRows(
            "settings-card/increment",
            "settings-card/increment.label-region",
            "settings-card/email-updates.box",
            "settings-card/email-updates.mark",
            "settings-card/notifications.track",
            "settings-card/notifications.thumb");
    }

    [Fact]
    public void PresenterSample_HitTesting_CoversFullButtonCheckboxAndSwitchTargets()
    {
        var frame = Render(new DemoState(0, true, false));

        frame.AssertHitActionInside("settings-card/increment", "counter.increment", HitPointKind.LeftCenter);
        frame.AssertHitActionInside("settings-card/increment", "counter.increment", HitPointKind.Center);
        frame.AssertHitActionInside("settings-card/increment", "counter.increment", HitPointKind.RightCenter);
        frame.AssertHitActionInside("settings-card/email-updates.label", "settings.emailUpdates.toggle");
        frame.AssertHitActionInside("settings-card/notifications.track", "settings.notifications.toggle");
    }

    [Fact]
    public void PresenterSample_IncrementButton_HasSingleTextDraw()
    {
        var pipeline = new MachinaRasterPipeline();
        var rendered = pipeline.Render(DemoDocumentFactory.Build(new DemoState(0, false, false)), DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);

        var incrementTextCommands = rendered.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Text == "Increment")
            .ToList();

        Assert.Single(incrementTextCommands);
        Assert.Equal("settings-card/increment.label", incrementTextCommands[0].Id);
    }

    [Fact]
    public void PresenterSample_GeometryStableAcrossStateToggles()
    {
        var offFrame = Render(new DemoState(0, false, false));
        var onFrame = Render(new DemoState(0, true, true));

        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card");
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card/increment");
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card/increment.label");
        GeometryHarness.AssertSameRowIds(offFrame, onFrame);
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card/email-updates.mark");
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card/notifications.track");
        GeometryHarness.AssertOnlyXDiffers(offFrame, onFrame, "settings-card/notifications.thumb");
    }

    [Fact]
    public void PresenterSample_CustomThemeAffectsButtonAndCard()
    {
        var customTheme = StandardTheme.Default with
        {
            Button = StandardTheme.Default.Button with
            {
                Default = StandardTheme.Default.Button.Default with
                {
                    Background = Machina.Core.Styling.ColorToken.Hex(0x111827FF),
                    Foreground = Machina.Core.Styling.ColorToken.Hex(0xF9FAFBFF),
                    Width = 144,
                    Height = 36,
                },
            },
            Card = StandardTheme.Default.Card with
            {
                Default = StandardTheme.Default.Card.Default with
                {
                    ContentInset = 18,
                },
            },
        };

        var frame = Render(new DemoState(0, false, false), customTheme);

        var cardRect = frame.RectOf("settings-card/settings-card-content");
        var contentRect = frame.RectOf("settings-card/settings-card-content.content");
        Assert.Equal(cardRect.X + 18, contentRect.X);
        Assert.Equal(cardRect.Y + 18, contentRect.Y);

        var buttonRect = frame.RectOf("settings-card/increment");
        Assert.Equal(144, buttonRect.Width);
        Assert.Equal(36, buttonRect.Height);
        Assert.Equal(customTheme.Button.Default.Background, frame.StyleOf("settings-card/increment").Background);

        frame.AssertHitActionInside("settings-card/increment", "counter.increment", HitPointKind.LeftCenter);
    }

    private static DocumentGeometryResult Render(DemoState state, StandardTheme? theme = null)
    {
        var document = DemoDocumentFactory.Build(state, theme ?? StandardTheme.Default);
        return GeometryHarness.ResolveDocument(document, DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);
    }
}
