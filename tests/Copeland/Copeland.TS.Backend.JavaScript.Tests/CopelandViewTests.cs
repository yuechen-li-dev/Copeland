using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class CopelandViewTests
{
    private const string PageSource = """
        import { Toggle } from "controls";
        template<static padding: Length = 20px>
        layout Card<0px, 0px> {
            width: fill;
            height: fit;
            column root { padding: padding; slot body { height: fit; } }
        }
        layout Styled = instantiate Card<> with {
            root.style: { fill: "#fffefb", radius: 8px,
                text: { size: 18px, color: "#22313c", weight: 400, lineHeight: 1.6, wrap: "anywhere" } };
        };
        export function Page(): View {
            state: boolean = false;
            on Set(value: boolean) => value;
            stream Content { body: <section class="content"><label for="toggle">Arrangement</label>
                <Toggle id="toggle" value={state} onValueChange={on.Set}>Change arrangement</Toggle>
            </section>; }
            return bind Styled using Content;
        }
        """;

    [Theory]
    [InlineData(false, JavaScriptEmissionProfile.Diagnostic)]
    [InlineData(true, JavaScriptEmissionProfile.Diagnostic)]
    [InlineData(false, JavaScriptEmissionProfile.Symbolic)]
    [InlineData(true, JavaScriptEmissionProfile.Symbolic)]
    [InlineData(false, JavaScriptEmissionProfile.Production)]
    [InlineData(true, JavaScriptEmissionProfile.Production)]
    public void One_source_retains_Copeland_types_styles_and_ownership_for_both_targets(bool vue, JavaScriptEmissionProfile profile)
    {
        var adapter = vue ? MirPresentationAdapter.Vue : MirPresentationAdapter.React;
        var compilation = Compile(PageSource, adapter);
        Assert.True(compilation.Success, Messages(compilation));
        var module = compilation.Modules.Single();
        var page = module.BoundCompilation!.Program.Functions.Single(function => function.Symbol.Name == "Page");
        Assert.Same(ViewTypeSymbol.Instance, page.Symbol.ReturnType);
        Assert.NotSame(ReactNodeTypeSymbol.Instance, page.Symbol.ReturnType);
        Assert.NotSame(VueNodeTypeSymbol.Instance, page.Symbol.ReturnType);
        Assert.Equal(RendererAdapterIdentity.Copeland, module.BoundCompilation.Program.ComponentDefinitions.Single().RendererAdapter);
        Assert.Equal(adapter, compilation.MirProjectGraph!.AggregateProgram.PresentationAdapter);
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph, new() { RuntimeTarget = JavaScriptRuntimeTarget.Browser, Profile = profile });
        emitted = LayoutJavaScriptProjectEmitter.AddLayouts(emitted, compilation.Modules);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var javascript = emitted.Files["Page.js"];
        Assert.Contains(vue ? "from \"vue\"" : "from \"react\"", javascript);
        Assert.DoesNotContain(vue ? "from \"react\"" : "from \"vue\"", javascript);
        Assert.Contains(vue ? "\"class\": \"content\"" : "className: \"content\"", javascript);
        Assert.Contains(vue ? "\"for\": \"toggle\"" : "htmlFor: \"toggle\"", javascript);
        Assert.Equal(vue, javascript.Contains("default: Object.assign(() =>"));
        var css = emitted.Files["generated/layouts.css"];
        Assert.Contains("font-size: 18px", css);
        Assert.Contains("line-height: 1.6", css);
        Assert.Contains("overflow-wrap: anywhere", css);
        Assert.Contains("border-radius: 8px", css);
    }

    [Fact]
    public void Adapter_identity_is_open_and_stateless_factory_conventions_are_data()
    {
        var adapter = new MirPresentationAdapter("test-custom-element", "factory", "create",
            MirPresentationChildren.Array, "class", "for");
        var compilation = Compile("export function Page(): View { return <section class=\"panel\">Hello</section>; }", adapter);
        Assert.True(compilation.Success, Messages(compilation));
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!, new() { RuntimeTarget = JavaScriptRuntimeTarget.Browser });
        Assert.True(emitted.Success);
        Assert.Contains("from \"factory\"", emitted.Files["Page.js"]);
        Assert.Contains("[\"Hello\"].flat()", emitted.Files["Page.js"]);
    }

    [Fact]
    public void Stateless_factory_does_not_guess_component_commit_semantics()
    {
        var adapter = new MirPresentationAdapter("test-custom-element", "factory", "create",
            MirPresentationChildren.Array, "class", "for");
        var compilation = Compile("export function Page(): View { state: int = 0; return <p>{state}</p>; }", adapter);
        Assert.Contains(compilation.Diagnostics, error => error.Id == "COPE-VIEW-0004");
    }

    [Fact]
    public void Renderer_owned_node_is_not_implicitly_a_Copeland_View()
    {
        var compilation = Compile("import { raw } from \"react\"; export function Page(): View { return raw(); }", MirPresentationAdapter.React);
        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, error => error.Message.Contains("ReactNode") && error.Message.Contains("View"));
    }

    [Theory]
    [InlineData("size: 1ui")]
    [InlineData("weight: 0")]
    [InlineData("lineHeight: 0")]
    [InlineData("wrap: \"ellipsis\"")]
    [InlineData("typo: \"red\"")]
    public void Text_style_has_exact_checked_fields_and_numeric_policies(string text)
    {
        var source = "layout Bad<0px, 0px> { width: fill; height: fit; column root { style: { text: { " + text + " } }; slot body { height: fit; } } }";
        var compilation = Compile(source, MirPresentationAdapter.React);
        Assert.Contains(compilation.Diagnostics, error => error.Id == "COPE-LAYOUT-STYLE-0001");
    }

    [Fact]
    public void Fixed_sibling_arguments_and_dynamic_sequences_retain_different_shapes()
    {
        var compilation = Compile("""
            export function Fixed(items: View[]): View { return <section><h2>Title</h2>{items}</section>; }
            export function Sequence(items: View[]): View { return <section>{items}</section>; }
            """, MirPresentationAdapter.React);
        Assert.True(compilation.Success, Messages(compilation));
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!, new() { RuntimeTarget = JavaScriptRuntimeTarget.Browser });
        Assert.True(emitted.Success);
        string javascript = emitted.Files["Page.js"];
        Assert.Contains("items), true)", javascript);
        Assert.Contains("items), false)", javascript);
        Assert.DoesNotContain("key:", javascript);
    }

    [Fact]
    public void Missing_adapter_is_a_project_error()
    {
        var compilation = Compile("export function Page(): View { return <p>Hello</p>; }", null);
        Assert.Contains(compilation.Diagnostics, error => error.Id == "COPE-VIEW-0002");
    }

    private static CopelandProjectCompilation Compile(string source, MirPresentationAdapter? adapter)
    {
        var packages = new CopelandNpmPackageContract[]
        {
            new("react", "test", [new("createElement", [], "ReactNode"), new("raw", [], "ReactNode")]),
            new("vue", "test", [new("h", [], "VueNode")]),
            new("factory", "test", [new("create", [], "View")]),
            new("controls", "test", [], Components:
            [
                new("Toggle", [new("id", "string"), new("value", "boolean", true),
                    new("onValueChange", "(boolean)=>void"), new("children", "View[]")],
                    Renderer: adapter?.Identity ?? "react", DefaultSlot: true),
            ]),
        };
        return CopelandProjectCompiler.CompileToMir([new("Page.tsx", "Page.tsx", source)], new()
        {
            ProjectTypes = CopelandProjectTypeSet.CopelandViews,
            PresentationAdapter = adapter,
            NpmDependencies = new CopelandNpmDependencyGraph(packages),
        });
    }

    private static string Messages(CopelandProjectCompilation compilation)
        => string.Join("\n", compilation.Diagnostics.Select(error => error.Id + ": " + error.Message));
}
