using Xunit;
using System.Diagnostics;
using System.Text;

namespace Copeland.Cli.Tests;

public sealed class CliIntegrationTests
{
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
    public async Task UnsupportedJavaScriptEmission_DoesNotWriteOutput()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function values(): number[] { return [1]; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.js");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-JS-0001", result.StdErr);
        Assert.False(File.Exists(outputPath));
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
