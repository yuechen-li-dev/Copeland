using Machina.Fonts.Toml;

namespace Machina.Fonts.Artifacts;

public static class FontAtlasArtifactImporter
{
    public static FontAtlasArtifactImportResult Import(string tomlPath, bool validatePageArtifacts = true)
    {
        ArgumentNullException.ThrowIfNull(tomlPath);
        FontAtlasTomlLoadResult load = FontAtlasTomlLoader.LoadFile(tomlPath);
        List<FontAtlasTomlDiagnostic> diagnostics = [.. load.Diagnostics];

        if (load.Document is not null && validatePageArtifacts)
        {
            diagnostics.AddRange(FontAtlasPageArtifactValidator.Validate(load.Document, tomlPath));
        }

        bool success = load.Snapshot is not null && !diagnostics.Any(diagnostic => diagnostic.Severity == FontAtlasTomlDiagnosticSeverity.Error);
        FontAtlasSnapshot? snapshot = success ? load.Snapshot : null;
        return new FontAtlasArtifactImportResult(success, load.Document, snapshot, diagnostics);
    }
}
