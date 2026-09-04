using Copeland.Profile;
using Copeland.TS.Profiles;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProfileTsxM0Tests
{
    [Fact]
    public void Gear_tsx_lowers_to_named_geometric_ssa_and_canonical_contours()
    {
        ProfileCompilationResult result = ProfileTsxCompiler.Compile(GearSource);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(["Base", "WithTeeth", "Hollow"], result.States.Select(state => state.Name));
        Assert.Equal(["Circle", "RepeatRadial", "Hole"], result.States.Select(state => state.OperationKind));
        Assert.Equal(2, result.Shape!.Contours.Count);
        Assert.NotNull(result.ProfileIrHash);
        Assert.Equal(result.Shape.NormalizedGeometryHash, result.CanonicalContourHash);
        Assert.Contains("<svg", result.Svg, StringComparison.Ordinal);
        Assert.DoesNotContain("React", GearSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Tabbed_badge_preserves_semantic_edge_features_and_hole()
    {
        ProfileCompilationResult result = ProfileTsxCompiler.Compile(BadgeSource, "TabbedBadge.profile.tsx");

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(["Base", "WithTab", "Notched", "Hollow"], result.States.Select(state => state.Name));
        Assert.Equal(2, result.Shape!.Contours.Count);
        Assert.Equal(100, result.Shape.Bounds.Width, 8);
        Assert.Equal(64, result.Shape.Bounds.Height, 8);
    }

    [Fact]
    public void Equivalent_source_is_deterministic_and_semantic_edit_is_local()
    {
        ProfileCompilationResult first = ProfileTsxCompiler.Compile(GearSource);
        ProfileCompilationResult second = ProfileTsxCompiler.Compile(GearSource);
        string edited = GearSource.Replace("count: 12", "count: 8", StringComparison.Ordinal)
            .Replace("radius: 12", "radius: 8", StringComparison.Ordinal);
        ProfileCompilationResult changed = ProfileTsxCompiler.Compile(edited);

        Assert.Equal(first.ProfileIrHash, second.ProfileIrHash);
        Assert.Equal(first.CanonicalContourHash, second.CanonicalContourHash);
        Assert.NotEqual(first.ProfileIrHash, changed.ProfileIrHash);
        Assert.NotEqual(first.CanonicalContourHash, changed.CanonicalContourHash);
        Assert.Equal(2, LineChanges(GearSource, edited));
    }

    [Theory]
    [InlineData("0", "COPE-PROFILE-0024")]
    [InlineData("-2", "COPE-PROFILE-0024")]
    [InlineData("radius: 12", "COPE-PROFILE-TSX-0005", true)]
    public void Invalid_profiles_report_spanned_diagnostics(string replacement, string id, bool removeYield = false)
    {
        string source;
        if (removeYield)
        {
            source = GearSource.Replace("{Yield(Hollow)}", string.Empty, StringComparison.Ordinal);
        }
        else if (replacement == "-2")
        {
            source = GearSource.Replace("toothDepth: 8", "toothDepth: -2", StringComparison.Ordinal);
        }
        else
        {
            source = GearSource.Replace("count: 12", $"count: {replacement}", StringComparison.Ordinal);
        }

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        ProfileDiagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Id == id);
        Assert.True(diagnostic.Span.Length > 0);
    }

    [Fact]
    public void Generic_shapes_transforms_and_contained_subtraction_are_available_without_runtime_jsx()
    {
        ProfileSourceSpan span = ProfileSourceSpan.Generated();
        ProfileDefinition definition = new(
            "Transforms",
            "Base",
            new RegularPolygonProfileShape(6, 20, 90, span),
            [
                new TransformProfileOperation("Move", "Base", "Moved", "Translate", 2, 3, span),
                new TransformProfileOperation("Turn", "Moved", "Turned", "Rotate", 30, 0, span),
                new TransformProfileOperation("Grow", "Turned", "Grown", "Scale", 2, 1.5, span),
                new TransformProfileOperation("Flip", "Grown", "Flipped", "Mirror", 1, 0, span),
                new SubtractProfileOperation("Cut", "Flipped", "Final", new CircleProfileShape(3, -2, 3, span), span),
            ],
            "Final",
            span);

        ProfileCompilationResult result = ProfileCompiler.Compile(definition);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(2, result.Shape!.Contours.Count);
    }

    [Fact]
    public void Generic_add_supports_disjoint_closed_regions()
    {
        ProfileSourceSpan span = ProfileSourceSpan.Generated();
        ProfileDefinition definition = new(
            "TwoIslands",
            "Left",
            new CircleProfileShape(5, -12, 0, span),
            [new AddProfileOperation("RightIsland", "Left", "Both", new CircleProfileShape(5, 12, 0, span), span)],
            "Both",
            span);

        ProfileCompilationResult result = ProfileCompiler.Compile(definition);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(2, result.Shape!.Contours.Count);
    }

    private static int LineChanges(string left, string right)
    {
        string[] a = left.Split('\n');
        string[] b = right.Split('\n');
        return a.Zip(b).Count(pair => pair.First != pair.Second);
    }

    private static string Diagnostics(ProfileCompilationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Id}: {item.Message}"));

    private const string GearSource = """
        export default (
            <Profile name="Gear" baseState="Base" base={Circle({ radius: 32 })}>
                {RepeatRadial({ as: "WithTeeth", id: "GearTeeth", count: 12, toothDepth: 8, toothFraction: 0.52, rotation: 90 })}
                {Hole({ as: "Hollow", id: "CenterHole", radius: 12 })}
                {Yield(Hollow)}
            </Profile>
        );
        """;

    private const string BadgeSource = """
        export default (
            <Profile name="TabbedBadge" base={RoundedRectangle({ width: 100, height: 56, radius: 8 })}>
                {Tab({ as: "WithTab", id: "MountTab", edge: Top, width: 22, depth: 8 })}
                {Notch({ as: "Notched", id: "CableNotch", edge: Right, width: 12, depth: 7 })}
                {Hole({ as: "Hollow", id: "MountHole", radius: 5, x: -30 })}
                {Yield(Hollow)}
            </Profile>
        );
        """;
}
