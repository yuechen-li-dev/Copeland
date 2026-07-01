namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvPath(IReadOnlyList<string> Segments)
{
    public SdslvPath(params string[] segments)
        : this((IReadOnlyList<string>)segments)
    {
    }

    public override string ToString() => string.Join('.', Segments);
}
