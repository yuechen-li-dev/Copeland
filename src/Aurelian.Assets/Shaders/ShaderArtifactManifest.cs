namespace Aurelian.Assets.Shaders;

public sealed record ShaderArtifactManifest(
    string Format,
    string SourceLanguage,
    string SourceName,
    string SourceSha256,
    string? GeneratedHlslPath,
    string? GeneratedHlslSha256,
    IReadOnlyList<ShaderArtifactStageManifest> Stages)
{
    public const string CurrentFormatVersion = "aurelian.shader-artifact/0";
}
