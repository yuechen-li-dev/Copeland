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
            [
                "workspace.reload",
                "cards.expand-all",
                "cards.collapse-all",
                "layout.single",
                "layout.vertical-split",
                "layout.horizontal-split",
                "layout.focus-next",
                "diagram.fit",
                "diagram.zoom-in",
                "diagram.zoom-out",
                "diagram.reset-view",
                "function.run",
            ],
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
    public void Layout_commands_transition_without_mutating_page_or_card_semantics()
    {
        OblivionApplication application = new();
        OblivionWorkspaceSession original = application.OpenWorkspace(FixtureRoot).Session!;
        OblivionCommandRegistry registry = new();
        OblivionCommandExecutionResult vertical = registry.Run(
            application,
            original,
            OblivionCommandId.LayoutVerticalSplit);
        OblivionCommandExecutionResult horizontal = registry.Run(
            application,
            vertical.Session,
            OblivionCommandId.LayoutHorizontalSplit);
        OblivionCommandExecutionResult single = registry.Run(
            application,
            horizontal.Session,
            OblivionCommandId.LayoutSingle);

        Assert.Equal(
            OblivionViewportLayoutMode.VerticalSplit,
            vertical.Session.State.GetViewportState("notebook").LayoutMode);
        Assert.Equal(
            OblivionViewportLayoutMode.HorizontalSplit,
            horizontal.Session.State.GetViewportState("notebook").LayoutMode);
        Assert.Equal(
            OblivionViewportLayoutMode.Single,
            single.Session.State.GetViewportState("notebook").LayoutMode);
        Assert.Equal(original.ActivePage, single.Session.ActivePage);
        Assert.Equal(original.Workspace, single.Session.Workspace);
    }

    [Fact]
    public void Diagram_commands_route_to_the_focused_slot_card()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "M20aDenseDiagram.oblivion");
        OblivionApplication application = new();
        OblivionWorkspaceSession session = application.OpenWorkspace(root).Session!;
        OblivionCommandRegistry registry = new();

        OblivionCommandExecutionResult zoomed = registry.Run(
            application,
            session,
            OblivionCommandId.DiagramZoomIn);
        OblivionCommandExecutionResult fit = registry.Run(
            application,
            zoomed.Session,
            OblivionCommandId.DiagramResetView);

        Assert.True(zoomed.Succeeded);
        Assert.Equal(
            OblivionDiagramFitMode.Manual,
            zoomed.Session.State.GetDiagramViewportState("diagram-card-realization").FitMode);
        Assert.True(fit.Succeeded);
        Assert.Equal(
            OblivionDiagramFitMode.Fit,
            fit.Session.State.GetDiagramViewportState("diagram-card-realization").FitMode);
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
