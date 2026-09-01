using Oblivion.Product;
using Copeland.TS.Tson;
using Oblivion.Model;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class WorkspaceReloadTests
{
    [Fact]
    public void Table_source_reload_updates_values_and_invalid_edit_preserves_previous_session()
    {
        using TemporaryTableVault vault = TemporaryTableVault.CopyFixture();
        OblivionApplication application = new();
        OblivionWorkspaceSession current = application.OpenWorkspace(vault.Root).Session!;
        OblivionCard beforeCard = current.ActivePage.Cards.Single(card =>
            card.Id.Value == "validation-evidence");
        TsonTable before = new OblivionTableCardRealizer().Realize(beforeCard, vault.Root).Table!;
        Assert.Equal("obj-ts-load", Assert.IsType<Copeland.TS.Tson.TsonString>(before.Columns[1].Cells[0]).Value);

        vault.ReplaceInSource("obj-ts-load", "obj-ts-load-reloaded");
        OblivionWorkspaceSessionReloadResult reloaded = application.ReloadWorkspace(current);

        Assert.True(reloaded.Succeeded, string.Join(Environment.NewLine, reloaded.Diagnostics));
        OblivionCard afterCard = reloaded.Session.ActivePage.Cards.Single(card =>
            card.Id.Value == "validation-evidence");
        TsonTable after = new OblivionTableCardRealizer().Realize(afterCard, vault.Root).Table!;
        Assert.Equal("obj-ts-load-reloaded", Assert.IsType<Copeland.TS.Tson.TsonString>(after.Columns[1].Cells[0]).Value);
        Assert.Equal(beforeCard.Id, afterCard.Id);

        vault.ReplaceInSource("const $value = ValidationEvidence;", "const $value = Missing;");
        OblivionWorkspaceSessionReloadResult rejected = application.ReloadWorkspace(reloaded.Session);

        Assert.False(rejected.Succeeded);
        Assert.Same(reloaded.Session, rejected.Session);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Severity == Oblivion.Model.OblivionDiagnosticSeverity.Error);
    }

    [Fact]
    public void Invalid_reload_preserves_workspace_selection_and_expansion_then_repair_swaps_atomically()
    {
        using TemporaryStructuredVault vault = TemporaryStructuredVault.CopyFixture();
        OblivionApplication application = new();
        OblivionWorkspaceSession current = Assert.IsType<OblivionWorkspaceSession>(
            application.OpenWorkspace(vault.Root).Session);
        string pageId = current.ActivePage.Id.Value;
        OblivionSessionState selected = current.State
            .WithSelectedCard(pageId, "notebook-stack")
            .ToggleCardExpansion(pageId, "notebook-stack");
        current = current with { State = selected };

        vault.ReplaceInCard(
            "notebook-stack",
            "card_kind = \"note\"",
            "card_kind = \"invalid-kind\"");
        OblivionWorkspaceSessionReloadResult rejected = application.ReloadWorkspace(current);

        Assert.False(rejected.Succeeded);
        Assert.Same(current, rejected.Session);
        Assert.Equal("notebook-stack", rejected.Session.State.GetSelectedCardId(
            pageId,
            rejected.Session.ActivePage.Cards));
        Assert.True(rejected.Session.State.GetCardViewState(pageId, "notebook-stack").IsExpanded);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "unknown-card-kind");

        vault.ReplaceInCard(
            "notebook-stack",
            "card_kind = \"invalid-kind\"",
            "card_kind = \"note\"");
        vault.ReplaceInCard(
            "notebook-stack",
            "From one card to a notebook stack",
            "A transactionally reloaded notebook stack");
        OblivionWorkspaceSessionReloadResult repaired = application.ReloadWorkspace(rejected.Session);

        Assert.True(repaired.Succeeded, string.Join(Environment.NewLine, repaired.Diagnostics));
        Assert.Equal(
            "A transactionally reloaded notebook stack",
            repaired.Session.ActivePage.Cards[1].Title);
        Assert.Equal("notebook-stack", repaired.Session.State.GetSelectedCardId(
            pageId,
            repaired.Session.ActivePage.Cards));
        Assert.True(repaired.Session.State.GetCardViewState(pageId, "notebook-stack").IsExpanded);
    }

    [Fact]
    public void Successful_reload_drops_stale_card_state_and_selects_first_remaining_card()
    {
        using TemporaryStructuredVault vault = TemporaryStructuredVault.CopyFixture();
        OblivionApplication application = new();
        OblivionWorkspaceSession current = application.OpenWorkspace(vault.Root).Session!;
        string pageId = current.ActivePage.Id.Value;
        current = current with
        {
            State = current.State
                .WithSelectedCard(pageId, "notebook-stack")
                .ToggleCardExpansion(pageId, "notebook-stack"),
        };
        vault.ReplaceInPage(
            "[\"physical-atom\", \"notebook-stack\"]",
            "[\"physical-atom\"]");

        OblivionWorkspaceSessionReloadResult reload = application.ReloadWorkspace(current);

        Assert.True(reload.Succeeded, string.Join(Environment.NewLine, reload.Diagnostics));
        Assert.Equal("physical-atom", reload.Session.State.GetSelectedCardId(
            pageId,
            reload.Session.ActivePage.Cards));
        Assert.Equal(
            OblivionCardViewState.Collapsed,
            reload.Session.State.GetCardViewState(pageId, "notebook-stack"));
    }

    [Fact]
    public void Stack_mutations_preserve_push_selection_and_select_new_top_after_selected_pop()
    {
        using TemporaryStructuredVault vault = TemporaryStructuredVault.CopyFixture();
        string source = Path.Combine(Path.GetTempPath(), $"m19k-session-{Guid.NewGuid():N}.md");
        File.WriteAllText(source, "# Session mutation note\n");
        try
        {
            OblivionApplication application = new();
            OblivionWorkspaceSession current = application.OpenWorkspace(vault.Root).Session!;
            string pageId = current.ActivePage.Id.Value;
            current = current with
            {
                State = current.State.WithSelectedCard(pageId, "physical-atom"),
            };

            OblivionStackOperationResult push = application.PushMarkdownCard(
                current,
                new OblivionPushMarkdownCardRequest(source));

            Assert.True(push.Succeeded, string.Join(Environment.NewLine, push.Diagnostics));
            Assert.Equal("physical-atom", push.Session.State.GetSelectedCardId(
                pageId,
                push.Session.ActivePage.Cards));
            Assert.Equal("m19k-session-" + Path.GetFileNameWithoutExtension(source).Split('-')[2], push.Mutation!.CardId);

            string pushedCardId = push.Mutation.CardId;
            OblivionWorkspaceSession selectedPushed = push.Session with
            {
                State = push.Session.State
                    .WithSelectedCard(pageId, pushedCardId)
                    .ToggleCardExpansion(pageId, pushedCardId),
            };
            OblivionStackOperationResult pop = application.PopCard(selectedPushed);

            Assert.True(pop.Succeeded, string.Join(Environment.NewLine, pop.Diagnostics));
            Assert.Equal("notebook-stack", pop.Session.State.GetSelectedCardId(
                pageId,
                pop.Session.ActivePage.Cards));
            Assert.Equal(
                OblivionCardViewState.Collapsed,
                pop.Session.State.GetCardViewState(pageId, pushedCardId));
        }
        finally
        {
            File.Delete(source);
        }
    }

    private sealed class TemporaryStructuredVault : IDisposable
    {
        private TemporaryStructuredVault(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryStructuredVault CopyFixture()
        {
            string fixture = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "M19iNotebook.oblivion");
            string root = Path.Combine(
                Path.GetTempPath(),
                "oblivion-m19j-app-tests",
                Guid.NewGuid().ToString("N"));
            foreach (string sourcePath in Directory.GetFiles(fixture, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(fixture, sourcePath);
                string destinationPath = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            return new TemporaryStructuredVault(root);
        }

        public void ReplaceInCard(string cardId, string oldValue, string newValue)
        {
            Replace(Path.Combine(Root, "cards", cardId + ".toml"), oldValue, newValue);
        }

        public void ReplaceInPage(string oldValue, string newValue)
        {
            Replace(Path.Combine(Root, "pages", "notebook.toml"), oldValue, newValue);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }

        private static void Replace(string path, string oldValue, string newValue)
        {
            string source = File.ReadAllText(path);
            Assert.Contains(oldValue, source, StringComparison.Ordinal);
            File.WriteAllText(path, source.Replace(oldValue, newValue, StringComparison.Ordinal));
        }
    }

    private sealed class TemporaryTableVault : IDisposable
    {
        private TemporaryTableVault(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryTableVault CopyFixture()
        {
            string fixture = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "M20eTsonTables.oblivion");
            string root = Path.Combine(
                Path.GetTempPath(),
                "oblivion-m20e-reload-tests",
                Guid.NewGuid().ToString("N"));
            foreach (string sourcePath in Directory.GetFiles(fixture, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(fixture, sourcePath);
                string destinationPath = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            return new TemporaryTableVault(root);
        }

        public void ReplaceInSource(string oldValue, string newValue)
        {
            string path = Path.Combine(Root, "content", "validation-evidence.obj.ts");
            string source = File.ReadAllText(path);
            Assert.Contains(oldValue, source, StringComparison.Ordinal);
            File.WriteAllText(path, source.Replace(oldValue, newValue, StringComparison.Ordinal));
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
