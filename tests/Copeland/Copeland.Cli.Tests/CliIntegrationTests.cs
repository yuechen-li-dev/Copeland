using Xunit;
using System.Diagnostics;

namespace Copeland.Cli.Tests;

public sealed class CliIntegrationTests
{
    [Fact]
    public void EmitMirToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function one(): number {
  return 1;
}
""");

        var result = RunCli(temp.Path, "compile", inputPath, "--emit", "mir");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("module", result.StdOut);
        Assert.Contains("func one() -> number", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void EmitCSharpToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function one(): number {
  return 1;
}
""");

        var result = RunCli(temp.Path, "compile", inputPath, "--emit", "csharp");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("namespace Copeland.Generated", result.StdOut);
        Assert.Contains("public static double one()", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void EmitMirToFile()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.cope");

        var result = RunCli(temp.Path, "compile", inputPath, "--emit", "mir", "--out", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("module", Normalize(File.ReadAllText(outputPath)));
        Assert.Contains($"wrote {outputPath}", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void EmitCSharpToFile()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.cs");

        var result = RunCli(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("namespace Copeland.Generated", Normalize(File.ReadAllText(outputPath)));
    }

    [Fact]
    public void InvalidSourceExitsOneAndDoesNotWriteOutput()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function main(): number {
  const x: number = "bad";
  return x;
}
""");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.cs");

        var result = RunCli(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TYPE", result.StdErr);
        Assert.False(File.Exists(outputPath));
        Assert.DoesNotContain("namespace Copeland.Generated", result.StdOut);
    }


    [Fact]
    public void Ternary_Profile_Ban_ExitsOne_With_Diagnostic()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function value(flag: boolean): number {
  return flag ? 1 : 2;
}
""");

        var result = RunCli(temp.Path, "compile", inputPath, "--emit", "csharp");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-PROFILE-0007", result.StdErr);
    }

    [Fact]
    public void MissingEmitExitsTwo()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");

        var result = RunCli(temp.Path, "compile", inputPath);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.StdErr);
        Assert.Contains("COPE-CLI-0001", result.StdErr);
    }

    [Fact]
    public void UnknownEmitExitsTwo()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");

        var result = RunCli(temp.Path, "compile", inputPath, "--emit", "wasm");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("COPE-CLI-0002", result.StdErr);
    }

    [Fact]
    public void MissingInputFileExitsThree()
    {
        using var temp = new TempDir();
        var missingPath = System.IO.Path.Combine(temp.Path, "missing.ts");

        var result = RunCli(temp.Path, "compile", missingPath, "--emit", "mir");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("COPE-CLI-0008", result.StdErr);
    }

    [Fact]
    public void MarkdownParseMirJsonToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.md", "# Heading");

        var result = RunCli(temp.Path, "markdown", "parse", inputPath, "--emit", "mir", "--format", "json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"kind\": \"DocumentMir\"", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"Heading\"", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownExportCorpusWritesArtifacts()
    {
        using var temp = new TempDir();
        var outputPath = System.IO.Path.Combine(temp.Path, "m12a");

        var result = RunCli(temp.Path, "markdown", "export-corpus", "--output-dir", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(outputPath, "copeland-markdown-readme.mir.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(outputPath, "copeland-markdown-closeout.mir.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(outputPath, "copeland-markdown-corpus-report.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(outputPath, "copeland-markdown-corpus-report.txt")));
    }

    private static CliResult RunCli(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(GetCliProjectPath());
        psi.ArgumentList.Add("--");
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdOut = Normalize(process.StandardOutput.ReadToEnd());
        var stdErr = Normalize(process.StandardError.ReadToEnd());
        process.WaitForExit();

        return new CliResult(process.ExitCode, stdOut, stdErr);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string GetCliProjectPath()
    {
        var repoRoot = GetRepoRoot();
        return System.IO.Path.Combine(repoRoot, "src", "Copeland", "Copeland.Cli", "Copeland.Cli.csproj");
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
