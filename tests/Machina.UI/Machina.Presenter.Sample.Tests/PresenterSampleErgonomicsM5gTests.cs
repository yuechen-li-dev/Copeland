using Machina.Dominatus.Rendering.Commands;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Presenter.Sample;
using Machina.Renderer.Raster.Text;
using Machina.Standard.Theme;
using Machina.Testing;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterSampleErgonomicsM5gTests
{
    [Fact]
    public void SettingsScreen_BuildsCanonicalDocument()
    {
        var document = SettingsScreen.Build(DemoState.Default, StandardTheme.Default);

        Assert.Contains(document.Rows, row => row.Id == "root");

        var settingsCard = Assert.Single(document.Rows, row => row.Id == "settings-card");
        Assert.NotNull(settingsCard.Component);

        Assert.DoesNotContain(document.Rows, row => row.Id.Value.Contains("email-updates", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Rows, row => row.Id.Value.Contains("notifications", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingsScreen_TopLevelLayoutIsFlatHostedComponent()
    {
        var document = SettingsScreen.Build(DemoState.Default, StandardTheme.Default);

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
    public void SettingsCard_UsesStandardUIComponentsLocally()
    {
        var document = SettingsScreen.Build(DemoState.Default, StandardTheme.Default);

        Assert.Equal(2, document.Rows.Count);
        Assert.DoesNotContain(document.Rows, row => row.Id.Value.Contains("increment", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Rows, row => row.Id.Value.Contains("email-updates", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Rows, row => row.Id.Value.Contains("notifications", StringComparison.Ordinal));

        var frame = Render(DemoState.Default);
        Assert.Contains("settings-card/increment", frame.Resolved.Nodes.Keys);
        Assert.Contains("settings-card/email-updates", frame.Resolved.Nodes.Keys);
        Assert.Contains("settings-card/notifications", frame.Resolved.Nodes.Keys);
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
        AssertRectInside(frame, "settings-card/rich-text-probe", "settings-card/settings-card-content.content");
        AssertRectInside(frame, "settings-card/rich-text-probe.content", "settings-card/settings-card-content.content");
    }

    [Fact]
    public void SettingsActions_AreSingleSourceForDispatchAndComponents()
    {
        var frame = Render(new DemoState(0, true, false));

        frame.AssertHitActionInside("settings-card/increment", SettingsActions.Increment.Value, HitPointKind.LeftCenter);
        frame.AssertHitActionInside("settings-card/increment", SettingsActions.Increment.Value, HitPointKind.Center);
        frame.AssertHitActionInside("settings-card/increment", SettingsActions.Increment.Value, HitPointKind.RightCenter);

        frame.AssertHitActionInside("settings-card/email-updates.box", SettingsActions.ToggleEmailUpdates.Value, HitPointKind.Center);
        frame.AssertHitActionInside("settings-card/email-updates.label", SettingsActions.ToggleEmailUpdates.Value, HitPointKind.Center);

        frame.AssertHitActionInside("settings-card/notifications.track", SettingsActions.ToggleNotifications.Value, HitPointKind.Center);
        frame.AssertHitActionInside("settings-card/notifications.label", SettingsActions.ToggleNotifications.Value, HitPointKind.Center);
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
    public void DemoStateDispatch_IncrementAndToggles_Work()
    {
        var incremented = DemoStateDispatch.Dispatch(new DemoState(0, true, false), SettingsActions.Increment);
        Assert.Equal(1, incremented.Count);
        Assert.True(incremented.EmailUpdates);
        Assert.False(incremented.Notifications);

        var emailToggled = DemoStateDispatch.Dispatch(new DemoState(3, true, false), SettingsActions.ToggleEmailUpdates);
        Assert.Equal(3, emailToggled.Count);
        Assert.False(emailToggled.EmailUpdates);
        Assert.False(emailToggled.Notifications);

        var notificationsToggled = DemoStateDispatch.Dispatch(new DemoState(4, true, false), SettingsActions.ToggleNotifications);
        Assert.Equal(4, notificationsToggled.Count);
        Assert.True(notificationsToggled.EmailUpdates);
        Assert.True(notificationsToggled.Notifications);

        var unchangedState = new DemoState(7, false, true);
        var unknown = DemoStateDispatch.Dispatch(unchangedState, new Machina.Core.Actions.UiActionId("unknown.action"));
        Assert.Same(unchangedState, unknown);
    }

    [Fact]
    public void SettingsScreen_CustomTheme_PropagatesToCardAndChildren()
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

        frame.AssertHitActionInside("settings-card/increment", SettingsActions.Increment.Value, HitPointKind.LeftCenter);
    }

    [Fact]
    public void PresenterDocument_IncrementButton_HasSingleTextDraw()
    {
        var pipeline = new MachinaRasterPipeline();
        var rendered = pipeline.Render(SettingsScreen.Build(new DemoState(0, false, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);

        var incrementTextCommands = rendered.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Text == "Increment")
            .ToList();

        Assert.Single(incrementTextCommands);
        Assert.Equal("settings-card/increment.label", incrementTextCommands[0].Id);
    }

    [Fact]
    public void PresenterSample_RichTextProbe_ExistsAndRendersThroughTextBlock()
    {
        var frame = new MachinaRasterPipeline().Render(SettingsScreen.Build(new DemoState(0, false, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);

        Assert.Contains("settings-card/rich-text-probe", frame.Resolved.Nodes.Keys);
        Assert.Contains("settings-card/rich-text-probe.content", frame.Resolved.Nodes.Keys);

        var drawCommands = frame.RenderCommands.OfType<DrawTextCommand>().ToList();
        Assert.Contains(drawCommands, command => command.Id.StartsWith("settings-card/rich-text-probe.content.", StringComparison.Ordinal));
        Assert.Contains(drawCommands, command => command.Text == "Standard.Text");
        Assert.Contains(drawCommands, command => command.Text == "\u2022");
    }

    [Fact]
    public void PresenterSample_TextNodes_HaveVisibleTextStyles()
    {
        var frame = new MachinaRasterPipeline().Render(SettingsScreen.Build(new DemoState(2, false, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);
        var textCommands = frame.RenderCommands.OfType<DrawTextCommand>().ToList();
        AssertVisibleText(textCommands, "Machina Presenter", "settings-card/title", TextSize.Md);
        AssertVisibleText(textCommands, "Count: 2", "settings-card/count", TextSize.Sm);

        var cardContent = frame.Resolved.Nodes["settings-card/settings-card-content.content"].Rect;
        foreach (var command in textCommands.Where(x => x.Id is "settings-card/title" or "settings-card/count"))
        {
            AssertRectInside(command.Rect, cardContent, command.Id);
        }
    }

    [Fact]
    public void PresenterSample_IncrementButton_TextFitsShell()
    {
        var frame = new MachinaRasterPipeline().Render(SettingsScreen.Build(new DemoState(0, false, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);
        var incrementTextCommands = frame.RenderCommands.OfType<DrawTextCommand>().Where(command => command.Text == "Increment").ToList();
        var incrementText = Assert.Single(incrementTextCommands);
        Assert.Equal("settings-card/increment.label", incrementText.Id);

        var labelRegion = frame.Resolved.Nodes["settings-card/increment.label-region"].Rect;
        var measured = ReadableBitmapTextRasterizer.MeasureText("Increment", incrementText.Style);

        Assert.Equal(StandardTheme.Default.Button.Default.TextStyle.Size, incrementText.Style.Size);
        Assert.Equal(labelRegion, incrementText.Rect);
        Assert.True(measured.Width <= labelRegion.Width, $"Text width {measured.Width} exceeds label region width {labelRegion.Width}.");
        Assert.True(measured.Height <= labelRegion.Height, $"Text height {measured.Height} exceeds label region height {labelRegion.Height}.");
    }

    [Fact]
    public void PresenterSample_RawTextCommands_FitTheirResolvedTextRects()
    {
        var frame = new MachinaRasterPipeline().Render(SettingsScreen.Build(new DemoState(2, true, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);
        var textCommands = frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id is "settings-card/title" or "settings-card/count")
            .ToList();

        Assert.NotEmpty(textCommands);

        foreach (var command in textCommands)
        {
            var measured = ReadableBitmapTextRasterizer.MeasureText(command.Text, command.Style);
            Assert.True(measured.Width <= command.Rect.Width, $"{command.Id} text width {measured.Width} exceeds rect width {command.Rect.Width}.");
            Assert.True(measured.Height <= command.Rect.Height, $"{command.Id} text height {measured.Height} exceeds rect height {command.Rect.Height}.");
        }
    }

    [Fact]
    public void PresenterSample_CheckedCheckbox_MarkIsVisible()
    {
        var checkedFrame = new MachinaRasterPipeline().Render(SettingsScreen.Build(new DemoState(0, true, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);
        var uncheckedFrame = new MachinaRasterPipeline().Render(SettingsScreen.Build(new DemoState(0, false, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);

        var markRect = checkedFrame.Resolved.Nodes["settings-card/email-updates.mark"].Rect;
        var boxRect = checkedFrame.Resolved.Nodes["settings-card/email-updates.box"].Rect;
        Assert.True(markRect.Width > 0 && markRect.Height > 0);
        Assert.True(markRect.X >= boxRect.X && markRect.Y >= boxRect.Y);
        Assert.True(markRect.X + markRect.Width <= boxRect.X + boxRect.Width);
        Assert.True(markRect.Y + markRect.Height <= boxRect.Y + boxRect.Height);

        var checkedMarkFill = Assert.Single(checkedFrame.RenderCommands.OfType<FillRectCommand>(), x => x.Id == "settings-card/email-updates.mark");
        var uncheckedMarkFill = Assert.Single(uncheckedFrame.RenderCommands.OfType<FillRectCommand>(), x => x.Id == "settings-card/email-updates.mark");
        Assert.NotEqual(ColorToken.Hex(0x00000000), checkedMarkFill.Color);
        Assert.Equal(ColorToken.Hex(0x00000000), uncheckedMarkFill.Color);
        Assert.Equal(StandardTheme.Default.Checkbox.Default.MarkColor, checkedMarkFill.Color);
        Assert.NotEqual(StandardTheme.Default.Checkbox.Default.BoxBackground, checkedMarkFill.Color);

        var uncheckedGeometry = Render(new DemoState(0, false, false));
        var checkedGeometry = Render(new DemoState(0, true, false));
        GeometryHarness.AssertSameRowIds(uncheckedGeometry, checkedGeometry);
    }

    [Fact]
    public void PresenterSample_EmailUpdatesChecked_HasVisibleCheckboxMark()
    {
        var frame = new MachinaRasterPipeline().Render(SettingsScreen.Build(new DemoState(0, true, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);

        var boxRect = frame.Resolved.Nodes["settings-card/email-updates.box"].Rect;
        var markRect = frame.Resolved.Nodes["settings-card/email-updates.mark"].Rect;
        var boxFill = Assert.Single(frame.RenderCommands.OfType<FillRectCommand>(), command => command.Id == "settings-card/email-updates.box");
        var markFill = Assert.Single(frame.RenderCommands.OfType<FillRectCommand>(), command => command.Id == "settings-card/email-updates.mark");

        Assert.True(markRect.Width > 0);
        Assert.True(markRect.Height > 0);
        Assert.True(markRect.X >= boxRect.X);
        Assert.True(markRect.Y >= boxRect.Y);
        Assert.True(markRect.X + markRect.Width <= boxRect.X + boxRect.Width);
        Assert.True(markRect.Y + markRect.Height <= boxRect.Y + boxRect.Height);
        Assert.NotEqual(ColorToken.Hex(0x00000000), markFill.Color);
        Assert.NotEqual(boxFill.Color, markFill.Color);
    }

    [Fact]
    public void PresenterSample_EmailUpdatesUnchecked_HidesCheckboxMark()
    {
        var frame = new MachinaRasterPipeline().Render(SettingsScreen.Build(new DemoState(0, false, false)), SettingsScreen.RootWidth, SettingsScreen.RootHeight);

        var markRect = frame.Resolved.Nodes["settings-card/email-updates.mark"].Rect;
        var markFill = Assert.Single(frame.RenderCommands.OfType<FillRectCommand>(), command => command.Id == "settings-card/email-updates.mark");

        Assert.True(markRect.Width > 0);
        Assert.True(markRect.Height > 0);
        Assert.Equal(ColorToken.Hex(0x00000000), markFill.Color);
    }

    private static void AssertVisibleText(IReadOnlyList<DrawTextCommand> commands, string text, string expectedId, TextSize expectedSize)
    {
        var command = Assert.Single(commands, x => x.Text == text);
        Assert.Equal(expectedId, command.Id);
        Assert.Equal(expectedSize, command.Style.Size);
        Assert.NotNull(command.Style.Color);
        Assert.NotEqual(ColorToken.Hex(0x00000000), command.Style.Color!.Value);
        Assert.NotEqual(ColorToken.Hex(0xFFFFFFFF), command.Style.Color!.Value);
    }

    private static void AssertRectInside(Machina.Layout.Geometry.Rect inner, Machina.Layout.Geometry.Rect outer, string id)
    {
        Assert.True(inner.X >= outer.X, $"{id} left outside");
        Assert.True(inner.Y >= outer.Y, $"{id} top outside");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width, $"{id} right outside");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height, $"{id} bottom outside");
    }

    private static DocumentGeometryResult Render(DemoState state, StandardTheme? theme = null)
    {
        var document = SettingsScreen.Build(state, theme ?? StandardTheme.Default);
        return GeometryHarness.ResolveDocument(document, SettingsScreen.RootWidth, SettingsScreen.RootHeight);
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
