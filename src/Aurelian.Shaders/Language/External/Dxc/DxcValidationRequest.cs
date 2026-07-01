namespace Aurelian.Shaders.Language.External.Dxc;

public sealed record DxcValidationRequest(
    string Hlsl,
    string EntryPoint,
    string Profile,
    string SourceName);
