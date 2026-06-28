using Machina.Fonts.Toml;

namespace Machina.Fonts.Tests.Toml;

internal static class FontAtlasTomlTestData
{
    public static FontAtlasTomlDocument CreateDocument()
    {
        return new FontAtlasTomlDocument
        {
            Atlas = new FontAtlasHeaderToml { Format = 1, Kind = "machina-font-atlas", Name = "machina-default", DistanceField = "msdf", Version = 1 },
            Font = new FontAtlasFontToml { Face = "machina-default-sans", Family = "Inter", Style = "Regular", Source = "assets/fonts/Inter-Regular.ttf", SourceHash = "sha256-source", License = "OFL-1.1" },
            Metrics = new FontAtlasMetricsToml { EmSize = 32, UnitsPerEm = 2048, Ascent = 26, Descent = -7, LineGap = 5, LineHeight = 38 },
            Msdf = new FontAtlasMsdfToml { Range = 4, Scale = 1, EdgeColoring = "simple", MiterLimit = 1 },
            Pages = [new FontAtlasPageToml { Index = 0, Image = "machina-default.page0.png", Width = 1024, Height = 1024, ContentHash = "sha256-page" }],
            Glyphs = [new FontAtlasGlyphToml { Codepoint = 65, Char = "A", EmSize = 32, Weight = 400, Slant = "upright", Page = 0, X = 12, Y = 16, Width = 40, Height = 44, Advance = 36, BearingX = 1, BearingY = 34, U0 = 12.0 / 1024.0, V0 = 16.0 / 1024.0, U1 = 52.0 / 1024.0, V1 = 60.0 / 1024.0 }],
        };
    }
}
