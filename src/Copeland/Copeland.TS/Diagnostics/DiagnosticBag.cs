namespace Copeland.TS.Diagnostics;

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public void Report(string id, string message, int position, int length)
    {
        _diagnostics.Add(new Diagnostic(id, message, position, length));
    }
}
