namespace Machina.Fonts.Toml;

public sealed record FontAtlasTomlLoadResult(
    bool Success,
    FontAtlasTomlDocument? Document,
    FontAtlasSnapshot? Snapshot,
    IReadOnlyList<FontAtlasTomlDiagnostic> Diagnostics);
