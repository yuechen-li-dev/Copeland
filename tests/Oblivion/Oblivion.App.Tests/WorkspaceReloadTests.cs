using Oblivion.Product;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class WorkspaceReloadTests
{
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
}
