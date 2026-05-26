using Machina.Core.Actions;
using Machina.Core.Flat;
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
}
