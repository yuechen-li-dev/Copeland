using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

public static partial class CSharpBackend
{
    private static void EmitTsonRecordDecoder(
        CSharpTextWriter writer, MirTsonEncodingPlan plan,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records)
    {
        if (plan.RootValuePlan is not MirTsonRecordValuePlan root)
        {
            return;
        }

        MirTsonRecordPlan rootPlan = plan.Definitions.OfType<MirTsonRecordPlan>()
            .Single(definition => definition.RecordTypeId == root.RecordTypeId);
        string prefix = RecordPrefix(rootPlan);
        writer.WriteLine($"private static {RecordTypeName(root.RecordTypeId)} {TsonDecodeMethodName(plan.Id)}(string text)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (!text.StartsWith(\"const $schema: string = \\\"\", global::System.StringComparison.Ordinal)) throw new global::System.InvalidOperationException(\"Malformed TSON transport schema.\");");
        writer.WriteLine($"int rootPosition = text.IndexOf({CSharpLiteralWriter.Write(prefix)}, global::System.StringComparison.Ordinal);");
        writer.WriteLine("if (rootPosition < 0) throw new global::System.InvalidOperationException(\"Malformed TSON transport root.\");");
        writer.WriteLine("var reader = new __TsonReader(text.Substring(rootPosition));");
        writer.WriteLine($"var value = {TsonNominalReaderName(plan, rootPlan.Name)}(reader, 0);");
        EmitTsonExpect(writer, ";\n", "suffix");
        writer.WriteLine("if (!reader.End) throw new global::System.InvalidOperationException(\"Trailing TSON transport data.\");");
        writer.WriteLine("return value;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();

        foreach (MirTsonRecordPlan definition in plan.Definitions.OfType<MirTsonRecordPlan>())
        {
            EmitTsonRecordReader(writer, plan, definition, records[definition.RecordTypeId]);
        }
        foreach (MirTsonEnumPlan definition in plan.Definitions.OfType<MirTsonEnumPlan>())
        {
            EmitTsonEnumReader(writer, plan, definition);
        }
    }

    private static string TsonNominalReaderName(MirTsonEncodingPlan plan, string name)
        => "__tson_read_" + EncodeStableIdentity(plan.Id.Value + ":" + name);

    private static string RecordPrefix(MirTsonRecordPlan record)
        => "$record." + record.Name + (record.Fields.Count == 0 ? "({})" : "({\n");

    private static void EmitTsonExpect(CSharpTextWriter writer, string token, string location)
        => writer.WriteLine($"if (!reader.Expect({CSharpLiteralWriter.Write(token)})) throw new global::System.InvalidOperationException(\"Malformed TSON transport {location}.\");");

    private static void EmitTsonReadIndent(CSharpTextWriter writer, string level)
        => writer.WriteLine($"if (!reader.Expect(new string(' ', ({level}) * 4))) throw new global::System.InvalidOperationException(\"Malformed TSON transport indentation.\");");

    private static void EmitTsonRecordReader(
        CSharpTextWriter writer, MirTsonEncodingPlan plan, MirTsonRecordPlan recordPlan, MirRecordDefinition record)
    {
        writer.WriteLine($"private static {RecordTypeName(record.Id)} {TsonNominalReaderName(plan, recordPlan.Name)}(__TsonReader reader, int indentation)");
        writer.WriteLine("{");
        writer.Indent();
        EmitTsonExpect(writer, RecordPrefix(recordPlan), "record");
        var fields = new List<string>();
        int temporaryIndex = 0;
        foreach (MirTsonRecordFieldPlan field in recordPlan.Fields)
        {
            EmitTsonReadIndent(writer, "indentation + 1");
            EmitTsonExpect(writer, "\"" + field.Name + "\": ", "field name");
            fields.Add(EmitTsonDecoderValue(writer, plan, field.ValuePlan, "indentation + 1", ref temporaryIndex));
            EmitTsonExpect(writer, ",\n", "field separator");
        }
        if (recordPlan.Fields.Count > 0)
        {
            EmitTsonReadIndent(writer, "indentation");
            EmitTsonExpect(writer, "})", "record suffix");
        }
        writer.WriteLine($"return new {RecordTypeName(record.Id)}({string.Join(", ", fields)});");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitTsonEnumReader(CSharpTextWriter writer, MirTsonEncodingPlan plan, MirTsonEnumPlan enumPlan)
    {
        string type = CSharpNameMangler.Mangle(enumPlan.Name);
        writer.WriteLine($"private static {type} {TsonNominalReaderName(plan, enumPlan.Name)}(__TsonReader reader, int indentation)");
        writer.WriteLine("{");
        writer.Indent();
        int temporaryIndex = 0;
        foreach (MirTsonEnumCasePlan @case in enumPlan.Cases.OrderByDescending(item => item.Name.Length))
        {
            string prefix = enumPlan.Name + "." + @case.Name;
            string caseType = type + "." + CSharpNameMangler.Mangle(@case.Name);
            if (@case.Payloads.Count == 0)
            {
                writer.WriteLine($"if (reader.Expect({CSharpLiteralWriter.Write(prefix)})) return {caseType}.__Singleton;");
                continue;
            }
            writer.WriteLine($"if (reader.Expect({CSharpLiteralWriter.Write(prefix + "(\n")}))");
            writer.WriteLine("{");
            writer.Indent();
            var arguments = new List<string>();
            for (int index = 0; index < @case.Payloads.Count; index++)
            {
                EmitTsonReadIndent(writer, "indentation + 1");
                arguments.Add(EmitTsonDecoderValue(writer, plan, @case.Payloads[index].ValuePlan, "indentation + 1", ref temporaryIndex));
                EmitTsonExpect(writer, index + 1 < @case.Payloads.Count ? ",\n" : "\n", "enum separator");
            }
            EmitTsonReadIndent(writer, "indentation");
            EmitTsonExpect(writer, ")", "enum suffix");
            writer.WriteLine($"return new {caseType}({string.Join(", ", arguments)});");
            writer.Unindent();
            writer.WriteLine("}");
        }
        writer.WriteLine("throw new global::System.InvalidOperationException(\"Malformed TSON transport enum.\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static string EmitTsonDecoderValue(
        CSharpTextWriter writer, MirTsonEncodingPlan plan, MirTsonValuePlan valuePlan, string indentation,
        ref int temporaryIndex)
    {
        string variable = "value" + temporaryIndex++;
        switch (valuePlan)
        {
            case MirTsonBooleanPlan:
                writer.WriteLine($"if (!reader.TryBoolean(out bool {variable})) throw new global::System.InvalidOperationException(\"Malformed TSON transport boolean.\");");
                break;
            case MirTsonNumberPlan:
                writer.WriteLine($"if (!reader.TryNumber(out double {variable})) throw new global::System.InvalidOperationException(\"Malformed TSON transport number.\");");
                break;
            case MirTsonStringPlan:
                writer.WriteLine($"if (!reader.TryString(out string {variable})) throw new global::System.InvalidOperationException(\"Malformed TSON transport string.\");");
                break;
            case MirTsonArrayPlan array:
                string items = "items" + temporaryIndex++;
                writer.WriteLine($"var {items} = new global::System.Collections.Generic.List<{TsonValuePlanType(array.ElementPlan)}>();");
                writer.WriteLine("reader.SkipWhitespace();");
                EmitTsonExpect(writer, "[", "array prefix");
                writer.WriteLine("while (true)");
                writer.WriteLine("{");
                writer.Indent();
                writer.WriteLine("reader.SkipWhitespace();");
                writer.WriteLine("if (reader.Expect(\"]\")) break;");
                writer.WriteLine($"if ({items}.Count >= {plan.Limits.MaximumArrayLength}) throw new global::System.InvalidOperationException(\"TSON transport array exceeds its length limit.\");");
                string element = EmitTsonDecoderValue(writer, plan, array.ElementPlan, indentation + " + 1", ref temporaryIndex);
                writer.WriteLine($"{items}.Add({element});");
                writer.WriteLine("reader.SkipWhitespace();");
                EmitTsonExpect(writer, ",", "array separator");
                writer.Unindent();
                writer.WriteLine("}");
                writer.WriteLine($"var {variable} = {items}.ToArray();");
                break;
            case MirTsonRecordValuePlan record:
                string name = plan.Definitions.OfType<MirTsonRecordPlan>()
                    .Single(definition => definition.RecordTypeId == record.RecordTypeId).Name;
                writer.WriteLine($"var {variable} = {TsonNominalReaderName(plan, name)}(reader, {indentation});");
                break;
            case MirTsonEnumValuePlan @enum:
                writer.WriteLine($"var {variable} = {TsonNominalReaderName(plan, @enum.EnumName)}(reader, {indentation});");
                break;
            default:
                throw new InvalidOperationException("Transport decoder received an unsupported TSON plan.");
        }
        return variable;
    }
}
