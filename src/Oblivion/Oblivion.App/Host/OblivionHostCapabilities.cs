using Oblivion.Product;
using Oblivion.Model;

namespace Oblivion.App;

public enum OblivionHostCapability
{
    RefreshContent,
    OpenSource,
    CopySourcePath,
    OpenArtifact,
    ExportCard,
    RenderPreview,
}

public enum OblivionHostPathTargetKind
{
    Source,
    Artifact,
}

public sealed record OblivionOpenPathCapabilityRequest(
    string RequestId,
    string WorkspaceId,
    string PageId,
    string CardId,
    string ActionId,
    OblivionCardEffectKind EffectKind,
    OblivionHostPathTargetKind TargetKind,
    string DeclaredReference,
    string ResolvedPath,
    OblivionArtifactAddress? ArtifactAddress = null);

public sealed record OblivionCopyTextCapabilityRequest(
    string RequestId,
    string WorkspaceId,
    string PageId,
    string CardId,
    string ActionId,
    OblivionCardEffectKind EffectKind,
    string Text,
    string SemanticKind);

public sealed record OblivionHostCapabilityResult(
    bool Succeeded,
    string Message,
    string? DiagnosticCode = null);

public sealed record OblivionLocalHostCapabilities(
    Func<OblivionOpenPathCapabilityRequest, OblivionHostCapabilityResult>? OpenPath = null,
    Func<OblivionCopyTextCapabilityRequest, OblivionHostCapabilityResult>? CopyText = null)
{
    public static OblivionLocalHostCapabilities None { get; } = new();
}

public sealed record OblivionHostCapabilities(
    Func<RefreshContentEffectRequest, OblivionEffectResult>? RefreshContent = null,
    Func<OpenSourceEffectRequest, OblivionEffectResult>? OpenSource = null,
    Func<CopySourcePathEffectRequest, OblivionEffectResult>? CopySourcePath = null,
    Func<OpenArtifactEffectRequest, OblivionEffectResult>? OpenArtifact = null,
    Func<ExportCardEffectRequest, OblivionEffectResult>? ExportCard = null,
    Func<RenderPreviewEffectRequest, OblivionEffectResult>? RenderPreview = null)
{
    public static OblivionHostCapabilities None { get; } = new();

    public bool TryExecute(
        OblivionEffectRequest request,
        out OblivionEffectResult? result)
    {
        result = request switch
        {
            RefreshContentEffectRequest refresh when RefreshContent is not null => RefreshContent(refresh),
            OpenSourceEffectRequest open when OpenSource is not null => OpenSource(open),
            CopySourcePathEffectRequest copy when CopySourcePath is not null => CopySourcePath(copy),
            OpenArtifactEffectRequest open when OpenArtifact is not null => OpenArtifact(open),
            ExportCardEffectRequest export when ExportCard is not null => ExportCard(export),
            RenderPreviewEffectRequest render when RenderPreview is not null => RenderPreview(render),
            _ => null,
        };
        return result is not null;
    }
}
