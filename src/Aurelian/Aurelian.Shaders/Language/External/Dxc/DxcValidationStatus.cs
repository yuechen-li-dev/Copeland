namespace Aurelian.Shaders.Language.External.Dxc;

public enum DxcValidationStatus
{
    Succeeded,
    Failed,
    SkippedToolUnavailable,
    SkippedNoEntryPoints,
    SkippedNoHlsl,
}
