namespace Copeland.TS.Mir;

public static partial class MirValidator
{
    private static void ValidateTableSummary(
        MirTableSummaryExpression summary,
        IReadOnlyDictionary<MirTableId, MirTableDefinition> tables,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (!tables.TryGetValue(summary.TableId, out MirTableDefinition? table))
        {
            diagnostics.Add(new MirValidationDiagnostic("Summary references an unknown source table."));
            return;
        }
        MirIterableType expectedRows = new(new MirTableRowType(table.RowTypeId, table.Name + ".Row"));
        MirTableRowsExpression? sourceRows = summary.Source switch
        {
            MirTableRowsExpression rows => rows,
            MirTableWhereExpression { Source: MirTableRowsExpression rows } => rows,
            _ => null,
        };
        if (sourceRows is null || sourceRows.TableId != summary.TableId)
        {
            diagnostics.Add(new MirValidationDiagnostic("Summary requires a direct row view of its declared source table."));
        }
        if (summary.Source is MirTableWhereExpression filtered)
        {
            foreach (MirExpression predicate in filtered.Predicates)
            {
                if (predicate.Type is not MirCallableType { Parameters.Count: 1 } callable
                    || !MirTypeFacts.AreEquivalent(callable.Parameters[0].Type, expectedRows.ElementType)
                    || callable.ReturnType.Identifier != "boolean")
                {
                    diagnostics.Add(new MirValidationDiagnostic("Summary filter must accept its source row and return boolean."));
                }
            }
        }
        if (!MirTypeFacts.AreEquivalent(summary.Source.Type, expectedRows))
        {
            diagnostics.Add(new MirValidationDiagnostic("Summary source does not match its table row type."));
        }
        if (summary.IsGrouped != (summary.GroupKeys.Count > 0) || summary.GroupKeys.Count > 7
            || summary.GroupKeys.Select(key => key.ColumnId).Distinct().Count() != summary.GroupKeys.Count)
        {
            diagnostics.Add(new MirValidationDiagnostic("Summary grouping metadata is invalid or duplicated."));
        }
        foreach (MirTableSummaryKey key in summary.GroupKeys)
        {
            MirTableColumnDefinition? column = table.Columns.SingleOrDefault(column => column.Id == key.ColumnId);
            if (column is null || !MirTypeFacts.AreEquivalent(column.ElementType, key.Type))
            {
                diagnostics.Add(new MirValidationDiagnostic("Summary group key does not match a source column."));
            }
        }
        foreach (MirTableSummaryField field in summary.Fields)
        {
            if (field.Aggregate == MirTableAggregateKind.Count)
            {
                if (field.Input is not null || field.Type.Identifier != "int")
                {
                    diagnostics.Add(new MirValidationDiagnostic("Summary count requires no input and an int result."));
                }
                continue;
            }
            MirTableColumnDefinition? input = table.Columns.SingleOrDefault(column => column.Id == field.Input);
            if (input is null || !MirTypeFacts.AreEquivalent(input.ElementType, field.Type))
            {
                diagnostics.Add(new MirValidationDiagnostic("Summary field input does not match a source column and result type."));
                continue;
            }
            if (field.Aggregate is null)
            {
                if (!summary.GroupKeys.Any(key => key.ColumnId == field.Input))
                {
                    diagnostics.Add(new MirValidationDiagnostic("Summary copied field is not a declared group key."));
                }
            }
            else if (field.Aggregate != MirTableAggregateKind.Sum || field.Type.Identifier is not ("int" or "float" or "number"))
            {
                diagnostics.Add(new MirValidationDiagnostic("Summary supports numeric sum and int count aggregates."));
            }
        }
        if (!summary.Fields.Any(field => field.Aggregate is not null))
        {
            diagnostics.Add(new MirValidationDiagnostic("Summary requires at least one aggregate field."));
        }
    }

    private static void ValidateSummaryRecord(
        MirTableSummaryExpression summary,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (!records.TryGetValue(summary.RecordTypeId, out MirRecordDefinition? record)
            || summary.RecordType is not MirRecordType recordType || recordType.RecordTypeId != summary.RecordTypeId)
        {
            diagnostics.Add(new MirValidationDiagnostic("Summary names an unknown or inconsistent record type."));
            return;
        }
        MirType expectedResult = summary.IsGrouped ? new MirArrayType(summary.RecordType) : summary.RecordType;
        if (!MirTypeFacts.AreEquivalent(summary.Type, expectedResult)
            || summary.Fields.Count != record.Fields.Count
            || !summary.Fields.Select(field => field.FieldId).SequenceEqual(record.Fields.Select(field => field.Id)))
        {
            diagnostics.Add(new MirValidationDiagnostic("Summary result must supply its record fields once in declaration order."));
        }
        foreach (MirTableSummaryField field in summary.Fields)
        {
            MirRecordFieldDefinition? declared = record.Fields.SingleOrDefault(candidate => candidate.Id == field.FieldId);
            if (declared is null || !MirTypeFacts.AreEquivalent(declared.Type, field.Type))
            {
                diagnostics.Add(new MirValidationDiagnostic("Summary result field type does not match its declared record."));
            }
        }
    }
}
