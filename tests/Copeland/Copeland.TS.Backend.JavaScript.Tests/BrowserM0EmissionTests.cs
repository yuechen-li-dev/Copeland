using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using System.Diagnostics;
using System.Text;
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

    [Fact]
    public void Browser_dispatch_callback_lifts_a_host_sender_to_a_retained_typed_callable()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { dispatchReact } from "@copeland/browser-v1";
                record State { count: int; }
                enum Event { Increment, }
                function Reduce(state: State, event: Event): State { return switch event { Increment => state with { count: state.count + 1 }, }; }
                export function Main(): void {
                    dispatchReact<State, Event>({ count: 0 }, Reduce, capture { Event } (state: State, send: (event: Event) => void) => {
                        send(Event.Increment);
                    });
                }
                """),
        ],
        new CopelandCompilationOptions { JavaScriptHostModules = [BrowserReactDispatchHost] });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
            project.MirProjectGraph!,
            new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        string output = emitted.Files["Main.js"];
        Assert.Contains("function __cope_callable_host_retained", output, StringComparison.Ordinal);
        Assert.Contains("host supplied a non-callable callback argument", output, StringComparison.Ordinal);
        Assert.Contains("let carrier = bySignature.get(signature);", output, StringComparison.Ordinal);
        Assert.Contains("__cope_callable_host_retained(\"(named:Event)->named:void\", args[1])", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browser_dispatch_sender_runs_one_transition_and_rejects_an_incompatible_event()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { dispatchReact } from "@fixture/browser";
                record State { count: int; }
                enum Event { Increment, }
                function Reduce(state: State, event: Event): State { return switch event { Increment => state with { count: state.count + 1 }, }; }
                export function Main(): void {
                    const increment: Event = Event.Increment;
                    dispatchReact<State, Event>({ count: 0 }, Reduce, capture { increment } (state: State, send: (event: Event) => void) => {
                        if (state.count == 0) {
                            send(increment);
                        }
                    });
                }
                """),
        ],
        new CopelandCompilationOptions { JavaScriptHostModules = [FixtureReactDispatchHost] });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
            project.MirProjectGraph!,
            new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser });
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        string root = Path.Combine(Path.GetTempPath(), "copeland-browser-dispatch-" + Guid.NewGuid().ToString("N"));
        string packageRoot = Path.Combine(root, "node_modules", "@fixture", "browser");
        Directory.CreateDirectory(packageRoot);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "package.json"), "{\"type\":\"module\",\"exports\":\"./index.js\"}", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "index.js"), """
                export function dispatchReact(initialState, reduce, render) {
                    let currentState = initialState;
                    let renderCount = 0;
                    let transitionCount = 0;
                    const send = event => {
                        const nextState = reduce(currentState, event);
                        transitionCount += 1;
                        currentState = nextState;
                        renderCount += 1;
                        render(currentState, send);
                    };
                    renderCount += 1;
                    render(currentState, send);
                    let invalidRejected = false;
                    try {
                        send({});
                    } catch {
                        invalidRejected = true;
                    }
                    globalThis.__dispatchResult = { renderCount, transitionCount, invalidRejected };
                    return send;
                }
                """, new UTF8Encoding(false));
            await File.WriteAllTextAsync(
                Path.Combine(root, "program.mjs"),
                emitted.Files["Main.js"] + "\nMain();\nconsole.log(JSON.stringify(globalThis.__dispatchResult));\n",
                new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo("node")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(Path.Combine(root, "program.mjs"));
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Node.js.");
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, stderr);
            Assert.Equal("{\"renderCount\":2,\"transitionCount\":1,\"invalidRejected\":true}\n", stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private static CopelandJavaScriptHostModuleContract BrowserReactDispatchHost { get; } = new(
        "@copeland/browser-v1",
        [
            new CopelandJavaScriptHostFunctionContract(
                "dispatchReact",
                [
                    new CopelandJavaScriptHostType.TypeParameter("State"),
                    new CopelandJavaScriptHostType.Callable(
                        [new CopelandJavaScriptHostType.TypeParameter("State"), new CopelandJavaScriptHostType.TypeParameter("Event")],
                        new CopelandJavaScriptHostType.TypeParameter("State")),
                    new CopelandJavaScriptHostType.Callable(
                        [
                            new CopelandJavaScriptHostType.TypeParameter("State"),
                            new CopelandJavaScriptHostType.Callable([new CopelandJavaScriptHostType.TypeParameter("Event")], CopelandJavaScriptHostType.Void),
                        ],
                        CopelandJavaScriptHostType.Void),
                ],
                new CopelandJavaScriptHostType.Callable([new CopelandJavaScriptHostType.TypeParameter("Event")], CopelandJavaScriptHostType.Void),
                ["State", "Event"]),
        ]);

    private static CopelandJavaScriptHostModuleContract FixtureReactDispatchHost { get; } = new(
        "@fixture/browser",
        BrowserReactDispatchHost.Exports);
}
