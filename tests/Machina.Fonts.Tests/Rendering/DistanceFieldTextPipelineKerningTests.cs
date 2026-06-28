using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Artifacts.DistanceField;
using Machina.Fonts.Tests.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class DistanceFieldTextPipelineKerningTests
{
    private static readonly Rgba32 Background = new(16, 16, 24, 255);

    [Fact]
    public async Task DistanceFieldTextPipeline_KerningChangesTextWidthForKnownPair()
    {
        TypographyGlyphOutlineSource sourceWithKerning = TypographyKerningFixtureFont.CreateSource();
        NoPairAdjustmentOutlineSource sourceWithoutKerning = new(sourceWithKerning);
        string withKerningDirectory = CreateArtifactDirectory();
        string withoutKerningDirectory = CreateArtifactDirectory();

        DistanceFieldTextPipeline withKerning = new(
            sourceWithKerning,
            new MsdfSharpDistanceFieldGenerator(),
            DistanceFieldArtifactTestHelpers.Metadata("crimson-kerning", "msdf"));
        DistanceFieldTextPipeline withoutKerning = new(
            sourceWithoutKerning,
            new MsdfSharpDistanceFieldGenerator(),
            DistanceFieldArtifactTestHelpers.Metadata("crimson-no-kerning", "msdf"));

        DistanceFieldTextPipelineResult kerned = await withKerning.RenderTextAsync("AV", CreateOptions(), withKerningDirectory);
        DistanceFieldTextPipelineResult plain = await withoutKerning.RenderTextAsync("AV", CreateOptions(), withoutKerningDirectory);

        Assert.True(kerned.Success);
        Assert.True(plain.Success);
        Assert.NotNull(kerned.Image);
        Assert.NotNull(plain.Image);

        int kernedRight = FindNonBackgroundPixels(kerned.Image!).Max(static point => point.X);
        int plainRight = FindNonBackgroundPixels(plain.Image!).Max(static point => point.X);

        Assert.True(kernedRight < plainRight);
    }

    private static DistanceFieldTextRenderOptions CreateOptions()
    {
        return new DistanceFieldTextRenderOptions(
            96,
            64,
            TypographyKerningFixtureFont.Face,
            32,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            DistanceFieldKind.Msdf,
            32,
            32,
            4d,
            new Rgba32(240, 240, 240, 255),
            Background,
            8d,
            40d,
            FlipY: true,
            PageWidth: 128,
            PageHeight: 128,
            PagePadding: 2);
    }

    private static string CreateArtifactDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8n-pipeline", Guid.NewGuid().ToString("N"));
    }

    private static IReadOnlyList<(int X, int Y)> FindNonBackgroundPixels(RgbaImage image)
    {
        List<(int X, int Y)> result = [];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y) != Background)
                {
                    result.Add((x, y));
                }
            }
        }

        return result;
    }

    private sealed class NoPairAdjustmentOutlineSource : IGlyphOutlineSource
    {
        private readonly IGlyphOutlineSource inner;

        public NoPairAdjustmentOutlineSource(IGlyphOutlineSource inner)
        {
            this.inner = inner;
        }

        public ValueTask<GlyphOutlineLoadResult> LoadGlyphOutlineAsync(
            FontFaceId face,
            int codepoint,
            GlyphOutlineLoadOptions options,
            CancellationToken cancellationToken = default)
        {
            return inner.LoadGlyphOutlineAsync(face, codepoint, options, cancellationToken);
        }
    }
}
