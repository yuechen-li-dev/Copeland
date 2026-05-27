using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Machina.Testing;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterSampleContractM5fTests
{
    [Fact]
    public void PresenterDocument_BuildsDefaultState()
    {
        var document = DemoDocumentFactory.Build(DemoState.Default, StandardTheme.Default);

        Assert.Contains(document.Rows, row => row.Id == "root");

        var settingsCard = Assert.Single(document.Rows, row => row.Id == "settings-card");
        Assert.NotNull(settingsCard.Component);

        Assert.DoesNotContain(document.Rows, row => row.Id.Value.Contains("email-updates", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Rows, row => row.Id.Value.Contains("notifications", StringComparison.Ordinal));
    }

    [Fact]
    public void PresenterDocument_TopLevelLayoutIsFlatHostedComponent()
    {
        var document = DemoDocumentFactory.Build(DemoState.Default, StandardTheme.Default);

        Assert.Equal(2, document.Rows.Count);

        var root = Assert.Single(document.Rows, row => row.Id == "root");
        Assert.IsType<RootFrame>(root.Frame);

        var settingsCard = Assert.Single(document.Rows, row => row.Id == "settings-card");
        var frame = Assert.IsType<AnchorFrame>(settingsCard.Frame);
        Assert.Equal(72, frame.Left);
        Assert.Equal(24, frame.Top);
        Assert.Equal(500, frame.Width);
        Assert.Equal(292, frame.Height);
        Assert.NotNull(settingsCard.Component);
    }

    [Fact]
    public void PresenterDocument_ResolvedGeometry_KeyControlsInsideCard()
    {
        var frame = Render(new DemoState(0, false, false));

        var cardRect = frame.RectOf("settings-card");
        Assert.Equal(72, cardRect.X);
        Assert.Equal(24, cardRect.Y);
        Assert.Equal(500, cardRect.Width);
        Assert.Equal(292, cardRect.Height);

        var cardContentRect = frame.RectOf("settings-card/settings-card-content.content");
        Assert.InRange(cardContentRect.X, cardRect.X, cardRect.X + cardRect.Width);
        Assert.InRange(cardContentRect.Y, cardRect.Y, cardRect.Y + cardRect.Height);

        AssertRectInside(frame, "settings-card/increment", "settings-card/settings-card-content.content");
        AssertRectInside(frame, "settings-card/increment.label", "settings-card/settings-card-content.content");

        AssertRectInside(frame, "settings-card/email-updates.box", "settings-card/settings-card-content.content");
        AssertRectInside(frame, "settings-card/email-updates.label", "settings-card/settings-card-content.content");

        AssertRectInside(frame, "settings-card/notifications.track", "settings-card/settings-card-content.content");
        AssertRectInside(frame, "settings-card/notifications.thumb", "settings-card/settings-card-content.content");
        AssertRectInside(frame, "settings-card/notifications.label", "settings-card/settings-card-content.content");
    }

    [Fact]
    public void PresenterDocument_HitTargets_ReturnExpectedActions()
    {
        var frame = Render(new DemoState(0, true, false));

        frame.AssertHitActionInside("settings-card/increment", DemoDocumentFactory.Actions.Increment.Value, HitPointKind.LeftCenter);
        frame.AssertHitActionInside("settings-card/increment", DemoDocumentFactory.Actions.Increment.Value, HitPointKind.Center);
        frame.AssertHitActionInside("settings-card/increment", DemoDocumentFactory.Actions.Increment.Value, HitPointKind.RightCenter);

        frame.AssertHitActionInside("settings-card/email-updates.box", DemoDocumentFactory.Actions.ToggleEmailUpdates.Value, HitPointKind.Center);
        frame.AssertHitActionInside("settings-card/email-updates.label", DemoDocumentFactory.Actions.ToggleEmailUpdates.Value, HitPointKind.Center);

        frame.AssertHitActionInside("settings-card/notifications.track", DemoDocumentFactory.Actions.ToggleNotifications.Value, HitPointKind.Center);
        frame.AssertHitActionInside("settings-card/notifications.label", DemoDocumentFactory.Actions.ToggleNotifications.Value, HitPointKind.Center);
    }

    [Fact]
    public void PresenterDocument_GeometryStableAcrossStateToggles()
    {
        var offFrame = Render(new DemoState(0, false, false));
        var onFrame = Render(new DemoState(0, true, true));

        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card");
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card/increment");
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card/email-updates.mark");
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "settings-card/notifications.track");

        GeometryHarness.AssertSameRowIds(offFrame, onFrame);
        GeometryHarness.AssertOnlyXDiffers(offFrame, onFrame, "settings-card/notifications.thumb");
    }

    [Fact]
    public void PresenterDispatch_IncrementAndToggles_Work()
    {
        var incremented = DemoStateDispatch.Dispatch(new DemoState(0, true, false), DemoDocumentFactory.Actions.Increment);
        Assert.Equal(1, incremented.Count);
        Assert.True(incremented.EmailUpdates);
        Assert.False(incremented.Notifications);

        var emailToggled = DemoStateDispatch.Dispatch(new DemoState(3, true, false), DemoDocumentFactory.Actions.ToggleEmailUpdates);
        Assert.Equal(3, emailToggled.Count);
        Assert.False(emailToggled.EmailUpdates);
        Assert.False(emailToggled.Notifications);

        var notificationsToggled = DemoStateDispatch.Dispatch(new DemoState(4, true, false), DemoDocumentFactory.Actions.ToggleNotifications);
        Assert.Equal(4, notificationsToggled.Count);
        Assert.True(notificationsToggled.EmailUpdates);
        Assert.True(notificationsToggled.Notifications);

        var unchangedState = new DemoState(7, false, true);
        var unknown = DemoStateDispatch.Dispatch(unchangedState, new Machina.Core.Actions.UiActionId("unknown.action"));
        Assert.Same(unchangedState, unknown);
    }

    [Fact]
    public void PresenterDocument_CustomTheme_PropagatesButtonAndCard()
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

        frame.AssertHitActionInside("settings-card/increment", DemoDocumentFactory.Actions.Increment.Value, HitPointKind.LeftCenter);
    }

    [Fact]
    public void PresenterDocument_IncrementButton_HasSingleTextDraw()
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

    private static DocumentGeometryResult Render(DemoState state, StandardTheme? theme = null)
    {
        var document = DemoDocumentFactory.Build(state, theme ?? StandardTheme.Default);
        return GeometryHarness.ResolveDocument(document, DemoDocumentFactory.RootWidth, DemoDocumentFactory.RootHeight);
    }

    private static void AssertRectInside(DocumentGeometryResult frame, string innerId, string outerId)
    {
        var inner = frame.RectOf(innerId);
        var outer = frame.RectOf(outerId);

        Assert.InRange(inner.X, outer.X, outer.X + outer.Width);
        Assert.InRange(inner.Y, outer.Y, outer.Y + outer.Height);
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width, $"{innerId} exceeds {outerId} in X.");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height, $"{innerId} exceeds {outerId} in Y.");
    }
}
