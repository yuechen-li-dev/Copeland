using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Layout.Compilation;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardLayoutPaddingHardeningTests
{
    [Fact]
    public void StandardCard_ContentIsInsetByLayoutRegion()
    {
        var theme = StandardTheme.Default;
        var lowered = UiDocumentLowerer.Lower(UiDocument.Create([
            Row.Root("root"),
            Row.Anchor(
                id: "card",
                parent: "root",
                left: 40,
                top: 20,
                width: 300,
                height: 140,
                component: StandardUI.Card(
                    id: "card-shell",
                    child: UI.Text("Hello", id: "card-text"),
                    theme: theme))
        ]));

        var compiled = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(compiled, new Rect(0, 0, 640, 360));

        var shellRect = resolved.Nodes[new NodeId("card")].Rect;
        var contentRect = resolved.Nodes[new NodeId("card/card-shell.content")].Rect;
        var textRect = resolved.Nodes[new NodeId("card/card-text")].Rect;

        Assert.Equal(40, shellRect.X);
        Assert.Equal(20, shellRect.Y);
        Assert.Equal(300, shellRect.Width);
        Assert.Equal(140, shellRect.Height);

        var inset = theme.Spacing.Sm;
        Assert.Equal(shellRect.X + inset, contentRect.X);
        Assert.Equal(shellRect.Y + inset, contentRect.Y);
        Assert.Equal(shellRect.Width - inset * 2, contentRect.Width);
        Assert.Equal(shellRect.Height - inset * 2, contentRect.Height);

        Assert.True(textRect.X >= contentRect.X);
        Assert.True(textRect.Y >= contentRect.Y);
    }

    [Fact]
    public void HostedSettingsCard_ComponentRowsStayInsideCardContent()
    {
        var lowered = UiDocumentLowerer.Lower(HostedComponentLayoutAuditTestsAccessor.BuildHostedSettingsCard(false, true));
        var compiled = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(compiled, new Rect(0, 0, 640, 360));

        var cardRect = resolved.Nodes[new NodeId("settings-card")].Rect;
        var contentRect = resolved.Nodes[new NodeId("settings-card/settings-card-content.content")].Rect;

        Assert.True(contentRect.X > cardRect.X);
        Assert.True(contentRect.Y > cardRect.Y);

        foreach (var id in new[] { "settings-card/title", "settings-card/count", "settings-card/increment", "settings-card/email-updates", "settings-card/notifications" })
        {
            var rect = resolved.Nodes[new NodeId(id)].Rect;
            Assert.True(rect.X >= contentRect.X);
            Assert.True(rect.Y >= contentRect.Y);
            Assert.True(rect.Width >= 0);
            Assert.True(rect.Height >= 0);
        }
    }

    [Fact]
    public void StandardInput_TextOrPlaceholderIsInset()
    {
        var lowered = UiDocumentLowerer.Lower(UiDocument.Create([
            Row.Root("root"),
            Row.Anchor(
                id: "input-host",
                parent: "root",
                left: 10,
                top: 10,
                width: 240,
                height: 40,
                component: StandardUI.Input(id: "input", placeholder: "Name"))
        ]));

        var compiled = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(compiled, new Rect(0, 0, 320, 120));

        var shellRect = resolved.Nodes[new NodeId("input-host")].Rect;
        var contentRect = resolved.Nodes[new NodeId("input-host/input.content")].Rect;
        var textRect = resolved.Nodes[new NodeId("input-host/input.text")].Rect;

        Assert.True(contentRect.X > shellRect.X);
        Assert.True(contentRect.Y > shellRect.Y);
        Assert.True(textRect.X >= contentRect.X);
        Assert.True(textRect.Y >= contentRect.Y);
    }

    [Fact]
    public void StandardCheckbox_LocalLayoutResolvesInsideHost()
    {
        var lowered = UiDocumentLowerer.Lower(UiDocument.Create([
            Row.Root("root"),
            Row.Anchor("checkbox-host", "root", left: 20, top: 20, width: 220, height: 40, component: StandardUI.Checkbox(id: "checkbox", label: "Email updates", changed: UiAction.Named("toggle")))
        ]));

        var compiled = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(compiled, new Rect(0, 0, 400, 120));

        var host = resolved.Nodes[new NodeId("checkbox-host")].Rect;
        var box = resolved.Nodes[new NodeId("checkbox-host/checkbox.box")].Rect;
        var label = resolved.Nodes[new NodeId("checkbox-host/checkbox.label")].Rect;

        Assert.True(box.X >= host.X && box.Y >= host.Y);
        Assert.True(label.X > host.X);
        Assert.True(label.Y >= host.Y);
        Assert.Contains(lowered.Actions, pair => pair.Value.Name == "toggle");
    }

    [Fact]
    public void StandardSwitch_LocalLayoutResolvesInsideHost()
    {
        var onLowered = UiDocumentLowerer.Lower(UiDocument.Create([
            Row.Root("root"),
            Row.Anchor("switch-host", "root", left: 20, top: 20, width: 240, height: 40, component: StandardUI.Switch(id: "switch", label: "Notifications", isOn: true, changed: UiAction.Named("toggle")))
        ]));
        var offLowered = UiDocumentLowerer.Lower(UiDocument.Create([
            Row.Root("root"),
            Row.Anchor("switch-host", "root", left: 20, top: 20, width: 240, height: 40, component: StandardUI.Switch(id: "switch", label: "Notifications", isOn: false, changed: UiAction.Named("toggle")))
        ]));

        var onResolved = LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(onLowered.Rows), new Rect(0, 0, 400, 120));
        var offResolved = LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(offLowered.Rows), new Rect(0, 0, 400, 120));

        var host = onResolved.Nodes[new NodeId("switch-host")].Rect;
        var track = onResolved.Nodes[new NodeId("switch-host/switch.track")].Rect;
        var label = onResolved.Nodes[new NodeId("switch-host/switch.label")].Rect;
        var onThumb = onResolved.Nodes[new NodeId("switch-host/switch.thumb")].Rect;
        var offThumb = offResolved.Nodes[new NodeId("switch-host/switch.thumb")].Rect;

        Assert.True(track.X >= host.X && track.Y >= host.Y);
        Assert.True(label.X > host.X);
        Assert.NotEqual(onThumb.X, offThumb.X);
        Assert.Contains(onLowered.Actions, pair => pair.Value.Name == "toggle");
    }
}

internal static class HostedComponentLayoutAuditTestsAccessor
{
    public static UiDocument BuildHostedSettingsCard(bool notifications, bool emailUpdates)
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
