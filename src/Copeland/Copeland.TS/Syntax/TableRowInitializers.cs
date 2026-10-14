namespace Copeland.TS.Syntax;

/// <summary>Projects authored row property expressions into the existing column initializer grammar.</summary>
public static class TableRowInitializers
{
    public static string PropertyName(ObjectPropertySyntax property)
    {
        return property.NameToken.Value as string ?? property.NameToken.Text;
    }

    public static ObjectLiteralExpressionSyntax RowValue(ExpressionSyntax element)
    {
        return (ObjectLiteralExpressionSyntax)(element is RepeatedInitializerSyntax repeated ? repeated.Value : element);
    }

    public static ArrayLiteralExpressionSyntax Column(TableDeclarationSyntax table, TableColumnSyntax column)
    {
        if (table.RowsInitializer is not ArrayLiteralExpressionSyntax rows)
        {
            return column.Cells;
        }
        var elements = new List<ExpressionSyntax>();
        foreach (ExpressionSyntax element in rows.Elements)
        {
            ExpressionSyntax value = RowValue(element).Properties.Single(property => PropertyName(property) == column.Identifier.Text).ValueExpression;
            elements.Add(element is RepeatedInitializerSyntax repeated ? repeated with { Value = value } : value);
        }
        return rows with { Elements = elements };
    }
}
