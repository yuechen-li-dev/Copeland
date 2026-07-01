namespace Aurelian.Shaders.Language.External.Dxc;

public sealed record DxcSpirvCompileResult(
    DxcSpirvStatus Status,
    byte[] SpirvBytes,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<DxcToolDiagnostic> Diagnostics)
{
    public bool Success => Status == DxcSpirvStatus.Compiled && SpirvBytes.Length > 0;
}
