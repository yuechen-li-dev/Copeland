namespace Aurelian.Shaders.Language.Artifacts.Files;

public static class ShaderArtifactFileDiagnosticCodes
{
    public const string ArtifactMissing = "ASF1001";
    public const string ArtifactFailed = "ASF1002";
    public const string OutputDirectoryMissing = "ASF1003";
    public const string StageMissingSpirv = "ASF1004";
    public const string FileWriteFailed = "ASF1005";
    public const string HashMismatch = "ASF1006";
    public const string UnsupportedSpirvEncoding = "ASF1007";
    public const string HexWriteFailed = "ASF1008";
}
