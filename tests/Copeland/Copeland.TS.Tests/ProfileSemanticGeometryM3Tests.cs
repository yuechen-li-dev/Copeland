using Copeland.Profile;
using Copeland.TS.Profiles;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProfileSemanticGeometryM3Tests
{
    [Fact]
    public void Concept_geometry_materializes_capsule_and_is_erased_from_svg()
    {
        const string source = """
            record BicycleConcept {
                rear: ConceptPoint;
                front: ConceptPoint;
                topTube: ConceptPath;
            }

            const Rear: ConceptPoint = Point(10.0, 20.0);
            const Front: ConceptPoint = Point(90.0, 20.0);
            const TopTube: ConceptPath = PathBetween(Rear, Front);
            const Center: ConceptPoint = Midpoint(TopTube);
            const Quarter: ConceptPoint = Along(TopTube, 0.25);
            const Guides: BicycleConcept = { rear: Rear, front: Front, topTube: TopTube };

            export default (
                <Profile name="Tube" base={Tube({ from: Guides.topTube.start, to: Guides.topTube.end, width: 8.0 })}>
                    {Yield(Base)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.IsType<CapsuleProfileShape>(result.Definition!.Base);
        Assert.DoesNotContain("Concept", result.Svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Guide", result.Svg, StringComparison.Ordinal);
        Assert.Equal(4, result.Shape!.Contours[0].Segments.Count);
    }

    [Theory]
    [InlineData("Slot({ length: 40.0, width: 12.0, angle: 15.0 })", 4)]
    [InlineData("Capsule({ from: Point(0.0, 0.0), to: Point(30.0, 20.0), width: 6.0 })", 4)]
    [InlineData("RegularPolygon({ sides: 3, radius: 20.0 })", 3)]
    [InlineData("RegularPolygon({ sides: 5, radius: 20.0 })", 5)]
    [InlineData("RegularPolygon({ sides: 6, radius: 20.0 })", 6)]
    [InlineData("Polygon({ points: [[0.0, 20.0], [20.0, -20.0], [-20.0, -20.0]] })", 3)]
    public void Closed_base_primitives_lower_deterministically(string expression, int segments)
    {
        string source = $$"""
            export default (
                <Profile name="Primitive" base={{{expression}}}>
                    {Yield(Base)}
                </Profile>
            );
            """;

        ProfileCompilationResult first = ProfileTsxCompiler.Compile(source);
        ProfileCompilationResult second = ProfileTsxCompiler.Compile(source);

        Assert.True(first.Success, Diagnostics(first));
        Assert.Equal(segments, first.Shape!.Contours[0].Segments.Count);
        Assert.Equal(first.CanonicalContourHash, second.CanonicalContourHash);
        Assert.All(first.Shape.Contours, AssertClosed);
    }

    [Theory]
    [InlineData("Arc({ bulge: 7.0 })", typeof(VectorQuadratic), "Q ")]
    [InlineData("Bulge({ amount: -5.0 })", typeof(VectorQuadratic), "Q ")]
    [InlineData("Spline({ control1: Point(15.0, 35.0), control2: Point(25.0, 35.0) })", typeof(VectorCubic), "C ")]
    public void Replace_segment_preserves_endpoints_identity_and_svg_curves(
        string curve,
        Type expectedType,
        string svgCommand)
    {
        string source = $$"""
            export default (
                <Profile name="Curved" base={Rectangle({ width: 40.0, height: 30.0 })}>
                    {ReplaceSegment({ id: "CurvedTop", as: "Curved", segment: 0, replacement: {{curve}} })}
                    {Yield(Curved)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        VectorSegment before = ProfileGeometryForTest.CreateRectangle().Contours[0].Segments[0];
        VectorSegment after = result.Shape!.Contours[0].Segments[0];
        Assert.IsType(expectedType, after);
        Assert.Equal(Start(before), Start(after));
        Assert.Equal(End(before), End(after));
        Assert.Contains(svgCommand, result.Svg, StringComparison.Ordinal);
        Assert.Equal(result.States[0].Segments[1], result.States[1].Segments[1]);
        Assert.Equal("CurvedTop", result.States[1].Segments[0].ProvenanceFeatureId);
        AssertClosed(result.Shape.Contours[0]);
    }

    [Fact]
    public void Self_intersecting_replacement_fails_closed_with_feature_and_source_span()
    {
        const string source = """
            export default (
                <Profile name="Invalid" base={Rectangle({ width: 40.0, height: 30.0 })}>
                    {ReplaceSegment({ id: "Loop", as: "Broken", segment: 0,
                        replacement: Spline({ control1: Point(80.0, -50.0), control2: Point(-80.0, -50.0) }) })}
                    {Yield(Broken)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source, "InvalidReplacement.profile.tsx");

        ProfileDiagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "COPE-PROFILE-0043");
        Assert.Contains("Loop", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(diagnostic.Span.Length > 0);
        Assert.Null(result.Shape);
    }

    [Fact]
    public void Non_finite_replacement_is_rejected_before_geometry_changes()
    {
        ProfileSourceSpan span = ProfileSourceSpan.Generated("NonFinite.profile.tsx");
        ProfileDefinition definition = new(
            "NonFinite",
            "Base",
            new RectangleProfileShape(40, 30, span),
            [new ReplaceSegmentProfileOperation(
                "BadCurve",
                "Base",
                "Broken",
                0,
                new SegmentReplacement(ProfileCurveKind.Bulge, double.NaN, default, default),
                span)],
            "Broken",
            span);

        ProfileCompilationResult result = ProfileCompiler.Compile(definition);

        ProfileDiagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "COPE-PROFILE-0041");
        Assert.Equal("NonFinite.profile.tsx", diagnostic.Span.Path);
        Assert.Null(result.Shape);
    }

    [Fact]
    public void Ordinary_helpers_and_template_produce_custom_profile_operations()
    {
        const string source = """
            function GearTooth(count: int): ProfileOperation {
                return RepeatRadial({ id: "GearTooth", as: "Geared", count: count, toothDepth: 4.0 });
            }
            function DovetailTab(): ProfileOperation {
                return Tab({ id: "DovetailTab", as: "Tabbed", edge: ProfileEdge.Top, width: 12.0, depth: 5.0 });
            }
            function VNotch(): ProfileOperation {
                return Notch({ id: "VNotch", as: "Notched", edge: ProfileEdge.Bottom, width: 10.0, depth: 4.0 });
            }

            export default (
                <Profile name="Custom" base={Rectangle({ width: 60.0, height: 40.0 })}>
                    {DovetailTab()}
                    {VNotch()}
                    {Yield(Notched)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(["DovetailTab", "VNotch"], result.Definition!.Operations.Select(item => item.FeatureId));
        Assert.All(result.Shape!.Contours, AssertClosed);

        const string gearSource = """
            function GearTooth(count: int): ProfileOperation {
                return RepeatRadial({ id: "GearTooth", as: "Geared", count: count, toothDepth: 4.0 });
            }
            export default (
                <Profile name="Gear" base={Circle({ radius: 20.0 })}>
                    {GearTooth(8)}
                    {Yield(Geared)}
                </Profile>
            );
            """;
        ProfileCompilationResult gear = ProfileTsxCompiler.Compile(gearSource);
        Assert.True(gear.Success, Diagnostics(gear));
        Assert.Equal("GearTooth", Assert.Single(gear.Definition!.Operations).FeatureId);
    }

    [Fact]
    public void Template_can_produce_segment_replacement_without_parser_changes()
    {
        const string source = """
            export default (
                <Profile name="TemplateCurve" base={Rectangle({ width: 50.0, height: 30.0 })}>
                    {instantiate CurvedEdge<amount: 6.0>}
                    {Yield(Curved)}
                </Profile>
            );
            """;
        const string library = """
            import { Bulge, ProfileOperation, ReplaceSegment } from "./Profile";

            template<static amount: number> CurvedEdge: ProfileOperation[] {
                return [ReplaceSegment({
                    id: "TemplateBulge",
                    as: "Curved",
                    segment: 0,
                    replacement: Bulge({ amount: amount })
                })];
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(source, library);

        Assert.True(result.Success, Diagnostics(result));
        ReplaceSegmentProfileOperation operation = Assert.IsType<ReplaceSegmentProfileOperation>(Assert.Single(result.Definition!.Operations));
        Assert.Equal("CurvedEdge", operation.TemplateProvenance!.TemplateName);
        AssertClosed(result.Shape!.Contours[0]);
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

    private static class ProfileGeometryForTest
    {
        public static VectorShape CreateRectangle()
        {
            ProfileSourceSpan span = ProfileSourceSpan.Generated();
            ProfileCompilationResult result = ProfileCompiler.Compile(new ProfileDefinition(
                "Rectangle",
                "Base",
                new RectangleProfileShape(40, 30, span),
                [],
                "Base",
                span));
            return result.Shape!;
        }
    }
}
