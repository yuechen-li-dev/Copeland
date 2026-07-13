using System.Diagnostics;
using System.Text;
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
