using Machina.Fonts.Toml;

namespace Machina.Fonts.Artifacts;

public sealed record FontAtlasArtifactExportResult(
    bool Success,
    string TomlPath,
    IReadOnlyList<string> PagePaths,
    FontAtlasTomlDocument? Document,
    IReadOnlyList<FontAtlasTomlDiagnostic> Diagnostics);
