using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Xunit;

namespace Machina.Fonts.Tests.Generation.MsdfSharp;

public sealed class MsdfSharpDistanceFieldGeneratorTests
{
    [Fact]
    public void GenerateSdf_ProducesOneChannelData()
    {
        MsdfSharpDistanceFieldGenerator generator = new();

        GeneratedGlyphDistanceField result = generator.Generate(
            MsdfSharpTestHelpers.CreateLineOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Sdf));

        Assert.Equal(1, result.ChannelCount);
        Assert.Equal(result.Width * result.Height, result.Data.Length);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void GeneratePsdf_ProducesOneChannelData()
    {
        MsdfSharpDistanceFieldGenerator generator = new();

        GeneratedGlyphDistanceField result = generator.Generate(
            MsdfSharpTestHelpers.CreateLineOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Psdf));

        Assert.Equal(1, result.ChannelCount);
        Assert.Equal(result.Width * result.Height, result.Data.Length);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void GenerateMsdf_ProducesThreeChannelData()
    {
        MsdfSharpDistanceFieldGenerator generator = new();

        GeneratedGlyphDistanceField result = generator.Generate(
            MsdfSharpTestHelpers.CreateQuadraticOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf));

        Assert.Equal(3, result.ChannelCount);
        Assert.Equal(result.Width * result.Height * 3, result.Data.Length);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void GenerateMtsdf_ProducesFourChannelData()
    {
        MsdfSharpDistanceFieldGenerator generator = new();

        GeneratedGlyphDistanceField result = generator.Generate(
            MsdfSharpTestHelpers.CreateCubicOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Mtsdf));

        Assert.Equal(4, result.ChannelCount);
        Assert.Equal(result.Width * result.Height * 4, result.Data.Length);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Generate_ProducesFiniteNonUniformDataForGlyph()
    {
        MsdfSharpDistanceFieldGenerator generator = new();
        GlyphOutline outline = await MsdfSharpTestHelpers.LoadFixtureOutlineAsync('A');

        GeneratedGlyphDistanceField result = generator.Generate(
            outline,
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf));

        Assert.Empty(result.Diagnostics);
        MsdfSharpTestHelpers.AssertFiniteNonUniform(result);
    }

    [Fact]
    public async Task Generate_IsDeterministicForSameOutlineAndSettings()
    {
        MsdfSharpDistanceFieldGenerator generator = new();
        GlyphOutline outline = await MsdfSharpTestHelpers.LoadFixtureOutlineAsync('a');
        MsdfGenerationSettings settings = MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Mtsdf);

        GeneratedGlyphDistanceField first = generator.Generate(outline, settings);
        GeneratedGlyphDistanceField second = generator.Generate(outline, settings);

        Assert.Empty(first.Diagnostics);
        Assert.Empty(second.Diagnostics);
        Assert.Equal(first.Data.ToArray(), second.Data.ToArray());
    }

    [Fact]
    public void Generate_RejectsInvalidSettings()
    {
        MsdfSharpDistanceFieldGenerator generator = new();

        GeneratedGlyphDistanceField result = generator.Generate(
            MsdfSharpTestHelpers.CreateLineOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf, edgeColoring: "unknown"));

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.InvalidGenerationSettings);
    }

    [Fact]
    public void Generate_ReportsEmptyOutline()
    {
        MsdfSharpDistanceFieldGenerator generator = new();

        GeneratedGlyphDistanceField result = generator.Generate(
            MsdfSharpTestHelpers.CreateEmptyVisibleOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Sdf));

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.EmptyOutline);
    }

    [Fact]
    public void Generate_PropagatesCancellation()
    {
        MsdfSharpDistanceFieldGenerator generator = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => generator.Generate(
            MsdfSharpTestHelpers.CreateLineOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Sdf),
            cts.Token));
    }
}
