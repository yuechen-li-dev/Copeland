using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Lowering;

public static class MirLowerer
{
    public static MirCompilation Lower(SyntaxTree tree)
    {
        var bound = Binder.Bind(tree);
        return Lower(bound);
    }

    public static MirCompilation Lower(BoundCompilation bound)
    {
        if (bound.Diagnostics.Any(d => d.Id.StartsWith("COPE-", StringComparison.Ordinal)))
            return new MirCompilation(null, bound.Diagnostics);

        return new MirCompilation(LowerProgram(bound.Program), bound.Diagnostics);
    }

    public static MirProgram LowerProgram(BoundProgram program)
    {
        var enums = program.Enums.Select(LowerEnum).ToArray();
        var functions = program.Functions.Select(LowerFunction).ToArray();
        return new MirProgram(enums, functions);
    }

    private static MirEnum LowerEnum(BoundEnumDeclaration declaration)
        => new(
            declaration.EnumType.Name,
            declaration.EnumType.Cases.Select(@case =>
                new MirEnumCase(
                    @case.Name,
                    @case.PayloadFields.Select(field => new MirEnumPayloadField(field.Name, ToMirType(field.Type))).ToArray()))
                .ToArray());

    private static MirFunction LowerFunction(BoundFunctionDeclaration function)
    {
        var locals = new Dictionary<string, MirLocal>(StringComparer.Ordinal);
        var body = LowerStatements(function.Body.Statements, locals);
        return new MirFunction(
            function.Symbol.Name,
            function.Symbol.Parameters.Select(p => new MirParameter(p.Name, ToMirType(p.Type))).ToArray(),
            ToMirType(function.Symbol.ReturnType),
            function.Symbol.ErrorType is null ? null : ToMirType(function.Symbol.ErrorType),
            locals.Values.OrderBy(l => l.Name, StringComparer.Ordinal).ToArray(),
            body);
    }

    private static IReadOnlyList<MirStatement> LowerStatements(IReadOnlyList<BoundStatement> statements, Dictionary<string, MirLocal> locals)
        => statements.SelectMany(s => LowerStatement(s, locals)).ToArray();

    private static IReadOnlyList<MirStatement> LowerStatement(BoundStatement statement, Dictionary<string, MirLocal> locals)
    {
        return statement switch
        {
            BoundBlockStatement b => LowerStatements(b.Statements, locals),
            BoundVariableDeclaration v => [LowerVariable(v, locals)],
            BoundExpressionStatement e => [new MirExpressionStatement(LowerExpression(e.Expression))],
            BoundReturnStatement r => [new MirReturnStatement(r.Expression is null ? null : LowerExpression(r.Expression))],
            BoundIfStatement i => [new MirIfStatement(LowerExpression(i.Condition), LowerStatement(i.ThenStatement, locals), i.ElseStatement is null ? null : LowerStatement(i.ElseStatement, locals))],
            BoundWhileStatement w => [new MirWhileStatement(LowerExpression(w.Condition), LowerStatement(w.Body, locals))],
            BoundForStatement f => [new MirForStatement(f.Initializer is null ? null : LowerStatement(f.Initializer, locals).Single(), f.Condition is null ? null : LowerExpression(f.Condition), f.Increment is null ? null : LowerExpression(f.Increment), LowerStatement(f.Body, locals))],
            _ => []
        };
    }

    private static MirStatement LowerVariable(BoundVariableDeclaration v, Dictionary<string, MirLocal> locals)
    {
        var local = new MirLocal(v.Variable.Name, ToMirType(v.Variable.Type), v.Variable.IsReadOnly);
        locals.TryAdd(local.Name, local);
        return new MirVariableDeclarationStatement(local, LowerExpression(v.Initializer));
    }

    private static MirExpression LowerExpression(BoundExpression expression)
        => expression switch
        {
            BoundLiteralExpression l => new MirLiteralExpression(l.Value, ToMirType(l.Type)),
            BoundVariableExpression v => new MirVariableExpression(v.Variable.Name, ToMirType(v.Type)),
            BoundAssignmentExpression a => new MirAssignmentExpression(a.Variable.Name, LowerExpression(a.Expression), ToMirType(a.Type)),
            BoundUnaryExpression u => new MirUnaryExpression(OperatorName(u.OperatorKind), LowerExpression(u.Operand), ToMirType(u.Type)),
            BoundBinaryExpression b => new MirBinaryExpression(OperatorName(b.OperatorKind), LowerExpression(b.Left), LowerExpression(b.Right), ToMirType(b.Type)),
            BoundCallExpression c => new MirCallExpression(c.Function.Name, c.Arguments.Select(LowerExpression).ToArray(), ToMirType(c.Type), c.IsFallible, c.ErrorType is null ? null : ToMirType(c.ErrorType), false),
            BoundEnumValueExpression e => new MirEnumValueExpression(e.Case.EnumType.Name, e.Case.Name, e.Arguments.Select(LowerExpression).ToArray(), ToMirType(e.Type)),
            BoundMatchExpression m => new MirMatchExpression(LowerExpression(m.Scrutinee), m.Arms.Select(arm => new MirMatchArm(arm.Case.Name, arm.PayloadVariables.Select(v => new MirMatchPayloadBinding(v.Name, ToMirType(v.Type))).ToArray(), LowerExpression(arm.Expression))).ToArray(), ToMirType(m.Type)),
            BoundIfExpression i => new MirIfExpression(LowerExpression(i.Condition), LowerExpression(i.ThenExpression), LowerExpression(i.ElseExpression), ToMirType(i.Type)),
            BoundPropagateExpression p when p.Operand is BoundCallExpression c => new MirCallExpression(c.Function.Name, c.Arguments.Select(LowerExpression).ToArray(), ToMirType(c.Type), c.IsFallible, c.ErrorType is null ? null : ToMirType(c.ErrorType), true),
            BoundPropagateExpression p => LowerExpression(p.Operand),
            BoundArrayExpression a => new MirArrayExpression(a.Elements.Select(LowerExpression).ToArray(), ToMirType(a.Type)),
            _ => new MirLiteralExpression("<error>", new MirType("error"))
        };

    private static MirType ToMirType(TypeSymbol type) => new(type.Name);

    private static string OperatorName(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PlusToken => "+",
        SyntaxKind.MinusToken => "-",
        SyntaxKind.StarToken => "*",
        SyntaxKind.SlashToken => "/",
        SyntaxKind.PercentToken => "%",
        SyntaxKind.BangToken => "!",
        SyntaxKind.LessToken => "<",
        SyntaxKind.LessOrEqualsToken => "<=",
        SyntaxKind.GreaterToken => ">",
        SyntaxKind.GreaterOrEqualsToken => ">=",
        SyntaxKind.EqualsEqualsToken => "==",
        SyntaxKind.BangEqualsToken => "!=",
        SyntaxKind.EqualsEqualsEqualsToken => "===",
        SyntaxKind.BangEqualsEqualsToken => "!==",
        SyntaxKind.AmpersandAmpersandToken => "&&",
        SyntaxKind.PipePipeToken => "||",
        _ => kind.ToString()
    };
}
