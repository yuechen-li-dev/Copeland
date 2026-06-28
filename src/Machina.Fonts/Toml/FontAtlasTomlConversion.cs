namespace Machina.Fonts.Toml;

public static class FontAtlasTomlConversion
{
    public static FontAtlasTomlDocument FromSnapshot(FontAtlasSnapshot snapshot, FontAtlasTomlExportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(metadata);

        string face = snapshot.Glyphs.Keys.Select(key => key.Face.Value).FirstOrDefault() ?? metadata.Name;
        return new FontAtlasTomlDocument
        {
            Atlas = new FontAtlasHeaderToml
            {
                Format = 1,
                Kind = "machina-font-atlas",
                Name = metadata.Name,
                DistanceField = metadata.DistanceField,
                Version = snapshot.Version,
            },
            Font = new FontAtlasFontToml
            {
                Face = face,
                Family = metadata.FontFamily,
                Style = metadata.FontStyle,
                Source = metadata.FontSource,
                SourceHash = metadata.FontSourceHash,
                License = metadata.License,
            },
            Metrics = metadata.Metrics,
            Msdf = metadata.Msdf,
            Pages = snapshot.Pages.Select(ToTomlPage).ToArray(),
            Glyphs = snapshot.Glyphs.Values.Select(ToTomlGlyph).ToArray(),
        };
    }

    public static FontAtlasSnapshot ToSnapshot(FontAtlasTomlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        FontAtlasPage[] pages = document.Pages
            .Select(page => new FontAtlasPage(page.Index, page.Image, page.Width, page.Height, page.ContentHash))
            .ToArray();

        Dictionary<GlyphKey, GlyphAtlasEntry> glyphs = [];
        foreach (FontAtlasGlyphToml glyph in document.Glyphs)
        {
            GlyphKey key = new(
                new FontFaceId(document.Font.Face),
                glyph.Codepoint,
                glyph.EmSize,
                (MachinaFontWeight)glyph.Weight,
                ParseSlant(glyph.Slant));

            GlyphMetrics metrics = new(glyph.Advance, glyph.BearingX, glyph.BearingY, glyph.Width, glyph.Height);
            glyphs.Add(
                key,
                new GlyphAtlasEntry(key, glyph.Page, glyph.X, glyph.Y, glyph.Width, glyph.Height, glyph.U0, glyph.V0, glyph.U1, glyph.V1, metrics));
        }

        return new FontAtlasSnapshot(document.Atlas.Version, pages, glyphs);
    }

    private static FontAtlasPageToml ToTomlPage(FontAtlasPage page)
    {
        return new FontAtlasPageToml
        {
            Index = page.Index,
            Image = page.ImagePath,
            Width = page.Width,
            Height = page.Height,
            ContentHash = page.ContentHash ?? string.Empty,
        };
    }

    private static FontAtlasGlyphToml ToTomlGlyph(GlyphAtlasEntry entry)
    {
        return new FontAtlasGlyphToml
        {
            Codepoint = entry.Key.Codepoint,
            Char = ToPrintableChar(entry.Key.Codepoint),
            EmSize = entry.Key.EmSize,
            Weight = (int)entry.Key.Weight,
            Slant = entry.Key.Slant.ToString().ToLowerInvariant(),
            Page = entry.PageIndex,
            X = entry.X,
            Y = entry.Y,
            Width = entry.Width,
            Height = entry.Height,
            Advance = entry.Metrics.Advance,
            BearingX = entry.Metrics.BearingX,
            BearingY = entry.Metrics.BearingY,
            U0 = entry.U0,
            V0 = entry.V0,
            U1 = entry.U1,
            V1 = entry.V1,
        };
    }

    private static string? ToPrintableChar(int codepoint)
    {
        if (codepoint < 0 || codepoint > char.MaxValue) return null;
        char value = (char)codepoint;
        if (char.IsControl(value) || char.IsSurrogate(value)) return null;
        return value.ToString();
    }

    private static MachinaFontSlant ParseSlant(string slant)
    {
        return FontAtlasTomlValidator.TryParseSlant(slant, out MachinaFontSlant result) ? result : MachinaFontSlant.Upright;
    }
}
