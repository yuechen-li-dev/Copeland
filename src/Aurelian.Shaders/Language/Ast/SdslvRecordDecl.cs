namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvFieldDecl(string Name, SdslvTypeRef TypeName);

public sealed record SdslvRecordDecl(
    string Name,
    IReadOnlyList<SdslvFieldDecl> Fields) : SdslvDecl;
