using Oblivion.Product;
using Oblivion.Model;

namespace Oblivion.App;

public sealed record OblivionHostOptions(
    string? WorkspacePath = null,
    string? PresentationId = null);

public enum OblivionShellMode
{
    Wide,
    Compact,
}

public sealed record OblivionHostLayout(
    OblivionShellMode ShellMode,
    int ContentVisibleWidth,
    int ViewportHeight);

public sealed record OblivionHostState(
    OblivionSessionState Session,
    OblivionApplicationState Application)
{
    public static OblivionHostState Empty { get; } = new(
        OblivionSessionState.Empty,
        OblivionApplicationState.Empty);

    public OblivionCompactPane CompactPane => Session.InspectorPaneSelected
        ? OblivionCompactPane.Inspector
        : OblivionCompactPane.CardList;

    public OblivionEffectState EffectState => Application.EffectState;

    public double GetScrollOffset(string pageId)
    {
        return Session.GetMainScrollOffset(pageId);
    }

    public double GetInspectorScrollOffset(string pageId)
    {
        return Session.GetInspectorScrollOffset(pageId);
    }

    public double GetRawMarkdownSourceScrollOffset(string cardId)
    {
        return Session.GetRawSourceScrollOffset(cardId);
    }

    public string? GetSelectedCardId(string pageId, IReadOnlyList<OblivionCard> cards)
    {
        return Session.GetSelectedCardId(pageId, cards);
    }

    public OblivionCardViewState GetCardViewState(string pageId, string cardId)
    {
        return Session.GetCardViewState(pageId, cardId);
    }
}
