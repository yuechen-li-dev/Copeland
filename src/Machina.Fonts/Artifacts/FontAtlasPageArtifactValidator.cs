using System.Globalization;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Artifacts;

public static class FontAtlasPageArtifactValidator
{
    public static IReadOnlyList<FontAtlasTomlDiagnostic> Validate(FontAtlasTomlDocument document, string tomlPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<FontAtlasTomlDiagnostic> diagnostics = [];
        string directory = Path.GetDirectoryName(Path.GetFullPath(tomlPath)) ?? Directory.GetCurrentDirectory();

        foreach (FontAtlasPageToml page in document.Pages.OrderBy(page => page.Index))
        {
            string path = Path.Combine(directory, page.Image);
            string keyPath = "page[" + page.Index + "]";
            if (!File.Exists(path))
            {
                diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.ImageMissing, "Page artifact file is missing.", path, KeyPath: keyPath + ".image"));
                continue;
            }

            string actualHash = FakeFontAtlasPageArtifactWriter.ComputeFileSha256(path);
            if (!StringComparer.Ordinal.Equals(actualHash, page.ContentHash))
            {
                diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.ContentHashMismatch, "Page artifact content hash does not match TOML content_hash.", path, KeyPath: keyPath + ".content_hash"));
            }

            ValidateFakePageFields(page, path, keyPath, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateFakePageFields(FontAtlasPageToml page, string path, string keyPath, List<FontAtlasTomlDiagnostic> diagnostics)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0] != FakeFontAtlasPageArtifactWriter.Header)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.InvalidPageArtifact, "Fake page artifact header is invalid.", path, KeyPath: keyPath));
            return;
        }

        Dictionary<string, string> fields = [];
        foreach (string line in lines.Skip(1))
        {
            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.InvalidPageArtifact, "Fake page artifact field is invalid.", path, KeyPath: keyPath));
                return;
            }

            fields[line[..separator]] = line[(separator + 1)..];
        }

        if (!TryInt(fields, "page", out int index) || index != page.Index)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.InvalidValue, "Fake page index does not match TOML page index.", path, KeyPath: keyPath + ".index"));
        }

        bool widthValid = TryInt(fields, "width", out int width);
        bool heightValid = TryInt(fields, "height", out int height);
        if (!widthValid || !heightValid || width != page.Width || height != page.Height)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.PageDimensionMismatch, "Fake page dimensions do not match TOML page dimensions.", path, KeyPath: keyPath));
        }
    }

    private static bool TryInt(Dictionary<string, string> fields, string key, out int value)
    {
        value = 0;
        return fields.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
