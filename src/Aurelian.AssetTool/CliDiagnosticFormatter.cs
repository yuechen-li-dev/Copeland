using System.Text.Json;
using Aurelian.Assets;

namespace Aurelian.AssetTool;

public enum DiagnosticFormat
{
    Text,
    Json,
    Jsonl
}

public static class CliDiagnosticFormatter
{
    public static string FormatDiagnostic(AssetDiagnostic diagnostic, DiagnosticFormat format)
    {
        return format switch
        {
            DiagnosticFormat.Text =>
                $"{diagnostic.Severity.ToUpperInvariant()} {diagnostic.Code} {diagnostic.SourcePath}:{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}",
            DiagnosticFormat.Jsonl or DiagnosticFormat.Json => JsonSerializer.Serialize(ToRecord(diagnostic)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported diagnostic format.")
        };
    }


    public static string FormatArtifactRecord(string shaderId, string manifestPath)
        => JsonSerializer.Serialize(new
        {
            kind = "artifact",
            id = shaderId,
            manifestPath,
            fatal = false
        });

    public static object ToRecord(AssetDiagnostic diagnostic)
        => new
        {
            severity = diagnostic.Severity,
            code = diagnostic.Code,
            message = diagnostic.Message,
            sourcePath = diagnostic.SourcePath,
            line = diagnostic.Line,
            column = diagnostic.Column,
            fatal = diagnostic.Fatal
        };
}
