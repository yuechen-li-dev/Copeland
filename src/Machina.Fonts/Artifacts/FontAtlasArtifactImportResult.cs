using Machina.Fonts.Toml;

namespace Machina.Fonts.Artifacts;

public sealed record FontAtlasArtifactImportResult(
    bool Success,
    FontAtlasTomlDocument? Document,
    FontAtlasSnapshot? Snapshot,
    IReadOnlyList<FontAtlasTomlDiagnostic> Diagnostics);
