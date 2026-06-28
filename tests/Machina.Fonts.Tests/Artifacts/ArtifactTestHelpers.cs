using Xunit;
using Machina.Fonts.Artifacts;
using Machina.Fonts.Generation;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Tests.Artifacts;

internal static class ArtifactTestHelpers
{
    public static readonly FontFaceId Face = new("machina-default-sans");

    public static FontAtlasTomlExportMetadata Metadata(string name = "machina-default")
    {
        return new FontAtlasTomlExportMetadata(
            name,
            "msdf",
            "Inter",
            "Regular",
            "assets/fonts/Inter-Regular.ttf",
            "sha256-source",
            "OFL-1.1",
            new FontAtlasMetricsToml { EmSize = 16, UnitsPerEm = 2048, Ascent = 13, Descent = -4, LineGap = 3, LineHeight = 20 },
            new FontAtlasMsdfToml { Range = 4, Scale = 1, EdgeColoring = "fake", MiterLimit = 1 });
    }

    public static async Task<FontAtlasSnapshot> CreateSnapshotAsync(params char[] chars)
    {
        await using FakeFontAtlasService service = new();
        GlyphKey[] keys = chars.Select(value => GlyphKey.FromChar(Face, value, 16)).ToArray();
        FontAtlasPreflightResult result = await FontAtlasPreflight.EnsureReadyAsync(service, keys, TimeSpan.FromSeconds(5));
        if (!result.Success)
        {
            throw new InvalidOperationException("Expected test snapshot glyphs to be ready.");
        }

        return result.Snapshot;
    }

    public static FontAtlasArtifactExportResult Export(FontAtlasSnapshot snapshot, string directory, string name = "machina-default")
    {
        return FontAtlasArtifactExporter.Export(snapshot, Metadata(name), new FontAtlasArtifactExportOptions(name, directory));
    }

    public static void AssertEquivalent(FontAtlasSnapshot expected, FontAtlasSnapshot actual)
    {
        Assert.Equal(expected.Pages.Count, actual.Pages.Count);
        foreach (FontAtlasPage page in expected.Pages.OrderBy(page => page.Index))
        {
            FontAtlasPage actualPage = actual.Pages.Single(candidate => candidate.Index == page.Index);
            Assert.Equal(Path.GetFileName(page.ImagePath), Path.GetFileName(actualPage.ImagePath));
            Assert.Equal(page.Width, actualPage.Width);
            Assert.Equal(page.Height, actualPage.Height);
            Assert.Equal(page.ContentHash, actualPage.ContentHash);
        }

        Assert.Equal(expected.Glyphs.Keys.OrderBy(key => key.Codepoint), actual.Glyphs.Keys.OrderBy(key => key.Codepoint));
        foreach (GlyphKey key in expected.Glyphs.Keys)
        {
            Assert.Equal(expected.Glyphs[key], actual.Glyphs[key]);
        }
    }
}
