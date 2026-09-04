using Copeland.Profile;
using Machina.VectorAssets;
using Xunit;

namespace Machina.Fonts.Tests.VectorAssets;

public sealed class ProfileVectorIconCompilerM0Tests
{
    [Theory]
    [InlineData("Gear")]
    [InlineData("TabbedBadge")]
    public void Semantic_profile_compiles_through_the_existing_m5_msdf_path(string fixture)
    {
        ProfileCompilationResult profile = ProfileCompiler.Compile(
            fixture == "Gear" ? ProfileFixtures.Gear() : ProfileFixtures.TabbedBadge());

        VectorIconCompilationResult result = ProfileVectorIconCompiler.Compile(
            profile,
            profile.Svg!,
            fixture + ".profile.tsx");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Reason)));
        Assert.Equal(profile.CanonicalContourHash, result.Artifact!.Shape.NormalizedGeometryHash);
        Assert.StartsWith("vector-icon-sha256-", result.Artifact.Identity.Value, StringComparison.Ordinal);
        Assert.All(result.Artifact.FieldPixels.ToArray(), value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void Canonical_fixture_set_covers_curves_and_multiple_holes()
    {
        ProfileCompilationResult shield = ProfileCompiler.Compile(ProfileFixtures.Shield());
        ProfileCompilationResult multiHole = ProfileCompiler.Compile(ProfileFixtures.MultiHole());

        Assert.True(shield.Success);
        Assert.Contains(shield.Shape!.Contours.SelectMany(contour => contour.Segments), segment => segment is VectorCubic);
        Assert.True(multiHole.Success);
        Assert.Equal(3, multiHole.Shape!.Contours.Count);
    }

    [Fact]
    public void Overlapping_generic_add_fails_closed_instead_of_emitting_invalid_msdf_geometry()
    {
        ProfileSourceSpan span = ProfileSourceSpan.Generated("DPad.profile.tsx");
        ProfileDefinition definition = new(
            "DPad",
            "Vertical",
            new RoundedRectangleProfileShape(18, 58, 4, span),
            [
                new AddProfileOperation("HorizontalArm", "Vertical", "Cross", new RoundedRectangleProfileShape(58, 18, 4, span), span),
                new HoleProfileOperation("CenterDetail", "Cross", "Finished", new CircleProfileShape(4, 0, 0, span), span),
            ],
            "Finished",
            span);

        ProfileCompilationResult profile = ProfileCompiler.Compile(definition);
        ProfileDiagnostic diagnostic = Assert.Single(profile.Diagnostics);
        Assert.Equal("COPE-PROFILE-0035", diagnostic.Id);
    }

    [Fact]
    public void Semantic_tabs_author_a_dpad_without_overlapping_generic_boolean_geometry()
    {
        ProfileSourceSpan span = ProfileSourceSpan.Generated("DPad.profile.tsx");
        ProfileDefinition definition = new(
            "DPad",
            "Vertical",
            new RoundedRectangleProfileShape(18, 58, 4, span),
            [
                new TabProfileOperation("RightArm", "Vertical", "RightArmState", ProfileEdge.Right, 18, 20, 0.5, span),
                new TabProfileOperation("LeftArm", "RightArmState", "Cross", ProfileEdge.Left, 18, 20, 0.5, span),
                new HoleProfileOperation("CenterDetail", "Cross", "Finished", new CircleProfileShape(4, 0, 0, span), span),
            ],
            "Finished",
            span);

        ProfileCompilationResult profile = ProfileCompiler.Compile(definition);
        VectorIconCompilationResult icon = ProfileVectorIconCompiler.Compile(profile, "semantic tab d-pad", "DPad.profile.tsx");

        Assert.True(icon.Success, string.Join(Environment.NewLine, icon.Diagnostics.Select(item => item.Reason)));
        Assert.Equal(["Vertical", "RightArmState", "Cross", "Finished"], profile.States.Select(item => item.Name));
        Assert.True(VectorIconCpuQualification.Compare(icon.Artifact!, 64).IntersectionOverUnion >= 0.98);
    }
}
