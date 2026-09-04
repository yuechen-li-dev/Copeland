using Copeland.Profile;
using Copeland.TS.Profiles;
using System.Reflection;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProfileFunctionAuthoringM2Tests
{
    [Fact]
    public void Brand_new_user_helper_uses_ordinary_binding_without_tsx_registration()
    {
        const string source = """
            function CompletelyNewOperationHelper(radius: number): ProfileOperation {
                return Hole({ id: "Fresh", as: "Cut", radius });
            }

            const FreshOperation: ProfileOperation = CompletelyNewOperationHelper(6.0);

            export default (
                <Profile name="Fresh" base={Circle({ radius: 24.0 })}>
                    {FreshOperation}
                    {Yield(Cut)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        HoleProfileOperation hole = Assert.IsType<HoleProfileOperation>(Assert.Single(result.Definition!.Operations));
        Assert.Equal(6, hole.Hole.Radius);
    }

    [Fact]
    public void Operation_arrays_and_local_values_are_consumed_by_return_type()
    {
        const string source = """
            function MountPattern(): ProfileOperation[] {
                return [
                    Hole({ id: "Left", as: "LeftCut", radius: 4.0, x: -10.0 }),
                    Hole({ id: "Right", as: "BothCut", radius: 4.0, x: 10.0 }),
                ];
            }

            const Operations: ProfileOperation[] = MountPattern();

            export default (
                <Profile name="Mount" base={Rectangle({ width: 48.0, height: 24.0 })}>
                    {Operations}
                    {Yield(BothCut)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(2, result.Definition!.Operations.Count);
    }

    [Fact]
    public void Geometry_looking_name_has_no_privilege_when_return_type_is_wrong()
    {
        const string source = """
            function HoleLikeThing(): int {
                return 42;
            }

            export default (
                <Profile name="Wrong" base={Circle({ radius: 24.0 })}>
                    {HoleLikeThing()}
                    {Yield(Base)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        ProfileDiagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "COPE-PROFILE-TSX-0004");
        Assert.Contains("int", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ImportedA")]
    [InlineData("ImportedB")]
    public void Imported_ordinary_helper_uses_the_same_static_function_contract(string profileName)
    {
        string source = """
            import { CenterHole } from "./ProfileTemplates";

            export default (
                <Profile name="PROFILE_NAME" base={Circle({ radius: 24.0 })}>
                    {CenterHole(7.0)}
                    {Yield(Cut)}
                </Profile>
            );
            """.Replace("PROFILE_NAME", profileName, StringComparison.Ordinal);
        const string library = """
            import { Hole, ProfileOperation } from "./Profile";

            export function CenterHole(radius: number): ProfileOperation {
                return Hole({ id: "Center", as: "Cut", radius });
            }
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.CompileWithTemplates(source, library);

        Assert.True(result.Success, Diagnostics(result));
        Assert.IsType<HoleProfileOperation>(Assert.Single(result.Definition!.Operations));
    }

    [Fact]
    public void Direct_and_user_wrapper_have_exact_semantic_geometry_parity()
    {
        const string direct = """
            export default (
                <Profile name="Parity" base={Circle({ radius: 24.0 })}>
                    {Hole({ id: "Center", as: "Cut", radius: 8.0 })}
                    {Yield(Cut)}
                </Profile>
            );
            """;
        const string wrapped = """
            function CenterHole(radius: number): ProfileOperation {
                return Hole({ id: "Center", as: "Cut", radius });
            }

            export default (
                <Profile name="Parity" base={Circle({ radius: 24.0 })}>
                    {CenterHole(8.0)}
                    {Yield(Cut)}
                </Profile>
            );
            """;

        ProfileCompilationResult directResult = ProfileTsxCompiler.Compile(direct);
        ProfileCompilationResult wrappedResult = ProfileTsxCompiler.Compile(wrapped);

        Assert.True(directResult.Success, Diagnostics(directResult));
        Assert.True(wrappedResult.Success, Diagnostics(wrappedResult));
        Assert.Equal(directResult.ProfileIrHash, wrappedResult.ProfileIrHash);
        Assert.Equal(directResult.CanonicalContourHash, wrappedResult.CanonicalContourHash);
        Assert.Equal(directResult.Svg, wrappedResult.Svg);
    }

    [Theory]
    [InlineData("Hole({ id: \"Bad\", as: \"Cut\", radius: 4.0, mystery: 1.0 })", "COPE-REC-0007")]
    [InlineData("[Hole({ id: \"Good\", as: \"Cut\", radius: 4.0 }), 42]", "COPE-TYPE-0009")]
    public void Direct_profile_expressions_use_ordinary_type_diagnostics(string expression, string expectedId)
    {
        string source = $$"""
            export default (
                <Profile name="Invalid" base={Circle({ radius: 24.0 })}>
                    {{{expression}}}
                    {Yield(Cut)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == expectedId);
    }

    [Fact]
    public void With_match_and_static_conditional_remain_ordinary_composition()
    {
        const string source = """
            enum Variant {
                Small,
                Large,
            }

            const BaseHole: HoleArgs = { id: "Center", as: "Cut", radius: 4.0 };
            const LargerHole: HoleArgs = BaseHole with { radius: 8.0 };
            const SelectedVariant: Variant = Variant.Large;
            const Matched: ProfileOperation = match SelectedVariant {
                Small => Hole(BaseHole),
                Large => Hole(LargerHole),
            };
            const Enabled: boolean = true;
            const Selected: ProfileOperation = if Enabled { Matched } else { Hole(BaseHole) };

            export default (
                <Profile name="Composed" base={Circle({ radius: 24.0 })}>
                    {Selected}
                    {Yield(Cut)}
                </Profile>
            );
            """;

        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        HoleProfileOperation hole = Assert.IsType<HoleProfileOperation>(Assert.Single(result.Definition!.Operations));
        Assert.Equal(8, hole.Hole.Radius);
    }

    [Fact]
    public void Tsx_lowerer_has_no_direct_geometry_parser_or_option_reader()
    {
        MethodInfo? parser = typeof(ProfileTsxCompiler).GetMethod("ParseOperation", BindingFlags.NonPublic | BindingFlags.Static);
        Type? optionReader = typeof(ProfileTsxCompiler).GetNestedType("OptionReader", BindingFlags.NonPublic);

        Assert.Null(parser);
        Assert.Null(optionReader);
    }

    private static string Diagnostics(ProfileCompilationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Id}: {item.Message}"));
}
