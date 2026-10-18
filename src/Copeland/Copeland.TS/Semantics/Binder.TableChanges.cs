using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private EnumTypeSymbol? _tableEditErrorType;
        private EnumTypeSymbol? _tableRelationErrorType;
        private bool _usesTableEditError;
        private bool _usesTableRelationError;

        private void PredeclareTableChangeErrors()
        {
            _tableEditErrorType = Declare("TableEditError", [
                ("DuplicateKey", ["table", "key"]),
                ("NotFound", ["table", "key"]),
                ("ConflictingEdit", ["table", "key"]),
                ("MissingReference", ["table", "column", "key"]),
                ("RowLimit", ["table"]),
            ]);
            _tableRelationErrorType = Declare("TableRelationError", [
                ("Unresolved", ["relation"]), ("NonUnique", ["relation"]),
            ]);

            EnumTypeSymbol Declare(string name, (string Name, string[] Fields)[] cases)
            {
                var type = new EnumTypeSymbol(name, "intrinsic:" + name);
                foreach (var item in cases)
                {
                    type.AddCase(new EnumCaseSymbol(item.Name, type,
                        item.Fields.Select(field => new EnumPayloadFieldSymbol(field, PrimitiveTypeSymbol.String)).ToArray()));
                }
                _enumTypes.Add(name, type);
                _global.TryDeclare(new VariableSymbol(name, type, true));
                return type;
            }
        }

        private BoundExpression BindTableChanges(CallExpressionSyntax call, SyntaxToken anchor)
        {
            _usesTableEditError = true;
            if (_currentFunction?.IsAsync == true || _currentFunction?.IsGenerator == true)
            {
                return Fail("COPE-CHANGE-0006", "Apply table changes in a synchronous helper.", anchor);
            }
            if (call.Arguments.Count != 2 || BindExpression(call.Arguments[0]) is not { Type: RecordTypeSymbol record } source
                || UnwrapSummarySyntax(call.Arguments[1]) is not ObjectLiteralExpressionSyntax changes)
            {
                return Fail("COPE-CHANGE-0001", "Tables.apply requires an input record and a literal of table change arrays.", anchor);
            }
            var fields = record.Fields.Where(field => field.Type is TableTypeSymbol).ToArray();
            var tables = fields.Select(field => (TableTypeSymbol)field.Type).ToArray();
            if (record is ClassTypeSymbol || tables.Length == 0 || tables.Select(table => table.Id).Distinct().Count() != tables.Length
                || tables.Any(table => table.KeyColumn is null || AvailableTableDefinitions.Any(definition => definition.TableType.Id == table.Id && definition is BoundDerivedTableDefinition)))
            {
                return Fail("COPE-CHANGE-0002", "Input records require one snapshot per nominal authored keyed table; derived or ambiguous snapshots are unsupported.", anchor);
            }
            foreach (TableTypeSymbol table in tables)
            {
                if (table.Columns.Any(column => column.Reference is { } reference && !tables.Any(target => target.Id == reference.TargetTable.Id)))
                {
                    return Fail("COPE-CHANGE-0002", $"Input record must include every reference target of '{table.Name}'; global fallbacks are not used.", anchor);
                }
            }
            var operations = new List<BoundTableChangeOperation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectPropertySyntax property in changes.Properties)
            {
                string name = TableRowInitializers.PropertyName(property);
                RecordFieldSymbol? field = fields.FirstOrDefault(candidate => candidate.Name == name);
                if (field is null || !seen.Add(name) || UnwrapSummarySyntax(property.ValueExpression) is not ArrayLiteralExpressionSyntax array)
                {
                    return Fail("COPE-CHANGE-0003", "Name each input table field once, with an explicit array of insertRow/deleteRow descriptions.", property.NameToken);
                }
                var table = (TableTypeSymbol)field.Type;
                foreach (ExpressionSyntax item in array.Elements)
                {
                    if (UnwrapSummarySyntax(item) is not CallExpressionSyntax operation || operation.Arguments.Count != 1
                        || operation.Target is not MemberAccessExpressionSyntax member
                        || BindExpression(member.Target) is not BoundTableReferenceExpression receiver || receiver.TableType.Id != table.Id
                        || member.NameToken.Text is not ("insertRow" or "deleteRow"))
                    {
                        return Fail("COPE-CHANGE-0003", $"Use {table.Name}.insertRow(row) or {table.Name}.deleteRow({{ {table.KeyColumn!.Name}: key }}), directly inside Tables.apply.", property.NameToken);
                    }
                    bool insert = member.NameToken.Text == "insertRow";
                    if (UnwrapSummarySyntax(operation.Arguments[0]) is not ObjectLiteralExpressionSyntax literal)
                    {
                        BoundExpression row = BindExpression(operation.Arguments[0], table.RowType);
                        if (!insert || !TypeFacts.AreEquivalent(row.Type, table.RowType))
                        {
                            return Fail("COPE-CHANGE-0004", "Insert a complete row literal or the exact nominal table row; delete with an exact key literal.", member.NameToken);
                        }
                        operations.Add(new(field, table, true, row, []));
                        continue;
                    }
                    TableColumnSymbol[] expected = insert ? table.Columns.ToArray() : [table.KeyColumn!];
                    var values = new List<BoundTableCellReplacement>();
                    var supplied = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ObjectPropertySyntax cell in literal.Properties)
                    {
                        string cellName = TableRowInitializers.PropertyName(cell);
                        TableColumnSymbol? column = expected.FirstOrDefault(candidate => candidate.Name == cellName);
                        if (column is null || !supplied.Add(cellName))
                        {
                            return Fail("COPE-CHANGE-0004", "Supply each declared row field exactly once, or only the declared key for deletion.", cell.NameToken);
                        }
                        BoundExpression value = BindExpression(cell.ValueExpression, column.Type);
                        if (!IsAssignable(column.Type, value.Type))
                        {
                            ReportTypeMismatch("COPE-CHANGE-0005", column.Type, value.Type, cell.NameToken);
                        }
                        values.Add(new(column, value));
                    }
                    if (supplied.Count != expected.Length)
                    {
                        return Fail("COPE-CHANGE-0004", "Insertion requires every row field; deletion requires the declared key.", member.NameToken);
                    }
                    operations.Add(new(field, table, insert, null, values));
                }
            }
            if (operations.Count > 65_536)
            {
                return Fail("COPE-CHANGE-0007", "Split batches exceeding 65536 explicit row operations.", anchor);
            }
            return new BoundTableChangesExpression(source, record, fields, operations, new ResultTypeSymbol(record, _tableEditErrorType!));
        }

        private BoundExpression Fail(string id, string message, SyntaxToken anchor)
        {
            Report(id, message, anchor);
            return new BoundErrorExpression();
        }
    }
}
