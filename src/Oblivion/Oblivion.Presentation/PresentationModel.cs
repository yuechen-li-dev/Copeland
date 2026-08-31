using Oblivion.Model;

namespace Oblivion.Presentation;

public readonly record struct PresentationId
{
    public PresentationId(string value)
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

public readonly record struct PresentationContentId
{
    public PresentationContentId(string value)
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

public sealed record Presentation(
    PresentationId Id,
    string Title,
    IReadOnlyList<PresentationContent> Content,
    IReadOnlyList<PresentationLayoutGroup> Layout)
{
    public static Presentation Create(
        string id,
        string title,
        IReadOnlyList<PresentationContent> content,
        IReadOnlyList<PresentationLayoutGroup>? layout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);

        return new Presentation(
            new PresentationId(id),
            title,
            content,
            layout ?? []);
    }
}

public sealed record PresentationSource(string Content, string? Reference)
{
    public static PresentationSource Inline(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new PresentationSource(content, Reference: null);
    }

    public static PresentationSource File(string path, string? content = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new PresentationSource(content ?? System.IO.File.ReadAllText(path), path);
    }
}

public abstract record PresentationContent(
    PresentationContentId Id,
    string? Title,
    OblivionProvenance? Provenance);

public sealed record SummaryContent(
    PresentationContentId Id,
    string Text,
    string? Title = null,
    OblivionProvenance? Provenance = null)
    : PresentationContent(Id, Title, Provenance);

public sealed record MarkdownContent(
    PresentationContentId Id,
    PresentationSource Source,
    string? Title = null,
    OblivionProvenance? Provenance = null)
    : PresentationContent(Id, Title, Provenance);

public sealed record CodeContent(
    PresentationContentId Id,
    PresentationSource Source,
    string? Language = null,
    int? StartLine = null,
    int? EndLine = null,
    string? Title = null,
    OblivionProvenance? Provenance = null)
    : PresentationContent(Id, Title, Provenance);

public abstract record DiagramSource
{
    private DiagramSource()
    {
    }

    public sealed record Mermaid(PresentationSource Source) : DiagramSource;
}

public sealed record DiagramContent(
    PresentationContentId Id,
    DiagramSource Source,
    string? Title = null,
    OblivionProvenance? Provenance = null)
    : PresentationContent(Id, Title, Provenance);

public sealed record ArtifactContent(
    PresentationContentId Id,
    string Reference,
    string Kind,
    string? Label = null,
    bool Generated = false,
    string? Title = null,
    OblivionProvenance? Provenance = null)
    : PresentationContent(Id, Title, Provenance);

public sealed record DecisionContent(
    PresentationContentId Id,
    string Text,
    IReadOnlyList<PresentationContentId> Evidence,
    string? Title = null,
    OblivionProvenance? Provenance = null)
    : PresentationContent(Id, Title, Provenance);

public sealed record NextActionsContent(
    PresentationContentId Id,
    IReadOnlyList<string> Items,
    string? Title = null,
    OblivionProvenance? Provenance = null)
    : PresentationContent(Id, Title, Provenance);

public static class Content
{
    public static SummaryContent Summary(string id, string text, string? title = null)
    {
        return new SummaryContent(new PresentationContentId(id), text, title);
    }

    public static MarkdownContent Markdown(string id, PresentationSource source, string? title = null)
    {
        return new MarkdownContent(new PresentationContentId(id), source, title);
    }

    public static CodeContent Code(
        string id,
        PresentationSource source,
        string? language = null,
        int? startLine = null,
        int? endLine = null,
        string? title = null)
    {
        return new CodeContent(new PresentationContentId(id), source, language, startLine, endLine, title);
    }

    public static DiagramContent Diagram(string id, DiagramSource source, string? title = null)
    {
        return new DiagramContent(new PresentationContentId(id), source, title);
    }

    public static ArtifactContent Artifact(
        string id,
        string reference,
        string kind,
        string? label = null,
        bool generated = false,
        string? title = null)
    {
        return new ArtifactContent(
            new PresentationContentId(id),
            reference,
            kind,
            label,
            generated,
            title);
    }

    public static DecisionContent Decision(
        string id,
        string text,
        IReadOnlyList<string>? evidence = null,
        string? title = null)
    {
        return new DecisionContent(
            new PresentationContentId(id),
            text,
            evidence?.Select(value => new PresentationContentId(value)).ToArray() ?? [],
            title);
    }

    public static NextActionsContent NextActions(
        string id,
        IReadOnlyList<string> items,
        string? title = null)
    {
        return new NextActionsContent(new PresentationContentId(id), items, title);
    }
}

public abstract record PresentationLayoutGroup(
    string Id,
    IReadOnlyList<PresentationContentId> ContentIds);

public sealed record CompareLayoutGroup(
    string Id,
    PresentationContentId Left,
    PresentationContentId Right)
    : PresentationLayoutGroup(Id, [Left, Right]);

public sealed record ColumnsLayoutGroup(
    string Id,
    IReadOnlyList<PresentationContentId> Columns)
    : PresentationLayoutGroup(Id, Columns);

public sealed record FocusLayoutGroup(
    string Id,
    PresentationContentId ContentId)
    : PresentationLayoutGroup(Id, [ContentId]);

public static class Layout
{
    public static CompareLayoutGroup Compare(string id, string left, string right)
    {
        return new CompareLayoutGroup(
            id,
            new PresentationContentId(left),
            new PresentationContentId(right));
    }

    public static ColumnsLayoutGroup Columns(string id, params string[] contentIds)
    {
        return new ColumnsLayoutGroup(
            id,
            contentIds.Select(value => new PresentationContentId(value)).ToArray());
    }

    public static FocusLayoutGroup Focus(string id, string contentId)
    {
        return new FocusLayoutGroup(id, new PresentationContentId(contentId));
    }
}
