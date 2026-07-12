namespace Aurelian.Shaders.Language.Artifacts.Spirv;

public sealed record SpirvShaderStageArtifact(
    HlslShaderStageKind Stage,
    string EntryPoint,
    string Profile,
    string SourceName,
    string SourceSha256,
    string SpirvSha256,
    byte[] SpirvBytes,
    IReadOnlyList<string> DxcArguments);
