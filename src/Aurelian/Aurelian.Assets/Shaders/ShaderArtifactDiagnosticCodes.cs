namespace Aurelian.Assets.Shaders;

public static class ShaderArtifactDiagnosticCodes
{
    public const string ManifestMissing = "ASA1001";
    public const string ManifestParseFailed = "ASA1002";
    public const string UnsupportedFormat = "ASA1003";
    public const string StageMissing = "ASA1004";
    public const string UnsupportedStage = "ASA1005";
    public const string SpirvFileMissing = "ASA1006";
    public const string SpirvHashMismatch = "ASA1007";
    public const string EmptySpirv = "ASA1008";
    public const string InvalidHash = "ASA1009";
    public const string DuplicateStage = "ASA1010";
    public const string UnsupportedSpirvEncoding = "ASA1011";
    public const string HexSpirvParseFailed = "ASA1012";
}
