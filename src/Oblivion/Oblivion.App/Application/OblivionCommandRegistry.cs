using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public enum OblivionCommandId
{
    WorkspaceReload,
    CardsExpandAll,
    CardsCollapseAll,
}

public enum OblivionCommandScope
{
    Workspace,
    ActivePage,
}

public sealed record OblivionCommandDescriptor(
    OblivionCommandId CommandId,
    string Id,
    string Title,
    string Description,
    OblivionCommandScope Scope,
    bool Available);

public sealed record OblivionCommandExecutionResult(
    OblivionCommandDescriptor? Command,
    OblivionWorkspaceSession Session,
    bool Executed,
    int AffectedCards,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Executed &&
        Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed class OblivionCommandRegistry
{
    private static readonly IReadOnlyList<OblivionCommandDescriptor> Registered =
    [
        new(
            OblivionCommandId.WorkspaceReload,
            "workspace.reload",
            "Reload workspace",
            "Transactionally reload the process-local workspace session.",
            OblivionCommandScope.Workspace,
            Available: true),
        new(
            OblivionCommandId.CardsExpandAll,
            "cards.expand-all",
            "Expand all cards",
            "Expand every Card on the active Page in process-local session state.",
            OblivionCommandScope.ActivePage,
            Available: true),
        new(
            OblivionCommandId.CardsCollapseAll,
            "cards.collapse-all",
            "Collapse all cards",
            "Collapse every Card on the active Page in process-local session state.",
            OblivionCommandScope.ActivePage,
            Available: true),
    ];

    public IReadOnlyList<OblivionCommandDescriptor> Descriptors => Registered;

    public bool TryResolve(string externalId, out OblivionCommandId commandId)
    {
        OblivionCommandDescriptor? descriptor = Registered.FirstOrDefault(
            candidate => string.Equals(candidate.Id, externalId, StringComparison.Ordinal));
        commandId = descriptor?.CommandId ?? default;
        return descriptor is not null;
    }

    public OblivionCommandExecutionResult Run(
        OblivionApplication application,
        OblivionWorkspaceSession session,
        OblivionCommandId commandId)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(session);

        OblivionCommandDescriptor descriptor = Registered.Single(
            candidate => candidate.CommandId == commandId);
        return commandId switch
        {
            OblivionCommandId.WorkspaceReload => Reload(application, session, descriptor),
            OblivionCommandId.CardsExpandAll => SetExpansion(session, descriptor, expanded: true),
            OblivionCommandId.CardsCollapseAll => SetExpansion(session, descriptor, expanded: false),
            _ => throw new ArgumentOutOfRangeException(nameof(commandId)),
        };
    }

    private static OblivionCommandExecutionResult Reload(
        OblivionApplication application,
        OblivionWorkspaceSession session,
        OblivionCommandDescriptor descriptor)
    {
        OblivionWorkspaceSessionReloadResult reload = application.ReloadWorkspace(session);
        return new(
            descriptor,
            reload.Session,
            reload.Reloaded,
            AffectedCards: 0,
            reload.Diagnostics);
    }

    private static OblivionCommandExecutionResult SetExpansion(
        OblivionWorkspaceSession session,
        OblivionCommandDescriptor descriptor,
        bool expanded)
    {
        string pageId = session.ActivePage.Id.Value;
        OblivionSessionState state = session.State;
        int affected = 0;
        foreach (OblivionCard card in session.ActivePage.Cards)
        {
            OblivionCardViewState current = state.GetCardViewState(pageId, card.Id.Value);
            if (current.IsExpanded != expanded)
            {
                state = state.WithCardViewState(pageId, card.Id.Value, current with { IsExpanded = expanded });
                affected++;
            }
        }

        return new(
            descriptor,
            session with { State = state },
            Executed: true,
            affected,
            []);
    }
}
