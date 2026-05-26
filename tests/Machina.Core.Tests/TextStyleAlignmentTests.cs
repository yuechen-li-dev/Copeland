using Machina.Core.Flat;
using Machina.Core.Styling;
using Xunit;

namespace Machina.Core.Tests;

public sealed class TextStyleAlignmentTests
{
    [Fact]
    public void TextStyle_DefaultsToLeftTopAlignment()
    {
        var style = new TextStyle();

        Assert.Equal(TextAlignX.Left, style.AlignX);
        Assert.Equal(TextAlignY.Top, style.AlignY);
    }

    [Fact]
    public void TextStyle_WithExpressionCanOverrideAlignment()
    {
        var style = new TextStyle() with
        {
            AlignX = TextAlignX.Center,
            AlignY = TextAlignY.Bottom,
        };

        Assert.Equal(TextAlignX.Center, style.AlignX);
        Assert.Equal(TextAlignY.Bottom, style.AlignY);
    }

    [Fact]
    public void ViewText_PropagatesAlignmentMetadata()
    {
        var view = View.Text("Hello", alignX: TextAlignX.Right, alignY: TextAlignY.Center);

        Assert.NotNull(view.TextStyle);
        Assert.Equal(TextAlignX.Right, view.TextStyle!.AlignX);
        Assert.Equal(TextAlignY.Center, view.TextStyle.AlignY);
    }
}
