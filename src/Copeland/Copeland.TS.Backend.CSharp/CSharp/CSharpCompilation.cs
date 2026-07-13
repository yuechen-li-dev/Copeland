namespace Copeland.TS.Backend.CSharp;

public sealed class CSharpCompilation(string sourceText, IReadOnlyList<CSharpDiagnostic> diagnostics)
{
    public string SourceText { get; } = sourceText;

    public IReadOnlyList<CSharpDiagnostic> Diagnostics { get; } = diagnostics;
}

public sealed record CSharpDiagnostic(string Id, string Message);
