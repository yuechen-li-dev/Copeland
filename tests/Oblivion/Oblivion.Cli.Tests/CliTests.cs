using System.Text.Json;
using Oblivion.App;
using Xunit;

namespace Oblivion.Cli.Tests;

public sealed class CliTests
{
    [Theory]
    [InlineData("--help", "workspace")]
    [InlineData("workspace --help", "reload")]
    [InlineData("card show --help", "card-id")]
    [InlineData("card content --help", "complete Markdown source")]
    [InlineData("card push --help", "push it onto a Page stack")]
    [InlineData("card peek --help", "top (last) Card")]
    [InlineData("card pop --help", "safely delete owned files")]
    [InlineData("config --help", "persistent Oblivion application configuration")]
    [InlineData("command run --help", "command-id")]
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
    public async Task Card_content_human_output_is_the_exact_full_markdown_payload()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string contentPath = Path.Combine(vault.Root, "content", "physical-atom.md");
        string content = "# Exact UTF-8 source λ\r\n\r\n" + new string('x', 650) + "\r\n";
        File.WriteAllText(contentPath, content);

        CliResult result = await Run(
            "card",
            "content",
            "physical-atom",
            "-w",
            vault.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(content, result.Output);
        Assert.Empty(result.Error);
        Assert.DoesNotContain("Content:", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("...", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Card_content_json_is_deterministic_and_contains_full_semantic_identity()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string contentPath = Path.Combine(vault.Root, "content", "physical-atom.md");
        string content = "# JSON source\n\n" + new string('y', 650) + "\n";
        File.WriteAllText(contentPath, content);

        CliResult first = await Run(
            "card",
            "content",
            "physical-atom",
            "-w",
            vault.Root,
            "--page",
            "notebook",
            "--json");
        CliResult second = await Run(
            "card",
            "content",
            "physical-atom",
            "-w",
            vault.Root,
            "--page",
            "notebook",
            "--json");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(first.Output, second.Output);
        using JsonDocument json = JsonDocument.Parse(first.Output);
        Assert.Equal("m19i-notebook", json.RootElement.GetProperty("workspaceId").GetString());
        Assert.Equal("notebook", json.RootElement.GetProperty("pageId").GetString());
        Assert.Equal("physical-atom", json.RootElement.GetProperty("cardId").GetString());
        Assert.Equal("markdown", json.RootElement.GetProperty("contentKind").GetString());
        Assert.Equal("content/physical-atom.md", json.RootElement.GetProperty("source").GetString());
        Assert.Equal(content, json.RootElement.GetProperty("content").GetString());
        Assert.True(first.Output.IndexOf("\"workspaceId\"", StringComparison.Ordinal) <
            first.Output.IndexOf("\"content\"", StringComparison.Ordinal));
        Assert.Contains("\\n", first.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Card_content_failures_follow_human_and_json_contracts()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();

        CliResult unknownHuman = await Run("card", "content", "missing", "-w", vault.Root);
        CliResult unknownJson = await Run(
            "card",
            "content",
            "missing",
            "-w",
            vault.Root,
            "--json");
        File.Delete(Path.Combine(vault.Root, "content", "physical-atom.md"));
        CliResult missingContent = await Run(
            "card",
            "content",
            "physical-atom",
            "-w",
            vault.Root,
            "--json");

        Assert.Equal(OblivionCliExitCode.ProductFailure, unknownHuman.ExitCode);
        Assert.Empty(unknownHuman.Output);
        Assert.Contains("unknown-card", unknownHuman.Error, StringComparison.Ordinal);
        Assert.Equal(OblivionCliExitCode.ProductFailure, unknownJson.ExitCode);
        Assert.Empty(unknownJson.Error);
        Assert.Contains("unknown-card", unknownJson.Output, StringComparison.Ordinal);
        Assert.Equal(OblivionCliExitCode.ProductFailure, missingContent.ExitCode);
        Assert.Contains("missing-markdown-body-file", missingContent.Output, StringComparison.Ordinal);
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
    public async Task Config_show_get_set_have_human_json_and_typed_failure_contracts()
    {
        using TemporaryConfig config = new();

        CliResult defaults = await RunWithConfig(config.Path, "config", "show", "--json");
        CliResult set = await RunWithConfig(config.Path, "config", "set", "appearance", "dark", "--json");
        CliResult get = await RunWithConfig(config.Path, "config", "get", "appearance");
        CliResult invalidKey = await RunWithConfig(config.Path, "config", "get", "missing", "--json");
        CliResult invalidValue = await RunWithConfig(config.Path, "config", "set", "newline", "native");

        using JsonDocument defaultsJson = JsonDocument.Parse(defaults.Output);
        Assert.Equal("system", defaultsJson.RootElement.GetProperty("appearance").GetString());
        Assert.Equal("preserve", defaultsJson.RootElement.GetProperty("newline").GetString());
        Assert.Equal("default", defaultsJson.RootElement.GetProperty("style").GetString());
        using JsonDocument setJson = JsonDocument.Parse(set.Output);
        Assert.Equal("appearance", setJson.RootElement.GetProperty("key").GetString());
        Assert.Equal("dark", setJson.RootElement.GetProperty("value").GetString());
        Assert.True(setJson.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal("dark" + Environment.NewLine, get.Output);
        Assert.Equal(OblivionCliExitCode.ProductFailure, invalidKey.ExitCode);
        Assert.Contains("OBLIVION-CONFIG-KEY-UNKNOWN", invalidKey.Output, StringComparison.Ordinal);
        Assert.Equal(OblivionCliExitCode.ProductFailure, invalidValue.ExitCode);
        Assert.Contains("OBLIVION-CONFIG-VALUE-INVALID", invalidValue.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_list_and_run_use_App_registry_with_stable_json_and_exit_codes()
    {
        CliResult list = await Run("command", "list", "--json");
        CliResult reload = await Run("command", "run", "workspace.reload", "-w", FixtureRoot, "--json");
        CliResult expand = await Run("command", "run", "cards.expand-all", "-w", FixtureRoot, "--json");
        CliResult collapse = await Run("command", "run", "cards.collapse-all", "-w", FixtureRoot);
        CliResult unknown = await Run("command", "run", "view.reset", "-w", FixtureRoot, "--json");

        using JsonDocument listJson = JsonDocument.Parse(list.Output);
        Assert.Equal(3, listJson.RootElement.GetArrayLength());
        Assert.Equal("workspace.reload", listJson.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("active-page", listJson.RootElement[1].GetProperty("scope").GetString());
        Assert.True(listJson.RootElement[2].GetProperty("available").GetBoolean());
        using JsonDocument reloadJson = JsonDocument.Parse(reload.Output);
        Assert.True(reloadJson.RootElement.GetProperty("executed").GetBoolean());
        Assert.Equal("workspace.reload", reloadJson.RootElement.GetProperty("id").GetString());
        using JsonDocument expandJson = JsonDocument.Parse(expand.Output);
        Assert.Equal(2, expandJson.RootElement.GetProperty("affectedCards").GetInt32());
        Assert.Contains("Executed cards.collapse-all", collapse.Output, StringComparison.Ordinal);
        Assert.Equal(OblivionCliExitCode.ProductFailure, unknown.ExitCode);
        Assert.Contains("OBLIVION-COMMAND-UNKNOWN", unknown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_peek_and_pop_have_human_and_deterministic_json_results()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string source = vault.CreateExternalMarkdown(
            "Architecture Notes.md",
            "# Architecture notes\n\nA real imported note.\n");

        CliResult push = await Run("card", "push", source, "-w", vault.Root);
        CliResult peek = await Run("card", "peek", "-w", vault.Root);
        CliResult show = await Run("card", "show", "architecture-notes", "-w", vault.Root, "--json");
        CliResult pop = await Run("card", "pop", "-w", vault.Root, "--json");

        Assert.Equal(0, push.ExitCode);
        Assert.Contains("Pushed architecture-notes onto notebook.", push.Output, StringComparison.Ordinal);
        Assert.Contains("Stack size: 2 → 3.", push.Output, StringComparison.Ordinal);
        Assert.Equal(0, peek.ExitCode);
        Assert.Contains("Top Card: architecture-notes", peek.Output, StringComparison.Ordinal);
        using JsonDocument showJson = JsonDocument.Parse(show.Output);
        Assert.Equal("Architecture notes", showJson.RootElement.GetProperty("title").GetString());
        Assert.Equal("ImportedMarkdown", showJson.RootElement.GetProperty("provenanceKind").GetString());
        using JsonDocument popJson = JsonDocument.Parse(pop.Output);
        Assert.Equal("pop", popJson.RootElement.GetProperty("operation").GetString());
        Assert.Equal(3, popJson.RootElement.GetProperty("oldCount").GetInt32());
        Assert.Equal(2, popJson.RootElement.GetProperty("newCount").GetInt32());
        Assert.True(popJson.RootElement.GetProperty("contentDeleted").GetBoolean());
        Assert.False(File.Exists(Path.Combine(vault.Root, "cards", "architecture-notes.toml")));
        Assert.False(File.Exists(Path.Combine(vault.Root, "content", "architecture-notes.md")));
    }

    [Fact]
    public async Task Explicit_page_id_title_and_card_id_are_honored()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string source = vault.CreateExternalMarkdown("note.md", "# Ignored heading\n");

        CliResult result = await Run(
            "card",
            "push",
            source,
            "-w",
            vault.Root,
            "--page",
            "notebook",
            "--id",
            "explicit-note",
            "--title",
            "My Card",
            "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        Assert.Equal("notebook", json.RootElement.GetProperty("pageId").GetString());
        Assert.Equal("explicit-note", json.RootElement.GetProperty("cardId").GetString());
        Assert.Equal("My Card", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Duplicate_push_and_empty_stack_fail_with_structured_diagnostics()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string source = vault.CreateExternalMarkdown("physical-atom.md", "# Duplicate\n");
        string pageBefore = File.ReadAllText(Path.Combine(vault.Root, "pages", "notebook.toml"));

        CliResult duplicate = await Run("card", "push", source, "-w", vault.Root, "--json");

        Assert.Equal(OblivionCliExitCode.ProductFailure, duplicate.ExitCode);
        using JsonDocument duplicateJson = JsonDocument.Parse(duplicate.Output);
        Assert.Equal(
            "OBLIVION-CARD-ID-ALREADY-EXISTS",
            duplicateJson.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.Equal(pageBefore, File.ReadAllText(Path.Combine(vault.Root, "pages", "notebook.toml")));
        Assert.False(File.Exists(Path.Combine(vault.Root, "content", "physical-atom-2.md")));

        Assert.Equal(0, (await Run("card", "pop", "-w", vault.Root)).ExitCode);
        Assert.Equal(0, (await Run("card", "pop", "-w", vault.Root)).ExitCode);
        CliResult peek = await Run("card", "peek", "-w", vault.Root, "--json");
        CliResult pop = await Run("card", "pop", "-w", vault.Root, "--json");
        Assert.Equal(OblivionCliExitCode.ProductFailure, peek.ExitCode);
        Assert.Equal(OblivionCliExitCode.ProductFailure, pop.ExitCode);
        Assert.Contains("OBLIVION-STACK-EMPTY", peek.Output, StringComparison.Ordinal);
        Assert.Contains("OBLIVION-STACK-EMPTY", pop.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_rejects_missing_invalid_conflicting_sources_and_unknown_pages_without_mutation()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string pagePath = Path.Combine(vault.Root, "pages", "notebook.toml");
        string pageBefore = File.ReadAllText(pagePath);
        string missing = Path.Combine(Path.GetDirectoryName(vault.Root)!, "missing-note.md");
        string wrongExtension = vault.CreateExternalFile("wrong.txt", "not Markdown");
        string conflictSource = vault.CreateExternalMarkdown("conflict.md", "# Conflict\n");
        File.WriteAllText(Path.Combine(vault.Root, "content", "conflict.md"), "orphan collision");

        CliResult missingResult = await Run("card", "push", missing, "-w", vault.Root, "--json");
        CliResult invalidResult = await Run("card", "push", wrongExtension, "-w", vault.Root, "--json");
        CliResult conflictResult = await Run("card", "push", conflictSource, "-w", vault.Root, "--json");
        CliResult pageResult = await Run(
            "card",
            "push",
            conflictSource,
            "-w",
            vault.Root,
            "--page",
            "missing-page",
            "--json");

        Assert.Contains("OBLIVION-CARD-IMPORT-SOURCE-MISSING", missingResult.Output, StringComparison.Ordinal);
        Assert.Contains("OBLIVION-CARD-IMPORT-SOURCE-INVALID", invalidResult.Output, StringComparison.Ordinal);
        Assert.Contains("OBLIVION-CARD-IMPORT-DESTINATION-CONFLICT", conflictResult.Output, StringComparison.Ordinal);
        Assert.Contains("unknown-page", pageResult.Output, StringComparison.Ordinal);
        Assert.Equal(pageBefore, File.ReadAllText(pagePath));
        Assert.False(File.Exists(Path.Combine(vault.Root, "cards", "conflict.toml")));
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

    private static async Task<CliResult> RunWithConfig(string configPath, params string[] args)
    {
        StringWriter output = new();
        StringWriter error = new();
        OblivionConfigStore store = new(configPath);
        OblivionCli cli = new(output, error, configStore: store);
        int exitCode = await cli.InvokeAsync(args);
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

    private sealed class TemporaryConfig : IDisposable
    {
        public TemporaryConfig()
        {
            Directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "oblivion-m19m-cli-config-tests",
                Guid.NewGuid().ToString("N"));
            Path = System.IO.Path.Combine(Directory, "config.toml");
        }

        public string Directory { get; }
        public string Path { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    private sealed class TemporaryVault : IDisposable
    {
        private readonly string _externalRoot;

        private TemporaryVault(string root)
        {
            Root = root;
            _externalRoot = root + "-external";
            Directory.CreateDirectory(_externalRoot);
        }

        public string Root { get; }

        public string CreateExternalMarkdown(string fileName, string content)
        {
            return CreateExternalFile(fileName, content);
        }

        public string CreateExternalFile(string fileName, string content)
        {
            string path = Path.Combine(_externalRoot, fileName);
            File.WriteAllText(path, content);
            return path;
        }

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
            Directory.Delete(_externalRoot, recursive: true);
        }
    }
}
