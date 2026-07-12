using Tomlyn;

namespace Machina.Fonts.Toml;

public static class FontAtlasTomlLoader
{
    public static FontAtlasTomlLoadResult LoadString(string text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<FontAtlasTomlDiagnostic> diagnostics = [];
        FontAtlasTomlDocument? document;

        try
        {
            document = TomlSerializer.Deserialize<FontAtlasTomlDocument>(text, new TomlSerializerOptions());
        }
        catch (TomlException ex)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.ParseError, ex.Message, path));
            return new FontAtlasTomlLoadResult(false, null, null, diagnostics);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.BindError, ex.Message, path));
            return new FontAtlasTomlLoadResult(false, null, null, diagnostics);
        }

        if (document is null)
        {
            diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.BindError, "TOML document did not bind to a font atlas document.", path));
            return new FontAtlasTomlLoadResult(false, null, null, diagnostics);
        }

        diagnostics.AddRange(FontAtlasTomlValidator.Validate(document, path));
        FontAtlasSnapshot? snapshot = null;
        if (!diagnostics.Any(diagnostic => diagnostic.Severity == FontAtlasTomlDiagnosticSeverity.Error))
        {
            try
            {
                snapshot = FontAtlasTomlConversion.ToSnapshot(document);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                diagnostics.Add(new FontAtlasTomlDiagnostic(FontAtlasTomlDiagnosticSeverity.Error, FontAtlasTomlDiagnosticCode.BindError, ex.Message, path));
            }
        }

        bool success = snapshot is not null && !diagnostics.Any(diagnostic => diagnostic.Severity == FontAtlasTomlDiagnosticSeverity.Error);
        return new FontAtlasTomlLoadResult(success, document, snapshot, diagnostics);
    }

    public static FontAtlasTomlLoadResult LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadString(File.ReadAllText(path), path);
    }
}
