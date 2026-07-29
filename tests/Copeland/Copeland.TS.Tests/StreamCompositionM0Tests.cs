using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class StreamCompositionM0Tests
{
    [Fact]
    public void Flat_stream_infers_a_column_contract_singular_slots_and_a_react_render_value()
    {
        CopelandProjectCompilation project = CompileProject("""
            import { createElement } from "react";
            function Header(): ReactNode { return <span>Header</span>; }
            function Content(): ReactNode { return <span>Content</span>; }
            function Footer(): ReactNode { return <span>Footer</span>; }
            stream Page<0px, 0px> {
                width: 800px;
                height: 600px;
                header: Header() { height: 64px; }
                content: Content() { height: fill; }
                footer: Footer() { height: 48px; }
            }
            export function Main(): ReactNode { return PageStream(); }
            """);

        Assert.Empty(project.Diagnostics);
        CopelandProjectModuleCompilation module = Assert.Single(project.Modules);
        StreamDeclarationSyntax stream = Assert.IsType<StreamDeclarationSyntax>(module.BoundCompilation!.SyntaxTree.Root.Members.First(member => member is StreamDeclarationSyntax));
        Assert.Equal(3, stream.Nodes.Count);
        BoundLayoutBinding binding = Assert.Single(module.BoundCompilation!.Program.LayoutBindings);
        Assert.Equal("PageStream", binding.RuntimeFunction.Name);
        Assert.Equal("PageShape", binding.Contract.Name);
        Assert.Equal(["Page.root.header", "Page.root.content", "Page.root.footer"], binding.Entries.Select(entry => entry.Slot.SemanticPath));
        NormalizedLayoutGraph graph = LayoutDataCompiler.Normalize(binding.Layout.BoundLayout!);
        Assert.Equal("root", graph.Root.Name);
        Assert.Equal(["header", "content", "footer"], graph.Root.Children.Select(child => child.Name));

        JavaScriptProjectCompilation output = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        Assert.True(output.Success, string.Join(Environment.NewLine, output.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string source = output.Files["Page.js"];
        Assert.Contains("function PageStream()", source, StringComparison.Ordinal);
        Assert.Contains("createElement(\"div\", { className:", source, StringComparison.Ordinal);
        Assert.Contains("Header()", source, StringComparison.Ordinal);
        Assert.Contains("Content()", source, StringComparison.Ordinal);
        Assert.Contains("Footer()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Stream_can_satisfy_an_explicit_contract_and_nested_containers_remain_structural()
    {
        CopelandCompilation compilation = Compile("""
            import { createElement } from "react";
            layout type Shell { row root { column sidebar { slot navigation; } column main { slot hero; slot content; } } }
            function Nav(): ReactNode { return <span>Nav</span>; }
            function Hero(): ReactNode { return <span>Hero</span>; }
            function Content(): ReactNode { return <span>Content</span>; }
            stream Desktop<0px, 0px> satisfies Shell {
                width: 1200px;
                height: 800px;
                row root {
                    column sidebar { width: 256px; height: fill; navigation: Nav() { height: fill; } }
                    column main { width: fill; height: fill; hero: Hero() { height: 320px; } content: Content() { height: fill; } }
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        BoundLayoutBinding binding = Assert.Single(compilation.BoundCompilation!.Program.LayoutBindings);
        Assert.Equal("Shell", binding.Contract.Name);
        Assert.Equal(["Desktop.root.sidebar.navigation", "Desktop.root.main.hero", "Desktop.root.main.content"], binding.Entries.Select(entry => entry.Slot.SemanticPath));
    }

    [Theory]
    [InlineData("header: Header() { height: 32px; } header: Header() { height: 32px; }", "COPE-STREAM-0006")]
    [InlineData("header: 1 { height: 32px; }", "COPE-STREAM-0012")]
    [InlineData("column shell: Header() { height: fill; }", "COPE-STREAM-0008")]
    public void Stream_reports_duplicate_non_renderable_and_ambiguous_container_content(string nodes, string diagnostic)
    {
        CopelandCompilation compilation = Compile($$"""
            import { createElement } from "react";
            function Header(): ReactNode { return <span>Header</span>; }
            stream Page<0px, 0px> {
                width: 100px;
                height: 100px;
                {{nodes}}
            }
            """);

        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }

    [Fact]
    public void Fixed_grid_collection_is_one_named_region_with_ordered_non_slot_content()
    {
        CopelandProjectCompilation project = CompileProject("""
            import { createElement } from "react";
            function A(): ReactNode { return <span>A</span>; }
            function B(): ReactNode { return <span>B</span>; }
            function C(): ReactNode { return <span>C</span>; }
            stream Features<0px, 0px> {
                width: 600px;
                height: 300px;
                grid features: [A(), B(), C()] { columns: 4; gap: 16px; height: fill; }
            }
            """);

        Assert.Empty(project.Diagnostics);
        BoundLayoutBinding binding = Assert.Single(Assert.Single(project.Modules).BoundCompilation!.Program.LayoutBindings);
        BoundStreamCollection collection = Assert.Single(binding.Collections);
        Assert.Equal("features", collection.Region.Name);
        Assert.Equal(3, collection.Items.Count);
        Assert.DoesNotContain(binding.Layout.Slots.Values, slot => slot.Name.StartsWith("item", StringComparison.Ordinal));

        JavaScriptProjectCompilation output = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        Assert.True(output.Success, string.Join(Environment.NewLine, output.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string source = output.Files["Page.js"];
        Assert.True(source.IndexOf("A()", StringComparison.Ordinal) < source.IndexOf("B()", StringComparison.Ordinal));
        Assert.True(source.IndexOf("B()", StringComparison.Ordinal) < source.IndexOf("C()", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixed_grid_collection_reports_the_non_renderable_item_index()
    {
        CopelandCompilation compilation = Compile("""
            import { createElement } from "react";
            function A(): ReactNode { return <span>A</span>; }
            stream Features<0px, 0px> {
                width: 100px;
                height: 100px;
                grid features: [A(), 2] { columns: 4; }
            }
            """);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-STREAM-COLLECTION-0003" && diagnostic.Message.Contains("Item 2", StringComparison.Ordinal));
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
