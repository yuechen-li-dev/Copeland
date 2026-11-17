using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Machina.Standard.Widgets;
using Machina.Standard.Windowing;
using Machina.Presentation;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class PointerFeedbackTests
{
    [Theory]
    [InlineData(UiRole.Button, UiCursor.Hand)]
    [InlineData(UiRole.Input, UiCursor.IBeam)]
    [InlineData(UiRole.SpinButton, UiCursor.IBeam)]
    [InlineData(UiRole.Checkbox, UiCursor.Hand)]
    [InlineData(UiRole.Slider, UiCursor.ResizeHorizontal)]
    public void CursorPolicyFollowsControlIntent(UiRole role, UiCursor expected)
    {
        Assert.Equal(expected, UiCursorPolicy.Resolve(new(role)));
        Assert.Equal(UiCursor.Arrow, UiCursorPolicy.Resolve(new(role, Disabled: true)));
        Assert.Equal(UiCursor.Crosshair, UiCursorPolicy.Resolve(new(role) { Cursor = UiCursor.Crosshair }));
    }

    [Fact]
    public void LegacyActionFeedbackRepaintsWithoutChangingGeometryOrInvokingCommands()
    {
        var prepared = new MachinaPresentationPipeline().Prepare(UI.Surface(children:
        [StandardUI.Place(StandardUI.Button("Open", id: "button", action: UiAction.Named("Open")), 0, 0, 112, 36)]), 400, 200);
        var feedback = new UiPointerFeedback();
        feedback.Refresh(prepared.HitTest);
        var original = prepared.Lowering.Styles[new NodeId("button")];
        Assert.True(feedback.Move(new(10, 10)));
        Assert.Equal(UiCursor.Hand, feedback.Cursor);
        Assert.False(feedback.Move(new(11, 11)));
        var hover = MachinaPointerPresentation.Apply(prepared, feedback, ColorToken.White, ColorToken.Hex(0x000000FF));
        var originalFill = prepared.PresentationFrame.Operations.OfType<FillRectangleOperation>().Single(op => op.SourceId == "button");
        var hoverFill = hover.Operations.OfType<FillRectangleOperation>().Single(op => op.SourceId == "button");
        Assert.NotEqual(originalFill.Color, hoverFill.Color);
        Assert.Equal(originalFill.Rect, hoverFill.Rect);
        Assert.Equal(original, prepared.Lowering.Styles[new NodeId("button")]);
        feedback.Press(new(10, 10));
        Assert.True(feedback.IsPressed("button"));
        feedback.Move(new(300, 150));
        Assert.False(feedback.IsPressed("button"));
        feedback.Release(new(300, 150));
        Assert.Null(feedback.Pressed);
        Assert.Equal(UiCursor.Arrow, feedback.Cursor);
    }

    [Fact]
    public void WidgetHoverAndPressAreTransientAndDisabledControlsStayNeutral()
    {
        using var session = new WidgetSession();
        var pipeline = new MachinaWidgetPipeline();
        MachinaPreparedPresentation View(bool disabled = false) => pipeline.Prepare(session,
            widgets => widgets.Button("button", "Button", disabled: disabled), 400, 200);
        var idle = View().Lowering.Styles[new NodeId("button")];
        Assert.Empty(session.Handle(new UiPointerMoved(new(10, 10), null, default)).Changes);
        Assert.Equal(UiCursor.Hand, session.Cursor);
        var hover = View().Lowering.Styles[new NodeId("button")];
        Assert.NotEqual(idle.Background, hover.Background);
        Assert.Empty(session.Handle(new UiPointerButtonChanged(new(10, 10), UiPointerButton.Primary, true, default)).Changes);
        var press = View().Lowering.Styles[new NodeId("button")];
        Assert.NotEqual(hover.Background, press.Background);
        session.Handle(new UiPointerButtonChanged(new(300, 150), UiPointerButton.Primary, false, default));
        session.Handle(new UiFocusChanged(false));
        Assert.Equal(UiCursor.Arrow, session.Cursor);
        Assert.Equal(idle.Background, View().Lowering.Styles[new NodeId("button")].Background);
        View(true);
        session.Handle(new UiPointerMoved(new(10, 10), null, default));
        Assert.Equal(UiCursor.Arrow, session.Cursor);
        Assert.Equal(idle.Background, View(true).Lowering.Styles[new NodeId("button")].Background);
    }

    [Fact]
    public void TextSelectionKeepsIBeamOutsideTheFieldUntilRelease()
    {
        using var session = new WidgetSession();
        new MachinaWidgetPipeline().Prepare(session, w => w.Input("input", "Text", "Name"), 400, 200);
        session.Handle(new UiPointerMoved(new(10, 10), null, default));
        Assert.Equal(UiCursor.IBeam, session.Cursor);
        session.Handle(new UiPointerButtonChanged(new(10, 10), UiPointerButton.Primary, true, default));
        session.Handle(new UiPointerMoved(new(350, 150), null, default));
        Assert.Equal(UiCursor.IBeam, session.Cursor);
        session.Handle(new UiPointerButtonChanged(new(350, 150), UiPointerButton.Primary, false, default));
        Assert.Equal(UiCursor.Arrow, session.Cursor);
    }

    [Fact]
    public void StationaryPointerAndRemovedControlsReevaluateAgainstTheNewFrame()
    {
        using var session = new WidgetSession();
        var pipeline = new MachinaWidgetPipeline();
        pipeline.Prepare(session, w => w.Button("button", "Button"), 400, 200);
        session.Handle(new UiPointerMoved(new(10, 10), null, default));
        Assert.Equal(UiCursor.Hand, session.Cursor);
        pipeline.Prepare(session, w => w.Input("input", "Text", "Name"), 400, 200);
        Assert.Equal(UiCursor.IBeam, session.Cursor);
        pipeline.Prepare(session, w => UI.Rect(), 400, 200);
        Assert.Null(session.Hovered);
        Assert.Equal(UiCursor.Arrow, session.Cursor);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    [InlineData(100)]
    public void TitleBarDrawingAndHitGeometryAgreeAtEveryWidth(int width)
    {
        var layout = new WindowTitleBarLayout(width);
        var pipeline = new MachinaPresentationPipeline();
        var frame = pipeline.Prepare(WindowTitleBar.Create("titlebar", layout, "Valencia", false), width, 200);
        foreach (var button in new[] { WindowCaptionButton.Minimize, WindowCaptionButton.Maximize, WindowCaptionButton.Close })
        {
            var rect = layout.Button(button);
            Assert.Equal(button, layout.At(rect.X + rect.Width / 2, rect.Y + 16));
            Assert.Equal(rect, frame.Resolved.Nodes[new NodeId("titlebar").Child(button.ToString())].Rect);
            Assert.Equal("window." + button.ToString().ToLowerInvariant(),
                frame.HitTest.HitTest(new(rect.X + rect.Width / 2, 16))!.Action.Name);
        }
        Assert.Equal(WindowCaptionButton.None, layout.At(width, 16));
        Assert.Equal(WindowCaptionButton.None, layout.At(width - 10, 32));
    }
}
