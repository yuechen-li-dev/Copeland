using System.Text;

namespace Copeland.Script.Mir;

public static class MirTextWriter
{
    public static string Write(MirProgram program)
    {
        var sb = new StringBuilder();
        sb.AppendLine("module");
        foreach (var function in program.Functions)
        {
            sb.AppendLine();
            sb.Append("func ").Append(function.Name).Append('(');
            sb.Append(string.Join(", ", function.Parameters.Select(p => $"{p.Name}: {p.Type.Name}")));
            sb.Append(") -> ").Append(function.ReturnType.Name);
            if (function.IsFallible && function.ErrorType is not null)
                sb.Append(" ! ").Append(function.ErrorType.Name);
            sb.AppendLine();
            if (function.Locals.Count > 0)
            {
                sb.AppendLine("locals:");
                foreach (var local in function.Locals)
                    sb.Append("  ").Append(local.IsReadOnly ? "const" : "let").Append(' ').Append(local.Name).Append(": ").Append(local.Type.Name).AppendLine();
            }

            sb.AppendLine("entry:");
            foreach (var stmt in function.Body)
                WriteStatement(sb, stmt, 1);
        }

        return sb.ToString();
    }

    private static void WriteStatement(StringBuilder sb, MirStatement stmt, int indent)
    {
        var i = new string(' ', indent * 2);
        switch (stmt)
        {
            case MirVariableDeclarationStatement v:
                sb.Append(i).Append("store ").Append(v.Local.Name).Append(", ").AppendLine(FormatExpression(v.Initializer));
                break;
            case MirExpressionStatement e:
                sb.Append(i).AppendLine(FormatExpression(e.Expression));
                break;
            case MirReturnStatement r:
                sb.Append(i).Append("return");
                if (r.Expression is not null) sb.Append(' ').Append(FormatExpression(r.Expression));
                sb.AppendLine();
                break;
            case MirIfStatement @if:
                sb.Append(i).Append("if ").AppendLine(FormatExpression(@if.Condition));
                foreach (var s in @if.ThenStatements) WriteStatement(sb, s, indent + 1);
                if (@if.ElseStatements is not null)
                {
                    sb.Append(i).AppendLine("else");
                    foreach (var s in @if.ElseStatements) WriteStatement(sb, s, indent + 1);
                }
                break;
            case MirWhileStatement w:
                sb.Append(i).Append("while ").AppendLine(FormatExpression(w.Condition));
                foreach (var s in w.BodyStatements) WriteStatement(sb, s, indent + 1);
                break;
            case MirForStatement f:
                sb.Append(i).Append("for (");
                sb.Append(f.Initializer is null ? "; " : StatementInline(f.Initializer) + "; ");
                sb.Append(f.Condition is null ? "; " : FormatExpression(f.Condition) + "; ");
                sb.Append(f.Increment is null ? ")" : FormatExpression(f.Increment) + ")");
                sb.AppendLine();
                foreach (var s in f.BodyStatements) WriteStatement(sb, s, indent + 1);
                break;
        }
    }

    private static string StatementInline(MirStatement stmt) => stmt switch
    {
        MirVariableDeclarationStatement v => $"store {v.Local.Name}, {FormatExpression(v.Initializer)}",
        MirExpressionStatement e => FormatExpression(e.Expression),
        _ => stmt.GetType().Name
    };

    private static string FormatExpression(MirExpression expr) => expr switch
    {
        MirLiteralExpression l => l.Value switch { string s => $"\"{s}\"", bool b => b ? "true" : "false", _ => l.Value?.ToString() ?? "null" },
        MirVariableExpression v => v.Name,
        MirAssignmentExpression a => $"{a.Name} = {FormatExpression(a.Expression)}",
        MirUnaryExpression u => $"({u.Operator}{FormatExpression(u.Operand)})",
        MirBinaryExpression b => $"({FormatExpression(b.Left)} {b.Operator} {FormatExpression(b.Right)})",
        MirCallExpression c => c.IsFallible && c.IsPropagated && c.ErrorType is not null
            ? $"call? {c.FunctionName}({string.Join(", ", c.Arguments.Select(FormatExpression))}) propagate {c.ErrorType.Name}"
            : $"call {c.FunctionName}({string.Join(", ", c.Arguments.Select(FormatExpression))})",
        MirArrayExpression a => $"[{string.Join(", ", a.Elements.Select(FormatExpression))}]",
        _ => expr.ToString() ?? "<expr>"
    };
}
