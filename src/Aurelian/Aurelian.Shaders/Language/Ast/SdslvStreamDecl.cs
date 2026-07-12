namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvStreamDecl(
    string Name,
    IReadOnlyList<SdslvFieldDecl> Fields) : SdslvDecl;
