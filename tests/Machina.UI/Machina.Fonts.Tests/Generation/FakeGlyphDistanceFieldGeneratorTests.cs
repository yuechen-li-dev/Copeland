using Xunit;
using Machina.Fonts.Generation;

namespace Machina.Fonts.Tests.Generation;

public sealed class FakeGlyphDistanceFieldGeneratorTests
{
    [Theory]
    [InlineData(DistanceFieldKind.Sdf, 1)]
    [InlineData(DistanceFieldKind.Psdf, 1)]
    [InlineData(DistanceFieldKind.Msdf, 3)]
    [InlineData(DistanceFieldKind.Mtsdf, 4)]
    public void Generate_ProducesExpectedChannelCountForEachKind(DistanceFieldKind kind, int expectedChannelCount)
    {
        FakeGlyphDistanceFieldGenerator generator = new();
        GeneratedGlyphDistanceField result = generator.Generate(CreateOutline('A'), CreateSettings(kind));

        Assert.Equal(expectedChannelCount, result.ChannelCount);
    }

    [Fact]
    public void Generate_ProducesDataWithExpectedLength()
    {
        FakeGlyphDistanceFieldGenerator generator = new();
        GeneratedGlyphDistanceField result = generator.Generate(CreateOutline('A'), CreateSettings(DistanceFieldKind.Msdf));

        Assert.Equal(result.Width * result.Height * result.ChannelCount, result.Data.Length);
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        FakeGlyphDistanceFieldGenerator generator = new();
        GlyphOutline outline = CreateOutline('A');
        MsdfGenerationSettings settings = CreateSettings(DistanceFieldKind.Mtsdf);

        GeneratedGlyphDistanceField first = generator.Generate(outline, settings);
        GeneratedGlyphDistanceField second = generator.Generate(outline, settings);

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(first.Metrics, second.Metrics);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Kind, second.Kind);
        Assert.Equal(first.ChannelCount, second.ChannelCount);
        Assert.Equal(first.Data.ToArray(), second.Data.ToArray());
    }

    [Fact]
    public void Generate_RejectsInvalidSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MsdfGenerationSettings(DistanceFieldKind.Sdf, 0, 4, 1, 1, "simple", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MsdfGenerationSettings(DistanceFieldKind.Sdf, 4, 4, 0, 1, "simple", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MsdfGenerationSettings(DistanceFieldKind.Sdf, 4, 4, 1, 0, "simple", 1));
    }

    [Fact]
    public void Generate_RejectsEmptyOutlineForNonWhitespaceIfPolicyRequires()
    {
        FakeGlyphDistanceFieldGenerator generator = new();
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 12);
        GlyphOutline outline = new(
            key,
            new GlyphMetrics(8, 0, 10, 8, 12),
            new GlyphBounds(0, 0, 0, 0),
            []);

        GeneratedGlyphDistanceField result = generator.Generate(outline, CreateSettings(DistanceFieldKind.Sdf));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.EmptyOutline
                && diagnostic.Severity == FontGenerationDiagnosticSeverity.Error);
    }

    [Fact]
    public void Generate_RespectsCancellation()
    {
        FakeGlyphDistanceFieldGenerator generator = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => generator.Generate(CreateOutline('A'), CreateSettings(DistanceFieldKind.Sdf), cts.Token));
    }

    private static GlyphOutline CreateOutline(char value)
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), value, 12);
        GlyphMetrics metrics = new(8, 0, 10, 8, 12);
        GlyphContour contour = new(
        [
            new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(8, 0)),
            new GlyphLineSegment(new GlyphPoint(8, 0), new GlyphPoint(8, 12)),
            new GlyphLineSegment(new GlyphPoint(8, 12), new GlyphPoint(0, 12)),
            new GlyphLineSegment(new GlyphPoint(0, 12), new GlyphPoint(0, 0)),
        ]);

        return new GlyphOutline(key, metrics, new GlyphBounds(0, 0, 8, 12), [contour]);
    }

    private static MsdfGenerationSettings CreateSettings(DistanceFieldKind kind)
    {
        return new MsdfGenerationSettings(kind, 6, 5, 4, 1, "simple", 2);
    }
}
