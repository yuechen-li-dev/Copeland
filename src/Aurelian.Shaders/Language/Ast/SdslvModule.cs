namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvModule(
    SdslvPath? Namespace,
    IReadOnlyList<SdslvUseDecl> Uses,
    IReadOnlyList<SdslvDecl> Declarations);

public sealed record SdslvUseDecl(SdslvPath Path);
