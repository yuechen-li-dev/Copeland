using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics.Bound;

public abstract class BoundNode;
public abstract class BoundStatement : BoundNode;
public abstract class BoundExpression : BoundNode { public abstract TypeSymbol Type { get; } }

public sealed class BoundProgram
{
    public BoundProgram(IReadOnlyList<BoundFunctionDeclaration> functions, IReadOnlyList<BoundEnumDeclaration> enums, IReadOnlyList<BoundStatement> globalStatements) { Functions = functions; Enums = enums; GlobalStatements = globalStatements; }
    public IReadOnlyList<BoundFunctionDeclaration> Functions { get; }
    public IReadOnlyList<BoundEnumDeclaration> Enums { get; }
    public IReadOnlyList<BoundStatement> GlobalStatements { get; }
}
public sealed class BoundCompilation
{
    public BoundCompilation(SyntaxTree syntaxTree, BoundProgram program, IReadOnlyList<Diagnostics.Diagnostic> diagnostics) { SyntaxTree = syntaxTree; Program = program; Diagnostics = diagnostics; }
    public SyntaxTree SyntaxTree { get; }
    public BoundProgram Program { get; }
    public IReadOnlyList<Diagnostics.Diagnostic> Diagnostics { get; }
}

public sealed class BoundFunctionDeclaration : BoundNode { public BoundFunctionDeclaration(FunctionSymbol symbol, BoundBlockStatement body) { Symbol = symbol; Body = body; } public FunctionSymbol Symbol { get; } public BoundBlockStatement Body { get; } }
public sealed class BoundEnumDeclaration : BoundNode { public BoundEnumDeclaration(EnumTypeSymbol enumType) => EnumType = enumType; public EnumTypeSymbol EnumType { get; } }
public sealed class BoundBlockStatement : BoundStatement { public BoundBlockStatement(IReadOnlyList<BoundStatement> statements) => Statements = statements; public IReadOnlyList<BoundStatement> Statements { get; } }
public sealed class BoundVariableDeclaration : BoundStatement { public BoundVariableDeclaration(VariableSymbol variable, BoundExpression initializer) { Variable = variable; Initializer = initializer; } public VariableSymbol Variable { get; } public BoundExpression Initializer { get; } }
public sealed class BoundExpressionStatement : BoundStatement { public BoundExpressionStatement(BoundExpression expression) => Expression = expression; public BoundExpression Expression { get; } }
public sealed class BoundIfStatement : BoundStatement { public BoundIfStatement(BoundExpression condition, BoundStatement thenStatement, BoundStatement? elseStatement) { Condition = condition; ThenStatement = thenStatement; ElseStatement = elseStatement; } public BoundExpression Condition { get; } public BoundStatement ThenStatement { get; } public BoundStatement? ElseStatement { get; } }
public sealed class BoundWhileStatement : BoundStatement { public BoundWhileStatement(BoundExpression condition, BoundStatement body) { Condition = condition; Body = body; } public BoundExpression Condition { get; } public BoundStatement Body { get; } }
public sealed class BoundForStatement : BoundStatement { public BoundForStatement(BoundStatement? initializer, BoundExpression? condition, BoundExpression? increment, BoundStatement body) { Initializer = initializer; Condition = condition; Increment = increment; Body = body; } public BoundStatement? Initializer { get; } public BoundExpression? Condition { get; } public BoundExpression? Increment { get; } public BoundStatement Body { get; } }
public sealed class BoundReturnStatement : BoundStatement { public BoundReturnStatement(BoundExpression? expression) => Expression = expression; public BoundExpression? Expression { get; } }

public sealed class BoundLiteralExpression : BoundExpression { public BoundLiteralExpression(object? value, TypeSymbol type) { Value = value; TypeImpl = type; } public object? Value { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundVariableExpression : BoundExpression { public BoundVariableExpression(VariableSymbol variable) => Variable = variable; public VariableSymbol Variable { get; } public override TypeSymbol Type => Variable.Type; }
public sealed class BoundAssignmentExpression : BoundExpression { public BoundAssignmentExpression(VariableSymbol variable, BoundExpression expression) { Variable = variable; Expression = expression; } public VariableSymbol Variable { get; } public BoundExpression Expression { get; } public override TypeSymbol Type => Expression.Type; }
public sealed class BoundUnaryExpression : BoundExpression { public BoundUnaryExpression(SyntaxKind op, BoundExpression operand, TypeSymbol type) { OperatorKind = op; Operand = operand; TypeImpl = type; } public SyntaxKind OperatorKind { get; } public BoundExpression Operand { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundBinaryExpression : BoundExpression { public BoundBinaryExpression(BoundExpression left, SyntaxKind op, BoundExpression right, TypeSymbol type) { Left = left; OperatorKind = op; Right = right; TypeImpl = type; } public BoundExpression Left { get; } public SyntaxKind OperatorKind { get; } public BoundExpression Right { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundCallExpression : BoundExpression { public BoundCallExpression(FunctionSymbol function, IReadOnlyList<BoundExpression> arguments) { Function = function; Arguments = arguments; } public FunctionSymbol Function { get; } public IReadOnlyList<BoundExpression> Arguments { get; } public override TypeSymbol Type => Function.ReturnType; }
public sealed class BoundEnumValueExpression : BoundExpression
{
    public BoundEnumValueExpression(EnumCaseSymbol @case, IReadOnlyList<BoundExpression> arguments)
    {
        Case = @case;
        Arguments = arguments;
    }
    public EnumCaseSymbol Case { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public bool IsConstructor => Arguments.Count > 0;
    public override TypeSymbol Type => Case.EnumType;
}
public enum BoundPropagationTarget { FunctionReturn }
public sealed class BoundPropagateExpression : BoundExpression
{
    public BoundPropagateExpression(BoundExpression operand, ResultTypeSymbol resultType, BoundPropagationTarget target)
    {
        Operand = operand;
        ResultType = resultType;
        Target = target;
    }

    public BoundExpression Operand { get; }
    public ResultTypeSymbol ResultType { get; }
    public BoundPropagationTarget Target { get; }
    public override TypeSymbol Type => ResultType.SuccessType;
}
public sealed class BoundOkExpression : BoundExpression { public BoundOkExpression(BoundExpression payload, ResultTypeSymbol type) { Payload = payload; TypeImpl = type; } public BoundExpression Payload { get; } private ResultTypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundErrExpression : BoundExpression { public BoundErrExpression(BoundExpression payload, ResultTypeSymbol type) { Payload = payload; TypeImpl = type; } public BoundExpression Payload { get; } private ResultTypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundUnitExpression : BoundExpression { public override TypeSymbol Type => PrimitiveTypeSymbol.Void; }
public sealed class BoundMatchArm
{
    public BoundMatchArm(EnumCaseSymbol @case, IReadOnlyList<VariableSymbol> payloadVariables, BoundExpression expression)
    {
        Case = @case;
        PayloadVariables = payloadVariables;
        Expression = expression;
    }
    public EnumCaseSymbol Case { get; }
    public IReadOnlyList<VariableSymbol> PayloadVariables { get; }
    public BoundExpression Expression { get; }
}
public sealed class BoundIfExpression : BoundExpression { public BoundIfExpression(BoundExpression condition, BoundExpression thenExpression, BoundExpression elseExpression, TypeSymbol type) { Condition = condition; ThenExpression = thenExpression; ElseExpression = elseExpression; TypeImpl = type; } public BoundExpression Condition { get; } public BoundExpression ThenExpression { get; } public BoundExpression ElseExpression { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundMatchExpression : BoundExpression
{
    public BoundMatchExpression(BoundExpression scrutinee, EnumTypeSymbol enumType, IReadOnlyList<BoundMatchArm> arms, TypeSymbol type)
    {
        Scrutinee = scrutinee;
        EnumType = enumType;
        Arms = arms;
        TypeImpl = type;
    }
    public BoundExpression Scrutinee { get; }
    public EnumTypeSymbol EnumType { get; }
    public IReadOnlyList<BoundMatchArm> Arms { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundResultMatchExpression : BoundExpression
{
    public BoundResultMatchExpression(BoundExpression scrutinee, VariableSymbol okVariable, BoundExpression okExpression, VariableSymbol errVariable, BoundExpression errExpression, TypeSymbol type)
    {
        Scrutinee = scrutinee;
        OkVariable = okVariable;
        OkExpression = okExpression;
        ErrVariable = errVariable;
        ErrExpression = errExpression;
        TypeImpl = type;
    }

    public BoundExpression Scrutinee { get; }
    public VariableSymbol OkVariable { get; }
    public BoundExpression OkExpression { get; }
    public VariableSymbol ErrVariable { get; }
    public BoundExpression ErrExpression { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundArrayExpression : BoundExpression { public BoundArrayExpression(IReadOnlyList<BoundExpression> elements, TypeSymbol type) { Elements = elements; TypeImpl = type; } public IReadOnlyList<BoundExpression> Elements { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundErrorExpression : BoundExpression { public override TypeSymbol Type => PrimitiveTypeSymbol.Error; }
