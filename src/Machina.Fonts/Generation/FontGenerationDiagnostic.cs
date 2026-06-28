namespace Machina.Fonts.Generation;

public enum FontGenerationDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public enum FontGenerationDiagnosticCode
{
    InvalidGlyphKey,
    MissingGlyph,
    UnsupportedGlyph,
    EmptyOutline,
    OutlineLoadFailed,
    DistanceFieldGenerationFailed,
    Cancelled,
    InvalidGenerationSettings,
}

public sealed record FontGenerationDiagnostic(
    FontGenerationDiagnosticSeverity Severity,
    FontGenerationDiagnosticCode Code,
    string Message,
    GlyphKey? Key = null)
{
    public string Message { get; } = string.IsNullOrWhiteSpace(Message)
        ? throw new ArgumentException("Diagnostic message must not be empty.", nameof(Message))
        : Message;
}
