namespace Aurelian.Shaders.Language.External.Dxc;

public sealed record DxcArtifactValidationResult(IReadOnlyList<DxcValidationResult> Results)
{
    public bool Success => Results.All(x =>
        x.Status is DxcValidationStatus.Succeeded
            or DxcValidationStatus.SkippedToolUnavailable
            or DxcValidationStatus.SkippedNoEntryPoints
            or DxcValidationStatus.SkippedNoHlsl);
}
