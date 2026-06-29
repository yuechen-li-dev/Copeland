namespace Machina.Presenter.Sample;

public enum OblivionCardKind
{
    Note,
    Status,
    UiPreview,
    Artifact,
    CodeFact,
    CodeTheory,
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

public sealed record OblivionCardId(string Value);

public sealed record OblivionCardAction(
    string Id,
    string Label,
    bool Enabled);

public sealed record OblivionCardArtifact(
    string Id,
    string Label,
    string Kind,
    string? Path);

public sealed record OblivionCard(
    OblivionCardId Id,
    OblivionCardKind Kind,
    OblivionCardStatus Status,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> BodyLines,
    IReadOnlyList<OblivionCardAction> Actions,
    IReadOnlyList<OblivionCardArtifact> Artifacts);
