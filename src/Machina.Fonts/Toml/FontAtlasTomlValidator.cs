using System.Text;

namespace Machina.Fonts.Toml;

public static class FontAtlasTomlValidator
{
    public static IReadOnlyList<FontAtlasTomlDiagnostic> Validate(FontAtlasTomlDocument document, string? path = null)
    {
        List<FontAtlasTomlDiagnostic> diagnostics = [];
        ValidateAtlas(document.Atlas, diagnostics, path);
        ValidateFont(document.Font, diagnostics, path);
        ValidateMetrics(document.Metrics, diagnostics, path);
        ValidateMsdf(document.Msdf, diagnostics, path);
        ValidatePages(document.Pages, diagnostics, path, out Dictionary<int, FontAtlasPageToml> pagesByIndex);
        ValidateGlyphs(document.Font.Face, document.Glyphs, pagesByIndex, diagnostics, path);
        return diagnostics;
    }

    private static void ValidateAtlas(FontAtlasHeaderToml atlas, List<FontAtlasTomlDiagnostic> diagnostics, string? path)
    {
        if (atlas.Format != 1) Add(diagnostics, FontAtlasTomlDiagnosticCode.UnsupportedFormat, "Atlas format must be 1.", path, "atlas.format");
        if (atlas.Kind != "machina-font-atlas") Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidKind, "Atlas kind must be machina-font-atlas.", path, "atlas.kind");
        RequireText(atlas.Name, diagnostics, path, "atlas.name");
        if (!TryParseDistanceField(atlas.DistanceField, out _)) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Distance field must be sdf, psdf, msdf, or mtsdf.", path, "atlas.distance_field");
        if (atlas.Version < 1) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Atlas version must be at least 1.", path, "atlas.version");
    }

    private static void ValidateFont(FontAtlasFontToml font, List<FontAtlasTomlDiagnostic> diagnostics, string? path)
    {
        RequireText(font.Face, diagnostics, path, "font.face");
        RequireText(font.Family, diagnostics, path, "font.family");
        RequireText(font.Style, diagnostics, path, "font.style");
        RequireText(font.Source, diagnostics, path, "font.source");
        RequireText(font.License, diagnostics, path, "font.license");
        RequireHash(font.SourceHash, diagnostics, path, "font.source_hash");

        try { _ = new FontFaceId(font.Face); }
        catch (Exception ex) when (ex is ArgumentException or ArgumentNullException)
        {
            Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Font face is not a valid FontFaceId.", path, "font.face");
        }
    }

    private static void ValidateMetrics(FontAtlasMetricsToml metrics, List<FontAtlasTomlDiagnostic> diagnostics, string? path)
    {
        PositiveFinite(metrics.EmSize, diagnostics, path, "metrics.em_size");
        if (metrics.UnitsPerEm <= 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Units per em must be greater than zero.", path, "metrics.units_per_em");
        Finite(metrics.Ascent, diagnostics, path, "metrics.ascent");
        Finite(metrics.Descent, diagnostics, path, "metrics.descent");
        Finite(metrics.LineGap, diagnostics, path, "metrics.line_gap");
        PositiveFinite(metrics.LineHeight, diagnostics, path, "metrics.line_height");
    }

    private static void ValidateMsdf(FontAtlasMsdfToml msdf, List<FontAtlasTomlDiagnostic> diagnostics, string? path)
    {
        PositiveFinite(msdf.Range, diagnostics, path, "msdf.range");
        PositiveFinite(msdf.Scale, diagnostics, path, "msdf.scale");
        RequireText(msdf.EdgeColoring, diagnostics, path, "msdf.edge_coloring");
        PositiveFinite(msdf.MiterLimit, diagnostics, path, "msdf.miter_limit");
    }

    private static void ValidatePages(IReadOnlyList<FontAtlasPageToml> pages, List<FontAtlasTomlDiagnostic> diagnostics, string? path, out Dictionary<int, FontAtlasPageToml> pagesByIndex)
    {
        pagesByIndex = [];
        for (int i = 0; i < pages.Count; i++)
        {
            FontAtlasPageToml page = pages[i];
            string key = $"page[{i}]";
            if (page.Index < 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Page index must be non-negative.", path, key + ".index");
            if (!pagesByIndex.TryAdd(page.Index, page)) Add(diagnostics, FontAtlasTomlDiagnosticCode.DuplicatePage, $"Duplicate page index {page.Index}.", path, key + ".index");
            RequireText(page.Image, diagnostics, path, key + ".image");
            if (page.Width <= 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Page width must be greater than zero.", path, key + ".width");
            if (page.Height <= 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Page height must be greater than zero.", path, key + ".height");
            RequireHash(page.ContentHash, diagnostics, path, key + ".content_hash");
        }
    }

    private static void ValidateGlyphs(string face, IReadOnlyList<FontAtlasGlyphToml> glyphs, IReadOnlyDictionary<int, FontAtlasPageToml> pagesByIndex, List<FontAtlasTomlDiagnostic> diagnostics, string? path)
    {
        HashSet<GlyphKey> keys = [];
        for (int i = 0; i < glyphs.Count; i++)
        {
            FontAtlasGlyphToml glyph = glyphs[i];
            string keyPath = $"glyph[{i}]";
            if (!TryBuildGlyphKey(face, glyph, out GlyphKey key))
            {
                Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidGlyphKey, "Glyph key fields are invalid.", path, keyPath);
            }
            else if (!keys.Add(key))
            {
                Add(diagnostics, FontAtlasTomlDiagnosticCode.DuplicateGlyph, $"Duplicate glyph key for codepoint {glyph.Codepoint}.", path, keyPath + ".codepoint");
            }

            ValidateChar(glyph, diagnostics, path, keyPath);
            ValidateGlyphNumbers(glyph, diagnostics, path, keyPath);
            ValidatePageReference(glyph, pagesByIndex, diagnostics, path, keyPath);
        }
    }

    private static bool TryBuildGlyphKey(string face, FontAtlasGlyphToml glyph, out GlyphKey key)
    {
        key = default;
        if (!Enum.IsDefined(typeof(MachinaFontWeight), glyph.Weight)) return false;
        if (!TryParseSlant(glyph.Slant, out MachinaFontSlant slant)) return false;
        if (!GlyphKey.IsValidCodepoint(glyph.Codepoint)) return false;
        if (!double.IsFinite(glyph.EmSize) || glyph.EmSize <= 0) return false;
        try
        {
            key = new GlyphKey(new FontFaceId(face), glyph.Codepoint, glyph.EmSize, (MachinaFontWeight)glyph.Weight, slant);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static void ValidateChar(FontAtlasGlyphToml glyph, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        if (glyph.Char is null) return;
        if (glyph.Char.Length != 1 || glyph.Char[0] != glyph.Codepoint)
        {
            Add(diagnostics, FontAtlasTomlDiagnosticCode.CharCodepointMismatch, "Glyph char does not match codepoint.", path, keyPath + ".char");
        }
    }

    private static void ValidateGlyphNumbers(FontAtlasGlyphToml glyph, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        if (glyph.X < 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Glyph x must be non-negative.", path, keyPath + ".x");
        if (glyph.Y < 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Glyph y must be non-negative.", path, keyPath + ".y");
        if (glyph.Width <= 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Glyph width must be greater than zero.", path, keyPath + ".width");
        if (glyph.Height <= 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Glyph height must be greater than zero.", path, keyPath + ".height");
        PositiveFinite(glyph.EmSize, diagnostics, path, keyPath + ".em_size");
        NonNegativeFinite(glyph.Advance, diagnostics, path, keyPath + ".advance");
        Finite(glyph.BearingX, diagnostics, path, keyPath + ".bearing_x");
        Finite(glyph.BearingY, diagnostics, path, keyPath + ".bearing_y");
        ValidateOrderedUv(glyph, diagnostics, path, keyPath);
    }

    private static void ValidatePageReference(FontAtlasGlyphToml glyph, IReadOnlyDictionary<int, FontAtlasPageToml> pagesByIndex, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        if (!pagesByIndex.TryGetValue(glyph.Page, out FontAtlasPageToml? page))
        {
            Add(diagnostics, FontAtlasTomlDiagnosticCode.MissingPage, $"Glyph references missing page {glyph.Page}.", path, keyPath + ".page");
            return;
        }

        if (glyph.X + glyph.Width > page.Width || glyph.Y + glyph.Height > page.Height)
        {
            Add(diagnostics, FontAtlasTomlDiagnosticCode.GlyphOutOfBounds, "Glyph rectangle is outside the referenced page bounds.", path, keyPath);
        }

        double expectedU1 = (glyph.X + glyph.Width) / (double)page.Width;
        double expectedV1 = (glyph.Y + glyph.Height) / (double)page.Height;
        double expectedU0 = glyph.X / (double)page.Width;
        double expectedV0 = glyph.Y / (double)page.Height;
        if (!Close(glyph.U0, expectedU0) || !Close(glyph.V0, expectedV0) || !Close(glyph.U1, expectedU1) || !Close(glyph.V1, expectedV1))
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Warning, FontAtlasTomlDiagnosticCode.UvMismatch, "Glyph UVs do not match page rectangle.", path, KeyPath: keyPath));
        }
    }

    private static void ValidateOrderedUv(FontAtlasGlyphToml glyph, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        Finite(glyph.U0, diagnostics, path, keyPath + ".u0");
        Finite(glyph.V0, diagnostics, path, keyPath + ".v0");
        Finite(glyph.U1, diagnostics, path, keyPath + ".u1");
        Finite(glyph.V1, diagnostics, path, keyPath + ".v1");
        if (glyph.U0 > glyph.U1 || glyph.V0 > glyph.V1)
        {
            Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Glyph UVs must be ordered.", path, keyPath);
        }
    }

    internal static bool TryParseSlant(string value, out MachinaFontSlant slant)
    {
        return Enum.TryParse(value, ignoreCase: true, out slant);
    }

    internal static bool TryParseDistanceField(string value, out Generation.DistanceFieldKind kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            kind = default;
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "sdf" => SetKind(Generation.DistanceFieldKind.Sdf, out kind),
            "psdf" => SetKind(Generation.DistanceFieldKind.Psdf, out kind),
            "msdf" => SetKind(Generation.DistanceFieldKind.Msdf, out kind),
            "mtsdf" => SetKind(Generation.DistanceFieldKind.Mtsdf, out kind),
            _ => SetKind(default, out kind, false),
        };
    }

    private static bool Close(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }

    private static void RequireText(string value, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(diagnostics, FontAtlasTomlDiagnosticCode.MissingRequiredField, "Required text field is missing or empty.", path, keyPath);
    }

    private static void RequireHash(string value, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(diagnostics, FontAtlasTomlDiagnosticCode.HashMissing, "Required hash field is missing or empty.", path, keyPath);
    }

    private static void PositiveFinite(double value, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        if (!double.IsFinite(value) || value <= 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Value must be finite and greater than zero.", path, keyPath);
    }

    private static void NonNegativeFinite(double value, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        if (!double.IsFinite(value) || value < 0) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Value must be finite and non-negative.", path, keyPath);
    }

    private static void Finite(double value, List<FontAtlasTomlDiagnostic> diagnostics, string? path, string keyPath)
    {
        if (!double.IsFinite(value)) Add(diagnostics, FontAtlasTomlDiagnosticCode.InvalidValue, "Value must be finite.", path, keyPath);
    }

    private static void Add(List<FontAtlasTomlDiagnostic> diagnostics, FontAtlasTomlDiagnosticCode code, string message, string? path, string keyPath)
    {
        diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, code, message, path, KeyPath: keyPath));
    }

    private static bool SetKind(Generation.DistanceFieldKind value, out Generation.DistanceFieldKind kind, bool result = true)
    {
        kind = value;
        return result;
    }
}
