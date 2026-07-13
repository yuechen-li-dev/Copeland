using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class JavaScriptRuntimeTests
{
    [Fact]
    public async Task Node_Executes_Main_Returning_42_Repeatedly()
    {
        const string source = """
            function add(left: number, right: number): number {
              return left + right;
            }

            function main(): number {
              const answer: number = add(40, 2);
              return if true {
                answer
              } else {
                0
              };
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success);

        string executableScript = emitted.SourceText + "console.log(main());\n";
        ProcessResult first = await RunNodeAsync(executableScript);
        ProcessResult second = await RunNodeAsync(executableScript);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("42\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Executes_Primitive_Equality_Edge_Cases_Repeatedly()
    {
        const string source = """
            function booleanEqual(): boolean { return true == true; }
            function booleanNotEqual(): boolean { return true != false; }
            function booleanFalse(): boolean { return false == true; }
            function numberEqual(): boolean { return 42 == 42; }
            function numberNotEqual(): boolean { return 42 != 41; }
            function nanEqual(): boolean {
              const nan: number = 0 / 0;
              return nan == nan;
            }
            function nanNotEqual(): boolean {
              const nan: number = 0 / 0;
              return nan != nan;
            }
            function signedZeroEqual(): boolean {
              const positiveZero: number = 0;
              const negativeZero: number = 0 * (0 - 1);
              return positiveZero == negativeZero;
            }
            function signedZeroNotEqual(): boolean {
              const positiveZero: number = 0;
              const negativeZero: number = 0 * (0 - 1);
              return positiveZero != negativeZero;
            }
            function stringEqual(): boolean { return "same" == "same"; }
            function stringNotEqual(): boolean { return "same" != "different"; }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success);

        string executableScript = emitted.SourceText + """
            console.log(booleanEqual());
            console.log(booleanNotEqual());
            console.log(booleanFalse());
            console.log(numberEqual());
            console.log(numberNotEqual());
            console.log(nanEqual());
            console.log(nanNotEqual());
            console.log(signedZeroEqual());
            console.log(signedZeroNotEqual());
            console.log(stringEqual());
            console.log(stringNotEqual());
            """;

        ProcessResult first = await RunNodeAsync(executableScript);
        ProcessResult second = await RunNodeAsync(executableScript);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("true\ntrue\nfalse\ntrue\ntrue\nfalse\ntrue\ntrue\nfalse\ntrue\ntrue\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Executes_Payload_Enum_Match_Repeatedly()
    {
        const string source = """
            enum Inner {
              None,
              Number(value: number),
            }

            enum Outer {
              Empty,
              Pair(first: number, second: string),
              Nested(value: Inner),
            }

            function main(): string {
              const outer: Outer = Outer.Nested(Inner.Number(9));
              return match outer {
                Empty => "empty",
                Pair(first, second) => second,
                Nested(inner) => match inner {
                  None => "none",
                  Number(value) => "nested",
                },
              };
            }
            """;

        JavaScriptCompilation emitted = Emit(source);
        Assert.True(emitted.Success);

        string script = emitted.SourceText + "console.log(main());\n";
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("nested\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Executes_Result_Construction_Matching_Forwarding_And_Propagation_Repeatedly()
    {
        const string source = """
            enum Box {
              Value(outcome: number ! string),
            }

            function good(): number ! string { return ok(4); }
            function bad(): number ! string { return err("bad"); }
            function forward(value: number ! string): number ! string { return value; }

            function observe(value: number ! string): number {
              return match value {
                ok(value) => value,
                err(error) => 0,
              };
            }

            function propagatedGood(): number ! string {
              const value: number = good()?;
              return value + 1;
            }

            function propagatedBad(): number ! string {
              const value: number = bad()?;
              return value + 1;
            }

            function stored(): number ! string {
              const value: number ! string = good();
              const numberValue: number = value?;
              return numberValue + 2;
            }

            function boxed(): Box { return Box.Value(good()); }

            function inspectBox(value: Box): number {
              return match value {
                Value(outcome) => observe(outcome),
              };
            }

            function nested(): (number ! string) ! string { return ok(ok(7)); }

            function inspectNested(): number {
              return match nested() {
                ok(inner) => observe(inner),
                err(error) => 0,
              };
            }

            function saved(): void ! string { return; }

            function inspectSaved(): number {
              return match saved() {
                ok(value) => 1,
                err(error) => 0,
              };
            }
            """;

        JavaScriptCompilation emitted = Emit(source);
        string script = emitted.SourceText + """
            console.log(observe(good()));
            console.log(observe(bad()));
            console.log(observe(forward(bad())));
            console.log(observe(propagatedGood()));
            console.log(observe(propagatedBad()));
            console.log(observe(stored()));
            console.log(inspectBox(boxed()));
            console.log(inspectNested());
            console.log(inspectSaved());
            """;

        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("4\n0\n0\n5\n0\n6\n4\n7\n1\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Result_Match_Panics_Deterministically_For_Malformed_Private_Values()
    {
        JavaScriptCompilation emitted = Emit("""
            function good(): number ! string { return ok(1); }
            function other(): string ! string { return ok("other"); }
            function inspect(value: number ! string): number {
              return match value {
                ok(payload) => payload,
                err(error) => 0,
              };
            }
            """);

        string script = emitted.SourceText + """
            const valid = good();
            const malformedTag = Object.freeze(Object.assign(Object.create(null), {
              $type: valid.$type, $tag: "unknown", $payload: Object.freeze([1]),
            }));
            const malformedPayload = Object.freeze(Object.assign(Object.create(null), {
              $type: valid.$type, $tag: "ok", $payload: Object.freeze(["wrong"]),
            }));
            for (const value of [other(), malformedTag, malformedPayload]) {
              try { inspect(value); } catch (error) { console.log(error.message); }
            }
            """;

        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("Copeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Match_Scrutinee_Is_Emitted_Once_And_Invalid_Tag_Panics_Deterministically()
    {
        const string source = """
            enum Choice {
              A,
              B(value: number),
            }

            enum Other {
              A,
            }

            function make(): Choice {
              return Choice.A;
            }

            function inspect(choice: Choice): number {
              return match choice {
                A => 1,
                B(value) => value,
              };
            }

            function other(): Other {
              return Other.A;
            }

            function main(): number {
              return match make() {
                A => 1,
                B(value) => value,
              };
            }
            """;

        JavaScriptCompilation emitted = Emit(source);
        Assert.True(emitted.Success);
        Assert.Single(Regex.Matches(emitted.SourceText!, @"const __cope_m3_match_\d+ = make\(\);").Cast<Match>());
        Assert.Contains("default: return __cope_m3_panic_", emitted.SourceText, StringComparison.Ordinal);

        string script = emitted.SourceText + """
            console.log(main());
            try {
              inspect(other());
            } catch (error) {
              console.log(error.message);
            }
            const valid = make();
            const invalid = Object.freeze(Object.assign(Object.create(null), {
              $type: valid.$type,
              $tag: "Unknown",
              $payload: Object.freeze([]),
            }));
            try {
              inspect(invalid);
            } catch (error) {
              console.log(error.message);
            }
            const malformed = Object.freeze(Object.assign(Object.create(null), {
              $type: valid.$type,
              $tag: "B",
              $payload: Object.freeze([]),
            }));
            try {
              inspect(malformed);
            } catch (error) {
              console.log(error.message);
            }
            """;
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("1\nCopeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    private static JavaScriptCompilation Emit(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return emitted;
    }

    private static async Task<ProcessResult> RunNodeAsync(string script)
    {
        string directory = Path.Combine(Path.GetTempPath(), "copeland-javascript-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "program.js");
        try
        {
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                WorkingDirectory = directory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(scriptPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for JavaScript backend execution.");
            process.StandardInput.Close();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                KillProcessTree(process);
                await process.WaitForExitAsync();
                throw new TimeoutException(BuildFailureMessage("Node.js timed out", process.ExitCode, await stdoutTask, await stderrTask));
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new Xunit.Sdk.XunitException(BuildFailureMessage("Node.js failed", process.ExitCode, stdout, stderr));
            }

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Temporary test artifacts are cleaned up best-effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary test artifacts are cleaned up best-effort.
            }
        }
    }

    private static string BuildFailureMessage(string heading, int exitCode, string stdout, string stderr)
    {
        var message = new StringBuilder();
        message.AppendLine(heading);
        message.AppendLine($"Exit code: {exitCode}");
        message.AppendLine("stdout:");
        message.AppendLine(stdout);
        message.AppendLine("stderr:");
        message.AppendLine(stderr);
        return message.ToString();
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
