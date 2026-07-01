namespace Aurelian.Shaders.Language.Artifacts;

public sealed record SdslvShaderArtifactOptions(
    string FormatVersion = SdslvShaderArtifactOptions.DefaultFormatVersion,
    bool EmitPartialHlslOnError = false)
{
    public const string DefaultFormatVersion = "aurelian.sdslv.artifact/0";

    public static SdslvShaderArtifactOptions Default { get; } = new();
}
