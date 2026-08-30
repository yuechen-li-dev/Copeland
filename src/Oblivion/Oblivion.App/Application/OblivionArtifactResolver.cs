using Oblivion.Model;
using Oblivion.Persistence;

namespace Oblivion.App;

public sealed record OblivionResolvedArtifactProvenance(
    OblivionProvenanceSourceKind SourceKind,
    string? SourceReference,
    string? ProducerActionId,
    OblivionArtifactId? ParentArtifactId,
    OblivionCardId? ParentCardId);

public sealed record OblivionResolvedArtifact(
    OblivionArtifactAddress Address,
    string Label,
    string Kind,
    string? DeclaredReference,
    string? ResolvedPath,
    bool Exists,
    bool IsFile,
    bool IsDirectory,
    string? Extension,
    long? ByteLength,
    string? MediaType,
    bool Generated,
    string? DeclarationSourceReference,
    OblivionResolvedArtifactProvenance Provenance,
    IReadOnlyList<OblivionProductDiagnostic> Diagnostics);

public sealed record OblivionArtifactResolutionResult(
    OblivionResolvedArtifact? Artifact,
    IReadOnlyList<OblivionProductDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Artifact is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed class OblivionArtifactResolver
{
    public OblivionArtifactResolutionResult Resolve(
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        OblivionWorkspacePage page,
        OblivionCard card,
        OblivionCardArtifact declaration)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(declaration);

        OblivionArtifactAddress address = new(
            workspace.Id,
            page.Id,
            card.Id,
            declaration.ArtifactId);
        List<OblivionProductDiagnostic> diagnostics = [];
        string? resolvedPath = ResolvePath(
            location.RootDirectory,
            declaration.Reference,
            address,
            diagnostics);

        bool exists = resolvedPath is not null && (File.Exists(resolvedPath) || Directory.Exists(resolvedPath));
        bool isFile = resolvedPath is not null && File.Exists(resolvedPath);
        bool isDirectory = resolvedPath is not null && Directory.Exists(resolvedPath);
        long? byteLength = isFile ? new FileInfo(resolvedPath!).Length : null;
        string? extension = GetExtension(resolvedPath ?? declaration.Reference);
        string? mediaType = ResolveMediaType(extension);

        if (resolvedPath is not null && !exists)
        {
            diagnostics.Add(CreateDiagnostic(
                "OBLIVION-ARTIFACT-NOT-FOUND",
                OblivionDiagnosticSeverity.Warning,
                $"Artifact '{declaration.Id}' does not exist at '{resolvedPath}'.",
                address,
                declaration.SourceReference));
        }

        OblivionResolvedArtifact artifact = new(
            address,
            declaration.Label,
            declaration.Kind,
            declaration.Reference,
            resolvedPath,
            exists,
            isFile,
            isDirectory,
            extension,
            byteLength,
            mediaType,
            declaration.Generated,
            declaration.SourceReference,
            new OblivionResolvedArtifactProvenance(
                declaration.Generated
                    ? OblivionProvenanceSourceKind.Generated
                    : card.Provenance.SourceKind,
                declaration.SourceReference ?? card.Provenance.SourceReference,
                card.Provenance.ProducerActionId,
                card.Provenance.ParentArtifactId,
                card.Provenance.ParentCardId),
            diagnostics);
        return new OblivionArtifactResolutionResult(artifact, diagnostics);
    }

    public OblivionArtifactResolutionResult Resolve(
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        string cardId,
        string artifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        List<(OblivionWorkspacePage Page, OblivionCard Card)> owners = [];
        foreach (OblivionWorkspacePage page in workspace.Pages)
        {
            foreach (OblivionCard card in page.Cards.Where(candidate => candidate.Id.Value == cardId))
            {
                owners.Add((page, card));
            }
        }

        if (owners.Count == 0)
        {
            return new OblivionArtifactResolutionResult(
                null,
                [new OblivionProductDiagnostic(
                    "OBLIVION-ARTIFACT-OWNER-NOT-FOUND",
                    OblivionDiagnosticSeverity.Error,
                    $"Owner card '{cardId}' was not found in workspace '{workspace.Id.Value}'.",
                    workspace.Id.Value,
                    CardId: cardId,
                    ArtifactId: artifactId)]);
        }

        List<(OblivionWorkspacePage Page, OblivionCard Card, OblivionCardArtifact Artifact)> matches = owners
            .SelectMany(owner => owner.Card.Artifacts
                .Where(artifact => artifact.Id == artifactId)
                .Select(artifact => (owner.Page, owner.Card, artifact)))
            .ToList();

        if (matches.Count == 0)
        {
            return new OblivionArtifactResolutionResult(
                null,
                [new OblivionProductDiagnostic(
                    "OBLIVION-ARTIFACT-NOT-FOUND",
                    OblivionDiagnosticSeverity.Error,
                    $"Artifact '{artifactId}' on card '{cardId}' was not found.",
                    workspace.Id.Value,
                    CardId: cardId,
                    ArtifactId: artifactId)]);
        }

        if (matches.Count > 1)
        {
            return new OblivionArtifactResolutionResult(
                null,
                [new OblivionProductDiagnostic(
                    "OBLIVION-ARTIFACT-ID-AMBIGUOUS",
                    OblivionDiagnosticSeverity.Error,
                    $"Artifact '{artifactId}' on card '{cardId}' has {matches.Count} declarations; lookup was rejected.",
                    workspace.Id.Value,
                    CardId: cardId,
                    ArtifactId: artifactId)]);
        }

        var match = matches[0];
        return Resolve(workspace, location, match.Page, match.Card, match.Artifact);
    }

    public OblivionArtifactResolutionResult Resolve(
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        OblivionArtifactAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (workspace.Id != address.WorkspaceId)
        {
            return new OblivionArtifactResolutionResult(
                null,
                [new OblivionProductDiagnostic(
                    "OBLIVION-ARTIFACT-OWNER-NOT-FOUND",
                    OblivionDiagnosticSeverity.Error,
                    $"Artifact address names workspace '{address.WorkspaceId.Value}', not loaded workspace '{workspace.Id.Value}'.",
                    workspace.Id.Value,
                    address.PageId.Value,
                    address.CardId.Value,
                    ArtifactId: address.ArtifactId.Value)]);
        }

        OblivionWorkspacePage? page = workspace.Pages.FirstOrDefault(candidate => candidate.Id == address.PageId);
        OblivionCard? card = page?.Cards.FirstOrDefault(candidate => candidate.Id == address.CardId);
        if (page is null || card is null)
        {
            return new OblivionArtifactResolutionResult(
                null,
                [new OblivionProductDiagnostic(
                    "OBLIVION-ARTIFACT-OWNER-NOT-FOUND",
                    OblivionDiagnosticSeverity.Error,
                    $"Artifact owner '{address.PageId.Value}/{address.CardId.Value}' was not found.",
                    workspace.Id.Value,
                    address.PageId.Value,
                    address.CardId.Value,
                    ArtifactId: address.ArtifactId.Value)]);
        }

        OblivionCardArtifact[] matches = card.Artifacts
            .Where(artifact => artifact.ArtifactId == address.ArtifactId)
            .ToArray();
        if (matches.Length != 1)
        {
            return Resolve(workspace, location, card.Id.Value, address.ArtifactId.Value);
        }

        return Resolve(workspace, location, page, card, matches[0]);
    }

    private static string? ResolvePath(
        string workspaceRoot,
        string? declaredReference,
        OblivionArtifactAddress address,
        List<OblivionProductDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(declaredReference))
        {
            diagnostics.Add(CreateDiagnostic(
                "OBLIVION-ARTIFACT-REFERENCE-MISSING",
                OblivionDiagnosticSeverity.Info,
                $"Artifact '{address.ArtifactId.Value}' has no filesystem reference.",
                address,
                null));
            return null;
        }

        if (Path.IsPathRooted(declaredReference))
        {
            diagnostics.Add(CreateDiagnostic(
                "OBLIVION-ARTIFACT-PATH-UNSAFE",
                OblivionDiagnosticSeverity.Error,
                $"Artifact reference '{declaredReference}' is absolute; artifact paths must be workspace-relative.",
                address,
                declaredReference));
            return null;
        }

        string fullRoot = Path.GetFullPath(workspaceRoot);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, declaredReference));
        string relativePath = Path.GetRelativePath(fullRoot, fullPath);
        if (relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                "OBLIVION-ARTIFACT-PATH-UNSAFE",
                OblivionDiagnosticSeverity.Error,
                $"Artifact reference '{declaredReference}' escapes workspace root '{fullRoot}'.",
                address,
                declaredReference));
            return null;
        }

        return fullPath;
    }

    private static string? GetExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string extension = Path.GetExtension(path);
        return string.IsNullOrEmpty(extension)
            ? null
            : extension.ToLowerInvariant();
    }

    private static string? ResolveMediaType(string? extension)
    {
        return extension switch
        {
            ".md" or ".markdown" => "text/markdown",
            ".txt" or ".log" or ".cs" or ".ts" or ".tsx" or ".js" or ".jsx" or ".toml" => "text/plain",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            null => null,
            _ => "application/octet-stream",
        };
    }

    private static OblivionProductDiagnostic CreateDiagnostic(
        string code,
        OblivionDiagnosticSeverity severity,
        string message,
        OblivionArtifactAddress address,
        string? sourceReference)
    {
        return new OblivionProductDiagnostic(
            code,
            severity,
            message,
            address.WorkspaceId.Value,
            address.PageId.Value,
            address.CardId.Value,
            ArtifactId: address.ArtifactId.Value,
            SourceReference: sourceReference);
    }
}
