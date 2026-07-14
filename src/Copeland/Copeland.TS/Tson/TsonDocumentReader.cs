using System.Globalization;
using System.Text;
using Copeland.TS.Syntax;

namespace Copeland.TS.Tson;

public static class TsonDocumentReader
{
    public static TsonReadResult ReadSelfDescribed(
        string source,
        TsonDocumentProfile profile,
        string? authoringSchemaIdentity = null,
        TsonLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= TsonLimits.Default;

        if (source.Length > limits.MaximumSourceLength)
        {
            return Failure(
                "COPE-TSON-0005",
                $"Source length exceeds the TSON limit of {limits.MaximumSourceLength} characters.",
                0,
                Math.Max(1, source.Length));
        }

        var depthFailure = CheckLexicalNesting(source, limits.MaximumNestingDepth);
        if (depthFailure is not null)
        {
            return new TsonReadResult(null, [], [depthFailure]);
        }

        var syntaxTree = SyntaxTree.Parse(source);
        if (syntaxTree.Diagnostics.Count > 0)
        {
            return new TsonReadResult(null, syntaxTree.Diagnostics, []);
        }

        var projector = new Projector(source, profile, authoringSchemaIdentity, limits);
        var document = projector.Project(syntaxTree.Root);
        if (document is null || projector.Diagnostics.Count > 0)
        {
            return new TsonReadResult(null, syntaxTree.Diagnostics, projector.Diagnostics);
        }

        if (profile == TsonDocumentProfile.CanonicalTson)
        {
            string canonical;
            try
            {
                canonical = TsonCanonicalPrinter.Print(document, limits);
            }
            catch (TsonCanonicalLimitException)
            {
                var code = document.Root is TsonTable ? "COPE-TSON-TABLE-0005" : "COPE-TSON-0005";
                return Failure(
                    code,
                    $"Canonical output exceeds the TSON limit of {limits.MaximumCanonicalUtf8ByteCount} UTF-8 bytes.",
                    0,
                    Math.Max(1, source.Length));
            }

            if (!string.Equals(source, canonical, StringComparison.Ordinal))
            {
                var diagnostic = new TsonDiagnostic(
                    document.Root is TsonTable ? "COPE-TSON-TABLE-0005" : "COPE-TSON-0005",
                    "Canonical TSON input does not use the canonical byte spelling.",
                    0,
                    Math.Max(1, source.Length));
                return new TsonReadResult(null, syntaxTree.Diagnostics, [diagnostic]);
            }
        }

        return new TsonReadResult(document, syntaxTree.Diagnostics, projector.Diagnostics);
    }

    public static TsonReadResult DecodeAuthoringValue(
        string source,
        string schemaIdentity,
        TsonLimits? limits = null)
    {
        if (string.IsNullOrWhiteSpace(schemaIdentity))
        {
            throw new ArgumentException("An authoring schema identity is required.", nameof(schemaIdentity));
        }

        return ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript,
            schemaIdentity,
            limits);
    }

    private static TsonReadResult Failure(
        string code,
        string message,
        int position,
        int length)
    {
        return new TsonReadResult(null, [], [new TsonDiagnostic(code, message, position, length)]);
    }

    private static TsonDiagnostic? CheckLexicalNesting(string source, int maximumDepth)
    {
        var lexer = new Lexer(source);
        var depth = 0;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind is SyntaxKind.OpenBraceToken
                or SyntaxKind.OpenBracketToken
                or SyntaxKind.OpenParenToken)
            {
                depth++;
                if (depth > maximumDepth)
                {
                    return new TsonDiagnostic(
                        "COPE-TSON-0005",
                        $"Syntax nesting exceeds the TSON limit of {maximumDepth}.",
                        token.Position,
                        Math.Max(1, token.Text.Length));
                }
            }
            else if (token.Kind is SyntaxKind.CloseBraceToken
                     or SyntaxKind.CloseBracketToken
                     or SyntaxKind.CloseParenToken)
            {
                depth = Math.Max(0, depth - 1);
            }

            if (token.Kind == SyntaxKind.EndOfFileToken)
            {
                return null;
            }
        }
    }

    private sealed class Projector
    {
        private readonly string _source;
        private readonly TsonDocumentProfile _profile;
        private readonly string? _authoringSchemaIdentity;
        private readonly TsonLimits _limits;
        private readonly List<TsonDiagnostic> _diagnostics = [];
        private readonly Dictionary<string, TsonNominalDefinition> _definitions = new(StringComparer.Ordinal);
        private int _valueNodeCount;
        private bool _insideTableCell;
        private bool _hasTableDeclarations;

        public Projector(
            string source,
            TsonDocumentProfile profile,
            string? authoringSchemaIdentity,
            TsonLimits limits)
        {
            _source = source;
            _profile = profile;
            _authoringSchemaIdentity = authoringSchemaIdentity;
            _limits = limits;
        }

        public IReadOnlyList<TsonDiagnostic> Diagnostics => _diagnostics;

        public TsonDocument? Project(CompilationUnitSyntax root)
        {
            var recordDeclarations = root.Members.OfType<RecordDeclarationSyntax>().ToArray();
            var enumDeclarations = root.Members.OfType<EnumDeclarationSyntax>().ToArray();
            var tableDeclarations = root.Members.OfType<TableDeclarationSyntax>().ToArray();
            _hasTableDeclarations = tableDeclarations.Length > 0;
            var declarations = recordDeclarations.Length + enumDeclarations.Length + tableDeclarations.Length;
            if (declarations > _limits.MaximumDeclarationCount)
            {
                Report(
                    "COPE-TSON-0005",
                    $"Declaration count exceeds the TSON limit of {_limits.MaximumDeclarationCount}.",
                    root);
                return null;
            }

            var schemaBindings = new List<VariableDeclarationStatementSyntax>();
            var rootBindings = new List<VariableDeclarationStatementSyntax>();
            ClassifyMembers(root, schemaBindings, rootBindings);

            var embeddedIdentity = ReadEmbeddedSchemaIdentity(schemaBindings);
            var schemaIdentity = ResolveSchemaIdentity(embeddedIdentity, schemaBindings);
            if (schemaIdentity is null)
            {
                return null;
            }

            BuildCatalog(schemaIdentity, recordDeclarations, enumDeclarations, tableDeclarations);
            ValidateTypeReferencesAndCycles();

            if (_diagnostics.Count > 0)
            {
                return null;
            }

            if (rootBindings.Count != 1)
            {
                Report(
                    "COPE-TSON-0001",
                    $"A TSON document requires exactly one 'const $value' binding; found {rootBindings.Count}.",
                    root);
                return null;
            }

            var rootBinding = rootBindings[0];
            if (tableDeclarations.Length > 0)
            {
                return ProjectTableDocument(schemaIdentity, rootBinding, tableDeclarations);
            }

            var expectedType = rootBinding.Type is null
                ? null
                : ReadType(rootBinding.Type, reportErrors: true);
            var value = ProjectValue(rootBinding.Initializer, expectedType, depth: 1);
            if (value is null || _diagnostics.Count > 0)
            {
                return null;
            }

            if (value is TsonArray)
            {
                Report(
                    "COPE-TSON-0004",
                    "A TSON array cannot be the document root; arrays must be nested beneath a nominal record or enum value.",
                    rootBinding.Initializer);
                return null;
            }

            var orderedDefinitions = _definitions.Values
                .OrderBy(definition => definition.Name, StringComparer.Ordinal)
                .ToArray();
            return new TsonDocument(new TsonCatalog(schemaIdentity, orderedDefinitions), value);
        }

        private void ClassifyMembers(
            CompilationUnitSyntax root,
            List<VariableDeclarationStatementSyntax> schemaBindings,
            List<VariableDeclarationStatementSyntax> rootBindings)
        {
            foreach (var member in root.Members)
            {
                if (member is RecordDeclarationSyntax or EnumDeclarationSyntax or TableDeclarationSyntax)
                {
                    continue;
                }

                if (member is GlobalStatementMemberSyntax
                    {
                        Statement: VariableDeclarationStatementSyntax binding,
                    })
                {
                    if (binding.Keyword.Kind != SyntaxKind.ConstKeyword)
                    {
                        Report(
                            "COPE-TSON-0002",
                            "TSON bindings must be non-executable 'const' declarations.",
                            binding);
                        continue;
                    }

                    if (binding.Identifier.Text == "$schema")
                    {
                        schemaBindings.Add(binding);
                    }
                    else if (binding.Identifier.Text == "$value")
                    {
                        rootBindings.Add(binding);
                    }
                    else
                    {
                        Report(
                            "COPE-TSON-0001",
                            $"Top-level binding '{binding.Identifier.Text}' is not part of the TSON envelope.",
                            binding);
                    }

                    continue;
                }

                Report(
                    "COPE-TSON-0002",
                    $"Syntax '{member.Kind}' is executable or unsupported in a TSON document.",
                    member);
            }
        }

        private TsonDocument? ProjectTableDocument(
            string schemaIdentity,
            VariableDeclarationStatementSyntax rootBinding,
            IReadOnlyList<TableDeclarationSyntax> declarations)
        {
            if (declarations.Count != 1)
            {
                Report(
                    "COPE-TSON-TABLE-0001",
                    $"A table-root TSON document requires exactly one table declaration; found {declarations.Count}.",
                    declarations.Count > 1 ? declarations[1] : rootBinding);
                return null;
            }

            var declaration = declarations[0];
            if (declaration.AssetClause is not null)
            {
                Report(
                    "COPE-TSON-TABLE-0001",
                    "A TSON document table declaration must contain authored column data, not an asset clause.",
                    declaration.AssetClause);
                return null;
            }
            if (rootBinding.Type is not null
                || rootBinding.Initializer is not NameExpressionSyntax rootName
                || rootName.IdentifierToken.Text != declaration.Identifier.Text)
            {
                Report(
                    "COPE-TSON-TABLE-0001",
                    $"A table-root TSON document requires the exact root form 'const $value = {declaration.Identifier.Text};'.",
                    rootBinding);
                return null;
            }

            if (!_definitions.TryGetValue(declaration.Identifier.Text, out var definition)
                || definition is not TsonTableSchema tableSchema)
            {
                Report(
                    "COPE-TSON-TABLE-0002",
                    $"Table schema '{schemaIdentity}#{declaration.Identifier.Text}' is unavailable.",
                    declaration);
                return null;
            }

            var rowCount = declaration.Columns[0].Cells.Elements.Count;
            for (var index = 0; index < declaration.Columns.Count; index++)
            {
                if (declaration.Columns[index].Identifier.Text != tableSchema.Columns[index].Name)
                {
                    Report(
                        "COPE-TSON-TABLE-0003",
                        "Table columns do not match schema declaration order.",
                        declaration.Columns[index]);
                    return null;
                }

                if (declaration.Columns[index].Cells.Elements.Count != rowCount)
                {
                    Report(
                        "COPE-TSON-TABLE-0003",
                        $"Table column '{declaration.Columns[index].Identifier.Text}' is ragged; expected {rowCount} cells.",
                        declaration.Columns[index]);
                    return null;
                }
            }

            if (rowCount > _limits.MaximumTableRowCount)
            {
                Report(
                    "COPE-TSON-TABLE-0005",
                    $"Table row count exceeds the limit of {_limits.MaximumTableRowCount}.",
                    declaration);
                return null;
            }

            var totalCells = (long)declaration.Columns.Count * rowCount;
            if (totalCells > _limits.MaximumTableCellCount)
            {
                Report(
                    "COPE-TSON-TABLE-0005",
                    $"Table cell count {totalCells} exceeds the limit of {_limits.MaximumTableCellCount}.",
                    declaration);
                return null;
            }

            if (!TryAddValueNodes(1L + declaration.Columns.Count, declaration))
            {
                return null;
            }

            var columns = new List<TsonTableColumn>(declaration.Columns.Count);
            for (var columnIndex = 0; columnIndex < declaration.Columns.Count; columnIndex++)
            {
                var columnSyntax = declaration.Columns[columnIndex];
                var columnSchema = tableSchema.Columns[columnIndex];
                var cells = new List<TsonValue>(rowCount);
                foreach (var cellSyntax in columnSyntax.Cells.Elements)
                {
                    _insideTableCell = true;
                    TsonValue? cell;
                    try
                    {
                        cell = ProjectValue(cellSyntax, columnSchema.ElementType, depth: 2);
                    }
                    finally
                    {
                        _insideTableCell = false;
                    }

                    if (cell is not null)
                    {
                        cells.Add(cell);
                    }
                }

                if (_diagnostics.Count == 0)
                {
                    columns.Add(new TsonTableColumn(columnSchema, cells));
                }
            }

            if (_diagnostics.Count > 0)
            {
                return null;
            }

            var table = new TsonTable(tableSchema, columns, _limits);
            var orderedDefinitions = _definitions.Values
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToArray();
            return new TsonDocument(new TsonCatalog(schemaIdentity, orderedDefinitions), table);
        }

        private bool TryAddValueNodes(long count, SyntaxNode syntax)
        {
            if (count > int.MaxValue || _valueNodeCount > _limits.MaximumValueNodeCount - count)
            {
                Report(
                    "COPE-TSON-TABLE-0005",
                    $"Value-node count exceeds the TSON limit of {_limits.MaximumValueNodeCount}.",
                    syntax);
                return false;
            }

            _valueNodeCount += (int)count;
            return true;
        }

        private string? ReadEmbeddedSchemaIdentity(
            IReadOnlyList<VariableDeclarationStatementSyntax> bindings)
        {
            if (bindings.Count > 1)
            {
                Report(
                    "COPE-TSON-0003",
                    "A TSON document cannot contain duplicate '$schema' bindings.",
                    bindings[1]);
                return null;
            }

            if (bindings.Count == 0)
            {
                return null;
            }

            var binding = bindings[0];
            if (binding.Type is not PredefinedTypeSyntax { Keyword.Kind: SyntaxKind.StringKeyword }
                || binding.Initializer is not LiteralExpressionSyntax
                {
                    LiteralToken.Kind: SyntaxKind.StringToken,
                } literal)
            {
                Report(
                    "COPE-TSON-0003",
                    "The '$schema' binding must have type 'string' and a string literal initializer.",
                    binding);
                return null;
            }

            var identity = DecodeString(literal.LiteralToken);
            if (identity is null || !IsValidSchemaIdentity(identity))
            {
                Report(
                    "COPE-TSON-0003",
                    "The TSON schema identity must be an absolute 'copeland://' identity without '#'.",
                    literal);
                return null;
            }

            return identity;
        }

        private string? ResolveSchemaIdentity(
            string? embeddedIdentity,
            IReadOnlyList<VariableDeclarationStatementSyntax> schemaBindings)
        {
            if (_profile == TsonDocumentProfile.CanonicalTson && embeddedIdentity is null)
            {
                Report(
                    "COPE-TSON-0001",
                    "Canonical TSON requires a self-contained '$schema' binding.",
                    schemaBindings.Count == 0 ? null : schemaBindings[0]);
                return null;
            }

            if (_authoringSchemaIdentity is not null
                && !IsValidSchemaIdentity(_authoringSchemaIdentity))
            {
                Report(
                    "COPE-TSON-0003",
                    "The supplied authoring schema identity must be an absolute 'copeland://' identity without '#'.",
                    null);
                return null;
            }

            if (embeddedIdentity is not null
                && _authoringSchemaIdentity is not null
                && !string.Equals(embeddedIdentity, _authoringSchemaIdentity, StringComparison.Ordinal))
            {
                Report(
                    "COPE-TSON-0003",
                    "The embedded and supplied TSON schema identities conflict.",
                    schemaBindings[0]);
                return null;
            }

            var identity = embeddedIdentity ?? _authoringSchemaIdentity;
            if (identity is null)
            {
                Report(
                    "COPE-TSON-0003",
                    "Object TypeScript requires either '$schema' or an explicit authoring schema identity.",
                    null);
            }

            return identity;
        }

        private void BuildCatalog(
            string schemaIdentity,
            IReadOnlyList<RecordDeclarationSyntax> records,
            IReadOnlyList<EnumDeclarationSyntax> enums,
            IReadOnlyList<TableDeclarationSyntax> tables)
        {
            var declaredKinds = new Dictionary<string, TsonTypeKind>(StringComparer.Ordinal);
            foreach (var declaration in records)
            {
                AddDeclaredKind(declaration.Identifier, TsonTypeKind.Record, declaredKinds);
            }

            foreach (var declaration in enums)
            {
                AddDeclaredKind(declaration.Identifier, TsonTypeKind.Enum, declaredKinds);
            }

            foreach (var declaration in tables)
            {
                AddDeclaredKind(declaration.Identifier, TsonTypeKind.Table, declaredKinds);
            }

            foreach (var declaration in records)
            {
                if (declaration.ConstKeyword is not null)
                {
                    Report(
                        "COPE-TSON-0003",
                        "TSON record declarations cannot use 'const record'.",
                        declaration);
                    continue;
                }

                if (declaration.Fields.Count > _limits.MaximumFieldsPerAggregate)
                {
                    Report(
                        "COPE-TSON-0005",
                        $"Record '{declaration.Identifier.Text}' exceeds the field limit of {_limits.MaximumFieldsPerAggregate}.",
                        declaration);
                    continue;
                }

                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                var fields = new List<TsonFieldDefinition>();
                foreach (var field in declaration.Fields)
                {
                    if (!field.HasExplicitType || !field.HasTerminator || field.UnsupportedTokens.Count > 0)
                    {
                        Report(
                            "COPE-TSON-0003",
                            $"Record field '{field.Identifier.Text}' must use the restricted 'name: type;' form.",
                            field);
                        continue;
                    }

                    if (!fieldNames.Add(field.Identifier.Text))
                    {
                        Report(
                            "COPE-TSON-0003",
                            $"Duplicate field '{field.Identifier.Text}' in record '{declaration.Identifier.Text}'.",
                            field);
                        continue;
                    }

                    var type = ReadType(field.Type, declaredKinds, reportErrors: true);
                    if (type is not null)
                    {
                        fields.Add(new TsonFieldDefinition(
                            field.Identifier.Text,
                            FieldIdentity(schemaIdentity, declaration.Identifier.Text, field.Identifier.Text),
                            type));
                    }
                }

                AddDefinition(new TsonRecordDefinition(
                    declaration.Identifier.Text,
                    TypeIdentity(schemaIdentity, declaration.Identifier.Text),
                    fields), declaration);
            }

            foreach (var declaration in enums)
            {
                if (declaration.Cases.Count > _limits.MaximumEnumCases)
                {
                    Report(
                        "COPE-TSON-0005",
                        $"Enum '{declaration.Identifier.Text}' exceeds the case limit of {_limits.MaximumEnumCases}.",
                        declaration);
                    continue;
                }

                var caseNames = new HashSet<string>(StringComparer.Ordinal);
                var cases = new List<TsonEnumCaseDefinition>();
                foreach (var item in declaration.Cases)
                {
                    if (!caseNames.Add(item.Identifier.Text))
                    {
                        Report(
                            "COPE-TSON-0003",
                            $"Duplicate case '{item.Identifier.Text}' in enum '{declaration.Identifier.Text}'.",
                            item);
                        continue;
                    }

                    if (item.PayloadFields.Count > _limits.MaximumPayloadsPerCase)
                    {
                        Report(
                            "COPE-TSON-0005",
                            $"Enum case '{declaration.Identifier.Text}.{item.Identifier.Text}' exceeds the payload limit of {_limits.MaximumPayloadsPerCase}.",
                            item);
                        continue;
                    }

                    var payloadNames = new HashSet<string>(StringComparer.Ordinal);
                    var payloads = new List<TsonFieldDefinition>();
                    foreach (var payload in item.PayloadFields)
                    {
                        if (!payloadNames.Add(payload.Identifier.Text))
                        {
                            Report(
                                "COPE-TSON-0003",
                                $"Duplicate payload '{payload.Identifier.Text}' in case '{declaration.Identifier.Text}.{item.Identifier.Text}'.",
                                payload);
                            continue;
                        }

                        var type = ReadType(payload.Type, declaredKinds, reportErrors: true);
                        if (type is not null)
                        {
                            payloads.Add(new TsonFieldDefinition(
                                payload.Identifier.Text,
                                PayloadIdentity(
                                    schemaIdentity,
                                    declaration.Identifier.Text,
                                    item.Identifier.Text,
                                    payload.Identifier.Text),
                                type));
                        }
                    }

                    cases.Add(new TsonEnumCaseDefinition(
                        item.Identifier.Text,
                        CaseIdentity(schemaIdentity, declaration.Identifier.Text, item.Identifier.Text),
                        payloads));
                }

                AddDefinition(new TsonEnumDefinition(
                    declaration.Identifier.Text,
                    TypeIdentity(schemaIdentity, declaration.Identifier.Text),
                    cases), declaration);
            }

            foreach (var declaration in tables)
            {
                BuildTableSchema(schemaIdentity, declaration, declaredKinds);
            }
        }

        private void BuildTableSchema(
            string schemaIdentity,
            TableDeclarationSyntax declaration,
            IReadOnlyDictionary<string, TsonTypeKind> declaredKinds)
        {
            var diagnosticCountBefore = _diagnostics.Count;
            if (declaration.Columns.Count == 0)
            {
                Report(
                    "COPE-TSON-TABLE-0003",
                    "A TSON table requires at least one typed column because a zero-column row count is not serializable.",
                    declaration);
                return;
            }

            if (declaration.Columns.Count > _limits.MaximumTableColumnCount)
            {
                Report(
                    "COPE-TSON-TABLE-0005",
                    $"Table '{declaration.Identifier.Text}' exceeds the column limit of {_limits.MaximumTableColumnCount}.",
                    declaration);
                return;
            }

            var tableIdentity = TsonTableIdentity.Create(schemaIdentity, declaration.Identifier.Text);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var columns = new List<TsonTableColumnSchema>();
            foreach (var column in declaration.Columns)
            {
                if (!names.Add(column.Identifier.Text))
                {
                    Report(
                        "COPE-TSON-TABLE-0003",
                        $"Duplicate column '{column.Identifier.Text}' in table '{declaration.Identifier.Text}'.",
                        column);
                    continue;
                }

                var type = column.ExplicitType is null
                    ? InferColumnType(column)
                    : ReadType(column.ExplicitType, declaredKinds, reportErrors: false);
                if (type is null || !IsSupportedArrayElementType(type))
                {
                    Report(
                        "COPE-TSON-TABLE-0002",
                        $"Column '{column.Identifier.Text}' requires an explicit or inferable supported cell type.",
                        column);
                    continue;
                }

                columns.Add(new TsonTableColumnSchema(
                    column.Identifier.Text,
                    TsonTableColumnIdentity.Create(tableIdentity, column.Identifier.Text),
                    type));
            }

            if (_diagnostics.Count == diagnosticCountBefore)
            {
                AddDefinition(
                    new TsonTableSchema(declaration.Identifier.Text, tableIdentity, columns),
                    declaration);
            }
        }

        private TsonTypeReference? InferColumnType(TableColumnSyntax column)
        {
            if (column.Cells.Elements.Count == 0)
            {
                return null;
            }

            return InferValueType(column.Cells.Elements[0]);
        }

        private TsonTypeReference? InferValueType(ExpressionSyntax syntax)
        {
            if (syntax is LiteralExpressionSyntax literal)
            {
                return literal.LiteralToken.Kind switch
                {
                    SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword => TsonTypeReference.Boolean,
                    SyntaxKind.NumberToken => TsonTypeReference.Number,
                    SyntaxKind.StringToken => TsonTypeReference.String,
                    _ => null,
                };
            }

            if (syntax is UnaryExpressionSyntax
                {
                    OperatorToken.Kind: SyntaxKind.MinusToken,
                    Operand: LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.NumberToken },
                })
            {
                return TsonTypeReference.Number;
            }

            if (syntax is CallExpressionSyntax call)
            {
                if (call.Target is NameExpressionSyntax { IdentifierToken.Text: "$number" })
                {
                    return TsonTypeReference.Number;
                }

                if (TryReadRecordConstructor(call.Target, out var recordName))
                {
                    return TsonTypeReference.Record(recordName!);
                }

                if (TryReadEnumConstructor(call.Target, out var enumName, out _))
                {
                    return TsonTypeReference.Enum(enumName!);
                }
            }

            if (syntax is MemberAccessExpressionSyntax member
                && TryReadEnumConstructor(member, out var zeroPayloadEnumName, out _))
            {
                return TsonTypeReference.Enum(zeroPayloadEnumName!);
            }

            if (syntax is ArrayLiteralExpressionSyntax array && array.Elements.Count > 0)
            {
                var elementType = InferValueType(array.Elements[0]);
                return elementType is null ? null : TsonTypeReference.Array(elementType);
            }

            return null;
        }

        private void AddDeclaredKind(
            SyntaxToken name,
            TsonTypeKind kind,
            Dictionary<string, TsonTypeKind> declaredKinds)
        {
            if (!declaredKinds.TryAdd(name.Text, kind))
            {
                Report(
                    "COPE-TSON-0003",
                    $"Duplicate nominal declaration '{name.Text}'.",
                    name);
            }
        }

        private void AddDefinition(TsonNominalDefinition definition, SyntaxNode syntax)
        {
            if (!_definitions.TryAdd(definition.Name, definition))
            {
                Report(
                    "COPE-TSON-0003",
                    $"Duplicate nominal declaration '{definition.Name}'.",
                    syntax);
            }
        }

        private void ValidateTypeReferencesAndCycles()
        {
            foreach (var definition in _definitions.Values)
            {
                foreach (var type in GetReferencedTypes(definition))
                {
                    if (type.NominalName is null)
                    {
                        continue;
                    }

                    if (!_definitions.TryGetValue(type.NominalName, out var target)
                        || type.Kind == TsonTypeKind.Record && target is not TsonRecordDefinition
                        || type.Kind == TsonTypeKind.Enum && target is not TsonEnumDefinition)
                    {
                        Report(
                            "COPE-TSON-0003",
                            $"Unknown or mismatched nominal TSON type '{type.NominalName}'.",
                            null);
                    }
                }
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in _definitions.Values)
            {
                if (HasCycle(definition, visiting, visited))
                {
                    Report(
                        "COPE-TSON-0003",
                        $"Recursive TSON schema involving '{definition.Name}' is excluded from M0b.",
                        null);
                    return;
                }
            }
        }

        private bool HasCycle(
            TsonNominalDefinition definition,
            HashSet<string> visiting,
            HashSet<string> visited)
        {
            if (visited.Contains(definition.Name))
            {
                return false;
            }

            if (!visiting.Add(definition.Name))
            {
                return true;
            }

            foreach (var type in GetReferencedTypes(definition))
            {
                if (type.NominalName is not null
                    && _definitions.TryGetValue(type.NominalName, out var target)
                    && HasCycle(target, visiting, visited))
                {
                    return true;
                }
            }

            visiting.Remove(definition.Name);
            visited.Add(definition.Name);
            return false;
        }

        private static IEnumerable<TsonTypeReference> GetReferencedTypes(
            TsonNominalDefinition definition)
        {
            IEnumerable<TsonTypeReference> roots = definition switch
            {
                TsonRecordDefinition record => record.Fields.Select(field => field.Type),
                TsonEnumDefinition @enum => @enum.Cases.SelectMany(item => item.Payloads).Select(payload => payload.Type),
                TsonTableSchema table => table.Columns.Select(column => column.ElementType),
                _ => [],
            };

            var pending = new Stack<TsonTypeReference>(roots.Reverse());
            while (pending.Count > 0)
            {
                var type = pending.Pop();
                if (type.Kind == TsonTypeKind.Array)
                {
                    pending.Push(type.ElementType!);
                    continue;
                }

                yield return type;
            }
        }

        private TsonTypeReference? ReadType(TypeSyntax syntax, bool reportErrors)
        {
            var declaredKinds = _definitions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value switch
                {
                    TsonRecordDefinition => TsonTypeKind.Record,
                    TsonEnumDefinition => TsonTypeKind.Enum,
                    TsonTableSchema => TsonTypeKind.Table,
                    _ => throw new InvalidOperationException("Unknown TSON nominal definition."),
                },
                StringComparer.Ordinal);
            return ReadType(syntax, declaredKinds, reportErrors);
        }

        private TsonTypeReference? ReadType(
            TypeSyntax syntax,
            IReadOnlyDictionary<string, TsonTypeKind> declaredKinds,
            bool reportErrors)
        {
            TsonTypeReference? type = syntax switch
            {
                PredefinedTypeSyntax { Keyword.Kind: SyntaxKind.BooleanKeyword } => TsonTypeReference.Boolean,
                PredefinedTypeSyntax { Keyword.Kind: SyntaxKind.NumberKeyword } => TsonTypeReference.Number,
                PredefinedTypeSyntax { Keyword.Kind: SyntaxKind.StringKeyword } => TsonTypeReference.String,
                IdentifierTypeSyntax { Identifier.Text: "$object" } => TsonTypeReference.Object,
                IdentifierTypeSyntax identifier when declaredKinds.TryGetValue(identifier.Identifier.Text, out var kind)
                    => kind switch
                    {
                        TsonTypeKind.Record => TsonTypeReference.Record(identifier.Identifier.Text),
                        TsonTypeKind.Enum => TsonTypeReference.Enum(identifier.Identifier.Text),
                        _ => null,
                    },
                ArrayTypeSyntax array => ReadArrayType(array, declaredKinds, reportErrors),
                _ => null,
            };

            if (type is null && reportErrors)
            {
                Report(
                    "COPE-TSON-0003",
                    $"Type syntax '{Slice(syntax)}' is outside the six-variant TSON type grammar.",
                    syntax);
            }

            return type;
        }

        private TsonTypeReference? ReadArrayType(
            ArrayTypeSyntax syntax,
            IReadOnlyDictionary<string, TsonTypeKind> declaredKinds,
            bool reportErrors)
        {
            var elementType = ReadType(syntax.ElementType, declaredKinds, reportErrors);
            if (elementType is null)
            {
                return null;
            }

            if (!IsSupportedArrayElementType(elementType))
            {
                if (reportErrors)
                {
                    Report(
                        "COPE-TSON-0003",
                        $"Array element type '{DisplayType(elementType)}' is outside the ARRAY-M0b TSON type grammar.",
                        syntax);
                }

                return null;
            }

            return TsonTypeReference.Array(elementType);
        }

        private static bool IsSupportedArrayElementType(TsonTypeReference type)
        {
            return type.Kind is TsonTypeKind.Boolean
                or TsonTypeKind.Number
                or TsonTypeKind.String
                or TsonTypeKind.Record
                or TsonTypeKind.Enum
                or TsonTypeKind.Array;
        }

        private TsonValue? ProjectValue(
            ExpressionSyntax syntax,
            TsonTypeReference? expectedType,
            int depth)
        {
            if (depth > _limits.MaximumNestingDepth)
            {
                Report(
                    "COPE-TSON-0005",
                    $"Semantic nesting exceeds the TSON limit of {_limits.MaximumNestingDepth}.",
                    syntax);
                return null;
            }

            _valueNodeCount++;
            if (_valueNodeCount > _limits.MaximumValueNodeCount)
            {
                Report(
                    "COPE-TSON-0005",
                    $"Value-node count exceeds the TSON limit of {_limits.MaximumValueNodeCount}.",
                    syntax);
                return null;
            }

            TsonValue? value = syntax switch
            {
                LiteralExpressionSyntax literal => ProjectLiteral(literal),
                UnaryExpressionSyntax unary => ProjectUnaryNumber(unary),
                ObjectLiteralExpressionSyntax objectLiteral => ProjectObject(objectLiteral, expectedType, depth),
                ArrayLiteralExpressionSyntax arrayLiteral => ProjectArray(arrayLiteral, expectedType, depth),
                CallExpressionSyntax call => ProjectCall(call, expectedType, depth),
                MemberAccessExpressionSyntax member => ProjectZeroPayloadEnum(member, expectedType),
                ParenthesizedExpressionSyntax parenthesized => ProjectValue(parenthesized.Expression, expectedType, depth),
                _ => UnsupportedValue(syntax),
            };

            if (value is not null && expectedType is not null && !MatchesExpectedType(value, expectedType))
            {
                Report(
                    "COPE-TSON-0004",
                    $"TSON value does not match expected type '{DisplayType(expectedType)}'.",
                    syntax);
                return null;
            }

            return value;
        }

        private TsonValue? ProjectArray(
            ArrayLiteralExpressionSyntax syntax,
            TsonTypeReference? expectedType,
            int depth)
        {
            if (expectedType?.Kind != TsonTypeKind.Array || expectedType.ElementType is null)
            {
                Report(
                    "COPE-TSON-0004",
                    "A TSON array requires an authoritative array element type from its enclosing schema context.",
                    syntax);
                return null;
            }

            if (syntax.Elements.Count > _limits.MaximumArrayLength)
            {
                Report(
                    "COPE-TSON-0005",
                    $"Array length exceeds the TSON limit of {_limits.MaximumArrayLength}.",
                    syntax);
                return null;
            }

            var elements = new List<TsonValue>(syntax.Elements.Count);
            for (var index = 0; index < syntax.Elements.Count; index++)
            {
                var element = ProjectValue(syntax.Elements[index], expectedType.ElementType, depth + 1);
                if (element is not null)
                {
                    elements.Add(element);
                }
            }

            return _diagnostics.Count == 0
                ? new TsonArray(new TsonArraySchema(expectedType.ElementType), elements)
                : null;
        }

        private TsonValue? ProjectLiteral(LiteralExpressionSyntax literal)
        {
            switch (literal.LiteralToken.Kind)
            {
                case SyntaxKind.TrueKeyword:
                    return TsonBoolean.True;
                case SyntaxKind.FalseKeyword:
                    return TsonBoolean.False;
                case SyntaxKind.NumberToken when literal.LiteralToken.Value is int number:
                    return TsonNumber.FromDouble(number);
                case SyntaxKind.StringToken:
                    var value = DecodeString(literal.LiteralToken);
                    if (value is null)
                    {
                        return null;
                    }

                    if (value.Length > _limits.MaximumStringLength)
                    {
                        Report(
                            "COPE-TSON-0005",
                            $"String length exceeds the TSON limit of {_limits.MaximumStringLength} UTF-16 code units.",
                            literal);
                        return null;
                    }

                    return new TsonString(value);
                case SyntaxKind.NullKeyword:
                    Report("COPE-TSON-0004", "'null' is not a TSON value.", literal);
                    return null;
                default:
                    Report("COPE-TSON-0004", "Invalid TSON literal.", literal);
                    return null;
            }
        }

        private TsonValue? ProjectUnaryNumber(UnaryExpressionSyntax unary)
        {
            if (unary.OperatorToken.Kind != SyntaxKind.MinusToken
                || unary.Operand is not LiteralExpressionSyntax
                {
                    LiteralToken.Kind: SyntaxKind.NumberToken,
                    LiteralToken.Value: int number,
                })
            {
                return UnsupportedValue(unary);
            }

            return TsonNumber.FromDouble(-(double)number);
        }

        private TsonValue? ProjectObject(
            ObjectLiteralExpressionSyntax syntax,
            TsonTypeReference? expectedType,
            int depth)
        {
            if (syntax.Properties.Count > _limits.MaximumFieldsPerAggregate)
            {
                Report(
                    "COPE-TSON-0005",
                    $"Object field count exceeds the TSON limit of {_limits.MaximumFieldsPerAggregate}.",
                    syntax);
                return null;
            }

            if (expectedType?.Kind == TsonTypeKind.Record)
            {
                return ProjectRecordObject(syntax, expectedType.NominalName!, depth);
            }

            if (expectedType is not null && expectedType.Kind != TsonTypeKind.Object)
            {
                Report(
                    "COPE-TSON-0004",
                    $"Object literal cannot initialize '{DisplayType(expectedType)}'.",
                    syntax);
                return null;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var fields = new List<TsonField>();
            foreach (var property in syntax.Properties)
            {
                var name = ReadPropertyName(property.NameToken);
                if (name is null)
                {
                    continue;
                }

                if (!names.Add(name))
                {
                    Report("COPE-TSON-0004", $"Duplicate object field '{name}'.", property);
                    continue;
                }

                var value = ProjectValue(property.ValueExpression, null, depth + 1);
                if (value is not null)
                {
                    fields.Add(new TsonField(name, value));
                }
            }

            return _diagnostics.Count == 0 ? new TsonObject(fields) : null;
        }

        private TsonValue? ProjectRecordObject(
            ObjectLiteralExpressionSyntax syntax,
            string recordName,
            int depth)
        {
            if (!_definitions.TryGetValue(recordName, out var definition)
                || definition is not TsonRecordDefinition record)
            {
                Report("COPE-TSON-0004", $"Unknown TSON record '{recordName}'.", syntax);
                return null;
            }

            var properties = new Dictionary<string, ObjectPropertySyntax>(StringComparer.Ordinal);
            foreach (var property in syntax.Properties)
            {
                var name = ReadPropertyName(property.NameToken);
                if (name is null)
                {
                    continue;
                }

                if (!properties.TryAdd(name, property))
                {
                    Report("COPE-TSON-0004", $"Duplicate record field '{name}'.", property);
                }
            }

            foreach (var name in properties.Keys)
            {
                if (!record.Fields.Any(field => field.Name == name))
                {
                    Report("COPE-TSON-0004", $"Unknown field '{name}' for record '{record.Name}'.", properties[name]);
                }
            }

            var fields = new List<TsonField>();
            foreach (var field in record.Fields)
            {
                if (!properties.TryGetValue(field.Name, out var property))
                {
                    Report("COPE-TSON-0004", $"Missing field '{field.Name}' for record '{record.Name}'.", syntax);
                    continue;
                }

                var value = ProjectValue(property.ValueExpression, field.Type, depth + 1);
                if (value is not null)
                {
                    fields.Add(new TsonField(field.Name, value, field.Identity));
                }
            }

            return _diagnostics.Count == 0 ? new TsonRecord(record.Identity, fields) : null;
        }

        private TsonValue? ProjectCall(
            CallExpressionSyntax call,
            TsonTypeReference? expectedType,
            int depth)
        {
            if (call.Target is NameExpressionSyntax { IdentifierToken.Text: "$number" })
            {
                return ProjectBitNumber(call);
            }

            if (TryReadRecordConstructor(call.Target, out var recordName))
            {
                if (call.Arguments.Count != 1
                    || call.Arguments[0] is not ObjectLiteralExpressionSyntax objectLiteral)
                {
                    Report(
                        "COPE-TSON-0004",
                        "A canonical record constructor requires exactly one object argument.",
                        call);
                    return null;
                }

                if (expectedType is not null
                    && (expectedType.Kind != TsonTypeKind.Record
                        || expectedType.NominalName != recordName))
                {
                    Report(
                        "COPE-TSON-0004",
                        $"Record '{recordName}' does not match expected type '{DisplayType(expectedType)}'.",
                        call);
                    return null;
                }

                return ProjectRecordObject(objectLiteral, recordName!, depth);
            }

            if (TryReadEnumConstructor(call.Target, out var enumName, out var caseName))
            {
                return ProjectEnum(call, enumName!, caseName!, expectedType, depth);
            }

            return UnsupportedValue(call);
        }

        private TsonValue? ProjectBitNumber(CallExpressionSyntax call)
        {
            if (call.Arguments.Count != 1
                || call.Arguments[0] is not LiteralExpressionSyntax
                {
                    LiteralToken.Kind: SyntaxKind.StringToken,
                } literal)
            {
                Report(
                    "COPE-TSON-0004",
                    "'$number' requires one 16-digit hexadecimal string argument.",
                    call);
                return null;
            }

            var text = DecodeString(literal.LiteralToken);
            if (text is null
                || text.Length != 16
                || !ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var bits))
            {
                Report(
                    "COPE-TSON-0004",
                    "'$number' requires one 16-digit hexadecimal string argument.",
                    literal);
                return null;
            }

            return TsonNumber.FromBits(bits);
        }

        private TsonValue? ProjectZeroPayloadEnum(
            MemberAccessExpressionSyntax member,
            TsonTypeReference? expectedType)
        {
            if (!TryReadEnumConstructor(member, out var enumName, out var caseName))
            {
                return UnsupportedValue(member);
            }

            if (!_definitions.TryGetValue(enumName!, out var definition)
                || definition is not TsonEnumDefinition @enum)
            {
                Report("COPE-TSON-0004", $"Unknown TSON enum '{enumName}'.", member);
                return null;
            }

            var item = @enum.Cases.FirstOrDefault(candidate => candidate.Name == caseName);
            if (item is null)
            {
                Report("COPE-TSON-0004", $"Unknown enum case '{@enum.Name}.{caseName}'.", member);
                return null;
            }

            if (item.Payloads.Count != 0)
            {
                Report(
                    "COPE-TSON-0004",
                    $"Enum case '{@enum.Name}.{item.Name}' requires {item.Payloads.Count} payload value(s).",
                    member);
                return null;
            }

            if (!MatchesExpectedEnum(@enum, expectedType))
            {
                Report(
                    "COPE-TSON-0004",
                    $"Enum '{@enum.Name}' does not match expected type '{DisplayType(expectedType!)}'.",
                    member);
                return null;
            }

            return new TsonEnum(@enum.Identity, item.Identity, item.Name, []);
        }

        private TsonValue? ProjectEnum(
            CallExpressionSyntax call,
            string enumName,
            string caseName,
            TsonTypeReference? expectedType,
            int depth)
        {
            if (!_definitions.TryGetValue(enumName, out var definition)
                || definition is not TsonEnumDefinition @enum)
            {
                Report("COPE-TSON-0004", $"Unknown TSON enum '{enumName}'.", call);
                return null;
            }

            var item = @enum.Cases.FirstOrDefault(candidate => candidate.Name == caseName);
            if (item is null)
            {
                Report("COPE-TSON-0004", $"Unknown enum case '{@enum.Name}.{caseName}'.", call);
                return null;
            }

            if (!MatchesExpectedEnum(@enum, expectedType))
            {
                Report(
                    "COPE-TSON-0004",
                    $"Enum '{@enum.Name}' does not match expected type '{DisplayType(expectedType!)}'.",
                    call);
                return null;
            }

            if (call.Arguments.Count != item.Payloads.Count)
            {
                Report(
                    "COPE-TSON-0004",
                    $"Enum case '{@enum.Name}.{item.Name}' requires {item.Payloads.Count} payload value(s); found {call.Arguments.Count}.",
                    call);
                return null;
            }

            var payloads = new List<TsonField>();
            for (var index = 0; index < item.Payloads.Count; index++)
            {
                var payload = item.Payloads[index];
                var value = ProjectValue(call.Arguments[index], payload.Type, depth + 1);
                if (value is not null)
                {
                    payloads.Add(new TsonField(payload.Name, value, payload.Identity));
                }
            }

            return _diagnostics.Count == 0
                ? new TsonEnum(@enum.Identity, item.Identity, item.Name, payloads)
                : null;
        }

        private TsonValue? UnsupportedValue(SyntaxNode syntax)
        {
            Report(
                "COPE-TSON-0002",
                $"Expression syntax '{syntax.Kind}' is executable or unsupported in TSON.",
                syntax);
            return null;
        }

        private bool MatchesExpectedType(TsonValue value, TsonTypeReference expectedType)
        {
            return expectedType.Kind switch
            {
                TsonTypeKind.Boolean => value is TsonBoolean,
                TsonTypeKind.Number => value is TsonNumber,
                TsonTypeKind.String => value is TsonString,
                TsonTypeKind.Object => value is TsonObject,
                TsonTypeKind.Record => value is TsonRecord record
                    && _definitions.TryGetValue(expectedType.NominalName!, out var recordDefinition)
                    && record.Identity == recordDefinition.Identity,
                TsonTypeKind.Enum => value is TsonEnum @enum
                    && _definitions.TryGetValue(expectedType.NominalName!, out var enumDefinition)
                    && @enum.EnumIdentity == enumDefinition.Identity,
                TsonTypeKind.Array => value is TsonArray array
                    && expectedType.ElementType is not null
                    && TypeReferencesMatch(array.Schema.ElementType, expectedType.ElementType),
                _ => false,
            };
        }

        private static bool TypeReferencesMatch(TsonTypeReference left, TsonTypeReference right)
        {
            var pairs = new Stack<(TsonTypeReference Left, TsonTypeReference Right)>();
            pairs.Push((left, right));
            while (pairs.Count > 0)
            {
                var pair = pairs.Pop();
                if (pair.Left.Kind != pair.Right.Kind
                    || !string.Equals(pair.Left.NominalName, pair.Right.NominalName, StringComparison.Ordinal))
                {
                    return false;
                }

                if (pair.Left.Kind == TsonTypeKind.Array)
                {
                    if (pair.Left.ElementType is null || pair.Right.ElementType is null)
                    {
                        return false;
                    }

                    pairs.Push((pair.Left.ElementType, pair.Right.ElementType));
                }
            }

            return true;
        }

        private static bool MatchesExpectedEnum(
            TsonEnumDefinition definition,
            TsonTypeReference? expectedType)
        {
            return expectedType is null
                || expectedType.Kind == TsonTypeKind.Enum
                && expectedType.NominalName == definition.Name;
        }

        private string? ReadPropertyName(SyntaxToken token)
        {
            return token.Kind == SyntaxKind.StringToken
                ? DecodeString(token)
                : token.Text;
        }

        private string? DecodeString(SyntaxToken token)
        {
            var text = token.Text;
            if (text.Length < 2)
            {
                Report("COPE-TSON-0004", "Malformed TSON string literal.", token);
                return null;
            }

            var builder = new StringBuilder(text.Length - 2);
            for (var index = 1; index < text.Length - 1; index++)
            {
                var current = text[index];
                if (current != '\\')
                {
                    if (char.IsSurrogate(current))
                    {
                        if (char.IsHighSurrogate(current)
                            && index + 1 < text.Length - 1
                            && char.IsLowSurrogate(text[index + 1]))
                        {
                            builder.Append(current);
                            builder.Append(text[++index]);
                            continue;
                        }

                        Report("COPE-TSON-0004", "TSON strings cannot contain isolated UTF-16 surrogates.", token);
                        return null;
                    }

                    builder.Append(current);
                    continue;
                }

                if (++index >= text.Length - 1)
                {
                    Report("COPE-TSON-0004", "Malformed TSON string escape.", token);
                    return null;
                }

                var escape = text[index];
                switch (escape)
                {
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    case '\'':
                        builder.Append('\'');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'u':
                        if (!TryDecodeUnicodeEscape(text, ref index, out var decoded))
                        {
                            Report("COPE-TSON-0004", "Malformed TSON Unicode escape.", token);
                            return null;
                        }

                        if (char.IsHighSurrogate(decoded))
                        {
                            if (!TryDecodeFollowingLowSurrogate(text, ref index, out var lowSurrogate))
                            {
                                Report("COPE-TSON-0004", "TSON Unicode escapes cannot encode isolated surrogates.", token);
                                return null;
                            }

                            builder.Append(decoded);
                            builder.Append(lowSurrogate);
                        }
                        else if (char.IsLowSurrogate(decoded))
                        {
                            Report("COPE-TSON-0004", "TSON Unicode escapes cannot encode isolated surrogates.", token);
                            return null;
                        }
                        else
                        {
                            builder.Append(decoded);
                        }

                        break;
                    default:
                        Report("COPE-TSON-0004", $"Unsupported TSON string escape '\\{escape}'.", token);
                        return null;
                }
            }

            return builder.ToString();
        }

        private static bool TryDecodeUnicodeEscape(
            string text,
            ref int escapeIndex,
            out char value)
        {
            value = default;
            var firstHexIndex = escapeIndex + 1;
            if (firstHexIndex + 4 > text.Length - 1)
            {
                return false;
            }

            var hex = text.AsSpan(firstHexIndex, 4);
            if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codeUnit))
            {
                return false;
            }

            escapeIndex += 4;
            value = (char)codeUnit;
            return true;
        }

        private static bool TryDecodeFollowingLowSurrogate(
            string text,
            ref int highEscapeEndIndex,
            out char lowSurrogate)
        {
            lowSurrogate = default;
            if (highEscapeEndIndex + 6 >= text.Length
                || text[highEscapeEndIndex + 1] != '\\'
                || text[highEscapeEndIndex + 2] != 'u')
            {
                return false;
            }

            var hex = text.AsSpan(highEscapeEndIndex + 3, 4);
            if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codeUnit)
                || !char.IsLowSurrogate((char)codeUnit))
            {
                return false;
            }

            highEscapeEndIndex += 6;
            lowSurrogate = (char)codeUnit;
            return true;
        }

        private static bool TryReadRecordConstructor(
            ExpressionSyntax target,
            out string? recordName)
        {
            recordName = null;
            if (target is not MemberAccessExpressionSyntax
                {
                    Target: NameExpressionSyntax { IdentifierToken.Text: "$record" },
                } member)
            {
                return false;
            }

            recordName = member.NameToken.Text;
            return true;
        }

        private static bool TryReadEnumConstructor(
            ExpressionSyntax target,
            out string? enumName,
            out string? caseName)
        {
            enumName = null;
            caseName = null;
            if (target is not MemberAccessExpressionSyntax
                {
                    Target: NameExpressionSyntax enumExpression,
                } member)
            {
                return false;
            }

            enumName = enumExpression.IdentifierToken.Text;
            caseName = member.NameToken.Text;
            return true;
        }

        private static bool IsValidSchemaIdentity(string identity)
        {
            return identity.StartsWith("copeland://", StringComparison.Ordinal)
                && identity.Length > "copeland://".Length
                && !identity.Contains('#', StringComparison.Ordinal)
                && !identity.Any(char.IsWhiteSpace);
        }

        private static string TypeIdentity(string schemaIdentity, string typeName)
        {
            return $"{schemaIdentity}#{typeName}";
        }

        private static string FieldIdentity(
            string schemaIdentity,
            string typeName,
            string fieldName)
        {
            return $"{TypeIdentity(schemaIdentity, typeName)}.{fieldName}";
        }

        private static string CaseIdentity(
            string schemaIdentity,
            string enumName,
            string caseName)
        {
            return $"{TypeIdentity(schemaIdentity, enumName)}.{caseName}";
        }

        private static string PayloadIdentity(
            string schemaIdentity,
            string enumName,
            string caseName,
            string payloadName)
        {
            return $"{CaseIdentity(schemaIdentity, enumName, caseName)}.{payloadName}";
        }

        private static string DisplayType(TsonTypeReference type)
        {
            return type.Kind == TsonTypeKind.Array
                ? DisplayType(type.ElementType!) + "[]"
                : type.NominalName ?? type.Kind.ToString().ToLowerInvariant();
        }

        private string Slice(SyntaxNode syntax)
        {
            var span = GetSpan(syntax);
            return span.Length == 0 ? syntax.Kind.ToString() : _source.Substring(span.Position, span.Length);
        }

        private void Report(string code, string message, object? syntax)
        {
            if (_insideTableCell)
            {
                code = code switch
                {
                    "COPE-TSON-0003" => "COPE-TSON-TABLE-0002",
                    "COPE-TSON-0005" => "COPE-TSON-TABLE-0005",
                    _ => "COPE-TSON-TABLE-0004",
                };
            }
            else if (_hasTableDeclarations && code.StartsWith("COPE-TSON-", StringComparison.Ordinal))
            {
                code = code switch
                {
                    "COPE-TSON-0001" or "COPE-TSON-0002" => "COPE-TSON-TABLE-0001",
                    "COPE-TSON-0003" => "COPE-TSON-TABLE-0002",
                    "COPE-TSON-0004" => "COPE-TSON-TABLE-0004",
                    "COPE-TSON-0005" => "COPE-TSON-TABLE-0005",
                    _ => code,
                };
            }

            var span = syntax switch
            {
                SyntaxToken token => (token.Position, Math.Max(1, token.Text.Length)),
                SyntaxNode node => GetSpan(node),
                _ => (0, Math.Max(1, _source.Length)),
            };
            _diagnostics.Add(new TsonDiagnostic(code, message, span.Item1, Math.Max(1, span.Item2)));
        }

        private static (int Position, int Length) GetSpan(SyntaxNode syntax)
        {
            var tokens = EnumerateTokens(syntax).Where(token => token.Text.Length > 0).ToArray();
            if (tokens.Length == 0)
            {
                return (0, 1);
            }

            var start = tokens.Min(token => token.Position);
            var end = tokens.Max(token => token.Position + token.Text.Length);
            return (start, Math.Max(1, end - start));
        }

        private static IEnumerable<SyntaxToken> EnumerateTokens(SyntaxNode syntax)
        {
            foreach (var child in syntax.GetChildren())
            {
                if (child is SyntaxToken token)
                {
                    yield return token;
                }
                else if (child is SyntaxNode node)
                {
                    foreach (var descendant in EnumerateTokens(node))
                    {
                        yield return descendant;
                    }
                }
            }
        }
    }
}
