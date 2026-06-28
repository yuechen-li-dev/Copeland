namespace Machina.Fonts.Toml;

public enum FontAtlasTomlDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public enum FontAtlasTomlDiagnosticCode
{
    ParseError,
    BindError,
    MissingRequiredField,
    UnsupportedFormat,
    InvalidKind,
    InvalidValue,
    DuplicatePage,
    DuplicateGlyph,
    MissingPage,
    GlyphOutOfBounds,
    InvalidGlyphKey,
    CharCodepointMismatch,
    UvMismatch,
    HashMissing,
    ImageMissing,
    PageDimensionMismatch,
    ContentHashMismatch,
    InvalidPageArtifact,
}

public sealed record FontAtlasTomlDiagnostic(
    FontAtlasTomlDiagnosticSeverity Severity,
    FontAtlasTomlDiagnosticCode Code,
    string Message,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? KeyPath = null);
