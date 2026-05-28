namespace Machina.Standard.Text;

public enum MachinaTextDiagnosticLevel
{
    Error,
    Warning,
}

public enum MachinaTextDiagnosticCode
{
    UnsupportedSyntax,
    HeadingForbidden,
    MaxListDepthExceeded,
    MalformedLink,
    UnclosedInline,
    InvalidEscape,
}

public sealed record MachinaTextDiagnostic
{
    public MachinaTextDiagnostic(
        MachinaTextDiagnosticCode code,
        string message,
        int index,
        int length,
        int line,
        int column,
        MachinaTextDiagnosticLevel level)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Diagnostic index must be non-negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Diagnostic length must be non-negative.");
        }

        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line), line, "Diagnostic line must be 1-based.");
        }

        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Diagnostic column must be 1-based.");
        }

        Code = code;
        Message = message;
        Index = index;
        Length = length;
        Line = line;
        Column = column;
        Level = level;
    }

    public MachinaTextDiagnosticCode Code { get; }

    public string Message { get; }

    public int Index { get; }

    public int Length { get; }

    public int Line { get; }

    public int Column { get; }

    public MachinaTextDiagnosticLevel Level { get; }
}

public sealed record ParseMachinaTextResult
{
    public ParseMachinaTextResult(
        bool ok,
        MachinaTextDocument document,
        IReadOnlyList<MachinaTextDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (diagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics cannot contain null items.", nameof(diagnostics));
        }

        Document = document;
        Diagnostics = diagnostics.ToArray();
        Ok = Diagnostics.All(diagnostic => diagnostic.Level != MachinaTextDiagnosticLevel.Error);
    }

    public bool Ok { get; }

    public MachinaTextDocument Document { get; }

    public IReadOnlyList<MachinaTextDiagnostic> Diagnostics { get; }
}
