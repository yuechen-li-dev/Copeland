using Xunit;
using Machina.Fonts;

namespace Machina.Fonts.Tests;

public sealed class FontAtlasRecordsTests
{
    [Fact]
    public void FontFaceId_TrimsAndRejectsEmpty()
    {
        Assert.Equal("Inter", new FontFaceId(" Inter ").Value);
        Assert.Throws<ArgumentException>(() => new FontFaceId(" "));
    }

    [Fact]
    public void GlyphKey_RejectsInvalidEmSize()
    {
        FontFaceId face = new("Fake");
        Assert.Throws<ArgumentOutOfRangeException>(() => GlyphKey.FromChar(face, 'A', 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlyphKey.FromCodepoint(face, 0xD800, 12));
    }

    [Fact]
    public void GlyphMetrics_RejectsNegativeDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphMetrics(1, 0, 0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphMetrics(1, 0, 0, 1, -1));
    }

    [Fact]
    public void GlyphAtlasEntry_RejectsInvalidRectOrUvs()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 12);
        GlyphMetrics metrics = new(8, 0, 10, 8, 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphAtlasEntry(key, 0, 0, 0, 0, 1, 0, 0, 1, 1, metrics));
        Assert.Throws<ArgumentException>(() => new GlyphAtlasEntry(key, 0, 0, 0, 1, 1, 1, 0, 0, 1, metrics));
    }

    [Fact]
    public void FontAtlasSnapshot_CopiesCollections()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 12);
        FontAtlasPage page = new(0, "fake.png", 64, 64, null);
        GlyphAtlasEntry entry = new(key, 0, 0, 0, 10, 10, 0, 0, 1, 1, new GlyphMetrics(8, 0, 10, 8, 12));
        List<FontAtlasPage> pages = [page];
        Dictionary<GlyphKey, GlyphAtlasEntry> glyphs = new() { [key] = entry };

        FontAtlasSnapshot snapshot = new(1, pages, glyphs);
        pages.Clear();
        glyphs.Clear();

        Assert.Single(snapshot.Pages);
        Assert.Single(snapshot.Glyphs);
    }

    [Fact]
    public void FontAtlasSnapshot_EmptyIsStable()
    {
        Assert.Same(FontAtlasSnapshot.Empty, FontAtlasSnapshot.Empty);
        Assert.Equal(0, FontAtlasSnapshot.Empty.Version);
        Assert.Empty(FontAtlasSnapshot.Empty.Pages);
        Assert.Empty(FontAtlasSnapshot.Empty.Glyphs);
    }
}
