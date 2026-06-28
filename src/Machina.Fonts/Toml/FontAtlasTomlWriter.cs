using System.Globalization;
using System.Text;

namespace Machina.Fonts.Toml;

public static class FontAtlasTomlWriter
{
    public static string Write(FontAtlasTomlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        StringBuilder builder = new();
        WriteAtlas(builder, document.Atlas);
        WriteFont(builder, document.Font);
        WriteMetrics(builder, document.Metrics);
        WriteMsdf(builder, document.Msdf);

        foreach (FontAtlasPageToml page in document.Pages.OrderBy(page => page.Index))
        {
            WritePage(builder, page);
        }

        IEnumerable<FontAtlasGlyphToml> glyphs = document.Glyphs
            .OrderBy(glyph => document.Font.Face, StringComparer.Ordinal)
            .ThenBy(glyph => glyph.EmSize)
            .ThenBy(glyph => glyph.Weight)
            .ThenBy(glyph => glyph.Slant, StringComparer.Ordinal)
            .ThenBy(glyph => glyph.Codepoint)
            .ThenBy(glyph => glyph.Page)
            .ThenBy(glyph => glyph.X)
            .ThenBy(glyph => glyph.Y);

        foreach (FontAtlasGlyphToml glyph in glyphs)
        {
            WriteGlyph(builder, glyph);
        }

        return builder.ToString();
    }

    private static void WriteAtlas(StringBuilder builder, FontAtlasHeaderToml atlas)
    {
        builder.AppendLine("[atlas]");
        Write(builder, "format", atlas.Format);
        Write(builder, "kind", atlas.Kind);
        Write(builder, "name", atlas.Name);
        Write(builder, "distance_field", atlas.DistanceField);
        Write(builder, "version", atlas.Version);
        builder.AppendLine();
    }

    private static void WriteFont(StringBuilder builder, FontAtlasFontToml font)
    {
        builder.AppendLine("[font]");
        Write(builder, "face", font.Face);
        Write(builder, "family", font.Family);
        Write(builder, "style", font.Style);
        Write(builder, "source", font.Source);
        Write(builder, "source_hash", font.SourceHash);
        Write(builder, "license", font.License);
        builder.AppendLine();
    }

    private static void WriteMetrics(StringBuilder builder, FontAtlasMetricsToml metrics)
    {
        builder.AppendLine("[metrics]");
        Write(builder, "em_size", metrics.EmSize);
        Write(builder, "units_per_em", metrics.UnitsPerEm);
        Write(builder, "ascent", metrics.Ascent);
        Write(builder, "descent", metrics.Descent);
        Write(builder, "line_gap", metrics.LineGap);
        Write(builder, "line_height", metrics.LineHeight);
        builder.AppendLine();
    }

    private static void WriteMsdf(StringBuilder builder, FontAtlasMsdfToml msdf)
    {
        builder.AppendLine("[msdf]");
        Write(builder, "range", msdf.Range);
        Write(builder, "scale", msdf.Scale);
        Write(builder, "edge_coloring", msdf.EdgeColoring);
        Write(builder, "miter_limit", msdf.MiterLimit);
        builder.AppendLine();
    }

    private static void WritePage(StringBuilder builder, FontAtlasPageToml page)
    {
        builder.AppendLine("[[page]]");
        Write(builder, "index", page.Index);
        Write(builder, "image", page.Image);
        Write(builder, "width", page.Width);
        Write(builder, "height", page.Height);
        Write(builder, "content_hash", page.ContentHash);
        builder.AppendLine();
    }

    private static void WriteGlyph(StringBuilder builder, FontAtlasGlyphToml glyph)
    {
        builder.AppendLine("[[glyph]]");
        Write(builder, "codepoint", glyph.Codepoint);
        if (IsPrintableSingleScalar(glyph.Char))
        {
            Write(builder, "char", glyph.Char!);
        }

        Write(builder, "em_size", glyph.EmSize);
        Write(builder, "weight", glyph.Weight);
        Write(builder, "slant", glyph.Slant);
        Write(builder, "page", glyph.Page);
        Write(builder, "x", glyph.X);
        Write(builder, "y", glyph.Y);
        Write(builder, "width", glyph.Width);
        Write(builder, "height", glyph.Height);
        Write(builder, "advance", glyph.Advance);
        Write(builder, "bearing_x", glyph.BearingX);
        Write(builder, "bearing_y", glyph.BearingY);
        Write(builder, "u0", glyph.U0);
        Write(builder, "v0", glyph.V0);
        Write(builder, "u1", glyph.U1);
        Write(builder, "v1", glyph.V1);
        builder.AppendLine();
    }

    private static bool IsPrintableSingleScalar(string? value)
    {
        return value is { Length: 1 } && !char.IsControl(value[0]) && !char.IsSurrogate(value[0]);
    }

    private static void Write(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append(" = \"").Append(Escape(value)).AppendLine("\"");
    }

    private static void Write(StringBuilder builder, string key, int value)
    {
        builder.Append(key).Append(" = ").AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Write(StringBuilder builder, string key, long value)
    {
        builder.Append(key).Append(" = ").AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Write(StringBuilder builder, string key, double value)
    {
        builder.Append(key).Append(" = ").AppendLine(value.ToString("G17", CultureInfo.InvariantCulture));
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
