using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionApplicationState(
    OblivionEffectState EffectState)
{
    public static OblivionApplicationState Empty { get; } = new(OblivionEffectState.Empty);

    public OblivionApplicationState Apply(
        OblivionEffectRequest request,
        OblivionEffectResult result)
    {
        return this with
        {
            EffectState = EffectState.WithOutcome(request, result),
        };
    }
}

public sealed record OblivionActionOutcome(
    OblivionEffectRequest Request,
    OblivionEffectResult Result,
    OblivionApplicationState State);

public sealed class OblivionApplication
{
    private readonly OblivionCardHandlerRegistry _handlers;
    private readonly OblivionCardEffectRouter _effects;

    public OblivionApplication(
        OblivionCardHandlerRegistry? handlers = null,
        OblivionCardEffectRouter? effects = null)
    {
        _handlers = handlers ?? OblivionCardHandlerRegistry.CreateDefault();
        _effects = effects ?? new OblivionCardEffectRouter();
    }

    public OblivionActionOutcome? Invoke(
        OblivionCard card,
        string pageId,
        OblivionProductActionId actionId,
        OblivionApplicationState? state = null)
    {
        OblivionApplicationState current = state ?? OblivionApplicationState.Empty;
        OblivionEffectRequest? request = _handlers.CreateEffectRequest(
            card,
            pageId,
            actionId.Value,
            card.WorkspaceId?.Value,
            current.EffectState);
        if (request is null)
        {
            return null;
        }

        OblivionEffectResult result = _effects.Route(request);
        return new OblivionActionOutcome(request, result, current.Apply(request, result));
    }

    public OblivionActionOutcome? Invoke(
        OblivionCard card,
        string pageId,
        string actionId,
        OblivionApplicationState? state = null)
    {
        return Invoke(card, pageId, new OblivionProductActionId(actionId), state);
    }
}

public static class OblivionWorkspaceApplication
{
    public static OblivionWorkspaceLoadResult Load(
        string manifestPath,
        OblivionWorkspaceLoadOptions? options = null,
        bool useCache = true)
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(manifestPath, options, useCache);
        if (result.Workspace is null)
        {
            return result;
        }

        List<OblivionWorkspaceDiagnostic> diagnostics = result.Diagnostics.ToList();
        List<OblivionWorkspaceSection> sections = [];
        foreach (OblivionWorkspaceSection section in result.Workspace.Sections)
        {
            List<OblivionWorkspacePage> pages = [];
            foreach (OblivionWorkspacePage page in section.Pages)
            {
                if (!OblivionDocsDogfoodCatalog.IsDocsPage(section.Id, page.Id.Value))
                {
                    pages.Add(page);
                    continue;
                }

                DocsDogfoodPageData docs = OblivionDocsDogfoodCatalog.CreatePageData(manifestPath);
                diagnostics.AddRange(docs.Documents.SelectMany(document => document.Diagnostics));
                pages.Add(page with { Cards = [.. page.Cards, .. docs.Cards] });
            }

            sections.Add(section with { Pages = pages });
        }

        return result with
        {
            Workspace = result.Workspace with { Sections = sections },
            Diagnostics = OblivionWorkspaceValidator.OrderDiagnostics(diagnostics),
        };
    }
}
