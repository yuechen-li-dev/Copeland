namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvInterfaceDecl(
    string Name,
    IReadOnlyList<SdslvFunctionDecl> Methods) : SdslvDecl;
