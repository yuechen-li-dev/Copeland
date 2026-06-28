using Xunit;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Tests.Toml;

public sealed class FontAtlasTomlRoundtripTests
{
    [Fact]
    public void Roundtrip_WriterThenLoader_PreservesDocument()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument();
        FontAtlasTomlLoadResult result = FontAtlasTomlLoader.LoadString(FontAtlasTomlWriter.Write(document));
        Assert.True(result.Success);
        Assert.Equal(document.Atlas, result.Document!.Atlas);
        Assert.Equal(document.Font, result.Document.Font);
        Assert.Equal(document.Glyphs[0].Codepoint, result.Document.Glyphs[0].Codepoint);
    }

    [Fact]
    public void Roundtrip_SnapshotExportThenLoad_PreservesGlyphEntries()
    {
        FontAtlasTomlLoadResult loaded = FontAtlasTomlLoader.LoadString(FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument()));
        FontAtlasTomlExportMetadata metadata = new("machina-default", "msdf", "Inter", "Regular", "assets/fonts/Inter-Regular.ttf", "sha256-source", "OFL-1.1", FontAtlasTomlTestData.CreateDocument().Metrics, FontAtlasTomlTestData.CreateDocument().Msdf);
        FontAtlasTomlDocument exported = FontAtlasTomlConversion.FromSnapshot(loaded.Snapshot!, metadata);
        FontAtlasTomlLoadResult reloaded = FontAtlasTomlLoader.LoadString(FontAtlasTomlWriter.Write(exported));
        Assert.True(reloaded.Success);
        Assert.Equal(loaded.Snapshot!.Glyphs.Keys, reloaded.Snapshot!.Glyphs.Keys);
    }

    [Fact]
    public void Roundtrip_IsDeterministicAcrossRuns()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument();
        string first = FontAtlasTomlWriter.Write(document);
        string second = FontAtlasTomlWriter.Write(document);
        Assert.Equal(first, second);
    }
}
