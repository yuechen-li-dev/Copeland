using Copeland.Profile;
using Copeland.TS.Profiles;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProfileSpanPatternM5Tests
{
    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void Authored_gear_tooth_pattern_repeats_through_sequential_replace_span(int count)
    {
        ProfileCompilationResult first = CompileGear(count);
        ProfileCompilationResult second = CompileGear(count);

        Assert.True(first.Success, Diagnostics(first));
        Assert.Equal(first.ProfileIrHash, second.ProfileIrHash);
        Assert.Equal(first.CanonicalContourHash, second.CanonicalContourHash);
        Assert.Equal(first.Svg, second.Svg);
        RepeatRadialPatternProfileOperation operation = Assert.IsType<RepeatRadialPatternProfileOperation>(first.Definition!.Operations[0]);
        Assert.Equal(3, operation.Pattern.Segments.Count);
        Assert.Equal(count, first.States[1].LoweredReplacements.Count);
        Assert.Equal("geometry-preserving-cubic-subdivision", first.States[1].RadialTargetPreparation!.Law);
        Assert.Equal(Enumerable.Range(0, count), first.States[1].LoweredReplacements.Select(item => item.RepetitionIndex));
        Assert.Equal(count * 3, first.States[1].Segments.Count(segment => segment.ProvenanceFeatureId == "GearTeeth"));
        Assert.All(
            first.States[1].Segments.Where(segment => segment.ProvenanceFeatureId == "GearTeeth"),
            segment => Assert.NotNull(segment.RepetitionIndex));
        Assert.Contains("<svg", first.Svg, StringComparison.Ordinal);
        AssertClosed(first.Shape!.Contours[0]);
    }

    [Fact]
    public void Count_variants_have_distinct_deterministic_contours()
    {
        int[] counts = [8, 12, 16];
        string[] hashes = counts.Select(count => CompileGear(count).CanonicalContourHash!).ToArray();

        Assert.Equal(3, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Pattern_is_ownerless_connected_and_has_normalized_endpoints()
    {
        RepeatRadialPatternProfileOperation operation = Assert.IsType<RepeatRadialPatternProfileOperation>(CompileGear(12).Definition!.Operations[0]);

        Assert.DoesNotContain(operation.Pattern.GetType().GetProperties(), property => property.Name.Contains("Owner", StringComparison.Ordinal));
        Assert.Equal(new VectorPoint(0, 0), operation.Pattern.Segments[0].Start);
        Assert.Equal(new VectorPoint(1, 0), operation.Pattern.Segments[^1].End);
        for (int index = 1; index < operation.Pattern.Segments.Count; index++)
        {
            Assert.Equal(operation.Pattern.Segments[index - 1].End, operation.Pattern.Segments[index].Start);
        }
    }

    [Fact]
    public void Dovetail_and_v_notch_patterns_instantiate_on_current_ssa_targets()
    {
        const string source = """
            import { DovetailTab, Point, Profile, ProfileEdge, Rectangle, ReplaceSpanWithPattern, SelectSegment, SpanOf, VNotch } from "./Profile";

            const Tab = DovetailTab({
                start: Point(0.0, 0.0),
                leftShoulder: Point(0.3, 6.0),
                rightShoulder: Point(0.7, 6.0),
                end: Point(1.0, 0.0)
            });
            const Notch = VNotch({
                start: Point(0.0, 0.0),
                tip: Point(0.5, -5.0),
                end: Point(1.0, 0.0)
            });

            export default (
                <Profile name="PatternEdges" base={Rectangle({ width: 60.0, height: 40.0 })}>
                    {ReplaceSpanWithPattern({
                        id: "Dovetail", as: "Tabbed",
                        target: SpanOf([SelectSegment("Base", 0)]),
                        pattern: Tab
                    })}
                    {ReplaceSpanWithPattern({
                        id: "VNotch", as: "Notched",
                        target: SpanOf([SelectSegment("Tabbed", 4)]),
                        pattern: Notch
                    })}
                    {Yield(Notched)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(["ReplaceSpanPattern", "ReplaceSpanPattern"], result.Definition!.Operations.Select(operation => operation.Kind));
        Assert.Equal(["Base", "Tabbed", "Notched"], result.States.Select(state => state.Name));
        Assert.Equal(26, result.Shape!.Bounds.MaxY);
        Assert.Equal(-20, result.Shape.Bounds.MinY);
        AssertClosed(result.Shape!.Contours[0]);
    }

    [Theory]
    [InlineData(500, "COPE-PROFILE-0051")]
    [InlineData(2, "COPE-PROFILE-0051")]
    public void Excessive_and_too_small_counts_use_profile_limits(int count, string diagnosticId)
    {
        ProfileCompilationResult result = CompileGear(count);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Disconnected_reversed_selected_and_non_finite_patterns_are_rejected()
    {
        ProfileSourceSpan span = ProfileSourceSpan.Generated("InvalidPattern.profile.tsx");
        ProfileSpanPattern disconnected = new([
            Line(new(0, 0), new(0.25, 2)),
            Line(new(0.75, 2), new(1, 0)),
        ]);
        ProfileSpanPattern reversed = new([Line(new(1, 0), new(0, 0))]);
        ProfileSpanPattern nonFinite = new([new ProfileReplacementSegment(
            ProfileCurveKind.Line,
            new(0, 0),
            new(1, 0),
            double.NaN,
            default,
            default)]);

        AssertFailure(disconnected, "COPE-PROFILE-0050");
        AssertFailure(reversed, "COPE-PROFILE-0052");
        AssertFailure(nonFinite, "COPE-PROFILE-0050");

        const string selectedPattern = """
            const Invalid: ProfileSpanPattern = SpanPattern(SpanOf([SelectSegment("Base", 0)]));
            export default (
                <Profile name="SelectedPattern" base={Circle({ radius: 20.0 })}>
                    {RepeatRadialPattern({ id: "Invalid", as: "Changed", count: 8, pattern: Invalid })}
                    {Yield(Changed)}
                </Profile>
            );
            """;
        Assert.Contains(
            ProfileTsxCompiler.Compile(selectedPattern).Diagnostics,
            diagnostic => diagnostic.Id == "COPE-PROFILE-0050");

        void AssertFailure(ProfileSpanPattern pattern, string diagnosticId)
        {
            ProfileCompilationResult result = ProfileCompiler.Compile(new(
                "Invalid",
                "Base",
                new CircleProfileShape(30, 0, 0, span),
                [new RepeatRadialPatternProfileOperation("Pattern", "Base", "Changed", 12, pattern, 0.5, 90, span)],
                "Changed",
                span));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        }
    }

    [Fact]
    public void Stale_pattern_target_and_overlap_are_rejected_by_replace_span_authority()
    {
        const string stale = """
            import { GearTooth, Point, Profile, ProfileEdge, Rectangle, ReplaceSpanWithPattern, SelectSegment, SpanOf } from "./Profile";

            const Tooth = GearTooth({
                rootLeft: Point(0.0, 0.0), tipLeft: Point(0.3, 5.0),
                tipRight: Point(0.7, 5.0), rootRight: Point(1.0, 0.0)
            });
            export default (
                <Profile name="Stale" base={Rectangle({ width: 40.0, height: 30.0 })}>
                    {ReplaceSpanWithPattern({ id: "One", as: "One", target: SpanOf([SelectSegment("Base", 0)]), pattern: Tooth })}
                    {ReplaceSpanWithPattern({ id: "Two", as: "Two", target: SpanOf([SelectSegment("Base", 1)]), pattern: Tooth })}
                    {Yield(Two)}
                </Profile>
            );
            """;
        const string overlap = """
            import { Circle, GearTooth, Point, Profile, ProfileEdge, RepeatRadialPattern } from "./Profile";

            const Tooth = GearTooth({
                rootLeft: Point(0.0, 0.0), tipLeft: Point(0.2, -100.0),
                tipRight: Point(0.8, -100.0), rootRight: Point(1.0, 0.0)
            });
            export default (
                <Profile name="Overlap" base={Circle({ radius: 20.0 })}>
                    {RepeatRadialPattern({ id: "Teeth", as: "Geared", count: 16, pattern: Tooth, targetFraction: 0.8 })}
                    {Yield(Geared)}
                </Profile>
            );
            """;
        const string selfIntersection = """
            const Crossing: ProfileSpanPattern = SpanPattern(SpanOf([
                LineSegment(Point(0.0, 0.0), Point(0.8, 8.0)),
                LineSegment(Point(0.8, 8.0), Point(0.2, -8.0)),
                LineSegment(Point(0.2, -8.0), Point(0.8, -8.0)),
                LineSegment(Point(0.8, -8.0), Point(0.2, 8.0)),
                LineSegment(Point(0.2, 8.0), Point(1.0, 0.0))
            ]));
            export default (
                <Profile name="Crossing" base={Rectangle({ width: 40.0, height: 30.0 })}>
                    {ReplaceSpanWithPattern({
                        id: "Crossing", as: "Changed",
                        target: SpanOf([SelectSegment("Base", 0)]), pattern: Crossing
                    })}
                    {Yield(Changed)}
                </Profile>
            );
            """;

        Assert.Contains(ProfileTsxCompiler.Compile(stale).Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-0047");
        Assert.Contains(ProfileTsxCompiler.Compile(overlap).Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-0043");
        Assert.Contains(ProfileTsxCompiler.Compile(selfIntersection).Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-0043");
    }

    [Fact]
    public void Pattern_curves_reuse_the_canonical_quadratic_and_cubic_segment_path()
    {
        const string source = """
            const Curved: ProfileSpanPattern = SpanPattern(SpanOf([
                CurveSegment(Point(0.0, 0.0), Point(0.5, 4.0), Bulge({ amount: 2.0 })),
                CurveSegment(Point(0.5, 4.0), Point(1.0, 0.0), Spline({
                    control1: Point(0.65, 5.0),
                    control2: Point(0.85, 2.0)
                }))
            ]));
            export default (
                <Profile name="CurvedPattern" base={Rectangle({ width: 40.0, height: 30.0 })}>
                    {ReplaceSpanWithPattern({
                        id: "Curved", as: "Changed",
                        target: SpanOf([SelectSegment("Base", 0)]),
                        pattern: Curved
                    })}
                    {Yield(Changed)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.IsType<VectorQuadratic>(result.Shape!.Contours[0].Segments[0]);
        Assert.IsType<VectorCubic>(result.Shape.Contours[0].Segments[1]);
    }

    [Fact]
    public void Imported_template_with_and_match_patterns_use_ordinary_language_values()
    {
        const string source = """
            import { Circle, Hole, Point, Profile, ProfileEdge, ProfileOperation, ProfileSpanPattern, RepeatRadialPattern } from "./Profile";

            export default (
                <Profile name="ImportedGear" base={Circle({ radius: 32.0 })}>
                    {instantiate GearTeeth<count: 12>}
                    {Hole({ id: "CenterHole", as: "Hollow", radius: 12.0 })}
                    {Yield(Hollow)}
                </Profile>
            );
            """;
        const string tools = """
            import { GearTooth, Point, ProfileOperation, ProfileSpanPattern, RepeatRadialPattern } from "./Profile";

            export enum ToothStyle { Sharp, Soft }

            export function ToothByStyle(style: ToothStyle, depth: number): ProfileSpanPattern {
                return match style {
                    Sharp => GearTooth({
                        rootLeft: Point(0.0, 0.0), tipLeft: Point(0.3, depth),
                        tipRight: Point(0.7, depth), rootRight: Point(1.0, 0.0)
                    }),
                    Soft => GearTooth({
                        rootLeft: Point(0.0, 0.0), tipLeft: Point(0.4, depth),
                        tipRight: Point(0.6, depth), rootRight: Point(1.0, 0.0)
                    })
                };
            }

            template<> AuthoredTooth: ProfileSpanPattern {
                return ToothByStyle(ToothStyle.Sharp, 7.0);
            }

            template<static count: int> GearTeeth: ProfileOperation[] {
                const tooth: ProfileSpanPattern = instantiate AuthoredTooth<>;
                return [RepeatRadialPattern({
                    id: "GearTeeth", as: "WithTeeth", count: count,
                    pattern: tooth with { segments: tooth.segments },
                    targetFraction: 0.52, rotation: 90.0
                })];
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(source, tools, templateSourcePath: "ProfileTools.ts");

        Assert.True(result.Success, Diagnostics(result));
        RepeatRadialPatternProfileOperation operation = Assert.IsType<RepeatRadialPatternProfileOperation>(result.Definition!.Operations[0]);
        Assert.Equal("GearTeeth", operation.TemplateProvenance!.TemplateName);
        Assert.Equal(12, result.States[1].LoweredReplacements.Count);
    }

    private static ProfileCompilationResult CompileGear(int count)
    {
        string source = GearSource.Replace("count: 12", $"count: {count}", StringComparison.Ordinal);
        return ProfileTsxCompiler.Compile(source);
    }

    private static ProfileReplacementSegment Line(VectorPoint start, VectorPoint end)
        => new(ProfileCurveKind.Line, start, end, 0, default, default);

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

    private const string GearSource = """
        import { Circle, GearTooth, Hole, Point, Profile, ProfileEdge, ProfileSpanPattern, RepeatRadialPattern } from "./Profile";

        const Tooth: ProfileSpanPattern = GearTooth({
            rootLeft: Point(0.0, 0.0),
            tipLeft: Point(0.3, 8.0),
            tipRight: Point(0.7, 8.0),
            rootRight: Point(1.0, 0.0)
        });

        export default (
            <Profile name="Gear" base={Circle({ radius: 32.0 })}>
                {RepeatRadialPattern({
                    id: "GearTeeth", as: "WithTeeth", count: 12,
                    pattern: Tooth, targetFraction: 0.52, rotation: 90.0
                })}
                {Hole({ id: "CenterHole", as: "Hollow", radius: 12.0 })}
                {Yield(Hollow)}
            </Profile>
        );
        """;
}
