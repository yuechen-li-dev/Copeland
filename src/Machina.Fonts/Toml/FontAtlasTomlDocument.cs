using Tomlyn.Serialization;

namespace Machina.Fonts.Toml;

public sealed record FontAtlasTomlDocument
{
    [TomlPropertyName("atlas")]
    public FontAtlasHeaderToml Atlas { get; init; } = new();

    [TomlPropertyName("font")]
    public FontAtlasFontToml Font { get; init; } = new();

    [TomlPropertyName("metrics")]
    public FontAtlasMetricsToml Metrics { get; init; } = new();

    [TomlPropertyName("msdf")]
    public FontAtlasMsdfToml Msdf { get; init; } = new();

    [TomlPropertyName("page")]
    public IReadOnlyList<FontAtlasPageToml> Pages { get; init; } = Array.Empty<FontAtlasPageToml>();

    [TomlPropertyName("glyph")]
    public IReadOnlyList<FontAtlasGlyphToml> Glyphs { get; init; } = Array.Empty<FontAtlasGlyphToml>();
}

public sealed record FontAtlasHeaderToml
{
    [TomlPropertyName("format")]
    public int Format { get; init; }

    [TomlPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [TomlPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [TomlPropertyName("distance_field")]
    public string DistanceField { get; init; } = string.Empty;

    [TomlPropertyName("version")]
    public long Version { get; init; }
}

public sealed record FontAtlasFontToml
{
    [TomlPropertyName("face")]
    public string Face { get; init; } = string.Empty;

    [TomlPropertyName("family")]
    public string Family { get; init; } = string.Empty;

    [TomlPropertyName("style")]
    public string Style { get; init; } = string.Empty;

    [TomlPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [TomlPropertyName("source_hash")]
    public string SourceHash { get; init; } = string.Empty;

    [TomlPropertyName("license")]
    public string License { get; init; } = string.Empty;
}

public sealed record FontAtlasMetricsToml
{
    [TomlPropertyName("em_size")]
    public double EmSize { get; init; }

    [TomlPropertyName("units_per_em")]
    public int UnitsPerEm { get; init; }

    [TomlPropertyName("ascent")]
    public double Ascent { get; init; }

    [TomlPropertyName("descent")]
    public double Descent { get; init; }

    [TomlPropertyName("line_gap")]
    public double LineGap { get; init; }

    [TomlPropertyName("line_height")]
    public double LineHeight { get; init; }
}

public sealed record FontAtlasMsdfToml
{
    [TomlPropertyName("range")]
    public double Range { get; init; }

    [TomlPropertyName("scale")]
    public double Scale { get; init; }

    [TomlPropertyName("edge_coloring")]
    public string EdgeColoring { get; init; } = string.Empty;

    [TomlPropertyName("miter_limit")]
    public double MiterLimit { get; init; }
}

public sealed record FontAtlasPageToml
{
    [TomlPropertyName("index")]
    public int Index { get; init; }

    [TomlPropertyName("image")]
    public string Image { get; init; } = string.Empty;

    [TomlPropertyName("width")]
    public int Width { get; init; }

    [TomlPropertyName("height")]
    public int Height { get; init; }

    [TomlPropertyName("content_hash")]
    public string ContentHash { get; init; } = string.Empty;
}

public sealed record FontAtlasGlyphToml
{
    [TomlPropertyName("codepoint")]
    public int Codepoint { get; init; }

    [TomlPropertyName("char")]
    public string? Char { get; init; }

    [TomlPropertyName("em_size")]
    public double EmSize { get; init; }

    [TomlPropertyName("weight")]
    public int Weight { get; init; }

    [TomlPropertyName("slant")]
    public string Slant { get; init; } = string.Empty;

    [TomlPropertyName("page")]
    public int Page { get; init; }

    [TomlPropertyName("x")]
    public int X { get; init; }

    [TomlPropertyName("y")]
    public int Y { get; init; }

    [TomlPropertyName("width")]
    public int Width { get; init; }

    [TomlPropertyName("height")]
    public int Height { get; init; }

    [TomlPropertyName("advance")]
    public double Advance { get; init; }

    [TomlPropertyName("bearing_x")]
    public double BearingX { get; init; }

    [TomlPropertyName("bearing_y")]
    public double BearingY { get; init; }

    [TomlPropertyName("u0")]
    public double U0 { get; init; }

    [TomlPropertyName("v0")]
    public double V0 { get; init; }

    [TomlPropertyName("u1")]
    public double U1 { get; init; }

    [TomlPropertyName("v1")]
    public double V1 { get; init; }
}

public sealed record FontAtlasTomlExportMetadata(
    string Name,
    string DistanceField,
    string FontFamily,
    string FontStyle,
    string FontSource,
    string FontSourceHash,
    string License,
    FontAtlasMetricsToml Metrics,
    FontAtlasMsdfToml Msdf);
