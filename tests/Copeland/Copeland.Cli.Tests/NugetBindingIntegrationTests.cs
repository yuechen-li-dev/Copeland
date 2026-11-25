using System.Text.Json;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed partial class CliIntegrationTests
{
    [Fact]
    public async Task Signature_bindings_run_the_same_NuGet_calls_on_Node_and_CLR()
    {
        using var project = new TempDir();
        CopyBrowserNugetSample(project);

        // Exercise both the in-memory generator and a generated source visible to project tools.
        await BuildNugetSample(project, "node");
        CliResult generation = await RunCliAsync(project.Path, "nuget", "generate", ".");
        Assert.True(generation.ExitCode == 0, generation.StdErr);
        string path = Path.Combine(project.Path, "src", "generated", "Nuget.ts");
        string bindings = await File.ReadAllTextAsync(path);
        Assert.Contains("record QuoteRequest { arg0: string; }", bindings);
        Assert.Contains("record FormatBooleanRequest { arg0: boolean; }", bindings);
        Assert.Contains("record QuoteReply { value: string; }", bindings);
        CliResult check = await RunCliAsync(project.Path, "check", "--standalone", ".");
        Assert.True(check.ExitCode == 0, check.StdErr + check.StdOut);
        await BuildNugetSample(project, "clr");

        using var outside = new TempDir();
        CliResult node = await RunExecutableAsync("node", outside.Path, Path.Combine(project.Path, "dist", "node", "main.js"));
        CliResult clr = await RunExecutableAsync("dotnet", outside.Path, Path.Combine(project.Path, "dist", "clr", "main.dll"));
        Assert.True(node.ExitCode == 0, node.StdErr);
        Assert.True(clr.ExitCode == 0, clr.StdErr);
        Assert.Equal("\"Ship the docs\" | true | typed error\n", node.StdOut);
        Assert.Equal(node.StdOut, clr.StdOut);
        Assert.True(File.Exists(Path.Combine(project.Path, "dist", "clr", "Newtonsoft.Json.dll")));
        Assert.False(File.Exists(Path.Combine(project.Path, "dist", "clr", "Copeland.TS.Sidecar.dll")));

        await File.AppendAllTextAsync(path, "\n// stale\n");
        CliResult stale = await RunCliAsync(project.Path, "build", "--standalone", ".", "--target", "node", "--result", "stale.json");
        Assert.NotEqual(0, stale.ExitCode);
        Assert.Contains("tscl nuget generate", await File.ReadAllTextAsync(Path.Combine(project.Path, "stale.json")));
    }

    [Fact]
    public async Task Signature_generation_rejects_a_CLR_object_return_before_emission()
    {
        using var project = new TempDir();
        CopyBrowserNugetSample(project);
        project.WriteFile("nuget-worker.json", """
            {
                "schemaVersion": 2,
                "packages": [{ "id": "Newtonsoft.Json", "version": "13.0.4" }],
                "operations": [{
                    "name": "json:parse",
                    "package": "Newtonsoft.Json",
                    "type": "Newtonsoft.Json.Linq.JToken",
                    "method": "Parse",
                    "parameters": ["string"],
                    "export": "Parse"
                }]
            }
            """);
        CliResult result = await RunCliAsync(project.Path, "nuget", "generate", ".");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("json:parse", result.StdErr);
        Assert.Contains("unsupported TSON type", result.StdErr);
        Assert.False(File.Exists(Path.Combine(project.Path, "src", "generated", "Nuget.ts")));
    }

    private static async Task BuildNugetSample(TempDir project, string target)
    {
        string receipt = Path.Combine(project.Path, target + ".json");
        CliResult build = await RunCliAsync(project.Path, "build", "--standalone", ".", "--target", target, "--result", receipt);
        string report = File.Exists(receipt) ? await File.ReadAllTextAsync(receipt) : string.Empty;
        Assert.True(build.ExitCode == 0, build.StdErr + report);
        using JsonDocument result = JsonDocument.Parse(report);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
    }

    private static void CopyBrowserNugetSample(TempDir project)
    {
        string sample = Path.Combine(GetRepoRoot(), "samples", "copeland-ts", "nuget-browser-m4");
        Directory.CreateDirectory(Path.Combine(project.Path, "src"));
        foreach (string path in new[] { "App.csproj", "manifest.tsx", "tsconfig.tsx", "nuget-worker.json", "src/Main.ts" })
        {
            File.Copy(Path.Combine(sample, path), Path.Combine(project.Path, path));
        }
    }
}
