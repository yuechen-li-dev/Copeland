using Machina.Fonts.Artifacts;
using Machina.Fonts.Artifacts.DistanceField;
using Machina.Fonts.Generation;
using Xunit;

namespace Machina.Fonts.Tests.Artifacts.DistanceField;

public sealed class TypographyMsdfPipelineArtifactTests
{
    [Fact]
    public async Task TypographyMsdfPipeline_PacksAndExportsFixtureGlyphs()
    {
        GeneratedFieldAtlasPackResult packResult = await DistanceFieldArtifactTestHelpers.RunTypographyMsdfPackAsync('A', 'a', '0', '&', ' ');
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        FontAtlasArtifactExportResult export = DistanceFieldArtifactTestHelpers.Export(packResult, directory, "space-mono-msdf");
        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);

        Assert.True(packResult.Success);
        Assert.True(export.Success);
        Assert.True(import.Success);
        Assert.Equal(4, import.Snapshot!.Glyphs.Count);

        Assert.Single(export.PagePaths);
        Assert.True(DistanceFieldPageArtifactReader.TryRead(export.PagePaths[0], out DistanceFieldPageArtifactDocument? page, out _));
        Assert.NotNull(page);
        Assert.Contains(page!.Data, value => value != 0f);
    }

    [Fact]
    public async Task TypographyMsdfPipeline_ExcludesWhitespaceFromAtlasEntries()
    {
        GeneratedFieldAtlasPackResult packResult = await DistanceFieldArtifactTestHelpers.RunTypographyMsdfPackAsync('A', ' ');

        Assert.True(packResult.Success);
        Assert.Contains(packResult.Diagnostics, diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.MetricsOnlyGlyphSkipped);
        Assert.DoesNotContain(packResult.Snapshot.Glyphs.Keys, key => key.Codepoint == ' ');
    }

    [Fact]
    public async Task TypographyMsdfPipeline_IsDeterministicForSameInputs()
    {
        GeneratedFieldAtlasPackResult firstPack = await DistanceFieldArtifactTestHelpers.RunTypographyMsdfPackAsync('A', 'a', '0', '&', ' ');
        GeneratedFieldAtlasPackResult secondPack = await DistanceFieldArtifactTestHelpers.RunTypographyMsdfPackAsync('&', '0', 'a', 'A', ' ');

        string firstDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string secondDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        FontAtlasArtifactExportResult firstExport = DistanceFieldArtifactTestHelpers.Export(firstPack, firstDirectory, "space-mono-msdf");
        FontAtlasArtifactExportResult secondExport = DistanceFieldArtifactTestHelpers.Export(secondPack, secondDirectory, "space-mono-msdf");

        Assert.Equal(File.ReadAllText(firstExport.TomlPath), File.ReadAllText(secondExport.TomlPath));
        Assert.Equal(File.ReadAllBytes(firstExport.PagePaths[0]), File.ReadAllBytes(secondExport.PagePaths[0]));
    }
}
