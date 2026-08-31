using System.Text.Json;
using Xunit;

namespace Oblivion.Cli.Tests;

public sealed class CliTests
{
    [Theory]
    [InlineData("--help", "workspace")]
    [InlineData("workspace --help", "reload")]
    [InlineData("card show --help", "card-id")]
    public async Task Generated_help_is_discoverable(string commandLine, string expected)
    {
        CliResult result = await Run(commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expected, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("workspace show", "--workspace")]
    [InlineData("unknown", "Unrecognized command")]
    public async Task Usage_errors_return_two_without_stack_traces(string commandLine, string expected)
    {
        CliResult result = await Run(commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(OblivionCliExitCode.UsageError, result.ExitCode);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workspace_show_has_useful_human_and_stable_json_output()
    {
        CliResult human = await Run("workspace", "show", "--workspace", FixtureRoot);
        CliResult first = await Run("workspace", "show", "--workspace", FixtureRoot, "--json");
        CliResult second = await Run("workspace", "show", "--workspace", FixtureRoot, "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("Workspace: m19i-notebook", human.Output, StringComparison.Ordinal);
        Assert.Contains("Pages: 1", human.Output, StringComparison.Ordinal);
        Assert.Equal(first.Output, second.Output);
        using JsonDocument json = JsonDocument.Parse(first.Output);
        Assert.Equal("m19i-notebook", json.RootElement.GetProperty("workspaceId").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("cardCount").GetInt32());
    }

    [Fact]
    public async Task Validate_reports_structured_diagnostics_and_product_exit_code()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        File.Delete(Path.Combine(vault.Root, "content", "physical-atom.md"));

        CliResult result = await Run(
            "workspace",
            "validate",
            "--workspace",
            vault.Root,
            "--json");

        Assert.Equal(OblivionCliExitCode.ProductFailure, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("valid").GetBoolean());
        Assert.Contains(
            json.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "missing-markdown-body-file");
    }

    [Fact]
    public async Task Page_and_card_commands_preserve_semantic_order_and_card_detail()
    {
        CliResult pages = await Run("page", "list", "--workspace", FixtureRoot, "--json");
        CliResult cards = await Run("card", "list", "--workspace", FixtureRoot, "--json");
        CliResult card = await Run(
            "card",
            "show",
            "physical-atom",
            "--workspace",
            FixtureRoot,
            "--json");

        using JsonDocument pageJson = JsonDocument.Parse(pages.Output);
        using JsonDocument cardsJson = JsonDocument.Parse(cards.Output);
        using JsonDocument cardJson = JsonDocument.Parse(card.Output);
        Assert.Equal("notebook", pageJson.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("physical-atom", cardsJson.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("notebook-stack", cardsJson.RootElement[1].GetProperty("id").GetString());
        Assert.Equal("content/physical-atom.md", cardJson.RootElement.GetProperty("markdownSource").GetString());
        Assert.True(cardJson.RootElement.GetProperty("contentPreview").GetString()!.Length <= 404);
    }

    [Fact]
    public async Task Unknown_card_is_a_structured_product_failure()
    {
        CliResult result = await Run(
            "card",
            "show",
            "missing",
            "--workspace",
            FixtureRoot,
            "--json");

        Assert.Equal(OblivionCliExitCode.ProductFailure, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "unknown-card",
            json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reload_runs_the_App_owned_transaction_and_reports_session()
    {
        CliResult result = await Run(
            "workspace",
            "reload",
            "--workspace",
            FixtureRoot,
            "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("reloaded").GetBoolean());
        Assert.Equal("notebook", json.RootElement.GetProperty("session").GetProperty("activePageId").GetString());
        Assert.Equal("physical-atom", json.RootElement.GetProperty("session").GetProperty("selectedCardId").GetString());
    }

    [Fact]
    public void Cli_project_has_no_forbidden_direct_dependencies_or_vault_parsing()
    {
        string repositoryRoot = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Oblivion",
            "Oblivion.Cli",
            "Oblivion.Cli.csproj"));
        string source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(
                    Path.Combine(repositoryRoot, "src", "Oblivion", "Oblivion.Cli"),
                    "*.cs")
                .Select(File.ReadAllText));

        Assert.DoesNotContain("Avalonia", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Machina", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Presenter", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian", project, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Read", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Toml", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Oblivion.Persistence", source, StringComparison.Ordinal);
        Assert.Contains("Oblivion.App", project, StringComparison.Ordinal);
    }

    private static string FixtureRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "M19iNotebook.oblivion");

    private static async Task<CliResult> Run(params string[] args)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await OblivionCli.RunAsync(args, output, error);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Oblivion.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryVault : IDisposable
    {
        private TemporaryVault(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryVault CopyFixture()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "oblivion-m19j-cli-tests",
                Guid.NewGuid().ToString("N"));
            foreach (string sourcePath in Directory.GetFiles(FixtureRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(FixtureRoot, sourcePath);
                string destinationPath = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            return new TemporaryVault(root);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
