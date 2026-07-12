using Machina.Fonts.Generation;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Artifacts.DistanceField;

public static class DistanceFieldAtlasArtifactExporter
{
    public static FontAtlasArtifactExportResult Export(
        GeneratedFieldAtlasPackResult packResult,
        FontAtlasTomlExportMetadata metadata,
        string outputDirectory,
        string atlasName,
        bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(packResult);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasName);

        List<FontAtlasTomlDiagnostic> diagnostics = [];
        List<string> pagePaths = [];
        string tomlPath = Path.Combine(outputDirectory, atlasName + ".font-atlas.toml");

        if (!packResult.Success)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                FontAtlasTomlDiagnosticCode.InvalidValue,
                "Cannot export a failed generated-field atlas pack result.",
                tomlPath));
            return new FontAtlasArtifactExportResult(false, tomlPath, pagePaths, null, diagnostics);
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);

            FontAtlasPage[] pages = new FontAtlasPage[packResult.Pages.Count];
            for (int i = 0; i < packResult.Pages.Count; i++)
            {
                GeneratedFieldAtlasPage page = packResult.Pages[i];
                string fileName = $"{atlasName}.page{page.Index}.dfpage";
                string pagePath = Path.Combine(outputDirectory, fileName);
                DistanceFieldPageArtifact artifact = DistanceFieldPageArtifactWriter.Write(
                    pagePath,
                    metadata.DistanceField,
                    page,
                    overwrite);

                pagePaths.Add(pagePath);
                pages[i] = new FontAtlasPage(
                    page.Index,
                    fileName,
                    page.Width,
                    page.Height,
                    artifact.ContentHash);
            }

            FontAtlasSnapshot exportSnapshot = new(
                packResult.Snapshot.Version,
                pages.OrderBy(static page => page.Index).ToArray(),
                packResult.Snapshot.Glyphs);

            FontAtlasTomlDocument document = FontAtlasTomlConversion.FromSnapshot(
                exportSnapshot,
                metadata with { Name = atlasName });

            File.WriteAllText(tomlPath, FontAtlasTomlWriter.Write(document));
            return new FontAtlasArtifactExportResult(true, tomlPath, pagePaths, document, diagnostics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                FontAtlasTomlDiagnosticCode.InvalidValue,
                ex.Message,
                tomlPath));
            return new FontAtlasArtifactExportResult(false, tomlPath, pagePaths, null, diagnostics);
        }
    }
}
