using Oblivion.Product;

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
