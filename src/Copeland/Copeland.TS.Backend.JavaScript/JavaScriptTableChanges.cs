using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

public static partial class JavaScriptBackend
{
    private static EmittedExpression EmitTableChanges(MirTableChangesExpression changes, MirFunction function,
        EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        var prelude = new List<EmittedLine>();
        string source = MaterializeTableOperand(changes.Source, "changes_source", prelude, function, catalog, results, names, flowEnabled);
        var payloads = new List<Dictionary<MirTableColumnId, string>>();
        foreach (MirTableChangeOperation operation in changes.Operations)
        {
            var cells = new Dictionary<MirTableColumnId, string>();
            if (operation.Row is not null)
            {
                string row = MaterializeTableOperand(operation.Row, "changes_row", prelude, function, catalog, results, names, flowEnabled);
                foreach (var column in catalog.GetTable(operation.TableId).Columns)
                {
                    cells.Add(column.Id, $"{row}[{names.TableRowTableSlot}][{names.TableColumnSlot(column)}][{names.ColumnReadSlot}]({row}[{names.TableRowIndexSlot}]).$payload[0]");
                }
            }
            else
            {
                foreach (MirTableCellReplacement value in operation.Values)
                    cells.Add(value.ColumnId, MaterializeTableOperand(value.Value, "changes_cell", prelude, function, catalog, results, names, flowEnabled));
            }
            payloads.Add(cells);
        }
        string result = names.NextTemporary("changes_result");
        prelude.Add(new($"const {result} = (() => {{", 0));
        var snapshots = new Dictionary<MirTableId, string>();
        var record = catalog.GetRecord(changes.RecordId);
        foreach (MirTableChangeField field in changes.Fields)
        {
            string name = names.NextTemporary("changes_table");
            snapshots.Add(field.TableId, name);
            var definition = record.Fields.Single(candidate => candidate.Id == field.FieldId);
            string read = IsProduction(names) ? $"{source}.{ProductionRecordFieldName(record, definition)}" : $"{source}[{names.RecordFieldSlot(definition)}]";
            Add($"let {name} = {read};");
            Add($"const {name}_touched = new Set();");
        }
        for (int index = 0; index < changes.Operations.Count; index++)
        {
            var operation = changes.Operations[index];
            var table = catalog.GetTable(operation.TableId);
            var keyColumn = catalog.GetTableColumn(table.KeyColumnId!.Value);
            string snapshot = snapshots[table.Id];
            string key = payloads[index][keyColumn.Id];
            string location = JavaScriptLiteralWriter.WriteString(table.Name);
            Add($"if ({snapshot}_touched.has({key})) return {Error("ConflictingEdit", location, $"String({key})")};");
            Add($"{snapshot}_touched.add({key});");
            string position = names.NextTemporary("changes_position");
            string cursor = names.NextTemporary("changes_cursor");
            Add($"let {position} = -1;");
            Add($"for (let {cursor} = 0; {cursor} < {SnapshotCount(snapshot)}; {cursor} += 1) {{");
            Add($"if ({Read(snapshot, keyColumn, cursor)} === {key}) {{ {position} = {cursor}; break; }}", 2);
            Add("}");
            if (operation.Insert)
            {
                Add($"if ({position} >= 0) return {Error("DuplicateKey", location, $"String({key})")};");
            }
            else
            {
                Add($"if ({position} < 0) return {Error("NotFound", location, $"String({key})")};");
            }
            var arrays = new List<string>();
            foreach (MirTableColumnDefinition column in table.Columns)
            {
                string values = names.NextTemporary("changes_column");
                Add($"const {values} = Array.from({{ length: {SnapshotCount(snapshot)} }}, (_, index) => {Read(snapshot, column, "index")});");
                if (operation.Insert) Add($"{values}.push({payloads[index][column.Id]});");
                else Add($"{values}.splice({position}, 1);");
                arrays.Add(values);
            }
            Add($"{snapshot} = {names.TableCreate(table)}(null, [{string.Join(", ", arrays)}]);");
        }
        foreach (MirTableChangeField field in changes.Fields)
        {
            var table = catalog.GetTable(field.TableId);
            string snapshot = snapshots[table.Id];
            var key = catalog.GetTableColumn(table.KeyColumnId!.Value);
            Add($"if ({SnapshotCount(snapshot)} > {MirTableChangesExpression.MaximumRows}) return {Error("RowLimit", JavaScriptLiteralWriter.WriteString(table.Name))};");
            Add($"const {snapshot}_keys = new Set();");
            Add($"for (let index = 0; index < {SnapshotCount(snapshot)}; index += 1) {{");
            Add($"const key = {Read(snapshot, key, "index")};", 2);
            Add($"if ({snapshot}_keys.has(key)) return {Error("DuplicateKey", JavaScriptLiteralWriter.WriteString(table.Name), "String(key)")};", 2);
            Add($"{snapshot}_keys.add(key);", 2);
            Add("}");
        }
        foreach (MirTableChangeField field in changes.Fields)
        {
            var table = catalog.GetTable(field.TableId);
            string snapshot = snapshots[table.Id];
            foreach (var column in table.Columns.Where(column => column.Reference is not null))
            {
                string target = snapshots[column.Reference!.TargetTableId];
                Add($"for (let index = 0; index < {SnapshotCount(snapshot)}; index += 1) {{");
                Add($"const key = {Read(snapshot, column, "index")};", 2);
                Add($"if (!{target}_keys.has(key)) return {Error("MissingReference", JavaScriptLiteralWriter.WriteString(table.Name), JavaScriptLiteralWriter.WriteString(column.Name), "String(key)")};", 2);
                Add("}");
            }
        }
        string arguments = string.Join(", ", record.Fields.Select(FieldValue));
        Add($"return {names.MakeValue}({names.TypeToken(results.Get(changes.ResultType))}, \"ok\", [{RecordConstructionName(record, names)}({arguments})]);");
        prelude.Add(new("})();", 0));
        return new(prelude, result);

        string FieldValue(MirRecordFieldDefinition field)
        {
            var input = changes.Fields.FirstOrDefault(input => input.FieldId == field.Id);
            if (input is not null)
            {
                return snapshots[input.TableId];
            }
            if (IsProduction(names))
            {
                return $"{source}.{ProductionRecordFieldName(record, field)}";
            }
            return $"{source}[{names.RecordFieldSlot(field)}]";
        }

        void Add(string text, int indent = 1) => prelude.Add(new(text, indent));
        string Read(string snapshot, MirTableColumnDefinition column, string index)
            => $"{snapshot}[{names.TableColumnSlot(column)}][{names.ColumnReadSlot}]({index}).$payload[0]";
        string Error(string name, params string[] arguments)
        {
            var definition = catalog.GetEnum("TableEditError");
            string error = IsProduction(names)
                ? EmitProductionEnumConstruction(definition, definition.Definition.Cases.Single(item => item.Name == name), names, arguments)
                : $"{names.MakeValue}({names.TypeToken(definition)}, {JavaScriptLiteralWriter.WriteString(name)}, [{string.Join(", ", arguments)}])";
            return $"{names.MakeValue}({names.TypeToken(results.Get(changes.ResultType))}, \"err\", [{error}])";
        }
    }
}
