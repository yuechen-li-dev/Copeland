using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Artifacts.DistanceField;
using Machina.Fonts.Tests.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class TypographyMsdfReferenceTextRenderTests
{
    private static readonly Rgba32 Background = new(16, 16, 24, 255);

    [Fact]
    public async Task TypographyMsdfTextPipeline_RendersMachina()
    {
        DistanceFieldTextPipelineResult result = await RenderAsync("Machina");

        Assert.True(result.Success);
        Assert.NotNull(result.Image);
        Assert.Equal(160, result.Image!.Width);
        Assert.Equal(64, result.Image.Height);
        Assert.Contains(result.Image.Pixels, pixel => pixel != Background);
        Assert.Equal("Machina".Length, result.RenderedGlyphs.Count);
    }

    [Fact]
    public async Task TypographyMsdfTextPipeline_RendersAa0()
    {
        DistanceFieldTextPipelineResult result = await RenderAsync("Aa0");

        Assert.True(result.Success);
        Assert.Equal(['A', 'a', '0'], result.RenderedGlyphs.Select(static key => key.Codepoint).ToArray());
    }

    [Fact]
    public async Task TypographyMsdfTextPipeline_RendersWhitespaceWithAdvance()
    {
        DistanceFieldTextPipelineResult result = await RenderAsync("A A");

        Assert.True(result.Success);
        Assert.Equal(2, result.RenderedGlyphs.Count);
        Assert.Single(result.MetricsOnlyGlyphs);
        Assert.Equal(' ', result.MetricsOnlyGlyphs[0].Codepoint);
    }

    [Fact]
    public async Task TypographyMsdfTextPipeline_OutputIsDeterministic()
    {
        DistanceFieldTextPipelineResult first = await RenderAsync("Machina");
        DistanceFieldTextPipelineResult second = await RenderAsync("Machina");

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Image!.Pixels, second.Image!.Pixels);
        Assert.Equal(File.ReadAllBytes(first.PpmPath!), File.ReadAllBytes(second.PpmPath!));
    }

    [Fact]
    public async Task TypographyMsdfTextPipeline_WritesPpmProofArtifact()
    {
        string directory = CreateArtifactDirectory();
        DistanceFieldTextPipelineResult result = await RenderAsync("Aa0", directory);

        Assert.True(result.Success);
        Assert.NotNull(result.PpmPath);
        Assert.True(File.Exists(result.PpmPath));

        byte[] bytes = File.ReadAllBytes(result.PpmPath!);
        Assert.StartsWith("P6\n160 64\n255\n", System.Text.Encoding.ASCII.GetString(bytes, 0, "P6\n160 64\n255\n".Length), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypographyMsdfTextPipeline_EndToEndThroughDfpageReadback()
    {
        string directory = CreateArtifactDirectory();
        DistanceFieldTextPipelineResult result = await RenderAsync("Machina 0", directory);

        Assert.True(result.Success);
        Assert.NotNull(result.Snapshot);
        Assert.True(File.Exists(Path.Combine(directory, "space-mono-text.font-atlas.toml")));
        Assert.NotEmpty(Directory.GetFiles(directory, "*.dfpage"));
    }

    private static async Task<DistanceFieldTextPipelineResult> RenderAsync(string text, string? artifactDirectory = null)
    {
        DistanceFieldTextPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator(),
            DistanceFieldArtifactTestHelpers.Metadata("space-mono-text", "msdf"));

        DistanceFieldTextRenderOptions options = new(
            160,
            64,
            TypographyFixtureFont.Face,
            32,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            Machina.Fonts.Generation.DistanceFieldKind.Msdf,
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

        return await pipeline.RenderTextAsync(text, options, artifactDirectory);
    }

    private static string CreateArtifactDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8k-text", Guid.NewGuid().ToString("N"));
    }
}
