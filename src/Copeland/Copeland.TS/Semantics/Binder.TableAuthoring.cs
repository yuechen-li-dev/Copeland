using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private bool ValidateRowInitializers(TableDeclarationSyntax table)
        {
            if (table.RowsInitializer is not ArrayLiteralExpressionSyntax rows)
            {
                foreach (TableColumnSyntax column in table.Columns.Where(column => !column.HasInlineData))
                {
                    Report("COPE-ROWS-0001", "A schema-only table needs a row initializer after its closing brace: '= [{ ... }];'.", column.Identifier);
                }
                return table.Columns.All(column => column.HasInlineData);
            }
            if (table.Columns.Any(column => column.HasInlineData) || table.AssetClause is not null)
            {
                Report("COPE-ROWS-0002", "Use schema-only columns for row authoring; do not combine rows with inline column data or an asset source.", rows.OpenBracketToken);
                return false;
            }
            var names = table.Columns.Select(column => column.Identifier.Text).ToHashSet(StringComparer.Ordinal);
            bool valid = true;
            foreach (ExpressionSyntax element in rows.Elements)
            {
                ExpressionSyntax value = element is RepeatedInitializerSyntax repeated ? repeated.Value : element;
                if (value is not ObjectLiteralExpressionSyntax row)
                {
                    Report("COPE-ROWS-0003", "A table row initializer must be an object literal with every declared field.", rows.OpenBracketToken);
                    valid = false;
                    continue;
                }
                var supplied = new HashSet<string>(StringComparer.Ordinal);
                foreach (ObjectPropertySyntax property in row.Properties)
                {
                    string name = TableRowInitializers.PropertyName(property);
                    if (!names.Contains(name))
                    {
                        Report("COPE-ROWS-0004", $"Row field '{name}' is not a declared column of '{table.Identifier.Text}'.", property.NameToken);
                        valid = false;
                    }
                    if (!supplied.Add(name))
                    {
                        Report("COPE-ROWS-0005", $"Row field '{name}' is supplied more than once.", property.NameToken);
                        valid = false;
                    }
                }
                foreach (string missing in names.Except(supplied, StringComparer.Ordinal))
                {
                    Report("COPE-ROWS-0006", $"Row is missing required column '{missing}'.", row.OpenBraceToken);
                    valid = false;
                }
            }
            return valid;
        }

        private IReadOnlyList<BoundDerivedTableLocal> BindDerivedLocals(
            DerivedTableClauseSyntax clause,
            IReadOnlyDictionary<string, TableTypeSymbol> aliases)
        {
            var locals = new List<BoundDerivedTableLocal>();
            var reservedNames = new HashSet<string>(aliases.Keys, StringComparer.Ordinal);
            CollectNames(clause);
            foreach (VariableDeclarationStatementSyntax syntax in clause.Locals)
            {
                if (syntax.Keyword.Kind != SyntaxKind.ConstKeyword)
                {
                    Report("COPE-DERIVE-0010", "Per-row locals must use const; mutable declarations are unsupported in derive.", syntax.Keyword);
                }
                TypeSymbol? explicitType = syntax.Type is null ? null : BindType(syntax.Type, syntax.Identifier, "COPE-DERIVE-0005", "derived local");
                BoundExpression expression = BindExpression(syntax.Initializer, explicitType);
                TypeSymbol type = explicitType ?? expression.Type;
                if (!TypeFacts.AreEquivalent(type, expression.Type))
                {
                    Report("COPE-DERIVE-0005", "Per-row local does not match its declared type.", syntax.Identifier);
                }
                if (!IsEligibleTableCellType(type, [], out _) || !IsDerivedExpressionPure(expression))
                {
                    Report("COPE-DERIVE-0007", "Per-row locals require pure expressions with supported immutable value types.", syntax.Identifier);
                }
                string runtimeName = "__derive_local_" + locals.Count;
                while (!reservedNames.Add(runtimeName))
                {
                    runtimeName += "_";
                }
                var variable = new VariableSymbol(runtimeName, type, true);
                if (!_scope.TryDeclare(syntax.Identifier.Text, variable))
                {
                    Report("COPE-DERIVE-0012", $"Per-row local '{syntax.Identifier.Text}' conflicts with an earlier local or relation alias.", syntax.Identifier);
                }
                locals.Add(new BoundDerivedTableLocal(syntax.Identifier.Text, variable, expression, syntax.Identifier.Position));
            }
            return locals;

            void CollectNames(SyntaxNode node)
            {
                foreach (object child in node.GetChildren())
                {
                    if (child is SyntaxToken { Kind: SyntaxKind.IdentifierToken } identifier)
                    {
                        reservedNames.Add(identifier.Text);
                    }
                    else if (child is SyntaxNode nested)
                    {
                        CollectNames(nested);
                    }
                }
            }
        }
    }
}
