namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvFunctionDecl(
    bool IsOverride,
    string? Stage,
    string Name,
    IReadOnlyList<SdslvFunctionParameter> Parameters,
    SdslvTypeRef ReturnType,
    SdslvTypeRef? ErrorType,
    SdslvBody? Body);

public sealed record SdslvFunctionParameter(string Name, SdslvTypeRef TypeName);

public sealed record SdslvBody(
    IReadOnlyList<SdslvStatement> Statements,
    SdslvSpan Span = default);
