using Machina.Standard.Theme;

namespace Oblivion.Product;

public sealed record OblivionCardIdentity(
    OblivionCardId Id,
    OblivionCardKind Kind,
    string? PageId,
    string? WorkspaceId,
    string? SourcePath);

public sealed record OblivionCardDiagnostic(
    string Code,
    OblivionDiagnosticSeverity Severity,
    string Message,
    string? SourcePath,
    int? Line = null,
    int? Column = null,
    int? SpanStart = null,
    int? SpanLength = null,
    string? DisplaySeverity = null);

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
    OblivionProductActionId ActionId,
    string Label,
    bool Enabled,
    string Intent,
    bool RequiresEffect,
    OblivionCardActionAvailability Availability,
    OblivionCardEffectKind EffectKind)
{
    public OblivionCardActionDescriptor(
        string Id,
        string Label,
        bool Enabled,
        string Intent,
        bool RequiresEffect,
        OblivionCardActionAvailability Availability,
        OblivionCardEffectKind EffectKind)
        : this(
            new OblivionProductActionId(Id),
            Label,
            Enabled,
            Intent,
            RequiresEffect,
            Availability,
            EffectKind)
    {
    }

    public string Id => ActionId.Value;
}

public sealed record OblivionCardActionInvocation(
    OblivionCardId CardId,
    OblivionProductActionId ActionId,
    string PageId,
    string? SourcePath)
{
    public OblivionCardActionInvocation(
        OblivionCardId cardId,
        string actionId,
        string pageId,
        string? sourcePath)
        : this(cardId, new OblivionProductActionId(actionId), pageId, sourcePath)
    {
    }
}

public sealed record OblivionEffectContext(
    OblivionProductActionId ActionId,
    OblivionCardKind CardKind,
    string PageId,
    string? WorkspaceId,
    string? SourcePath,
    string Intent);

public abstract record OblivionEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context)
{
    public abstract OblivionCardEffectKind Kind { get; }

    public string Intent => Context.Intent;
}

public sealed record RefreshContentEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.RefreshMarkdown;
}

public sealed record OpenSourceEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.OpenSource;
}

public sealed record CopySourcePathEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.CopySourcePath;
}

public sealed record OpenArtifactEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.OpenArtifact;
}

public sealed record RunCodeFactEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.RunCodeFact;
}

public sealed record RunCodeTheoryEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.RunCodeTheory;
}

public sealed record ExportCardEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.ExportCard;
}

public sealed record RenderPreviewEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.RenderPreview;
}

public sealed record NoOpEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.None;
}

public sealed record CustomEffectRequest(
    string RequestId,
    OblivionCardId CardId,
    OblivionEffectContext Context) : OblivionEffectRequest(RequestId, CardId, Context)
{
    public override OblivionCardEffectKind Kind => OblivionCardEffectKind.Custom;
}

public enum OblivionCardEffectStatus
{
    Deferred,
    Rejected,
    Completed,
}

public abstract record OblivionEffectResult(
    string RequestId,
    OblivionCardId CardId,
    OblivionCardEffectKind Kind,
    string Message,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    IReadOnlyList<OblivionCardArtifactRef> Artifacts)
{
    public abstract OblivionCardEffectStatus Status { get; }
}

public sealed record DeferredEffectResult(
    string RequestId,
    OblivionCardId CardId,
    OblivionCardEffectKind Kind,
    string Message,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    IReadOnlyList<OblivionCardArtifactRef> Artifacts)
    : OblivionEffectResult(RequestId, CardId, Kind, Message, Diagnostics, Artifacts)
{
    public override OblivionCardEffectStatus Status => OblivionCardEffectStatus.Deferred;
}

public sealed record RejectedEffectResult(
    string RequestId,
    OblivionCardId CardId,
    OblivionCardEffectKind Kind,
    string Message,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    IReadOnlyList<OblivionCardArtifactRef> Artifacts)
    : OblivionEffectResult(RequestId, CardId, Kind, Message, Diagnostics, Artifacts)
{
    public override OblivionCardEffectStatus Status => OblivionCardEffectStatus.Rejected;
}

public sealed record CompletedEffectResult(
    string RequestId,
    OblivionCardId CardId,
    OblivionCardEffectKind Kind,
    string Message,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    IReadOnlyList<OblivionCardArtifactRef> Artifacts)
    : OblivionEffectResult(RequestId, CardId, Kind, Message, Diagnostics, Artifacts)
{
    public override OblivionCardEffectStatus Status => OblivionCardEffectStatus.Completed;
}

public sealed record OblivionEffectState(
    IReadOnlyDictionary<string, OblivionEffectRequest> LastRequestByCardId,
    IReadOnlyDictionary<string, OblivionEffectResult> LastResultByCardId)
{
    public static OblivionEffectState Empty { get; } = new(
        new Dictionary<string, OblivionEffectRequest>(StringComparer.Ordinal),
        new Dictionary<string, OblivionEffectResult>(StringComparer.Ordinal));

    public OblivionEffectRequest? GetLastRequest(OblivionCardId cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        return LastRequestByCardId.TryGetValue(cardId.Value, out OblivionEffectRequest? request)
            ? request
            : null;
    }

    public OblivionEffectResult? GetLastResult(OblivionCardId cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        return LastResultByCardId.TryGetValue(cardId.Value, out OblivionEffectResult? result)
            ? result
            : null;
    }

    public OblivionEffectState WithOutcome(
        OblivionEffectRequest request,
        OblivionEffectResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(request.RequestId, result.RequestId, StringComparison.Ordinal) ||
            request.CardId != result.CardId ||
            request.Kind != result.Kind)
        {
            throw new InvalidOperationException(
                $"Effect result '{result.RequestId}' does not match request '{request.RequestId}'.");
        }

        Dictionary<string, OblivionEffectRequest> requests = new(LastRequestByCardId, StringComparer.Ordinal)
        {
            [request.CardId.Value] = request,
        };
        Dictionary<string, OblivionEffectResult> results = new(LastResultByCardId, StringComparer.Ordinal)
        {
            [result.CardId.Value] = result,
        };

        return new OblivionEffectState(requests, results);
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
    OblivionEffectRequest? LastEffectRequest,
    OblivionEffectResult? LastEffectResult,
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
    OblivionEffectRequest? LastEffectRequest,
    OblivionEffectResult? LastEffectResult,
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

    OblivionEffectRequest? CreateEffectRequest(
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
            OblivionCardKind.Diagram => "Diagram",
            OblivionCardKind.Table => "Table",
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
