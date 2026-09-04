using Copeland.Profile;
using Machina.VectorAssets;
using Xunit;

namespace Machina.Fonts.Tests.VectorAssets;

public sealed class VectorIconCompilerM5Tests
{
    [Fact]
    public void Parser_SupportsLineQuadraticCubicCloseAndMultipleContours()
    {
        VectorSourceParseResult result = SvgVectorIconParser.Parse("""
            <svg viewBox="0 0 20 20"><path d="M1 1 L10 1 Q15 1 15 6 C15 12 8 18 1 10 Z M5 5 L7 5 L7 7 L5 7 Z"/></svg>
            """);

        Assert.True(result.Success, Describe(result));
        Assert.Equal(2, result.Shape!.Contours.Count);
        Assert.Contains(result.Shape.Contours[0].Segments, static segment => segment is VectorLine);
        Assert.Contains(result.Shape.Contours[0].Segments, static segment => segment is VectorQuadratic);
        Assert.Contains(result.Shape.Contours[0].Segments, static segment => segment is VectorCubic);
    }

    [Theory]
    [InlineData("translate(2 3)")]
    [InlineData("scale(2 .5)")]
    [InlineData("rotate(30 10 10)")]
    [InlineData("matrix(1 0 0 1 3 4)")]
    public void Parser_FlattensBoundedTransforms(string transform)
    {
        VectorSourceParseResult result = SvgVectorIconParser.Parse($"""
            <svg viewBox="0 0 40 40"><g transform="{transform}"><rect x="5" y="5" width="10" height="8"/></g></svg>
            """);

        Assert.True(result.Success, Describe(result));
        Assert.All(result.Shape!.Contours.SelectMany(static contour => contour.Segments), static segment => Assert.NotNull(segment));
    }

    [Fact]
    public void Parser_NormalizesViewBoxAndPreservesNonSquareBounds()
    {
        VectorSourceParseResult result = SvgVectorIconParser.Parse("<svg viewBox='10 20 40 20'><rect x='10' y='20' width='40' height='20'/></svg>");

        Assert.True(result.Success, Describe(result));
        Assert.Equal(new VectorBounds(0, 0, 40, 20), result.Shape!.Bounds);
    }

    [Theory]
    [InlineData("<svg viewBox='0 0 10 10'><linearGradient/></svg>", "linearGradient")]
    [InlineData("<svg viewBox='0 0 10 10'><text>bad</text></svg>", "text")]
    [InlineData("<svg viewBox='0 0 10 10'><path style='fill:red' d='M0 0 L1 0 L1 1 Z'/></svg>", "path")]
    [InlineData("<svg viewBox='0 0 10 10'><path d='M0 0 A1 1 0 0 0 2 2 Z'/></svg>", "path")]
    [InlineData("<svg viewBox='0 0 10 10'></svg>", "svg")]
    public void Parser_RejectsUnsupportedMalformedAndEmptySources(string source, string element)
    {
        VectorSourceParseResult result = SvgVectorIconParser.Parse(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Element == element);
    }

    [Fact]
    public void Parser_RemovesOnlyExactZeroLengthSegmentsAndRejectsDegenerateOnly()
    {
        VectorSourceParseResult mixed = SvgVectorIconParser.Parse("<svg viewBox='0 0 10 10'><path d='M1 1 L1 1 L9 1 L9 9 Z'/></svg>");
        VectorSourceParseResult degenerate = SvgVectorIconParser.Parse("<svg viewBox='0 0 10 10'><path d='M1 1 L1 1 Z'/></svg>");

        Assert.True(mixed.Success, Describe(mixed));
        Assert.Equal(3, mixed.Shape!.Contours[0].Segments.Count);
        Assert.False(degenerate.Success);
    }

    [Fact]
    public void Compiler_IsContentAddressedDeterministicAndFinite()
    {
        VectorIconFixture source = VectorIconFixtures.Canonical.Single(static fixture => fixture.Name == "Heart");
        VectorIconCompilationResult first = VectorIconMsdfCompiler.CompileSvg(source.Source, "one.svg");
        VectorIconCompilationResult second = VectorIconMsdfCompiler.CompileSvg(source.Source, "renamed.svg");

        Assert.True(first.Success, Describe(first));
        Assert.True(second.Success, Describe(second));
        Assert.Equal(first.Artifact!.Identity, second.Artifact!.Identity);
        Assert.Equal(first.Artifact.FieldHash, second.Artifact.FieldHash);
        Assert.Equal(first.Artifact.Shape.NormalizedGeometryHash, second.Artifact.Shape.NormalizedGeometryHash);
        Assert.DoesNotContain(first.Artifact.FieldPixels.Span.ToArray(), static value => !float.IsFinite(value));
        Assert.NotEqual(first.Artifact.PlaneBounds, first.Artifact.FieldBounds);
    }

    [Fact]
    public void CanonicalCorpus_CompilesCurvesHolesConcavityRotationAndWideShape()
    {
        IReadOnlyDictionary<string, VectorIconMsdfArtifact> artifacts = VectorIconFixtures.CompileCanonical();

        Assert.Equal(8, artifacts.Count);
        Assert.Contains(artifacts["Heart"].Shape.Contours.SelectMany(static contour => contour.Segments), static segment => segment is VectorCubic);
        Assert.True(artifacts["InfoCircle"].Shape.Contours.Count >= 3);
        Assert.True(artifacts["Folder"].PlaneBounds.Width > artifacts["Folder"].PlaneBounds.Height);
        Assert.Equal(8, artifacts.Values.Select(static artifact => artifact.Identity).Distinct().Count());
    }

    [Fact]
    public void Atlas_IsDeterministicExplicitlyOrientedAndContentAddressed()
    {
        VectorIconMsdfArtifact[] artifacts = VectorIconFixtures.CompileCanonical().Values.ToArray();
        VectorIconAtlas first = VectorIconAtlasPacker.Pack(artifacts);
        VectorIconAtlas second = VectorIconAtlasPacker.Pack(artifacts.Reverse().ToArray());

        Assert.Equal(VectorAtlasRowOrder.TopToBottom, first.RowOrder);
        Assert.Equal(first.AtlasHash, second.AtlasHash);
        Assert.Equal(first.Entries, second.Entries);
        Assert.Equal(8, first.Entries.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => VectorIconAtlasPacker.Pack(artifacts, rowOrder: VectorAtlasRowOrder.Unspecified));
    }

    [Fact]
    public void CpuReference_QualifiesCanonicalCorpusAtRequiredSizes()
    {
        IReadOnlyDictionary<string, VectorIconMsdfArtifact> artifacts = VectorIconFixtures.CompileCanonical();
        int[] sizes = [16, 24, 32, 64, 128];

        VectorIconParityMetrics[] metrics = artifacts.Values
            .SelectMany(artifact => sizes.Select(size => VectorIconCpuQualification.Compare(artifact, size)))
            .ToArray();

        Assert.Equal(40, metrics.Length);
        Assert.All(metrics, metric => Assert.True(metric.IntersectionOverUnion >= 0.72, $"{metric.Size}px IoU={metric.IntersectionOverUnion}"));
        Assert.All(metrics, metric => Assert.True(metric.MeanEdgeDistance <= 1.5, $"{metric.Size}px edge={metric.MeanEdgeDistance}"));
    }

    private static string Describe(VectorSourceParseResult result)
    {
        return string.Join("; ", result.Diagnostics.Select(static diagnostic => $"{diagnostic.Element}/{diagnostic.Attribute}: {diagnostic.Reason}"));
    }

    private static string Describe(VectorIconCompilationResult result)
    {
        return string.Join("; ", result.Diagnostics.Select(static diagnostic => $"{diagnostic.Element}/{diagnostic.Attribute}: {diagnostic.Reason}"));
    }
}
