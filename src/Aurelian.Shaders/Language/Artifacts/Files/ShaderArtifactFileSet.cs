namespace Aurelian.Shaders.Language.Artifacts.Files;

public sealed record ShaderArtifactFileSet(
    string OutputDirectory,
    string ManifestPath,
    IReadOnlyList<string> SpirvPaths,
    string? GeneratedHlslPath);
