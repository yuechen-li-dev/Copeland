namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvEnumDecl(
    string Name,
    IReadOnlyList<SdslvEnumVariant> Variants,
    SdslvSpan Span = default) : SdslvDecl;

public sealed record SdslvEnumVariant(string Name, SdslvSpan Span = default);
