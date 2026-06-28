using System.Globalization;
using Machina.Fonts.Artifacts.DistanceField;
using Machina.Fonts.Generation;
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

            ValidatePageFields(document, page, path, keyPath, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidatePageFields(FontAtlasTomlDocument document, FontAtlasPageToml page, string path, string keyPath, List<FontAtlasTomlDiagnostic> diagnostics)
    {
        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".dfpage", StringComparison.OrdinalIgnoreCase))
        {
            ValidateDistanceFieldPageFields(document, page, path, keyPath, diagnostics);
            return;
        }

        ValidateFakePageFields(page, path, keyPath, diagnostics);
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

    private static void ValidateDistanceFieldPageFields(FontAtlasTomlDocument document, FontAtlasPageToml page, string path, string keyPath, List<FontAtlasTomlDiagnostic> diagnostics)
    {
        if (!DistanceFieldPageArtifactReader.TryRead(path, out DistanceFieldPageArtifactDocument? artifact, out string? error))
        {
            FontAtlasTomlDiagnosticCode code = error is not null && error.Contains("data length", StringComparison.OrdinalIgnoreCase)
                ? FontAtlasTomlDiagnosticCode.DataLengthMismatch
                : FontAtlasTomlDiagnosticCode.InvalidPageArtifact;
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                code,
                error ?? "DF page artifact is invalid.",
                path,
                KeyPath: keyPath));
            return;
        }

        if (artifact!.PageIndex != page.Index)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                FontAtlasTomlDiagnosticCode.PageIndexMismatch,
                "DF page index does not match TOML page index.",
                path,
                KeyPath: keyPath + ".index"));
        }

        if (artifact.Width != page.Width || artifact.Height != page.Height)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                FontAtlasTomlDiagnosticCode.PageDimensionMismatch,
                "DF page dimensions do not match TOML page dimensions.",
                path,
                KeyPath: keyPath));
        }

        if (!FontAtlasTomlValidator.TryParseDistanceField(document.Atlas.DistanceField, out DistanceFieldKind kind))
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                FontAtlasTomlDiagnosticCode.InvalidValue,
                "TOML atlas distance_field is invalid for DF page validation.",
                path,
                KeyPath: "atlas.distance_field"));
            return;
        }

        string expectedKind = document.Atlas.DistanceField.Trim().ToLowerInvariant();
        if (!string.Equals(artifact.DistanceField, expectedKind, StringComparison.Ordinal))
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                FontAtlasTomlDiagnosticCode.InvalidPageArtifact,
                "DF page kind does not match TOML atlas distance_field.",
                path,
                KeyPath: keyPath));
        }

        int expectedChannelCount = FakeDistanceFieldValidation.GetChannelCount(kind);
        if (artifact.ChannelCount != expectedChannelCount)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                FontAtlasTomlDiagnosticCode.PageChannelMismatch,
                "DF page channel count does not match the atlas distance field kind.",
                path,
                KeyPath: keyPath));
        }

        int expectedDataLength = checked(page.Width * page.Height * expectedChannelCount);
        if (artifact.Data.Length != expectedDataLength)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(
                FontAtlasTomlDiagnosticSeverity.Error,
                FontAtlasTomlDiagnosticCode.DataLengthMismatch,
                "DF page data length does not match page dimensions and channel count.",
                path,
                KeyPath: keyPath));
        }
    }

    private static bool TryInt(Dictionary<string, string> fields, string key, out int value)
    {
        value = 0;
        return fields.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
