using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

/// <summary>Accumulator statements shared by typed summaries and query materialization.</summary>
internal static class CSharpTableAccumulator
{
    public static string AddCount(string target)
    {
        return target + "++;";
    }

    public static string AddSum(string target, string value)
    {
        return target + " += " + value + ";";
    }
}

public static partial class CSharpBackend
{
    private static string EmitTableSummary(
        CSharpTextWriter writer,
        MirTableSummaryExpression summary,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        (MirExpression table, IReadOnlyList<MirExpression> predicates) = UnwrapTableSelection(summary.Source);
        string prefix = "__cope_summary_" + tempIndex++;
        string source = prefix + "_source";
        string rowIndex = prefix + "_row_index";
        string row = prefix + "_row";
        string groupIndex = prefix + "_group_index";
        string groupCount = prefix + "_group_count";
        string rowTypeId = summary.TableId.Value + ".row";
        string recordType = RecordTypeName(summary.RecordTypeId);
        var rowExpression = new MirVariableExpression(row, new MirTableRowType(rowTypeId, string.Empty));
        writer.WriteLine($"var {source} = {EmitExpression(writer, table, function, enumNames, ref tempIndex, diagnostics)};");
        if (summary.IsGrouped)
        {
            writer.WriteLine($"int {groupCount} = 0;");
        }
        for (int index = 0; index < summary.Fields.Count; index += 1)
        {
            MirTableSummaryField field = summary.Fields[index];
            string initial = summary.IsGrouped ? string.Empty : "default!";
            writer.WriteLine($"var {prefix}_field_{index} = new global::System.Collections.Generic.List<{MapType(field.Type)}> {{ {initial} }};");
        }
        if (summary.IsGrouped)
        {
            string[] keyTypes = summary.GroupKeys.Select(key => MapType(key.Type)).ToArray();
            string keyType = keyTypes.Length == 1 ? keyTypes[0] : "(" + string.Join(", ", keyTypes) + ")";
            writer.WriteLine($"var {prefix}_groups = new global::System.Collections.Generic.Dictionary<{keyType}, int>();");
        }
        var predicateNames = new List<string>();
        for (int index = 0; index < predicates.Count; index += 1)
        {
            string name = prefix + "_predicate_" + index;
            string value = EmitExpression(writer, predicates[index], function, enumNames, ref tempIndex, diagnostics);
            writer.WriteLine($"var {name} = {value};");
            predicateNames.Add(name);
        }
        writer.WriteLine($"for (int {rowIndex} = 0; {rowIndex} < {source}.Count; {rowIndex}++)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"var {row} = new {TableRowTypeName(rowTypeId)}({source}, {rowIndex});");
        for (int index = 0; index < predicates.Count; index += 1)
        {
            var predicate = new MirVariableExpression(predicateNames[index], predicates[index].Type);
            var invoke = new MirInvokeExpression(predicate, [rowExpression], new MirNamedType("boolean"));
            string condition = EmitExpression(writer, invoke, function, enumNames, ref tempIndex, diagnostics);
            writer.WriteLine($"if (!{condition}) continue;");
        }
        string ReadColumn(MirTableColumnId column, MirType type)
        {
            var access = new MirTableRowFieldAccessExpression(rowExpression, rowTypeId, column.Value + ".f", type);
            // Direct field access emits no statements and does not allocate temporaries.
            int fieldTemporary = 0;
            return EmitExpression(writer, access, function, enumNames, ref fieldTemporary, diagnostics);
        }
        if (summary.IsGrouped)
        {
            string[] keyValues = summary.GroupKeys.Select(key => ReadColumn(key.ColumnId, key.Type)).ToArray();
            string key = keyValues.Length == 1 ? keyValues[0] : "(" + string.Join(", ", keyValues) + ")";
            writer.WriteLine($"var {prefix}_key = {key};");
            writer.WriteLine($"if (!{prefix}_groups.TryGetValue({prefix}_key, out int {groupIndex}))");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"{groupIndex} = {groupCount}++;");
            writer.WriteLine($"{prefix}_groups.Add({prefix}_key, {groupIndex});");
            for (int index = 0; index < summary.Fields.Count; index += 1)
            {
                MirTableSummaryField field = summary.Fields[index];
                string initial = field.Aggregate is null ? ReadColumn(field.Input!.Value, field.Type) : "default!";
                writer.WriteLine($"{prefix}_field_{index}.Add({initial});");
            }
            writer.Unindent();
            writer.WriteLine("}");
        }
        else
        {
            writer.WriteLine($"int {groupIndex} = 0;");
        }
        for (int index = 0; index < summary.Fields.Count; index += 1)
        {
            MirTableSummaryField field = summary.Fields[index];
            string target = $"{prefix}_field_{index}[{groupIndex}]";
            if (field.Aggregate == MirTableAggregateKind.Count)
            {
                writer.WriteLine(CSharpTableAccumulator.AddCount(target));
            }
            else if (field.Aggregate == MirTableAggregateKind.Sum)
            {
                writer.WriteLine(CSharpTableAccumulator.AddSum(target, ReadColumn(field.Input!.Value, field.Type)));
            }
        }
        writer.Unindent();
        writer.WriteLine("}");
        string[] constructorValues = summary.Fields.Select((field, index) => $"{prefix}_field_{index}[{groupIndex}]").ToArray();
        // Bound fields are normalized into declared record order before lowering.
        if (!summary.IsGrouped)
        {
            string values = string.Join(", ", summary.Fields.Select((field, index) => $"{prefix}_field_{index}[0]"));
            return $"new {recordType}({values})";
        }
        writer.WriteLine($"var {prefix}_output = new {recordType}[{groupCount}];");
        writer.WriteLine($"for (int {groupIndex} = 0; {groupIndex} < {groupCount}; {groupIndex}++)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"{prefix}_output[{groupIndex}] = new {recordType}({string.Join(", ", constructorValues)});");
        writer.Unindent();
        writer.WriteLine("}");
        return prefix + "_output";
    }

}
