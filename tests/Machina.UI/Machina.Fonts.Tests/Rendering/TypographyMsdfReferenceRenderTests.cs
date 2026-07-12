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

    [Fact]
    public async Task TypographyMsdfReferenceRender_FlipYProducesUprightGlyphOrientation()
    {
        ProofRenderResult inverted = await RenderGlyphAAsync(flipY: false);
        ProofRenderResult upright = await RenderGlyphAAsync(flipY: true);

        (int topWidth, int bottomWidth) invertedSpan = GetFirstAndLastInkRowWidths(inverted.Image);
        (int topWidth, int bottomWidth) uprightSpan = GetFirstAndLastInkRowWidths(upright.Image);

        Assert.True(invertedSpan.topWidth > invertedSpan.bottomWidth);
        Assert.True(uprightSpan.topWidth < uprightSpan.bottomWidth);
    }

    private static async Task<ProofRenderResult> RenderGlyphAAsync(bool flipY = true)
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
                FlipY: flipY));

        string ppmPath = Path.Combine(directory, "space-mono-a.ppm");
        PpmImageWriter.Write(ppmPath, image);

        byte[] bytes = File.ReadAllBytes(ppmPath);
        Assert.StartsWith("P6\n64 64\n255\n", System.Text.Encoding.ASCII.GetString(bytes, 0, "P6\n64 64\n255\n".Length), StringComparison.Ordinal);

        return new ProofRenderResult(image, ppmPath);
    }

    private static (int TopWidth, int BottomWidth) GetFirstAndLastInkRowWidths(RgbaImage image)
    {
        int firstRow = -1;
        int lastRow = -1;

        for (int y = 0; y < image.Height; y++)
        {
            if (RowHasInk(image, y))
            {
                firstRow = y;
                break;
            }
        }

        for (int y = image.Height - 1; y >= 0; y--)
        {
            if (RowHasInk(image, y))
            {
                lastRow = y;
                break;
            }
        }

        Assert.True(firstRow >= 0);
        Assert.True(lastRow >= 0);

        return (GetInkRowWidth(image, firstRow), GetInkRowWidth(image, lastRow));
    }

    private static bool RowHasInk(RgbaImage image, int y)
    {
        for (int x = 0; x < image.Width; x++)
        {
            if (image.GetPixel(x, y) != new Rgba32(16, 16, 24, 255))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetInkRowWidth(RgbaImage image, int y)
    {
        int minX = image.Width;
        int maxX = -1;

        for (int x = 0; x < image.Width; x++)
        {
            if (image.GetPixel(x, y) == new Rgba32(16, 16, 24, 255))
            {
                continue;
            }

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }

        Assert.True(maxX >= minX);
        return maxX - minX + 1;
    }

    private sealed record ProofRenderResult(RgbaImage Image, string PpmPath);
}
