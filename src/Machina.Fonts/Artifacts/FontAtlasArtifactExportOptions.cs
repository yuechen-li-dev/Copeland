namespace Machina.Fonts.Artifacts;

public sealed record FontAtlasArtifactExportOptions(
    string AtlasName,
    string OutputDirectory,
    string PageFileExtension = ".fakepage",
    bool Overwrite = true);
