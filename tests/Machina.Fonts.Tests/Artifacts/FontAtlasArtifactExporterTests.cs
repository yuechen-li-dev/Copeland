using Machina.Fonts.Artifacts;
using Machina.Fonts.Toml;
using Xunit;

namespace Machina.Fonts.Tests.Artifacts;

public sealed class FontAtlasArtifactExporterTests
{
    [Fact]
    public async Task Exporter_CreatesTomlAndFakePageArtifacts()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        FontAtlasArtifactExportResult result = ArtifactTestHelpers.Export(await ArtifactTestHelpers.CreateSnapshotAsync('A', 'B'), directory);
        Assert.True(result.Success);
        Assert.True(File.Exists(result.TomlPath));
        Assert.All(result.PagePaths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task Exporter_CreatesOutputDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nested");
        FontAtlasArtifactExportResult result = ArtifactTestHelpers.Export(await ArtifactTestHelpers.CreateSnapshotAsync('A'), directory);
        Assert.True(result.Success);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public async Task Exporter_WritesDeterministicTomlAcrossRuns()
    {
        FontAtlasSnapshot snapshot = await ArtifactTestHelpers.CreateSnapshotAsync('C', 'A', 'B');
        string firstDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string secondDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        FontAtlasArtifactExportResult first = ArtifactTestHelpers.Export(snapshot, firstDirectory);
        FontAtlasArtifactExportResult second = ArtifactTestHelpers.Export(snapshot, secondDirectory);
        Assert.Equal(File.ReadAllText(first.TomlPath), File.ReadAllText(second.TomlPath));
    }

    [Fact]
    public async Task Exporter_WritesDeterministicFakePageContent()
    {
        FontAtlasSnapshot snapshot = await ArtifactTestHelpers.CreateSnapshotAsync('C', 'A', 'B');
        string firstDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string secondDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        FontAtlasArtifactExportResult first = ArtifactTestHelpers.Export(snapshot, firstDirectory);
        FontAtlasArtifactExportResult second = ArtifactTestHelpers.Export(snapshot, secondDirectory);
        Assert.Equal(File.ReadAllText(first.PagePaths[0]), File.ReadAllText(second.PagePaths[0]));
    }

    [Fact]
    public async Task Exporter_WritesContentHashesIntoToml()
    {
        FontAtlasArtifactExportResult result = ArtifactTestHelpers.Export(await ArtifactTestHelpers.CreateSnapshotAsync('A'), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        FontAtlasTomlLoadResult loaded = FontAtlasTomlLoader.LoadFile(result.TomlPath);
        Assert.Equal(FakeFontAtlasPageArtifactWriter.ComputeFileSha256(result.PagePaths[0]), loaded.Document!.Pages[0].ContentHash);
    }

    [Fact]
    public async Task Exporter_SortsPagesAndGlyphsDeterministically()
    {
        FontAtlasSnapshot snapshot = await ArtifactTestHelpers.CreateSnapshotAsync('Z', 'A', 'M');
        FontAtlasArtifactExportResult result = ArtifactTestHelpers.Export(snapshot, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        string text = File.ReadAllText(result.TomlPath);
        Assert.True(text.IndexOf("codepoint = 65", StringComparison.Ordinal) < text.IndexOf("codepoint = 90", StringComparison.Ordinal));
    }
}
