using Xunit;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Copeland.Cli.Tests;

public sealed class CliIntegrationTests
{
    [Theory]
    [InlineData("mir", "tson-plan tson0")]
    [InlineData("csharp", "__TsonWriter")]
    [InlineData("javascript", "function makeWriter(")]
    public async Task Compile_emits_runtime_Tson_encoding_for_every_target(string emitTarget, string expectedToken)
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-encoding";
            record Settings { value: string; }
            function encode(value: Settings): string ! TsonEncodeError { return tsonEncode(value); }
            """);

        CliResult first = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget);
        CliResult second = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains(expectedToken, first.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("tsonEncode(", first.StdOut, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Failed_Tson_encoding_compilation_preserves_stale_output()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-encoding";
            record Unsupported { values: number[]; }
            function encode(value: Unsupported): string ! TsonEncodeError { return tsonEncode(value); }
            """);
        string outputPath = temp.WriteFile("output.g.js", "stale-output");

        CliResult result = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--out",
            outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TSON-ENCODE-0003", result.StdErr, StringComparison.Ordinal);
        Assert.Equal("stale-output", File.ReadAllText(outputPath));
    }

    [Theory]
    [InlineData("mir")]
    [InlineData("csharp")]
    [InlineData("javascript")]
    public async Task Compile_resolves_Tson_asset_relative_to_source_for_every_emit_target(string emitTarget)
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Item { label: string; }
            enum State { Off, On(value: number), }
            record Settings { empty: number[]; values: number[]; items: Item[]; states: State[]; rows: number[][]; }
            function main(): Settings {
                const settings: Settings = tsonAsset("./settings.obj.ts");
                return settings;
            }
            """);
        temp.WriteFile("settings.obj.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Item { label: string; }
            enum State { Off, On(value: number), }
            record Settings { empty: number[]; values: number[]; items: Item[]; states: State[]; rows: number[][]; }
            const $value: Settings = {
                empty: [],
                values: [1, 2],
                items: [{ label: "first" }],
                states: [State.Off, State.On(3)],
                rows: [[], [4]],
            };
            """);

        CliResult first = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget);
        CliResult second = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget);

        Assert.Equal(0, first.ExitCode);
        Assert.DoesNotContain("tsonAsset", first.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.obj.ts", first.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Tson", first.StdOut, StringComparison.Ordinal);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Failed_Tson_asset_compilation_preserves_stale_output()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Settings { value: number; }
            function main(): number {
                const settings: Settings = tsonAsset("./missing.tson");
                return settings.value;
            }
            """);
        string outputPath = temp.WriteFile("output.g.js", "stale-output");

        CliResult result = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--out",
            outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TSON-ASSET-0002", result.StdErr, StringComparison.Ordinal);
        Assert.Equal("stale-output", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task Invalid_array_asset_preserves_stale_output_without_deleting_unrelated_files()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Settings { values: number[]; }
            function main(): Settings { const settings: Settings = tsonAsset("./settings.obj.ts"); return settings; }
            """);
        temp.WriteFile("settings.obj.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Settings { values: number[]; }
            const $value: Settings = { values: ["wrong"], };
            """);
        string outputPath = temp.WriteFile("output.g.js", "stale-output");
        string unrelatedPath = temp.WriteFile("keep.txt", "preserve-me");

        CliResult result = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--out",
            outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TSON-0004", result.StdErr, StringComparison.Ordinal);
        Assert.Equal("stale-output", File.ReadAllText(outputPath));
        Assert.Equal("preserve-me", File.ReadAllText(unrelatedPath));
    }

    [Fact]
    public async Task EmitMirToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function one(): number {
  return 1;
}
""");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("module", result.StdOut);
        Assert.Contains("func one() -> number", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task EmitCSharpToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function one(): number {
  return 1;
}
""");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("namespace Copeland.Generated", result.StdOut);
        Assert.Contains("public static double one()", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task EmitMirToFile()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.cope");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir", "--out", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("module", Normalize(File.ReadAllText(outputPath)));
        Assert.Contains($"wrote {outputPath}", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task EmitCSharpToFile()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.cs");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("namespace Copeland.Generated", Normalize(File.ReadAllText(outputPath)));
    }

    [Fact]
    public async Task EmitJavaScriptToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"use strict\";", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("function one()", result.StdOut, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task EmitJavaScriptEquality_Executes_In_Node()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
            function main(): boolean {
              const nan: number = 0 / 0;
              return nan != nan;
            }
            """);
        string outputPath = System.IO.Path.Combine(temp.Path, "output.g.js");

        CliResult compilation = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(0, compilation.ExitCode);
        Assert.True(File.Exists(outputPath));
        string emitted = Normalize(File.ReadAllText(outputPath));
        Assert.Contains("nan !== nan", emitted, StringComparison.Ordinal);
        Assert.DoesNotMatch("(?<![=!])==(?!=)", emitted);
        Assert.DoesNotMatch("(?<!!)!=(?!=)", emitted);

        File.AppendAllText(outputPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, outputPath);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("true\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Fact]
    public async Task EmitJavaScriptPayloadEnumMatch_Executes_In_Node()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
            enum Choice {
              Empty,
              Pair(first: number, second: string),
            }

            function main(): string {
              const choice: Choice = Choice.Pair(1, "ordered");
              return match choice {
                Empty => "empty",
                Pair(first, second) => second,
              };
            }
            """);
        string outputPath = System.IO.Path.Combine(temp.Path, "output.g.js");

        CliResult compilation = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(0, compilation.ExitCode);
        string emitted = Normalize(File.ReadAllText(outputPath));
        Assert.Contains("Object.create(null)", emitted, StringComparison.Ordinal);
        Assert.Contains("switch (__cope_m3_match_", emitted, StringComparison.Ordinal);

        File.AppendAllText(outputPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, outputPath);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("ordered\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Fact]
    public async Task EmitResultMir_CSharp_And_JavaScript()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("result.ts", """
            function result(): number ! string { return ok(4); }
            function main(): number {
              return match result() {
                ok(value) => value,
                err(error) => 0,
              };
            }
            """);
        string outputPath = System.IO.Path.Combine(temp.Path, "result.g.js");

        CliResult mir = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir");
        CliResult csharp = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp");
        CliResult javascript = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(0, mir.ExitCode);
        Assert.Contains("result-match", mir.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, csharp.ExitCode);
        Assert.Contains("CopeResult", csharp.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, javascript.ExitCode);
        Assert.True(File.Exists(outputPath));

        File.AppendAllText(outputPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, outputPath);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("4\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Fact]
    public async Task JavaScriptArrayEmission_WritesOrdinaryArrayOutput()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function values(): number[] { return [1]; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.js");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("return [1];", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Table_mir_csharp_and_javascript_emission_succeed()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("table.ts", "record table Values { value: [1, 2]; } function main(): number { const row: Values.Row = Values[1]!; return row.value; }");
        string mirPath = Path.Combine(temp.Path, "table.cope");
        string csharpPath = Path.Combine(temp.Path, "table.g.cs");
        string javaScriptPath = Path.Combine(temp.Path, "table.g.js");

        CliResult mir = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir", "--out", mirPath);
        CliResult csharp = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", csharpPath);
        CliResult javaScript = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", javaScriptPath);

        Assert.Equal(0, mir.ExitCode);
        Assert.True(File.Exists(mirPath));
        Assert.Equal(0, csharp.ExitCode);
        Assert.True(File.Exists(csharpPath));
        string generated = await File.ReadAllTextAsync(csharpPath);
        Assert.Contains("__CopeTable_t1", generated, StringComparison.Ordinal);
        Assert.Equal(0, javaScript.ExitCode);
        Assert.True(File.Exists(javaScriptPath));
        await File.AppendAllTextAsync(javaScriptPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, javaScriptPath);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("2\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Fact]
    public async Task RecordMirCSharpAndJavaScriptSucceed_AndJavaScriptExecutes()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("record.ts", "record Point { x: number; } function main(): number { const point: Point = { x: 42 }; return point.x; }");
        var csharpPath = System.IO.Path.Combine(temp.Path, "record.g.cs");
        var javaScriptPath = System.IO.Path.Combine(temp.Path, "record.g.js");

        CliResult mir = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir");
        CliResult csharp = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", csharpPath);
        CliResult javaScript = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", javaScriptPath);

        Assert.Equal(0, mir.ExitCode);
        Assert.Contains("record Point [r1]", mir.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, csharp.ExitCode);
        Assert.True(File.Exists(csharpPath));
        string generatedCSharp = await File.ReadAllTextAsync(csharpPath);
        Assert.Contains("public sealed class __CopeRecord_r1", generatedCSharp, StringComparison.Ordinal);
        Assert.DoesNotContain("record __CopeRecord", generatedCSharp, StringComparison.Ordinal);
        Assert.Equal(0, javaScript.ExitCode);
        Assert.Equal(string.Empty, javaScript.StdErr);
        Assert.True(File.Exists(javaScriptPath));
        string generatedJavaScript = await File.ReadAllTextAsync(javaScriptPath);
        Assert.Contains("Symbol(\"r1\")", generatedJavaScript, StringComparison.Ordinal);
        Assert.DoesNotContain("COPE-JS-REC-0001", generatedJavaScript, StringComparison.Ordinal);

        await File.AppendAllTextAsync(javaScriptPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, javaScriptPath);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("42\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Theory]
    [InlineData("mir", "record.cope")]
    [InlineData("csharp", "record.g.cs")]
    [InlineData("javascript", "record.g.js")]
    public async Task Rejected_Record_Source_Does_Not_Overwrite_Or_Masquerade_An_Earlier_Artifact(
        string emitTarget,
        string outputName)
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile(
            "record.ts",
            "record Point { x: number; } function main(): Point { return { x: 42 }; }");
        string outputPath = System.IO.Path.Combine(temp.Path, outputName);

        CliResult accepted = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            emitTarget,
            "--out",
            outputPath);
        Assert.Equal(0, accepted.ExitCode);
        string retainedArtifact = await File.ReadAllTextAsync(outputPath);

        temp.WriteFile(
            "record.ts",
            "record Point { x: number; } function main(): Point { return { y: 42 }; }");
        CliResult rejected = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            emitTarget,
            "--out",
            outputPath);

        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("COPE-REC-0007", rejected.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("wrote", rejected.StdOut, StringComparison.Ordinal);
        Assert.Equal(retainedArtifact, await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task InvalidSourceExitsOneAndDoesNotWriteOutput()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function main(): number {
  const x: number = "bad";
  return x;
}
""");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.cs");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TYPE", result.StdErr);
        Assert.False(File.Exists(outputPath));
        Assert.DoesNotContain("namespace Copeland.Generated", result.StdOut);
    }


    [Fact]
    public async Task Ternary_Profile_Ban_ExitsOne_With_Diagnostic()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function value(flag: boolean): number {
  return flag ? 1 : 2;
}
""");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-PROFILE-0007", result.StdErr);
    }

    [Fact]
    public async Task MissingEmitExitsTwo()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");

        var result = await RunCliAsync(temp.Path, "compile", inputPath);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.StdErr);
        Assert.Contains("COPE-CLI-0001", result.StdErr);
    }

    [Fact]
    public async Task UnknownEmitExitsTwo()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "wasm");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("COPE-CLI-0002", result.StdErr);
    }

    [Fact]
    public async Task MissingInputFileExitsThree()
    {
        using var temp = new TempDir();
        var missingPath = System.IO.Path.Combine(temp.Path, "missing.ts");

        var result = await RunCliAsync(temp.Path, "compile", missingPath, "--emit", "mir");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("COPE-CLI-0008", result.StdErr);
    }

    [Fact]
    public async Task MarkdownParseMirJsonToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.md", "# Heading");

        var result = await RunCliAsync(temp.Path, "markdown", "parse", inputPath, "--emit", "mir", "--format", "json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"kind\": \"DocumentMir\"", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"Heading\"", result.StdOut, StringComparison.Ordinal);
    }

    private static async Task<CliResult> RunCliAsync(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(GetCliAssemblyPath());
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Copeland CLI process.");
        process.StandardInput.Close();

        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync();

            string timedOutStdOut = Normalize(await stdOutTask);
            string timedOutStdErr = Normalize(await stdErrTask);
            throw new TimeoutException(BuildTimeoutMessage(args, timedOutStdOut, timedOutStdErr));
        }
        finally
        {
            if (!process.HasExited)
            {
                KillProcessTree(process);
            }
        }

        string stdOut = Normalize(await stdOutTask);
        string stdErr = Normalize(await stdErrTask);

        return new CliResult(process.ExitCode, stdOut, stdErr);
    }

    private static async Task<CliResult> RunExecutableAsync(string fileName, string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        process.StandardInput.Close();
        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync();
            throw new TimeoutException(BuildTimeoutMessage(args, Normalize(await stdOutTask), Normalize(await stdErrTask)));
        }

        return new CliResult(process.ExitCode, Normalize(await stdOutTask), Normalize(await stdErrTask));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }

    private static string BuildTimeoutMessage(string[] args, string stdOut, string stdErr)
    {
        var message = new StringBuilder();
        message.AppendLine($"Copeland CLI exceeded the 10 second test timeout. Arguments: {string.Join(' ', args)}");
        message.AppendLine("stdout:");
        message.AppendLine(stdOut);
        message.AppendLine("stderr:");
        message.AppendLine(stdErr);
        return message.ToString();
    }

    private static string GetCliAssemblyPath()
    {
        var repoRoot = GetRepoRoot();
        var testOutputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        string targetFramework = testOutputDirectory.Name;
        string configuration = testOutputDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        string cliAssemblyPath = System.IO.Path.Combine(
            repoRoot,
            "src",
            "Copeland",
            "Copeland.Cli",
            "bin",
            configuration,
            targetFramework,
            "Copeland.Cli.dll");

        if (!File.Exists(cliAssemblyPath))
        {
            throw new FileNotFoundException(
                "The Copeland CLI must be built before its process contract tests run.",
                cliAssemblyPath);
        }

        return cliAssemblyPath;
    }

    private static string GetRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "copeland-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteFile(string relativePath, string text)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            File.WriteAllText(fullPath, text);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
