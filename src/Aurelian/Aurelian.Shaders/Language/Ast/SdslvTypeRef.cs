namespace Aurelian.Shaders.Language.Ast;

public abstract record SdslvTypeRef
{
    public abstract string ToDisplayString();
}

public sealed record SdslvNamedTypeRef(SdslvPath Path) : SdslvTypeRef
{
    public override string ToDisplayString() => Path.ToString();
}

public sealed record SdslvArrayTypeRef(SdslvTypeRef Element, int Length, SdslvSpan Span = default) : SdslvTypeRef
{
    public override string ToDisplayString() => $"array<{Element.ToDisplayString()}, {Length}>";
}
