using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Database;

public static class DatabaseDefinitionBinder
{
    public static DatabaseDefinitionResult Bind(
        string schemaSource,
        string definitionSource,
        string schemaPath = "schema.ts",
        string definitionPath = "index.tsx")
    {
        var diagnostics = new List<Diagnostic>();
        SyntaxTree schemaTree = SyntaxTree.Parse(schemaSource, schemaPath);
        string? schemaAuthority = ReadSchemaAuthority(schemaTree, schemaPath, diagnostics);
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            schemaSource,
            new CopelandCompilationOptions { SourcePath = schemaPath });
        diagnostics.AddRange(compilation.Diagnostics);

        SyntaxTree definitionTree = SyntaxTree.Parse(definitionSource, definitionPath);
        diagnostics.AddRange(definitionTree.Diagnostics.Select(diagnostic =>
            diagnostic with { SourcePath = definitionPath }));

        if (diagnostics.Count > 0 || compilation.MirCompilation?.Program is not MirProgram program)
        {
            return new DatabaseDefinitionResult(null, Ordered(diagnostics));
        }

        if (!TryGetRoot(definitionTree, definitionPath, diagnostics, out TsXmlElementExpressionSyntax? database))
        {
            return new DatabaseDefinitionResult(null, Ordered(diagnostics));
        }

        string? databaseName = RequiredStringAttribute(database!, "name", definitionPath, diagnostics);
        List<TsXmlElementExpressionSyntax> databaseChildren = ElementChildren(database!).ToList();
        if (databaseChildren.Count != 1)
        {
            Report(diagnostics, "COPE-DATABASE-0003", "<Database> requires exactly one root <Index>.", database!.NameToken, definitionPath);
            return new DatabaseDefinitionResult(null, Ordered(diagnostics));
        }

        var partitionFields = new List<string>();
        string? recordName = BindIndexTree(
            databaseChildren[0],
            definitionPath,
            diagnostics,
            partitionFields,
            new HashSet<string>(StringComparer.Ordinal));
        if (databaseName is null || recordName is null)
        {
            return new DatabaseDefinitionResult(null, Ordered(diagnostics));
        }

        MirRecordDefinition? record = program.Records.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, recordName, StringComparison.Ordinal));
        if (record is null)
        {
            Report(diagnostics, "COPE-DATABASE-0004", $"Leaf record '{recordName}' is not a logical Copeland record.", database!.NameToken, definitionPath);
            return new DatabaseDefinitionResult(null, Ordered(diagnostics));
        }

        var fields = new List<DatabaseField>();
        foreach (MirRecordFieldDefinition field in record.Fields)
        {
            if (!TryMapType(field.Type, out DatabaseScalarType type))
            {
                Report(diagnostics, "COPE-DATABASE-0005", $"Field '{field.Name}' has unsupported database type '{field.Type.Identifier}'.", database!.NameToken, definitionPath);
                continue;
            }

            fields.Add(new DatabaseField(field.Name, type));
        }

        foreach (string partitionField in partitionFields)
        {
            DatabaseField? field = fields.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, partitionField, StringComparison.Ordinal));
            if (field is null)
            {
                Report(diagnostics, "COPE-DATABASE-0006", $"Index field '{partitionField}' is absent from record '{recordName}'.", database!.NameToken, definitionPath);
                continue;
            }

            if (field.Type is not (DatabaseScalarType.String or DatabaseScalarType.Int32))
            {
                Report(diagnostics, "COPE-DATABASE-0007", $"Index field '{partitionField}' must be string or int.", database!.NameToken, definitionPath);
            }
        }

        if (fields.Count == partitionFields.Count)
        {
            Report(diagnostics, "COPE-DATABASE-0008", "M0 requires at least one non-partition value column.", database!.NameToken, definitionPath);
        }

        if (diagnostics.Count > 0)
        {
            return new DatabaseDefinitionResult(null, Ordered(diagnostics));
        }

        if (schemaAuthority is null)
        {
            return new DatabaseDefinitionResult(null, Ordered(diagnostics));
        }

        string logicalMetadata = CanonicalLogicalMetadata(schemaAuthority, recordName, fields);
        string indexMetadata = CanonicalIndexMetadata(databaseName, recordName, partitionFields);
        var schema = new DatabaseSchema(
            databaseName,
            schemaAuthority,
            recordName,
            fields,
            partitionFields,
            Hash(logicalMetadata),
            Hash(logicalMetadata + "\n" + indexMetadata));
        return new DatabaseDefinitionResult(schema, []);
    }

    private static bool TryGetRoot(
        SyntaxTree tree,
        string sourcePath,
        List<Diagnostic> diagnostics,
        out TsXmlElementExpressionSyntax? database)
    {
        database = null;
        if (tree.Root.Members.Count != 1
            || tree.Root.Members[0] is not ExportDefaultDeclarationSyntax export
            || export.Expression is not CallExpressionSyntax call
            || call.Target is not NameExpressionSyntax { IdentifierToken.Text: "defineDatabase" }
            || call.Arguments.Count != 1
            || call.Arguments[0] is not TsXmlElementExpressionSyntax element
            || element.NameToken.Text != "Database")
        {
            diagnostics.Add(new Diagnostic(
                "COPE-DATABASE-0001",
                "Database definition must be exactly export default defineDatabase(<Database ...>...</Database>).",
                0,
                Math.Max(1, tree.Text.Length),
                sourcePath));
            return false;
        }

        database = element;
        return true;
    }

    private static string? BindIndexTree(
        TsXmlElementExpressionSyntax element,
        string sourcePath,
        List<Diagnostic> diagnostics,
        List<string> partitionFields,
        HashSet<string> seenFields)
    {
        if (element.NameToken.Text != "Index")
        {
            Report(diagnostics, "COPE-DATABASE-0003", "The database root child must be <Index>.", element.NameToken, sourcePath);
            return null;
        }

        string? field = RequiredStringAttribute(element, "field", sourcePath, diagnostics);
        if (field is not null)
        {
            if (!seenFields.Add(field))
            {
                Report(diagnostics, "COPE-DATABASE-0009", $"Duplicate index field '{field}'.", element.NameToken, sourcePath);
            }

            partitionFields.Add(field);
        }

        List<TsXmlElementExpressionSyntax> children = ElementChildren(element).ToList();
        if (children.Count != 1)
        {
            Report(diagnostics, "COPE-DATABASE-0003", "<Index> requires exactly one nested <Index> or <Table>.", element.NameToken, sourcePath);
            return null;
        }

        TsXmlElementExpressionSyntax child = children[0];
        if (child.NameToken.Text == "Index")
        {
            return BindIndexTree(child, sourcePath, diagnostics, partitionFields, seenFields);
        }

        if (child.NameToken.Text != "Table")
        {
            Report(diagnostics, "COPE-DATABASE-0003", $"Unsupported database element <{child.NameToken.Text}>.", child.NameToken, sourcePath);
            return null;
        }

        if (ElementChildren(child).Any())
        {
            Report(diagnostics, "COPE-DATABASE-0003", "<Table> must be an empty leaf.", child.NameToken, sourcePath);
        }

        TsXmlAttributeSyntax? attribute = SingleAttribute(child, "type", sourcePath, diagnostics);
        if (attribute?.ExpressionValue is not NameExpressionSyntax recordReference)
        {
            Report(diagnostics, "COPE-DATABASE-0010", "<Table type={RecordName} /> requires a direct record reference.", attribute?.NameToken ?? child.NameToken, sourcePath);
            return null;
        }

        return recordReference.IdentifierToken.Text;
    }

    private static string? RequiredStringAttribute(
        TsXmlElementExpressionSyntax element,
        string name,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        TsXmlAttributeSyntax? attribute = SingleAttribute(element, name, sourcePath, diagnostics);
        if (attribute?.StringValueToken?.Value is not string value || string.IsNullOrWhiteSpace(value))
        {
            Report(diagnostics, "COPE-DATABASE-0002", $"<{element.NameToken.Text}> requires string attribute '{name}'.", attribute?.NameToken ?? element.NameToken, sourcePath);
            return null;
        }

        return value;
    }

    private static TsXmlAttributeSyntax? SingleAttribute(
        TsXmlElementExpressionSyntax element,
        string name,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        TsXmlAttributeSyntax[] matching = element.Attributes
            .Where(attribute => attribute.NameToken.Text == name)
            .ToArray();
        if (matching.Length > 1)
        {
            Report(diagnostics, "COPE-DATABASE-0002", $"Duplicate attribute '{name}'.", matching[1].NameToken, sourcePath);
        }

        foreach (TsXmlAttributeSyntax attribute in element.Attributes.Where(attribute => attribute.NameToken.Text != name))
        {
            Report(diagnostics, "COPE-DATABASE-0002", $"Unsupported attribute '{attribute.NameToken.Text}' on <{element.NameToken.Text}>.", attribute.NameToken, sourcePath);
        }

        return matching.FirstOrDefault();
    }

    private static IEnumerable<TsXmlElementExpressionSyntax> ElementChildren(TsXmlElementExpressionSyntax element)
    {
        foreach (TsXmlChildSyntax child in element.Children)
        {
            switch (child)
            {
                case TsXmlElementChildSyntax { Element: TsXmlElementExpressionSyntax nested }:
                    yield return nested;
                    break;
                case TsXmlTextSyntax text when string.IsNullOrWhiteSpace(text.TextToken.Text):
                    break;
                default:
                    yield break;
            }
        }
    }

    private static bool TryMapType(MirType type, out DatabaseScalarType scalar)
    {
        scalar = type.Identifier switch
        {
            "boolean" => DatabaseScalarType.Boolean,
            "int" => DatabaseScalarType.Int32,
            "number" or "float" => DatabaseScalarType.Float64,
            "string" => DatabaseScalarType.String,
            _ => (DatabaseScalarType)(-1),
        };
        return Enum.IsDefined(scalar);
    }

    private static string CanonicalLogicalMetadata(
        string schemaAuthority,
        string recordName,
        IReadOnlyList<DatabaseField> fields)
        => "copeland-database-logical-v1\nauthority:" + schemaAuthority + "\nrecord:" + recordName + "\n"
            + string.Join("\n", fields.Select(field => $"field:{field.Name}:{field.Type}"));

    private static string? ReadSchemaAuthority(
        SyntaxTree tree,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        VariableDeclarationStatementSyntax[] declarations = tree.Root.Members
            .OfType<GlobalStatementMemberSyntax>()
            .Select(member => member.Statement)
            .OfType<VariableDeclarationStatementSyntax>()
            .Where(variable => variable.Identifier.Text == "$schema")
            .ToArray();
        if (declarations.Length != 1
            || declarations[0].Keyword.Kind != SyntaxKind.ConstKeyword
            || declarations[0].Initializer is not LiteralExpressionSyntax literal
            || literal.LiteralToken.Value is not string authority
            || string.IsNullOrWhiteSpace(authority))
        {
            diagnostics.Add(new Diagnostic(
                "COPE-DATABASE-0011",
                "Logical database schema requires one explicit const $schema string identity.",
                0,
                Math.Max(1, tree.Text.Length),
                sourcePath));
            return null;
        }

        return authority;
    }

    private static string CanonicalIndexMetadata(string databaseName, string recordName, IReadOnlyList<string> fields)
        => "copeland-database-index-v1\ndatabase:" + databaseName + "\nrecord:" + recordName + "\n"
            + string.Join("\n", fields.Select((field, depth) => $"index:{depth}:{field}"))
            + "\nleaf:columnar-segment-v1;partition-keys=path";

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void Report(
        List<Diagnostic> diagnostics,
        string id,
        string message,
        SyntaxToken token,
        string sourcePath)
        => diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length), sourcePath));

    private static IReadOnlyList<Diagnostic> Ordered(List<Diagnostic> diagnostics)
        => diagnostics.OrderBy(diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Position)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToArray();
}
