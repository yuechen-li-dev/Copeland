namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvCompileDecl(
    SdslvPath GenericShader,
    IReadOnlyList<SdslvTypeRef> TypeArguments,
    string Alias) : SdslvDecl;
