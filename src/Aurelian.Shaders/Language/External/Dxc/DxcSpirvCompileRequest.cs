namespace Aurelian.Shaders.Language.External.Dxc;

public sealed record DxcSpirvCompileRequest(
    string SourceText,
    string EntryPoint,
    string Profile,
    string SourceName,
    IReadOnlyList<string>? AdditionalArguments = null);
