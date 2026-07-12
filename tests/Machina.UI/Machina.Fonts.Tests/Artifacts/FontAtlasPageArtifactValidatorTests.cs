using Machina.Fonts.Artifacts;
using Machina.Fonts.Toml;
using Xunit;

namespace Machina.Fonts.Tests.Artifacts;

public sealed class FontAtlasPageArtifactValidatorTests
{
    [Fact]
    public async Task Importer_LoadsExportedArtifact()
    {
        FontAtlasArtifactExportResult export = await ExportAsync();
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        Assert.True(import.Success);
        Assert.NotNull(import.Snapshot);
    }

    [Fact]
    public async Task Importer_ReportsMissingPageArtifact()
    {
        FontAtlasArtifactExportResult export = await ExportAsync();
        File.Delete(export.PagePaths[0]);
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        Assert.Contains(import.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.ImageMissing);
    }

    [Fact]
    public async Task Importer_ReportsContentHashMismatch()
    {
        FontAtlasArtifactExportResult export = await ExportAsync();
        await File.AppendAllTextAsync(export.PagePaths[0], "stale=true\n");
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        Assert.Contains(import.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.ContentHashMismatch);
    }

    [Fact]
    public async Task Importer_ReportsPageDimensionMismatch()
    {
        FontAtlasArtifactExportResult export = await ExportAsync();
        string text = File.ReadAllText(export.PagePaths[0]).Replace("width=256", "width=999", StringComparison.Ordinal);
        File.WriteAllText(export.PagePaths[0], text);
        RewriteTomlHash(export);
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        Assert.Contains(import.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.PageDimensionMismatch);
    }

    [Fact]
    public async Task Importer_RejectsInvalidFakePageFormat()
    {
        FontAtlasArtifactExportResult export = await ExportAsync();
        File.WriteAllText(export.PagePaths[0], "not-a-fake-page\n");
        RewriteTomlHash(export);
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        Assert.Contains(import.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.InvalidPageArtifact);
    }

    private static async Task<FontAtlasArtifactExportResult> ExportAsync()
    {
        return ArtifactTestHelpers.Export(await ArtifactTestHelpers.CreateSnapshotAsync('A', 'B'), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    }

    private static void RewriteTomlHash(FontAtlasArtifactExportResult export)
    {
        string hash = FakeFontAtlasPageArtifactWriter.ComputeFileSha256(export.PagePaths[0]);
        FontAtlasTomlLoadResult loaded = FontAtlasTomlLoader.LoadFile(export.TomlPath);
        FontAtlasTomlDocument document = loaded.Document! with
        {
            Pages = [loaded.Document.Pages[0] with { ContentHash = hash }],
        };
        File.WriteAllText(export.TomlPath, FontAtlasTomlWriter.Write(document));
    }
}
