namespace Aurelian.Shaders.Language.Artifacts.Spirv;

public sealed record HlslShaderStageSource(
    HlslShaderStageKind Stage,
    string SourceText,
    string EntryPoint,
    string Profile,
    string SourceName);
