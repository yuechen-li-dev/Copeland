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

        foreach (var record in program.Records)
        {
            sb.AppendLine();
            sb.Append("record ").Append(record.Name).Append(" [").Append(record.Id).AppendLine("]");
            foreach (var field in record.Fields)
            {
                sb.Append("  field ").Append(field.Name).Append(" [").Append(field.Id).Append("]: ").Append(field.Type.Name).AppendLine();
            }
        }

        foreach (var table in program.Tables)
        {
            sb.AppendLine();
            sb.Append("table ").Append(table.Name).Append(" [").Append(table.Id).Append("] row [").Append(table.RowTypeId).Append("] count ").AppendLine(table.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var column in table.Columns)
                sb.Append("  column ").Append(column.Name).Append(" [").Append(column.Id).Append("]: ").Append(column.ElementType.Name).Append(" = [").Append(string.Join(", ", column.Constants.Select(FormatTableConstant))).AppendLine("]");
        }

        foreach (var plan in program.TsonEncodingPlans)
        {
            sb.AppendLine();
            sb.Append("tson-plan ").Append(plan.Id).Append(" schema ").Append(plan.SchemaIdentity)
                .Append(" root ").Append(plan.RootType.Name)
                .Append(" limits utf8=").Append(plan.Limits.MaximumUtf8Bytes)
                .Append(" string-utf16=").Append(plan.Limits.MaximumStringCodeUnits);
            if (TsonPlanContainsArray(plan.RootValuePlan)
                || plan.Definitions.Any(TsonDefinitionContainsArray))
            {
                sb.Append(" array-length=").Append(plan.Limits.MaximumArrayLength);
            }
            sb.AppendLine();
            foreach (var definition in plan.Definitions)
            {
                switch (definition)
                {
                    case MirTsonRecordPlan record:
                        sb.Append("  record ").Append(record.Name).Append(" [").Append(record.RecordTypeId).Append("] identity ").AppendLine(record.StableIdentity);
                        foreach (var field in record.Fields)
                            sb.Append("    field ").Append(field.Name).Append(" [").Append(field.FieldId).Append("] identity ").Append(field.StableIdentity).Append(": ").AppendLine(FormatTsonValuePlan(field.ValuePlan));
                        break;
                    case MirTsonEnumPlan @enum:
                        sb.Append("  enum ").Append(@enum.Name).Append(" identity ").AppendLine(@enum.StableIdentity);
                        foreach (var @case in @enum.Cases)
                        {
                            sb.Append("    case ").Append(@case.Name).Append(" identity ").AppendLine(@case.StableIdentity);
                            foreach (var payload in @case.Payloads)
                                sb.Append("      payload ").Append(payload.Name).Append(" identity ").Append(payload.StableIdentity).Append(": ").AppendLine(FormatTsonValuePlan(payload.ValuePlan));
                        }
                        break;
                }
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
        MirLiteralExpression l => l.Value switch
        {
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            IFormattable value => value.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => l.Value?.ToString() ?? "null",
        },
        MirVariableExpression v => v.Name,
        MirAssignmentExpression a => $"{a.Name} = {FormatExpression(a.Expression)}",
        MirUnaryExpression u => $"({u.Operator}{FormatExpression(u.Operand)})",
        MirBinaryExpression b => $"({FormatExpression(b.Left)} {b.Operator} {FormatExpression(b.Right)})",
        MirUnitExpression => "unit",
        MirCallExpression c => $"call {c.FunctionName}({string.Join(", ", c.Arguments.Select(FormatExpression))})",
        MirArrayExpression a => $"[{string.Join(", ", a.Elements.Select(FormatExpression))}]",
        MirRecordConstructionExpression construction => $"record-new [{construction.RecordTypeId}] {{ {string.Join(", ", construction.Initializers.Select(FormatRecordFieldValue))} }}",
        MirRecordFieldAccessExpression access => $"record-get [{access.RecordTypeId}] {FormatExpression(access.Receiver)}.[{access.FieldId}]",
        MirTableReferenceExpression table => $"table-ref [{table.TableId}]",
        MirTableColumnAccessExpression access => $"table-column [{access.TableId}] {FormatExpression(access.Receiver)}.[{access.ColumnId}]",
        MirTableRowAccessExpression access => $"table-row [{access.TableId}] {FormatExpression(access.Receiver)}[{FormatExpression(access.Index)}]",
        MirColumnElementAccessExpression access => $"column-element {FormatExpression(access.Receiver)}[{FormatExpression(access.Index)}]",
        MirTableRowFieldAccessExpression access => $"table-row-field [{access.RowTypeId}] {FormatExpression(access.Receiver)}.[{access.FieldId}]",
        MirRecordWithExpression withExpression => $"record-with [{withExpression.RecordTypeId}] {FormatExpression(withExpression.Source)} {{ {string.Join(", ", withExpression.Replacements.Select(FormatRecordFieldValue))} }}",
        MirEnumValueExpression e => $"enum {e.EnumName}.{e.CaseName}{(e.Arguments.Count == 0 ? string.Empty : $"({string.Join(", ", e.Arguments.Select(FormatExpression))})")}",
        MirMatchExpression m => $"match {FormatExpression(m.Scrutinee)} : {m.Type.Name} {{ {string.Join(" | ", m.Arms.Select(FormatArm))} }}",
        MirIfExpression i => $"if {FormatExpression(i.Condition)} : {i.Type.Name} {{ then {FormatExpression(i.ThenExpression)} else {FormatExpression(i.ElseExpression)} }}",
        MirTsonEncodeExpression encode => $"tson-encode [{encode.PlanId}] {FormatExpression(encode.Operand)} : {encode.ResultType.Name}",
        MirOkExpression ok => $"ok {FormatExpression(ok.Payload)}",
        MirErrExpression err => $"err {FormatExpression(err.Payload)}",
        MirResultMatchExpression match => $"result-match {FormatExpression(match.Scrutinee)} : {match.Type.Name} {{ ok({match.OkBinding.Name}: {match.OkBinding.Type.Name}) => {FormatExpression(match.OkExpression)} | err({match.ErrBinding.Name}: {match.ErrBinding.Type.Name}) => {FormatExpression(match.ErrExpression)} }}",
        MirPropagateExpression propagate => $"propagate {FormatExpression(propagate.Operand)} to {FormatPropagationTarget(propagate.Target)}",
        MirUnwrapExpression unwrap => $"unwrap {FormatExpression(unwrap.Operand)}",
        MirTryExpression tryExpression => FormatTryExpression(tryExpression),
        _ => expr.ToString() ?? "<expr>"
    };

    private static string FormatTsonValuePlan(MirTsonValuePlan plan)
        => plan switch
        {
            MirTsonBooleanPlan => "boolean",
            MirTsonNumberPlan => "number",
            MirTsonStringPlan => "string",
            MirTsonRecordValuePlan record => $"record [{record.RecordTypeId}]",
            MirTsonEnumValuePlan @enum => $"enum {@enum.EnumName}",
            MirTsonArrayPlan array => FormatTsonValuePlan(array.ElementPlan) + "[]",
            _ => "<unsupported>",
        };

    private static bool TsonDefinitionContainsArray(MirTsonNominalPlan definition)
        => definition switch
        {
            MirTsonRecordPlan record => record.Fields.Any(field => TsonPlanContainsArray(field.ValuePlan)),
            MirTsonEnumPlan @enum => @enum.Cases.Any(@case => @case.Payloads.Any(payload => TsonPlanContainsArray(payload.ValuePlan))),
            _ => false,
        };

    private static bool TsonPlanContainsArray(MirTsonValuePlan plan)
        => plan is MirTsonArrayPlan;

    private static string FormatTableConstant(MirTableConstant constant) => constant switch
    {
        MirTableLiteralConstant literal => literal.Value switch { string text => $"\"{text}\"", bool boolean => boolean ? "true" : "false", _ => Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "null" },
        MirTableArrayConstant array => $"[{string.Join(", ", array.Elements.Select(FormatTableConstant))}]: {array.ArrayType.Name}",
        MirTableRecordConstant record => $"record [{record.RecordTypeId}] {{ {string.Join(", ", record.Fields.Select(field => $"[{field.FieldId}]: {FormatTableConstant(field.Value)}"))} }}",
        MirTableEnumConstant value => $"enum {value.EnumName}.{value.CaseName}{(value.Payloads.Count == 0 ? string.Empty : $"({string.Join(", ", value.Payloads.Select(FormatTableConstant))})")}",
        MirTableResultConstant result => (result.IsOk ? "ok " : "err ") + FormatTableConstant(result.Payload),
        _ => "<table-constant>",
    };

    private static string FormatRecordFieldValue(MirRecordFieldValue fieldValue)
        => $"[{fieldValue.FieldId}]: {FormatExpression(fieldValue.Value)}";

    private static string FormatPropagationTarget(MirPropagationTarget target)
        => target switch
        {
            MirPropagationTarget.FunctionReturn => "function-return",
            MirPropagationTarget.LexicalExcept lexical => $"except {lexical.HandlerId}",
            _ => target.ToString() ?? "<target>"
        };

    private static string FormatTryExpression(MirTryExpression tryExpression)
    {
        var protectedLines = FormatValueBlock(tryExpression.Protected);
        var handlerLines = FormatValueBlock(tryExpression.Handler);
        return $"try-result {tryExpression.HandlerId} error {tryExpression.HandledErrorType.Name} -> {tryExpression.Type.Name} {{ protected {{ {protectedLines} }} except {tryExpression.HandlerBinding.Name}: {tryExpression.HandlerBinding.Type.Name} {{ {handlerLines} }} }}";
    }

    private static string FormatValueBlock(MirValueBlock block)
    {
        var prefixes = block.PrefixStatements.Select(StatementInline);
        return string.Join("; ", prefixes.Append(FormatExpression(block.ValueExpression)));
    }

    private static string FormatArm(MirMatchArm arm)
    {
        var payload = arm.PayloadBindings.Count == 0
            ? string.Empty
            : $"({string.Join(", ", arm.PayloadBindings.Select(p => $"{p.Name}: {p.Type.Name}"))})";
        return $"{arm.CaseName}{payload} => {FormatExpression(arm.Expression)}";
    }
}
