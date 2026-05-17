using System.Text;
using Copeland.Script.Syntax;

namespace Copeland.Script.Semantics.Bound;

public static class BoundTreeDumper
{
    public static string Dump(BoundProgram program)
    {
        var sb = new StringBuilder();
        foreach (var fn in program.Functions)
        {
            AppendFunction(sb, fn, 0);
        }
        foreach (var en in program.Enums)
        {
            AppendEnum(sb, en, 0);
        }
        foreach (var stmt in program.GlobalStatements)
        {
            AppendStatement(sb, stmt, 0);
        }
        return sb.ToString();
    }
    private static void AppendEnum(StringBuilder sb, BoundEnumDeclaration en, int i)
    {
        I(sb, i);
        sb.Append("EnumDeclaration ").Append(en.EnumType.Name).AppendLine();
        foreach (var @case in en.EnumType.Cases)
        {
            I(sb, i + 1);
            sb.Append("Case ").Append(@case.Name);
            if (@case.HasPayload)
            {
                sb.Append('(');
                for (var p = 0; p < @case.PayloadFields.Count; p++)
                {
                    if (p > 0) sb.Append(", ");
                    var field = @case.PayloadFields[p];
                    sb.Append(field.Name).Append(": ").Append(field.Type.Name);
                }
                sb.Append(')');
            }
            sb.AppendLine();
        }
    }

    private static void I(StringBuilder sb, int n) => sb.Append(' ', n * 2);
    private static void AppendFunction(StringBuilder sb, BoundFunctionDeclaration fn, int i)
    {
        I(sb, i);
        sb.Append("FunctionDeclaration ").Append(fn.Symbol.Name).Append('(');
        for (var p = 0; p < fn.Symbol.Parameters.Count; p++)
        {
            if (p > 0) sb.Append(", ");
            var param = fn.Symbol.Parameters[p];
            sb.Append(param.Name).Append(": ").Append(param.Type.Name);
        }
        sb.Append(") -> ").Append(fn.Symbol.ReturnType.Name);
        if (fn.Symbol.IsFallible)
        {
            sb.Append(" ! ").Append(fn.Symbol.ErrorType!.Name);
        }
        sb.AppendLine();
        AppendStatement(sb, fn.Body, i + 1);
    }

    private static void AppendStatement(StringBuilder sb, BoundStatement s, int i)
    {
        switch (s)
        {
            case BoundBlockStatement b:
                I(sb, i); sb.AppendLine("BlockStatement");
                foreach (var st in b.Statements) AppendStatement(sb, st, i + 1);
                break;
            case BoundVariableDeclaration v:
                I(sb, i); sb.Append("VariableDeclaration ").Append(v.Variable.IsReadOnly ? "const " : "let ").Append(v.Variable.Name).Append(": ").Append(v.Variable.Type.Name).AppendLine();
                AppendExpression(sb, v.Initializer, i + 1);
                break;
            case BoundExpressionStatement e:
                I(sb, i); sb.AppendLine("ExpressionStatement"); AppendExpression(sb, e.Expression, i + 1); break;
            case BoundIfStatement x:
                I(sb, i); sb.AppendLine("IfStatement"); AppendExpression(sb, x.Condition, i + 1); AppendStatement(sb, x.ThenStatement, i + 1); if (x.ElseStatement is not null) AppendStatement(sb, x.ElseStatement, i + 1); break;
            case BoundWhileStatement w:
                I(sb, i); sb.AppendLine("WhileStatement"); AppendExpression(sb, w.Condition, i + 1); AppendStatement(sb, w.Body, i + 1); break;
            case BoundForStatement f:
                I(sb, i); sb.AppendLine("ForStatement"); if (f.Initializer is not null) AppendStatement(sb, f.Initializer, i + 1); if (f.Condition is not null) AppendExpression(sb, f.Condition, i + 1); if (f.Increment is not null) AppendExpression(sb, f.Increment, i + 1); AppendStatement(sb, f.Body, i + 1); break;
            case BoundReturnStatement r:
                I(sb, i); sb.AppendLine("ReturnStatement"); if (r.Expression is not null) AppendExpression(sb, r.Expression, i + 1); break;
        }
    }

    private static void AppendExpression(StringBuilder sb, BoundExpression e, int i)
    {
        I(sb, i);
        switch (e)
        {
            case BoundLiteralExpression l: sb.Append("LiteralExpression ").Append(l.Value ?? "null").Append(" : ").Append(l.Type.Name).AppendLine(); break;
            case BoundVariableExpression v: sb.Append("VariableExpression ").Append(v.Variable.Name).Append(" : ").Append(v.Type.Name).AppendLine(); break;
            case BoundAssignmentExpression a: sb.Append("AssignmentExpression ").Append(a.Variable.Name).Append(" : ").Append(a.Type.Name).AppendLine(); AppendExpression(sb, a.Expression, i + 1); break;
            case BoundUnaryExpression u: sb.Append("UnaryExpression ").Append(u.OperatorKind).Append(" : ").Append(u.Type.Name).AppendLine(); AppendExpression(sb, u.Operand, i + 1); break;
            case BoundBinaryExpression b: sb.Append("BinaryExpression ").Append(b.OperatorKind).Append(" : ").Append(b.Type.Name).AppendLine(); AppendExpression(sb, b.Left, i + 1); AppendExpression(sb, b.Right, i + 1); break;
            case BoundCallExpression c:
                sb.Append("CallExpression ").Append(c.Function.Name).Append(" : ").Append(c.Type.Name);
                if (c.IsFallible) sb.Append(" ! ").Append(c.ErrorType!.Name);
                sb.AppendLine();
                foreach (var a in c.Arguments) AppendExpression(sb, a, i + 1); break;
            case BoundEnumValueExpression eev:
                sb.Append(eev.IsConstructor ? "EnumConstructor " : "EnumCase ")
                    .Append(eev.Case.EnumType.Name).Append('.').Append(eev.Case.Name)
                    .Append(" : ").Append(eev.Type.Name).AppendLine();
                foreach (var a in eev.Arguments) AppendExpression(sb, a, i + 1); break;
            case BoundPropagateExpression p:
                sb.Append("PropagateExpression ? : ").Append(p.Type.Name).AppendLine();
                AppendExpression(sb, p.Operand, i + 1); break;
            case BoundArrayExpression a: sb.Append("ArrayExpression : ").Append(a.Type.Name).AppendLine(); foreach (var x in a.Elements) AppendExpression(sb, x, i + 1); break;
            default: sb.Append("ErrorExpression : error").AppendLine(); break;
        }
    }
}
