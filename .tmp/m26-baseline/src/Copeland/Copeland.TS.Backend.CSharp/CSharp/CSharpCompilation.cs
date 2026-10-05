namespace Copeland.TS.Backend.CSharp;

public sealed class CSharpCompilation(
    string sourceText,
    IReadOnlyList<CSharpDiagnostic> diagnostics,
    CSharpSidecarContract? sidecarContract = null)
{
    public string SourceText { get; } = sourceText;

    public IReadOnlyList<CSharpDiagnostic> Diagnostics { get; } = diagnostics;

    public CSharpSidecarContract? SidecarContract { get; } = sidecarContract;
}

public sealed record CSharpDiagnostic(string Id, string Message);
