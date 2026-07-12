namespace Aurelian.Rendering.Contracts.Shaders;

public sealed record CompiledShaderStage(
    CompiledShaderStageKind Stage,
    string EntryPoint,
    string Profile,
    byte[] SpirvBytes,
    string SpirvSha256,
    string SourceName);
