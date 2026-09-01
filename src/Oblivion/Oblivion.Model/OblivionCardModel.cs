namespace Oblivion.Model;

public enum OblivionCardKind
{
    Note,
    Status,
    UiPreview,
    Artifact,
    CodeFact,
    CodeTheory,
    Diagram,
    Table,
    Function,
}

public enum OblivionCardStatus
{
    Idle,
    Passing,
    Failing,
    Warning,
    Deferred,
    Placeholder,
}

public enum OblivionCardBodyFormat
{
    Plain,
    CopelandMarkdown,
}

public sealed record OblivionWorkspaceId(string Value);
public sealed record OblivionPageId(string Value);
public sealed record OblivionCardId(string Value);
public sealed record OblivionArtifactId(string Value);

public enum OblivionDiagramSourceKind
{
    CopelandFlow,
    CopelandTemplate,
}

public enum OblivionDiagramProjectionKind
{
    State,
    Diagram,
}

public sealed record OblivionDiagramSource(
    OblivionDiagramSourceKind Kind,
    string Reference,
    string Symbol,
    OblivionDiagramProjectionKind Projection);

public enum OblivionTableSourceKind
{
    TsonTable,
}

public sealed record OblivionTableSource(
    OblivionTableSourceKind Kind,
    string Reference);

public enum OblivionFunctionSourceKind
{
    CopelandXunit,
}

public sealed record OblivionFunctionSource(
    OblivionFunctionSourceKind Kind,
    string Reference,
    string Test);

public enum OblivionDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record OblivionArtifactAddress(
    OblivionWorkspaceId WorkspaceId,
    OblivionPageId PageId,
    OblivionCardId CardId,
    OblivionArtifactId ArtifactId);

public enum OblivionProvenanceSourceKind
{
    Unknown,
    Manual,
    WorkspaceAsset,
    ImportedMarkdown,
    Generated,
}

public sealed record OblivionProvenance(
    OblivionProvenanceSourceKind SourceKind,
    string? SourceReference,
    string? ProducerActionId = null,
    OblivionArtifactId? ParentArtifactId = null,
    OblivionCardId? ParentCardId = null)
{
    public static OblivionProvenance Unknown { get; } = new(
        OblivionProvenanceSourceKind.Unknown,
        SourceReference: null);
}

public readonly record struct OblivionProductActionId
{
    public OblivionProductActionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

public sealed record OblivionCardAction(
    OblivionProductActionId ActionId,
    string Label,
    bool Enabled)
{
    public OblivionCardAction(string id, string label, bool enabled)
        : this(new OblivionProductActionId(id), label, enabled)
    {
    }

    public string Id => ActionId.Value;
}

public sealed record OblivionCardArtifact(
    OblivionArtifactId ArtifactId,
    string Label,
    string Kind,
    string? Reference,
    bool Generated = false,
    string? SourceReference = null)
{
    public OblivionCardArtifact(
        string id,
        string label,
        string kind,
        string? reference,
        bool generated = false,
        string? sourceReference = null)
        : this(new OblivionArtifactId(id), label, kind, reference, generated, sourceReference)
    {
    }

    public string Id => ArtifactId.Value;
}

public abstract record OblivionCardContent;
public sealed record OblivionPlainTextContent(string Text) : OblivionCardContent;
public sealed record OblivionInlineMarkdownContent(string Source) : OblivionCardContent;
public sealed record OblivionMarkdownReferenceContent(string Source, string Reference) : OblivionCardContent;
public sealed record OblivionArtifactContent(OblivionArtifactId ArtifactId) : OblivionCardContent;

public sealed record OblivionCardBody(
    OblivionCardBodyFormat Format,
    OblivionCardContent Content)
{
    public string RawText => Content switch
    {
        OblivionPlainTextContent plainText => plainText.Text,
        OblivionInlineMarkdownContent inlineMarkdown => inlineMarkdown.Source,
        OblivionMarkdownReferenceContent markdownReference => markdownReference.Source,
        _ => string.Empty,
    };

    public string? SourceReference => Content switch
    {
        OblivionMarkdownReferenceContent markdownReference => markdownReference.Reference,
        _ => null,
    };
}

public sealed record OblivionCard(
    OblivionCardId Id,
    OblivionCardKind Kind,
    OblivionCardStatus Status,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Tags,
    OblivionCardBody Body,
    IReadOnlyList<OblivionCardAction> Actions,
    IReadOnlyList<OblivionCardArtifact> Artifacts,
    OblivionProvenance Provenance,
    OblivionPageId? PageId = null,
    OblivionWorkspaceId? WorkspaceId = null,
    OblivionDiagramSource? Diagram = null,
    OblivionTableSource? Table = null,
    OblivionFunctionSource? Function = null);

public sealed record OblivionWorkspacePage(
    OblivionPageId Id,
    string Title,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<OblivionCard> Cards);

public sealed record OblivionWorkspaceSection(
    string Id,
    string Title,
    IReadOnlyList<OblivionWorkspacePage> Pages);

public sealed record OblivionWorkspace(
    OblivionWorkspaceId Id,
    string Title,
    OblivionPageId? DefaultPageId,
    IReadOnlyList<OblivionWorkspaceSection> Sections)
{
    public IReadOnlyList<OblivionWorkspacePage> Pages => Sections
        .SelectMany(section => section.Pages)
        .ToArray();
}
