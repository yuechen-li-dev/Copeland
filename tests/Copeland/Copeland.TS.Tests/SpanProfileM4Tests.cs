using Copeland.Profile;
using Copeland.TS.Compiler;
using Copeland.TS.Profiles;
using Copeland.TS.Semantics;
using Copeland.TS.Templates;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class SpanProfileM4Tests
{
    [Fact]
    public void Span_is_a_generic_language_type_with_non_profile_static_use()
    {
        const string source = """
            function SpanOf<T>(values: T[]): Span<T> {
                return values;
            }
            const Values: Span<int> = SpanOf([1, 2, 3]);
            const Empty: Span<int> = SpanOf<int>([]);
            const Length: int = Values.length;
            const First: int = Values[0];
            """;

        var result = CopelandCompiler.CompileToMir(source);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("SpanOf__primitive_int", result.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Span_element_types_are_invariant()
    {
        const string source = """
            function SpanOf<T>(values: T[]): Span<T> {
                return values;
            }
            const Wrong: Span<int> = SpanOf(["not an int"]);
            """;

        var result = CopelandCompiler.CompileToMir(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-TYPE-0001");
    }

    [Fact]
    public void Ordinary_helper_returns_connected_dovetail_span_and_preserves_unchanged_ids()
    {
        const string source = """
            function DovetailTab(start: ConceptPoint, end: ConceptPoint, shoulder: number, height: number): Span<ProfileSegment> {
                const left: ConceptPoint = Point(start.x + shoulder, start.y + height);
                const right: ConceptPoint = Point(end.x - shoulder, end.y + height);
                return SpanOf([
                    LineSegment(start, left),
                    LineSegment(left, right),
                    LineSegment(right, end)
                ]);
            }

            const TopStart: ConceptPoint = Point(-30.0, 20.0);
            const TopEnd: ConceptPoint = Point(30.0, 20.0);

            export default (
                <Profile name="TabbedBadge" base={Rectangle({ width: 60.0, height: 40.0 })}>
                    {ReplaceSpan({
                        id: "DovetailTab",
                        as: "Tabbed",
                        target: SpanOf([SelectSegment("Base", 0)]),
                        replacement: DovetailTab(TopStart, TopEnd, 18.0, 8.0)
                    })}
                    {Yield(Tabbed)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(6, result.Shape!.Contours[0].Segments.Count);
        Assert.Equal(
            ["feature:DovetailTab/segment:0", "feature:DovetailTab/segment:1", "feature:DovetailTab/segment:2"],
            result.States[1].Segments.Take(3).Select(segment => segment.Id));
        Assert.Equal(result.States[0].Segments[1], result.States[1].Segments[3]);
        Assert.All(result.States[1].Segments.Take(3), segment => Assert.Equal("DovetailTab", segment.ProvenanceFeatureId));
        AssertClosed(result.Shape.Contours[0]);
    }

    [Fact]
    public void Sequential_dovetail_and_v_notch_are_two_ssa_deltas()
    {
        const string source = """
            function VNotch(start: ConceptPoint, tip: ConceptPoint, end: ConceptPoint): Span<ProfileSegment> {
                return SpanOf([LineSegment(start, tip), LineSegment(tip, end)]);
            }

            export default (
                <Profile name="TabbedBadge" base={Rectangle({ width: 60.0, height: 40.0 })}>
                    {ReplaceSpan({
                        id: "DovetailTab", as: "Tabbed",
                        target: SpanOf([SelectSegment("Base", 0)]),
                        replacement: SpanOf([
                            LineSegment(Point(-30.0, 20.0), Point(-12.0, 28.0)),
                            LineSegment(Point(-12.0, 28.0), Point(12.0, 28.0)),
                            LineSegment(Point(12.0, 28.0), Point(30.0, 20.0))
                        ])
                    })}
                    {ReplaceSpan({
                        id: "VNotch", as: "Notched",
                        target: SpanOf([SelectSegment("Tabbed", 4)]),
                        replacement: VNotch(Point(30.0, -20.0), Point(0.0, -28.0), Point(-30.0, -20.0))
                    })}
                    {Yield(Notched)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(["ReplaceSpan", "ReplaceSpan"], result.Definition!.Operations.Select(operation => operation.Kind));
        Assert.Equal(["Base", "Tabbed", "Notched"], result.States.Select(state => state.Name));
        AssertClosed(result.Shape!.Contours[0]);
    }

    [Fact]
    public void Stale_disconnected_reversed_and_crossing_spans_fail_deterministically()
    {
        ProfileSourceSpan span = ProfileSourceSpan.Generated("InvalidSpan.profile.tsx");
        ProfileReplacementSegment[] disconnected =
        [
            Line(new(-20, 15), new(-5, 20)),
            Line(new(5, 20), new(20, 15)),
        ];

        AssertFailure(Operation("Old", disconnected), "COPE-PROFILE-0047");
        AssertFailure(Operation("Base", disconnected), "COPE-PROFILE-0046");
        AssertFailure(Operation("Base", [Line(new(20, 15), new(-20, 15))]), "COPE-PROFILE-0045");
        AssertFailure(Operation("Base", [
            Line(new(-20, 15), new(20, -20)),
            Line(new(20, -20), new(-20, -20)),
            Line(new(-20, -20), new(20, 15)),
        ]), "COPE-PROFILE-0043");
        AssertFailure(Operation("Base", [new(
            ProfileCurveKind.Line,
            new(-20, 15),
            new(20, 15),
            double.NaN,
            default,
            default)]), "COPE-PROFILE-0044");
        AssertFailure(new ReplaceSpanProfileOperation(
            "Empty",
            "Base",
            "Changed",
            new("Base", 0, 0),
            [],
            span), "COPE-PROFILE-0044");

        ReplaceSpanProfileOperation Operation(string owner, IReadOnlyList<ProfileReplacementSegment> replacement)
            => new("Invalid", "Base", "Changed", new(owner, 0, 1), replacement, span);

        void AssertFailure(ReplaceSpanProfileOperation operation, string diagnosticId)
        {
            ProfileCompilationResult result = ProfileCompiler.Compile(new(
                "Invalid",
                "Base",
                new RectangleProfileShape(40, 30, span),
                [operation],
                "Changed",
                span));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
            Assert.Null(result.Shape);
        }
    }

    [Fact]
    public void Multi_segment_target_and_semantic_curve_replacement_are_supported()
    {
        const string source = """
            export default (
                <Profile name="CurvedCorner" base={Rectangle({ width: 40.0, height: 30.0 })}>
                    {ReplaceSpan({
                        id: "BeakCurve", as: "Curved",
                        target: SpanOf([SelectSegment("Base", 0), SelectSegment("Base", 1)]),
                        replacement: SpanOf([
                            CurveSegment(Point(-20.0, 15.0), Point(20.0, -15.0), Bulge({ amount: 5.0 }))
                        ])
                    })}
                    {Yield(Curved)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.IsType<VectorQuadratic>(result.Shape!.Contours[0].Segments[0]);
        AssertClosed(result.Shape.Contours[0]);
    }

    [Fact]
    public void Imported_ordinary_helper_returns_a_profile_span()
    {
        const string source = """
            import { LineSegment, Point, Profile, ProfileEdge, ProfileOperation, ProfileSegment, Rectangle, ReplaceSpan, SelectSegment, SpanOf } from "./Profile";
            import { BeakCurve } from "./ProfileTools";

            export default (
                <Profile name="Imported" base={Rectangle({ width: 40.0, height: 30.0 })}>
                    {ReplaceSpan({
                        id: "Beak", as: "Changed",
                        target: SpanOf([SelectSegment("Base", 0)]),
                        replacement: BeakCurve(Point(-20.0, 15.0), Point(20.0, 15.0))
                    })}
                    {Yield(Changed)}
                </Profile>
            );
            """;
        const string profileTools = """
            import { ConceptPoint, LineSegment, OffsetPoint, ProfileSegment, SpanOf } from "./Profile";

            export function BeakCurve(start: ConceptPoint, end: ConceptPoint): Span<ProfileSegment> {
                const tip: ConceptPoint = OffsetPoint(start, 20.0, 8.0);
                return SpanOf([LineSegment(start, tip), LineSegment(tip, end)]);
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(source, profileTools, templateSourcePath: "ProfileTools.ts");

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(2, result.Shape!.Contours[0].Segments.Take(2).Count());
        Assert.Equal("Beak", result.States[1].Segments[0].ProvenanceFeatureId);
    }

    [Fact]
    public void Template_typed_value_can_be_a_profile_segment_span()
    {
        const string source = """
            record PointValue {
                x: number;
                y: number;
            }
            record LineValue {
                start: PointValue;
                end: PointValue;
            }
            enum ProfileSegment {
                Line(value: LineValue),
            }
            function Line(start: PointValue, end: PointValue): ProfileSegment {
                return ProfileSegment.Line({ start, end });
            }

            template<> GearTooth: Span<ProfileSegment> {
                return [
                    Line({ x: 0.0, y: 0.0 }, { x: 1.0, y: 1.0 }),
                    Line({ x: 1.0, y: 1.0 }, { x: 2.0, y: 0.0 })
                ];
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "GearTooth");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        TemplateTypedValue value = Assert.IsType<TemplateTypedValue>(result.Value);
        SpanTypeSymbol span = Assert.IsType<SpanTypeSymbol>(value.Type);
        Assert.Equal("ProfileSegment", span.ElementType.Name);
        Assert.Equal(2, Assert.IsType<object?[]>(value.Value).Length);
    }

    [Fact]
    public void Standard_gear_tooth_is_a_real_three_segment_pattern_instantiated_through_replace_span()
    {
        const string source = """
            import { GearTooth, Point, Profile, ProfileEdge, Rectangle, ReplaceSpanWithPattern, SelectSegment, SpanOf } from "./Profile";

            export default (
                <Profile name="Tooth" base={Rectangle({ width: 40.0, height: 30.0 })}>
                    {ReplaceSpanWithPattern({
                        id: "GearTooth", as: "Toothed",
                        target: SpanOf([SelectSegment("Base", 0)]),
                        pattern: GearTooth({
                            rootLeft: Point(0.0, 0.0),
                            tipLeft: Point(0.3, 7.0),
                            tipRight: Point(0.7, 7.0),
                            rootRight: Point(1.0, 0.0)
                        })
                    })}
                    {Yield(Toothed)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(3, result.States[1].Segments.Count(segment => segment.ProvenanceFeatureId == "GearTooth"));
        AssertClosed(result.Shape!.Contours[0]);
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
}
