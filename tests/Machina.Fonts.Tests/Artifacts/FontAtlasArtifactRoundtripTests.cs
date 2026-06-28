using Machina.Fonts.Artifacts;
using Machina.Fonts.Generation;
using Xunit;

namespace Machina.Fonts.Tests.Artifacts;

public sealed class FontAtlasArtifactRoundtripTests
{
    [Fact]
    public async Task Roundtrip_FakeWorkerSnapshot_ExportsAndImportsEquivalentSnapshot()
    {
        FontAtlasSnapshot snapshot = await ArtifactTestHelpers.CreateSnapshotAsync('A', 'B', 'a');
        FontAtlasSnapshot exportSnapshot = ExportAndImport(snapshot);
        ArtifactTestHelpers.AssertEquivalent(NormalizeSnapshot(snapshot, exportSnapshot), exportSnapshot);
    }

    [Fact]
    public async Task Roundtrip_IsDeterministicAcrossRuns()
    {
        FontAtlasSnapshot snapshot = await ArtifactTestHelpers.CreateSnapshotAsync('D', 'E', 'F');
        string first = ExportText(snapshot);
        string second = ExportText(snapshot);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Roundtrip_MultiPageSnapshot_PreservesPagesAndGlyphs()
    {
        await using FakeFontAtlasService service = new(packer: new FakeAtlasPacker(24, 24));
        GlyphKey[] keys = Enumerable.Range('A', 8).Select(value => GlyphKey.FromCodepoint(ArtifactTestHelpers.Face, value, 12)).ToArray();
        FontAtlasPreflightResult preflight = await FontAtlasPreflight.EnsureReadyAsync(service, keys, TimeSpan.FromSeconds(5));
        Assert.True(preflight.Snapshot.Pages.Count > 1);
        FontAtlasSnapshot imported = ExportAndImport(preflight.Snapshot);
        Assert.Equal(preflight.Snapshot.Pages.Count, imported.Pages.Count);
        Assert.Equal(preflight.Snapshot.Glyphs.Count, imported.Glyphs.Count);
    }

    [Fact]
    public async Task PreflightThenExport_AllGlyphsReady_ExportsSuccessfully()
    {
        await using FakeFontAtlasService service = new();
        GlyphKey[] keys = [GlyphKey.FromChar(ArtifactTestHelpers.Face, 'A', 16), GlyphKey.FromChar(ArtifactTestHelpers.Face, 'B', 16)];
        FontAtlasPreflightResult preflight = await FontAtlasPreflight.EnsureReadyAsync(service, keys, TimeSpan.FromSeconds(5));
        FontAtlasArtifactExportResult export = ArtifactTestHelpers.Export(preflight.Snapshot, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.True(preflight.Success);
        Assert.True(export.Success);
    }

    [Fact]
    public async Task PreflightThenExport_MissingGlyphs_AreReportedOrExcludedByPolicy()
    {
        await using FakeFontAtlasService service = new(generator: new FakeGlyphGenerator(['X']));
        GlyphKey missing = GlyphKey.FromChar(ArtifactTestHelpers.Face, 'X', 16);
        GlyphKey ready = GlyphKey.FromChar(ArtifactTestHelpers.Face, 'Y', 16);
        FontAtlasPreflightResult preflight = await FontAtlasPreflight.EnsureReadyAsync(service, [missing, ready], TimeSpan.FromSeconds(5));
        FontAtlasArtifactExportResult export = ArtifactTestHelpers.Export(preflight.Snapshot, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.False(preflight.Success);
        Assert.Single(preflight.Failures);
        Assert.True(export.Success);
        Assert.DoesNotContain(missing, export.Document!.Glyphs.Select(glyph => GlyphKey.FromCodepoint(ArtifactTestHelpers.Face, glyph.Codepoint, glyph.EmSize)));
    }

    private static FontAtlasSnapshot ExportAndImport(FontAtlasSnapshot snapshot)
    {
        FontAtlasArtifactExportResult export = ArtifactTestHelpers.Export(snapshot, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        Assert.True(import.Success);
        return import.Snapshot!;
    }

    private static string ExportText(FontAtlasSnapshot snapshot)
    {
        FontAtlasArtifactExportResult export = ArtifactTestHelpers.Export(snapshot, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        return File.ReadAllText(export.TomlPath) + File.ReadAllText(export.PagePaths[0]);
    }

    private static FontAtlasSnapshot NormalizeSnapshot(FontAtlasSnapshot original, FontAtlasSnapshot exported)
    {
        return new FontAtlasSnapshot(original.Version, exported.Pages, original.Glyphs);
    }
}
