using Machina.Core.Actions;
using Machina.Core.Flat;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardViewFlatTests
{
    [Fact]
    public void StandardViewButton_LowersActionAndSemantics()
    {
        var lowered = UiDocumentLowerer.Lower(UiDocument.Create([Row.Root("button", StandardView.Button("Save", UiAction.Named("save")))]));

        Assert.Equal("save", lowered.Actions["button"].Name);
        Assert.Equal(UiRole.Button, lowered.Semantics["button"].Role);
    }

    [Fact]
    public void StandardView_ButtonAndBadge_TextIsCenterAligned()
    {
        var button = StandardView.Button("Save");
        var badge = StandardView.Badge("Beta");

        Assert.NotNull(button.TextStyle);
        Assert.Equal(TextAlignX.Center, button.TextStyle!.AlignX);
        Assert.Equal(TextAlignY.Center, button.TextStyle.AlignY);

        Assert.NotNull(badge.TextStyle);
        Assert.Equal(TextAlignX.Center, badge.TextStyle!.AlignX);
        Assert.Equal(TextAlignY.Center, badge.TextStyle.AlignY);
    }

    [Fact]
    public void StandardView_ComponentCoverage_ProvidesExpectedRoles()
    {
        var document = UiDocument.Create(
            [
                Row.Root("card", StandardView.Card()),
                Row.Anchor("text", "card", left: 0, top: 0, width: 80, height: 20, view: StandardView.Text("Hello")),
                Row.Anchor("checkbox", "card", left: 0, top: 22, width: 80, height: 20, view: StandardView.Checkbox("Email", true, UiAction.Named("email.toggle"))),
                Row.Anchor("switch", "card", left: 0, top: 44, width: 80, height: 20, view: StandardView.Switch("Notifications", false, UiAction.Named("notifications.toggle"))),
                Row.Anchor("label", "card", left: 0, top: 66, width: 80, height: 20, view: StandardView.Label("Name")),
                Row.Anchor("badge", "card", left: 0, top: 88, width: 80, height: 20, view: StandardView.Badge("Beta")),
                Row.Anchor("separator", "card", left: 0, top: 110, width: 80, height: 1, view: StandardView.Separator()),
                Row.Anchor("input", "card", left: 0, top: 113, width: 120, height: 24, view: StandardView.Input("Alice", label: "Name", action: UiAction.Named("name.edit")))
            ]);

        var lowered = UiDocumentLowerer.Lower(document);

        Assert.Equal(UiRole.Container, lowered.Semantics["card"].Role);
        Assert.Equal(UiRole.Text, lowered.Semantics["text"].Role);
        Assert.Equal(UiRole.Checkbox, lowered.Semantics["checkbox"].Role);
        Assert.Equal(UiRole.Switch, lowered.Semantics["switch"].Role);
        Assert.Equal(UiRole.Label, lowered.Semantics["label"].Role);
        Assert.Equal(UiRole.Label, lowered.Semantics["badge"].Role);
        Assert.Equal(UiRole.Container, lowered.Semantics["separator"].Role);
        Assert.Equal(UiRole.Input, lowered.Semantics["input"].Role);
        Assert.Equal("name.edit", lowered.Actions["input"].Name);
    }
}
