using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private BoundExpression BindTableWithRow(
            CallExpressionSyntax call,
            MemberAccessExpressionSyntax member,
            BoundExpression source,
            TableTypeSymbol tableType)
        {
            _usesTableKeyError = true;
            BoundTableDefinition? definition = AvailableTableDefinitions.FirstOrDefault(table => table.TableType.Id == tableType.Id);
            if (definition is BoundDerivedTableDefinition || tableType.KeyColumn is not { } keyColumn)
            {
                Report("COPE-ROW-0001", "withRow requires an authored table with a declared key; edit its inputs, then recalculate derived tables.", member.NameToken);
                return new BoundErrorExpression();
            }
            if (_currentFunction?.IsAsync == true || _currentFunction?.IsGenerator == true)
            {
                Report("COPE-ROW-0006", "Edit tables in a synchronous helper, then call it from async or generator code.", member.NameToken);
                return new BoundErrorExpression();
            }
            if (call.Arguments.Count != 2
                || UnwrapSummarySyntax(call.Arguments[0]) is not ObjectLiteralExpressionSyntax selector
                || selector.Properties.Count != 1
                || TableRowInitializers.PropertyName(selector.Properties[0]) != keyColumn.Name
                || UnwrapSummarySyntax(call.Arguments[1]) is not ObjectLiteralExpressionSyntax patch
                || patch.Properties.Count == 0)
            {
                Report("COPE-ROW-0002", $"withRow requires {{ {keyColumn.Name}: key }} and a nonempty field patch literal.", member.NameToken);
                return new BoundErrorExpression();
            }

            ObjectPropertySyntax keyProperty = selector.Properties[0];
            BoundExpression key = BindExpression(keyProperty.ValueExpression, keyColumn.Type);
            if (!TypeFacts.AreEquivalent(keyColumn.Type, key.Type))
            {
                Report("COPE-ROW-0003", $"Key '{keyColumn.Name}' requires '{keyColumn.Type.Name}', got '{key.Type.Name}'.", keyProperty.NameToken);
            }
            var replacements = new List<BoundTableCellReplacement>();
            var seen = new HashSet<TableColumnId>();
            foreach (ObjectPropertySyntax property in patch.Properties)
            {
                string name = TableRowInitializers.PropertyName(property);
                TableColumnSymbol? column = tableType.Columns.FirstOrDefault(candidate => candidate.Name == name);
                if (column is null || !seen.Add(column.Id))
                {
                    Report("COPE-ROW-0004", $"Patch field '{name}' is unknown or duplicated in '{tableType.Name}'.", property.NameToken);
                    continue;
                }
                if (column == keyColumn || column.Reference is not null)
                {
                    Report("COPE-ROW-0005", $"withRow cannot change key or reference field '{name}'; coordinated identity edits are unsupported.", property.NameToken);
                    continue;
                }
                BoundExpression value = BindExpression(property.ValueExpression, column.Type);
                if (!IsAssignable(column.Type, value.Type))
                {
                    Report("COPE-ROW-0003", $"Field '{name}' requires '{column.Type.Name}', got '{value.Type.Name}'.", property.NameToken);
                }
                replacements.Add(new BoundTableCellReplacement(column, value));
            }
            return new BoundTableWithRowExpression(
                source, tableType, keyColumn, key, replacements,
                new ResultTypeSymbol(tableType, _tableKeyErrorType!));
        }
    }
}
