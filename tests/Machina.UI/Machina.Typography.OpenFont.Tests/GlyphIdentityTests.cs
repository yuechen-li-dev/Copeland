using Typography.OpenFont;
using Xunit;

namespace Machina.Typography.OpenFont.Tests;

public sealed class GlyphIdentityTests
{
    [Fact]
    public void EmptyOutlineGlyph_PreservesMappedGlyphIdentityAndAdvance()
    {
        using FileStream stream = File.OpenRead(FontPath);
        Typeface typeface = new OpenFontReader().Read(stream);

        ushort spaceGlyphIndex = typeface.GetGlyphIndex(' ');
        Glyph spaceGlyph = typeface.GetGlyph(spaceGlyphIndex);

        Assert.Equal((ushort)556, spaceGlyphIndex);
        Assert.Equal(spaceGlyphIndex, spaceGlyph.GlyphIndex);
        Assert.Empty(spaceGlyph.GlyphPoints);
        Assert.Empty(spaceGlyph.EndPoints);
        Assert.Equal((ushort)229, typeface.GetAdvanceWidthFromGlyphIndex(spaceGlyph.GlyphIndex));
    }

    [Fact]
    public void EveryLoadedGlyph_PreservesItsRequestedIndex()
    {
        using FileStream stream = File.OpenRead(FontPath);
        Typeface typeface = new OpenFontReader().Read(stream);

        for (int index = 0; index < typeface.GlyphCount; index++)
        {
            Glyph glyph = typeface.GetGlyph((ushort)index);
            Assert.Equal((ushort)index, glyph.GlyphIndex);
        }
    }

    private static string FontPath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "Fonts",
        "CrimsonText-Regular.ttf");
}
