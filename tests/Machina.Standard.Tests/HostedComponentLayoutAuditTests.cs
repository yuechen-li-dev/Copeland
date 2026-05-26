using Machina.Layout.Rows;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Diagnostics;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Layout.Compilation;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class HostedComponentLayoutAuditTests
{
    [Fact]
    public void HostedSettingsCard_LoweredRows_MatchFrameStackReferenceShape()
    {
        var lowered = UiDocumentLowerer.Lower(BuildHostedSettingsCard(false, true));

        var host = Assert.Single(lowered.Rows, x => x.Id.Value == "settings-card");
        Assert.IsType<AnchorFrame>(host.Frame);

        var componentRoot = Assert.Single(lowered.Rows, x => x.Id.Value == "settings-card/settings-card-content");
        Assert.Equal("settings-card", componentRoot.Parent!.Value);
        Assert.Equal(new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0), componentRoot.Frame);

        var column = Assert.Single(lowered.Rows, x => x.Id.Value == "settings-card/settings-card-column");
        Assert.Equal("settings-card/settings-card-content", column.Parent!.Value);
        var arrange = Assert.IsType<StackArrange>(column.Arrange);
        Assert.Equal(StackAxis.Vertical, arrange.Axis);

        var columnChildren = lowered.Rows.Where(x => x.Parent?.Value == column.Id.Value).ToArray();
        Assert.NotEmpty(columnChildren);
        foreach (var child in columnChildren)
        {
            Assert.True(child.Frame is FixedFrame or FillFrame, $"Child '{child.Id}' should be FixedFrame or FillFrame under stack.");
        }

        var snapshot = UiLoweringSnapshotWriter.Write(lowered);
        Assert.Contains("settings-card/settings-card-column", snapshot);
    }

    [Fact]
    public void HostedSettingsCard_ResolvesInsideHostRect()
    {
        var lowered = UiDocumentLowerer.Lower(BuildHostedSettingsCard(false, true));
        var compiled = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(compiled, new Rect(0, 0, 640, 360));

        var cardRect = resolved.Nodes[new NodeId("settings-card")].Rect;
        foreach (var id in new[]
                 {
                     "settings-card/title",
                     "settings-card/count",
                     "settings-card/increment",
                     "settings-card/email-updates",
                     "settings-card/notifications"
                 })
        {
            var rect = resolved.Nodes[new NodeId(id)].Rect;
            Assert.True(rect.X >= cardRect.X && rect.Y >= cardRect.Y, $"{id} starts outside card");
            Assert.True(rect.X + rect.Width <= cardRect.X + cardRect.Width, $"{id} extends outside card width");
            Assert.True(rect.Y + rect.Height <= cardRect.Y + cardRect.Height, $"{id} extends outside card height");
            Assert.True(rect.Width >= 0 && rect.Height >= 0, $"{id} has negative size");
        }
    }

    private static UiDocument BuildHostedSettingsCard(bool notifications, bool emailUpdates)
    {
        return UiDocument.Create(
            [
                Row.Root("root"),
                Row.Anchor(
                    id: "settings-card",
                    parent: "root",
                    left: 72,
                    top: 24,
                    width: 500,
                    height: 292,
                    component: StandardUI.Card(
                        id: "settings-card-content",
                        child: UI.Column(
                            id: "settings-card-column",
                            gap: 10,
                            children:
                            [
                                UI.Text("Machina Presenter", id: "title"),
                                UI.Text("Count: 0", id: "count"),
                                StandardUI.Button("Increment", id: "increment", action: UiAction.Named("counter.increment")),
                                StandardUI.Checkbox(id: "email-updates", label: "Email updates", isChecked: emailUpdates, changed: UiAction.Named("settings.emailUpdates.toggle")),
                                StandardUI.Switch(id: "notifications", label: "Notifications", isOn: notifications, changed: UiAction.Named("settings.notifications.toggle"))
                            ])))
            ]);
    }
}
