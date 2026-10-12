using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics;
using Copeland.TS.Templates;
using Copeland.TS.Backend.JavaScript;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LayoutTemplatesTests
{
    [Fact]
    public void Layout_template_hash_includes_nested_geometry_and_repeats_deterministically()
    {
        const string source = """
            template<static gap: Length = 12px> layout Reading<0px, 0px> {
                width: fill;
                height: fit;
                column root { gap: gap; slot body { height: fit; } }
            }
            """;
        TemplateEvaluationResult first = TemplateCompiler.Evaluate(source, "Reading");
        TemplateEvaluationResult repeated = TemplateCompiler.Evaluate(source, "Reading");
        TemplateEvaluationResult changed = TemplateCompiler.Evaluate(source.Replace("12px", "8px"), "Reading");
        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.True(repeated.Success, string.Join(Environment.NewLine, repeated.Diagnostics));
        Assert.True(changed.Success, string.Join(Environment.NewLine, changed.Diagnostics));
        Assert.Equal(first.Value!.DeterministicHash, repeated.Value!.DeterministicHash);
        Assert.NotEqual(first.Value.DeterministicHash, changed.Value!.DeterministicHash);
    }

    [Fact]
    public void Forwarded_imported_templates_retain_transitive_private_static_helpers()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Base.ts", "Base.ts", """
                function Gap(): Length { return 14px; }
                export template<static gap: Length = Gap()> layout Reading<0px, 0px> {
                    width: fill;
                    height: fit;
                    column root { gap: gap; slot body { height: fit; } }
                }
                """),
            new CopelandProjectSource("Library.ts", "Library.ts", """
                import { Reading } from "./Base";
                export template<> DefaultReading: Layout { return instantiate Reading<>; }
                """),
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { DefaultReading } from "./Library";
                layout Article = instantiate DefaultReading<>;
                """)
        ]);
        Assert.Empty(compilation.Diagnostics);
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!);
        Assert.Empty(emitted.Diagnostics);
        BoundLayoutDeclaration layout = compilation.Modules.SelectMany(module => module.BoundCompilation!.Program.Layouts)
            .Single(item => item.Name == "Article");
        Assert.Equal(14, layout.Slots["root"].Gap.Px);
    }
    [Fact]
    public void Imported_layout_variants_retain_private_contract_authority()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Library.ts", "Library.ts", """
                layout type PrivateShape { column root { slot body; } }
                export template<> layout Reading<0px, 0px> satisfies PrivateShape {
                    width: fill;
                    height: fit;
                    column root { slot body { height: fit; } }
                }
                """),
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { Reading } from "./Library";
                layout Article = instantiate Reading<>;
                layout Dense = Article with { root.gap: 8px; };
                """)
        ]);
        Assert.Empty(compilation.Diagnostics);
        Assert.All(compilation.Modules.SelectMany(module => module.BoundCompilation!.Program.Layouts),
            layout => Assert.True(layout.Satisfaction!.IsSatisfied));
    }
    [Fact]
    public void Imported_template_aliases_keep_their_defaults_and_private_helpers()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Library.ts", "Library.ts", """
                function DefaultGap(): Length { return 14px; }
                export template<static gap: Length = DefaultGap()>
                layout Reading<0px, 0px> {
                    width: fill;
                    height: fit;
                    column root { gap: gap; slot body { height: fit; } }
                }
                """),
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { Reading as ImportedReading } from "./Library";
                layout Article = instantiate ImportedReading<>;
                layout Dense = Article with { root.gap: 8px; };
                """)
        ]);
        Assert.Empty(compilation.Diagnostics);
        BoundLayoutDeclaration layout = compilation.Modules.SelectMany(module => module.BoundCompilation!.Program.Layouts)
            .Single(item => item.Name == "Article");
        Assert.Equal(14, layout.Slots["root"].Gap.Px);
    }

    [Fact]
    public void Defaults_can_reference_earlier_parameters_and_origin_units_are_retained()
    {
        CopelandCompilation compilation = Compile("""
            template<static gap: Length = 12px, static padding: Length = gap, static x: Length = -20px>
            layout Reading<x, 0ui> {
                width: fill;
                height: fit;
                column root { gap: gap; padding: padding; slot body { height: fit; } }
            }
            layout Page = instantiate Reading<gap: 18px>;
            """);
        Assert.Empty(compilation.Diagnostics);
        BoundLayoutDeclaration layout = Assert.Single(compilation.BoundCompilation!.Program.Layouts);
        Assert.Equal(18, layout.Slots["root"].Padding.Left);
        Assert.Equal(new BoundLayoutCoordinate(-20, LayoutCoordinateUnit.Px), layout.Origin.X);
        Assert.Equal(new BoundLayoutCoordinate(0, LayoutCoordinateUnit.Ui), layout.Origin.Y);
    }

    [Theory]
    [InlineData("gap: 12", "COPE-STATIC-0007")]
    [InlineData("typo: 12px", "COPE-TEMPLATE-0002")]
    [InlineData("gap: 12px, gap: 8px", "COPE-TEMPLATE-0002")]
    [InlineData("gap: -1px", "COPE-LAYOUT-DIMENSION-0001")]
    [InlineData("gap: 4ui", "COPE-MACHINA-UI-0001")]
    public void Invalid_specializations_report_without_crashing(string arguments, string diagnostic)
    {
        CopelandCompilation compilation = Compile($$"""
            template<static gap: Length = 12px>
            layout Reading<0px, 0px> {
                width: fill;
                height: fit;
                column root { gap: gap; slot body { height: fit; } }
            }
            layout Page = instantiate Reading<{{arguments}}>;
            """);
        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }

    [Fact]
    public void Runtime_values_are_rejected_as_static_configuration()
    {
        CopelandCompilation compilation = Compile("""
            let spacing: Length = 12px;
            template<static gap: Length>
            layout Reading<0px, 0px> {
                width: fill;
                height: fit;
                column root { gap: gap; slot body { height: fit; } }
            }
            layout Page = instantiate Reading<gap: spacing>;
            """);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-STATIC-0001");
    }

    [Fact]
    public void Exact_contract_is_retained_and_rechecked_after_geometry_updates()
    {
        CopelandCompilation compilation = Compile("""
            layout type Shape { row root { slot body; } }
            template<static compact: boolean = false>
            layout Reading<0px, 0px> satisfies Shape {
                width: fill;
                height: fit;
                static if (compact) {
                    column root { slot body { height: fit; } }
                } else {
                    row root { slot body { height: fit; } }
                }
            }
            layout Wide = instantiate Reading<>;
            layout Variant = Wide with { root.gap: 8px; };
            """);
        Assert.Empty(compilation.Diagnostics);
        Assert.All(compilation.BoundCompilation!.Program.Layouts, layout => Assert.True(layout.Satisfaction!.IsSatisfied));
        CopelandCompilation invalid = Compile("""
            layout type Shape { row root { slot body; } }
            template<> layout Reading<0px, 0px> satisfies Shape {
                width: fill;
                height: fit;
                column root { slot body { height: fit; } }
            }
            layout Invalid = instantiate Reading<>;
            """);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-TYPE-0010");
    }
    [Fact]
    public void Template_specialization_and_path_variants_use_the_spatial_compiler()
    {
        CopelandCompilation compilation = Compile("""
            template<static gap: Length = 12px, static padding: Length = 16px>
            layout Reading<4ui, 2ui> {
                width: fill;
                height: fit;
                column root {
                    gap: gap;
                    padding: padding;
                    slot body { height: fit; }
                }
            }
            layout Article = instantiate Reading<>;
            layout Spacious = instantiate Reading<gap: 20px>;
            layout Dense = Article with { root.gap: 8px; root.padding: 12px; };
            layout Floating<20px, 10px> = Dense;
            """);
        Assert.Empty(compilation.Diagnostics);
        var layouts = compilation.BoundCompilation!.Program.Layouts.ToDictionary(layout => layout.Name);
        Assert.Equal(12, layouts["Article"].Slots["root"].Gap.Px);
        Assert.Equal(20, layouts["Spacious"].Slots["root"].Gap.Px);
        Assert.Equal(8, layouts["Dense"].Slots["root"].Gap.Px);
        Assert.Equal(layouts["Article"].Origin, layouts["Dense"].Origin);
        Assert.Equal(new BoundLayoutCoordinate(20, LayoutCoordinateUnit.Px), layouts["Floating"].Origin.X);
        Assert.NotNull(compilation.MirCompilation?.Program);
    }

    [Fact]
    public void Immutable_record_configuration_and_static_topology_selection_work()
    {
        CopelandCompilation compilation = Compile("""
            record Config { gap: Length; compact: boolean; }
            const Comfortable: Config = { gap: 12px, compact: false };
            const Compact = Comfortable with { gap: 8px, compact: true };
            template<static config: Config = Comfortable>
            layout Panels<0px, 0px> {
                width: fill;
                height: fit;
                static if (config.compact) {
                    column root {
                        gap: config.gap;
                        slot list { height: fit; }
                        slot detail { height: fit; }
                    }
                } else {
                    row root {
                        gap: config.gap;
                        slot list { width: 300px; height: fit; }
                        slot detail { width: fill; height: fit; }
                    }
                }
            }
            layout Wide = instantiate Panels<>;
            layout Narrow = instantiate Panels<config: Compact>;
            """);
        Assert.Empty(compilation.Diagnostics);
        var layouts = compilation.BoundCompilation!.Program.Layouts.ToDictionary(layout => layout.Name);
        Assert.Equal(LayoutNodeKind.Row, layouts["Wide"].Slots["root"].Kind);
        Assert.Equal(LayoutNodeKind.Column, layouts["Narrow"].Slots["root"].Kind);
        Assert.Equal(8, layouts["Narrow"].Slots["root"].Gap.Px);
        Assert.Equal(12, layouts["Wide"].Slots["root"].Gap.Px);
    }

    [Theory]
    [InlineData("root.typo: 8px;", "COPE-LAYOUT-PROPERTY-0002")]
    [InlineData("missing.gap: 8px;", "COPE-LAYOUT-WITH-0002")]
    [InlineData("root.gap: 8px; root.gap: 9px;", "COPE-LAYOUT-WITH-0001")]
    [InlineData("root.gap: 8;", "COPE-LAYOUT-DIMENSION-0001")]
    [InlineData("root.style: { fill: 123 };", "COPE-LAYOUT-STYLE-0001")]
    [InlineData("root.style: { typo: \"red\" };", "COPE-LAYOUT-STYLE-0001")]
    public void Invalid_variants_have_authored_diagnostics(string replacements, string diagnostic)
    {
        CopelandCompilation compilation = Compile($$"""
            layout Base<0px, 0px> {
                width: fill;
                height: fit;
                column root { slot body { height: fit; } }
            }
            layout Variant = Base with { {{replacements}} };
            """);
        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }

    private static CopelandCompilation Compile(string source)
        => CopelandCompiler.Compile(source, new CopelandCompilationOptions { SourcePath = "Layouts.ts" });
}
