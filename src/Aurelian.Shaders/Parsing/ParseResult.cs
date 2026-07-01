using Aurelian.Shaders.Diagnostics;

namespace Aurelian.Shaders.Parsing;

public sealed record ParseResult<T>(T? Document, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0 && Document is not null;
}
