using Copeland.TS.Compiler;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LayoutBindingM0Tests
{
    [Fact]
    public void Exact_binding_resolves_authored_slots_to_renderable_component_values()
    {
        CopelandCompilation compilation = Compile("""
            import { createElement } from "react";
            layout type PageShell {
                column root { slot header; slot content; slot footer; }
            }
            layout Page<0px, 0px> satisfies PageShell {
                width: 800px;
                height: 600px;
                column root {
                    slot header { height: 64px; }
                    slot content { height: fill; }
                    slot footer { height: 48px; }
                }
            }
            function Header(): ReactNode { return <header></header>; }
            function Content(): ReactNode { return <main></main>; }
            function Footer(): ReactNode { return <footer></footer>; }
            bind Page { header: Header(); content: Content(); footer: Footer(); }
            """);

        Assert.Empty(compilation.Diagnostics);
        LayoutBindingDeclarationSyntax syntax = Assert.IsType<LayoutBindingDeclarationSyntax>(compilation.SyntaxTree!.Root.Members.Last());
        Assert.Equal(3, syntax.Entries.Count);
        BoundLayoutBinding binding = Assert.Single(compilation.BoundCompilation!.Program.LayoutBindings);
        Assert.Equal("Page", binding.Layout.Name);
        Assert.Equal("PageShell", binding.Contract.Name);
        Assert.Equal(["Page.root.header", "Page.root.content", "Page.root.footer"], binding.Entries.Select(entry => entry.Slot.SemanticPath));
    }

    [Theory]
    [InlineData("header: Header(); content: Content();", "COPE-LAYOUT-BIND-0009")]
    [InlineData("header: Header(); content: Content(); footer: Footer(); ads: Footer();", "COPE-LAYOUT-BIND-0006")]
    [InlineData("header: Header(); header: Header(); content: Content(); footer: Footer();", "COPE-LAYOUT-BIND-0005")]
    [InlineData("root: Header(); header: Header(); content: Content(); footer: Footer();", "COPE-LAYOUT-BIND-0007")]
    public void Binding_enforces_exact_singular_slot_cardinality(string entries, string diagnostic)
    {
        CopelandCompilation compilation = Compile($$"""
            import { createElement } from "react";
            layout type PageShell { column root { slot header; slot content; slot footer; } }
            layout Page<0px, 0px> satisfies PageShell {
                width: 800px;
                height: 600px;
                column root { slot header; slot content; slot footer; }
            }
            function Header(): ReactNode { return <header></header>; }
            function Content(): ReactNode { return <main></main>; }
            function Footer(): ReactNode { return <footer></footer>; }
            bind Page { {{entries}} }
            """);

        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }

    [Fact]
    public void Fixed_grid_children_are_independent_named_slots_not_track_count_bindings()
    {
        CopelandCompilation compilation = Compile("""
            import { createElement } from "react";
            layout type Features { grid features { columns: 4; slot bridge; slot react; slot templates; slot tables; } }
            layout Page<0px, 0px> satisfies Features {
                width: 800px;
                height: 600px;
                grid features {
                    columns: 4;
                    slot bridge { frame: { x: 0px, y: 0px, width: 200px, height: 600px }; }
                    slot react { frame: { x: 200px, y: 0px, width: 200px, height: 600px }; }
                    slot templates { frame: { x: 400px, y: 0px, width: 200px, height: 600px }; }
                    slot tables { frame: { x: 600px, y: 0px, width: 200px, height: 600px }; }
                }
            }
            function Card(): ReactNode { return <article></article>; }
            bind Page { bridge: Card(); react: Card(); templates: Card(); tables: Card(); }
            """);

        Assert.Empty(compilation.Diagnostics);
        Assert.Equal(4, Assert.Single(compilation.BoundCompilation!.Program.LayoutBindings).Entries.Count);
    }

    [Fact]
    public void Binding_requires_a_concrete_satisfied_layout_and_renderable_values()
    {
        CopelandCompilation compilation = Compile("""
            layout type Shell { slot body; }
            layout Unsatisfied<0px, 0px> { width: 100px; height: 100px; slot body; }
            function NumberView(): int { return 1; }
            bind Shell { body: NumberView(); }
            bind Unsatisfied { body: NumberView(); }
            """);

        Assert.Contains(compilation.Diagnostics, item => item.Id == "COPE-LAYOUT-BIND-0003");
        Assert.Contains(compilation.Diagnostics, item => item.Id == "COPE-LAYOUT-BIND-0004");
    }

    [Fact]
    public void Imported_layout_contract_and_component_aliases_bind_through_normal_modules()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Shell.ts", "Shell.ts", "export layout type Shell { column root { slot body; } }"),
            new CopelandProjectSource("Page.ts", "Page.ts", """
                import { Shell } from "./Shell";
                export layout Page<0px, 0px> satisfies Shell {
                    width: 100px;
                    height: 100px;
                    column root { slot body; }
                }
                """),
            new CopelandProjectSource("View.tsx", "View.tsx", """
                import { createElement } from "react";
                export function Body(): ReactNode { return <main></main>; }
                """),
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { createElement } from "react";
                import { Page as Desktop } from "./Page";
                import { Shell } from "./Shell";
                import { Body as Content } from "./View";
                bind Desktop { body: Content(); }
                """),
        ],
        ReactOptions());

        Assert.Empty(project.Diagnostics);
        CopelandProjectModuleCompilation main = project.Modules.Single(module => module.LogicalPath == "Main.ts");
        BoundLayoutBinding binding = Assert.Single(main.BoundCompilation!.Program.LayoutBindings);
        Assert.Equal("Page", binding.Layout.Name);
        Assert.Equal("Shell", binding.Contract.Name);
    }

    [Fact]
    public void Binding_lowers_the_declared_layout_topology_to_react_hosts_with_component_children()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Page.tsx", "Page.tsx", """
                import { createElement } from "react";
                layout type PageShell { column root { slot header; slot content; slot footer; } }
                layout Page<0px, 0px> satisfies PageShell {
                    width: 800px;
                    height: 600px;
                    column root {
                        slot header { height: 64px; }
                        slot content { height: fill; }
                        slot footer { height: 48px; }
                    }
                }
                function Header(): ReactNode { return <span>Header</span>; }
                function Content(): ReactNode { return <span>Content</span>; }
                function Footer(): ReactNode { return <span>Footer</span>; }
                bind Page { header: Header(); content: Content(); footer: Footer(); }
                export function Main(): ReactNode { return PageBinding(); }
                """),
        ], ReactOptions());

        Assert.Empty(project.Diagnostics);
        JavaScriptProjectCompilation baseOutput = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        JavaScriptProjectCompilation output = LayoutJavaScriptProjectEmitter.AddLayouts(baseOutput, project.Modules);
        Assert.True(output.Success, string.Join(Environment.NewLine, output.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string source = output.Files["Page.js"];
        Assert.Contains("function PageBinding()", source, StringComparison.Ordinal);
        Assert.Contains("createElement(\"div\", { className:", source, StringComparison.Ordinal);
        Assert.Contains("Header()", source, StringComparison.Ordinal);
        Assert.Contains("Content()", source, StringComparison.Ordinal);
        Assert.Contains("Footer()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("className: undefined", source, StringComparison.Ordinal);
        Assert.True(output.Files.ContainsKey("generated/layouts.css"));
        Assert.Contains("Page", output.Files["generated/layouts.css"], StringComparison.Ordinal);
    }

    [Fact]
    public void Third_party_react_element_is_a_slot_child_without_a_layout_class_contract()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("App.tsx", "App.tsx", """
                import { createElement } from "react";
                import { Widget } from "@fixture/widget";
                layout type Shell { slot body; }
                layout Page<0px, 0px> satisfies Shell { width: 100px; height: 100px; slot body; }
                bind Page { body: <Widget />; }
                export function Main(): ReactNode { return PageBinding(); }
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
        string source = output.Files["App.js"];
        Assert.Contains("createElement(Widget", source, StringComparison.Ordinal);
        Assert.Contains("function PageBinding()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_react_render_sample_builds_to_a_page_binding_factory()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourcePath = Path.Combine(repositoryRoot, "samples", "copeland-ts", "machina-layout-binding-m0", "09-react-render", "Page.tsx");
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [new CopelandProjectSource("Page.tsx", sourcePath, File.ReadAllText(sourcePath))],
        ReactOptions());

        Assert.Empty(project.Diagnostics);
        JavaScriptProjectCompilation output = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        Assert.True(output.Success, string.Join(Environment.NewLine, output.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("function PageBinding()", output.Files["Page.js"], StringComparison.Ordinal);
    }

    private static CopelandCompilation Compile(string source)
        => CopelandCompiler.Compile(
            source,
            new CopelandCompilationOptions
            {
                SourcePath = "Main.tsx",
                TsXmlProfile = CopelandTsXmlProfile.ReactM0,
                NpmPackages =
                [
                    new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
                ],
            });

    private static CopelandCompilationOptions ReactOptions()
        => new()
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
            ],
        };

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Copeland.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
