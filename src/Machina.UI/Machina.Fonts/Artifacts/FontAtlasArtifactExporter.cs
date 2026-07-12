using Machina.Fonts.Toml;

namespace Machina.Fonts.Artifacts;

public static class FontAtlasArtifactExporter
{
    public static FontAtlasArtifactExportResult Export(FontAtlasSnapshot snapshot, FontAtlasTomlExportMetadata metadata, FontAtlasArtifactExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);

        List<FontAtlasTomlDiagnostic> diagnostics = [];
        List<string> pagePaths = [];
        string tomlPath = Path.Combine(options.OutputDirectory, options.AtlasName + ".font-atlas.toml");

        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            List<FontAtlasPage> pages = [];
            foreach (FontAtlasPage page in snapshot.Pages.OrderBy(page => page.Index))
            {
                string fileName = options.AtlasName + ".page" + page.Index + options.PageFileExtension;
                string pagePath = Path.Combine(options.OutputDirectory, fileName);
                GlyphAtlasEntry[] glyphs = snapshot.Glyphs.Values.Where(glyph => glyph.PageIndex == page.Index).ToArray();
                FakeFontAtlasPageArtifact artifact = FakeFontAtlasPageArtifactWriter.Write(pagePath, options.AtlasName, page, glyphs, options.Overwrite);
                pagePaths.Add(pagePath);
                pages.Add(new FontAtlasPage(page.Index, fileName, page.Width, page.Height, artifact.ContentHash));
            }

            FontAtlasSnapshot exportSnapshot = new(snapshot.Version, pages, snapshot.Glyphs);
            FontAtlasTomlDocument document = FontAtlasTomlConversion.FromSnapshot(exportSnapshot, metadata with { Name = options.AtlasName });
            string text = FontAtlasTomlWriter.Write(document);
            File.WriteAllText(tomlPath, text);
            return new FontAtlasArtifactExportResult(true, tomlPath, pagePaths, document, diagnostics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.InvalidValue, ex.Message, tomlPath));
            return new FontAtlasArtifactExportResult(false, tomlPath, pagePaths, null, diagnostics);
        }
    }
}
