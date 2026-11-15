using System.Text.Json;
using Copeland.TS.Diagnostics;
using Copeland.TS.Syntax;
using Copeland.TS.Workspace;

namespace Copeland.TS.Tson;

public sealed record TypeProfileCompilation(TsonCatalog? Catalog, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Catalog is not null && Diagnostics.Count == 0;
}

/// <summary>Bounded type-alias binding into the existing TSON catalog. Never emits runtime variables or executes initializers.</summary>
public static class TypeProfileCompiler
{
    public static TypeProfileCompilation Compile(string source, string path, string schemaIdentity, string? selectedProfile = null)
    {
        var diagnostics = new List<Diagnostic>();
        SourceFileProfile profile = SourceFileProfiles.Read(source);
        if ((selectedProfile ?? profile.Name) != "type" || (profile.Name is not null && profile.Name != "type"))
        {
            return new(null, [new("COPE-TYPE-0001", "Select the type profile explicitly or use type CopelandProfile = \"type\";.", 0, 1, path)]);
        }
        if (source.Length > TsonLimits.Default.MaximumSourceLength)
        {
            return new(null, [new("COPE-TYPE-0002", "Type source exceeds the TSON source limit.", 0, 1, path)]);
        }
        SyntaxTree tree = SyntaxTree.ParseTypeSchema(source, path);
        diagnostics.AddRange(tree.Diagnostics);
        var aliases = new SortedDictionary<string, TypeAliasDeclarationSyntax>(StringComparer.Ordinal);
        foreach (MemberSyntax member in tree.Root.Members)
        {
            if (member is not TypeAliasDeclarationSyntax alias)
            {
                diagnostics.Add(new("COPE-TYPE-0002", "The type profile accepts only finite type declarations.", 0, 1, path));
                continue;
            }
            if (!aliases.TryAdd(alias.Identifier.Text, alias))
            {
                Report(alias.Identifier, "Duplicate type declaration.");
            }
            if (alias.TypeParameterTokens.Count != 0 || alias.UnsupportedTokens.Count != 0)
            {
                Report(alias.Identifier, "Generic/unsupported type forms are outside this schema profile.");
            }
        }
        if (aliases.Count == 0 || aliases.Count > TsonLimits.Default.MaximumDeclarationCount)
        {
            diagnostics.Add(new("COPE-TYPE-0002", "Schema requires a bounded, nonempty set of declarations.", 0, 1, path));
        }
        var definitions = new List<TsonNominalDefinition>();
        int bindingNodes = 0;
        foreach (var pair in aliases)
        {
            var stack = new HashSet<string>(StringComparer.Ordinal) { pair.Key };
            TsonTypeReference shape = Bind(pair.Value.TargetType, stack, 0, pair.Key);
            definitions.Add(new TsonTypeAliasDefinition(pair.Key, schemaIdentity + "#" + pair.Key, shape));
        }
        return diagnostics.Count == 0 ? new(new(schemaIdentity, definitions), []) : new(null, diagnostics);

        TsonTypeReference Bind(TypeSyntax syntax, HashSet<string> stack, int depth, string owner)
        {
            if (++bindingNodes > 10000 || depth > TsonLimits.Default.MaximumNestingDepth)
            {
                diagnostics.Add(new("COPE-TYPE-0002", "Recursive/excessively nested schema.", 0, 1, path));
                return TsonTypeReference.Object;
            }
            switch (syntax)
            {
                case PredefinedTypeSyntax primitive:
                    return primitive.Keyword.Text switch
                    {
                        "string" => TsonTypeReference.String,
                        "number" => TsonTypeReference.Number,
                        "boolean" => TsonTypeReference.Boolean,
                        _ => Unsupported(primitive.Keyword)
                    };
                case IdentifierTypeSyntax reference:
                    if (!aliases.TryGetValue(reference.Identifier.Text, out var declaration))
                    {
                        return Unsupported(reference.Identifier);
                    }
                    if (!stack.Add(reference.Identifier.Text))
                    {
                        Report(reference.Identifier, "Recursive aliases are not supported in finite schemas.");
                        return TsonTypeReference.Object;
                    }
                    _ = Bind(declaration.TargetType, stack, depth + 1, reference.Identifier.Text);
                    stack.Remove(reference.Identifier.Text);
                    return TsonTypeReference.Reference(reference.Identifier.Text);
                case StructuralObjectTypeSyntax structure:
                    var fields = new List<TsonFieldDefinition>();
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var field in structure.Fields)
                    {
                        if (!names.Add(field.Identifier.Text)) Report(field.Identifier, "Duplicate field.");
                        string fieldPath = owner + "/field/" + field.Identifier.Text;
                        fields.Add(new(field.Identifier.Text, schemaIdentity + "#" + fieldPath,
                            Bind(field.Type, stack, depth + 1, fieldPath), field.QuestionToken is not null));
                    }
                    if (fields.Count > TsonLimits.Default.MaximumFieldsPerAggregate) Report(structure.OpenBraceToken, "Too many fields.");
                    return TsonTypeReference.Structural(fields.OrderBy(f => f.Name, StringComparer.Ordinal));
                case ArrayTypeSyntax array:
                    return TsonTypeReference.Array(Bind(array.ElementType, stack, depth + 1, owner + "/item"));
                case UnionTypeSyntax union:
                    return TsonTypeReference.Union([
                        Bind(union.Left, stack, depth + 1, owner + "/alternative/0"),
                        Bind(union.Right, stack, depth + 1, owner + "/alternative/1")]);
                case ParenthesizedTypeSyntax parentheses:
                    return Bind(parentheses.Type, stack, depth + 1, owner);
                case LiteralTypeSyntax literal when literal.LiteralToken.Value is string value:
                    return TsonTypeReference.Literal(value);
                default:
                    diagnostics.Add(new("COPE-TYPE-0002", "Unsupported schema type form: " + syntax.Kind, 0, 1, path));
                    return TsonTypeReference.Object;
            }
        }
        TsonTypeReference Unsupported(SyntaxToken token)
        {
            Report(token, "Unsupported or unresolved schema type '" + token.Text + "'.");
            return TsonTypeReference.Object;
        }
        void Report(SyntaxToken token, string message)
        {
            diagnostics.Add(new("COPE-TYPE-0002", message, token.Position, Math.Max(1, token.Text.Length), path));
        }
    }

    public static string CanonicalArtifact(TsonCatalog catalog)
    {
        return JsonSerializer.Serialize(new
        {
            format = "copeland.type.v1",
            identity = catalog.SchemaIdentity,
            types = catalog.Definitions.OfType<TsonTypeAliasDefinition>().OrderBy(d => d.Name, StringComparer.Ordinal)
                .Select(d => new { d.Name, d.Shape })
        }, new JsonSerializerOptions { MaxDepth = 256 });
    }

    /// <summary>Non-executable obj.ts data projection using the Copeland AST and the bound TSON catalog.</summary>
    public static JsonElement ReadTypedValue(string source, string path, TsonCatalog catalog, string rootType)
    {
        if (source.Length > TsonLimits.Default.MaximumSourceLength) throw new ArgumentException("Object source limit exceeded.");
        var tree = SyntaxTree.Parse(source, path);
        if (tree.Diagnostics.Count > 0) throw new ArgumentException(string.Join("\n", tree.Diagnostics));
        var members = tree.Root.Members;
        if (members.Count != 1 || members[0] is not GlobalStatementMemberSyntax
            { Statement: VariableDeclarationStatementSyntax declaration } || declaration.Keyword.Kind != SyntaxKind.ConstKeyword
            || declaration.Identifier.Text != "$value" || declaration.Type is not IdentifierTypeSyntax type || type.Identifier.Text != rootType)
        {
            throw new ArgumentException("Typed object authoring requires exactly const $value: " + rootType + " = <literal data>;");
        }
        int nodes = 0;
        JsonElement value = JsonSerializer.SerializeToElement(Project(declaration.Initializer, 0));
        var errors = Validate(catalog, rootType, value);
        if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
        return value;

        object? Project(ExpressionSyntax expression, int depth)
        {
            if (++nodes > TsonLimits.Default.MaximumValueNodeCount || depth > TsonLimits.Default.MaximumNestingDepth)
                throw new ArgumentException("Typed object exceeds TSON value limits.");
            switch (expression)
            {
                case LiteralExpressionSyntax literal when literal.LiteralToken.Kind == SyntaxKind.TrueKeyword: return true;
                case LiteralExpressionSyntax literal when literal.LiteralToken.Kind == SyntaxKind.FalseKeyword: return false;
                case LiteralExpressionSyntax literal when literal.LiteralToken.Value is string text:
                    if (text.Length > TsonLimits.Default.MaximumStringLength) throw new ArgumentException("String limit exceeded.");
                    return text;
                case LiteralExpressionSyntax literal when literal.LiteralToken.Value is int or double: return literal.LiteralToken.Value;
                case ArrayLiteralExpressionSyntax array:
                    if (array.Elements.Count > TsonLimits.Default.MaximumArrayLength) throw new ArgumentException("Array limit exceeded.");
                    return array.Elements.Select(e => Project(e, depth + 1)).ToArray();
                case ObjectLiteralExpressionSyntax obj:
                    var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                    if (obj.Properties.Count > TsonLimits.Default.MaximumFieldsPerAggregate) throw new ArgumentException("Field limit exceeded.");
                    foreach (var field in obj.Properties)
                    {
                        string name = field.NameToken.Value as string ?? field.NameToken.Text;
                        if (!result.TryAdd(name, Project(field.ValueExpression, depth + 1))) throw new ArgumentException("Duplicate field " + name);
                    }
                    return result;
                default: throw new ArgumentException("Executable expressions are not permitted in typed object data: " + expression.Kind);
            }
        }
    }

    public static IReadOnlyList<string> Validate(TsonCatalog catalog, string rootType, JsonElement value)
    {
        var errors = new List<string>();
        Check(TsonTypeReference.Reference(rootType), value, "$", errors, 0);
        return errors;

        void Check(TsonTypeReference type, JsonElement item, string path, List<string> result, int depth)
        {
            if (depth > TsonLimits.Default.MaximumNestingDepth || result.Count >= 32)
            {
                result.Add(path + ": schema depth/diagnostic limit exceeded.");
                return;
            }
            switch (type.Kind)
            {
                case TsonTypeKind.Reference:
                    if (catalog.TryGetDefinition(type.NominalName!, out var definition) && definition is TsonTypeAliasDefinition alias)
                    {
                        Check(alias.Shape, item, path, result, depth + 1);
                    }
                    else result.Add(path + ": unresolved type.");
                    return;
                case TsonTypeKind.Structural:
                    if (item.ValueKind != JsonValueKind.Object) break;
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in item.EnumerateObject())
                    {
                        if (!seen.Add(property.Name)) result.Add(path + ": duplicate field " + property.Name);
                        if (!type.Fields.Any(f => f.Name == property.Name)) result.Add(path + ": unexpected field " + property.Name);
                    }
                    foreach (var field in type.Fields)
                    {
                        if (item.TryGetProperty(field.Name, out var child)) Check(field.Type, child, path + "." + field.Name, result, depth + 1);
                        else if (!field.Optional) result.Add(path + ": missing field " + field.Name);
                    }
                    return;
                case TsonTypeKind.Array:
                    if (item.ValueKind != JsonValueKind.Array) break;
                    if (item.GetArrayLength() > TsonLimits.Default.MaximumArrayLength) { result.Add(path + ": array limit exceeded."); return; }
                    int index = 0;
                    foreach (var child in item.EnumerateArray()) Check(type.ElementType!, child, path + "[" + index++ + "]", result, depth + 1);
                    return;
                case TsonTypeKind.Union:
                    foreach (var alternative in type.Alternatives)
                    {
                        var branch = new List<string>();
                        Check(alternative, item, path, branch, depth + 1);
                        if (branch.Count == 0) return;
                    }
                    result.Add(path + ": no union alternative matches.");
                    return;
                case TsonTypeKind.String when item.ValueKind == JsonValueKind.String:
                case TsonTypeKind.Number when item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double number) && double.IsFinite(number):
                case TsonTypeKind.Boolean when item.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    return;
                case TsonTypeKind.Literal when item.ValueKind == JsonValueKind.String && item.GetString() == type.LiteralValue:
                    return;
            }
            result.Add(path + ": expected " + type.Kind + ".");
        }
    }
}
