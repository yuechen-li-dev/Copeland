using Copeland.Script.Diagnostics;

namespace Copeland.Script.Codegen.CSharp;

public sealed class CSharpCompilation
{
    public CSharpCompilation(string sourceText, IReadOnlyList<Diagnostic> diagnostics)
    {
        SourceText = sourceText;
        Diagnostics = diagnostics;
    }

    public string SourceText { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
