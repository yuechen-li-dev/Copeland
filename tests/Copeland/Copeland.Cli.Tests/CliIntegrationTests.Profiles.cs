using System.Text.Json;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed partial class CliIntegrationTests
{
    [Fact]
    public async Task File_profile_dispatch_preserves_tsc_process_arguments_output_and_exit_status()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "node_modules/typescript/bin"));
        temp.WriteFile("node_modules/typescript/bin/tsc", """
            console.log(JSON.stringify({ arguments: process.argv.slice(2), cwd: process.cwd() }));
            console.error("delegated diagnostic");
            process.exit(7);
            """);
        string config = temp.WriteFile("tsconfig.json", "{}");
        string source = temp.WriteFile("Legacy.ts", "export type CopelandProfile = 'Legacy';\nconst value: any = undefined;");
        CliResult result = await RunCliAsync(temp.Path, "compile", source, "--", "--noEmit", "--pretty", "false", "--out", "raw.js");
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("delegated diagnostic\n", result.StdErr);
        using JsonDocument json = JsonDocument.Parse(result.StdOut);
        Assert.Equal(["--project", config, "--noEmit", "--pretty", "false", "--out", "raw.js"],
            json.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(temp.Path, json.RootElement.GetProperty("cwd").GetString());
    }

    [Fact]
    public async Task File_profile_without_a_project_compiler_stops_without_copeland_fallback()
    {
        using var temp = new TempDir();
        string source = temp.WriteFile("Legacy.ts", "export type CopelandProfile = 'Legacy';\nconst value: any = undefined;");
        CliResult result = await RunCliAsync(temp.Path, "compile", source);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-PROFILE-0005", result.StdErr);
        Assert.Contains("local TypeScript compiler", result.StdErr);
        Assert.DoesNotContain("COPE-PARSE", result.StdErr);
    }

    [Theory]
    [InlineData("JavaScript", "csharp")]
    [InlineData("Dotnet", "javascript")]
    [InlineData("Dotnet-NativeAOT", "javascript")]
    [InlineData("Legacy", "csharp")]
    public async Task File_profile_rejects_incompatible_emitters(string profile, string emitter)
    {
        using var temp = new TempDir();
        string source = temp.WriteFile("Main.ts", $"export type CopelandProfile = '{profile}';\nexport function answer(): int {{ return 42; }}");
        CliResult result = await RunCliAsync(temp.Path, "compile", source, "--emit", emitter);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-PROFILE-0004", result.StdErr);
    }

    [Fact]
    public async Task File_profile_selects_default_emission_and_workspace_headers_fill_unowned_files()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "copeland"));
        string source = temp.WriteFile("src/Free.ts", "export type CopelandProfile = 'JavaScript';\nexport function answer(): int { return 42; }");
        CliResult emitted = await RunCliAsync(temp.Path, "compile", source);
        Assert.Equal(0, emitted.ExitCode);
        Assert.Contains("function", emitted.StdOut);
        temp.WriteFile("App.csproj", "<Project />");
        temp.WriteFile("tsconfig.tsx", """
            export default defineTypeScriptWorkspace({
                tsc: { include: ["legacy/**"] },
                tscl: { project: "./App.csproj", include: ["copeland/**"] }
            });
            """);
        CliResult sync = await RunCliAsync(temp.Path, "workspace", "sync", "--format", "json");
        Assert.Equal(0, sync.ExitCode);
        CliResult owner = await RunCliAsync(temp.Path, "workspace", "owner", source, "--format", "json");
        using JsonDocument json = JsonDocument.Parse(owner.StdOut);
        Assert.Equal("JavaScript", json.RootElement.GetProperty("profile").GetString());
        Assert.Equal("tscl", json.RootElement.GetProperty("owner").GetString());
        Assert.Equal("CopelandProfile", json.RootElement.GetProperty("matchedRule").GetString());
        temp.WriteFile("copeland/Conflict.ts", "export type CopelandProfile = 'Legacy';");
        CliResult conflict = await RunCliAsync(temp.Path, "workspace", "validate", "--format", "json");
        Assert.Equal(1, conflict.ExitCode);
        Assert.Contains("COPE-PROFILE-0003", conflict.StdOut);
    }

    [Fact]
    public async Task File_profile_build_contract_checks_sources_before_emitting_an_artifact()
    {
        using var temp = new TempDir();
        string source = temp.WriteFile("Main.ts", "export type CopelandProfile = 'Dotnet';\nexport function main(): int { return 42; }");
        string project = temp.WriteFile("project.json", JsonSerializer.Serialize(new
        {
            projectRoot = temp.Path,
            entry = new { module = "Main.ts", @export = "main" },
            sources = new[] { new { logicalPath = "Main.ts", path = source } },
            outputDirectory = Path.Combine(temp.Path, "dist"),
            javascriptRuntime = "node",
            entryOutputPath = "main.js",
        }));
        string resultPath = Path.Combine(temp.Path, "result.json");
        CliResult result = await RunCliAsync(temp.Path, "build", "--project", project, "--result", resultPath);
        Assert.Equal(1, result.ExitCode);
        string diagnostics = await File.ReadAllTextAsync(resultPath);
        Assert.True(diagnostics.Contains("COPE-PROFILE-0004", StringComparison.Ordinal), diagnostics);
        Assert.False(File.Exists(Path.Combine(temp.Path, "dist/main.js")));
    }
}
