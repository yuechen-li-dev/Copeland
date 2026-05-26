using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Xunit;

namespace Machina.Core.Tests;

public sealed class FlatAuthoringTests
{
    [Fact]
    public void UiDocumentLowerer_PreservesFlatRows()
    {
        var document = UiDocument.Create(
            [
                Row.Root("root", View.Rect(background: ColorToken.White)),
                Row.Absolute("panel", "root", 10, 20, 300, 160),
                Row.Anchor("title", "panel", left: 8, top: 8, width: 100, height: 20, view: View.Text("Hi"))
            ]);

        var lowered = UiDocumentLowerer.Lower(document);

        Assert.Equal(document.Rows.Count, lowered.Rows.Count);
        Assert.Equal(new[] { "root", "panel", "title" }, lowered.Rows.Select(x => x.Id.Value));
        Assert.Equal(new AbsoluteFrame(10, 20, 300, 160), lowered.Rows[1].Frame);
        Assert.Equal("panel", lowered.Rows[2].Parent!.Value);
    }

    [Fact]
    public void RowFixed_And_RowFill_CreateStackChildFrames()
    {
        Assert.IsType<FixedFrame>(Row.Fixed("fixed", "parent", height: 24).Frame);
        Assert.IsType<FillFrame>(Row.Fill("fill", "parent", weight: 2).Frame);
    }

    [Fact]
    public void ViewText_LowersTextMetadata()
    {
        var lowered = UiDocumentLowerer.Lower(UiDocument.Create([Row.Root("text", View.Text("Hello", size: TextSize.Sm))]));

        Assert.Equal(TextSize.Sm, lowered.TextStyles["text"].Size);
        Assert.Equal(UiRole.Text, lowered.Semantics["text"].Role);
        Assert.Equal("Hello", lowered.Semantics["text"].Label);
    }

    [Fact]
    public void UiDocumentLowerer_HostedComponent_EmitsHostAndScopedComponentRows()
    {
        var document = UiDocument.Create(
            [
                Row.Root("root"),
                Row.Anchor(
                    "settings-card",
                    "root",
                    left: 20,
                    top: 20,
                    width: 240,
                    height: 140,
                    component: UI.Column(
                        id: "column",
                        children:
                        [
                            UI.Text("Hello", id: "title"),
                            UI.Button("Increment", id: "increment", action: UiAction.Named("counter.increment"))
                        ]))
            ]);

        var lowered = UiDocumentLowerer.Lower(document);

        Assert.Contains(lowered.Rows, row => row.Id.Value == "settings-card");
        Assert.Contains(lowered.Rows, row => row.Id.Value == "settings-card/increment");
        Assert.Equal("counter.increment", lowered.Actions["settings-card/increment"].Name);
        Assert.Equal("Hello", lowered.Semantics["settings-card/title"].Label);
    }

    [Fact]
    public void HostedComponent_ExplicitIdsAreScoped()
    {
        var doc = UiDocument.Create(
            [
                Row.Root("root"),
                Row.Anchor("settings-card", "root", left: 0, top: 0, width: 100, height: 100, component: UI.Text("A", id: "increment"))
            ]);

        var lowered = UiDocumentLowerer.Lower(doc);
        Assert.Contains(lowered.Rows, row => row.Id.Value == "settings-card/increment");
    }

    [Fact]
    public void HostedComponent_GeneratedIdsAreStable()
    {
        UiNode component = UI.Column(children: [UI.Text("No id"), UI.Text("No id 2")]);
        var doc = UiDocument.Create([Row.Root("root"), Row.Anchor("host", "root", left: 0, top: 0, width: 100, height: 100, component: component)]);

        var first = UiDocumentLowerer.Lower(doc).Rows.Select(x => x.Id.Value).ToArray();
        var second = UiDocumentLowerer.Lower(doc).Rows.Select(x => x.Id.Value).ToArray();

        Assert.Equal(first, second);
    }
}
