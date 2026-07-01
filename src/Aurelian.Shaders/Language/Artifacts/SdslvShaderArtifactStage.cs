namespace Aurelian.Shaders.Language.Artifacts;

public sealed record SdslvShaderArtifactStage(
    string EntryPoint,
    SdslvShaderStageKind Stage,
    string? Profile);
