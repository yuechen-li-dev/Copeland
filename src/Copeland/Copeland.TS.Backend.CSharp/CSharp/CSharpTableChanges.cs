using System.Text.Json;
using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

public static partial class CSharpBackend
{
    private static void EmitTableCardinalityMethods(CSharpTextWriter writer, MirTableDefinition table)
    {
        string type = TableTypeName(table.Id);
        string arguments = string.Join(", ", table.Columns.Select((column, index) => MapType(column.ElementType) + " cell" + index));
        writer.WriteLine($"internal {type} InsertRow({arguments})");
        writer.WriteLine("{");
        writer.Indent();
        for (int index = 0; index < table.Columns.Count; index++)
        {
            MirTableColumnDefinition column = table.Columns[index];
            writer.WriteLine($"var values{index} = new {MapType(column.ElementType)}[Count + 1];");
            writer.WriteLine($"global::System.Array.Copy({TableStorageName(column.Id)}, values{index}, Count);");
            writer.WriteLine($"values{index}[Count] = cell{index};");
        }
        writer.WriteLine($"return new {type}({string.Join(", ", table.Columns.Select((_, index) => "values" + index))});");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"internal {type} DeleteRow(int index)");
        writer.WriteLine("{");
        writer.Indent();
        for (int index = 0; index < table.Columns.Count; index++)
        {
            MirTableColumnDefinition column = table.Columns[index];
            writer.WriteLine($"var values{index} = new {MapType(column.ElementType)}[Count - 1];");
            writer.WriteLine($"global::System.Array.Copy({TableStorageName(column.Id)}, 0, values{index}, 0, index);");
            writer.WriteLine($"global::System.Array.Copy({TableStorageName(column.Id)}, index + 1, values{index}, index, Count - index - 1);");
        }
        writer.WriteLine($"return new {type}({string.Join(", ", table.Columns.Select((_, index) => "values" + index))});");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static string EmitTableChanges(CSharpTextWriter writer, MirTableChangesExpression changes,
        MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        var tables = CurrentTables.Value!;
        var record = GetRecordDefinition(changes.RecordId);
        string prefix = "__cope_changes_" + tempIndex++;
        string resultType = MapType(changes.ResultType);
        string source = EmitExpression(writer, changes.Source, function, enumNames, ref tempIndex, diagnostics);
        writer.WriteLine($"var {prefix}_source = {source};");
        var payloads = new List<Dictionary<MirTableColumnId, string>>();
        for (int index = 0; index < changes.Operations.Count; index++)
        {
            MirTableChangeOperation operation = changes.Operations[index];
            var cells = new Dictionary<MirTableColumnId, string>();
            if (operation.Row is not null)
            {
                string row = EmitExpression(writer, operation.Row, function, enumNames, ref tempIndex, diagnostics);
                writer.WriteLine($"var {prefix}_row_{index} = {row};");
                foreach (var column in tables[operation.TableId].Columns)
                    cells.Add(column.Id, prefix + "_row_" + index + "." + TableRowFieldName(column.Id));
            }
            else
            {
                for (int cellIndex = 0; cellIndex < operation.Values.Count; cellIndex++)
                {
                    MirTableCellReplacement value = operation.Values[cellIndex];
                    string expression = EmitExpression(writer, value.Value, function, enumNames, ref tempIndex, diagnostics);
                    string name = prefix + "_cell_" + index + "_" + cellIndex;
                    writer.WriteLine($"var {name} = {expression};");
                    cells.Add(value.ColumnId, name);
                }
            }
            payloads.Add(cells);
        }
        writer.WriteLine($"{resultType} {prefix}_apply()");
        writer.WriteLine("{");
        writer.Indent();
        var snapshots = new Dictionary<MirTableId, string>();
        foreach (MirTableChangeField field in changes.Fields)
        {
            string name = prefix + "_table_" + snapshots.Count;
            snapshots.Add(field.TableId, name);
            var table = tables[field.TableId];
            var key = table.Columns.Single(column => column.Id == table.KeyColumnId);
            writer.WriteLine($"var {name} = {prefix}_source.{RecordFieldName(field.FieldId)};");
            writer.WriteLine($"var {name}_touched = new global::System.Collections.Generic.HashSet<{MapType(key.ElementType)}>();");
        }
        for (int index = 0; index < changes.Operations.Count; index++)
        {
            MirTableChangeOperation operation = changes.Operations[index];
            var table = tables[operation.TableId];
            string snapshot = snapshots[table.Id];
            string key = payloads[index][table.KeyColumnId!.Value];
            string keyText = $"global::System.Convert.ToString({key}, global::System.Globalization.CultureInfo.InvariantCulture)!";
            string location = JsonSerializer.Serialize(table.Name);
            writer.WriteLine($"if (!{snapshot}_touched.Add({key})) return {resultType}.Err(new TableEditError.ConflictingEdit({location}, {keyText}));");
            writer.WriteLine($"int {prefix}_index_{index} = {snapshot}.FindKey({key});");
            string position = prefix + "_index_" + index;
            if (operation.Insert)
            {
                writer.WriteLine($"if ({position} >= 0) return {resultType}.Err(new TableEditError.DuplicateKey({location}, {keyText}));");
                writer.WriteLine($"{snapshot} = {snapshot}.InsertRow({string.Join(", ", table.Columns.Select(column => payloads[index][column.Id]))});");
            }
            else
            {
                writer.WriteLine($"if ({position} < 0) return {resultType}.Err(new TableEditError.NotFound({location}, {keyText}));");
                writer.WriteLine($"{snapshot} = {snapshot}.DeleteRow({position});");
            }
        }
        foreach (MirTableChangeField field in changes.Fields)
        {
            var table = tables[field.TableId];
            var key = table.Columns.Single(column => column.Id == table.KeyColumnId);
            string snapshot = snapshots[table.Id];
            writer.WriteLine($"if ({snapshot}.Count > {MirTableChangesExpression.MaximumRows}) return {resultType}.Err(new TableEditError.RowLimit({JsonSerializer.Serialize(table.Name)}));");
            writer.WriteLine($"var {snapshot}_keys = new global::System.Collections.Generic.HashSet<{MapType(key.ElementType)}>();");
            writer.WriteLine($"for (int index = 0; index < {snapshot}.Count; index++)");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"var key = {snapshot}.{TableReadMethodName(key.Id)}(index);");
            writer.WriteLine($"if (!{snapshot}_keys.Add(key)) return {resultType}.Err(new TableEditError.DuplicateKey({JsonSerializer.Serialize(table.Name)}, global::System.Convert.ToString(key, global::System.Globalization.CultureInfo.InvariantCulture)!));");
            writer.Unindent();
            writer.WriteLine("}");
        }
        foreach (MirTableChangeField field in changes.Fields)
        {
            var table = tables[field.TableId];
            string snapshot = snapshots[table.Id];
            foreach (MirTableColumnDefinition column in table.Columns.Where(column => column.Reference is not null))
            {
                string target = snapshots[column.Reference!.TargetTableId];
                writer.WriteLine($"for (int index = 0; index < {snapshot}.Count; index++)");
                writer.WriteLine("{");
                writer.Indent();
                writer.WriteLine($"var key = {snapshot}.{TableReadMethodName(column.Id)}(index);");
                writer.WriteLine($"if (!{target}_keys.Contains(key)) return {resultType}.Err(new TableEditError.MissingReference({JsonSerializer.Serialize(table.Name)}, {JsonSerializer.Serialize(column.Name)}, global::System.Convert.ToString(key, global::System.Globalization.CultureInfo.InvariantCulture)!));");
                writer.Unindent();
                writer.WriteLine("}");
            }
        }
        string arguments = string.Join(", ", record.Fields.Select(field => changes.Fields.FirstOrDefault(input => input.FieldId == field.Id) is { } input
            ? snapshots[input.TableId] : prefix + "_source." + RecordFieldName(field.Id)));
        writer.WriteLine($"return {resultType}.Ok(new {RecordTypeName(record.Id)}({arguments}));");
        writer.Unindent();
        writer.WriteLine("}");
        return prefix + "_apply()";
    }
}
