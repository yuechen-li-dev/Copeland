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
    [InlineData("function run --help", "Function Card id")]
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
            diagnostic => diagnostic.GetProperty("code").GetString() == "OBLIVION-MISSING-MARKDOWN-BODY-FILE");
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
    public async Task Table_card_list_show_and_content_follow_structured_contracts()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "M20eTsonTables.oblivion");
        CliResult list = await Run("card", "list", "-w", root);
        CliResult show = await Run("card", "show", "validation-evidence", "-w", root, "--json");
        CliResult content = await Run("card", "content", "validation-evidence", "-w", root, "--json");

        Assert.Equal(0, list.ExitCode);
        Assert.Contains("validation-evidence\tvalidation-review\ttable", list.Output, StringComparison.Ordinal);
        Assert.Equal(0, show.ExitCode);
        using JsonDocument json = JsonDocument.Parse(show.Output);
        Assert.Equal("table", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("obj.ts", json.RootElement.GetProperty("tableProfile").GetString());
        Assert.Equal(16, json.RootElement.GetProperty("tableRowCount").GetInt32());
        Assert.Equal(7, json.RootElement.GetProperty("tableColumnCount").GetInt32());
        Assert.Equal("order", json.RootElement.GetProperty("tableColumnNames")[0].GetString());
        Assert.Equal(OblivionCliExitCode.ProductFailure, content.ExitCode);
        Assert.Contains("OBLIVION-CARD-CONTENT-NOT-TEXT", content.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Card_read_returns_paged_table_rows_and_exact_diagram_text()
    {
        string tableRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "M20eTsonTables.oblivion");
        CliResult table = await Run(
            "card", "read", "validation-evidence",
            "-w", tableRoot,
            "--offset", "3",
            "--limit", "4",
            "--fidelity", "full",
            "--json-compact");

        Assert.Equal(0, table.ExitCode);
        using JsonDocument tableJson = JsonDocument.Parse(table.Output);
        Assert.Equal(16, tableJson.RootElement.GetProperty("totalItems").GetInt32());
        Assert.Equal(4, tableJson.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(4, tableJson.RootElement.GetProperty("rows")[0].GetProperty("order").GetInt32());
        Assert.Contains("not tokenizer-exact", tableJson.RootElement.GetProperty("cost").GetProperty("method").GetString());

        string diagramRoot = Path.Combine(
            FindRepositoryRoot(),
            "src", "Oblivion", "Oblivion.Standalone", "M19oDiagramCards.oblivion");
        CliResult diagram = await Run(
            "card", "read", "vehicle-flow-state",
            "-w", diagramRoot,
            "--fidelity", "full",
            "--json-compact");

        Assert.Equal(0, diagram.ExitCode);
        using JsonDocument diagramJson = JsonDocument.Parse(diagram.Output);
        Assert.True(diagramJson.RootElement.GetProperty("nodes").GetArrayLength() >= 3);
        Assert.Contains("--", diagramJson.RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("-->", diagramJson.RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_projects_fields_and_headless_render_is_deterministic_for_mature_content()
    {
        CliResult query = await Run(
            "card", "query",
            "-w", FixtureRoot,
            "--contains", "physical",
            "--fields", "id,pageId,title",
            "--json-compact");
        Assert.Equal(0, query.ExitCode);
        using JsonDocument queryJson = JsonDocument.Parse(query.Output);
        Assert.Equal("physical-atom", queryJson.RootElement[0].GetProperty("id").GetString());
        Assert.False(queryJson.RootElement[0].TryGetProperty("summary", out _));

        string diagramRoot = Path.Combine(
            FindRepositoryRoot(),
            "src", "Oblivion", "Oblivion.Standalone", "M19oDiagramCards.oblivion");
        string tableRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "M20eTsonTables.oblivion");
        string directory = Path.Combine(Path.GetTempPath(), "oblivion-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string tablePath = Path.Combine(directory, "table.png");
            string documentPath = Path.Combine(directory, "document.png");
            string diagramPath = Path.Combine(directory, "diagram.png");
            CliResult table = await Run("card", "render", "validation-evidence", "-w", tableRoot, "--out", tablePath, "--json-compact");
            CliResult document = await Run("card", "render", "physical-atom", "-w", FixtureRoot, "--out", documentPath, "--json-compact");
            CliResult diagram = await Run("card", "render", "vehicle-flow-state", "-w", diagramRoot, "--out", diagramPath, "--json-compact");
            Assert.Equal(0, table.ExitCode);
            Assert.Equal(0, document.ExitCode);
            Assert.Equal(0, diagram.ExitCode);
            Assert.All(new[] { tablePath, documentPath, diagramPath }, path =>
            {
                Assert.True(new FileInfo(path).Length > 1000);
                Assert.Equal(new byte[] { 137, 80, 78, 71 }, File.ReadAllBytes(path)[..4]);
            });
            using JsonDocument first = JsonDocument.Parse(diagram.Output);
            string firstHash = first.RootElement.GetProperty("sha256").GetString()!;
            CliResult rerender = await Run("card", "render", "vehicle-flow-state", "-w", diagramRoot, "--out", diagramPath, "--json-compact");
            using JsonDocument second = JsonDocument.Parse(rerender.Output);
            Assert.Equal(firstHash, second.RootElement.GetProperty("sha256").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Function_run_uses_exact_card_and_returns_structured_runner_result()
    {
        string root = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Oblivion",
            "Oblivion.Standalone",
            "M20fFunctionCards.oblivion");

        OblivionWorkspaceControl control = new(new OblivionApplication());
        StringWriter coldOutput = new();
        StringWriter coldError = new();
        OblivionCli coldCli = new(coldOutput, coldError, control);
        int coldExitCode = await coldCli.InvokeAsync(
            ["function", "run", "passing-function", "-w", root, "--json"]);
        StringWriter warmOutput = new();
        StringWriter warmError = new();
        OblivionCli warmCli = new(warmOutput, warmError, control);
        int warmExitCode = await warmCli.InvokeAsync(
            ["function", "run", "passing-function", "-w", root, "--json"]);
        CliResult nonFunction = await Run("function", "run", "execution-context", "-w", root, "--json");
        CliResult content = await Run("card", "content", "passing-function", "-w", root, "--json");

        Assert.Equal(OblivionCliExitCode.Success, coldExitCode);
        Assert.Equal(OblivionCliExitCode.Success, warmExitCode);
        using JsonDocument json = JsonDocument.Parse(coldOutput.ToString());
        using JsonDocument warmJson = JsonDocument.Parse(warmOutput.ToString());
        Assert.Equal("passing-function", json.RootElement.GetProperty("cardId").GetString());
        Assert.Equal("Passed", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("dotnet-test-trx-v1", json.RootElement.GetProperty("runner").GetString());
        Assert.True(json.RootElement.GetProperty("durationMilliseconds").GetDouble() > 0);
        string firstRealization = json.RootElement.GetProperty("realization").GetString()!;
        Assert.Contains(firstRealization, new[] { "cold", "warm" });
        Assert.Equal(
            firstRealization == "cold",
            json.RootElement.GetProperty("materializationInvoked").GetBoolean());
        Assert.Equal(
            firstRealization == "cold",
            json.RootElement.GetProperty("discoveryInvoked").GetBoolean());
        Assert.True(json.RootElement.GetProperty("executionInvoked").GetBoolean());
        Assert.Equal("warm", warmJson.RootElement.GetProperty("realization").GetString());
        Assert.False(warmJson.RootElement.GetProperty("materializationInvoked").GetBoolean());
        Assert.False(warmJson.RootElement.GetProperty("discoveryInvoked").GetBoolean());
        Assert.True(warmJson.RootElement.GetProperty("executionInvoked").GetBoolean());
        Assert.NotEqual(
            json.RootElement.GetProperty("resultIdentity").GetString(),
            warmJson.RootElement.GetProperty("resultIdentity").GetString());
        Assert.Equal(OblivionCliExitCode.ProductFailure, nonFunction.ExitCode);
        Assert.Contains("OBLIVION-FUNCTION-CARD-REQUIRED", nonFunction.Output, StringComparison.Ordinal);
        Assert.Equal(OblivionCliExitCode.ProductFailure, content.ExitCode);
        Assert.Contains("OBLIVION-CARD-CONTENT-NOT-TEXT", content.Output, StringComparison.Ordinal);
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
            "OBLIVION-UNKNOWN-CARD",
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
        Assert.Contains("OBLIVION-UNKNOWN-CARD", unknownHuman.Error, StringComparison.Ordinal);
        Assert.Equal(OblivionCliExitCode.ProductFailure, unknownJson.ExitCode);
        Assert.Empty(unknownJson.Error);
        Assert.Contains("OBLIVION-UNKNOWN-CARD", unknownJson.Output, StringComparison.Ordinal);
        Assert.Equal(OblivionCliExitCode.ProductFailure, missingContent.ExitCode);
        Assert.Contains(
            "OBLIVION-MISSING-MARKDOWN-BODY-FILE",
            missingContent.Output,
            StringComparison.Ordinal);
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
        CliResult vertical = await Run("command", "run", "layout.vertical-split", "-w", FixtureRoot, "--json");
        CliResult unknown = await Run("command", "run", "view.reset", "-w", FixtureRoot, "--json");

        using JsonDocument listJson = JsonDocument.Parse(list.Output);
        Assert.Equal(12, listJson.RootElement.GetArrayLength());
        Assert.Equal("workspace.reload", listJson.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("active-page", listJson.RootElement[1].GetProperty("scope").GetString());
        Assert.True(listJson.RootElement[2].GetProperty("available").GetBoolean());
        using JsonDocument reloadJson = JsonDocument.Parse(reload.Output);
        Assert.True(reloadJson.RootElement.GetProperty("executed").GetBoolean());
        Assert.Equal("workspace.reload", reloadJson.RootElement.GetProperty("id").GetString());
        using JsonDocument expandJson = JsonDocument.Parse(expand.Output);
        Assert.Equal(2, expandJson.RootElement.GetProperty("affectedCards").GetInt32());
        Assert.Contains("Executed cards.collapse-all", collapse.Output, StringComparison.Ordinal);
        using JsonDocument verticalJson = JsonDocument.Parse(vertical.Output);
        JsonElement verticalSession = verticalJson.RootElement.GetProperty("session");
        Assert.Equal("VerticalSplit", verticalSession.GetProperty("viewportLayout").GetString());
        Assert.Equal("A", verticalSession.GetProperty("focusedSlot").GetString());
        Assert.Equal(2, verticalSession.GetProperty("slots").GetArrayLength());
        Assert.Equal(OblivionCliExitCode.ProductFailure, unknown.ExitCode);
        Assert.Contains("OBLIVION-COMMAND-UNKNOWN", unknown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Durable_session_survives_cli_instances_and_compact_schema_is_machine_readable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "oblivion-session-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sessionPath = Path.Combine(directory, "session.json");
        try
        {
            CliResult vertical = await Run(
                "command", "run", "layout.vertical-split",
                "-w", FixtureRoot,
                "--session-file", sessionPath,
                "--json-compact");
            CliResult expand = await Run(
                "command", "run", "cards.expand-all",
                "-w", FixtureRoot,
                "--session-file", sessionPath,
                "--json-compact");
            CliResult schema = await Run("schema", "--json-compact");

            Assert.Equal(0, vertical.ExitCode);
            Assert.Equal(0, expand.ExitCode);
            Assert.DoesNotContain(Environment.NewLine + "  ", expand.Output, StringComparison.Ordinal);
            using JsonDocument expandedJson = JsonDocument.Parse(expand.Output);
            JsonElement session = expandedJson.RootElement.GetProperty("session");
            Assert.Equal("VerticalSplit", session.GetProperty("viewportLayout").GetString());
            Assert.Equal(2, session.GetProperty("expandedCardIds").GetArrayLength());
            using JsonDocument sessionDocument = JsonDocument.Parse(File.ReadAllText(sessionPath));
            Assert.Equal("oblivion.session.v1", sessionDocument.RootElement.GetProperty("schema").GetString());
            using JsonDocument schemaJson = JsonDocument.Parse(schema.Output);
            Assert.Equal("oblivion.cli.schema.v1", schemaJson.RootElement.GetProperty("schema").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Context_set_persists_fidelity_and_reports_budget_without_auto_selection()
    {
        string directory = Path.Combine(Path.GetTempPath(), "oblivion-context-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sessionPath = Path.Combine(directory, "session.json");
        try
        {
            CliResult budget = await Run(
                "context", "budget", "1",
                "-w", FixtureRoot,
                "--session-file", sessionPath,
                "--json-compact");
            CliResult add = await Run(
                "context", "add", "physical-atom",
                "--fidelity", "full",
                "-w", FixtureRoot,
                "--session-file", sessionPath,
                "--json-compact");
            CliResult show = await Run(
                "context", "show",
                "-w", FixtureRoot,
                "--session-file", sessionPath,
                "--json-compact");

            Assert.Equal(0, budget.ExitCode);
            Assert.Equal(0, add.ExitCode);
            Assert.Equal(add.Output, show.Output);
            using JsonDocument json = JsonDocument.Parse(show.Output);
            Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("full", json.RootElement.GetProperty("items")[0].GetProperty("fidelity").GetString());
            Assert.True(json.RootElement.GetProperty("overBudget").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
        Assert.DoesNotContain("Aurelian.Graphics", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.World", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Runtime", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Native", project, StringComparison.Ordinal);
        Assert.Contains("Aurelian.Rendering.Contracts", project, StringComparison.Ordinal);
        Assert.Contains("Aurelian.Rendering.Raster", project, StringComparison.Ordinal);
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
