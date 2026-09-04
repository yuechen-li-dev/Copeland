using Copeland.Profile;
using Copeland.TS.Profiles;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProfileTemplateFunctionsM1Tests
{
    [Fact]
    public void GearTeeth_template_lowers_to_existing_repeat_radial_operation()
    {
        ProfileCompilationResult result = CompileGear(12);

        Assert.True(result.Success, Diagnostics(result));
        RepeatRadialProfileOperation repeat = Assert.IsType<RepeatRadialProfileOperation>(result.Definition!.Operations[0]);
        Assert.Equal(12, repeat.Count);
        Assert.Equal("GearTeeth", repeat.FeatureId);
        Assert.Equal("GearTeeth", repeat.TemplateProvenance!.TemplateName);
        Assert.Equal(0, repeat.TemplateProvenance.GeneratedOperationIndex);
        Assert.IsType<HoleProfileOperation>(result.Definition.Operations[1]);
    }

    [Fact]
    public void Template_and_manual_operation_sequences_have_exact_semantic_and_contour_parity()
    {
        ProfileCompilationResult template = CompileGear(12);
        ProfileCompilationResult manual = ProfileTsxCompiler.Compile(ManualGearSource);

        Assert.True(template.Success, Diagnostics(template));
        Assert.True(manual.Success, Diagnostics(manual));
        Assert.Equal(manual.ProfileIrHash, template.ProfileIrHash);
        Assert.Equal(manual.CanonicalContourHash, template.CanonicalContourHash);
        Assert.Equal(manual.Svg, template.Svg);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void Tooth_count_is_one_static_value_edit_with_deterministic_output(int count)
    {
        ProfileCompilationResult first = CompileGear(count);
        ProfileCompilationResult second = CompileGear(count);

        Assert.True(first.Success, Diagnostics(first));
        Assert.Equal(first.ProfileIrHash, second.ProfileIrHash);
        Assert.Equal(first.CanonicalContourHash, second.CanonicalContourHash);
        Assert.Equal(first.Svg, second.Svg);
    }

    [Fact]
    public void Second_reusable_template_returns_a_semantic_hole_operation()
    {
        const string source = """
            export default (
                <Profile name="Mount" base={Circle({ radius: 24 })}>
                    {instantiate MountHole<radius: 5.0>}
                    {Yield(Hollow)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(source, TemplateLibrary);

        Assert.True(result.Success, Diagnostics(result));
        HoleProfileOperation hole = Assert.IsType<HoleProfileOperation>(Assert.Single(result.Definition!.Operations));
        Assert.Equal("MountHole", hole.TemplateProvenance!.TemplateName);
    }

    [Theory]
    [InlineData(0, "COPE-PROFILE-0024")]
    [InlineData(-1, "COPE-PROFILE-0024")]
    [InlineData(257, "COPE-PROFILE-0024")]
    public void Invalid_repeat_counts_use_existing_profile_validation(int count, string expectedDiagnostic)
    {
        ProfileCompilationResult result = CompileGear(count);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == expectedDiagnostic);
    }

    [Fact]
    public void Non_constant_specialization_argument_is_rejected_at_the_profile_boundary()
    {
        string source = GearSource.Replace("count: 12", "count: RuntimeCount", StringComparison.Ordinal);

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(source, TemplateLibrary);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-TEMPLATE-0004");
    }

    [Fact]
    public void Wrong_profile_function_argument_bag_uses_normal_record_diagnostics()
    {
        const string invalidLibrary = """
            import { ProfileOperation, RepeatRadial } from "./Profile";

            template<static count: int, static toothFraction: number, static toothDepth: number> GearTeeth: ProfileOperation[] {
                return [RepeatRadial({
                    id: "GearTeeth",
                    as: "WithTeeth",
                    count,
                    toothDepth,
                    missing: 0.52,
                    rotation: 90.0
                })];
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(GearSource, invalidLibrary);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-REC-0007");
    }

    [Theory]
    [InlineData("Hole({ id: \"Bad\", as: \"Bad\", radius: \"wide\", x: 0.0, y: 0.0 })")]
    [InlineData("Tab({ id: \"Bad\", as: \"Bad\", edge: ProfileEdge.Top, width: 2.0, depth: \"deep\", position: 0.5 })")]
    public void Wrong_operation_bags_fail_in_normal_function_binding(string operation)
    {
        string invalidLibrary = $$"""
            import { Hole, ProfileEdge, ProfileOperation, Tab } from "./Profile";

            template<static count: int, static toothFraction: number, static toothDepth: number> GearTeeth: ProfileOperation[] {
                return [{{operation}}];
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(GearSource, invalidLibrary);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-REC-0009");
    }

    [Fact]
    public void Wrong_template_result_domain_is_rejected_before_profile_lowering()
    {
        const string invalidLibrary = """
            import { Circle, ProfileShape } from "./Profile";

            template<static count: int, static toothFraction: number, static toothDepth: number> GearTeeth: ProfileShape[] {
                return [Circle({ radius: 2.0, x: 0.0, y: 0.0 })];
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(GearSource, invalidLibrary);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-TEMPLATE-0007");
    }

    [Fact]
    public void Nested_specialization_type_error_keeps_the_template_source_span()
    {
        const string invalidLibrary = """
            import { ProfileOperation } from "./Profile";

            template<> InvalidInner: ProfileOperation {
                return "wrong";
            }

            template<static count: int, static toothFraction: number, static toothDepth: number> GearTeeth: ProfileOperation[] {
                return [instantiate InvalidInner<>];
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(GearSource, invalidLibrary);

        ProfileDiagnostic diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0005");
        Assert.Equal("ProfileTemplates.ts", diagnostic.Span.Path);
        Assert.True(diagnostic.Span.Length > 0);
    }

    private static ProfileCompilationResult CompileGear(int count)
        => ProfileTsxCompiler.CompileWithTemplates(
            GearSource.Replace("count: 12", $"count: {count}", StringComparison.Ordinal),
            TemplateLibrary);

    private static string Diagnostics(ProfileCompilationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Id}: {item.Message}"));

    private const string GearSource = """
        export default (
            <Profile name="Gear" baseState="Base" base={Circle({ radius: 32 })}>
                {instantiate GearTeeth<count: 12, toothFraction: 0.52, toothDepth: 8.0>}
                {Hole({ as: "Hollow", id: "CenterHole", radius: 12 })}
                {Yield(Hollow)}
            </Profile>
        );
        """;

    private const string ManualGearSource = """
        export default (
            <Profile name="Gear" baseState="Base" base={Circle({ radius: 32 })}>
                {RepeatRadial({ as: "WithTeeth", id: "GearTeeth", count: 12, toothDepth: 8, toothFraction: 0.52, rotation: 90 })}
                {Hole({ as: "Hollow", id: "CenterHole", radius: 12 })}
                {Yield(Hollow)}
            </Profile>
        );
        """;

    private const string TemplateLibrary = """
        import { Hole, ProfileOperation, RepeatRadial } from "./Profile";

        template<static count: int, static toothFraction: number, static toothDepth: number> ToothFeature: ProfileOperation {
            return RepeatRadial({
                id: "GearTeeth",
                as: "WithTeeth",
                count,
                toothDepth,
                toothFraction,
                rotation: 90.0
            });
        }

        template<static count: int, static toothFraction: number, static toothDepth: number> GearTeeth: ProfileOperation[] {
            return [instantiate ToothFeature<count: count, toothFraction: toothFraction, toothDepth: toothDepth>];
        }

        function CenterHole(radius: number): ProfileOperation {
            return Hole({
                id: "MountHole",
                as: "Hollow",
                radius,
                x: 0.0,
                y: 0.0
            });
        }

        template<static radius: number> MountHole: ProfileOperation[] {
            return [CenterHole(radius)];
        }
        """;
}
