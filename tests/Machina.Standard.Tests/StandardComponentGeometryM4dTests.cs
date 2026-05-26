using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardComponentGeometryM4dTests
{
    [Fact]
    public void StandardButton_ResolvesShellAndCenteredLabelInsideHost()
    {
        var lowered = LowerHostedComponent(StandardUI.Button("Increment", id: "increment", action: UiAction.Named("inc")), 180, 30);
        var resolved = Resolve(lowered, 300, 120);

        var hostRect = GetRect(resolved, "host");
        var shellRect = GetRect(resolved, "host/increment");
        var labelRegionRect = GetRect(resolved, "host/increment.label-region");

        Assert.Equal(hostRect, shellRect);
        Assert.Equal(shellRect, labelRegionRect);

        var textStyle = lowered.TextStyles[new NodeId("host/increment.label")];
        Assert.Equal(TextAlignX.Center, textStyle.AlignX);
        Assert.Equal(TextAlignY.Center, textStyle.AlignY);

        var actionNode = lowered.Actions.Single(pair => pair.Value.Name == "inc").Key;
        Assert.Equal("host/increment", actionNode.Value);
    }

    [Fact]
    public void StandardButton_DoesNotDependOnStylePaddingForLabelGeometry()
    {
        var lowered = LowerHostedComponent(StandardUI.Button("Increment", id: "increment", action: UiAction.Named("inc")), 180, 30);
        var shellStyle = lowered.Styles[new NodeId("host/increment")];

        Assert.Equal(0, shellStyle.Padding);
        Assert.Contains(lowered.Rows, row => row.Id.Value == "host/increment.label-region");
        Assert.Contains(lowered.Rows, row => row.Id.Value == "host/increment.label");
    }

    [Fact]
    public void StandardCheckbox_ResolvesBoxMarkAndLabelInsideHost()
    {
        var checkedLowered = LowerHostedComponent(StandardUI.Checkbox(id: "email", label: "Email", isChecked: true, changed: UiAction.Named("toggle")), 220, 40);
        var uncheckedLowered = LowerHostedComponent(StandardUI.Checkbox(id: "email", label: "Email", isChecked: false, changed: UiAction.Named("toggle")), 220, 40);
        var checkedResolved = Resolve(checkedLowered, 300, 120);

        Assert.Equal(GetRect(checkedResolved, "host"), GetRect(checkedResolved, "host/email"));
        Assert.Equal(18, GetRect(checkedResolved, "host/email.box").Width);
        Assert.Equal(18, GetRect(checkedResolved, "host/email.box").Height);
        Assert.True(GetRect(checkedResolved, "host/email.label").X > (GetRect(checkedResolved, "host/email.box").X + GetRect(checkedResolved, "host/email.box").Width));
        Assert.Contains(checkedLowered.Rows, row => row.Id.Value == "host/email.mark");
        Assert.Contains(uncheckedLowered.Rows, row => row.Id.Value == "host/email.mark");
        Assert.Contains(checkedLowered.Actions, pair => pair.Value.Name == "toggle" && pair.Key.Value == "host/email");
    }

    [Fact]
    public void StandardCheckbox_CheckedAndUnchecked_LabelRectIsStable()
    {
        var uncheckedLowered = LowerHostedComponent(StandardUI.Checkbox(id: "email", label: "Email updates", isChecked: false, changed: UiAction.Named("toggle")), 240, 40);
        var checkedLowered = LowerHostedComponent(StandardUI.Checkbox(id: "email", label: "Email updates", isChecked: true, changed: UiAction.Named("toggle")), 240, 40);
        var uncheckedResolved = Resolve(uncheckedLowered, 320, 120);
        var checkedResolved = Resolve(checkedLowered, 320, 120);

        var uncheckedLabel = GetRect(uncheckedResolved, "host/email.label");
        var checkedLabel = GetRect(checkedResolved, "host/email.label");

        Assert.Equal(uncheckedLabel, checkedLabel);
    }

    [Fact]
    public void StandardCheckbox_CheckedAndUnchecked_RowShapeIsStable()
    {
        var uncheckedLowered = LowerHostedComponent(StandardUI.Checkbox(id: "email", label: "Email updates", isChecked: false, changed: UiAction.Named("toggle")), 240, 40);
        var checkedLowered = LowerHostedComponent(StandardUI.Checkbox(id: "email", label: "Email updates", isChecked: true, changed: UiAction.Named("toggle")), 240, 40);

        var uncheckedIds = uncheckedLowered.Rows.Select(row => row.Id.Value).OrderBy(value => value).ToArray();
        var checkedIds = checkedLowered.Rows.Select(row => row.Id.Value).OrderBy(value => value).ToArray();

        Assert.Equal(uncheckedIds, checkedIds);
        Assert.Contains(uncheckedIds, id => id == "host/email.mark");
    }

    [Fact]
    public void StandardCheckbox_CheckedMark_IsCenteredGeometry()
    {
        var checkedLowered = LowerHostedComponent(StandardUI.Checkbox(id: "email", label: "Email", isChecked: true, changed: UiAction.Named("toggle")), 220, 40);
        var checkedResolved = Resolve(checkedLowered, 300, 120);

        var box = GetRect(checkedResolved, "host/email.box");
        var mark = GetRect(checkedResolved, "host/email.mark");

        Assert.Equal(18, box.Width);
        Assert.Equal(18, box.Height);
        Assert.Equal(10, mark.Width);
        Assert.Equal(10, mark.Height);
        Assert.Equal(box.X + (box.Width / 2), mark.X + (mark.Width / 2));
        Assert.Equal(box.Y + (box.Height / 2), mark.Y + (mark.Height / 2));
    }

    [Fact]
    public void StandardSwitch_ResolvesTrackThumbAndLabelInsideHost()
    {
        var onLowered = LowerHostedComponent(StandardUI.Switch(id: "notifications", label: "Notifications", isOn: true, changed: UiAction.Named("toggle")), 260, 40);
        var offLowered = LowerHostedComponent(StandardUI.Switch(id: "notifications", label: "Notifications", isOn: false, changed: UiAction.Named("toggle")), 260, 40);
        var onResolved = Resolve(onLowered, 360, 120);
        var offResolved = Resolve(offLowered, 360, 120);

        var track = GetRect(onResolved, "host/notifications.track");
        Assert.Equal(42, track.Width);
        Assert.Equal(20, track.Height);

        var onThumb = GetRect(onResolved, "host/notifications.thumb");
        var offThumb = GetRect(offResolved, "host/notifications.thumb");
        Assert.True(onThumb.X > offThumb.X);
        Assert.True(onThumb.X >= track.X && (onThumb.X + onThumb.Width) <= (track.X + track.Width));
        Assert.True(GetRect(onResolved, "host/notifications.label").X > (track.X + track.Width));
        Assert.Contains(onLowered.Actions, pair => pair.Value.Name == "toggle" && pair.Key.Value == "host/notifications");
    }

    [Fact]
    public void StandardSwitch_OnAndOff_LabelRectStable_ThumbMovesOnly()
    {
        var offLowered = LowerHostedComponent(StandardUI.Switch(id: "notifications", label: "Notifications", isOn: false, changed: UiAction.Named("toggle")), 260, 40);
        var onLowered = LowerHostedComponent(StandardUI.Switch(id: "notifications", label: "Notifications", isOn: true, changed: UiAction.Named("toggle")), 260, 40);
        var offResolved = Resolve(offLowered, 360, 120);
        var onResolved = Resolve(onLowered, 360, 120);

        Assert.Equal(GetRect(offResolved, "host/notifications.label"), GetRect(onResolved, "host/notifications.label"));
        Assert.Equal(GetRect(offResolved, "host/notifications.track"), GetRect(onResolved, "host/notifications.track"));

        var offThumb = GetRect(offResolved, "host/notifications.thumb");
        var onThumb = GetRect(onResolved, "host/notifications.thumb");
        Assert.Equal(offThumb.Y, onThumb.Y);
        Assert.Equal(offThumb.Width, onThumb.Width);
        Assert.Equal(offThumb.Height, onThumb.Height);
        Assert.True(onThumb.X > offThumb.X);

        var offIds = offLowered.Rows.Select(row => row.Id.Value).OrderBy(value => value).ToArray();
        var onIds = onLowered.Rows.Select(row => row.Id.Value).OrderBy(value => value).ToArray();
        Assert.Equal(offIds, onIds);
    }

    [Fact]
    public void HostedSettingsCard_HeadlessGeometryMatchesExpectedRows()
    {
        var lowered = UiDocumentLowerer.Lower(HostedComponentLayoutAuditTestsAccessor.BuildHostedSettingsCard(false, true));
        var resolved = Resolve(lowered, 640, 360);
        var card = GetRect(resolved, "settings-card");

        foreach (var id in new[]
                 {
                     "settings-card/increment",
                     "settings-card/increment.label-region",
                     "settings-card/email-updates.box",
                     "settings-card/email-updates.label",
                     "settings-card/notifications.track",
                     "settings-card/notifications.thumb",
                     "settings-card/notifications.label",
                 })
        {
            var rect = GetRect(resolved, id);
            Assert.True(rect.X >= card.X && rect.Y >= card.Y);
            Assert.True((rect.X + rect.Width) <= (card.X + card.Width) && (rect.Y + rect.Height) <= (card.Y + card.Height));
        }
    }

    private static UiLoweringResult LowerHostedComponent(UiNode component, double width, double height)
    {
        return UiDocumentLowerer.Lower(UiDocument.Create([
            Row.Root("root"),
            Row.Anchor("host", "root", left: 20, top: 20, width: width, height: height, component: component),
        ]));
    }

    private static Machina.Layout.Documents.ResolvedLayoutDocument Resolve(UiLoweringResult lowered, int width, int height)
    {
        var compiled = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(compiled, new Rect(0, 0, width, height));
    }

    private static Rect GetRect(Machina.Layout.Documents.ResolvedLayoutDocument resolved, string id)
    {
        return resolved.Nodes[new NodeId(id)].Rect;
    }
}
