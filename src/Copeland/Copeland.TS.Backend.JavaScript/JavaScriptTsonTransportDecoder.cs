using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

public static partial class JavaScriptBackend
{
    private static void EmitJavaScriptTsonRecordDecoder(
        JavaScriptTextWriter writer, MirTsonEncodingPlan plan, int planIndex,
        EnumCatalog catalog, GeneratedNames names, TsonGeneratedNames tsonNames)
    {
        var root = (MirTsonRecordValuePlan)plan.RootValuePlan;
        var readers = plan.Definitions.Select((definition, index) => (definition.Name, Reader: "readNominal" + index))
            .ToDictionary(pair => pair.Name, pair => pair.Reader, StringComparer.Ordinal);
        MirTsonRecordDefinitionName(root.RecordTypeId, plan, out MirTsonRecordPlan rootPlan);
        string decoder = tsonNames.Encoder(planIndex) + "Decode";
        writer.WriteLine($"function {decoder}(text) {{");
        writer.Indent();
        writer.WriteLine("if (!text.startsWith(\"const $schema: string = \\\"\") || !text.endsWith(\";\\n\")) throw new Error(\"Malformed TSON transport payload.\");");
        writer.WriteLine($"let position = text.indexOf({JavaScriptLiteralWriter.WriteString(TsonRecordPrefix(rootPlan))});");
        writer.WriteLine("if (position < 0) throw new Error(\"Malformed TSON transport payload.\");");
        writer.WriteLine("""
            function consume(value) {
                if (!text.startsWith(value, position)) return false;
                position += value.length;
                return true;
            }
            function expect(value) {
                if (!consume(value)) throw new Error("Malformed TSON transport payload.");
            }
            function indent(level) {
                expect(" ".repeat(level * 4));
            }
            function readString() {
                const start = position;
                expect('"');
                while (position < text.length) {
                    const current = text[position++];
                    if (current === "\\") {
                        position++;
                    } else if (current === '"') {
                        return JSON.parse(text.slice(start, position));
                    } else if (current < ' ') {
                        break;
                    }
                }
                throw new Error("Malformed TSON transport string.");
            }
            function readNumber() {
                expect('$number("');
                const hex = text.slice(position, position + 16);
                if (!/^[0-9A-F]{16}$/.test(hex)) throw new Error("Malformed TSON transport number.");
                position += 16;
                expect('")');
                const view = new DataView(new ArrayBuffer(8));
                view.setUint32(0, Number.parseInt(hex.slice(0, 8), 16), false);
                view.setUint32(4, Number.parseInt(hex.slice(8, 16), 16), false);
                return view.getFloat64(0, false);
            }
            function readBoolean() {
                if (consume("true")) return true;
                expect("false");
                return false;
            }
            """);
        if (CollectTsonArrayPlans(plan).Count > 0)
        {
            writer.WriteLine($$"""
                function readArray(indentation, readItem) {
                    if (consume("[]")) return Object.freeze([]);
                    expect("[\n");
                    const items = [];
                    const suffix = " ".repeat(indentation * 4) + "]";
                    while (!consume(suffix)) {
                        if (items.length >= {{plan.Limits.MaximumArrayLength}}) throw new Error("TSON transport array exceeds its length limit.");
                        indent(indentation + 1);
                        items.push(readItem(indentation + 1));
                        expect(",\n");
                    }
                    return Object.freeze(items);
                }
                """);
        }
        foreach (MirTsonRecordPlan definition in plan.Definitions.OfType<MirTsonRecordPlan>())
        {
            writer.WriteLine($"function {readers[definition.Name]}(level) {{");
            writer.Indent();
            writer.WriteLine($"expect({JavaScriptLiteralWriter.WriteString(TsonRecordPrefix(definition))});");
            var values = new List<string>();
            foreach (MirTsonRecordFieldPlan field in definition.Fields)
            {
                writer.WriteLine("indent(level + 1);");
                writer.WriteLine($"expect({JavaScriptLiteralWriter.WriteString("\"" + field.Name + "\": ")});");
                string value = "field" + values.Count;
                writer.WriteLine($"const {value} = {JavaScriptTsonReadValue(plan, field.ValuePlan, readers, "level + 1", 0)};");
                writer.WriteLine("expect(\",\\n\");");
                values.Add(value);
            }
            if (values.Count > 0)
            {
                writer.WriteLine("indent(level);");
                writer.WriteLine("expect(\"})\");");
            }
            writer.WriteLine($"return {RecordConstructionName(catalog.GetRecord(definition.RecordTypeId), names)}({string.Join(", ", values)});");
            writer.Unindent();
            writer.WriteLine("}");
        }
        foreach (MirTsonEnumPlan definition in plan.Definitions.OfType<MirTsonEnumPlan>())
        {
            EnumInfo type = catalog.GetEnum(definition.Name);
            writer.WriteLine($"function {readers[definition.Name]}(level) {{");
            writer.Indent();
            foreach (MirTsonEnumCasePlan @case in definition.Cases.OrderByDescending(item => item.Name.Length))
            {
                string prefix = definition.Name + "." + @case.Name + (@case.Payloads.Count == 0 ? "" : "(\n");
                writer.WriteLine($"if (consume({JavaScriptLiteralWriter.WriteString(prefix)})) {{");
                writer.Indent();
                var values = new List<string>();
                for (int index = 0; index < @case.Payloads.Count; index++)
                {
                    writer.WriteLine("indent(level + 1);");
                    string value = "payload" + index;
                    writer.WriteLine($"const {value} = {JavaScriptTsonReadValue(plan, @case.Payloads[index].ValuePlan, readers, "level + 1", 0)};");
                    writer.WriteLine(index + 1 < @case.Payloads.Count ? "expect(\",\\n\");" : "expect(\"\\n\");");
                    values.Add(value);
                }
                if (values.Count > 0)
                {
                    writer.WriteLine("indent(level);");
                    writer.WriteLine("expect(\")\");");
                }
                string result = IsProduction(names)
                    ? EmitProductionEnumConstruction(type, type.Definition.Cases.Single(item => item.Name == @case.Name), names, values)
                    : $"{names.MakeValue}({names.TypeToken(type)}, {JavaScriptLiteralWriter.WriteString(@case.Name)}, [{string.Join(", ", values)}])";
                writer.WriteLine($"return {result};");
                writer.Unindent();
                writer.WriteLine("}");
            }
            writer.WriteLine("throw new Error(\"Malformed TSON transport enum.\");");
            writer.Unindent();
            writer.WriteLine("}");
        }
        writer.WriteLine($"const value = {readers[rootPlan.Name]}(0);");
        writer.WriteLine("expect(\";\\n\");");
        writer.WriteLine("if (position !== text.length) throw new Error(\"Trailing TSON transport data.\");");
        writer.WriteLine("return value;");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static string TsonRecordPrefix(MirTsonRecordPlan record)
        => "$record." + record.Name + (record.Fields.Count == 0 ? "({})" : "({\n");

    private static string MirTsonRecordDefinitionName(
        MirRecordTypeId id, MirTsonEncodingPlan plan, out MirTsonRecordPlan definition)
    {
        definition = plan.Definitions.OfType<MirTsonRecordPlan>().Single(record => record.RecordTypeId == id);
        return definition.Name;
    }

    private static string JavaScriptTsonReadValue(
        MirTsonEncodingPlan plan, MirTsonValuePlan value, Dictionary<string, string> readers, string level, int depth)
    {
        switch (value)
        {
            case MirTsonBooleanPlan: return "readBoolean()";
            case MirTsonNumberPlan: return "readNumber()";
            case MirTsonStringPlan: return "readString()";
            case MirTsonRecordValuePlan record:
                string name = MirTsonRecordDefinitionName(record.RecordTypeId, plan, out _);
                return $"{readers[name]}({level})";
            case MirTsonEnumValuePlan @enum:
                return $"{readers[@enum.EnumName]}({level})";
            case MirTsonArrayPlan array:
                string child = "itemLevel" + depth;
                return $"readArray({level}, {child} => {JavaScriptTsonReadValue(plan, array.ElementPlan, readers, child, depth + 1)})";
            default:
                throw new InvalidOperationException("Unsupported TSON transport decoder plan.");
        }
    }
}
