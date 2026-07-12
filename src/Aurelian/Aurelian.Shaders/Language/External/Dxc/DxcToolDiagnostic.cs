namespace Aurelian.Shaders.Language.External.Dxc;

public sealed record DxcToolDiagnostic(
    string Code,
    string Message,
    string? Path = null);
