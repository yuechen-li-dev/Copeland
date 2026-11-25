using System.Text.Json;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed partial class CliIntegrationTests
{
    [Fact]
    public async Task Published_Node_app_runs_Newtonsoft_in_the_generated_worker()
    {
        using var project = new TempDir();
        CopyNugetSample(project);
        string receipt = Path.Combine(project.Path, "build.json");
        CliResult build = await RunCliAsync(project.Path, "build", "--standalone", ".", "--target", "node", "--result", receipt);
        string report = File.Exists(receipt) ? await File.ReadAllTextAsync(receipt) : string.Empty;
        Assert.True(build.ExitCode == 0, build.StdErr + report);
        using JsonDocument result = JsonDocument.Parse(report);

        using var published = new TempDir();
        string[] paths = result.RootElement.GetProperty("outputs").EnumerateArray()
            .Select(output => output.GetProperty("path").GetString()!).ToArray();
        Assert.Contains("copeland-nuget/host.mjs", paths);
        Assert.Contains("copeland-nuget/worker/NugetWorker.dll", paths);
        Assert.Contains("copeland-nuget/worker/Newtonsoft.Json.dll", paths);
        Assert.DoesNotContain(paths, path => Path.GetFileName(path).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));
        foreach (string path in paths)
        {
            string output = Path.Combine(published.Path, path);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(Path.Combine(project.Path, "dist", path), output);
        }
        using var outside = new TempDir();
        string entry = Path.Combine(published.Path, "main.js");
        CliResult execution = await RunExecutableAsync("node", outside.Path, entry);
        Assert.True(execution.ExitCode == 0, execution.StdErr);
        Assert.Equal("\"Ship the docs\"\n", execution.StdOut);

        File.Delete(Path.Combine(published.Path, "copeland-nuget", "worker", "NugetWorker.dll"));
        CliResult missingWorker = await RunExecutableAsync("node", outside.Path, entry);
        Assert.NotEqual(0, missingWorker.ExitCode);
        Assert.Contains("Copeland entry transport failed.", missingWorker.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_NuGet_worker_preserves_arrays_and_typed_CLR_errors()
    {
        using var project = new TempDir();
        CopyNugetSample(project);
        project.WriteFile("nuget-worker.json", """
            {
                "schemaVersion": 1,
                "packages": [{ "id": "Newtonsoft.Json", "version": "13.0.4" }],
                "operations": [
                    { "name": "concat", "type": "System.String", "method": "Concat" },
                    { "name": "parse", "type": "System.Double", "method": "Parse" },
                    { "name": "args", "type": "System.Environment", "method": "GetCommandLineArgs" }
                ]
            }
            """);
        project.WriteFile("src/Main.ts", """
            const $schema: string = "copeland://test/nuget";
            record Texts { values: string[]; }
            record TextValue { value: string; }
            record NumberValue { value: number; }
            record PackageError { message: string; }
            record Empty {}

            function CheckArgs(value: Texts): string {
                if (value.values.length == 1) { return "array reply"; }
                return "bad array";
            }

            function CheckError(error: PackageError): string {
                if (error.message.length > 0) { return "typed error"; }
                return "empty error";
            }

            export async function Main(): string {
                const texts: Texts = { values: ["left", "right"] };
                const invalid: TextValue = { value: "not a number" };
                const empty: Empty = {};
                const concatenation: Async<TextValue ! PackageError> = tsonCall<TextValue, PackageError>("concat", texts);
                const parse: Async<NumberValue ! PackageError> = tsonCall<NumberValue, PackageError>("parse", invalid);
                const args: Async<Texts ! PackageError> = tsonCall<Texts, PackageError>("args", empty);
                const concatResult: TextValue ! PackageError = await concatenation;
                const parseResult: NumberValue ! PackageError = await parse;
                const argsResult: Texts ! PackageError = await args;
                const a: string = match concatResult { ok(value) => value.value, err(error) => error.message, };
                const b: string = match parseResult { ok(value) => "unexpected success", err(error) => CheckError(error), };
                const c: string = match argsResult { ok(value) => CheckArgs(value), err(error) => error.message, };
                return a + "|" + b + "|" + c;
            }
            """);
        string receipt = Path.Combine(project.Path, "build.json");
        CliResult build = await RunCliAsync(project.Path, "build", "--standalone", ".", "--target", "node", "--result", receipt);
        string report = File.Exists(receipt) ? await File.ReadAllTextAsync(receipt) : string.Empty;
        Assert.True(build.ExitCode == 0, build.StdErr + report);
        CliResult execution = await RunExecutableAsync("node", project.Path, Path.Combine(project.Path, "dist", "main.js"));
        Assert.True(execution.ExitCode == 0, execution.StdErr);
        Assert.Equal("leftright|typed error|array reply\n", execution.StdOut);
    }

    private static void CopyNugetSample(TempDir project)
    {
        string sample = Path.Combine(GetRepoRoot(), "samples", "copeland-ts", "nuget-worker-m3");
        Directory.CreateDirectory(Path.Combine(project.Path, "src"));
        foreach (string path in new[] { "App.csproj", "manifest.tsx", "tsconfig.tsx", "nuget-worker.json", "src/Main.ts" })
        {
            File.Copy(Path.Combine(sample, path), Path.Combine(project.Path, path));
        }
    }
}
