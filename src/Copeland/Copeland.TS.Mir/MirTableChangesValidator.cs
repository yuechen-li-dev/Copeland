namespace Copeland.TS.Mir;

public static partial class MirValidator
{
    private static void ValidateTableChangeErrors(IReadOnlyList<MirEnum> enums, List<MirValidationDiagnostic> diagnostics)
    {
        Check("TableEditError", [
            ("DuplicateKey", ["table", "key"]), ("NotFound", ["table", "key"]),
            ("ConflictingEdit", ["table", "key"]), ("MissingReference", ["table", "column", "key"]),
            ("RowLimit", ["table"]),
        ]);
        Check("TableRelationError", [("Unresolved", ["relation"]), ("NonUnique", ["relation"])]);

        void Check(string name, (string Name, string[] Fields)[] expected)
        {
            MirEnum[] definitions = enums.Where(item => item.Name == name).ToArray();
            if (definitions.Length == 0) return;
            if (definitions.Length != 1 || definitions[0].Cases.Count != expected.Length
                || definitions[0].Cases.Zip(expected).Any(pair => pair.First.Name != pair.Second.Name
                    || !pair.First.PayloadFields.Select(field => field.Name).SequenceEqual(pair.Second.Fields)
                    || pair.First.PayloadFields.Any(field => field.Type.Identifier != "string")))
            {
                diagnostics.Add(new($"{name} does not have its required compiler-owned cases and string payloads."));
            }
        }
    }

    private static void ValidateTableChanges(MirTableChangesExpression changes,
        IReadOnlyDictionary<MirTableId, MirTableDefinition> tables, List<MirValidationDiagnostic> diagnostics)
    {
        if (changes.Source.Type is not MirRecordType source || source.RecordTypeId != changes.RecordId
            || changes.ResultType.SuccessType is not MirRecordType result || result.RecordTypeId != changes.RecordId
            || changes.ResultType.ErrorType is not MirNamedType { Identifier: "TableEditError" }
            || !MirTypeFacts.AreEquivalent(changes.Type, changes.ResultType))
        {
            diagnostics.Add(new("Table changes require a nominal input record and Result<Record, TableEditError>."));
        }
        if (changes.Operations.Count > 65_536 || changes.Fields.Count == 0 || changes.Fields.Select(field => field.TableId).Distinct().Count() != changes.Fields.Count
            || changes.Fields.Select(field => field.FieldId).Distinct().Count() != changes.Fields.Count)
        {
            diagnostics.Add(new("Table change fields must identify each owned table exactly once."));
        }
        foreach (MirTableChangeField field in changes.Fields)
        {
            if (!tables.TryGetValue(field.TableId, out var table) || table.DerivedPlan is not null || table.KeyColumnId is null)
            {
                diagnostics.Add(new("Table changes require authored keyed tables."));
                continue;
            }
            if (table.Columns.Any(column => column.Reference is { } reference && !changes.Fields.Any(target => target.TableId == reference.TargetTableId)))
            {
                diagnostics.Add(new("Table change inputs must close every declared reference target."));
            }
        }
        foreach (MirTableChangeOperation operation in changes.Operations)
        {
            if (!changes.Fields.Contains(new(operation.FieldId, operation.TableId)) || !tables.TryGetValue(operation.TableId, out var table))
            {
                diagnostics.Add(new("Table operation targets an unknown input field."));
                continue;
            }
            if (operation.Row is not null)
            {
                if (!operation.Insert || operation.Values.Count != 0 || operation.Row.Type is not MirTableRowType row || row.RowTypeId != table.RowTypeId)
                    diagnostics.Add(new("Table insertion row must have the exact nominal row type."));
                continue;
            }
            var expected = operation.Insert ? table.Columns.Select(column => column.Id).ToHashSet() : new HashSet<MirTableColumnId> { table.KeyColumnId!.Value };
            if (!expected.SetEquals(operation.Values.Select(value => value.ColumnId)) || operation.Values.Count != expected.Count)
            {
                diagnostics.Add(new("Table operation fields must be a complete insert row or an exact delete key."));
            }
            foreach (MirTableCellReplacement value in operation.Values)
            {
                var column = table.Columns.FirstOrDefault(column => column.Id == value.ColumnId);
                if (column is null || !MirTypeFacts.AreEquivalent(column.ElementType, value.Value.Type))
                    diagnostics.Add(new("Table operation field identity/type is invalid."));
            }
        }
    }
}
