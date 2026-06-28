using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class FontProofExporterTests
{
    [Fact]
    public async Task FontProofExporter_WritesExpectedArtifacts()
    {
        string directory = CreateDirectory();

        FontProofExportResult result = await FontProofWorkflow.ExportAsync(directory);

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(directory, "space-mono-msdf-proofs.font-atlas.toml"), result.TomlPath);
        Assert.NotEmpty(result.PagePaths);
        Assert.Equal(FontProofWorkflow.Definitions.Count, result.Artifacts.Count);

        foreach (FontProofArtifactDefinition definition in FontProofWorkflow.Definitions)
        {
            string artifactPath = Path.Combine(directory, definition.Name);
            Assert.Contains(result.Artifacts, artifact => string.Equals(artifact.PpmPath, artifactPath, StringComparison.Ordinal));
            Assert.True(File.Exists(artifactPath));
        }
    }

    [Fact]
    public async Task FontProofExporter_IsDeterministicAcrossRuns()
    {
        string firstDirectory = CreateDirectory();
        string secondDirectory = CreateDirectory();

        FontProofExportResult first = await FontProofWorkflow.ExportAsync(firstDirectory);
        FontProofExportResult second = await FontProofWorkflow.ExportAsync(secondDirectory);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Artifacts.Select(ReadArtifactBytes), second.Artifacts.Select(ReadArtifactBytes));
        Assert.Equal(File.ReadAllBytes(first.TomlPath!), File.ReadAllBytes(second.TomlPath!));
    }

    [Fact]
    public async Task FontProofExporter_WritesNonBlankImages()
    {
        string directory = CreateDirectory();

        FontProofExportResult result = await FontProofWorkflow.ExportAsync(directory);

        Assert.True(result.Success);
        foreach (FontProofArtifact artifact in result.Artifacts)
        {
            Assert.Contains(artifact.Image.Pixels, pixel => pixel != FontProofWorkflow.BackgroundColor);
        }
    }

    [Fact]
    public async Task FontProofExporter_WhitespaceAdvancesInAspaceA()
    {
        string directory = CreateDirectory();

        FontProofExportResult result = await FontProofWorkflow.ExportAsync(directory);

        Assert.True(result.Success);

        FontProofArtifact artifact = Assert.Single(
            result.Artifacts,
            static artifact => artifact.Definition.Name == "msdf-a-space-a.ppm");
        Assert.Single(artifact.MetricsOnlyGlyphs);

        int minX = FindNonBackgroundPixels(artifact.Image).Min(static point => point.X);
        int maxX = FindNonBackgroundPixels(artifact.Image).Max(static point => point.X);

        Assert.Equal(' ', artifact.MetricsOnlyGlyphs[0].Codepoint);
        Assert.InRange(minX, 8, 9);
        Assert.True(maxX >= 56);
    }

    [Fact]
    public async Task FontProofExporter_LongestProofStringsLeaveRightEdgeClear()
    {
        string directory = CreateDirectory();

        FontProofExportResult result = await FontProofWorkflow.ExportAsync(directory);

        Assert.True(result.Success);

        FontProofArtifact artifact = Assert.Single(
            result.Artifacts,
            static artifact => artifact.Definition.Name == "msdf-hello-machina.ppm");

        for (int y = 0; y < artifact.Image.Height; y++)
        {
            Assert.Equal(FontProofWorkflow.BackgroundColor, artifact.Image.GetPixel(artifact.Image.Width - 1, y));
        }
    }

    [Fact]
    public async Task FontProofExporter_ScriptWorkflowExportsProofSet()
    {
        string directory = FontProofWorkflow.GetRequestedOutputDirectoryOrCreateTemp();

        IReadOnlyList<string> createdFiles = await FontProofWorkflow.ExportAllAsync(directory);

        Assert.All(createdFiles, static path =>
        {
            Assert.True(File.Exists(path));
        });
    }

    [Fact]
    public async Task FontProofExporter_WritesKerningProofArtifacts()
    {
        string directory = CreateDirectory();

        FontProofExportResult result = await FontProofWorkflow.ExportKerningAsync(directory);

        Assert.True(result.Success);
        Assert.Equal(FontProofWorkflow.KerningDefinitions.Count, result.Artifacts.Count);
        Assert.All(FontProofWorkflow.KerningDefinitions, definition =>
        {
            Assert.True(File.Exists(Path.Combine(directory, definition.Name)));
        });
    }

    [Fact]
    public async Task FontProofExporter_KerningProofIsDeterministic()
    {
        string firstDirectory = CreateDirectory();
        string secondDirectory = CreateDirectory();

        FontProofExportResult first = await FontProofWorkflow.ExportKerningAsync(firstDirectory);
        FontProofExportResult second = await FontProofWorkflow.ExportKerningAsync(secondDirectory);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Artifacts.Select(ReadArtifactBytes), second.Artifacts.Select(ReadArtifactBytes));
    }

    [Fact]
    public async Task FontProofExporter_AVPairDiffersWithKerningIfFixtureSupportsIt()
    {
        string directory = CreateDirectory();

        FontProofExportResult result = await FontProofWorkflow.ExportKerningAsync(directory);

        Assert.True(result.Success);
        FontProofArtifact artifact = Assert.Single(
            result.Artifacts,
            static item => item.Definition.Name == "msdf-av-to-wa.ppm");

        IReadOnlyList<(int X, int Y)> pixels = FindNonBackgroundPixels(artifact.Image);
        Assert.NotEmpty(pixels);
        Assert.True(pixels.Max(static point => point.X) < artifact.Image.Width - 8);
    }

    private static byte[] ReadArtifactBytes(FontProofArtifact artifact)
    {
        return File.ReadAllBytes(artifact.PpmPath);
    }

    private static string CreateDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8l-tests", Guid.NewGuid().ToString("N"));
    }

    private static IReadOnlyList<(int X, int Y)> FindNonBackgroundPixels(RgbaImage image)
    {
        List<(int X, int Y)> result = [];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y) != FontProofWorkflow.BackgroundColor)
                {
                    result.Add((x, y));
                }
            }
        }

        return result;
    }
}
