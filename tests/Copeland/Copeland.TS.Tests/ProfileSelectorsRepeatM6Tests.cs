using Copeland.Profile;
using Copeland.TS.Profiles;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProfileSelectorsRepeatM6Tests
{
    [Fact]
    public void Named_and_feature_selectors_drive_linear_repeat_through_current_ssa_states()
    {
        ProfileCompilationResult result = ProfileTsxCompiler.Compile(MechanicalSource);

        Assert.True(result.Success, Diagnostics(result));
        RepeatLinearPatternProfileOperation repeat = Assert.IsType<RepeatLinearPatternProfileOperation>(result.Definition!.Operations[1]);
        Assert.IsType<AlongProfileSelector>(repeat.Target);
        Assert.Equal(result.States[0].ContourHash, result.States[1].ContourHash);
        Assert.Equal(4, result.States[2].LoweredReplacements.Count);
        Assert.Equal(Enumerable.Range(0, 4), result.States[2].LoweredReplacements.Select(item => item.RepetitionIndex));
        Assert.All(
            result.States[2].Segments.Where(segment => segment.ProvenanceFeatureId == "TopNotches"),
            segment => Assert.StartsWith("feature:TopNotches/instance:", segment.Id, StringComparison.Ordinal));
        AssertClosed(result.Shape!.Contours[0]);
    }

    [Fact]
    public void Semantic_name_survives_unrelated_earlier_topology_insertion()
    {
        ProfileCompilationResult result = ProfileTsxCompiler.Compile(TopologyShiftSource);

        Assert.True(result.Success, Diagnostics(result));
        ProfileLoweredReplacementSummary lowered = Assert.Single(result.States[3].LoweredReplacements);
        Assert.Equal(1, result.States[1].Segments.FindIndex(segment => segment.Id == "contour:0/segment:1"));
        Assert.True(lowered.TargetSegmentIndex > 1);
        AssertClosed(result.Shape!.Contours[0]);
    }

    [Fact]
    public void Curved_concept_path_uses_arc_length_stations_and_tangent_aligned_boundary_chords()
    {
        ProfileCompilationResult result = ProfileTsxCompiler.Compile(CurvedPathSource);

        Assert.True(result.Success, Diagnostics(result));
        RepeatAlongPathProfileOperation repeat = Assert.IsType<RepeatAlongPathProfileOperation>(result.Definition!.Operations[1]);
        Assert.Equal(ProfileCurveKind.Spline, repeat.Path.Segment.Kind);
        Assert.Equal(4, result.States[2].LoweredReplacements.Count);
        Assert.Equal(8, result.States[2].Segments.Count(segment => segment.ProvenanceFeatureId == "Scallops"));
        AssertClosed(result.Shape!.Contours[0]);
    }

    [Fact]
    public void Selectors_are_ownerless_and_resolved_spans_remain_owner_bound()
    {
        var selector = new AlongProfileSelector(new FeatureSpanProfileSelector("TopTarget"), 0.2, 0.8);
        var span = new ProfileSpanSelection("Current", 2, 3);

        Assert.DoesNotContain(selector.GetType().GetProperties(), property => property.Name.Contains("Owner", StringComparison.Ordinal));
        Assert.Equal("Current", span.OwnerState);
        Assert.Equal(selector.SemanticHash, new AlongProfileSelector(new FeatureSpanProfileSelector("TopTarget"), 0.2, 0.8).SemanticHash);
    }

    [Fact]
    public void Imported_templates_return_selector_pattern_and_operation_with_match_and_with()
    {
        const string source = """
            import { NameSpan, Profile, ProfileEdge, Rectangle, SelectSegment, SpanOf } from "./Profile";

            export default (
                <Profile name="TemplateRepeat" base={Rectangle({ width: 80.0, height: 40.0 })}>
                    {NameSpan({ id: "TopTarget", as: "Named", name: "TopEdge", target: SpanOf([SelectSegment("Base", 0)]) })}
                    {instantiate Decoration<>}
                    {Yield(Repeated)}
                </Profile>
            );
            """;
        const string helpers = """
            import { AlongSpan, FeatureSpan, NamedSpan, Point, ProfileOperation, ProfileSelector, ProfileSpanPattern, RepeatLinear, VNotch } from "./Profile";

            export enum TargetMode { Feature, Named }

            export function TargetByMode(mode: TargetMode): ProfileSelector {
                return match mode {
                    Feature => AlongSpan(FeatureSpan("TopTarget"), 0.1, 0.9),
                    Named => AlongSpan(NamedSpan("TopEdge"), 0.1, 0.9)
                };
            }

            template<> DecorationTarget: ProfileSelector {
                return TargetByMode(TargetMode.Named);
            }

            template<> DecorationPattern: ProfileSpanPattern {
                return VNotch({ start: Point(0.0, 0.0), tip: Point(0.5, -2.0), end: Point(1.0, 0.0) });
            }

            template<> Decoration: ProfileOperation[] {
                const target: ProfileSelector = instantiate DecorationTarget<>;
                const pattern: ProfileSpanPattern = instantiate DecorationPattern<>;
                return [RepeatLinear({
                    id: "TemplateNotches", as: "Repeated", target: target,
                    pattern: pattern with { segments: pattern.segments },
                    count: 4, spacing: 14.0, footprint: 6.0, offset: 1.0
                })];
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(
            source,
            helpers,
            templateSourcePath: "ProfileSelectorTools.ts");

        Assert.True(result.Success, Diagnostics(result));
        RepeatLinearPatternProfileOperation operation = Assert.IsType<RepeatLinearPatternProfileOperation>(result.Definition!.Operations[1]);
        Assert.Equal("Decoration", operation.TemplateProvenance!.TemplateName);
        Assert.IsType<AlongProfileSelector>(operation.Target);
    }

    [Theory]
    [InlineData("FeatureSpan(\"Missing\")", "COPE-PROFILE-0056")]
    [InlineData("AlongSpan(FeatureSpan(\"TopTarget\"), 0.8, 0.2)", "COPE-PROFILE-0060")]
    public void Invalid_selectors_fail_deterministically(string selector, string diagnosticId)
    {
        string source = MechanicalSource.Replace(
            "AlongSpan(FeatureSpan(\"TopTarget\"), 0.1, 0.9)",
            selector,
            StringComparison.Ordinal);

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Theory]
    [InlineData(0, 14.0, "COPE-PROFILE-0061")]
    [InlineData(-2, 14.0, "COPE-PROFILE-0061")]
    [InlineData(4, -1.0, "COPE-PROFILE-0061")]
    [InlineData(257, 1.0, "COPE-PROFILE-0061")]
    public void Invalid_repeat_configuration_is_rejected(int count, double spacing, string diagnosticId)
    {
        string source = MechanicalSource
            .Replace("count: 4", $"count: {count}", StringComparison.Ordinal)
            .Replace("spacing: 14.0", $"spacing: {spacing:R}", StringComparison.Ordinal);

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Duplicate_name_and_zero_length_path_are_rejected()
    {
        string duplicate = MechanicalSource.Replace(
            "{RepeatLinear({",
            "{NameSpan({ id: \"Duplicate\", as: \"Duplicate\", name: \"TopEdge\", target: SpanOf([SelectSegment(\"Named\", 0)]) })}\n            {RepeatLinear({",
            StringComparison.Ordinal);
        ProfileCompilationResult duplicateResult = ProfileTsxCompiler.Compile(duplicate);
        Assert.Contains(duplicateResult.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-0059");

        string zeroLength = CurvedPathSource.Replace("Point(40.0, 10.0),", "Point(30.0, 20.0),", StringComparison.Ordinal);
        ProfileCompilationResult pathResult = ProfileTsxCompiler.Compile(zeroLength);
        Assert.Contains(pathResult.Diagnostics, diagnostic => diagnostic.Id is "COPE-PROFILE-0063" or "COPE-PROFILE-0064");
    }

    [Fact]
    public void Disconnected_selector_overlap_and_path_cusp_are_rejected()
    {
        string disconnected = MechanicalSource.Replace(
            "{Yield(Repeated)}",
            "{RepeatLinear({ id: \"Again\", as: \"Again\", target: FeatureSpan(\"TopNotches\"), pattern: NotchPattern, count: 1, spacing: 2.0, footprint: 1.0 })}\n                {Yield(Again)}",
            StringComparison.Ordinal);
        Assert.Contains(
            ProfileTsxCompiler.Compile(disconnected).Diagnostics,
            diagnostic => diagnostic.Id == "COPE-PROFILE-0058");

        string overlap = MechanicalSource.Replace("spacing: 14.0, footprint: 6.0", "spacing: 4.0, footprint: 6.0", StringComparison.Ordinal);
        Assert.Contains(
            ProfileTsxCompiler.Compile(overlap).Diagnostics,
            diagnostic => diagnostic.Id == "COPE-PROFILE-0065");

        string cusp = CurvedPathSource.Replace(
            "control1: Point(35.52284749830794, 20.0)",
            "control1: Point(30.0, 20.0)",
            StringComparison.Ordinal);
        Assert.Contains(
            ProfileTsxCompiler.Compile(cusp).Diagnostics,
            diagnostic => diagnostic.Id == "COPE-PROFILE-0063");
    }

    private static void AssertClosed(VectorContour contour)
    {
        Assert.Equal(Start(contour.Segments[0]), End(contour.Segments[^1]));
        for (int index = 1; index < contour.Segments.Count; index++)
        {
            Assert.Equal(End(contour.Segments[index - 1]), Start(contour.Segments[index]));
        }
    }

    private static VectorPoint Start(VectorSegment segment) => segment switch
    {
        VectorLine line => line.P0,
        VectorQuadratic quadratic => quadratic.P0,
        VectorCubic cubic => cubic.P0,
        _ => throw new InvalidOperationException(),
    };

    private static VectorPoint End(VectorSegment segment) => segment switch
    {
        VectorLine line => line.P1,
        VectorQuadratic quadratic => quadratic.P2,
        VectorCubic cubic => cubic.P3,
        _ => throw new InvalidOperationException(),
    };

    private static string Diagnostics(ProfileCompilationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Id}: {item.Message}"));

    private const string MechanicalSource = """
        import { AlongSpan, FeatureSpan, NameSpan, Point, Profile, ProfileEdge, ProfileSpanPattern, Rectangle, RepeatLinear, SelectSegment, SpanOf, VNotch } from "./Profile";

        const NotchPattern: ProfileSpanPattern = VNotch({
            start: Point(0.0, 0.0), tip: Point(0.5, -2.0), end: Point(1.0, 0.0)
        });
        export default (
            <Profile name="SlottedPlate" base={Rectangle({ width: 80.0, height: 40.0 })}>
                {NameSpan({ id: "TopTarget", as: "Named", name: "TopEdge", target: SpanOf([SelectSegment("Base", 0)]) })}
                {RepeatLinear({
                    id: "TopNotches", as: "Repeated",
                    target: AlongSpan(FeatureSpan("TopTarget"), 0.1, 0.9),
                    pattern: NotchPattern with { segments: NotchPattern.segments },
                    count: 4, spacing: 14.0, footprint: 6.0, offset: 1.0
                })}
                {Yield(Repeated)}
            </Profile>
        );
        """;

    private const string TopologyShiftSource = """
        import { DovetailTab, NamedSpan, NameSpan, Point, Profile, ProfileEdge, ProfileSpanPattern, Rectangle, RepeatLinear, ReplaceSpanWithPattern, SelectSegment, SpanOf, VNotch } from "./Profile";

        const TabPattern: ProfileSpanPattern = DovetailTab({
            start: Point(0.0, 0.0), leftShoulder: Point(0.3, 3.0),
            rightShoulder: Point(0.7, 3.0), end: Point(1.0, 0.0)
        });
        const NotchPattern: ProfileSpanPattern = VNotch({
            start: Point(0.0, 0.0), tip: Point(0.5, -2.0), end: Point(1.0, 0.0)
        });
        export default (
            <Profile name="StableTopology" base={Rectangle({ width: 80.0, height: 40.0 })}>
                {NameSpan({ id: "RightTarget", as: "Named", name: "RightEdge", target: SpanOf([SelectSegment("Base", 1)]) })}
                {ReplaceSpanWithPattern({ id: "EarlierTab", as: "Shifted", target: SpanOf([SelectSegment("Named", 0)]), pattern: TabPattern })}
                {RepeatLinear({ id: "RightNotch", as: "Done", target: NamedSpan("RightEdge"), pattern: NotchPattern, count: 1, spacing: 4.0, footprint: 4.0, offset: 4.0 })}
                {Yield(Done)}
            </Profile>
        );
        """;

    private const string CurvedPathSource = """
        import { ConceptPath, CurvedPath, FeatureSpan, NameSpan, Point, Profile, ProfileEdge, ProfileSpanPattern, RepeatAlongPath, RoundedRectangle, SelectSegment, SpanOf, Spline, VNotch } from "./Profile";

        const Scallop: ProfileSpanPattern = VNotch({
            start: Point(0.0, 0.0), tip: Point(0.5, -0.5), end: Point(1.0, 0.0)
        });
        const Guide: ConceptPath = CurvedPath(
            Point(30.0, 20.0),
            Point(40.0, 10.0),
            Spline({ control1: Point(35.52284749830794, 20.0), control2: Point(40.0, 15.522847498307936) })
        );
        export default (
            <Profile name="CurvedScallops" base={RoundedRectangle({ width: 80.0, height: 40.0, radius: 10.0 })}>
                {NameSpan({ id: "CurveTarget", as: "NamedCurve", name: "TopRightCurve", target: SpanOf([SelectSegment("Base", 1)]) })}
                {RepeatAlongPath({
                    id: "Scallops", as: "Repeated", target: FeatureSpan("CurveTarget"),
                    path: Guide, pattern: Scallop, count: 4, spacing: 3.4, footprint: 1.4, offset: 0.4
                })}
                {Yield(Repeated)}
            </Profile>
        );
        """;
}

internal static class ProfileSegmentSummaryListExtensions
{
    public static int FindIndex(this IReadOnlyList<ProfileSegmentSummary> values, Func<ProfileSegmentSummary, bool> predicate)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (predicate(values[index]))
            {
                return index;
            }
        }
        return -1;
    }
}
