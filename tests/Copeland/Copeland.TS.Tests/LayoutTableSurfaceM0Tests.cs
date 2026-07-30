using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LayoutTableSurfaceM0Tests
{
    [Fact]
    public void Csv_overlay_binds_to_the_existing_stream_layout_graph_and_react_hosts()
    {
        CopelandProjectCompilation project = CompileProject("""
            import { createElement } from "react";
            export layers AppLayers { content; modal; }
            function Page(): ReactNode { return <span>Page</span>; }
            function Dialog(title: string, description: string): ReactNode { return <span>{title}{description}</span>; }
            stream DialogScene<0px, 0px> {
                layers: AppLayers;
                width: 320px;
                height: 180px;
                csv overlay root {
                    name, content, x, y, width, height, layer, z;
                    page, Page(), 0px, 0px, 320px, 180px, content, 0;
                    dialog, Dialog("Title", "Description"), 20px, 20px, 260px, 120px, modal, -1;
                }
            }
            export function Main(): ReactNode { return DialogSceneStream(); }
            """);

        Assert.Empty(project.Diagnostics);
        CopelandProjectModuleCompilation module = Assert.Single(project.Modules);
        StreamDeclarationSyntax stream = Assert.IsType<StreamDeclarationSyntax>(module.BoundCompilation!.SyntaxTree.Root.Members.Single(member => member is StreamDeclarationSyntax));
        StreamTableSyntax table = Assert.Single(stream.Tables);
        Assert.Equal("csv", table.CsvKeyword.Text);
        Assert.Equal(["name", "content", "x", "y", "width", "height", "layer", "z"], table.Headers.Select(header => header.Text));

        BoundLayoutBinding binding = Assert.Single(module.BoundCompilation.Program.LayoutBindings);
        Assert.Equal(["DialogScene.root.page", "DialogScene.root.dialog"], binding.Entries.Select(entry => entry.Slot.SemanticPath));
        NormalizedLayoutGraph graph = LayoutDataCompiler.Normalize(binding.Layout.BoundLayout!);
        Assert.Equal("root", graph.Root.Name);
        Assert.Equal(["page", "dialog"], graph.Root.Children.Select(child => child.Name));
        Assert.Equal(["page", "dialog"], graph.Root.Children.OrderBy(child => child.PaintOrder).Select(child => child.Name));

        JavaScriptProjectCompilation output = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        Assert.True(output.Success, string.Join(Environment.NewLine, output.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("Dialog(\"Title\", \"Description\")", output.Files["Page.js"], StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_overlay_defaults_layer_and_z_and_resolves_columns_by_header_name()
    {
        CopelandCompilation compilation = Compile("""
            import { createElement } from "react";
            function Page(): ReactNode { return <span>Page</span>; }
            stream PageScene<0px, 0px> {
                width: 100px;
                height: 80px;
                csv overlay root {
                    content, height, name, width, y, x;
                    Page(), 80px, page, 100px, 0px, 0px;
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        BoundLayoutBinding binding = Assert.Single(compilation.BoundCompilation!.Program.LayoutBindings);
        NormalizedLayoutNode page = Assert.Single(LayoutDataCompiler.Normalize(binding.Layout.BoundLayout!).Root.Children);
        Assert.Equal("default", page.LayerIdentity);
        Assert.Equal(0, page.LocalZ);
        Assert.Equal("page", page.Name);
    }

    [Fact]
    public void Csv_overlay_nested_in_a_column_satisfies_the_same_exact_topology_as_nested_authoring()
    {
        CopelandCompilation table = Compile("""
            import { createElement } from "react";
            layout type Shell { row root { column main { overlay scene { slot page; slot dialog; } } } }
            function Page(): ReactNode { return <span>Page</span>; }
            function Dialog(): ReactNode { return <span>Dialog</span>; }
            stream Scene<0px, 0px> satisfies Shell {
                width: 320px;
                height: 180px;
                row root {
                    column main {
                        width: fill;
                        height: fill;
                        csv overlay scene {
                            name, content, x, y, width, height;
                            page, Page(), 0px, 0px, 320px, 180px;
                            dialog, Dialog(), 20px, 20px, 260px, 120px;
                        }
                    }
                }
            }
            """);

        Assert.Empty(table.Diagnostics);
        BoundLayoutBinding binding = Assert.Single(table.BoundCompilation!.Program.LayoutBindings);
        Assert.Equal("Shell", binding.Contract.Name);
        Assert.Equal(["Scene.root.main.scene.page", "Scene.root.main.scene.dialog"], binding.Entries.Select(entry => entry.Slot.SemanticPath));
    }

    [Fact]
    public void Csv_overlay_normalizes_equivalently_to_the_nested_overlay_form()
    {
        CopelandCompilation nested = Compile("""
            import { createElement } from "react";
            function Page(): ReactNode { return <span>Page</span>; }
            function Dialog(): ReactNode { return <span>Dialog</span>; }
            stream Nested<0px, 0px> {
                width: 320px;
                height: 180px;
                overlay root {
                    page: Page() { frame: { x: 0px, y: 0px, width: 320px, height: 180px }; }
                    dialog: Dialog() { frame: { x: 20px, y: 20px, width: 260px, height: 120px }; z: 1; }
                }
            }
            """);
        CopelandCompilation table = Compile("""
            import { createElement } from "react";
            function Page(): ReactNode { return <span>Page</span>; }
            function Dialog(): ReactNode { return <span>Dialog</span>; }
            stream Table<0px, 0px> {
                width: 320px;
                height: 180px;
                csv overlay root {
                    name, content, x, y, width, height, z;
                    page, Page(), 0px, 0px, 320px, 180px, 0;
                    dialog, Dialog(), 20px, 20px, 260px, 120px, 1;
                }
            }
            """);

        Assert.Empty(nested.Diagnostics);
        Assert.Empty(table.Diagnostics);
        NormalizedLayoutNode nestedRoot = LayoutDataCompiler.Normalize(nested.BoundCompilation!.Program.LayoutBindings.Single().Layout.BoundLayout!).Root;
        NormalizedLayoutNode tableRoot = LayoutDataCompiler.Normalize(table.BoundCompilation!.Program.LayoutBindings.Single().Layout.BoundLayout!).Root;
        Assert.Equal(nestedRoot.Children.Select(child => (child.Name, child.LayerIdentity, child.LocalZ, child.AuthoredNodeOrder)), tableRoot.Children.Select(child => (child.Name, child.LayerIdentity, child.LocalZ, child.AuthoredNodeOrder)));
    }

    [Fact]
    public void Csv_content_cell_accepts_a_third_party_react_element()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Page.tsx", "Page.tsx", """
                import { createElement } from "react";
                import { Widget } from "@fixture/widget";
                stream WidgetScene<0px, 0px> {
                    width: 100px;
                    height: 80px;
                    csv overlay root {
                        name, content, x, y, width, height;
                        widget, <Widget />, 0px, 0px, 100px, 80px;
                    }
                }
                export function Main(): ReactNode { return WidgetSceneStream(); }
                """),
        ], new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
                new CopelandNpmPackageContract("@fixture/widget", "1.0.0", [], Components: [new CopelandNpmComponentContract("Widget")]),
            ],
        });

        Assert.Empty(project.Diagnostics);
        JavaScriptProjectCompilation output = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        Assert.True(output.Success, string.Join(Environment.NewLine, output.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("createElement(Widget", output.Files["Page.js"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name, name, content, x, y, width, height; page, page, Page(), 0px, 0px, 1px, 1px;", "COPE-LAYOUT-TABLE-0002")]
    [InlineData("name, content, x, y, width; page, Page(), 0px, 0px, 1px;", "COPE-LAYOUT-TABLE-0004")]
    [InlineData("name, content, x, y, width, height; page, Page(), 0px, 0px, 1px;", "COPE-LAYOUT-TABLE-0005")]
    [InlineData("name, content, x, y, width, height; page, Page(), 0px, 0px, 1px, 1px, 7;", "COPE-LAYOUT-TABLE-0006")]
    [InlineData("name, content, x, y, width, height, z; page, Page(), 0px, 0px, 1px, 1px, 12;", "COPE-LAYOUT-TABLE-0012")]
    public void Csv_overlay_reports_schema_and_cell_diagnostics(string table, string diagnostic)
    {
        CopelandCompilation compilation = Compile($$"""
            import { createElement } from "react";
            function Page(): ReactNode { return <span>Page</span>; }
            stream PageScene<0px, 0px> {
                width: 100px;
                height: 80px;
                csv overlay root { {{table}} }
            }
            """);

        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }

    private static CopelandCompilation Compile(string source)
        => CopelandCompiler.Compile(source, Options());

    private static CopelandProjectCompilation CompileProject(string source)
        => CopelandProjectCompiler.CompileToMir([new CopelandProjectSource("Page.tsx", "Page.tsx", source)], Options());

    private static CopelandCompilationOptions Options()
        => new()
        {
            SourcePath = "Main.tsx",
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
            ],
        };
}
