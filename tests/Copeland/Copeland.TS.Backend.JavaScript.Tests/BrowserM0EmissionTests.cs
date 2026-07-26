using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class BrowserM0EmissionTests
{
    [Fact]
    public void Browser_profile_emits_a_typed_host_callback_and_keeps_host_imports_in_the_owning_module()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Counter.ts", "Counter.ts", "export function Increment(value: int): int { return value + 1; }"),
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { Increment } from "./Counter";
                import { onClick, setText } from "@copeland/browser-m0";
                export function Main(): void {
                    const countElement: string = "count";
                    setText(countElement, "0");
                    onClick("increment", capture { countElement } (current: int): int => {
                        const next: int = Increment(current);
                        setText(countElement, String.From(next));
                        return next;
                    });
                }
                """),
        ],
        new CopelandCompilationOptions { JavaScriptHostModules = [BrowserHost] });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
            project.MirProjectGraph!,
            new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.Contains("import { Increment } from \"./Counter.js\";", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("import { onClick } from \"@copeland/browser-m0\";", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("__cope_callable_capture", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("__cope_callable_invoke", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.DoesNotContain("@copeland/browser-m0", emitted.Files["Counter.js"], StringComparison.Ordinal);
    }

    [Fact]
    public void Node_profile_rejects_browser_host_contracts()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { setText } from "@copeland/browser-m0";
                export function Main(): void { setText("count", "0"); }
                """),
        ],
        new CopelandCompilationOptions { JavaScriptHostModules = [BrowserHost] });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.Compilation!.MirCompilation!.Program!);

        Assert.Contains(emitted.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-BROWSER-0001");
    }

    [Fact]
    public void Switch_is_an_exhaustive_pattern_alias_for_match()
    {
        const string prefix = "enum Event { Increment, Reset, } function Reduce(event: Event): int { return ";
        const string suffix = " event { Increment => 1, Reset => 0, }; }";

        CopelandCompilation match = CopelandCompiler.CompileToMir(prefix + "match" + suffix);
        CopelandCompilation @switch = CopelandCompiler.CompileToMir(prefix + "switch" + suffix);
        CopelandCompilation incomplete = CopelandCompiler.CompileToMir(
            "enum Event { Increment, Reset, } function Reduce(event: Event): int { return switch event { Increment => 1, }; }");

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Diagnostics));
        Assert.True(@switch.Success, string.Join(Environment.NewLine, @switch.Diagnostics));
        Assert.Equal(match.MirText, @switch.MirText);
        Assert.Contains(incomplete.Diagnostics, diagnostic => diagnostic.Id == "COPE-MATCH-0004");
    }

    [Fact]
    public void Browser_dispatch_specializes_host_type_parameters_and_wraps_returned_callable()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { dispatch } from "@copeland/browser-v1";
                record State { count: int; }
                enum Event { Increment, }
                function Reduce(state: State, event: Event): State { return switch event { Increment => state with { count: state.count + 1 }, }; }
                export function Main(): void {
                    const send: (event: Event) => void = dispatch<State, Event>({ count: 0 }, Reduce, state => {});
                    send(Event.Increment);
                }
                """),
        ],
        new CopelandCompilationOptions { JavaScriptHostModules = [BrowserDispatchHost] });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
            project.MirProjectGraph!,
            new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.Contains("__cope_callable_host", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("dispatch(", emitted.Files["Main.js"], StringComparison.Ordinal);
    }

    private static CopelandJavaScriptHostModuleContract BrowserHost { get; } = new(
        "@copeland/browser-m0",
        [
            new CopelandJavaScriptHostFunctionContract(
                "setText",
                [CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String],
                CopelandJavaScriptHostType.Void),
            new CopelandJavaScriptHostFunctionContract(
                "onClick",
                [
                    CopelandJavaScriptHostType.String,
                    new CopelandJavaScriptHostType.Callable([CopelandJavaScriptHostType.Int], CopelandJavaScriptHostType.Int),
                ],
                CopelandJavaScriptHostType.Void),
        ]);

    private static CopelandJavaScriptHostModuleContract BrowserDispatchHost { get; } = new(
        "@copeland/browser-v1",
        [
            new CopelandJavaScriptHostFunctionContract(
                "dispatch",
                [
                    new CopelandJavaScriptHostType.TypeParameter("State"),
                    new CopelandJavaScriptHostType.Callable(
                        [new CopelandJavaScriptHostType.TypeParameter("State"), new CopelandJavaScriptHostType.TypeParameter("Event")],
                        new CopelandJavaScriptHostType.TypeParameter("State")),
                    new CopelandJavaScriptHostType.Callable(
                        [new CopelandJavaScriptHostType.TypeParameter("State")],
                        CopelandJavaScriptHostType.Void),
                ],
                new CopelandJavaScriptHostType.Callable(
                    [new CopelandJavaScriptHostType.TypeParameter("Event")],
                    CopelandJavaScriptHostType.Void),
                ["State", "Event"]),
        ]);
}
