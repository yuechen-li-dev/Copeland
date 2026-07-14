using System.Diagnostics;
using System.Text;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class ResultBackendParityTests
{
    [Fact]
    public async Task JavaScript_And_CSharp_Observe_The_Same_Typed_Try_Except_Behavior()
    {
        const string source = """
            function bad(): number ! string { return err("bad"); }
            function main(): number {
              return try {
                try { bad()? } except (inner) { bad()? }
              } except (outer) {
                7
              };
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        object? csharpResult = GeneratedModuleInvoker.Invoke(generated.Assembly!, "main");

        Assert.Equal(7d, Assert.IsType<double>(csharpResult));
        Assert.Equal("7\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Classify_Unwrap_Panic_The_Same_Way()
    {
        const string source = """
            function bad(): number ! string { return err("bad"); }
            function main(): number { return bad()!; }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "try { main(); } catch (error) { console.log(error.message); }\n");

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Exception panic = Assert.ThrowsAny<Exception>(() => GeneratedModuleInvoker.Invoke(generated.Assembly!, "main"));

        Assert.Equal("COPE-PANIC-UNWRAP: Result unwrap encountered err", panic.Message);
        Assert.Equal("COPE-PANIC-UNWRAP: Result unwrap encountered err\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Observe_The_Same_Result_Behavior()
    {
        const string source = """
            enum Box { Value(outcome: number ! string), }
            function good(): number ! string { return ok(4); }
            function bad(): number ! string { return err("bad"); }
            function forward(value: number ! string): number ! string { return value; }
            function observe(value: number ! string): number {
              return match value { ok(resultValue) => resultValue, err(error) => 0, };
            }
            function stored(): number ! string {
              const outcome: number ! string = good();
              const value: number = outcome?;
              return value + 1;
            }
            function failedPropagation(): number ! string {
              const value: number = bad()?;
              return value + 1;
            }
            function boxed(): Box { return Box.Value(good()); }
            function inspectBox(box: Box): number {
              return match box { Value(outcome) => observe(outcome), };
            }
            function nested(): (number ! string) ! string { return ok(ok(7)); }
            function inspectNested(): number {
              return match nested() { ok(inner) => observe(inner), err(error) => 0, };
            }
            function saved(): void ! string { return; }
            function inspectSaved(): number {
              return match saved() { ok(value) => 1, err(error) => 0, };
            }
            function main(): number {
              return observe(forward(good())) + observe(bad()) + observe(stored()) + observe(failedPropagation()) + inspectBox(boxed()) + inspectNested() + inspectSaved();
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        object? csharpResult = GeneratedModuleInvoker.Invoke(generated.Assembly!, "main");

        Assert.Equal(21d, Assert.IsType<double>(csharpResult));
        Assert.Equal("21\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    private static async Task<ProcessResult> RunNodeAsync(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "copeland-result-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "program.js");
        try
        {
            await File.WriteAllTextAsync(scriptPath, source, new UTF8Encoding(false));
            var startInfo = new ProcessStartInfo("node")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(scriptPath);
            using Process process = Process.Start(startInfo)!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            return new ProcessResult(stdout, stderr);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed record ProcessResult(string StdOut, string StdErr);
}
