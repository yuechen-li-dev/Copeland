using Machina.Fonts.Artifacts;
using Machina.Fonts.Artifacts.DistanceField;
using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Artifacts.DistanceField;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class TypographyMsdfReferenceRenderTests
{
    [Fact]
    public async Task TypographyMsdfReferenceRender_RendersGlyphA()
    {
        ProofRenderResult result = await RenderGlyphAAsync();

        Assert.True(File.Exists(result.PpmPath));
        Assert.Equal(64, result.Image.Width);
        Assert.Equal(64, result.Image.Height);
    }

    [Fact]
    public async Task TypographyMsdfReferenceRender_OutputIsNonBlank()
    {
        ProofRenderResult result = await RenderGlyphAAsync();
        Rgba32 background = new(16, 16, 24, 255);

        Assert.Contains(result.Image.Pixels, pixel => pixel != background);
    }

    [Fact]
    public async Task TypographyMsdfReferenceRender_OutputIsDeterministic()
    {
        ProofRenderResult first = await RenderGlyphAAsync();
        ProofRenderResult second = await RenderGlyphAAsync();

        Assert.Equal(first.Image.Pixels, second.Image.Pixels);
        Assert.Equal(File.ReadAllBytes(first.PpmPath), File.ReadAllBytes(second.PpmPath));
    }

    private static async Task<ProofRenderResult> RenderGlyphAAsync()
    {
        GeneratedFieldAtlasPackResult packResult = await DistanceFieldArtifactTestHelpers.RunTypographyMsdfPackAsync('A');
        Assert.True(packResult.Success);

        string directory = Path.Combine(Path.GetTempPath(), "machina-fonts-m8k", Guid.NewGuid().ToString("N"));
        FontAtlasArtifactExportResult export = DistanceFieldAtlasArtifactExporter.Export(
            packResult,
            DistanceFieldArtifactTestHelpers.Metadata("space-mono-a", "msdf"),
            directory,
            "space-mono-a");

        Assert.True(export.Success);

        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        Assert.True(import.Success);

        DistanceFieldPageReference page = DistanceFieldPageReferenceReader.Read(export.PagePaths[0]);
        GlyphAtlasEntry entry = import.Snapshot!.Glyphs.Values.Single(candidate => candidate.Key.Codepoint == 'A');
        RgbaImage image = CpuDistanceFieldGlyphRenderer.RenderGlyph(
            page,
            entry,
            new DistanceFieldRenderOptions(
                64,
                64,
                new Rgba32(240, 240, 240, 255),
                new Rgba32(16, 16, 24, 255),
                PxRange: 4d,
                Threshold: 0.5d,
                FlipY: false));

        string ppmPath = Path.Combine(directory, "space-mono-a.ppm");
        PpmImageWriter.Write(ppmPath, image);

        byte[] bytes = File.ReadAllBytes(ppmPath);
        Assert.StartsWith("P6\n64 64\n255\n", System.Text.Encoding.ASCII.GetString(bytes, 0, "P6\n64 64\n255\n".Length), StringComparison.Ordinal);

        return new ProofRenderResult(image, ppmPath);
    }

    private sealed record ProofRenderResult(RgbaImage Image, string PpmPath);
}
