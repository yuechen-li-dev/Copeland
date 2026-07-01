namespace Aurelian.Shaders.Language.Ast;

public abstract record SdslvDecl;

public sealed record SdslvTypeAliasDecl(
    string Name,
    SdslvTypeRef TargetType,
    SdslvPath? SpaceAnnotation = null) : SdslvDecl;
