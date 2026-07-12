namespace Aurelian.Assets.Shaders;

public sealed record ShaderArtifactStageManifest(
    string Stage,
    string EntryPoint,
    string Profile,
    string SpirvEncoding,
    string SpirvPath,
    string SpirvSha256,
    string SourceName);
