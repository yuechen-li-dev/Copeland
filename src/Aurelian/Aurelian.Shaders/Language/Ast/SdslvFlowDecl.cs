namespace Aurelian.Shaders.Language.Ast;

public sealed record SdslvFlowDecl(
    string Name,
    IReadOnlyList<SdslvFunctionParameter> Parameters,
    SdslvTypeRef ReturnType,
    SdslvFlowBoard? Board,
    IReadOnlyList<SdslvFlowState> States,
    SdslvSpan Span = default) : SdslvDecl;

public sealed record SdslvFlowBoard(
    IReadOnlyList<SdslvFlowBoardField> Fields,
    SdslvSpan Span = default);

public sealed record SdslvFlowBoardField(
    string Name,
    SdslvTypeRef TypeName,
    SdslvSpan Span = default);

public sealed record SdslvFlowState(
    string Name,
    IReadOnlyList<SdslvFlowStatement> Statements,
    SdslvSpan Span = default);

public abstract record SdslvFlowStatement;

public sealed record SdslvFlowWhenStatement(SdslvFlowWhen When) : SdslvFlowStatement;
public sealed record SdslvFlowGotoStatement(SdslvPath Target) : SdslvFlowStatement;
public sealed record SdslvFlowReturnStatement(SdslvExpression Value) : SdslvFlowStatement;

public sealed record SdslvFlowBoardAssignStatement(
    string Field,
    SdslvExpression Value,
    SdslvSpan Span = default) : SdslvFlowStatement;

public sealed record SdslvFlowWhen(
    IReadOnlyList<SdslvFlowCase> Cases,
    SdslvFlowAction? ElseAction,
    SdslvSpan Span = default);

public sealed record SdslvFlowCase(
    SdslvExpression Condition,
    SdslvFlowAction Action);

public abstract record SdslvFlowAction;

public sealed record SdslvFlowGotoAction(SdslvPath Target) : SdslvFlowAction;
public sealed record SdslvFlowReturnAction(SdslvExpression Value) : SdslvFlowAction;
