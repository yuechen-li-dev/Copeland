using System.Text;

namespace Copeland.TS.Mir;

public static class MirTsonCanonicalText
{
    public static string BuildDocumentPrefix(MirTsonEncodingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var builder = new StringBuilder();
        builder.Append("const $schema: string = ");
        AppendString(builder, plan.SchemaIdentity);
        builder.Append(";\n");

        IEnumerable<MirTsonNominalPlan> definitionsBeforeTable = plan.TablePlan is null
            ? plan.Definitions
            : plan.Definitions.Where(definition => string.CompareOrdinal(definition.Name, plan.TablePlan.Name) < 0);
        foreach (MirTsonNominalPlan definition in definitionsBeforeTable)
        {
            builder.Append('\n');
            switch (definition)
            {
                case MirTsonRecordPlan record:
                    AppendRecordDefinition(builder, record, plan);
                    break;
                case MirTsonEnumPlan @enum:
                    AppendEnumDefinition(builder, @enum, plan);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported validated TSON nominal plan.");
            }
        }

        if (plan.TablePlan is not null)
        {
            AppendTableHeader(builder, plan.TablePlan, plan);
            return builder.ToString();
        }

        builder.Append("\nconst $value = ");
        return builder.ToString();
    }

    public static string BuildTableColumnPrefix(MirTsonEncodingPlan plan, MirTsonTableColumnPlan column)
        => "    " + column.Name + ": " + ValueTypeName(column.ElementPlan, plan) + " = ";

    public static string BuildTableDocumentSuffix(MirTsonEncodingPlan plan)
    {
        MirTsonTablePlan table = plan.TablePlan
            ?? throw new InvalidOperationException("A table suffix request requires a table plan.");
        var builder = new StringBuilder("}\n");
        foreach (MirTsonNominalPlan definition in plan.Definitions.Where(definition => string.CompareOrdinal(definition.Name, table.Name) > 0))
        {
            builder.Append('\n');
            switch (definition)
            {
                case MirTsonRecordPlan record:
                    AppendRecordDefinition(builder, record, plan);
                    break;
                case MirTsonEnumPlan @enum:
                    AppendEnumDefinition(builder, @enum, plan);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported validated TSON nominal plan.");
            }
        }
        builder.Append("\nconst $value = ").Append(table.Name).Append(";\n");
        return builder.ToString();
    }

    public static string BuildTableStaticText(MirTsonEncodingPlan plan)
    {
        if (plan.TablePlan is null)
        {
            throw new InvalidOperationException("A table static-text request requires a table plan.");
        }

        var builder = new StringBuilder(BuildDocumentPrefix(plan));
        foreach (MirTsonTableColumnPlan column in plan.TablePlan.Columns)
        {
            builder.Append(BuildTableColumnPrefix(plan, column));
            builder.Append("[];\n");
        }
        builder.Append(BuildTableDocumentSuffix(plan));
        return builder.ToString();
    }

    public static int CountUtf8Bytes(string value)
    {
        int bytes = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character <= 0x7F)
            {
                bytes += 1;
            }
            else if (character <= 0x7FF)
            {
                bytes += 2;
            }
            else if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw new ArgumentException("Canonical TSON static text contains invalid Unicode.", nameof(value));
                }
                bytes += 4;
                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                throw new ArgumentException("Canonical TSON static text contains invalid Unicode.", nameof(value));
            }
            else
            {
                bytes += 3;
            }
        }
        return bytes;
    }

    private static void AppendRecordDefinition(StringBuilder builder, MirTsonRecordPlan record, MirTsonEncodingPlan plan)
    {
        builder.Append("record ").Append(record.Name).Append(" {\n");
        foreach (MirTsonRecordFieldPlan field in record.Fields)
        {
            builder.Append("    ").Append(field.Name).Append(": ");
            builder.Append(ValueTypeName(field.ValuePlan, plan));
            builder.Append(";\n");
        }
        builder.Append("}\n");
    }

    private static void AppendEnumDefinition(StringBuilder builder, MirTsonEnumPlan @enum, MirTsonEncodingPlan plan)
    {
        builder.Append("enum ").Append(@enum.Name).Append(" {\n");
        foreach (MirTsonEnumCasePlan @case in @enum.Cases)
        {
            builder.Append("    ").Append(@case.Name);
            if (@case.Payloads.Count > 0)
            {
                builder.Append('(');
                for (int index = 0; index < @case.Payloads.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }
                    MirTsonEnumPayloadPlan payload = @case.Payloads[index];
                    builder.Append(payload.Name).Append(": ");
                    builder.Append(ValueTypeName(payload.ValuePlan, plan));
                }
                builder.Append(')');
            }
            builder.Append(",\n");
        }
        builder.Append("}\n");
    }

    private static void AppendTableHeader(StringBuilder builder, MirTsonTablePlan table, MirTsonEncodingPlan plan)
    {
        _ = plan;
        builder.Append("\nrecord table ").Append(table.Name).Append(" {\n");
    }

    private static string ValueTypeName(MirTsonValuePlan valuePlan, MirTsonEncodingPlan plan)
    {
        return valuePlan switch
        {
            MirTsonBooleanPlan => "boolean",
            MirTsonNumberPlan => "number",
            MirTsonStringPlan => "string",
            MirTsonRecordValuePlan record => plan.Definitions
                .OfType<MirTsonRecordPlan>()
                .Single(item => item.RecordTypeId == record.RecordTypeId)
                .Name,
            MirTsonEnumValuePlan @enum => @enum.EnumName,
            MirTsonArrayPlan array => ValueTypeName(array.ElementPlan, plan) + "[]",
            _ => throw new InvalidOperationException("Unsupported validated TSON value plan."),
        };
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\u2028':
                case '\u2029':
                    AppendUnicodeEscape(builder, character);
                    break;
                default:
                    if (char.IsHighSurrogate(character))
                    {
                        if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                        {
                            throw new ArgumentException(
                                "Canonical TSON schema identity contains invalid Unicode.",
                                nameof(value));
                        }
                        builder.Append(character);
                        builder.Append(value[++index]);
                    }
                    else if (char.IsLowSurrogate(character))
                    {
                        throw new ArgumentException(
                            "Canonical TSON schema identity contains invalid Unicode.",
                            nameof(value));
                    }
                    else if (character < ' ')
                    {
                        AppendUnicodeEscape(builder, character);
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        builder.Append('"');
    }

    private static void AppendUnicodeEscape(StringBuilder builder, char value)
    {
        const string hexadecimal = "0123456789ABCDEF";
        builder.Append("\\u");
        builder.Append(hexadecimal[(value >> 12) & 0xF]);
        builder.Append(hexadecimal[(value >> 8) & 0xF]);
        builder.Append(hexadecimal[(value >> 4) & 0xF]);
        builder.Append(hexadecimal[value & 0xF]);
    }
}
