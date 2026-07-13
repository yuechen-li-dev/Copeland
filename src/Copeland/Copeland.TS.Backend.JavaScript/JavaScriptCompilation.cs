namespace Copeland.TS.Backend.JavaScript;

public sealed class JavaScriptCompilation(string? sourceText, IReadOnlyList<JavaScriptDiagnostic> diagnostics)
{
    public string? SourceText { get; } = sourceText;

    public IReadOnlyList<JavaScriptDiagnostic> Diagnostics { get; } = diagnostics;

    public bool Success => SourceText is not null && Diagnostics.Count == 0;
}

public sealed record JavaScriptDiagnostic(string Id, string Message);
