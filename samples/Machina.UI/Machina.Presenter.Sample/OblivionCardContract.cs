using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public sealed record OblivionCardIdentity(
    OblivionCardId Id,
    OblivionCardKind Kind,
    string? PageId,
    string? WorkspaceId,
    string? SourcePath);

public enum OblivionCardDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record OblivionCardDiagnostic(
    string Code,
    OblivionCardDiagnosticSeverity Severity,
    string Message,
    string? SourcePath,
    int? Line = null,
    int? Column = null);

public sealed record OblivionCardArtifactRef(
    string Id,
    string Label,
    string Kind,
    string? Path,
    bool Generated);

public enum OblivionCardActionAvailability
{
    Enabled,
    Disabled,
    Deferred,
}

public enum OblivionCardEffectKind
{
    None,
    RefreshMarkdown,
    OpenSource,
    CopySourcePath,
    OpenArtifact,
    RunCodeFact,
    RunCodeTheory,
    ExportCard,
    RenderPreview,
    Custom,
}

public sealed record OblivionCardActionDescriptor(
    string Id,
    string Label,
    bool Enabled,
    string Intent,
    bool RequiresEffect,
    OblivionCardActionAvailability Availability,
    OblivionCardEffectKind EffectKind);

public sealed record OblivionCardActionInvocation(
    OblivionCardId CardId,
    string ActionId,
    string PageId,
    string? SourcePath);

public sealed record OblivionCardEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionCardEffectKind Kind,
    string Intent,
    IReadOnlyDictionary<string, string> Properties);

public enum OblivionCardEffectStatus
{
    Deferred,
    Rejected,
    Completed,
}

public sealed record OblivionCardEffectResult(
    string RequestId,
    OblivionCardId CardId,
    OblivionCardEffectKind Kind,
    OblivionCardEffectStatus Status,
    string Message,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    IReadOnlyList<OblivionCardArtifactRef> Artifacts);

public sealed record OblivionCardEffectState(
    IReadOnlyDictionary<string, OblivionCardEffectRequest> LastRequestByCardId,
    IReadOnlyDictionary<string, OblivionCardEffectResult> LastResultByCardId)
{
    public static OblivionCardEffectState Empty { get; } = new(
        new Dictionary<string, OblivionCardEffectRequest>(StringComparer.Ordinal),
        new Dictionary<string, OblivionCardEffectResult>(StringComparer.Ordinal));

    public OblivionCardEffectRequest? GetLastRequest(OblivionCardId cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        return LastRequestByCardId.TryGetValue(cardId.Value, out OblivionCardEffectRequest? request)
            ? request
            : null;
    }

    public OblivionCardEffectResult? GetLastResult(OblivionCardId cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        return LastResultByCardId.TryGetValue(cardId.Value, out OblivionCardEffectResult? result)
            ? result
            : null;
    }

    public OblivionCardEffectState WithOutcome(
        OblivionCardEffectRequest request,
        OblivionCardEffectResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        Dictionary<string, OblivionCardEffectRequest> requests = new(LastRequestByCardId, StringComparer.Ordinal)
        {
            [request.CardId.Value] = request,
        };
        Dictionary<string, OblivionCardEffectResult> results = new(LastResultByCardId, StringComparer.Ordinal)
        {
            [result.CardId.Value] = result,
        };

        return new OblivionCardEffectState(requests, results);
    }
}

public sealed record OblivionCardLocalState(
    OblivionCardId CardId,
    bool IsExpanded,
    double BodyScrollOffset,
    string? SelectedArtifactId,
    IReadOnlyDictionary<string, string> Properties)
{
    public static OblivionCardLocalState CreateDefault(OblivionCardId cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        return new OblivionCardLocalState(
            cardId,
            IsExpanded: false,
            BodyScrollOffset: 0,
            SelectedArtifactId: null,
            Properties: new Dictionary<string, string>(StringComparer.Ordinal));
    }
}

public sealed record OblivionCardViewState(
    bool IsExpanded,
    double BodyScrollOffset)
{
    public static OblivionCardViewState Collapsed { get; } = new(
        IsExpanded: false,
        BodyScrollOffset: 0);
}

public static class OblivionCardLocalStateCatalog
{
    public static OblivionCardLocalState CreateDefault(OblivionCardId cardId)
    {
        return OblivionCardLocalState.CreateDefault(cardId);
    }

    public static IReadOnlyDictionary<string, OblivionCardLocalState> CreateDefaults(
        IEnumerable<OblivionCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        Dictionary<string, OblivionCardLocalState> states = new(StringComparer.Ordinal);

        foreach (OblivionCard card in cards)
        {
            states[card.Id.Value] = CreateDefault(card.Id);
        }

        return states;
    }
}

public sealed record OblivionCardContext(
    string? PageId,
    string? WorkspaceId,
    string? SourcePath,
    OblivionCardEffectRequest? LastEffectRequest,
    OblivionCardEffectResult? LastEffectResult,
    OblivionCardLocalState? LocalStateOverride = null);

public sealed record OblivionCardViewContext(
    OblivionCardLocalState LocalState);

public sealed record OblivionCardInspectorContext(
    OblivionCardLocalState LocalState);

public sealed record OblivionCardActionContext(
    OblivionCardLocalState LocalState);

public sealed record OblivionCardEffectContext(
    string PageId,
    string? WorkspaceId,
    string? SourcePath,
    OblivionCardLocalState LocalState);

public sealed record OblivionCardRuntimeModel(
    OblivionCardIdentity Identity,
    OblivionCardStatus Status,
    OblivionCardLocalState LocalState,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    IReadOnlyList<OblivionCardArtifactRef> Artifacts,
    IReadOnlyList<OblivionCardActionDescriptor> Actions,
    OblivionCardEffectRequest? LastEffectRequest,
    OblivionCardEffectResult? LastEffectResult,
    OblivionCard SourceCard,
    object? KindModel);

public abstract record OblivionCompactBodyContent;

public sealed record OblivionCompactPlainBodyContent(
    IReadOnlyList<string> Lines) : OblivionCompactBodyContent;

public sealed record OblivionCompactMarkdownBodyContent(
    OblivionCardBody Body) : OblivionCompactBodyContent;

public sealed record OblivionCompactCardView(
    string CardId,
    string Title,
    string? Subtitle,
    string? SourceLabel,
    string? SummaryLine,
    IReadOnlyList<string> MetaBadges,
    IReadOnlyList<string> Tags,
    OblivionCompactBodyContent Body,
    IReadOnlyList<string> ActionBadges,
    IReadOnlyList<string> ArtifactBadges,
    bool IsExpanded,
    double BodyScrollOffset,
    double PreferredHeight,
    double ExpandedPreferredHeight)
{
    public OblivionCompactCardView(
        string cardId,
        string title,
        string? subtitle,
        IReadOnlyList<string> metaBadges,
        IReadOnlyList<string> tags,
        OblivionCompactBodyContent body,
        IReadOnlyList<string> actionBadges,
        IReadOnlyList<string> artifactBadges,
        double preferredHeight)
        : this(
            cardId,
            title,
            subtitle,
            null,
            null,
            metaBadges,
            tags,
            body,
            actionBadges,
            artifactBadges,
            false,
            0,
            preferredHeight,
            preferredHeight)
    {
    }
}

public abstract record OblivionInspectorBodyContent;

public sealed record OblivionInspectorTextBodyContent(
    IReadOnlyList<string> Lines) : OblivionInspectorBodyContent;

public sealed record OblivionInspectorRawMarkdownSourceBodyContent(
    OblivionCardBody Body) : OblivionInspectorBodyContent;

public sealed record OblivionInspectorSectionView(
    string Id,
    string Title,
    IReadOnlyList<string> Badges,
    OblivionInspectorBodyContent Body,
    double Height,
    bool ClipContent = true);

public sealed record OblivionInspectorCardView(
    string CardId,
    IReadOnlyList<OblivionInspectorSectionView> Sections,
    double PreferredHeight);

public sealed record OblivionBuiltCard(
    OblivionCard SourceCard,
    OblivionCardRuntimeModel RuntimeModel,
    OblivionCompactCardView CompactView,
    OblivionInspectorCardView InspectorView);

public interface IOblivionCardHandler
{
    OblivionCardKind Kind { get; }

    OblivionCardRuntimeModel BuildModel(
        OblivionCard card,
        OblivionCardContext context);

    OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context);

    OblivionInspectorCardView BuildInspectorView(
        OblivionCardRuntimeModel model,
        OblivionCardInspectorContext context);

    IReadOnlyList<OblivionCardActionDescriptor> GetActions(
        OblivionCardRuntimeModel model,
        OblivionCardActionContext context);

    OblivionCardEffectRequest? CreateEffectRequest(
        OblivionCardRuntimeModel model,
        OblivionCardActionInvocation invocation,
        OblivionCardEffectContext context);
}

public static class OblivionCardLabels
{
    public static string KindLabel(OblivionCardKind kind)
    {
        return kind switch
        {
            OblivionCardKind.Note => "Note",
            OblivionCardKind.Status => "Status",
            OblivionCardKind.UiPreview => "UI Preview",
            OblivionCardKind.Artifact => "Artifact",
            OblivionCardKind.CodeFact => "Code Fact",
            OblivionCardKind.CodeTheory => "Code Theory",
            _ => kind.ToString(),
        };
    }

    public static string StatusLabel(OblivionCardStatus status)
    {
        return status switch
        {
            OblivionCardStatus.Idle => "Idle",
            OblivionCardStatus.Passing => "Passing",
            OblivionCardStatus.Failing => "Failing",
            OblivionCardStatus.Warning => "Warning",
            OblivionCardStatus.Deferred => "Deferred",
            OblivionCardStatus.Placeholder => "Placeholder",
            _ => status.ToString(),
        };
    }

    public static string BodyFormatLabel(OblivionCardBodyFormat format)
    {
        return format switch
        {
            OblivionCardBodyFormat.Plain => "Plain",
            OblivionCardBodyFormat.CopelandMarkdown => "Copeland Markdown",
            _ => format.ToString(),
        };
    }
}
