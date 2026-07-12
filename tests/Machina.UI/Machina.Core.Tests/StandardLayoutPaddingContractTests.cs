using Machina.Layout.Rows;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Xunit;

namespace Machina.Core.Tests;

public sealed class StandardLayoutPaddingContractTests
{
    [Fact]
    public void RectStylePaddingDoesNotCreateExplicitLayoutInset()
    {
        var ui = UI.Rect(
            id: "shell",
            style: new Machina.Core.Styling.UiStyle(Padding: 16),
            child: UI.Text("content", id: "text"));

        var lowered = UiLowerer.Lower(ui);

        Assert.Contains(lowered.Rows, row => row.Id == new NodeId("text") && row.Parent == new NodeId("shell"));
        Assert.Equal(16, lowered.Styles[new NodeId("shell")].Padding);
    }
}
