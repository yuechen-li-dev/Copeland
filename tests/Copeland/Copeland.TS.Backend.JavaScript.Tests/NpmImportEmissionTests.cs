using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class NpmImportEmissionTests
{
    [Fact]
    public void Emits_a_native_named_esm_import_for_npm_calls()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Source, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmPackages = [new CopelandNpmPackageContract("@fixture/transform", "1.0.0", [new CopelandNpmFunctionContract("delayedTransform", ["Request"], "Response", "RemoteError", true)])],
        });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("import { delayedTransform } from \"@fixture/transform\";", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("Promise.resolve(delayedTransform(", emitted.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_the_authored_alias_from_module_import_metadata()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(AliasedSource, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph(
            [
                new CopelandNpmPackageContract(
                    "@fixture/transform",
                    "1.0.0",
                    [new CopelandNpmFunctionContract("delayedTransform", ["Request"], "Response", "RemoteError", IsPromise: true)]),
            ]),
        });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("import { delayedTransform as transformLater } from \"@fixture/transform\";", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("transformLater(", emitted.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Deduplicates_repeated_identical_named_imports()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(RepeatedImportSource, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph(
            [
                new CopelandNpmPackageContract(
                    "@fixture/transform",
                    "1.0.0",
                    [new CopelandNpmFunctionContract("delayedTransform", ["Request"], "Response", "RemoteError", IsPromise: true)]),
            ]),
        });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Equal(1, emitted.SourceText!.Split("import { delayedTransform } from \"@fixture/transform\";", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Executes_a_real_local_esm_package_with_positional_arguments()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(PositionalSource, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph(
            [
                new CopelandNpmPackageContract("@fixture/math", "1.0.0", [new CopelandNpmFunctionContract("sum", ["number", "number"], "number", "RemoteError")]),
            ]),
        });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("sum(frame.left, frame.right)", emitted.SourceText, StringComparison.Ordinal);

        string root = Path.Combine(Path.GetTempPath(), "copeland-npm-js-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "@fixture", "math"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "@fixture", "math", "package.json"), "{\"type\":\"module\",\"exports\":\"./index.js\"}", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "@fixture", "math", "index.js"), "export function sum(left, right) { return left + right; }", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(root, "program.mjs"), emitted.SourceText + "\nconst pending = add(2, 3); pending.subscribe(() => console.log(pending.value.$payload[0]), () => {}, () => {}, () => {});\n", new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo("node")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(Path.Combine(root, "program.mjs"));
            using Process process = Process.Start(startInfo)!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.Equal(0, process.ExitCode);
            Assert.Equal("5\n", stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Executes_arrays_records_and_promise_exports_from_a_real_local_esm_package()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(ComprehensiveSource, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph(
            [
                new CopelandNpmPackageContract("@fixture/interop", "1.0.0",
                [
                    new CopelandNpmFunctionContract("zero", [], "boolean", "RemoteError"),
                    new CopelandNpmFunctionContract("mirrorArray", ["number[]", "number"], "number[]", "RemoteError"),
                    new CopelandNpmFunctionContract("mirrorRecord", ["Input", "number"], "Output", "RemoteError"),
                    new CopelandNpmFunctionContract("delayed", ["number", "number"], "string[]", "RemoteError", IsPromise: true),
                ]),
            ]),
        });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("npmMirrorArray(frame.values, frame.increment)", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("npmMirrorRecord(frame.input, frame.increment)", emitted.SourceText, StringComparison.Ordinal);

        string root = Path.Combine(Path.GetTempPath(), "copeland-npm-js-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "@fixture", "interop"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "@fixture", "interop", "package.json"), "{\"type\":\"module\",\"exports\":\"./index.js\"}", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "@fixture", "interop", "index.js"), """
                export function zero() { console.log("zero"); return true; }
                export function mirrorArray(values, increment) { console.log("array:" + values.join(",") + ":" + increment); return values.map(value => value + increment); }
                export function mirrorRecord(input, increment) { const [value, label] = Object.getOwnPropertySymbols(input).map(symbol => input[symbol]).filter(value => typeof value === "number" || typeof value === "string"); console.log("record:" + label + ":" + value + ":" + increment); return { output: label + "-" + (value + increment), passed: true }; }
                export async function delayed(value, delay) { await new Promise(resolve => setTimeout(resolve, delay)); console.log("delayed:" + value); return [String(value)]; }
                """, new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(root, "program.mjs"), emitted.SourceText + """

                const pending = [zero(), mapArray([1, 2, 3], 1), mapRecord(makeInput(4, "record"), 5), delayed(1, 120), delayed(2, 5)];
                let remaining = pending.length;
                for (const item of pending) item.subscribe(() => { if (--remaining === 0) console.log("complete"); }, () => {}, () => { console.log("failed"); }, () => {});
                """, new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo("node") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            startInfo.ArgumentList.Add(Path.Combine(root, "program.mjs"));
            using Process process = Process.Start(startInfo)!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, stderr);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("zero", stdout, StringComparison.Ordinal);
            Assert.Contains("array:1,2,3:1", stdout, StringComparison.Ordinal);
            Assert.Contains("record:record:4:5", stdout, StringComparison.Ordinal);
            Assert.True(stdout.IndexOf("delayed:2", StringComparison.Ordinal) < stdout.IndexOf("delayed:1", StringComparison.Ordinal));
            Assert.Contains("complete", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string Source = """
        import { delayedTransform } from "@fixture/transform";
        const $schema: string = "copeland://npm/test";
        record Request { value: number; }
        record Response { value: number; }
        record RemoteError { message: string; }
        function request(value: number): Request { return { value }; }
        async function load(value: number): Response ! RemoteError { const pending: Async<Response ! RemoteError> = delayedTransform(request(value)); return await pending; }
        """;

    private const string AliasedSource = """
        import { delayedTransform as transformLater } from "@fixture/transform";
        const $schema: string = "copeland://npm/test";
        record Request { value: number; }
        record Response { value: number; }
        record RemoteError { message: string; }
        function request(value: number): Request { return { value }; }
        async function load(value: number): Response ! RemoteError { const pending: Async<Response ! RemoteError> = transformLater(request(value)); return await pending; }
        """;

    private const string RepeatedImportSource = """
        import { delayedTransform } from "@fixture/transform";
        import { delayedTransform } from "@fixture/transform";
        const $schema: string = "copeland://npm/test";
        record Request { value: number; }
        record Response { value: number; }
        record RemoteError { message: string; }
        function request(value: number): Request { return { value }; }
        async function load(value: number): Response ! RemoteError { const pending: Async<Response ! RemoteError> = delayedTransform(request(value)); return await pending; }
        """;

    private const string PositionalSource = """
        import { sum } from "@fixture/math";
        const $schema: string = "copeland://npm/test";
        record RemoteError { message: string; }
        async function add(left: number, right: number): number ! RemoteError {
            const pending: Async<number ! RemoteError> = sum(left, right);
            return await pending;
        }
        """;

    private const string ComprehensiveSource = """
        import { delayed as npmDelayed, mirrorArray as npmMirrorArray, mirrorRecord as npmMirrorRecord, zero as npmZero } from "@fixture/interop";
        const $schema: string = "copeland://npm/test";
        record Input { value: number; label: string; }
        record Output { output: string; passed: boolean; }
        record RemoteError { message: string; }
        function makeInput(value: number, label: string): Input { return { value, label }; }
        async function zero(): boolean ! RemoteError { const pending: Async<boolean ! RemoteError> = npmZero(); return await pending; }
        async function mapArray(values: number[], increment: number): number[] ! RemoteError { const pending: Async<number[] ! RemoteError> = npmMirrorArray(values, increment); return await pending; }
        async function mapRecord(input: Input, increment: number): Output ! RemoteError { const pending: Async<Output ! RemoteError> = npmMirrorRecord(input, increment); return await pending; }
        async function delayed(value: number, delay: number): string[] ! RemoteError { const pending: Async<string[] ! RemoteError> = npmDelayed(value, delay); return await pending; }
        """;
}
