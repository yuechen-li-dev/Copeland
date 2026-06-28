using Xunit;
using Machina.Fonts.Generation;

namespace Machina.Fonts.Tests.Generation;

public sealed class GlyphGenerationPipelineTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsDistanceFieldForValidGlyph()
    {
        GlyphGenerationPipeline pipeline = new(new FakeGlyphOutlineSource(), new FakeGlyphDistanceFieldGenerator());
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 12, MachinaFontWeight.Bold, MachinaFontSlant.Italic);

        GlyphGenerationResult result = await pipeline.GenerateAsync(
            key,
            new GlyphOutlineLoadOptions(12, 0, GlyphHintingMode.None, normalizeToEm: true),
            new MsdfGenerationSettings(DistanceFieldKind.Msdf, 6, 5, 4, 1, "simple", 2));

        Assert.True(result.Success);
        Assert.NotNull(result.Outline);
        Assert.NotNull(result.DistanceField);
        Assert.Equal(key, result.Outline.Key);
        Assert.Equal(key, result.DistanceField.Key);
    }

    [Fact]
    public async Task GenerateAsync_DoesNotCallGeneratorWhenOutlineMissing()
    {
        CountingGenerator generator = new();
        GlyphGenerationPipeline pipeline = new(new FakeGlyphOutlineSource(['?']), generator);

        GlyphGenerationResult result = await pipeline.GenerateAsync(
            GlyphKey.FromChar(new FontFaceId("Fake"), '?', 12),
            new GlyphOutlineLoadOptions(12, 0, GlyphHintingMode.None, normalizeToEm: true),
            new MsdfGenerationSettings(DistanceFieldKind.Sdf, 4, 4, 2, 1, "simple", 1));

        Assert.False(result.Success);
        Assert.Equal(0, generator.CallCount);
        Assert.Null(result.DistanceField);
    }

    [Fact]
    public async Task GenerateAsync_CombinesDiagnostics()
    {
        DiagnosticOutlineSource outlineSource = new();
        DiagnosticGenerator generator = new();
        GlyphGenerationPipeline pipeline = new(outlineSource, generator);

        GlyphGenerationResult result = await pipeline.GenerateAsync(
            GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 12),
            new GlyphOutlineLoadOptions(12, 0, GlyphHintingMode.None, normalizeToEm: true),
            new MsdfGenerationSettings(DistanceFieldKind.Sdf, 4, 4, 2, 1, "simple", 1));

        Assert.False(result.Success);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.UnsupportedGlyph);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.DistanceFieldGenerationFailed);
    }

    [Fact]
    public async Task GenerateAsync_PropagatesCancellation()
    {
        GlyphGenerationPipeline pipeline = new(new FakeGlyphOutlineSource(), new FakeGlyphDistanceFieldGenerator());
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.GenerateAsync(
            GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 12),
            new GlyphOutlineLoadOptions(12, 0, GlyphHintingMode.None, normalizeToEm: true),
            new MsdfGenerationSettings(DistanceFieldKind.Sdf, 4, 4, 2, 1, "simple", 1),
            cts.Token).AsTask());
    }

    [Fact]
    public async Task GenerateAsync_IsDeterministic()
    {
        GlyphGenerationPipeline pipeline = new(new FakeGlyphOutlineSource(), new FakeGlyphDistanceFieldGenerator());
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), '&', 12);
        GlyphOutlineLoadOptions options = new(12, 0, GlyphHintingMode.Auto, normalizeToEm: false);
        MsdfGenerationSettings settings = new(DistanceFieldKind.Mtsdf, 5, 4, 2, 1.5, "simple", 2);

        GlyphGenerationResult first = await pipeline.GenerateAsync(key, options, settings);
        GlyphGenerationResult second = await pipeline.GenerateAsync(key, options, settings);

        Assert.NotNull(first.Outline);
        Assert.NotNull(second.Outline);
        Assert.Equal(first.Outline.Key, second.Outline.Key);
        Assert.Equal(first.Outline.Metrics, second.Outline.Metrics);
        Assert.Equal(first.Outline.Bounds, second.Outline.Bounds);
        Assert.Equal(first.Outline.Contours.Count, second.Outline.Contours.Count);
        Assert.NotNull(first.DistanceField);
        Assert.NotNull(second.DistanceField);
        Assert.Equal(first.DistanceField.Key, second.DistanceField.Key);
        Assert.Equal(first.DistanceField.Metrics, second.DistanceField.Metrics);
        Assert.Equal(first.DistanceField.Width, second.DistanceField.Width);
        Assert.Equal(first.DistanceField.Height, second.DistanceField.Height);
        Assert.Equal(first.DistanceField.Kind, second.DistanceField.Kind);
        Assert.Equal(first.DistanceField.ChannelCount, second.DistanceField.ChannelCount);
        Assert.Equal(first.DistanceField!.Data.ToArray(), second.DistanceField!.Data.ToArray());
    }

    private sealed class CountingGenerator : IGlyphDistanceFieldGenerator
    {
        public int CallCount { get; private set; }

        public GeneratedGlyphDistanceField Generate(
            GlyphOutline outline,
            MsdfGenerationSettings settings,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return new FakeGlyphDistanceFieldGenerator().Generate(outline, settings, cancellationToken);
        }
    }

    private sealed class DiagnosticOutlineSource : IGlyphOutlineSource
    {
        public ValueTask<GlyphOutlineLoadResult> LoadGlyphOutlineAsync(
            FontFaceId face,
            int codepoint,
            GlyphOutlineLoadOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GlyphKey key = GlyphKey.FromCodepoint(face, codepoint, options.EmSize);
            GlyphMetrics metrics = new(8, 0, 10, 8, 12);
            GlyphContour contour = new([new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(1, 1))]);
            GlyphOutline outline = new(key, metrics, new GlyphBounds(0, 0, 1, 1), [contour]);
            FontGenerationDiagnostic diagnostic = new(
                FontGenerationDiagnosticSeverity.Warning,
                FontGenerationDiagnosticCode.UnsupportedGlyph,
                "Testing diagnostic aggregation.",
                key);

            return ValueTask.FromResult(new GlyphOutlineLoadResult(true, outline, metrics, [diagnostic]));
        }
    }

    private sealed class DiagnosticGenerator : IGlyphDistanceFieldGenerator
    {
        public GeneratedGlyphDistanceField Generate(
            GlyphOutline outline,
            MsdfGenerationSettings settings,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FontGenerationDiagnostic diagnostic = new(
                FontGenerationDiagnosticSeverity.Error,
                FontGenerationDiagnosticCode.DistanceFieldGenerationFailed,
                "Testing generator diagnostics.",
                outline.Key);

            int channelCount = settings.Kind is DistanceFieldKind.Msdf ? 3 : settings.Kind is DistanceFieldKind.Mtsdf ? 4 : 1;
            float[] data = new float[settings.Width * settings.Height * channelCount];

            return new GeneratedGlyphDistanceField(
                outline.Key,
                outline.Metrics,
                settings.Width,
                settings.Height,
                settings.Kind,
                channelCount,
                data,
                [diagnostic]);
        }
    }
}
