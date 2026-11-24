using Machina.Core.Authoring;
using Machina.Core.Semantics;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Presentation;
using Machina.Runtime.Input;
using Machina.Standard.Widgets;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class ContextMenuTests
{
    private static readonly NodeId Menu = new("context");
    private static readonly WidgetOption[] Items = [new("disabled", "Disabled", true), new("open", "Open"), new("copy", "Copy")];

    [Fact]
    public void PopoverFollowsAnEasedPathAndFlipsInsideTheViewport()
    {
        using var session = new WidgetSession();
        var pipeline = new MachinaWidgetPipeline();
        Rect Place(PointerPoint point, int milliseconds)
        {
            var frame = pipeline.PrepareOverlay(session,
                widgets => widgets.ContextMenu(Menu, "Actions", point, Items, elapsed: TimeSpan.FromMilliseconds(milliseconds)), 400, 300);
            Assert.DoesNotContain(frame.PresentationFrame.Operations, operation =>
                operation is FillRectangleOperation fill && fill.SourceId == "machina.widgets.surface");
            return frame.Resolved.Nodes[Menu.Child("popup")].Rect;
        }

        var start = Place(new(40, 60), 0);
        var midpoint = Place(new(40, 60), 80);
        var end = Place(new(40, 60), 160);
        Assert.Equal(12, start.Y - end.Y);
        Assert.Equal(1.5, midpoint.Y - end.Y);
        Assert.Equal((40d, 64d), (end.X, end.Y));
        var corner = Place(new(399, 299), 0);
        var settledCorner = Place(new(399, 299), 160);
        Assert.Equal(400, corner.X + corner.Width);
        Assert.True(corner.Y + corner.Height < settledCorner.Y + settledCorner.Height);
        Assert.True(settledCorner.Y + settledCorner.Height < 299);
    }

    [Fact]
    public void KeyboardAndOutsideClicksUseExistingMenuSemantics()
    {
        using var session = new WidgetSession();
        var pipeline = new MachinaWidgetPipeline();
        var frame = pipeline.Prepare(session, widgets =>
        {
            widgets.ContextMenu(Menu, "Actions", new(40, 50), Items);
            return UI.Surface(children: [widgets.Button("under", "Under")]);
        }, 400, 300);
        var submenu = Menu.Child("option");
        Assert.Equal(UiRole.Menu, frame.Lowering.Semantics[Menu.Child("popup")].Role);
        Assert.Equal(submenu.Child("open"), session.Focused);

        var disabled = frame.Resolved.Nodes[submenu.Child("disabled")].Rect;
        Assert.Empty(Click(new(disabled.X + 10, disabled.Y + 10)).Changes);
        session.Handle(new UiKeyChanged(UiKey.ArrowDown, true, false, UiModifiers.None));
        Assert.Equal(submenu.Child("copy"), session.Focused);
        var enter = session.Handle(new UiKeyChanged(UiKey.Enter, true, false, UiModifiers.None));
        Assert.Equal("copy", Assert.IsType<WidgetSelectionChanged>(enter.Changes[0]).Keys.Single());
        Assert.False(Assert.IsType<WidgetOpenChanged>(enter.Changes[1]).Open);
        Assert.False(Assert.IsType<WidgetOpenChanged>(Assert.Single(Click(new(10, 10)).Changes)).Open);
        Assert.False(Assert.IsType<WidgetOpenChanged>(Assert.Single(
            session.Handle(new UiKeyChanged(UiKey.Escape, true, false, UiModifiers.None)).Changes)).Open);

        WidgetInputResult Click(PointerPoint point)
        {
            Assert.True(session.Handle(new UiPointerButtonChanged(point, UiPointerButton.Primary, true, UiModifiers.None)).Handled);
            return session.Handle(new UiPointerButtonChanged(point, UiPointerButton.Primary, false, UiModifiers.None));
        }
    }
}
