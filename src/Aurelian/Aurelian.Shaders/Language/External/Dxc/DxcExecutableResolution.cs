namespace Aurelian.Shaders.Language.External.Dxc;

public sealed record DxcExecutableResolution(
    DxcToolStatus Status,
    string? ExecutablePath,
    IReadOnlyList<DxcToolDiagnostic> Diagnostics)
{
    public bool Success => Status == DxcToolStatus.Available && !string.IsNullOrWhiteSpace(ExecutablePath);
}
