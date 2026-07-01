namespace Aurelian.Shaders.Language.Ast;

public abstract record SdslvStatement;

public sealed record SdslvLetStatement(
    string Name,
    SdslvTypeRef TypeName,
    SdslvExpression? Initializer = null) : SdslvStatement;

public sealed record SdslvAssignStatement(
    SdslvExpression Target,
    SdslvExpression Value) : SdslvStatement;

public sealed record SdslvReturnStatement(SdslvExpression Value) : SdslvStatement;

public sealed record SdslvIfStatement(
    SdslvExpression Condition,
    IReadOnlyList<SdslvStatement> ThenBody,
    IReadOnlyList<SdslvStatement>? ElseBody = null,
    SdslvSpan Span = default) : SdslvStatement;

public sealed record SdslvForStatement(
    string Iterator,
    SdslvExpression Start,
    SdslvExpression End,
    SdslvExpression? Step,
    IReadOnlyList<SdslvStatement> Body,
    SdslvSpan Span = default) : SdslvStatement;

public sealed record SdslvExpressionStatement(SdslvExpression Value) : SdslvStatement;

public sealed record SdslvEmptyStatement : SdslvStatement;
