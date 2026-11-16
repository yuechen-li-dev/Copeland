using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AurelianNativeUiFontTests
{
    private static readonly Lazy<AurelianNativeUiFont> Font = new(() => AurelianNativeUiFont.Create(
    [
        new(TextSize.Md, Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "NativeUI.ttf"), "native-ui-test", 12)
    ]));

    [Theory]
    [InlineData("UNDO")]
    [InlineData("REDO")]
    [InlineData("PLAY")]
    [InlineData("OUT")]
    [InlineData("VIEW / SEQUENCE")]
    [InlineData("   PLAY  ")]
    [InlineData(">>")]
    public void QualifiedMsdfCentersVisibleInkInsteadOfAdvanceOrLineBoxes(string label)
    {
        var rectangle = new Rect(70, 50, 240, 32);
        var operation = new PositionedTextOperation("arbitrary-identity", rectangle, label,
            new TextStyle(Size: TextSize.Md, AlignX: TextAlignX.Center, AlignY: TextAlignY.Center), ColorToken.White);

        var qualified = Font.Value.Qualify(operation);
        Assert.Equal(rectangle, qualified.Rect);
        var primitive = Assert.IsType<MachinaTextPresentationPrimitive>(qualified.Primitive);
        Assert.Equal(MachinaTextRenderingMode.Msdf, primitive.RenderingMode);
        var ink = Assert.Single(primitive.GlyphRun.Lines).InkBounds;
        Assert.NotNull(ink);
        Assert.Equal(rectangle.Width / 2, (ink.Left + ink.Right) / 2, 8);
        Assert.Equal(rectangle.Height / 2, (ink.Top + ink.Bottom) / 2, 8);
    }
}
