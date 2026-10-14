using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private BoundExpression? BindTableSummary(
            MemberAccessExpressionSyntax member,
            IReadOnlyList<ExpressionSyntax> arguments,
            IReadOnlyList<TypeSyntax> typeArguments,
            TypeSymbol? contextualType)
        {
            ExpressionSyntax sourceSyntax = member.Target;
            CallExpressionSyntax? grouping = sourceSyntax as CallExpressionSyntax;
            MemberAccessExpressionSyntax? groupMember = grouping?.Target as MemberAccessExpressionSyntax;
            bool isGrouped = groupMember?.NameToken.Text == "groupBy";
            if (isGrouped)
            {
                sourceSyntax = groupMember!.Target;
            }
            BoundExpression source = BindExpression(sourceSyntax);
            if (source.Type is not TableRowsTypeSymbol rowsType)
            {
                // Ordinary callable members named summarize retain their existing binding.
                return null;
            }
            if (_currentFunction?.IsAsync == true || _currentFunction?.IsGenerator == true)
            {
                Report("COPE-SUMMARY-0011", "Materialize summaries in a synchronous helper, then call it from async or generator code; direct summary state lowering is unsupported.", member.NameToken);
                return new BoundErrorExpression();
            }
            if (source is not BoundTableRowsExpression
                && source is not BoundTableWhereExpression { Source: BoundTableRowsExpression })
            {
                Report("COPE-SUMMARY-0010", "Write rows().where(...).summarize(...) directly; stored row views are unsupported in this summary slice.", member.NameToken);
                return new BoundErrorExpression();
            }
            TypeSymbol? resultType = contextualType;
            if (typeArguments.Count == 1)
            {
                resultType = BindType(typeArguments[0], member.NameToken, "COPE-SUMMARY-0002", "summary record");
            }
            else if (typeArguments.Count > 1)
            {
                Report("COPE-SUMMARY-0002", "summarize accepts one declared record type.", member.NameToken);
                return new BoundErrorExpression();
            }
            if (isGrouped && typeArguments.Count == 0 && resultType is ArrayTypeSymbol array)
            {
                resultType = array.ElementType;
            }
            if (resultType is not RecordTypeSymbol record || record is ClassTypeSymbol)
            {
                Report("COPE-SUMMARY-0002", "Give summarize a declared immutable record type, for example summarize<Totals>(...).", member.NameToken);
                return new BoundErrorExpression();
            }
            var keys = new List<TableColumnSymbol>();
            if (isGrouped)
            {
                if (grouping!.Arguments.Count is < 1 or > 7)
                {
                    Report("COPE-SUMMARY-0006", "groupBy requires one to seven direct column selectors.", groupMember!.NameToken);
                }
                foreach (ExpressionSyntax selector in grouping.Arguments)
                {
                    WithSummaryRow(selector, rowsType.TableType, (body, row) =>
                    {
                        TableColumnSymbol? column = BindSummaryInput(body, row, rowsType.TableType);
                        if (column is null) return;
                        if (!TableAggregateRules.IsGroupable(column.Type))
                        {
                            Report("COPE-SUMMARY-0006", $"Column '{column.Name}' is not a supported scalar group key.", groupMember!.NameToken);
                        }
                        if (keys.Contains(column))
                        {
                            Report("COPE-SUMMARY-0006", $"Group key '{column.Name}' is repeated.", groupMember!.NameToken);
                        }
                        keys.Add(column);
                    });
                }
            }
            var fields = new List<BoundTableSummaryField>();
            if (arguments.Count != 1)
            {
                Report("COPE-SUMMARY-0003", "summarize requires one expression arrow returning the summary record fields.", member.NameToken);
                return new BoundErrorExpression();
            }
            BoundTableDefinition? definition = _tables.FirstOrDefault(table => table.TableType.Id == rowsType.TableType.Id);
            if (definition is null)
            {
                Report("COPE-SUMMARY-0001", "The source table requires compiler-owned relation metadata for summary binding.", member.NameToken);
                return new BoundErrorExpression();
            }
            WithSummaryRow(arguments[0], rowsType.TableType, (body, row) =>
            {
                if (body is not ObjectLiteralExpressionSyntax value)
                {
                    Report("COPE-SUMMARY-0003", "Return a record literal from the summary arrow, for example row => ({ units: sum(row.units) }).", member.NameToken);
                    return;
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (ObjectPropertySyntax property in value.Properties)
                {
                    string name = TableRowInitializers.PropertyName(property);
                    RecordFieldSymbol? field = record.Fields.SingleOrDefault(field => field.Name == name);
                    if (field is null || !names.Add(name))
                    {
                        Report("COPE-SUMMARY-0004", $"Summary field '{name}' is unknown or duplicated in '{record.Name}'.", property.NameToken);
                        continue;
                    }
                    ExpressionSyntax expression = UnwrapSummarySyntax(property.ValueExpression);
                    TableAggregateKind? aggregate = null;
                    TableColumnSymbol? input = null;
                    TypeSymbol? fieldType = null;
                    if (expression is CallExpressionSyntax { Target: NameExpressionSyntax operation } call)
                    {
                        if (operation.IdentifierToken.Text == "count" && call.Arguments.Count == 0)
                        {
                            aggregate = TableAggregateKind.Count;
                            fieldType = PrimitiveTypeSymbol.Int;
                        }
                        else if (operation.IdentifierToken.Text == "sum" && call.Arguments.Count == 1)
                        {
                            aggregate = TableAggregateKind.Sum;
                            input = BindSummaryInput(call.Arguments[0], row, rowsType.TableType);
                            fieldType = input?.Type;
                            if (input is not null && !TableAggregateRules.SupportsSum(input.Type))
                            {
                                Report("COPE-SUMMARY-0005", "sum requires an int, float, or number column.", property.NameToken);
                            }
                        }
                        else
                        {
                            Report("COPE-SUMMARY-0007", "This summary slice supports sum(column) and count(). Derive calculated inputs before summarizing; average/min/max require a separate empty-input design.", operation.IdentifierToken);
                        }
                    }
                    else
                    {
                        input = BindSummaryInput(expression, row, rowsType.TableType);
                        fieldType = input?.Type;
                        if (input is not null && !keys.Contains(input))
                        {
                            Report("COPE-SUMMARY-0008", $"Column '{input.Name}' is not a group key; aggregate it or add it to groupBy.", property.NameToken);
                        }
                    }
                    if (fieldType is null) continue;
                    if (!TypeFacts.AreEquivalent(field.Type, fieldType))
                    {
                        Report("COPE-SUMMARY-0005", $"Summary field '{name}' requires '{field.Type.Name}', got '{fieldType.Name}'.", property.NameToken);
                    }
                    TableQueryColumnProvenance provenance = input is null
                        ? new("aggregate", definition.TableType.Name, [], [], "count")
                        : TableQueryBinder.Provenance(definition, input);
                    if (aggregate is not null && input is not null)
                    {
                        provenance = provenance with { Kind = "aggregate", Aggregate = "sum" };
                    }
                    fields.Add(new BoundTableSummaryField(field, input, aggregate, provenance));
                }
                foreach (RecordFieldSymbol missing in record.Fields.Where(field => !names.Contains(field.Name)))
                {
                    Report("COPE-SUMMARY-0004", $"Summary is missing field '{missing.Name}' of '{record.Name}'.", member.NameToken);
                }
            });
            if (!fields.Any(field => field.Aggregate is not null))
            {
                Report("COPE-SUMMARY-0009", "A summary requires at least one aggregate field.", member.NameToken);
            }
            fields.Sort((left, right) => left.Field.Id.Ordinal.CompareTo(right.Field.Id.Ordinal));
            return new BoundTableSummaryExpression(source, rowsType.TableType, record, keys, fields, isGrouped);
        }

        private void WithSummaryRow(ExpressionSyntax syntax, TableTypeSymbol table, Action<ExpressionSyntax, VariableSymbol> bind)
        {
            syntax = UnwrapSummarySyntax(syntax);
            if (syntax is not ArrowExpressionSyntax { Parameters.Count: 1, ExpressionBody: not null, BlockBody: null } arrow)
            {
                Report("COPE-SUMMARY-0003", "Summary descriptors require an expression arrow with one row parameter; statement bodies and captures are unsupported.", InferenceAnchor(syntax));
                return;
            }
            if (arrow.ReturnType is not null)
            {
                Report("COPE-SUMMARY-0003", "Summary descriptors take their result type from summarize<T>(); remove the arrow return annotation.", arrow.ArrowToken);
            }
            ArrowParameterSyntax parameter = arrow.Parameters[0];
            if (parameter.Type is not null && !TypeFacts.AreEquivalent(
                BindType(parameter.Type, parameter.Identifier, "COPE-SUMMARY-0005", "summary row"), table.RowType))
            {
                Report("COPE-SUMMARY-0005", "Summary selector parameter must match the source row type.", parameter.Identifier);
            }
            var row = new VariableSymbol(parameter.Identifier.Text, table.RowType, true);
            Scope previous = _scope;
            _scope = new Scope(_global);
            _scope.TryDeclare(row);
            try
            {
                bind(UnwrapSummarySyntax(arrow.ExpressionBody), row);
            }
            finally
            {
                _scope = previous;
            }
        }

        private TableColumnSymbol? BindSummaryInput(ExpressionSyntax syntax, VariableSymbol row, TableTypeSymbol table)
        {
            syntax = UnwrapSummarySyntax(syntax);
            if (syntax is MemberAccessExpressionSyntax { Target: NameExpressionSyntax name } member
                && name.IdentifierToken.Text == row.Name)
            {
                BoundExpression expression = BindExpression(member);
                if (expression is BoundTableRowFieldAccessExpression access)
                {
                    return table.Columns.Single(column => column.Id == access.Field.Id.ColumnId);
                }
                return null;
            }
            Report("COPE-SUMMARY-0008", "Use a direct source-row column here; compute expressions in derive before summarizing.", InferenceAnchor(syntax));
            return null;
        }

        private static ExpressionSyntax UnwrapSummarySyntax(ExpressionSyntax syntax)
        {
            while (syntax is ParenthesizedExpressionSyntax parenthesized)
            {
                syntax = parenthesized.Expression;
            }
            return syntax;
        }
    }
}
