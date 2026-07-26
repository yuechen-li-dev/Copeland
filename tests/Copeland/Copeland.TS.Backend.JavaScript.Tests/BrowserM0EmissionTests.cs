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
}
