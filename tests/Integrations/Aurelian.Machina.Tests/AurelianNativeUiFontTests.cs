using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Aurelian.Graphics.Vulkan.Native2D;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AurelianNativeUiFontTests
{
    [Theory]
    [InlineData("S")]
    [InlineData("E")]
    [InlineData("Q")]
    public void ClippingPreservesNativeAtlasCoordinatesAtTheRemainingPixels(string character)
    {
        var operation = Font.Value.Qualify(new("clip-probe", new(0, 0, 80, 32), character,
            new TextStyle(Size: TextSize.Md), ColorToken.White));
        var resource = Font.Value.ResourceFor(operation);
        var textures = resource.Snapshot.Pages.ToDictionary(p => p.Index, _ => new Native2DTextureHandle(1));
        var whole = Assert.Single(AurelianGlyphRunAdapter.Adapt(operation.Primitive!.GlyphRun,
            resource.Snapshot, textures, Native2DTint.White, normalizeAtlasUv: true));
        var clip = whole.Destination with
        {
            Y = whole.Destination.Y + 2,
            Height = whole.Destination.Height - 3
        };
        var cropped = Assert.Single(AurelianGlyphRunAdapter.Adapt(operation.Primitive.GlyphRun,
            resource.Snapshot, textures, Native2DTint.White, clipRect: clip, normalizeAtlasUv: true));
        float unitsPerPixel = (whole.Uv.V1 - whole.Uv.V0) / whole.Destination.Height;
        Assert.Equal(whole.Uv.V0 + 2 * unitsPerPixel, cropped.Uv.V0, 6);
        Assert.Equal(whole.Uv.V1 - unitsPerPixel, cropped.Uv.V1, 6);
        Assert.Equal(clip, cropped.Destination);
    }

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
