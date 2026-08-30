using Oblivion.Model;

namespace Oblivion.Persistence;

public sealed record OblivionWorkspaceManifest(
    int Format,
    string Kind,
    string WorkspaceId,
    string Title,
    string? DefaultPageId,
    IReadOnlyList<OblivionWorkspaceSectionManifest> Sections);

public sealed record OblivionWorkspaceSectionManifest(
    string Id,
    string Title,
    IReadOnlyList<OblivionWorkspacePageManifest> Pages);

public sealed record OblivionWorkspacePageManifest(
    string Id,
    string Title,
    string? Asset,
    IReadOnlyList<string> Cards);

public sealed record OblivionPageAssetDocument(
    int Format,
    string Kind,
    string Id,
    string Title,
    string? Description,
    IReadOnlyList<string> Tags);

public sealed record OblivionCardBodyDocument(
    string Format,
    string? Text,
    string? Path);

public sealed record OblivionCardActionDocument(
    string Id,
    string Label,
    bool Enabled);

public sealed record OblivionCardArtifactDocument(
    string Id,
    string Label,
    string Kind,
    string? Path,
    bool Generated,
    string? Asset);

public sealed record OblivionCardAssetDocument(
    int Format,
    string Kind,
    string Id,
    string CardKind,
    string Status,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Tags,
    OblivionCardBodyDocument Body,
    IReadOnlyList<OblivionCardActionDocument> Actions,
    IReadOnlyList<OblivionCardArtifactDocument> Artifacts);

public sealed record OblivionArtifactAssetDocument(
    int Format,
    string Kind,
    string Id,
    string Label,
    string ArtifactKind,
    string? Path,
    bool Generated);

public sealed record OblivionWorkspaceLocation(
    string RootDirectory,
    string ManifestPath);

public sealed record OblivionWorkspaceDiagnostic(
    OblivionDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? SourcePath,
    string? DisplaySeverity = null,
    int? Line = null,
    int? Column = null,
    int? SpanStart = null,
    int? SpanLength = null)
{
    public override string ToString()
    {
        string severity = DisplaySeverity ?? Severity.ToString();
        string location = Line is null || Column is null
            ? string.Empty
            : $"@{Line}:{Column}";

        return SourcePath is null
            ? $"{severity}:{Code}{location}:{Message}"
            : $"{severity}:{Code}{location}:{SourcePath}:{Message}";
    }
}

public sealed record OblivionWorkspaceLoadOptions(
    bool AllowAbsolutePaths = false);

public sealed record OblivionWorkspaceJsonReadResult(
    OblivionWorkspaceManifest? Manifest,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics);

public sealed record OblivionPageTomlReadResult(
    OblivionPageAssetDocument? Document,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics);

public sealed record OblivionCardTomlReadResult(
    OblivionCardAssetDocument? Document,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics);

public sealed record OblivionArtifactTomlReadResult(
    OblivionArtifactAssetDocument? Document,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics);

public sealed record OblivionWorkspaceLoadResult(
    OblivionWorkspace? Workspace,
    OblivionWorkspaceLocation? Location,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Workspace is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}
