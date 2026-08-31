using Oblivion.Product;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class CommandRegistryTests
{
    [Fact]
    public void Registry_has_stable_order_and_known_discoverable_descriptors()
    {
        OblivionCommandRegistry registry = new();

        Assert.Equal(
            ["workspace.reload", "cards.expand-all", "cards.collapse-all"],
            registry.Descriptors.Select(descriptor => descriptor.Id));
        Assert.All(registry.Descriptors, descriptor =>
        {
            Assert.NotEmpty(descriptor.Title);
            Assert.NotEmpty(descriptor.Description);
            Assert.True(descriptor.Available);
        });
        Assert.True(registry.TryResolve("cards.expand-all", out OblivionCommandId commandId));
        Assert.Equal(OblivionCommandId.CardsExpandAll, commandId);
        Assert.False(registry.TryResolve("view.reset", out _));
    }

    [Fact]
    public void Expand_and_collapse_all_change_only_process_local_active_page_state()
    {
        OblivionApplication application = new();
        OblivionWorkspaceSession session = application.OpenWorkspace(FixtureRoot).Session!;
        OblivionCommandRegistry registry = new();

        OblivionCommandExecutionResult expanded = registry.Run(
            application,
            session,
            OblivionCommandId.CardsExpandAll);
        OblivionCommandExecutionResult collapsed = registry.Run(
            application,
            expanded.Session,
            OblivionCommandId.CardsCollapseAll);

        Assert.True(expanded.Succeeded);
        Assert.Equal(2, expanded.AffectedCards);
        Assert.All(expanded.Session.ActivePage.Cards, card =>
            Assert.True(expanded.Session.State.GetCardViewState("notebook", card.Id.Value).IsExpanded));
        Assert.True(collapsed.Succeeded);
        Assert.Equal(2, collapsed.AffectedCards);
        Assert.All(collapsed.Session.ActivePage.Cards, card =>
            Assert.False(collapsed.Session.State.GetCardViewState("notebook", card.Id.Value).IsExpanded));
        Assert.All(session.ActivePage.Cards, card =>
            Assert.False(session.State.GetCardViewState("notebook", card.Id.Value).IsExpanded));
    }

    [Fact]
    public void Workspace_reload_command_reuses_transactional_application_operation()
    {
        OblivionApplication application = new();
        OblivionWorkspaceSession session = application.OpenWorkspace(FixtureRoot).Session!;

        OblivionCommandExecutionResult result = new OblivionCommandRegistry().Run(
            application,
            session,
            OblivionCommandId.WorkspaceReload);

        Assert.True(result.Succeeded);
        Assert.Equal("workspace.reload", result.Command!.Id);
        Assert.Equal("notebook", result.Session.ActivePage.Id.Value);
    }

    private static string FixtureRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "M19iNotebook.oblivion");
}
