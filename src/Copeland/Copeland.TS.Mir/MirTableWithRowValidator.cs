namespace Copeland.TS.Mir;

public static partial class MirValidator
{
    private static void ValidateTableWithRow(
        MirTableWithRowExpression update,
        IReadOnlyDictionary<MirTableId, MirTableDefinition> tables,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (!tables.TryGetValue(update.TableId, out MirTableDefinition? table)
            || table.DerivedPlan is not null
            || table.KeyColumnId != update.KeyColumnId
            || update.Source.Type is not MirTableType sourceType
            || sourceType.TableId != update.TableId
            || update.ResultType.SuccessType is not MirTableType successType
            || successType.TableId != update.TableId
            || update.ResultType.ErrorType.Name != "TableKeyError"
            || !MirTypeFacts.AreEquivalent(update.Type, update.ResultType))
        {
            diagnostics.Add(new MirValidationDiagnostic("Table withRow requires an authored keyed table and its nominal Result<Table, TableKeyError>."));
            return;
        }
        MirTableColumnDefinition? key = table.Columns.FirstOrDefault(column => column.Id == update.KeyColumnId);
        if (key is null || !MirTypeFacts.AreEquivalent(key.ElementType, update.Key.Type))
        {
            diagnostics.Add(new MirValidationDiagnostic("Table withRow key identity or type does not match its declared key."));
        }
        if (update.Replacements.Count == 0)
        {
            diagnostics.Add(new MirValidationDiagnostic("Table withRow requires at least one cell replacement."));
        }
        var seen = new HashSet<MirTableColumnId>();
        foreach (MirTableCellReplacement field in update.Replacements)
        {
            MirTableColumnDefinition? column = table.Columns.FirstOrDefault(candidate => candidate.Id == field.ColumnId);
            if (column is null || !seen.Add(field.ColumnId)
                || column.Id == table.KeyColumnId || column.Reference is not null
                || !MirTypeFacts.AreEquivalent(column.ElementType, field.Value.Type))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Table withRow field '{field.ColumnId}' is unknown, repeated, protected, or has an incompatible type."));
            }
        }
    }
}
