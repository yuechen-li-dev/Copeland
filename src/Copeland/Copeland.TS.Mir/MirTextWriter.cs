using System.Text;

namespace Copeland.TS.Mir;

public static class MirTextWriter
{
    public static string Write(MirProgram program)
    {
        var sb = new StringBuilder();
        sb.AppendLine("module");

        foreach (var @enum in program.Enums)
        {
            sb.AppendLine();
            sb.Append("enum ").AppendLine(@enum.Name);
            foreach (var @case in @enum.Cases)
            {
                sb.Append("  case ").Append(@case.Name);
                if (@case.PayloadFields.Count > 0)
                    sb.Append('(').Append(string.Join(", ", @case.PayloadFields.Select(f => $"{f.Name}: {f.Type.Name}"))).Append(')');

                sb.AppendLine();
            }
        }

        foreach (var function in program.Functions)
        {
            sb.AppendLine();
            sb.Append("func ").Append(function.Name).Append('(');
            sb.Append(string.Join(", ", function.Parameters.Select(p => $"{p.Name}: {p.Type.Name}")));
            sb.Append(") -> ").Append(function.ReturnType.Name).AppendLine();
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
        MirUnitExpression => "unit",
        MirCallExpression c => $"call {c.FunctionName}({string.Join(", ", c.Arguments.Select(FormatExpression))})",
        MirArrayExpression a => $"[{string.Join(", ", a.Elements.Select(FormatExpression))}]",
        MirEnumValueExpression e => $"enum {e.EnumName}.{e.CaseName}{(e.Arguments.Count == 0 ? string.Empty : $"({string.Join(", ", e.Arguments.Select(FormatExpression))})")}",
        MirMatchExpression m => $"match {FormatExpression(m.Scrutinee)} : {m.Type.Name} {{ {string.Join(" | ", m.Arms.Select(FormatArm))} }}",
        MirIfExpression i => $"if {FormatExpression(i.Condition)} : {i.Type.Name} {{ then {FormatExpression(i.ThenExpression)} else {FormatExpression(i.ElseExpression)} }}",
        MirOkExpression ok => $"ok {FormatExpression(ok.Payload)}",
        MirErrExpression err => $"err {FormatExpression(err.Payload)}",
        MirResultMatchExpression match => $"result-match {FormatExpression(match.Scrutinee)} : {match.Type.Name} {{ ok({match.OkBinding.Name}: {match.OkBinding.Type.Name}) => {FormatExpression(match.OkExpression)} | err({match.ErrBinding.Name}: {match.ErrBinding.Type.Name}) => {FormatExpression(match.ErrExpression)} }}",
        MirPropagateExpression propagate => $"propagate {FormatExpression(propagate.Operand)} to function-return",
        MirUnwrapExpression unwrap => $"unwrap {FormatExpression(unwrap.Operand)}",
        _ => expr.ToString() ?? "<expr>"
    };

    private static string FormatArm(MirMatchArm arm)
    {
        var payload = arm.PayloadBindings.Count == 0
            ? string.Empty
            : $"({string.Join(", ", arm.PayloadBindings.Select(p => $"{p.Name}: {p.Type.Name}"))})";
        return $"{arm.CaseName}{payload} => {FormatExpression(arm.Expression)}";
    }
}
