using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Xunit;

namespace Machina.Fonts.Tests.Generation.MsdfSharp;

public sealed class MsdfSharpDistanceFieldGeneratorTests
{
    [Theory]
    [InlineData(DistanceFieldKind.Sdf, 32)]
    [InlineData(DistanceFieldKind.Sdf, 64)]
    [InlineData(DistanceFieldKind.Msdf, 64)]
    [InlineData(DistanceFieldKind.Mtsdf, 64)]
    public void EncodedDistanceRangeIsInAtlasPixelsRegardlessOfProjection(DistanceFieldKind kind, int dimension)
    {
        // The rectangle's bottom edge projects exactly to y=8. Probe its middle,
        // two atlas pixels inside the edge, away from corners and other edges.
        var field = new MsdfSharpDistanceFieldGenerator().Generate(
            MsdfSharpTestHelpers.CreateLineOutline(),
            MsdfSharpTestHelpers.CreateSettings(kind, dimension, dimension, pixelRange: 8));
        float distance = Machina.Fonts.ReferenceRendering.DistanceFieldSampling.SampleDistance(
            field.Data.Span, dimension, dimension, field.ChannelCount, kind,
            .5, 10d / dimension);

        Assert.Empty(field.Diagnostics);
        Assert.InRange(distance, .749f, .751f); // 0.5 + 2 / 8, not 0.5 + 2 / (8 * projection)
    }

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
        Assert.Equal(first.Placement, second.Placement);
        Assert.Equal(first.Data.ToArray(), second.Data.ToArray());
    }

    [Fact]
    public void GeneratedGlyphDistanceField_IncludesPlacement()
    {
        MsdfSharpDistanceFieldGenerator generator = new();

        GeneratedGlyphDistanceField result = generator.Generate(
            MsdfSharpTestHelpers.CreateLineOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Sdf));

        Assert.NotNull(result.Placement);
        Assert.True(result.Placement.Width > 0d);
        Assert.True(result.Placement.Height > 0d);
    }

    [Fact]
    public void MsdfSharpGenerator_ComputesFinitePlacement()
    {
        MsdfSharpDistanceFieldGenerator generator = new();

        GeneratedGlyphDistanceField result = generator.Generate(
            MsdfSharpTestHelpers.CreateQuadraticOutline(),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf));

        Assert.All(
            new[]
            {
                result.Placement.PlaneLeft,
                result.Placement.PlaneTop,
                result.Placement.PlaneRight,
                result.Placement.PlaneBottom,
                result.Placement.PixelRange,
                result.Placement.ProjectionScale,
            },
            static value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void MsdfSharpGenerator_PlacementMatchesProjectionCorners()
    {
        MsdfSharpDistanceFieldGenerator generator = new();
        GlyphOutline outline = MsdfSharpTestHelpers.CreateLineOutline();
        MsdfGenerationSettings settings = MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Sdf);

        GeneratedGlyphDistanceField result = generator.Generate(outline, settings);

        double drawableWidth = settings.Width - (settings.PixelRange * 2d);
        double drawableHeight = settings.Height - (settings.PixelRange * 2d);
        double fitScale = Math.Min(
            drawableWidth / (outline.Bounds.MaxX - outline.Bounds.MinX),
            drawableHeight / (outline.Bounds.MaxY - outline.Bounds.MinY));
        double projectionScale = fitScale * settings.Scale;
        double pixelTranslateX = ((settings.Width - ((outline.Bounds.MaxX - outline.Bounds.MinX) * projectionScale)) / 2d)
            - (outline.Bounds.MinX * projectionScale);
        double pixelTranslateY = ((settings.Height - ((outline.Bounds.MaxY - outline.Bounds.MinY) * projectionScale)) / 2d)
            - (outline.Bounds.MinY * projectionScale);
        double shapeTranslateX = pixelTranslateX / projectionScale;
        double shapeTranslateY = pixelTranslateY / projectionScale;

        double planeLeft = (0d / projectionScale) - shapeTranslateX;
        double planeRight = (settings.Width / projectionScale) - shapeTranslateX;
        double glyphBottom = (0d / projectionScale) - shapeTranslateY;
        double glyphTop = (settings.Height / projectionScale) - shapeTranslateY;

        Assert.Equal(planeLeft, result.Placement.PlaneLeft, 6);
        Assert.Equal(-glyphTop, result.Placement.PlaneTop, 6);
        Assert.Equal(planeRight, result.Placement.PlaneRight, 6);
        Assert.Equal(-glyphBottom, result.Placement.PlaneBottom, 6);
    }

    [Fact]
    public void MsdfSharpGenerator_PlacementIsDeterministic()
    {
        MsdfSharpDistanceFieldGenerator generator = new();
        GlyphOutline outline = MsdfSharpTestHelpers.CreateQuadraticOutline();
        MsdfGenerationSettings settings = MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf);

        GeneratedGlyphDistanceField first = generator.Generate(outline, settings);
        GeneratedGlyphDistanceField second = generator.Generate(outline, settings);

        Assert.Equal(first.Placement, second.Placement);
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
