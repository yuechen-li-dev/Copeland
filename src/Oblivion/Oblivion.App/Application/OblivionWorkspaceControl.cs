using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionControlDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? WorkspaceId,
    string? PageId,
    string? CardId,
    string? Source,
    int? Line,
    int? Column);

public sealed record OblivionWorkspaceInfo(
    string WorkspaceId,
    string Title,
    int FormatVersion,
    string? DefaultPageId,
    int PageCount,
    int CardCount,
    string WorkspaceRoot);

public sealed record OblivionWorkspaceValidation(
    bool Valid,
    int ErrorCount,
    int WarningCount,
    int PageCount,
    int CardCount,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed record OblivionPageInfo(
    string Id,
    string Title,
    int CardCount);

public sealed record OblivionCardInfo(
    string Id,
    string PageId,
    string Title,
    string Kind,
    string Status,
    string? ContentSource,
    string ContentSummary);

public sealed record OblivionCardDetail(
    string Id,
    string PageId,
    string Title,
    string? Subtitle,
    string Kind,
    string Status,
    IReadOnlyList<string> Tags,
    string? MarkdownSource,
    string ProvenanceKind,
    string? ProvenanceSource,
    IReadOnlyList<string> Actions,
    string ContentPreview,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed record OblivionReloadSessionInfo(
    string ActivePageId,
    string? SelectedCardId,
    IReadOnlyList<string> ExpandedCardIds);

public sealed record OblivionWorkspaceReload(
    bool Reloaded,
    OblivionWorkspaceInfo Workspace,
    OblivionReloadSessionInfo Session,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed record OblivionControlResult<T>(
    T? Value,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Value is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != "error");
}

public sealed class OblivionWorkspaceControl
{
    private const int FormatVersion = 1;
    private const int PreviewLimit = 400;
    private readonly OblivionApplication _application;

    public OblivionWorkspaceControl(OblivionApplication? application = null)
    {
        _application = application ?? new OblivionApplication();
    }

    public OblivionControlResult<OblivionWorkspaceInfo> Show(string workspaceRoot)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspaceInfo info = CreateWorkspaceInfo(open.Session);
        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            open.Diagnostics,
            open.Session.Workspace);
        return new(info, diagnostics);
    }

    public OblivionWorkspaceValidation Validate(string workspaceRoot)
    {
        OblivionWorkspaceLoadResult load = OblivionApplication.LoadVault(workspaceRoot);
        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            load.Diagnostics,
            load.Workspace);
        return new OblivionWorkspaceValidation(
            load.Succeeded,
            diagnostics.Count(diagnostic => diagnostic.Severity == "error"),
            diagnostics.Count(diagnostic => diagnostic.Severity == "warning"),
            load.Workspace?.Pages.Count ?? 0,
            load.Workspace?.Pages.Sum(page => page.Cards.Count) ?? 0,
            diagnostics);
    }

    public OblivionControlResult<IReadOnlyList<OblivionPageInfo>> ListPages(string workspaceRoot)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionPageInfo[] pages = open.Session.Workspace.Pages
            .Select(page => new OblivionPageInfo(page.Id.Value, page.Title, page.Cards.Count))
            .ToArray();
        return new(pages, ConvertDiagnostics(open.Diagnostics, open.Session.Workspace));
    }

    public OblivionControlResult<IReadOnlyList<OblivionCardInfo>> ListCards(
        string workspaceRoot,
        string? pageId = null)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspace workspace = open.Session.Workspace;
        IReadOnlyList<OblivionWorkspacePage> pages = workspace.Pages;
        if (pageId is not null)
        {
            OblivionWorkspacePage? page = pages.FirstOrDefault(
                candidate => candidate.Id.Value == pageId);
            if (page is null)
            {
                OblivionControlDiagnostic diagnostic = UnknownPage(workspace, pageId);
                return new(null, [diagnostic]);
            }

            pages = [page];
        }

        OblivionCardInfo[] cards = pages
            .SelectMany(page => page.Cards.Select(card => new OblivionCardInfo(
                card.Id.Value,
                page.Id.Value,
                card.Title,
                OblivionWorkspaceValidator.GetCardKindValue(card.Kind),
                OblivionWorkspaceValidator.GetCardStatusValue(card.Status),
                card.Body.SourceReference,
                Summarize(card.Body.RawText))))
            .ToArray();
        return new(cards, ConvertDiagnostics(open.Diagnostics, workspace));
    }

    public OblivionControlResult<OblivionCardDetail> ShowCard(
        string workspaceRoot,
        string cardId)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspace workspace = open.Session.Workspace;
        OblivionWorkspacePage? page = workspace.Pages.FirstOrDefault(
            candidate => candidate.Cards.Any(card => card.Id.Value == cardId));
        OblivionCard? card = page?.Cards.FirstOrDefault(candidate => candidate.Id.Value == cardId);
        if (page is null || card is null)
        {
            OblivionControlDiagnostic diagnostic = new(
                "unknown-card",
                "error",
                $"Card '{cardId}' was not found in workspace '{workspace.Id.Value}'.",
                workspace.Id.Value,
                null,
                cardId,
                open.Session.Location.ManifestPath,
                null,
                null);
            return new(null, [diagnostic]);
        }

        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            open.Diagnostics,
            workspace);
        OblivionCardDetail detail = new(
            card.Id.Value,
            page.Id.Value,
            card.Title,
            card.Subtitle,
            OblivionWorkspaceValidator.GetCardKindValue(card.Kind),
            OblivionWorkspaceValidator.GetCardStatusValue(card.Status),
            card.Tags,
            card.Body.SourceReference,
            card.Provenance.SourceKind.ToString(),
            card.Provenance.SourceReference,
            card.Actions.Where(action => action.Enabled).Select(action => action.Id).ToArray(),
            Preview(card.Body.RawText),
            diagnostics);
        return new(detail, diagnostics);
    }

    public OblivionControlResult<OblivionWorkspaceReload> Reload(string workspaceRoot)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspaceSessionReloadResult reload = _application.ReloadWorkspace(open.Session);
        OblivionWorkspaceSession session = reload.Session;
        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            reload.Diagnostics,
            session.Workspace);
        OblivionWorkspaceReload value = new(
            reload.Reloaded,
            CreateWorkspaceInfo(session),
            CreateSessionInfo(session),
            diagnostics);
        return new(value, diagnostics);
    }

    private static OblivionWorkspaceInfo CreateWorkspaceInfo(OblivionWorkspaceSession session)
    {
        return new OblivionWorkspaceInfo(
            session.Workspace.Id.Value,
            session.Workspace.Title,
            FormatVersion,
            session.Workspace.DefaultPageId?.Value,
            session.Workspace.Pages.Count,
            session.Workspace.Pages.Sum(page => page.Cards.Count),
            session.Location.RootDirectory);
    }

    private static OblivionReloadSessionInfo CreateSessionInfo(OblivionWorkspaceSession session)
    {
        string pageId = session.ActivePage.Id.Value;
        string? selectedCardId = session.State.GetSelectedCardId(pageId, session.ActivePage.Cards);
        string[] expandedCardIds = session.ActivePage.Cards
            .Where(card => session.State.GetCardViewState(pageId, card.Id.Value).IsExpanded)
            .Select(card => card.Id.Value)
            .ToArray();
        return new OblivionReloadSessionInfo(pageId, selectedCardId, expandedCardIds);
    }

    private static IReadOnlyList<OblivionControlDiagnostic> ConvertDiagnostics(
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics,
        OblivionWorkspace? workspace)
    {
        return diagnostics.Select(diagnostic => new OblivionControlDiagnostic(
            diagnostic.Code,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.Message,
            workspace?.Id.Value,
            null,
            null,
            diagnostic.SourcePath,
            diagnostic.Line,
            diagnostic.Column)).ToArray();
    }

    private static OblivionControlDiagnostic UnknownPage(
        OblivionWorkspace workspace,
        string pageId)
    {
        return new OblivionControlDiagnostic(
            "unknown-page",
            "error",
            $"Page '{pageId}' was not found in workspace '{workspace.Id.Value}'.",
            workspace.Id.Value,
            pageId,
            null,
            null,
            null,
            null);
    }

    private static string Summarize(string text)
    {
        string normalized = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 80 ? normalized : normalized[..77] + "...";
    }

    private static string Preview(string text)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.Length <= PreviewLimit
            ? normalized
            : normalized[..PreviewLimit] + "\n...";
    }
}
