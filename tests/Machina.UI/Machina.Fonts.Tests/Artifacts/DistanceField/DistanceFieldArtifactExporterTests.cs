using Machina.Fonts.Artifacts;
using Machina.Fonts.Artifacts.DistanceField;
using Machina.Fonts.Generation;
using Machina.Fonts.Toml;
using Xunit;

namespace Machina.Fonts.Tests.Artifacts.DistanceField;

public sealed class DistanceFieldArtifactExporterTests
{
    [Fact]
    public void DistanceFieldArtifactExporter_WritesTomlAndDfpage()
    {
        GeneratedFieldAtlasPackResult packResult = DistanceFieldArtifactTestHelpers.Pack(
            [DistanceFieldArtifactTestHelpers.CreateField('A', 4, 4)],
            16,
            16,
            1);

        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        FontAtlasArtifactExportResult export = DistanceFieldArtifactTestHelpers.Export(packResult, directory);

        Assert.True(export.Success);
        Assert.True(File.Exists(export.TomlPath));
        Assert.Single(export.PagePaths);
        Assert.EndsWith(".dfpage", export.PagePaths[0], StringComparison.Ordinal);
        Assert.True(File.Exists(export.PagePaths[0]));
    }

    [Fact]
    public void DistanceFieldArtifactExporter_WritesContentHashes()
    {
        GeneratedFieldAtlasPackResult packResult = DistanceFieldArtifactTestHelpers.Pack(
            [DistanceFieldArtifactTestHelpers.CreateField('A', 4, 4)],
            16,
            16,
            1);

        FontAtlasArtifactExportResult export = DistanceFieldArtifactTestHelpers.Export(
            packResult,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        FontAtlasTomlLoadResult loaded = FontAtlasTomlLoader.LoadFile(export.TomlPath);
        Assert.Equal(
            DistanceFieldPageArtifactWriter.ComputeFileSha256(export.PagePaths[0]),
            loaded.Document!.Pages[0].ContentHash);
    }

    [Fact]
    public void DistanceFieldArtifactImporter_LoadsExportedArtifact()
    {
        GeneratedFieldAtlasPackResult packResult = DistanceFieldArtifactTestHelpers.Pack(
            [DistanceFieldArtifactTestHelpers.CreateField('A', 4, 4)],
            16,
            16,
            1);

        FontAtlasArtifactExportResult export = DistanceFieldArtifactTestHelpers.Export(
            packResult,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);

        Assert.True(import.Success);
        Assert.NotNull(import.Snapshot);
    }

    [Fact]
    public void DistanceFieldArtifactValidator_ReportsMissingDfpage()
    {
        FontAtlasArtifactExportResult export = ExportSinglePage();
        File.Delete(export.PagePaths[0]);

        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);

        Assert.Contains(import.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.ImageMissing);
    }

    [Fact]
    public void DistanceFieldArtifactValidator_ReportsHashMismatch()
    {
        FontAtlasArtifactExportResult export = ExportSinglePage();
        using (FileStream stream = new(export.PagePaths[0], FileMode.Append, FileAccess.Write, FileShare.None))
        {
            stream.WriteByte(0x5A);
        }

        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);

        Assert.Contains(import.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.ContentHashMismatch);
    }

    [Fact]
    public void DistanceFieldArtifactValidator_ReportsHeaderMismatch()
    {
        FontAtlasArtifactExportResult export = ExportSinglePage();
        byte[] bytes = File.ReadAllBytes(export.PagePaths[0]);
        byte[] replacement = System.Text.Encoding.UTF8.GetBytes("not-a-dfpage");
        Buffer.BlockCopy(replacement, 0, bytes, 0, replacement.Length);
        File.WriteAllBytes(export.PagePaths[0], bytes);
        DistanceFieldArtifactTestHelpers.RewriteTomlHash(export, export.PagePaths[0]);

        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);

        Assert.Contains(import.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.InvalidPageArtifact);
    }

    [Fact]
    public void DistanceFieldArtifactValidator_ReportsDataLengthMismatch()
    {
        FontAtlasArtifactExportResult export = ExportSinglePage();
        byte[] bytes = File.ReadAllBytes(export.PagePaths[0]);
        File.WriteAllBytes(export.PagePaths[0], bytes[..^4]);
        DistanceFieldArtifactTestHelpers.RewriteTomlHash(export, export.PagePaths[0]);

        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);

        Assert.Contains(import.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.DataLengthMismatch);
    }

    [Fact]
    public void DistanceFieldArtifactRoundtrip_PreservesSnapshotEntries()
    {
        GeneratedFieldAtlasPackResult packResult = DistanceFieldArtifactTestHelpers.Pack(
            [
                DistanceFieldArtifactTestHelpers.CreateField('A', 4, 4),
                DistanceFieldArtifactTestHelpers.CreateField('B', 4, 5),
                DistanceFieldArtifactTestHelpers.CreateField('C', 6, 4),
            ],
            16,
            16,
            1);

        FontAtlasArtifactExportResult export = DistanceFieldArtifactTestHelpers.Export(
            packResult,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);

        Assert.True(import.Success);
        ArtifactTestHelpers.AssertEquivalent(
            DistanceFieldArtifactTestHelpers.NormalizeSnapshot(packResult.Snapshot, import.Snapshot!),
            import.Snapshot!);
    }

    [Fact]
    public void ArtifactRoundtrip_PreservesPlacement()
    {
        GeneratedFieldAtlasPackResult packResult = DistanceFieldArtifactTestHelpers.Pack(
            [DistanceFieldArtifactTestHelpers.CreateField('A', 4, 4)],
            16,
            16,
            1);

        FontAtlasArtifactExportResult export = DistanceFieldArtifactTestHelpers.Export(
            packResult,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);

        Assert.Equal(
            packResult.Snapshot.Glyphs.Values.Single().Placement,
            import.Snapshot!.Glyphs.Values.Single().Placement);
    }

    private static FontAtlasArtifactExportResult ExportSinglePage()
    {
        GeneratedFieldAtlasPackResult packResult = DistanceFieldArtifactTestHelpers.Pack(
            [DistanceFieldArtifactTestHelpers.CreateField('A', 4, 4)],
            16,
            16,
            1);

        return DistanceFieldArtifactTestHelpers.Export(
            packResult,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    }
}
