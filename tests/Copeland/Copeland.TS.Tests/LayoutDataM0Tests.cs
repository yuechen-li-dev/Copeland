using Copeland.TS.MachinaSource;
using Copeland.TS.Compiler;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Semantics;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LayoutDataM0Tests
{
    [Fact]
    public void Layout_is_a_first_class_declaration_with_direct_dimensions_and_named_slots()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile(DesktopLayout, "Desktop.layout.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
        Assert.IsType<LayoutDeclarationSyntax>(compilation.SyntaxTree.Root.Members.Single());
        BoundLayoutDeclaration layout = compilation.Layouts["DesktopLayout"];
        Assert.Equal(new BoundLayoutCoordinate(0, LayoutCoordinateUnit.Px), layout.Origin.X);
        Assert.Equal(new BoundLayoutCoordinate(0, LayoutCoordinateUnit.Px), layout.Origin.Y);
        Assert.Contains("root", layout.Slots.Keys);
        Assert.Contains("sidebar", layout.Slots.Keys);
        Assert.Contains("hero", layout.Slots.Keys);
        Assert.Equal(LayoutDimensionKind.Fixed, layout.Root.Dimensions["width"].Kind);
        Assert.Equal(LayoutDimensionKind.Fill, layout.Slots["sidebar"].Dimensions["height"].Kind);

        NormalizedLayoutGraph graph = LayoutDataCompiler.Normalize(layout);
        Assert.NotNull(graph.Root.Origin);
        Assert.Equal(layout.Origin, graph.Root.Origin!.Local);
        Assert.All(graph.Root.Children, child => Assert.Equal(NormalizedLayoutOriginRelation.FlowDerived, child.OriginRelation));
        Assert.Equal("DesktopLayout.root.sidebar", graph.SlotIdentities["sidebar"]);
        NormalizedLayoutGraph repeated = LayoutDataCompiler.Normalize(layout);
        Assert.Equal(graph.Root.StableIdentity, repeated.Root.StableIdentity);
        Assert.Equal(graph.SlotIdentities.OrderBy(pair => pair.Key), repeated.SlotIdentities.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void Named_react_projection_exposes_authored_slots_instead_of_coordinate_accessors()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile(DesktopLayout, "Desktop.layout.ts");
        LayoutReactArtifact first = LayoutDataCompiler.LowerForReact(compilation.Layouts["DesktopLayout"]);
        LayoutReactArtifact second = LayoutDataCompiler.LowerForReact(compilation.Layouts["DesktopLayout"]);

        Assert.Equal(first.Css, second.Css);
        Assert.Equal(first.ClassesBySlot["hero"], second.ClassesBySlot["hero"]);
        Assert.Contains("m-frame-DesktopLayout-root-0", first.ClassesBySlot["root"], StringComparison.Ordinal);
        Assert.Contains("m-frame-DesktopLayout-root-0-1-0", first.ClassesBySlot["hero"], StringComparison.Ordinal);
        Assert.DoesNotContain("MachinaDesktopRoot_", first.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void Grid_anchor_and_immutable_composition_bind_without_executing_layout_functions()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            layout Base<0px, 0px> {
                width: 200px;
                height: 100px;
                overlay root {
                    anchor inset { left: 10px; right: 10px; top: 10px; bottom: 10px; }
                    grid cards { columns: 2; frame: { x: 0px, y: 0px, width: 200px, height: 100px }; }
                }
            }
            layout Derived<20px, 10px> = Base with { width: 400px; };
            """, "Composition.layout.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundLayoutDeclaration derived = compilation.Layouts["Derived"];
        Assert.Equal(400, derived.Root.Dimensions["width"].Length!.Value.Px);
        Assert.Equal(new BoundLayoutCoordinate(20, LayoutCoordinateUnit.Px), derived.Origin.X);
        Assert.Contains("cards", derived.Slots.Keys);
        Assert.Equal(LayoutNodeKind.Grid, derived.Slots["cards"].Kind);
        Assert.Contains("cards", LayoutDataCompiler.LowerForReact(derived).ClassesBySlot.Keys);
        Assert.Equal(
            NormalizedLayoutOriginRelation.AnchorDerived,
            LayoutDataCompiler.Normalize(compilation.Layouts["Base"]).Root.Children.Single(node => node.Name == "inset").OriginRelation);
    }

    [Fact]
    public void Duplicate_slots_and_recursive_composition_have_stable_diagnostics()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            layout First<0px, 0px> = Second;
            layout Second<0px, 0px> = First;
            layout Duplicate<0px, 0px> {
                slot header;
                column header;
            }
            """, "Invalid.layout.ts");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-COMPOSE-0001");
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-SLOT-0001");
    }

    [Fact]
    public void Canonical_layout_data_fixtures_compile()
    {
        string root = FindRepositoryRoot();
        string fixtureRoot = Path.Combine(root, "samples", "copeland-ts", "machina-layout-data-m0");
        foreach (string fixture in Directory.GetFiles(fixtureRoot, "*.layout.ts", SearchOption.AllDirectories))
        {
            LayoutDataCompilation compilation = LayoutDataCompiler.Compile(File.ReadAllText(fixture), fixture);
            Assert.True(compilation.Success, fixture + Environment.NewLine + string.Join(Environment.NewLine, compilation.Diagnostics));
        }

        string crossModuleRoot = Path.Combine(fixtureRoot, "08-cross-module");
        CopelandProjectCompilation crossModule = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Shared.ts", Path.Combine(crossModuleRoot, "Shared.ts"), File.ReadAllText(Path.Combine(crossModuleRoot, "Shared.ts"))),
            new CopelandProjectSource("Desktop.ts", Path.Combine(crossModuleRoot, "Desktop.ts"), File.ReadAllText(Path.Combine(crossModuleRoot, "Desktop.ts"))),
        ]);
        Assert.Empty(crossModule.Diagnostics);
    }

    [Fact]
    public void Exported_layouts_flow_through_normal_project_import_binding_with_aliases()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("ui/Shared.ts", "Shared.ts", """
                export layout SharedShell<0ui, 0ui> {
                    slot header;
                    slot content;
                }
                """),
            new CopelandProjectSource("ui/Desktop.ts", "Desktop.ts", """
                import { SharedShell as Shell } from "./Shared";
                export layout page DesktopLayout<0px, 0px> = Shell with { width: 1440px; height: 900px; };
                """),
        ]);

        Assert.Empty(project.Diagnostics);
        CopelandProjectModuleCompilation desktop = project.Modules.Single(module => module.LogicalPath == "ui/Desktop.ts");
        LayoutSymbol symbol = Assert.IsType<LayoutSymbol>(desktop.BoundCompilation!.ModuleScope!.Declarations["DesktopLayout"]);
        Assert.Equal("ui/Desktop.ts::layout::DesktopLayout", symbol.StableIdentity);
        Assert.Equal("page", symbol.Profile);
        Assert.Contains("header", symbol.Slots.Keys);
        Assert.Contains("content", symbol.Slots.Keys);
    }

    [Fact]
    public void React_projection_generates_typed_semantic_accessors_deterministically()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile(DesktopLayout, "Desktop.layout.ts");
        LayoutReactProjection first = LayoutDataCompiler.ProjectReact(compilation.Layouts["DesktopLayout"]);
        LayoutReactProjection second = LayoutDataCompiler.ProjectReact(compilation.Layouts["DesktopLayout"]);

        Assert.Equal(first.TypeScript, second.TypeScript);
        Assert.Contains("export const DesktopLayout", first.TypeScript, StringComparison.Ordinal);
        Assert.Contains("\"hero\": Object.freeze({ className:", first.TypeScript, StringComparison.Ordinal);
        Assert.DoesNotContain("MachinaDesktopRoot_", first.TypeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Normal_javascript_project_emission_includes_named_layout_modules_and_one_stylesheet()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("App.ts", "App.ts", """
                export layout DesktopLayout<12px, 8px> {
                    width: 320px;
                    height: 160px;
                    overlay root {
                        slot hero { frame: { x: 10px, y: 10px, width: 300px, height: 140px }; }
                    }
                }
                export function Main(): number { return 1; }
                """),
        ]);

        Assert.Empty(project.Diagnostics);
        JavaScriptProjectCompilation baseEmission = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        JavaScriptProjectCompilation emitted = LayoutJavaScriptProjectEmitter.AddLayouts(baseEmission, project.Modules);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        KeyValuePair<string, string> module = emitted.Files.Single(file => file.Key.EndsWith("DesktopLayout.js", StringComparison.Ordinal));
        Assert.Contains("export const DesktopLayout", module.Value, StringComparison.Ordinal);
        Assert.Contains("\"hero\": Object.freeze({ className:", module.Value, StringComparison.Ordinal);
        Assert.Contains("generated/layouts.css", emitted.Files.Keys);
        Assert.Equal(1, emitted.Files.Keys.Count(path => path == "generated/layouts.css"));
    }

    [Fact]
    public void Layout_origins_are_typed_mandatory_and_projected_without_a_hidden_default()
    {
        LayoutDataCompilation px = LayoutDataCompiler.Compile("""
            layout Pixel<12px, 8px> {
                width: 320px;
                height: 160px;
                row root {
                    slot first { width: 100px; height: fill; }
                    slot second { width: fill; height: fill; }
                }
            }
            """, "Pixel.layout.ts");

        Assert.True(px.Success, string.Join(Environment.NewLine, px.Diagnostics));
        Assert.Equal(new BoundLayoutCoordinate(12, LayoutCoordinateUnit.Px), px.Layouts["Pixel"].Origin.X);
        Assert.Contains("position: absolute;\n  left: 12px;\n  top: 8px;", LayoutDataCompiler.LowerForReact(px.Layouts["Pixel"]).Css, StringComparison.Ordinal);

        LayoutDataCompilation ui = LayoutDataCompiler.Compile("""
            layout Logical<10ui, 4ui> {
                width: 320px;
                height: 160px;
            }
            """, "Logical.layout.ts");
        Assert.True(ui.Success, string.Join(Environment.NewLine, ui.Diagnostics));
        Assert.Equal(LayoutCoordinateUnit.Ui, ui.Layouts["Logical"].Origin.X.Unit);
        Assert.Contains("left: calc(var(--machina-ui, 1px) * 10);", LayoutDataCompiler.LowerForReact(ui.Layouts["Logical"]).Css, StringComparison.Ordinal);

        LayoutDataCompilation independentAxes = LayoutDataCompiler.Compile("layout Independent<2px, 3ui> { width: 1px; height: 1px; }", "Independent.layout.ts");
        Assert.True(independentAxes.Success, string.Join(Environment.NewLine, independentAxes.Diagnostics));
        Assert.Equal(LayoutCoordinateUnit.Px, independentAxes.Layouts["Independent"].Origin.X.Unit);
        Assert.Equal(LayoutCoordinateUnit.Ui, independentAxes.Layouts["Independent"].Origin.Y.Unit);

        LayoutDataCompilation negative = LayoutDataCompiler.Compile("layout Negative<-2px, -3ui> { width: 1px; height: 1px; }", "Negative.layout.ts");
        Assert.True(negative.Success, string.Join(Environment.NewLine, negative.Diagnostics));
        Assert.Equal(-2, negative.Layouts["Negative"].Origin.X.Value);
        Assert.Equal(-3, negative.Layouts["Negative"].Origin.Y.Value);

        LayoutDataCompilation missing = LayoutDataCompiler.Compile("layout Missing { width: 1px; height: 1px; }", "Missing.layout.ts");
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-ORIGIN-0001");
        Assert.Empty(missing.Layouts);

        LayoutDataCompilation malformed = LayoutDataCompiler.Compile("layout Bad<0px 0px> { width: 1px; height: 1px; }", "Bad.layout.ts");
        Assert.Contains(malformed.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-ORIGIN-0002");

        LayoutDataCompilation runtime = LayoutDataCompiler.Compile("layout Runtime<offset, 0px> { width: 1px; height: 1px; }", "Runtime.layout.ts");
        Assert.Contains(runtime.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-ORIGIN-0003");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the Copeland repository root.");
    }

    private const string DesktopLayout = """
        layout DesktopLayout<0px, 0px> {
            width: 1440px;
            height: 900px;

            row root {
                gap: 18px;

                column sidebar {
                    width: 256px;
                    height: fill;
                }

                column main {
                    width: fill;
                    height: fill;
                    gap: 18px;

                    slot hero { height: 520px; }
                    grid features { columns: 4; gap: 16px; height: fill; }
                    slot footer { height: 44px; }
                }
            }
        }
        """;
}
