using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Tests.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Generation.MsdfSharp;

public sealed class TypographyToMsdfSharpPipelineTests
{
    private static readonly GlyphOutlineLoadOptions OutlineOptions = new(32, 0, GlyphHintingMode.None, normalizeToEm: true);

    [Fact]
    public async Task Pipeline_GeneratesMsdfForA()
    {
        GlyphGenerationPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());

        GlyphGenerationResult result = await pipeline.GenerateAsync(
            GlyphKey.FromCodepoint(TypographyFixtureFont.Face, 'A', 32),
            OutlineOptions,
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf));

        Assert.True(result.Success);
        Assert.NotNull(result.DistanceField);
        Assert.Equal(32, result.DistanceField.Width);
        Assert.Equal(32, result.DistanceField.Height);
        Assert.Equal(3, result.DistanceField.ChannelCount);
        MsdfSharpTestHelpers.AssertFiniteNonUniform(result.DistanceField);
    }

    [Fact]
    public async Task Pipeline_GeneratesMsdfForLowercaseAndDigit()
    {
        GlyphGenerationPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());

        GlyphGenerationResult lowercase = await pipeline.GenerateAsync(
            GlyphKey.FromCodepoint(TypographyFixtureFont.Face, 'a', 32),
            OutlineOptions,
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf));

        GlyphGenerationResult digit = await pipeline.GenerateAsync(
            GlyphKey.FromCodepoint(TypographyFixtureFont.Face, '0', 32),
            OutlineOptions,
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf));

        Assert.True(lowercase.Success);
        Assert.True(digit.Success);
        Assert.NotNull(lowercase.DistanceField);
        Assert.NotNull(digit.DistanceField);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(96)]
    [InlineData(128)]
    public async Task Pipeline_GeneratesFiniteMsdfForPeriod(int size)
    {
        GlyphOutlineLoadOptions outlineOptions = new(size, 0, GlyphHintingMode.None, normalizeToEm: true);
        int fieldDimension = Machina.Fonts.ReferenceRendering.ExperimentalMsdfSizing.ComputeFieldDimension(size);
        GlyphGenerationPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());

        GlyphGenerationResult result = await pipeline.GenerateAsync(
            GlyphKey.FromCodepoint(TypographyFixtureFont.Face, '.', size),
            outlineOptions,
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf, fieldDimension, fieldDimension));

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.NotNull(result.DistanceField);
        MsdfSharpTestHelpers.AssertFiniteNonUniform(result.DistanceField);
        float[] values = result.DistanceField.Data.Span.ToArray();
        Assert.True(values.Max() > 0.5f, $"range={values.Min()}..{values.Max()}");
    }

    [Theory]
    [InlineData('Q')]
    [InlineData('g')]
    [InlineData('j')]
    [InlineData('p')]
    [InlineData('q')]
    [InlineData('y')]
    public async Task Pipeline_GeneratesFiniteMsdfForStressGlyphs(char value)
    {
        GlyphGenerationPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());

        GlyphGenerationResult result = await pipeline.GenerateAsync(
            GlyphKey.FromCodepoint(TypographyFixtureFont.Face, value, 64),
            new GlyphOutlineLoadOptions(64, 0, GlyphHintingMode.None, normalizeToEm: true),
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf, 64, 64));

        Assert.True(result.Success);
        Assert.NotNull(result.DistanceField);
        MsdfSharpTestHelpers.AssertFiniteNonUniform(result.DistanceField);
    }

    [Fact]
    public async Task Pipeline_GeneratesDeterministicOutput()
    {
        GlyphGenerationPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());
        GlyphKey key = GlyphKey.FromCodepoint(TypographyFixtureFont.Face, '&', 32);
        MsdfGenerationSettings settings = MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Mtsdf);

        GlyphGenerationResult first = await pipeline.GenerateAsync(key, OutlineOptions, settings);
        GlyphGenerationResult second = await pipeline.GenerateAsync(key, OutlineOptions, settings);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotNull(first.DistanceField);
        Assert.NotNull(second.DistanceField);
        Assert.Equal(first.DistanceField.Data.ToArray(), second.DistanceField.Data.ToArray());
    }

    [Fact]
    public async Task Pipeline_MissingGlyphDoesNotCallGenerator()
    {
        CountingGenerator generator = new(new MsdfSharpDistanceFieldGenerator());
        GlyphGenerationPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            generator);

        GlyphGenerationResult result = await pipeline.GenerateAsync(
            GlyphKey.FromCodepoint(TypographyFixtureFont.Face, 0xE000, 32),
            OutlineOptions,
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf));

        Assert.False(result.Success);
        Assert.Equal(0, generator.CallCount);
        Assert.Null(result.DistanceField);
    }

    [Fact]
    public async Task Pipeline_SpacePolicy_IsDocumentedAndTested()
    {
        GlyphGenerationPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());

        GlyphGenerationResult result = await pipeline.GenerateAsync(
            GlyphKey.FromCodepoint(TypographyFixtureFont.Face, ' ', 32),
            OutlineOptions,
            MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Sdf));

        Assert.False(result.Success);
        Assert.NotNull(result.DistanceField);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.EmptyOutline);
        Assert.All(result.DistanceField.Data.ToArray(), static value => Assert.Equal(0f, value));
    }

    private sealed class CountingGenerator : IGlyphDistanceFieldGenerator
    {
        private readonly IGlyphDistanceFieldGenerator inner;

        public CountingGenerator(IGlyphDistanceFieldGenerator inner)
        {
            this.inner = inner;
        }

        public int CallCount { get; private set; }

        public GeneratedGlyphDistanceField Generate(
            GlyphOutline outline,
            MsdfGenerationSettings settings,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return inner.Generate(outline, settings, cancellationToken);
        }
    }
}
