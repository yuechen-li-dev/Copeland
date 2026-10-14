using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private BoundExpression BindTableRecalculation(
            CallExpressionSyntax call,
            MemberAccessExpressionSyntax member,
            BoundExpression receiver,
            TableTypeSymbol tableType)
        {
            BoundTableDefinition? definition = _tables.FirstOrDefault(table => table.TableType.Id == tableType.Id);
            if (receiver is not BoundTableReferenceExpression || definition is not BoundDerivedTableDefinition)
            {
                Report("COPE-RECALC-0001", "Call recalculate on a declared derived table, for example Plan.recalculate({ Stock: revisedStock }).", member.NameToken);
                return new BoundErrorExpression();
            }
            if (_currentFunction?.IsAsync == true || _currentFunction?.IsGenerator == true)
            {
                Report("COPE-RECALC-0005", "Recalculate in a synchronous helper, then call it from async or generator code; direct recalculation state lowering is unsupported.", member.NameToken);
                return new BoundErrorExpression();
            }
            if (call.Arguments.Count != 1
                || UnwrapSummarySyntax(call.Arguments[0]) is not ObjectLiteralExpressionSyntax replacements
                || replacements.Properties.Count == 0)
            {
                Report("COPE-RECALC-0002", "recalculate requires one nonempty input literal naming authored dependencies, for example { Stock: revisedStock }.", member.NameToken);
                return new BoundErrorExpression();
            }

            var leaves = new List<TableTypeSymbol>();
            var steps = new List<BoundTableRecalculationStep>();
            var visited = new HashSet<TableTypeId>();
            Visit(tableType);
            var inputs = new List<BoundTableRecalculationInput>();
            var replaced = new HashSet<TableTypeId>();
            foreach (ObjectPropertySyntax property in replacements.Properties)
            {
                string name = TableRowInitializers.PropertyName(property);
                TableTypeSymbol? input = leaves.FirstOrDefault(leaf => leaf.Name == name);
                if (input is null || !replaced.Add(input.Id))
                {
                    Report("COPE-RECALC-0003", $"Input '{name}' is unknown, derived, unrelated, or duplicated. Replace an authored dependency of '{tableType.Name}'.", property.NameToken);
                    continue;
                }
                BoundExpression value = BindExpression(property.ValueExpression, input);
                if (!TypeFacts.AreEquivalent(input, value.Type))
                {
                    Report("COPE-RECALC-0004", $"Input '{name}' requires its nominal table snapshot type '{input.Name}', got '{value.Type.Name}'.", property.NameToken);
                }
                inputs.Add(new BoundTableRecalculationInput(input, value));
            }
            // Written replacements evaluate first, once each, in literal order.
            foreach (TableTypeSymbol leaf in leaves)
            {
                if (!replaced.Contains(leaf.Id))
                {
                    inputs.Add(new BoundTableRecalculationInput(leaf, new BoundTableReferenceExpression(leaf)));
                }
            }
            return new BoundTableRecalculationExpression(tableType, inputs, steps);

            void Visit(TableTypeSymbol table)
            {
                if (!visited.Add(table.Id))
                {
                    return;
                }
                BoundTableDefinition? current = _tables.FirstOrDefault(candidate => candidate.TableType.Id == table.Id);
                if (current is not BoundDerivedTableDefinition derived)
                {
                    leaves.Add(table);
                    return;
                }
                TableTypeSymbol[] dependencies = new[] { derived.SourceTable }
                    .Concat(derived.Joins.Select(join => join.JoinedTable)).ToArray();
                foreach (TableTypeSymbol dependency in dependencies)
                {
                    Visit(dependency);
                }
                steps.Add(new BoundTableRecalculationStep(table, dependencies));
            }
        }
    }
}
