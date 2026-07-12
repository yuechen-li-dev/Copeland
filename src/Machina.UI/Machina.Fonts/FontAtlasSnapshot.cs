using System.Collections.ObjectModel;

namespace Machina.Fonts;

public sealed record FontAtlasSnapshot
{
    public FontAtlasSnapshot(
        long version,
        IReadOnlyList<FontAtlasPage> pages,
        IReadOnlyDictionary<GlyphKey, GlyphAtlasEntry> glyphs)
    {
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(glyphs);

        FontAtlasPage[] pageCopy = pages.ToArray();
        if (pageCopy.Any(page => page is null)) throw new ArgumentException("Pages must not contain null entries.", nameof(pages));

        Dictionary<GlyphKey, GlyphAtlasEntry> glyphCopy = new(glyphs.Count);
        foreach ((GlyphKey key, GlyphAtlasEntry entry) in glyphs)
        {
            glyphCopy.Add(key, entry ?? throw new ArgumentException("Glyph entries must not be null.", nameof(glyphs)));
        }

        Version = version;
        Pages = Array.AsReadOnly(pageCopy);
        Glyphs = new ReadOnlyDictionary<GlyphKey, GlyphAtlasEntry>(glyphCopy);
    }

    public long Version { get; }
    public IReadOnlyList<FontAtlasPage> Pages { get; }
    public IReadOnlyDictionary<GlyphKey, GlyphAtlasEntry> Glyphs { get; }

    public static FontAtlasSnapshot Empty { get; } = new(0, Array.Empty<FontAtlasPage>(), new Dictionary<GlyphKey, GlyphAtlasEntry>());
}
