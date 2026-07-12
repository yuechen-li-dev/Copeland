using Machina.Core.Nodes;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardComponentContractM5dTests
{
    [Fact]
    public void StandardUI_PrimaryComponents_ReturnUiNode()
    {
        UiNode card = StandardUI.Card(StandardUI.Label("inside"));
        UiNode button = StandardUI.Button("Save");
        UiNode input = StandardUI.Input();
        UiNode field = StandardUI.Field(StandardUI.Input(), label: "Email");
        UiNode checkbox = StandardUI.Checkbox(label: "Email updates");
        UiNode toggle = StandardUI.Switch(label: "Notifications");

        Assert.NotNull(card);
        Assert.NotNull(button);
        Assert.NotNull(input);
        Assert.NotNull(field);
        Assert.NotNull(checkbox);
        Assert.NotNull(toggle);
    }
}
