using System.Globalization;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

internal static class GpuUtilityChoice
{
    // Keep guard, score and result expressions inside the helper. Capturing the
    // evaluated expressions would make ineligible scores and losing values eager.
    public static VdMirExpression Bind(WhenUtilityExpressionSyntax syntax, string helperName, string? expected,
        Func<SyntaxNode, VdMirSourceSpan> span,
        Func<ExpressionSyntax, string?, VdMirExpression> bindExpression,
        Func<string, bool> nameInScope,
        Func<IReadOnlySet<string>, IReadOnlyList<VdMirParameter>> capture,
        Func<VdMirExpression, VdMirExpression, VdMirStatement> assign,
        Action<VdMirFunction> addFunction,
        Action<VdMirSourceSpan> invalidCaseCount,
        Action<ExpressionSyntax, string, string> typeMismatch)
    {
        VdMirSourceSpan source = span(syntax);
        if (syntax.Cases.Count is < 1 or > 16)
        {
            invalidCaseCount(source);
            return new("error", "error", source);
        }
        string winner = Fresh("utilityWinner");
        string best = Fresh("utilityBest");
        string scoreName = Fresh("utilityScore");
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        VdMirExpression fallback = Bind(syntax.Fallback, expected);
        string resultType = expected ?? fallback.Type;
        Check(syntax.Fallback, fallback, resultType);
        var statements = new List<VdMirStatement>
        {
            new("local", source, winner, "u32", true, Literal(0)),
            new("local", source, best, "u32", true, Literal(0)),
        };
        var values = new List<VdMirExpression>();
        for (int index = 0; index < syntax.Cases.Count; index++)
        {
            UtilityCaseSyntax candidate = syntax.Cases[index];
            VdMirExpression condition = Bind(candidate.Condition, "bool");
            VdMirExpression score = Bind(candidate.Score, "u32");
            VdMirExpression value = Bind(candidate.Value, resultType);
            Check(candidate.Condition, condition, "bool");
            Check(candidate.Score, score, "u32");
            Check(candidate.Value, value, resultType);
            values.Add(value);
            VdMirExpression wins = Binary("||", Binary("==", Name(winner, "u32"), Literal(0)),
                Binary(">", Name(scoreName, "u32"), Name(best, "u32")));
            statements.Add(new("if", span(candidate), Expression: condition, Body: [
                new("local", source, scoreName, "u32", false, score),
                new("if", source, Expression: wins, Body: [
                    assign(Name(best, "u32"), Name(scoreName, "u32")),
                    assign(Name(winner, "u32"), Literal((uint)index + 1)),
                ]),
            ]));
        }
        for (int index = 0; index < values.Count; index++)
        {
            statements.Add(new("if", source,
                Expression: Binary("==", Name(winner, "u32"), Literal((uint)index + 1)),
                Body: [new("return", source, Expression: values[index])]));
        }
        statements.Add(new("return", source, Expression: fallback));
        IReadOnlyList<VdMirParameter> parameters = capture(usedNames);
        addFunction(new(helperName, parameters, resultType, statements, source));
        return new("call", resultType, source, helperName,
            parameters.Select(parameter => Name(parameter.Name, parameter.Type)).ToArray());

        string Fresh(string stem)
        {
            while (nameInScope(stem)) { stem += "_"; }
            return stem;
        }

        VdMirExpression Bind(ExpressionSyntax expression, string? type)
        {
            VdMirExpression value = bindExpression(expression, type);
            FindNames(value, usedNames);
            return value;
        }

        void Check(ExpressionSyntax expression, VdMirExpression value, string type)
        {
            if (value.Type != type) { typeMismatch(expression, type, value.Type); }
        }

        VdMirExpression Name(string name, string type) => new("name", type, source, name);
        VdMirExpression Literal(uint value) => new("literal", "u32", source, value.ToString(CultureInfo.InvariantCulture));
        VdMirExpression Binary(string op, VdMirExpression a, VdMirExpression b) => new("binary", "bool", source, op, [a, b]);
    }

    private static void FindNames(VdMirExpression expression, HashSet<string> names)
    {
        if (expression.Kind == "name") { names.Add(expression.Value!); }
        foreach (var operand in expression.Operands ?? []) { FindNames(operand, names); }
    }
}
