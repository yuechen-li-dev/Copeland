namespace Aurelian.Shaders.Language.External.Dxc;

public sealed record DxcValidationResult(
    DxcValidationStatus Status,
    string EntryPoint,
    string Profile,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    IReadOnlyList<string> Arguments)
{
    public bool Success => Status == DxcValidationStatus.Succeeded;
}
