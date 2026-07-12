namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvShaderDecl(
    string Name,
    IReadOnlyList<string> GenericParameters,
    IReadOnlyList<SdslvPath> Implements,
    IReadOnlyList<SdslvWhereConstraint> Constraints,
    IReadOnlyList<SdslvFieldDecl> MaterialFields,
    IReadOnlyList<SdslvFunctionDecl> Methods,
    IReadOnlyList<SdslvFunctionDecl> StageMethods) : SdslvDecl;

public sealed record SdslvWhereConstraint(
    string ParameterName,
    IReadOnlyList<SdslvPath> Bounds);
