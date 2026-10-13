using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class VuePresentationTests
{
    [Fact]
    public void Vue_nodes_layout_templates_and_component_facts_retain_the_selected_renderer()
    {
        var compilation = Compile("""
            import { h } from "vue";
            template<static gap: Length = 8px>
            layout Panels<0px, 0px> {
                width: fill;
                height: fit;
                column root { gap: gap; slot body { height: fit; } }
            }
            layout Base = instantiate Panels<>;
            layout Styled = Base with { root.style: { fill: "#e8eef0" }; };
            export function Page(): VueNode {
                state: int = 0;
                on Increment() => state + 1;
                stream Content { body: <button onClick={on.Increment}>{state}</button>; }
                return bind Styled using Content;
            }
            """);
        Assert.True(compilation.Success, Messages(compilation));
        var definition = compilation.Modules.Single().BoundCompilation!.Program.ComponentDefinitions.Single();
        Assert.Equal(RendererAdapterIdentity.Vue, definition.RendererAdapter);
        Assert.Equal(ComponentContentCapabilities.VueSubtree, definition.RequiredContentCapabilities);
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!, new() { RuntimeTarget = JavaScriptRuntimeTarget.Browser });
        emitted = LayoutJavaScriptProjectEmitter.AddLayouts(emitted, compilation.Modules);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        string javascript = emitted.Files["Page.js"];
        Assert.DoesNotContain("from \"react\"", javascript);
        Assert.Contains("__cope_component_vue_host", javascript);
        Assert.Contains("onUpdated", javascript);
        Assert.Contains("\"class\": \"m-", javascript);
        Assert.Contains("background: #e8eef0", emitted.Files["generated/layouts.css"]);
    }

    [Fact]
    public void Vue_component_update_events_use_typed_endpoints_and_default_slots_are_functions()
    {
        var compilation = Compile("""
            import { h } from "vue";
            import { Toggle } from "controls";
            export function Page(): VueNode {
                state: boolean = false;
                on Set(value: boolean) => value;
                return <section class="panel"><label for="toggle">Arrangement</label>
                    <Toggle id="toggle" modelValue={state} onUpdate:modelValue={on.Set}>Change arrangement</Toggle>
                </section>;
            }
            """);
        Assert.True(compilation.Success, Messages(compilation));
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!, new() { RuntimeTarget = JavaScriptRuntimeTarget.Browser });
        Assert.True(emitted.Success);
        Assert.Contains("\"onUpdate:modelValue\":", emitted.Files["Page.js"]);
        Assert.Contains("default: Object.assign(() =>", emitted.Files["Page.js"]);
    }

    [Fact]
    public void Empty_foreign_component_children_do_not_create_a_non_function_default_slot()
    {
        var compilation = Compile("""
            import { h } from "vue";
            import { Toggle } from "controls";
            export function Page(): VueNode { return <Toggle modelValue={false} />; }
            """);
        Assert.True(compilation.Success, Messages(compilation));
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!, new() { RuntimeTarget = JavaScriptRuntimeTarget.Browser });
        Assert.Contains("h(Toggle, { modelValue: false }, null)", emitted.Files["Page.js"]);
    }

    [Theory]
    [InlineData("<Toggle modelValue={\"wrong\"} />", "COPE-REACT-0014")]
    [InlineData("<Toggle typo={false} />", "COPE-REACT-0013")]
    [InlineData("<Toggle />", "COPE-REACT-0015")]
    [InlineData("<ReactControl />", "COPE-COMPONENT-RENDERER-0001")]
    [InlineData("<Closed>Unexpected slot</Closed>", "COPE-REACT-0016")]
    public void Foreign_component_contract_errors_are_compiler_errors(string node, string diagnostic)
    {
        var compilation = Compile($$"""
            import { h } from "vue";
            import { Toggle, ReactControl, Closed } from "controls";
            export function Page(): VueNode { return {{node}}; }
            """);
        Assert.Contains(compilation.Diagnostics, error => error.Id == diagnostic);
    }

    [Theory]
    [InlineData("on Edit(value: string) => value;", "COPE-COMPONENT-STATE-0010")]
    [InlineData("on Edit() => state after AttachmentsSettled effect External();", "COPE-COMPONENT-ON-0005")]
    [InlineData("on Edit() => Impure(state);", "COPE-COMPONENT-ON-0007")]
    public void Vue_preserves_state_type_effect_phase_and_purity_checks(string handler, string diagnostic)
    {
        var compilation = Compile($$"""
            import { h } from "vue";
            import { focus } from "host";
            function External(): void { focus("target"); }
            function Impure(value: int): int { focus("target"); return value; }
            export function Page(): VueNode {
                state: int = 0;
                {{handler}}
                return <button>Page</button>;
            }
            """);
        Assert.Contains(compilation.Diagnostics, error => error.Id == diagnostic);
    }

    [Fact]
    public void A_project_must_select_one_component_renderer()
    {
        var compilation = Compile("export function Value(): int { return 1; }",
            CopelandProjectTypeSet.ReactComponents | CopelandProjectTypeSet.VueComponents);
        Assert.Contains(compilation.Diagnostics, error => error.Id == "COPE-COMPONENT-RENDERER-0002");
    }

    [Fact]
    public void Vue_node_names_are_not_available_without_explicit_project_selection()
    {
        var compilation = Compile("export function Page(): VueNode { return <section />; }", CopelandProjectTypeSet.None);
        Assert.Contains(compilation.Diagnostics, error => error.Id == "COPE-TSXML-0101");
    }

    private static CopelandProjectCompilation Compile(string source, CopelandProjectTypeSet types = CopelandProjectTypeSet.VueComponents)
        => CopelandProjectCompiler.CompileToMir([new("Page.tsx", "Page.tsx", source)], new()
        {
            ProjectTypes = types,
            JavaScriptHostModules = [CopelandProjectHostContracts.Browser(types)],
            NpmPackages =
            [
                new("vue", "3.5.43", [new("h", [], "VueNode")]),
                new("host", "0.1.0", [new("focus", ["string"], "void")]),
                new("controls", "0.1.0", [], Components:
                [
                    new("Toggle", Properties:
                    [
                        new("id", "string"), new("modelValue", "boolean", true),
                        new("onUpdate:modelValue", "(boolean)=>void"),
                    ], Renderer: "vue", DefaultSlot: true),
                    new("ReactControl"),
                    new("Closed", Renderer: "vue"),
                ]),
            ],
        });

    private static string Messages(CopelandProjectCompilation compilation)
        => string.Join("\n", compilation.Diagnostics.Select(error => error.Id + ": " + error.Message));
}
