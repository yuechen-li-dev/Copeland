using Copeland.TS.Diagnostics;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Tson;

namespace Copeland.TS.Semantics;

public static class Binder
{
    public static BoundCompilation Bind(SyntaxTree tree)
    {
        var impl = new BinderImpl(tree, null);
        return impl.Bind();
    }

    internal static BoundCompilation Bind(SyntaxTree tree, CopelandAssetResolver? assetResolver)
    {
        var impl = new BinderImpl(tree, assetResolver);
        return impl.Bind();
    }

    private sealed class Scope(Scope? parent)
    {
        private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);
        public Scope? Parent { get; } = parent;
        public bool TryDeclare(Symbol s) => _symbols.TryAdd(s.Name, s);
        public bool TryLookup(string n, out Symbol? symbol)
        {
            for (var c = this; c is not null; c = c.Parent)
                if (c._symbols.TryGetValue(n, out symbol)) return true;
            symbol = null; return false;
        }
    }

    private sealed class BinderImpl(SyntaxTree tree, CopelandAssetResolver? assetResolver)
    {
        private readonly SyntaxTree _tree = tree;
        private readonly CopelandAssetResolver? _assetResolver = assetResolver;
        private readonly DiagnosticBag _diagnostics = new();
        private readonly Scope _global = new(null);
        private Scope _scope = null!;
        private FunctionSymbol? _currentFunction;
        private readonly List<BoundFunctionDeclaration> _functions = [];
        private readonly List<BoundEnumDeclaration> _enums = [];
        private readonly List<BoundRecordDeclaration> _records = [];
        private readonly List<BoundTableDefinition> _tables = [];
        private readonly List<BoundStatement> _globals = [];
        private readonly Dictionary<string, EnumTypeSymbol> _enumTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RecordTypeSymbol> _recordTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TableTypeSymbol> _tableTypes = new(StringComparer.Ordinal);
        private EnumTypeSymbol? _tableBoundsErrorType;
        private EnumTypeSymbol? _tsonEncodeErrorType;
        private readonly Dictionary<TypeSymbol, BoundTsonEncodingPlan> _tsonEncodingPlans = [];
        private bool _usesTsonEncode;
        private readonly List<PropagationTargetContext> _propagationTargets = [];
        private int _nextHandlerId = 1;
        private int _nextRecordTypeId = 1;
        private int _nextTableTypeId = 1;
        private string? _schemaIdentity;

        private sealed class PropagationTargetContext(BoundHandlerId handlerId)
        {
            public BoundHandlerId HandlerId { get; } = handlerId;
            public TypeSymbol? ErrorType { get; set; }
            public bool WasTargeted { get; set; }
        }

        public BoundCompilation Bind()
        {
            _scope = _global;
            BindSchemaMetadata(_tree.Root);
            PredeclareTableBoundsError();
            PredeclareTsonEncodeError();
            PredeclareRecords(_tree.Root);
            PredeclareTables(_tree.Root);
            PredeclareEnums(_tree.Root);
            PredeclareFunctions(_tree.Root);
            BindRecordBodies(_tree.Root);
            BindEnumBodies(_tree.Root);
            BindTableBodies(_tree.Root);
            ValidateRecordCycles();
            foreach (var m in _tree.Root.Members)
            {
                if (m is FunctionDeclarationSyntax f) _functions.Add(BindFunction(f));
                else if (m is EnumDeclarationSyntax e && e.Identifier.Text != "TableBoundsError" && _enumTypes.TryGetValue(e.Identifier.Text, out var enumType)) _enums.Add(new BoundEnumDeclaration(enumType));
                else if (m is RecordDeclarationSyntax r && _recordTypes.TryGetValue(r.Identifier.Text, out var recordType)) _records.Add(new BoundRecordDeclaration(recordType));
                else if (m is GlobalStatementMemberSyntax g && !IsSchemaDeclaration(g.Statement)) _globals.Add(BindStatement(g.Statement));
            }
            if (_tables.Count > 0 && _tableBoundsErrorType is not null)
            {
                _enums.Insert(0, new BoundEnumDeclaration(_tableBoundsErrorType));
            }
            if (_usesTsonEncode && _tsonEncodeErrorType is not null)
            {
                _enums.Insert(0, new BoundEnumDeclaration(_tsonEncodeErrorType));
            }
            return new BoundCompilation(
                _tree,
                new BoundProgram(
                    _functions,
                    _enums,
                    _records,
                    _globals,
                    _tables,
                    _tsonEncodingPlans.Values.OrderBy(plan => plan.Id, StringComparer.Ordinal).ToArray()),
                _tree.Diagnostics.Concat(_diagnostics.Diagnostics).ToArray());
        }

        private void PredeclareTsonEncodeError()
        {
            var errorType = new EnumTypeSymbol("TsonEncodeError");
            errorType.AddCase(new EnumCaseSymbol("InvalidUnicode", errorType, []));
            errorType.AddCase(new EnumCaseSymbol("OutputLimitExceeded", errorType, []));
            _tsonEncodeErrorType = errorType;
            _enumTypes.Add(errorType.Name, errorType);
            _global.TryDeclare(new VariableSymbol(errorType.Name, errorType, true));
        }

        private void PredeclareTableBoundsError()
        {
            var tableBoundsError = new EnumTypeSymbol("TableBoundsError");
            tableBoundsError.AddCase(new EnumCaseSymbol(
                "InvalidIndex",
                tableBoundsError,
                [new EnumPayloadFieldSymbol("index", PrimitiveTypeSymbol.Number)]));
            tableBoundsError.AddCase(new EnumCaseSymbol(
                "OutOfBounds",
                tableBoundsError,
                [
                    new EnumPayloadFieldSymbol("index", PrimitiveTypeSymbol.Number),
                    new EnumPayloadFieldSymbol("rowCount", PrimitiveTypeSymbol.Number),
                ]));
            _tableBoundsErrorType = tableBoundsError;
            _enumTypes.Add(tableBoundsError.Name, tableBoundsError);
            _global.TryDeclare(new VariableSymbol(tableBoundsError.Name, tableBoundsError, true));
        }

        private void PredeclareTables(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<TableDeclarationSyntax>())
            {
                if (declaration.Identifier.Text == "TsonEncodeError")
                {
                    Report(
                        "COPE-TSON-ENCODE-0001",
                        "'TsonEncodeError' is a compiler-owned TSON encoding error enum.",
                        declaration.Identifier);
                    continue;
                }
                var table = new TableTypeSymbol(declaration.Identifier.Text, new TableTypeId(_nextTableTypeId++));
                if (!_global.TryDeclare(new VariableSymbol(table.Name, table, true)) || _tableTypes.ContainsKey(table.Name))
                {
                    Report("COPE-TABLE-0002", $"Duplicate table declaration '{table.Name}'.", declaration.Identifier);
                    continue;
                }
                _tableTypes.Add(table.Name, table);
            }
        }

        private void BindTableBodies(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<TableDeclarationSyntax>())
            {
                if (!_tableTypes.TryGetValue(declaration.Identifier.Text, out var table)) continue;
                if (declaration.Columns.Count == 0) Report("COPE-TABLE-0003", "A table requires at least one column.", declaration.Identifier);
                var names = new HashSet<string>(StringComparer.Ordinal);
                var columns = new List<BoundTableColumnDefinition>();
                int? rowCount = null;
                foreach (var columnSyntax in declaration.Columns)
                {
                    if (!names.Add(columnSyntax.Identifier.Text)) { Report("COPE-TABLE-0004", $"Duplicate column '{columnSyntax.Identifier.Text}'.", columnSyntax.Identifier); continue; }
                    TypeSymbol? explicitType = columnSyntax.ExplicitType is null ? null : BindType(columnSyntax.ExplicitType, columnSyntax.Identifier, "COPE-TABLE-0019", "table column");
                    if (columnSyntax.ExplicitType is null && columnSyntax.Cells.Elements.Count == 0)
                    {
                        Report("COPE-TABLE-0005", "An empty table column requires an explicit element type.", columnSyntax.Identifier);
                    }
                    var boundCells = columnSyntax.Cells.Elements.Select(cell => BindExpression(cell, explicitType)).ToArray();
                    var elementType = explicitType ?? boundCells.FirstOrDefault()?.Type ?? PrimitiveTypeSymbol.Error;
                    if (!IsEligibleTableCellType(elementType, [], out bool isCyclic))
                    {
                        string diagnosticId = isCyclic ? "COPE-TABLE-0010" : "COPE-TABLE-0009";
                        string message = isCyclic
                            ? "Table column element types cannot be recursive or cyclic."
                            : "Table column element types must be deeply immutable.";
                        Report(diagnosticId, message, columnSyntax.Identifier);
                    }
                    var cells = new List<BoundTableConstant>();
                    foreach (var cell in boundCells)
                    {
                        var constant = BindTableConstant(cell);
                        if (constant is null) Report("COPE-TABLE-0009", "Table cells must be static deeply immutable constants.", columnSyntax.Identifier);
                        else cells.Add(constant);
                        if (!TypeFacts.AreEquivalent(elementType, cell.Type)) Report(explicitType is null ? "COPE-TABLE-0006" : "COPE-TABLE-0007", $"Table column '{columnSyntax.Identifier.Text}' has an incompatible cell type.", columnSyntax.Identifier);
                    }
                    if (rowCount is null) rowCount = boundCells.Length;
                    else if (rowCount != boundCells.Length) Report("COPE-TABLE-0008", "Table columns must have equal lengths.", columnSyntax.Identifier);
                    var column = new TableColumnSymbol(columnSyntax.Identifier.Text, new TableColumnId(table.Id, table.Columns.Count), elementType);
                    table.AddColumn(column);
                    columns.Add(new BoundTableColumnDefinition(column, cells));
                }
                _tables.Add(new BoundTableDefinition(table, columns, rowCount ?? 0));
            }
        }

        private static BoundTableConstant? BindTableConstant(BoundExpression expression)
            => expression switch
            {
                BoundLiteralExpression literal when literal.Value is not null => new BoundTableLiteralConstant(literal.Value, literal.Type),
                BoundUnaryExpression { OperatorKind: SyntaxKind.MinusToken, Operand: BoundLiteralExpression literal }
                    when literal.Type == PrimitiveTypeSymbol.Number && literal.Value is IConvertible number => new BoundTableLiteralConstant(-number.ToDouble(System.Globalization.CultureInfo.InvariantCulture), literal.Type),
                BoundEnumValueExpression value => BindTableEnumConstant(value),
                BoundOkExpression ok => BindTableResultConstant(true, ok.Payload, (ResultTypeSymbol)ok.Type),
                BoundErrExpression err => BindTableResultConstant(false, err.Payload, (ResultTypeSymbol)err.Type),
                BoundRecordConstructionExpression record => BindTableRecordConstant(record),
                _ => null,
            };

        private static BoundTableConstant? BindTableEnumConstant(BoundEnumValueExpression value)
        {
            var payloads = value.Arguments.Select(BindTableConstant).ToArray();
            return payloads.Any(payload => payload is null)
                ? null
                : new BoundTableEnumConstant(value.Case, payloads.Select(payload => payload!).ToArray());
        }

        private static BoundTableConstant? BindTableResultConstant(bool isOk, BoundExpression payload, ResultTypeSymbol type)
        {
            var constant = BindTableConstant(payload);
            return constant is null ? null : new BoundTableResultConstant(isOk, constant, type);
        }

        private static BoundTableConstant? BindTableRecordConstant(BoundRecordConstructionExpression record)
        {
            var fields = new List<BoundTableRecordFieldConstant>();
            foreach (var initializer in record.Initializers)
            {
                var constant = BindTableConstant(initializer.Value);
                if (constant is null) return null;
                fields.Add(new BoundTableRecordFieldConstant(initializer.Field, constant));
            }
            return new BoundTableRecordConstant(record.RecordType, fields);
        }

        private static bool IsEligibleTableCellType(TypeSymbol type, HashSet<TypeSymbol> visiting, out bool isCyclic)
        {
            if (!visiting.Add(type))
            {
                isCyclic = true;
                return false;
            }
            bool eligible = type switch
            {
                PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.Number
                    || primitive == PrimitiveTypeSymbol.String
                    || primitive == PrimitiveTypeSymbol.Boolean => true,
                EnumTypeSymbol @enum => @enum.Cases.All(@case => @case.PayloadFields.All(field => IsEligibleTableCellType(field.Type, visiting, out _))),
                RecordTypeSymbol record => record.Fields.All(field => IsEligibleTableCellType(field.Type, visiting, out _)),
                ResultTypeSymbol result => IsEligibleTableCellType(result.SuccessType, visiting, out _)
                    && IsEligibleTableCellType(result.ErrorType, visiting, out _),
                _ => false,
            };
            isCyclic = !eligible && ContainsCyclicTableCellType(type, []);
            visiting.Remove(type);
            return eligible;
        }

        private static bool ContainsCyclicTableCellType(TypeSymbol type, HashSet<TypeSymbol> visiting)
        {
            if (!visiting.Add(type)) return true;
            bool cyclic = type switch
            {
                EnumTypeSymbol @enum => @enum.Cases.Any(@case => @case.PayloadFields.Any(field => ContainsCyclicTableCellType(field.Type, visiting))),
                RecordTypeSymbol record => record.Fields.Any(field => ContainsCyclicTableCellType(field.Type, visiting)),
                ResultTypeSymbol result => ContainsCyclicTableCellType(result.SuccessType, visiting)
                    || ContainsCyclicTableCellType(result.ErrorType, visiting),
                _ => false,
            };
            visiting.Remove(type);
            return cyclic;
        }

        private void BindSchemaMetadata(CompilationUnitSyntax root)
        {
            var declarations = root.Members
                .OfType<GlobalStatementMemberSyntax>()
                .Select(member => member.Statement)
                .OfType<VariableDeclarationStatementSyntax>()
                .Where(declaration => declaration.Identifier.Text == "$schema")
                .ToArray();

            if (declarations.Length > 1)
            {
                foreach (var duplicate in declarations.Skip(1))
                {
                    Report("COPE-TSON-ASSET-0004", "A compilation unit can declare '$schema' only once.", duplicate.Identifier);
                }
            }

            if (declarations.Length == 0)
            {
                return;
            }

            var declaration = declarations[0];
            bool exactForm = declaration.Keyword.Kind == SyntaxKind.ConstKeyword
                && declaration.Type is PredefinedTypeSyntax predefined
                && predefined.Keyword.Kind == SyntaxKind.StringKeyword
                && declaration.Initializer is LiteralExpressionSyntax literal
                && literal.LiteralToken.Kind == SyntaxKind.StringToken;
            if (!exactForm)
            {
                Report(
                    "COPE-TSON-ASSET-0004",
                    "Schema metadata must use exactly 'const $schema: string = \"copeland://...\";'.",
                    declaration.Identifier);
                return;
            }

            var schemaLiteral = (LiteralExpressionSyntax)declaration.Initializer;
            string identity = (string)schemaLiteral.LiteralToken.Value!;
            if (!IsValidSchemaIdentity(identity))
            {
                Report(
                    "COPE-TSON-ASSET-0004",
                    "Schema identity must be a nonblank whitespace-free 'copeland://...' value without '#'.",
                    schemaLiteral.LiteralToken);
                return;
            }

            _schemaIdentity = identity;
        }

        private static bool IsSchemaDeclaration(StatementSyntax statement)
        {
            return statement is VariableDeclarationStatementSyntax declaration
                && declaration.Identifier.Text == "$schema";
        }

        private static bool IsValidSchemaIdentity(string identity)
        {
            return identity.StartsWith("copeland://", StringComparison.Ordinal)
                && identity.Length > "copeland://".Length
                && !identity.Any(char.IsWhiteSpace)
                && !identity.Contains('#', StringComparison.Ordinal);
        }

        private void PredeclareRecords(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<RecordDeclarationSyntax>())
            {
                if (declaration.Identifier.Text is "tsonAsset" or "tsonEncode")
                {
                    string name = declaration.Identifier.Text;
                    Report(name == "tsonEncode" ? "COPE-TSON-ENCODE-0001" : "COPE-TSON-ASSET-0001", $"'{name}' is a compiler intrinsic and cannot be redefined.", declaration.Identifier);
                    continue;
                }
                if (declaration.Identifier.Text == "TsonEncodeError")
                {
                    Report(
                        "COPE-TSON-ENCODE-0001",
                        "'TsonEncodeError' is a compiler-owned TSON encoding error enum.",
                        declaration.Identifier);
                    continue;
                }
                if (declaration.ConstKeyword is not null)
                {
                    Report("COPE-REC-0001", "Record declarations use 'record', not 'const record'.", declaration.ConstKeyword);
                }

                string? identity = _schemaIdentity is null ? null : $"{_schemaIdentity}#{declaration.Identifier.Text}";
                var recordType = new RecordTypeSymbol(
                    declaration.Identifier.Text,
                    new RecordTypeId(_nextRecordTypeId++),
                    identity);
                if (!_global.TryDeclare(new VariableSymbol(recordType.Name, recordType, true)) || _recordTypes.ContainsKey(recordType.Name))
                {
                    Report("COPE-REC-0002", $"Duplicate record declaration '{recordType.Name}'.", declaration.Identifier);
                    continue;
                }
                _recordTypes.Add(recordType.Name, recordType);
            }
        }

        private void BindRecordBodies(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<RecordDeclarationSyntax>())
            {
                if (!_recordTypes.TryGetValue(declaration.Identifier.Text, out var recordType) || recordType.Fields.Count > 0)
                {
                    continue;
                }

                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var fieldSyntax in declaration.Fields)
                {
                    if (!fieldSyntax.HasExplicitType
                        || !fieldSyntax.HasTerminator
                        || fieldSyntax.UnsupportedTokens.Count > 0)
                    {
                        Report("COPE-REC-0001", $"Record field '{fieldSyntax.Identifier.Text}' must have exactly one explicit type and no initializer or method body.", fieldSyntax.Identifier);
                    }
                    if (!names.Add(fieldSyntax.Identifier.Text))
                    {
                        Report("COPE-REC-0003", $"Duplicate field '{fieldSyntax.Identifier.Text}' in record '{recordType.Name}'.", fieldSyntax.Identifier);
                        continue;
                    }

                    var fieldType = !fieldSyntax.HasExplicitType
                        ? PrimitiveTypeSymbol.Error
                        : BindType(fieldSyntax.Type, fieldSyntax.Identifier, "COPE-REC-0001", "record field");
                    var fieldId = new RecordFieldId(recordType.Id, recordType.Fields.Count);
                    recordType.AddField(new RecordFieldSymbol(fieldSyntax.Identifier.Text, fieldId, fieldType));
                }
            }
        }

        private void ValidateRecordCycles()
        {
            var visiting = new HashSet<RecordTypeSymbol>();
            var visited = new HashSet<RecordTypeSymbol>();
            foreach (var recordType in _recordTypes.Values)
            {
                ValidateRecordCycle(recordType, visiting, visited);
            }
        }

        private void ValidateRecordCycle(RecordTypeSymbol recordType, HashSet<RecordTypeSymbol> visiting, HashSet<RecordTypeSymbol> visited)
        {
            if (visited.Contains(recordType))
            {
                return;
            }
            if (!visiting.Add(recordType))
            {
                var declaration = _tree.Root.Members.OfType<RecordDeclarationSyntax>().First(item => item.Identifier.Text == recordType.Name);
                Report("COPE-REC-0004", $"Recursive record definition involving '{recordType.Name}' is not supported.", declaration.Identifier);
                return;
            }

            foreach (var dependency in recordType.Fields.SelectMany(field => EnumerateContainedRecordTypes(field.Type)))
            {
                ValidateRecordCycle(dependency, visiting, visited);
            }
            visiting.Remove(recordType);
            visited.Add(recordType);
        }

        private static IEnumerable<RecordTypeSymbol> EnumerateContainedRecordTypes(TypeSymbol type)
            => EnumerateContainedRecordTypes(type, []);

        private static IEnumerable<RecordTypeSymbol> EnumerateContainedRecordTypes(TypeSymbol type, HashSet<TypeSymbol> visited)
        {
            if (!visited.Add(type)) yield break;
            switch (type)
            {
                case RecordTypeSymbol recordType:
                    yield return recordType;
                    break;
                case ArrayTypeSymbol arrayType:
                    foreach (var nested in EnumerateContainedRecordTypes(arrayType.ElementType, visited)) yield return nested;
                    break;
                case ResultTypeSymbol resultType:
                    foreach (var nested in EnumerateContainedRecordTypes(resultType.SuccessType, visited)) yield return nested;
                    foreach (var nested in EnumerateContainedRecordTypes(resultType.ErrorType, visited)) yield return nested;
                    break;
                case EnumTypeSymbol enumType:
                    foreach (var payloadType in enumType.Cases.SelectMany(@case => @case.PayloadFields).Select(field => field.Type))
                    {
                        foreach (var nested in EnumerateContainedRecordTypes(payloadType, visited)) yield return nested;
                    }
                    break;
            }
        }

        private void PredeclareFunctions(CompilationUnitSyntax root)
        {
            foreach (var m in root.Members.OfType<FunctionDeclarationSyntax>())
            {
                if (m.Identifier.Text is "tsonAsset" or "tsonEncode")
                {
                    string name = m.Identifier.Text;
                    Report(name == "tsonEncode" ? "COPE-TSON-ENCODE-0001" : "COPE-TSON-ASSET-0001", $"'{name}' is a compiler intrinsic and cannot be redefined.", m.Identifier);
                    continue;
                }
                if (m.Identifier.Text == "TsonEncodeError")
                {
                    Report(
                        "COPE-TSON-ENCODE-0001",
                        "'TsonEncodeError' is a compiler-owned TSON encoding error enum.",
                        m.Identifier);
                    continue;
                }
                var ps = new List<ParameterSymbol>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in m.Parameters)
                {
                    if (p.Identifier.Text is "tsonEncode" or "TsonEncodeError")
                    {
                        Report(
                            "COPE-TSON-ENCODE-0001",
                            $"'{p.Identifier.Text}' is compiler-owned and cannot be declared or shadowed.",
                            p.Identifier);
                    }
                    var pt = BindType(p.Type, p.Identifier, missingId: "COPE-TYPE-0002", missingPrefix: "parameter");
                    if (!seen.Add(p.Identifier.Text)) Report("COPE-BIND-0005", $"Duplicate parameter '{p.Identifier.Text}'.", p.Identifier);
                    ps.Add(new ParameterSymbol(p.Identifier.Text, pt));
                }
                var rt = BindType(m.ReturnType, m.Identifier, missingId: "COPE-TYPE-0002", missingPrefix: "function return");
                var fn = new FunctionSymbol(m.Identifier.Text, ps, rt);
                if (_enumTypes.ContainsKey(fn.Name) || _recordTypes.ContainsKey(fn.Name))
                {
                    Report("COPE-BIND-0002", $"Name '{fn.Name}' is already used by a named type.", m.Identifier);
                    continue;
                }
                if (!_global.TryDeclare(fn)) Report("COPE-BIND-0002", $"Duplicate declaration '{fn.Name}'.", m.Identifier);
            }
        }
        private void PredeclareEnums(CompilationUnitSyntax root)
        {
            foreach (var m in root.Members.OfType<EnumDeclarationSyntax>())
            {
                if (m.Identifier.Text is "tsonAsset" or "tsonEncode")
                {
                    string name = m.Identifier.Text;
                    Report(name == "tsonEncode" ? "COPE-TSON-ENCODE-0001" : "COPE-TSON-ASSET-0001", $"'{name}' is a compiler intrinsic and cannot be redefined.", m.Identifier);
                    continue;
                }
                if (m.Identifier.Text == "TableBoundsError")
                {
                    Report("COPE-TABLE-0002", "'TableBoundsError' is a compiler-owned table bounds enum.", m.Identifier);
                    continue;
                }
                if (m.Identifier.Text == "TsonEncodeError")
                {
                    Report("COPE-TSON-ENCODE-0001", "'TsonEncodeError' is a compiler-owned TSON encoding error enum.", m.Identifier);
                    continue;
                }
                string? identity = _schemaIdentity is null ? null : $"{_schemaIdentity}#{m.Identifier.Text}";
                var enumType = new EnumTypeSymbol(m.Identifier.Text, identity);
                if (_tableTypes.ContainsKey(m.Identifier.Text))
                {
                    Report("COPE-TABLE-0002", $"Name '{m.Identifier.Text}' is already used by a record table.", m.Identifier);
                    continue;
                }
                if (!_global.TryDeclare(new VariableSymbol(m.Identifier.Text, enumType, true)) || _enumTypes.ContainsKey(m.Identifier.Text))
                {
                    Report("COPE-ENUM-0001", $"Duplicate enum declaration '{m.Identifier.Text}'.", m.Identifier);
                    continue;
                }
                _enumTypes[m.Identifier.Text] = enumType;
            }
        }

        private void BindEnumBodies(CompilationUnitSyntax root)
        {
            foreach (var decl in root.Members.OfType<EnumDeclarationSyntax>())
            {
                if (decl.Identifier.Text == "TableBoundsError")
                {
                    continue;
                }
                if (!_enumTypes.TryGetValue(decl.Identifier.Text, out var enumType))
                    continue;
                var seenCases = new HashSet<string>(StringComparer.Ordinal);
                foreach (var @case in decl.Cases)
                {
                    if (!seenCases.Add(@case.Identifier.Text))
                    {
                        Report("COPE-ENUM-0002", $"Duplicate enum case '{@case.Identifier.Text}' in enum '{enumType.Name}'.", @case.Identifier);
                        continue;
                    }
                    var seenPayload = new HashSet<string>(StringComparer.Ordinal);
                    var payloadFields = new List<EnumPayloadFieldSymbol>();
                    foreach (var field in @case.PayloadFields)
                    {
                        if (!seenPayload.Add(field.Identifier.Text))
                        {
                            Report("COPE-ENUM-0003", $"Duplicate payload field '{field.Identifier.Text}' in enum case '{@case.Identifier.Text}'.", field.Identifier);
                            continue;
                        }
                        payloadFields.Add(new EnumPayloadFieldSymbol(field.Identifier.Text, BindType(field.Type, field.Identifier, "COPE-TYPE-0002", "enum payload")));
                    }
                    enumType.AddCase(new EnumCaseSymbol(@case.Identifier.Text, enumType, payloadFields));
                }
            }
        }

        private BoundFunctionDeclaration BindFunction(FunctionDeclarationSyntax s)
        {
            _global.TryLookup(s.Identifier.Text, out var sym);
            var fn = sym as FunctionSymbol ?? new FunctionSymbol(s.Identifier.Text, [], PrimitiveTypeSymbol.Error);
            var prevFn = _currentFunction; _currentFunction = fn;
            var prev = _scope; _scope = new Scope(_global);
            foreach (var p in fn.Parameters)
            {
                if (!_scope.TryDeclare(p)) Report("COPE-BIND-0005", $"Duplicate parameter '{p.Name}'.", s.Identifier);
            }
            var body = (BoundBlockStatement)BindStatement(s.Body);
            _scope = prev; _currentFunction = prevFn;
            return new BoundFunctionDeclaration(fn, body);
        }

        private BoundStatement BindStatement(StatementSyntax s) => s switch
        {
            BlockStatementSyntax b => BindBlock(b),
            VariableDeclarationStatementSyntax v => BindVariable(v),
            ExpressionStatementSyntax e => BindExpressionStatement(e),
            IfStatementSyntax i => BindIf(i),
            WhileStatementSyntax w => new BoundWhileStatement(EnsureBoolean(BindExpression(w.Condition), w.WhileKeyword), BindStatement(w.Body)),
            ForStatementSyntax f => BindFor(f),
            ReturnStatementSyntax r => BindReturn(r),
            NestedRecordDeclarationStatementSyntax nested => BindNestedRecord(nested),
            NestedTableDeclarationStatementSyntax nested => BindNestedTable(nested),
            _ => new BoundExpressionStatement(new BoundErrorExpression())
        };

        private BoundStatement BindNestedRecord(NestedRecordDeclarationStatementSyntax nested)
        {
            Report("COPE-REC-0001", "Record declarations are allowed only at module scope.", nested.Declaration.RecordKeyword);
            return new BoundExpressionStatement(new BoundErrorExpression());
        }

        private BoundStatement BindNestedTable(NestedTableDeclarationStatementSyntax nested)
        {
            Report("COPE-TABLE-0001", "Record table declarations are allowed only at module scope.", nested.Declaration.RecordKeyword);
            return new BoundExpressionStatement(new BoundErrorExpression());
        }

        private BoundStatement BindBlock(BlockStatementSyntax b)
        {
            var prev = _scope; _scope = new Scope(prev);
            var list = b.Statements.Select(BindStatement).ToArray();
            _scope = prev;
            return new BoundBlockStatement(list);
        }

        private BoundStatement BindExpressionStatement(ExpressionStatementSyntax statement)
        {
            var expression = BindExpression(statement.Expression);
            if (expression.Type is ResultTypeSymbol)
            {
                Report("COPE-TYPE-0013", "Result expression statements must be handled, stored, returned, matched, propagated, or unwrapped.", AnchorToken(statement.Expression));
            }

            return new BoundExpressionStatement(expression);
        }

        private BoundStatement BindVariable(VariableDeclarationStatementSyntax v)
        {
            if (v.Keyword.Kind == SyntaxKind.VarKeyword) Report("COPE-PROFILE-0001", "'var' is not supported by Browser TypeScript Profile v1.", v.Keyword);
            if (v.Identifier.Text is "$schema" or "tsonAsset" or "tsonEncode" or "TsonEncodeError")
            {
                string message = v.Identifier.Text switch
                {
                    "$schema" => "'$schema' is reserved compilation-unit metadata and cannot be declared here.",
                    "tsonAsset" => "'tsonAsset' is a compiler intrinsic and cannot be declared or shadowed.",
                    "tsonEncode" => "'tsonEncode' is a compiler intrinsic and cannot be declared or shadowed.",
                    _ => "'TsonEncodeError' is a compiler-owned type and cannot be shadowed.",
                };
                string id = v.Identifier.Text is "tsonEncode" or "TsonEncodeError"
                    ? "COPE-TSON-ENCODE-0001"
                    : "COPE-TSON-ASSET-0001";
                Report(id, message, v.Identifier);
            }
            var type = BindType(v.Type, v.Identifier, "COPE-TYPE-0002", "variable");
            BoundExpression init;
            if (IsTsonAssetCall(v.Initializer))
            {
                bool isSupportedPosition = !ReferenceEquals(_scope, _global)
                    && v.Keyword.Kind == SyntaxKind.ConstKeyword
                    && v.Type is not null;
                init = BindTsonAsset((CallExpressionSyntax)v.Initializer, type, isSupportedPosition);
            }
            else
            {
                init = BindExpression(v.Initializer, type);
            }
            if (!IsAssignable(type, init.Type)) ReportTypeMismatch("COPE-TYPE-0001", type, init.Type, v.Identifier);
            var varSym = new VariableSymbol(v.Identifier.Text, type, v.Keyword.Kind == SyntaxKind.ConstKeyword);
            if (!_scope.TryDeclare(varSym)) Report("COPE-BIND-0002", $"Duplicate declaration '{varSym.Name}'.", v.Identifier);
            return new BoundVariableDeclaration(varSym, init);
        }

        private BoundStatement BindIf(IfStatementSyntax i)
            => new BoundIfStatement(EnsureBoolean(BindExpression(i.Condition), i.IfKeyword), BindStatement(i.ThenStatement), i.ElseStatement is null ? null : BindStatement(i.ElseStatement));

        private BoundStatement BindFor(ForStatementSyntax f)
        {
            BoundStatement? init = f.Initializer switch
            {
                VariableDeclarationStatementSyntax v => BindVariable(v),
                ExpressionSyntax e => new BoundExpressionStatement(BindExpression(e)),
                _ => null
            };
            var c = f.Condition is null ? null : EnsureBoolean(BindExpression(f.Condition), f.ForKeyword);
            var inc = f.Increment is null ? null : BindExpression(f.Increment);
            return new BoundForStatement(init, c, inc, BindStatement(f.Body));
        }

        private BoundStatement BindReturn(ReturnStatementSyntax r)
        {
            var expected = _currentFunction?.ReturnType ?? PrimitiveTypeSymbol.Void;
            if (r.Expression is null)
            {
                if (expected is ResultTypeSymbol result && result.SuccessType == PrimitiveTypeSymbol.Void)
                {
                    return new BoundReturnStatement(new BoundOkExpression(new BoundUnitExpression(), result));
                }

                if (expected != PrimitiveTypeSymbol.Void) Report("COPE-TYPE-0003", $"Type mismatch: expected '{expected.Name}', got 'void'.", r.ReturnKeyword);
                return new BoundReturnStatement(null);
            }
            var expr = BindExpression(r.Expression, expected);
            if (expected == PrimitiveTypeSymbol.Void) Report("COPE-TYPE-0003", "Invalid return expression for void function.", r.ReturnKeyword);
            else if (expected is ResultTypeSymbol result)
            {
                if (IsAssignable(result, expr.Type))
                {
                    return new BoundReturnStatement(expr);
                }

                if (IsAssignable(result.SuccessType, expr.Type))
                {
                    return new BoundReturnStatement(new BoundOkExpression(expr, result));
                }

                Report("COPE-TYPE-0003", $"Type mismatch: expected '{result.Name}', got '{expr.Type.Name}'.", r.ReturnKeyword);
            }
            else if (!IsAssignable(expected, expr.Type)) ReportTypeMismatch("COPE-TYPE-0003", expected, expr.Type, r.ReturnKeyword);
            return new BoundReturnStatement(expr);
        }

        private BoundExpression BindExpression(ExpressionSyntax s, TypeSymbol? contextualType = null)
        {
            var expression = s switch
            {
                LiteralExpressionSyntax l => BindLiteral(l),
                NameExpressionSyntax n => BindName(n),
                ParenthesizedExpressionSyntax p => BindExpression(p.Expression, contextualType),
                PropagateExpressionSyntax p => BindPropagate(p),
                UnwrapExpressionSyntax u => BindUnwrap(u),
                TryExceptExpressionSyntax t => BindTryExcept(t, contextualType),
                UnaryExpressionSyntax u => BindUnary(u),
                BinaryExpressionSyntax b => BindBinary(b),
                AssignmentExpressionSyntax a => BindAssignment(a),
                CallExpressionSyntax c => BindCall(c, contextualType),
                ArrayLiteralExpressionSyntax a => BindArray(a, contextualType),
                ObjectLiteralExpressionSyntax o => BindObject(o, contextualType),
                MemberAccessExpressionSyntax m => BindMember(m),
                IndexExpressionSyntax i => BindIndex(i),
                WithExpressionSyntax w => BindWith(w),
                IfExpressionSyntax i => BindIfExpression(i, contextualType),
                MatchExpressionSyntax m => BindMatch(m, contextualType),
                _ => new BoundErrorExpression()
            };

            return expression;
        }


        private BoundExpression BindIfExpression(IfExpressionSyntax ifExpression, TypeSymbol? contextualType)
        {
            var condition = BindExpression(ifExpression.Condition);
            if (condition.Type != PrimitiveTypeSymbol.Boolean)
                Report("COPE-TYPE-0017", $"If expression condition must be 'boolean', got '{condition.Type.Name}'.", ifExpression.IfKeyword);

            var thenExpression = BindExpression(ifExpression.ThenExpression, contextualType);
            var elseExpression = BindExpression(ifExpression.ElseExpression, contextualType);

            if (!TypeFacts.AreEquivalent(thenExpression.Type, elseExpression.Type))
            {
                Report("COPE-TYPE-0018", $"If expression branch type mismatch: expected '{thenExpression.Type.Name}', got '{elseExpression.Type.Name}'.", ifExpression.ElseKeyword);
                return new BoundErrorExpression();
            }

            return new BoundIfExpression(condition, thenExpression, elseExpression, thenExpression.Type);
        }

        private BoundExpression BindName(NameExpressionSyntax n)
        {
            if (n.IdentifierToken.Text == "tsonAsset")
            {
                Report("COPE-TSON-ASSET-0001", "'tsonAsset' is a compiler intrinsic and cannot be used as a value.", n.IdentifierToken);
                return new BoundErrorExpression();
            }
            if (n.IdentifierToken.Text == "tsonEncode")
            {
                Report("COPE-TSON-ENCODE-0001", "'tsonEncode' is a compiler intrinsic and cannot be used as a value.", n.IdentifierToken);
                return new BoundErrorExpression();
            }
            if (!_scope.TryLookup(n.IdentifierToken.Text, out var symbol) || symbol is null)
            {
                Report("COPE-BIND-0001", $"Undefined name '{n.IdentifierToken.Text}'.", n.IdentifierToken);
                return new BoundErrorExpression();
            }
            return symbol switch
            {
                VariableSymbol v when v.Type is TableTypeSymbol table => new BoundTableReferenceExpression(table),
                VariableSymbol v => new BoundVariableExpression(v),
                ParameterSymbol p => new BoundVariableExpression(new VariableSymbol(p.Name, p.Type, true)),
                _ => new BoundErrorExpression()
            };
        }

        private BoundExpression BindLiteral(LiteralExpressionSyntax l)
        {
            var k = l.LiteralToken.Kind;
            return k switch
            {
                SyntaxKind.NumberToken => new BoundLiteralExpression(l.LiteralToken.Value, PrimitiveTypeSymbol.Number),
                SyntaxKind.StringToken => new BoundLiteralExpression(l.LiteralToken.Value, PrimitiveTypeSymbol.String),
                SyntaxKind.TrueKeyword => new BoundLiteralExpression(true, PrimitiveTypeSymbol.Boolean),
                SyntaxKind.FalseKeyword => new BoundLiteralExpression(false, PrimitiveTypeSymbol.Boolean),
                SyntaxKind.NullKeyword => BindNullLiteral(l),
                _ => new BoundErrorExpression()
            };
        }

        private BoundExpression BindUnary(UnaryExpressionSyntax u)
        {
            var op = u.OperatorToken.Kind; var operand = BindExpression(u.Operand);
            if (op == SyntaxKind.MinusToken && operand.Type == PrimitiveTypeSymbol.Number) return new BoundUnaryExpression(op, operand, PrimitiveTypeSymbol.Number);
            if (op == SyntaxKind.BangToken && operand.Type == PrimitiveTypeSymbol.Boolean) return new BoundUnaryExpression(op, operand, PrimitiveTypeSymbol.Boolean);
            Report("COPE-TYPE-0006", $"Invalid unary operand for '{u.OperatorToken.Text}'.", u.OperatorToken);
            return new BoundErrorExpression();
        }

        private BoundExpression BindBinary(BinaryExpressionSyntax b)
        {
            var l = BindExpression(b.Left); var r = BindExpression(b.Right); var op = b.OperatorToken.Kind;
            if (op is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.BangEqualsEqualsToken)
            {
                if (l.Type is TableTypeSymbol or TableRowTypeSymbol or ColumnTypeSymbol
                    || r.Type is TableTypeSymbol or TableRowTypeSymbol or ColumnTypeSymbol)
                {
                    Report("COPE-TABLE-0017", "Equality is not supported for table values, table rows, or columns.", b.OperatorToken);
                    return new BoundErrorExpression();
                }
            }
            if (op is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken
                && (l.Type is RecordTypeSymbol || r.Type is RecordTypeSymbol))
            {
                Report("COPE-REC-0016", "Record equality is not supported.", b.OperatorToken);
                return new BoundErrorExpression();
            }
            if (l.Type == PrimitiveTypeSymbol.Number && r.Type == PrimitiveTypeSymbol.Number && op is SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken)
                return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Number);
            if (op == SyntaxKind.PlusToken && l.Type == PrimitiveTypeSymbol.String && r.Type == PrimitiveTypeSymbol.String)
                return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.String);
            if (op is SyntaxKind.LessToken or SyntaxKind.LessOrEqualsToken or SyntaxKind.GreaterToken or SyntaxKind.GreaterOrEqualsToken)
            {
                if (l.Type == PrimitiveTypeSymbol.Number && r.Type == PrimitiveTypeSymbol.Number) return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Boolean);
            }
            if (op is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken)
            {
                if (l.Type == PrimitiveTypeSymbol.Boolean && r.Type == PrimitiveTypeSymbol.Boolean) return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Boolean);
            }
            if (op is SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.BangEqualsEqualsToken)
            {
                Report("COPE-PROFILE-0009", $"Strict equality spelling '{b.OperatorToken.Text}' is reserved and not supported. Use typed '{(op == SyntaxKind.EqualsEqualsEqualsToken ? "==" : "!=")}' equality.", b.OperatorToken);
                return new BoundErrorExpression();
            }
            if (op is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken)
            {
                if (l.Type == r.Type && IsPrimitiveEqualityType(l.Type))
                {
                    return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Boolean);
                }
            }
            Report("COPE-TYPE-0007", $"Invalid binary operands for '{b.OperatorToken.Text}'.", b.OperatorToken);
            return new BoundErrorExpression();
        }

        private BoundExpression BindAssignment(AssignmentExpressionSyntax a)
        {
            if (a.Left is IndexExpressionSyntax indexed)
            {
                var receiver = BindExpression(indexed.Target);
                _ = BindExpression(indexed.Index);
                string diagnosticId = receiver.Type is ColumnTypeSymbol ? "COPE-TABLE-0015" : "COPE-TABLE-0016";
                Report(diagnosticId, "Table columns and table rows are immutable.", a.EqualsToken);
                return new BoundErrorExpression();
            }
            if (a.Left is MemberAccessExpressionSyntax member)
            {
                var receiver = BindExpression(member.Target);
                if (receiver.Type is TableTypeSymbol)
                {
                    Report("COPE-TABLE-0014", "Table members are immutable.", member.NameToken);
                    return new BoundErrorExpression();
                }
                if (receiver.Type is TableRowTypeSymbol)
                {
                    Report("COPE-TABLE-0016", "Table row fields are immutable.", member.NameToken);
                    return new BoundErrorExpression();
                }
                if (receiver.Type is RecordTypeSymbol recordType)
                {
                    var field = recordType.Fields.FirstOrDefault(candidate => candidate.Name == member.NameToken.Text);
                    if (field is null)
                    {
                        Report("COPE-REC-0010", $"Record '{recordType.Name}' has no field '{member.NameToken.Text}'.", member.NameToken);
                    }
                    else
                    {
                        Report("COPE-REC-0011", $"Cannot assign to immutable record field '{recordType.Name}.{field.Name}'.", member.NameToken);
                    }
                    return new BoundErrorExpression();
                }
            }
            if (a.Left is not NameExpressionSyntax n)
            {
                Report("COPE-BIND-0007", "Invalid assignment target.", a.EqualsToken);
                return new BoundErrorExpression();
            }
            if (!_scope.TryLookup(n.IdentifierToken.Text, out var symbol) || symbol is null)
            {
                Report("COPE-PROFILE-0004", $"Implicit global assignment is not supported: '{n.IdentifierToken.Text}'.", n.IdentifierToken);
                Report("COPE-BIND-0001", $"Undefined name '{n.IdentifierToken.Text}'.", n.IdentifierToken);
                return new BoundErrorExpression();
            }
            var variable = symbol as VariableSymbol ?? (symbol is ParameterSymbol p ? new VariableSymbol(p.Name, p.Type, true) : null);
            if (variable is null) { Report("COPE-BIND-0007", "Invalid assignment target.", n.IdentifierToken); return new BoundErrorExpression(); }
            if (variable.Type is TableTypeSymbol && variable.IsReadOnly)
            {
                Report("COPE-TABLE-0014", "The authored table singleton is immutable.", n.IdentifierToken);
                return new BoundErrorExpression();
            }
            if (variable.IsReadOnly) Report("COPE-BIND-0003", $"Cannot assign to const variable '{variable.Name}'.", n.IdentifierToken);
            var expr = BindExpression(a.Right, variable.Type);
            if (!IsAssignable(variable.Type, expr.Type)) ReportTypeMismatch("COPE-TYPE-0001", variable.Type, expr.Type, a.EqualsToken);
            return new BoundAssignmentExpression(variable, expr);
        }

        private BoundExpression BindCall(CallExpressionSyntax c, TypeSymbol? contextualType)
        {
            if (c.Target is NameExpressionSyntax tsonEncodeName
                && tsonEncodeName.IdentifierToken.Text == "tsonEncode")
            {
                return BindTsonEncode(c, tsonEncodeName);
            }
            if (IsTsonAssetCall(c))
            {
                Report(
                    "COPE-TSON-ASSET-0001",
                    "'tsonAsset' is supported only as the initializer of an explicitly typed local const.",
                    ((NameExpressionSyntax)c.Target).IdentifierToken);
                return new BoundErrorExpression();
            }
            if (c.Target is NameExpressionSyntax n && n.IdentifierToken.Text == "eval")
                Report("COPE-PROFILE-0003", "Dynamic evaluation is not supported by Browser TypeScript Profile v1.", n.IdentifierToken);

            if (c.Target is NameExpressionSyntax intrinsicName && (intrinsicName.IdentifierToken.Text is "ok" or "err"))
            {
                if (contextualType is not ResultTypeSymbol resultType)
                {
                    Report("COPE-RESULT-0001", $"Result constructor '{intrinsicName.IdentifierToken.Text}' requires an expected Result type.", intrinsicName.IdentifierToken);
                    return new BoundErrorExpression();
                }

                return BindResultConstructor(c, intrinsicName, resultType);
            }

            if (c.Target is MemberAccessExpressionSyntax m && m.Target is NameExpressionSyntax enumName)
            {
                return BindEnumConstructorCall(c, m, enumName);
            }

            if (c.Target is not NameExpressionSyntax name || !_scope.TryLookup(name.IdentifierToken.Text, out var s) || s is null)
            { Report("COPE-BIND-0001", "Undefined function name.", c.OpenParenToken); return new BoundErrorExpression(); }
            if (s is not FunctionSymbol fn) { Report("COPE-BIND-0006", $"Cannot call non-function '{s.Name}'.", c.OpenParenToken); return new BoundErrorExpression(); }
            if (c.Arguments.Count != fn.Parameters.Count) Report("COPE-TYPE-0004", $"Argument count mismatch: expected {fn.Parameters.Count}, got {c.Arguments.Count}.", c.OpenParenToken);
            var args = c.Arguments.Select((a, index) => BindExpression(a, index < fn.Parameters.Count ? fn.Parameters[index].Type : null)).ToArray();
            for (var i = 0; i < Math.Min(args.Length, fn.Parameters.Count); i++)
                if (!IsAssignable(fn.Parameters[i].Type, args[i].Type)) ReportTypeMismatch("COPE-TYPE-0005", fn.Parameters[i].Type, args[i].Type, c.Arguments[i] is LiteralExpressionSyntax le ? le.LiteralToken : c.OpenParenToken);
            return new BoundCallExpression(fn, args);
        }

        private BoundExpression BindTsonEncode(CallExpressionSyntax call, NameExpressionSyntax intrinsicName)
        {
            if (call.Arguments.Count != 1)
            {
                foreach (ExpressionSyntax argument in call.Arguments)
                {
                    _ = BindExpression(argument);
                }
                Report("COPE-TSON-ENCODE-0001", "'tsonEncode' requires exactly one argument.", call.OpenParenToken);
                return new BoundErrorExpression();
            }

            BoundExpression operand = BindExpression(call.Arguments[0]);
            if (operand.Type is not RecordTypeSymbol and not EnumTypeSymbol)
            {
                Report(
                    "COPE-TSON-ENCODE-0001",
                    $"'tsonEncode' requires one nominal record or payload enum root, not '{operand.Type.Name}'.",
                    intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }

            if (_schemaIdentity is null)
            {
                Report(
                    "COPE-TSON-ENCODE-0002",
                    "A compilation unit using 'tsonEncode' requires one valid top-level '$schema' declaration.",
                    intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }

            if (!TryGetOrCreateTsonEncodingPlan(operand.Type, intrinsicName.IdentifierToken, out BoundTsonEncodingPlan? plan))
            {
                return new BoundErrorExpression();
            }

            if (_tsonEncodeErrorType is null)
            {
                throw new InvalidOperationException("Compiler-owned TsonEncodeError was not predeclared.");
            }

            _usesTsonEncode = true;
            var resultType = new ResultTypeSymbol(PrimitiveTypeSymbol.String, _tsonEncodeErrorType);
            return new BoundTsonEncodeExpression(operand, plan!, resultType);
        }

        private bool TryGetOrCreateTsonEncodingPlan(
            TypeSymbol rootType,
            SyntaxToken anchor,
            out BoundTsonEncodingPlan? plan)
        {
            if (_tsonEncodingPlans.TryGetValue(rootType, out plan))
            {
                return true;
            }

            string schemaIdentity = _schemaIdentity
                ?? throw new InvalidOperationException("TSON encoding plan creation requires validated schema metadata.");

            var reachable = new HashSet<TypeSymbol>();
            var visiting = new HashSet<TypeSymbol>();
            bool valid = VisitTsonType(rootType, rootType.Name, anchor, reachable, visiting);
            if (!valid)
            {
                plan = null;
                return false;
            }

            TypeSymbol[] definitions = reachable
                .Where(type => type is RecordTypeSymbol or EnumTypeSymbol)
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (TypeSymbol definition in definitions)
            {
                string? identity = GetStableIdentity(definition);
                if (identity is null || !identity.StartsWith(schemaIdentity + "#", StringComparison.Ordinal))
                {
                    Report("COPE-TSON-ENCODE-0005", $"Reachable type '{definition.Name}' does not belong to schema '{schemaIdentity}'.", anchor);
                    valid = false;
                    continue;
                }
                if (!identities.Add(identity))
                {
                    Report("COPE-TSON-ENCODE-0004", $"Stable TSON identity collision at '{identity}'.", anchor);
                    valid = false;
                }
            }
            if (!valid)
            {
                plan = null;
                return false;
            }

            plan = new BoundTsonEncodingPlan(
                $"tson{_tsonEncodingPlans.Count}",
                schemaIdentity,
                rootType,
                definitions);
            _tsonEncodingPlans.Add(rootType, plan);
            return true;
        }

        private bool VisitTsonType(
            TypeSymbol type,
            string path,
            SyntaxToken anchor,
            HashSet<TypeSymbol> reachable,
            HashSet<TypeSymbol> visiting)
        {
            if (type == PrimitiveTypeSymbol.Boolean
                || type == PrimitiveTypeSymbol.Number
                || type == PrimitiveTypeSymbol.String)
            {
                return true;
            }
            if (type is ArrayTypeSymbol array)
            {
                return VisitTsonType(array.ElementType, path + "[]", anchor, reachable, visiting);
            }
            if (type is not RecordTypeSymbol and not EnumTypeSymbol)
            {
                Report("COPE-TSON-ENCODE-0003", $"TSON encoding does not support reachable type '{type.Name}' at '{path}'.", anchor);
                return false;
            }
            if (reachable.Contains(type))
            {
                return true;
            }
            if (!visiting.Add(type))
            {
                Report("COPE-TSON-ENCODE-0004", $"TSON encoding schema cycle involves '{type.Name}'.", anchor);
                return false;
            }

            bool valid = true;
            IEnumerable<(string Name, TypeSymbol Type)> children = type switch
            {
                RecordTypeSymbol record => record.Fields.Select(field => (field.Name, field.Type)),
                EnumTypeSymbol @enum => @enum.Cases.SelectMany(@case => @case.PayloadFields.Select(field => ($"{@case.Name}.{field.Name}", field.Type))),
                _ => [],
            };
            foreach ((string name, TypeSymbol childType) in children)
            {
                valid &= VisitTsonType(childType, $"{path}.{name}", anchor, reachable, visiting);
            }
            visiting.Remove(type);
            reachable.Add(type);
            return valid;
        }

        private static string? GetStableIdentity(TypeSymbol type)
            => type switch
            {
                RecordTypeSymbol record => record.StableIdentity,
                EnumTypeSymbol @enum => @enum.StableIdentity,
                _ => null,
            };

        private static bool IsTsonAssetCall(ExpressionSyntax expression)
        {
            return expression is CallExpressionSyntax call
                && call.Target is NameExpressionSyntax name
                && name.IdentifierToken.Text == "tsonAsset";
        }

        private BoundExpression BindTsonAsset(
            CallExpressionSyntax call,
            TypeSymbol expectedType,
            bool isSupportedPosition)
        {
            var intrinsicName = (NameExpressionSyntax)call.Target;
            if (!isSupportedPosition)
            {
                Report(
                    "COPE-TSON-ASSET-0001",
                    "'tsonAsset' requires an explicitly typed local const initializer.",
                    intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }

            if (expectedType is not RecordTypeSymbol && expectedType is not EnumTypeSymbol)
            {
                Report(
                    "COPE-TSON-ASSET-0001",
                    $"'tsonAsset' expected type must be one nominal record or payload enum, not '{expectedType.Name}'.",
                    intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }

            if (call.Arguments.Count != 1
                || call.Arguments[0] is not LiteralExpressionSyntax pathLiteral
                || pathLiteral.LiteralToken.Kind != SyntaxKind.StringToken)
            {
                Report(
                    "COPE-TSON-ASSET-0001",
                    "'tsonAsset' requires exactly one string-literal relative path.",
                    call.OpenParenToken);
                return new BoundErrorExpression();
            }

            string? expectedIdentity = expectedType switch
            {
                RecordTypeSymbol record => record.StableIdentity,
                EnumTypeSymbol @enum => @enum.StableIdentity,
                _ => null,
            };
            if (_schemaIdentity is null || expectedIdentity is null)
            {
                Report(
                    "COPE-TSON-ASSET-0004",
                    "A compilation unit using 'tsonAsset' requires one valid top-level '$schema' declaration.",
                    intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }

            if (_assetResolver is null)
            {
                Report(
                    "COPE-TSON-ASSET-0002",
                    "This compilation has no source path, compilation root, and asset source for resolving TSON assets.",
                    pathLiteral.LiteralToken);
                return new BoundErrorExpression();
            }

            string authoredPath = (string)pathLiteral.LiteralToken.Value!;
            if (!_assetResolver.TryResolve(authoredPath, out var asset, out string? resolutionError))
            {
                Report(
                    "COPE-TSON-ASSET-0002",
                    resolutionError ?? "The TSON asset could not be resolved.",
                    pathLiteral.LiteralToken);
                return new BoundErrorExpression();
            }

            TsonDocumentProfile profile = asset!.NormalizedPath.EndsWith(".obj.ts", StringComparison.OrdinalIgnoreCase)
                ? TsonDocumentProfile.ObjectTypeScript
                : TsonDocumentProfile.CanonicalTson;
            TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(asset.SourceText, profile);
            if (!read.Success)
            {
                foreach (var diagnostic in read.SyntaxDiagnostics)
                {
                    _diagnostics.Report(
                        diagnostic.Id,
                        $"TSON asset '{asset.NormalizedPath}': {diagnostic.Message}",
                        diagnostic.Position,
                        Math.Max(1, diagnostic.Length),
                        asset.NormalizedPath);
                }

                foreach (var diagnostic in read.Diagnostics)
                {
                    _diagnostics.Report(
                        diagnostic.Code,
                        $"TSON asset '{asset.NormalizedPath}': {diagnostic.Message}",
                        diagnostic.Position,
                        diagnostic.Length,
                        asset.NormalizedPath);
                }

                return new BoundErrorExpression();
            }

            if (read.Document!.Root is not TsonRecord && read.Document.Root is not TsonEnum)
            {
                ReportAssetUnsupported(
                    asset.NormalizedPath,
                    $"M1b requires one nominal record or payload-enum root; actual root is '{DescribeTsonValue(read.Document.Root)}'.",
                    pathLiteral.LiteralToken);
                return new BoundErrorExpression();
            }

            if (!ValidateTsonSchema(read.Document.Catalog, expectedType, asset.NormalizedPath, pathLiteral.LiteralToken))
            {
                return new BoundErrorExpression();
            }

            if (!TryLowerTsonValue(
                    read.Document.Root,
                    expectedType,
                    asset.NormalizedPath,
                    pathLiteral.LiteralToken,
                    out BoundExpression? expression))
            {
                return new BoundErrorExpression();
            }

            return expression!;
        }

        private bool ValidateTsonSchema(
            TsonCatalog catalog,
            TypeSymbol expectedType,
            string assetPath,
            SyntaxToken callSite)
        {
            var visited = new HashSet<TypeSymbol>();
            if (ValidateTsonSchemaType(catalog, expectedType, visited, out string? mismatch))
            {
                return true;
            }

            ReportAssetMismatch(assetPath, mismatch ?? "The asset schema does not match the compiled declaration graph.", callSite);
            return false;
        }

        private static bool ValidateTsonSchemaType(
            TsonCatalog catalog,
            TypeSymbol type,
            HashSet<TypeSymbol> visited,
            out string? mismatch)
        {
            mismatch = null;
            if (!visited.Add(type))
            {
                return true;
            }

            if (type is RecordTypeSymbol record)
            {
                if (!catalog.TryGetDefinition(record.Name, out TsonNominalDefinition? nominal)
                    || nominal is not TsonRecordDefinition definition
                    || definition.Identity != record.StableIdentity)
                {
                    mismatch = $"Expected record schema '{record.StableIdentity}' was not declared exactly by the asset.";
                    return false;
                }

                if (definition.Fields.Count != record.Fields.Count)
                {
                    mismatch = $"Record schema '{record.StableIdentity}' has a different field count.";
                    return false;
                }

                for (int index = 0; index < record.Fields.Count; index++)
                {
                    RecordFieldSymbol field = record.Fields[index];
                    TsonFieldDefinition authored = definition.Fields[index];
                    string identity = $"{record.StableIdentity}.{field.Name}";
                    if (authored.Name != field.Name
                        || authored.Identity != identity
                        || !TsonTypeMatches(authored.Type, field.Type))
                    {
                        mismatch = $"Record field schema mismatch: expected '{identity}'.";
                        return false;
                    }

                    if (!ValidateTsonSchemaType(catalog, field.Type, visited, out mismatch))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (type is EnumTypeSymbol enumType)
            {
                if (!catalog.TryGetDefinition(enumType.Name, out TsonNominalDefinition? nominal)
                    || nominal is not TsonEnumDefinition definition
                    || definition.Identity != enumType.StableIdentity)
                {
                    mismatch = $"Expected enum schema '{enumType.StableIdentity}' was not declared exactly by the asset.";
                    return false;
                }

                if (definition.Cases.Count != enumType.Cases.Count)
                {
                    mismatch = $"Enum schema '{enumType.StableIdentity}' has a different case count.";
                    return false;
                }

                for (int caseIndex = 0; caseIndex < enumType.Cases.Count; caseIndex++)
                {
                    EnumCaseSymbol enumCase = enumType.Cases[caseIndex];
                    TsonEnumCaseDefinition authoredCase = definition.Cases[caseIndex];
                    string caseIdentity = $"{enumType.StableIdentity}.{enumCase.Name}";
                    if (authoredCase.Name != enumCase.Name
                        || authoredCase.Identity != caseIdentity
                        || authoredCase.Payloads.Count != enumCase.PayloadFields.Count)
                    {
                        mismatch = $"Enum case schema mismatch: expected '{caseIdentity}'.";
                        return false;
                    }

                    for (int payloadIndex = 0; payloadIndex < enumCase.PayloadFields.Count; payloadIndex++)
                    {
                        EnumPayloadFieldSymbol payload = enumCase.PayloadFields[payloadIndex];
                        TsonFieldDefinition authoredPayload = authoredCase.Payloads[payloadIndex];
                        string payloadIdentity = $"{caseIdentity}.{payload.Name}";
                        if (authoredPayload.Name != payload.Name
                            || authoredPayload.Identity != payloadIdentity
                            || !TsonTypeMatches(authoredPayload.Type, payload.Type))
                        {
                            mismatch = $"Enum payload schema mismatch: expected '{payloadIdentity}'.";
                            return false;
                        }

                        if (!ValidateTsonSchemaType(catalog, payload.Type, visited, out mismatch))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            if (type is ArrayTypeSymbol array)
            {
                if (!IsSupportedTsonArrayElementType(array.ElementType))
                {
                    mismatch = $"Array element type '{array.ElementType.Name}' is unsupported in TSON assets.";
                    return false;
                }

                return ValidateTsonSchemaType(catalog, array.ElementType, visited, out mismatch);
            }

            if (type is PrimitiveTypeSymbol primitive
                && primitive != PrimitiveTypeSymbol.Boolean
                && primitive != PrimitiveTypeSymbol.Number
                && primitive != PrimitiveTypeSymbol.String)
            {
                mismatch = $"Type '{type.Name}' is unsupported in TSON assets.";
                return false;
            }

            if (type is not PrimitiveTypeSymbol)
            {
                mismatch = $"Type '{type.Name}' is unsupported in TSON assets.";
                return false;
            }

            return true;
        }

        private static bool TsonTypeMatches(TsonTypeReference authored, TypeSymbol compiled)
        {
            var pending = new Stack<(TsonTypeReference Authored, TypeSymbol Compiled)>();
            pending.Push((authored, compiled));
            while (pending.Count > 0)
            {
                var pair = pending.Pop();
                switch (pair.Authored.Kind, pair.Compiled)
                {
                    case (TsonTypeKind.Boolean, PrimitiveTypeSymbol boolean)
                        when boolean == PrimitiveTypeSymbol.Boolean:
                    case (TsonTypeKind.Number, PrimitiveTypeSymbol number)
                        when number == PrimitiveTypeSymbol.Number:
                    case (TsonTypeKind.String, PrimitiveTypeSymbol text)
                        when text == PrimitiveTypeSymbol.String:
                        break;
                    case (TsonTypeKind.Record, RecordTypeSymbol record)
                        when pair.Authored.NominalName == record.Name:
                    case (TsonTypeKind.Enum, EnumTypeSymbol enumType)
                        when pair.Authored.NominalName == enumType.Name:
                        break;
                    case (TsonTypeKind.Array, ArrayTypeSymbol array)
                        when pair.Authored.ElementType is not null:
                        pending.Push((pair.Authored.ElementType, array.ElementType));
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool IsSupportedTsonArrayElementType(TypeSymbol type)
        {
            return type switch
            {
                PrimitiveTypeSymbol primitive => primitive == PrimitiveTypeSymbol.Boolean
                    || primitive == PrimitiveTypeSymbol.Number
                    || primitive == PrimitiveTypeSymbol.String,
                RecordTypeSymbol => true,
                EnumTypeSymbol => true,
                ArrayTypeSymbol nested => IsSupportedTsonArrayElementType(nested.ElementType),
                _ => false,
            };
        }

        private bool TryLowerTsonValue(
            TsonValue value,
            TypeSymbol expectedType,
            string assetPath,
            SyntaxToken callSite,
            out BoundExpression? expression)
        {
            expression = null;
            switch (expectedType)
            {
                case PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.Boolean && value is TsonBoolean boolean:
                    expression = new BoundLiteralExpression(boolean.Value, primitive);
                    return true;
                case PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.Number && value is TsonNumber number:
                    expression = new BoundLiteralExpression(number.Value, primitive);
                    return true;
                case PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.String && value is TsonString text:
                    expression = new BoundLiteralExpression(text.Value, primitive);
                    return true;
                case RecordTypeSymbol record:
                    return TryLowerTsonRecord(value, record, assetPath, callSite, out expression);
                case EnumTypeSymbol @enum:
                    return TryLowerTsonEnum(value, @enum, assetPath, callSite, out expression);
                case ArrayTypeSymbol array:
                    return TryLowerTsonArray(value, array, assetPath, callSite, out expression);
                case PrimitiveTypeSymbol primitive
                    when primitive == PrimitiveTypeSymbol.Boolean
                        || primitive == PrimitiveTypeSymbol.Number
                        || primitive == PrimitiveTypeSymbol.String:
                    ReportAssetMismatch(
                        assetPath,
                        $"TSON value type mismatch: expected '{expectedType.Name}', actual '{DescribeTsonValue(value)}'.",
                        callSite);
                    return false;
                default:
                    ReportAssetUnsupported(
                        assetPath,
                        $"TSON asset value type is unsupported in M1b; expected '{expectedType.Name}'.",
                        callSite);
                    return false;
            }
        }

        private bool TryLowerTsonArray(
            TsonValue value,
            ArrayTypeSymbol arrayType,
            string assetPath,
            SyntaxToken callSite,
            out BoundExpression? expression)
        {
            expression = null;
            if (value is not TsonArray tsonArray)
            {
                ReportAssetMismatch(
                    assetPath,
                    $"TSON value type mismatch: expected '{arrayType.Name}', actual '{DescribeTsonValue(value)}'.",
                    callSite);
                return false;
            }

            if (!TsonTypeMatches(tsonArray.Schema.ElementType, arrayType.ElementType))
            {
                ReportAssetMismatch(
                    assetPath,
                    $"TSON array element schema does not match expected '{arrayType.ElementType.Name}'.",
                    callSite);
                return false;
            }

            var elements = new List<BoundExpression>(tsonArray.Elements.Count);
            for (var index = 0; index < tsonArray.Elements.Count; index++)
            {
                if (!TryLowerTsonValue(
                        tsonArray.Elements[index],
                        arrayType.ElementType,
                        assetPath,
                        callSite,
                        out BoundExpression? element))
                {
                    return false;
                }

                elements.Add(element!);
            }

            expression = new BoundArrayExpression(elements, arrayType);
            return true;
        }

        private bool TryLowerTsonRecord(
            TsonValue value,
            RecordTypeSymbol record,
            string assetPath,
            SyntaxToken callSite,
            out BoundExpression? expression)
        {
            expression = null;
            if (value is not TsonRecord tsonRecord)
            {
                if (value is TsonObject)
                {
                    ReportAssetUnsupported(assetPath, "A structural TSON object cannot become a compiled runtime record or root.", callSite);
                    return false;
                }

                ReportAssetMismatch(assetPath, $"Expected nominal record identity '{record.StableIdentity}', but the asset root/value is '{DescribeTsonValue(value)}'.", callSite);
                return false;
            }

            if (!string.Equals(record.StableIdentity, tsonRecord.Identity, StringComparison.Ordinal))
            {
                ReportAssetMismatch(assetPath, $"Stable identity mismatch: expected '{record.StableIdentity}', actual '{tsonRecord.Identity}'.", callSite);
                return false;
            }

            if (record.Fields.Count != tsonRecord.Fields.Count)
            {
                ReportAssetMismatch(assetPath, $"Record '{record.StableIdentity}' field count does not match the compiled declaration.", callSite);
                return false;
            }

            var initializers = new List<BoundRecordFieldInitializer>();
            for (int index = 0; index < record.Fields.Count; index++)
            {
                RecordFieldSymbol field = record.Fields[index];
                TsonField tsonField = tsonRecord.Fields[index];
                string fieldIdentity = $"{record.StableIdentity}.{field.Name}";
                if (field.Name != tsonField.Name || tsonField.Identity != fieldIdentity)
                {
                    ReportAssetMismatch(assetPath, $"Record field mismatch: expected '{fieldIdentity}', actual '{tsonField.Identity ?? tsonField.Name}'.", callSite);
                    return false;
                }

                if (!TryLowerTsonValue(tsonField.Value, field.Type, assetPath, callSite, out BoundExpression? child))
                {
                    return false;
                }

                initializers.Add(new BoundRecordFieldInitializer(field, child!));
            }

            expression = new BoundRecordConstructionExpression(record, initializers);
            return true;
        }

        private bool TryLowerTsonEnum(
            TsonValue value,
            EnumTypeSymbol enumType,
            string assetPath,
            SyntaxToken callSite,
            out BoundExpression? expression)
        {
            expression = null;
            if (value is not TsonEnum tsonEnum)
            {
                if (value is TsonObject)
                {
                    ReportAssetUnsupported(assetPath, "A structural TSON object cannot become a compiled runtime enum or root.", callSite);
                    return false;
                }

                ReportAssetMismatch(assetPath, $"Expected nominal enum identity '{enumType.StableIdentity}', but the asset root/value is '{DescribeTsonValue(value)}'.", callSite);
                return false;
            }

            if (!string.Equals(enumType.StableIdentity, tsonEnum.EnumIdentity, StringComparison.Ordinal))
            {
                ReportAssetMismatch(assetPath, $"Stable identity mismatch: expected '{enumType.StableIdentity}', actual '{tsonEnum.EnumIdentity}'.", callSite);
                return false;
            }

            EnumCaseSymbol? enumCase = enumType.Cases.FirstOrDefault(candidate => candidate.Name == tsonEnum.CaseName);
            string expectedCaseIdentity = $"{enumType.StableIdentity}.{tsonEnum.CaseName}";
            if (enumCase is null || tsonEnum.CaseIdentity != expectedCaseIdentity)
            {
                ReportAssetMismatch(assetPath, $"Enum case mismatch for '{enumType.StableIdentity}': actual '{tsonEnum.CaseIdentity}'.", callSite);
                return false;
            }

            if (enumCase.PayloadFields.Count != tsonEnum.Payloads.Count)
            {
                ReportAssetMismatch(assetPath, $"Enum case '{expectedCaseIdentity}' payload count does not match the compiled declaration.", callSite);
                return false;
            }

            var arguments = new List<BoundExpression>();
            for (int index = 0; index < enumCase.PayloadFields.Count; index++)
            {
                EnumPayloadFieldSymbol payload = enumCase.PayloadFields[index];
                TsonField tsonPayload = tsonEnum.Payloads[index];
                string payloadIdentity = $"{expectedCaseIdentity}.{payload.Name}";
                if (payload.Name != tsonPayload.Name || tsonPayload.Identity != payloadIdentity)
                {
                    ReportAssetMismatch(assetPath, $"Enum payload mismatch: expected '{payloadIdentity}', actual '{tsonPayload.Identity ?? tsonPayload.Name}'.", callSite);
                    return false;
                }

                if (!TryLowerTsonValue(tsonPayload.Value, payload.Type, assetPath, callSite, out BoundExpression? child))
                {
                    return false;
                }

                arguments.Add(child!);
            }

            expression = new BoundEnumValueExpression(enumCase, arguments);
            return true;
        }

        private static string DescribeTsonValue(TsonValue value)
        {
            return value switch
            {
                TsonRecord record => record.Identity,
                TsonEnum @enum => @enum.EnumIdentity,
                TsonObject => "structural object",
                TsonBoolean => "boolean",
                TsonNumber => "number",
                TsonString => "string",
                TsonArray array => array.Schema.ElementType.Kind == TsonTypeKind.Array
                    ? "array"
                    : DisplayTsonArrayType(array.Schema.ElementType) + "[]",
                _ => "unsupported value",
            };
        }

        private static string DisplayTsonArrayType(TsonTypeReference type)
        {
            return type.Kind == TsonTypeKind.Array
                ? DisplayTsonArrayType(type.ElementType!) + "[]"
                : type.NominalName ?? type.Kind.ToString().ToLowerInvariant();
        }

        private void ReportAssetMismatch(string assetPath, string message, SyntaxToken callSite)
        {
            _diagnostics.Report(
                "COPE-TSON-ASSET-0003",
                $"TSON asset '{assetPath}': {message}",
                callSite.Position,
                Math.Max(1, callSite.Text.Length));
        }

        private void ReportAssetUnsupported(string assetPath, string message, SyntaxToken callSite)
        {
            _diagnostics.Report(
                "COPE-TSON-ASSET-0005",
                $"TSON asset '{assetPath}': {message}",
                callSite.Position,
                Math.Max(1, callSite.Text.Length));
        }

        private BoundExpression BindTryExcept(TryExceptExpressionSyntax tryExcept, TypeSymbol? contextualType)
        {
            var handlerId = new BoundHandlerId(_nextHandlerId++);
            var target = new PropagationTargetContext(handlerId);
            var previousScope = _scope;

            _scope = new Scope(previousScope);
            _propagationTargets.Add(target);
            var protectedBlock = BindValueBlock(tryExcept.Protected, contextualType);
            _propagationTargets.RemoveAt(_propagationTargets.Count - 1);
            _scope = previousScope;

            if (!target.WasTargeted)
            {
                Report("COPE-TRY-0004", "A try protected block must contain at least one '?' targeting its own except handler.", tryExcept.TryKeyword);
            }

            var handledErrorType = target.ErrorType ?? PrimitiveTypeSymbol.Error;
            var handlerBinding = new VariableSymbol(tryExcept.BindingIdentifier.Text, handledErrorType, true);
            _scope = new Scope(previousScope);
            if (!IsUsableHandlerBinding(tryExcept.BindingIdentifier) || !_scope.TryDeclare(handlerBinding))
            {
                Report("COPE-TRY-0006", "The except binding must form one read-only inferred error binding.", tryExcept.BindingIdentifier);
            }

            var handlerBlock = BindValueBlock(tryExcept.Handler, contextualType ?? protectedBlock.Type);
            _scope = previousScope;

            if (protectedBlock.Type != PrimitiveTypeSymbol.Error
                && handlerBlock.Type != PrimitiveTypeSymbol.Error
                && !TypeFacts.AreEquivalent(protectedBlock.Type, handlerBlock.Type))
            {
                Report("COPE-TRY-0002", $"Try protected value type '{protectedBlock.Type.Name}' does not match handler value type '{handlerBlock.Type.Name}'.", tryExcept.ExceptKeyword);
            }

            var resultType = protectedBlock.Type != PrimitiveTypeSymbol.Error
                ? protectedBlock.Type
                : handlerBlock.Type;
            return new BoundTryExceptExpression(handlerId, protectedBlock, handlerBinding, handledErrorType, handlerBlock, resultType);
        }

        private BoundValueBlock BindValueBlock(TryValueBlockSyntax block, TypeSymbol? contextualType)
        {
            var prefix = block.PrefixStatements.Select(BindStatement).ToArray();
            var value = BindExpression(block.ValueExpression, contextualType);
            return new BoundValueBlock(prefix, value);
        }

        private static bool IsUsableHandlerBinding(SyntaxToken binding)
            => binding.Kind == SyntaxKind.IdentifierToken && !string.IsNullOrWhiteSpace(binding.Text);

        private BoundExpression BindResultConstructor(CallExpressionSyntax call, NameExpressionSyntax name, ResultTypeSymbol resultType)
        {
            if (call.Arguments.Count != 1)
            {
                Report("COPE-RESULT-0002", $"Result constructor '{name.IdentifierToken.Text}' expects exactly one payload.", call.OpenParenToken);
                return new BoundErrorExpression();
            }

            var expectedPayloadType = name.IdentifierToken.Text == "ok" ? resultType.SuccessType : resultType.ErrorType;
            var payload = BindExpression(call.Arguments[0], expectedPayloadType);
            if (!IsAssignable(expectedPayloadType, payload.Type))
            {
                Report("COPE-RESULT-0003", $"Result constructor '{name.IdentifierToken.Text}' expected payload type '{expectedPayloadType.Name}', got '{payload.Type.Name}'.", call.OpenParenToken);
            }

            return name.IdentifierToken.Text == "ok"
                ? new BoundOkExpression(payload, resultType)
                : new BoundErrExpression(payload, resultType);
        }

        private BoundExpression BindArray(ArrayLiteralExpressionSyntax a, TypeSymbol? contextual)
        {
            var elems = a.Elements.Select(e => BindExpression(e, contextual is ArrayTypeSymbol context ? context.ElementType : null)).ToArray();
            if (contextual is ArrayTypeSymbol ctx)
            {
                foreach (var e in elems) if (!IsAssignable(ctx.ElementType, e.Type)) Report("COPE-TYPE-0009", $"Type mismatch: expected '{ctx.ElementType.Name}', got '{e.Type.Name}'.", a.OpenBracketToken);
                return new BoundArrayExpression(elems, contextual);
            }
            if (elems.Length == 0) { Report("COPE-TYPE-0010", "Empty array requires contextual type.", a.OpenBracketToken); return new BoundErrorExpression(); }
            var first = elems[0].Type;
            if (elems.Any(x => !TypeFacts.AreEquivalent(x.Type, first))) { Report("COPE-TYPE-0009", "Array element type mismatch.", a.OpenBracketToken); return new BoundErrorExpression(); }
            return new BoundArrayExpression(elems, new ArrayTypeSymbol(first));
        }

        private BoundExpression BindObject(ObjectLiteralExpressionSyntax literal, TypeSymbol? contextualType)
        {
            if (contextualType is TableRowTypeSymbol)
            {
                Report("COPE-TABLE-0016", "Table-owned rows cannot be constructed from object literals.", literal.OpenBraceToken);
                return new BoundErrorExpression();
            }
            if (contextualType is not RecordTypeSymbol recordType)
            {
                Report("COPE-REC-0005", "A record literal requires one expected nominal record type.", literal.OpenBraceToken);
                return new BoundErrorExpression();
            }

            var initializers = new List<BoundRecordFieldInitializer>();
            var seen = new HashSet<RecordFieldId>();
            foreach (var property in literal.Properties)
            {
                var field = recordType.Fields.FirstOrDefault(candidate => candidate.Name == property.NameToken.Text);
                if (field is null || property.NameToken.Kind != SyntaxKind.IdentifierToken)
                {
                    Report("COPE-REC-0007", $"Record '{recordType.Name}' has no field '{property.NameToken.Text}'.", property.NameToken);
                    BindExpression(property.ValueExpression);
                    continue;
                }
                if (!seen.Add(field.Id))
                {
                    Report("COPE-REC-0008", $"Field '{field.Name}' is initialized more than once.", property.NameToken);
                }

                var value = BindExpression(property.ValueExpression, field.Type);
                if (!IsAssignable(field.Type, value.Type))
                {
                    Report("COPE-REC-0009", $"Initializer for '{recordType.Name}.{field.Name}' expected '{field.Type.Name}', got '{value.Type.Name}'.", property.NameToken);
                }
                initializers.Add(new BoundRecordFieldInitializer(field, value));
            }

            var missing = recordType.Fields.Where(field => !seen.Contains(field.Id)).Select(field => field.Name).ToArray();
            if (missing.Length > 0)
            {
                Report("COPE-REC-0006", $"Record '{recordType.Name}' is missing fields: {string.Join(", ", missing)}.", literal.OpenBraceToken);
            }
            return new BoundRecordConstructionExpression(recordType, initializers);
        }
        private BoundExpression BindMatch(MatchExpressionSyntax match, TypeSymbol? contextualType)
        {
            var scrutinee = BindExpression(match.Expression);
            if (scrutinee.Type is ResultTypeSymbol resultType)
            {
                return BindResultMatch(match, scrutinee, resultType, contextualType);
            }

            if (scrutinee.Type is not EnumTypeSymbol enumType)
            {
                Report("COPE-MATCH-0001", "Match expression requires an enum value.", match.MatchKeyword);
                return new BoundErrorExpression();
            }

            var boundArms = new List<BoundMatchArm>();
            var seenCases = new HashSet<string>(StringComparer.Ordinal);
            TypeSymbol? expectedArmType = null;

            foreach (var arm in match.Arms)
            {
                var caseName = arm.Pattern.CaseIdentifier.Text;
                var enumCase = enumType.Cases.FirstOrDefault(c => c.Name == caseName);
                if (enumCase is null)
                {
                    Report("COPE-MATCH-0002", $"Enum '{enumType.Name}' has no case '{caseName}'.", arm.Pattern.CaseIdentifier);
                    continue;
                }

                if (!seenCases.Add(caseName))
                {
                    Report("COPE-MATCH-0003", $"Duplicate match arm for case '{caseName}'.", arm.Pattern.CaseIdentifier);
                }

                var payloadCount = arm.Pattern.PayloadIdentifiers.Count;
                if (payloadCount != enumCase.PayloadFields.Count)
                {
                    Report("COPE-MATCH-0005", $"Match arm for case '{caseName}' expects {enumCase.PayloadFields.Count} payload values, got {payloadCount}.", arm.Pattern.CaseIdentifier);
                }

                var prevScope = _scope;
                _scope = new Scope(prevScope);
                var payloadVars = new List<VariableSymbol>();
                var seenPayload = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < Math.Min(payloadCount, enumCase.PayloadFields.Count); i++)
                {
                    var payloadIdentifier = arm.Pattern.PayloadIdentifiers[i];
                    if (!seenPayload.Add(payloadIdentifier.Text))
                    {
                        Report("COPE-MATCH-0006", $"Duplicate payload variable '{payloadIdentifier.Text}' in match arm for case '{caseName}'.", payloadIdentifier);
                        continue;
                    }

                    var symbol = new VariableSymbol(payloadIdentifier.Text, enumCase.PayloadFields[i].Type, true);
                    _scope.TryDeclare(symbol);
                    payloadVars.Add(symbol);
                }

                var armExpression = BindExpression(arm.Expression, contextualType);
                _scope = prevScope;

                if (expectedArmType is null && armExpression.Type != PrimitiveTypeSymbol.Error)
                {
                    expectedArmType = armExpression.Type;
                }
                else if (expectedArmType is not null && armExpression.Type != PrimitiveTypeSymbol.Error && !IsAssignable(expectedArmType, armExpression.Type))
                {
                    Report("COPE-MATCH-0007", $"Match arm type mismatch: expected '{expectedArmType.Name}', got '{armExpression.Type.Name}'.", arm.ArrowToken);
                }

                boundArms.Add(new BoundMatchArm(enumCase, payloadVars, armExpression));
            }

            var missingCases = enumType.Cases.Where(c => !seenCases.Contains(c.Name)).Select(c => c.Name).ToArray();
            if (missingCases.Length > 0)
            {
                Report("COPE-MATCH-0004", $"Match expression for enum '{enumType.Name}' is missing cases: {string.Join(", ", missingCases)}.", match.MatchKeyword);
            }

            return new BoundMatchExpression(scrutinee, enumType, boundArms, expectedArmType ?? PrimitiveTypeSymbol.Error);
        }

        private BoundExpression BindResultMatch(MatchExpressionSyntax match, BoundExpression scrutinee, ResultTypeSymbol resultType, TypeSymbol? contextualType)
        {
            BoundExpression? okExpression = null;
            BoundExpression? errExpression = null;
            VariableSymbol? okVariable = null;
            VariableSymbol? errVariable = null;
            TypeSymbol? armType = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var arm in match.Arms)
            {
                var alternative = arm.Pattern.CaseIdentifier.Text;
                if (alternative is not "ok" and not "err")
                {
                    Report("COPE-RESULT-0004", "Result match arms must be 'ok' or 'err'.", arm.Pattern.CaseIdentifier);
                    continue;
                }

                if (!seen.Add(alternative))
                {
                    Report("COPE-RESULT-0005", $"Duplicate Result match arm '{alternative}'.", arm.Pattern.CaseIdentifier);
                }

                if (arm.Pattern.PayloadIdentifiers.Count != 1)
                {
                    Report("COPE-RESULT-0006", $"Result match arm '{alternative}' expects exactly one payload binding.", arm.Pattern.CaseIdentifier);
                }

                var payloadType = alternative == "ok" ? resultType.SuccessType : resultType.ErrorType;
                var payloadName = arm.Pattern.PayloadIdentifiers.FirstOrDefault();
                var previousScope = _scope;
                _scope = new Scope(previousScope);
                VariableSymbol? payloadVariable = null;
                if (payloadName is not null)
                {
                    payloadVariable = new VariableSymbol(payloadName.Text, payloadType, true);
                    _scope.TryDeclare(payloadVariable);
                }

                var expression = BindExpression(arm.Expression, contextualType);
                _scope = previousScope;
                if (armType is null && expression.Type != PrimitiveTypeSymbol.Error)
                {
                    armType = expression.Type;
                }
                else if (armType is not null && expression.Type != PrimitiveTypeSymbol.Error && !IsAssignable(armType, expression.Type))
                {
                    Report("COPE-RESULT-0007", $"Result match arm type mismatch: expected '{armType.Name}', got '{expression.Type.Name}'.", arm.ArrowToken);
                }

                if (alternative == "ok")
                {
                    okExpression = expression;
                    okVariable = payloadVariable;
                }
                else
                {
                    errExpression = expression;
                    errVariable = payloadVariable;
                }
            }

            if (!seen.Contains("ok") || !seen.Contains("err"))
            {
                var missing = new[] { "ok", "err" }.Where(alternative => !seen.Contains(alternative));
                Report("COPE-RESULT-0008", $"Result match is missing arms: {string.Join(", ", missing)}.", match.MatchKeyword);
            }

            return new BoundResultMatchExpression(
                scrutinee,
                okVariable ?? new VariableSymbol("<ok>", resultType.SuccessType, true),
                okExpression ?? new BoundErrorExpression(),
                errVariable ?? new VariableSymbol("<err>", resultType.ErrorType, true),
                errExpression ?? new BoundErrorExpression(),
                armType ?? contextualType ?? PrimitiveTypeSymbol.Error);
        }

        private BoundExpression BindMember(MemberAccessExpressionSyntax m)
        {
            if (m.Target is NameExpressionSyntax n && _enumTypes.TryGetValue(n.IdentifierToken.Text, out var enumType))
            {
                var @case = enumType.Cases.FirstOrDefault(c => c.Name == m.NameToken.Text);
                if (@case is null)
                {
                    Report("COPE-ENUM-0004", $"Enum '{enumType.Name}' has no case '{m.NameToken.Text}'.", m.NameToken);
                    return new BoundErrorExpression();
                }
                if (@case.HasPayload)
                {
                    Report("COPE-ENUM-0007", $"Enum case '{enumType.Name}.{@case.Name}' requires arguments.", m.NameToken);
                    return new BoundErrorExpression();
                }
                return new BoundEnumValueExpression(@case, []);
            }
            var receiver = BindExpression(m.Target);
            if (receiver.Type is TableTypeSymbol tableType)
            {
                var column = tableType.Columns.FirstOrDefault(candidate => candidate.Name == m.NameToken.Text);
                if (column is null) { Report("COPE-TABLE-0012", $"Table '{tableType.Name}' has no column '{m.NameToken.Text}'.", m.NameToken); return new BoundErrorExpression(); }
                return new BoundTableColumnAccessExpression(receiver, tableType, column);
            }
            if (receiver.Type is TableRowTypeSymbol rowType)
            {
                var field = rowType.Fields.FirstOrDefault(candidate => candidate.Name == m.NameToken.Text);
                if (field is null) { Report("COPE-TABLE-0012", $"Row '{rowType.Name}' has no field '{m.NameToken.Text}'.", m.NameToken); return new BoundErrorExpression(); }
                return new BoundTableRowFieldAccessExpression(receiver, rowType, field);
            }
            if (receiver.Type is RecordTypeSymbol recordType)
            {
                var field = recordType.Fields.FirstOrDefault(candidate => candidate.Name == m.NameToken.Text);
                if (field is null)
                {
                    Report("COPE-REC-0010", $"Record '{recordType.Name}' has no field '{m.NameToken.Text}'.", m.NameToken);
                    return new BoundErrorExpression();
                }
                return new BoundRecordFieldAccessExpression(receiver, recordType, field);
            }
            Report("COPE-REC-0010", $"Field access requires a record receiver, got '{receiver.Type.Name}'.", m.DotToken);
            return new BoundErrorExpression();
        }

        private BoundExpression BindIndex(IndexExpressionSyntax index)
        {
            var receiver = BindExpression(index.Target);
            var boundIndex = BindExpression(index.Index);
            if (!TypeFacts.AreEquivalent(boundIndex.Type, PrimitiveTypeSymbol.Number))
            {
                Report("COPE-TABLE-0013", "Table and column indexes must have type 'number'.", index.OpenBracketToken);
                return new BoundErrorExpression();
            }
            TypeSymbol errorType = _tableBoundsErrorType as TypeSymbol ?? PrimitiveTypeSymbol.Error;
            return receiver.Type switch
            {
                TableTypeSymbol table => new BoundTableRowAccessExpression(receiver, boundIndex, table, new ResultTypeSymbol(table.RowType, errorType)),
                ColumnTypeSymbol column => new BoundColumnElementAccessExpression(receiver, boundIndex, new ResultTypeSymbol(column.ElementType, errorType)),
                _ => ReportInvalidIndex(index)
            };
        }

        private BoundExpression ReportInvalidIndex(IndexExpressionSyntax index)
        {
            Report("COPE-TABLE-0011", "Indexing is currently supported only for record tables and columns.", index.OpenBracketToken);
            return new BoundErrorExpression();
        }

        private BoundExpression BindWith(WithExpressionSyntax withExpression)
        {
            var source = BindExpression(withExpression.Source);
            if (source.Type is TableRowTypeSymbol)
            {
                Report("COPE-TABLE-0016", "Table-owned rows cannot be updated with 'with'.", withExpression.WithKeyword);
                return new BoundErrorExpression();
            }
            if (source.Type is TableTypeSymbol or ColumnTypeSymbol)
            {
                Report("COPE-TABLE-0014", "Table values and columns cannot be updated with 'with'.", withExpression.WithKeyword);
                return new BoundErrorExpression();
            }
            if (source.Type is not RecordTypeSymbol recordType)
            {
                Report("COPE-REC-0012", $"A 'with' expression requires a record source, got '{source.Type.Name}'.", withExpression.WithKeyword);
                return new BoundErrorExpression();
            }
            if (withExpression.Replacements.Properties.Count == 0)
            {
                Report("COPE-REC-0013", "A 'with' expression requires at least one replacement.", withExpression.WithKeyword);
            }

            var replacements = new List<BoundRecordFieldInitializer>();
            var seen = new HashSet<RecordFieldId>();
            foreach (var property in withExpression.Replacements.Properties)
            {
                var field = recordType.Fields.FirstOrDefault(candidate => candidate.Name == property.NameToken.Text);
                if (field is null || property.NameToken.Kind != SyntaxKind.IdentifierToken)
                {
                    Report("COPE-REC-0007", $"Record '{recordType.Name}' has no field '{property.NameToken.Text}'.", property.NameToken);
                    BindExpression(property.ValueExpression);
                    continue;
                }
                if (!seen.Add(field.Id))
                {
                    Report("COPE-REC-0008", $"Field '{field.Name}' is replaced more than once.", property.NameToken);
                }
                var value = BindExpression(property.ValueExpression, field.Type);
                if (!IsAssignable(field.Type, value.Type))
                {
                    Report("COPE-REC-0014", $"Replacement for '{recordType.Name}.{field.Name}' expected '{field.Type.Name}', got '{value.Type.Name}'.", property.NameToken);
                }
                replacements.Add(new BoundRecordFieldInitializer(field, value));
            }
            return new BoundRecordWithExpression(source, recordType, replacements);
        }

        private TypeSymbol BindType(TypeSyntax? type, SyntaxToken anchor, string missingId, string missingPrefix)
        {
            if (type is null) { Report(missingId, $"Missing type annotation for {missingPrefix} '{anchor.Text}'.", anchor); return PrimitiveTypeSymbol.Error; }
            return type switch
            {
                PredefinedTypeSyntax p => p.Keyword.Kind switch
                {
                    SyntaxKind.NumberKeyword => PrimitiveTypeSymbol.Number,
                    SyntaxKind.StringKeyword => PrimitiveTypeSymbol.String,
                    SyntaxKind.BooleanKeyword => PrimitiveTypeSymbol.Boolean,
                    SyntaxKind.VoidKeyword => PrimitiveTypeSymbol.Void,
                    SyntaxKind.NullKeyword => ReportedNullType(p.Keyword),
                    _ => PrimitiveTypeSymbol.Error
                },
                ArrayTypeSyntax a => new ArrayTypeSymbol(BindType(a.ElementType, anchor, missingId, missingPrefix)),
                ColumnTypeSyntax c => new ColumnTypeSymbol(BindType(c.ElementType, anchor, "COPE-TABLE-0019", "column element")),
                QualifiedRowTypeSyntax q => ResolveQualifiedRowType(q),
                ParenthesizedTypeSyntax p => BindType(p.Type, anchor, missingId, missingPrefix),
                ResultTypeSyntax r => BindResultType(r, anchor, missingId, missingPrefix),
                IdentifierTypeSyntax i => ResolveIdentifierType(i),
                _ => PrimitiveTypeSymbol.Error
            };
        }

        private TypeSymbol ResolveQualifiedRowType(QualifiedRowTypeSyntax type)
        {
            if (_tableTypes.TryGetValue(type.TableIdentifier.Text, out var table) && type.RowIdentifier.Text == "Row") return table.RowType;
            Report("COPE-TABLE-0019", $"Unknown table row type '{type.TableIdentifier.Text}.{type.RowIdentifier.Text}'.", type.TableIdentifier);
            return PrimitiveTypeSymbol.Error;
        }

        private TypeSymbol BindResultType(ResultTypeSyntax type, SyntaxToken anchor, string missingId, string missingPrefix)
        {
            if (type.ErrorType is ResultTypeSyntax)
            {
                Report("COPE-RESULT-0009", "Repeated '!' in a type requires parentheses around the nested Result type.", type.BangToken);
            }

            return new ResultTypeSymbol(
                BindType(type.SuccessType, anchor, missingId, missingPrefix),
                BindResultErrorType(type.ErrorType, anchor, missingId, missingPrefix));
        }


        private BoundExpression BindPropagate(PropagateExpressionSyntax p)
        {
            var operand = BindExpression(p.Operand);
            if (operand.Type is not ResultTypeSymbol operandResult)
            {
                Report("COPE-TYPE-0016", "'?' can only be applied to a Result expression.", p.QuestionToken);
                return new BoundErrorExpression();
            }

            if (_propagationTargets.Count > 0)
            {
                var handler = _propagationTargets[^1];
                if (handler.ErrorType is null)
                {
                    handler.ErrorType = operandResult.ErrorType;
                }
                else if (!TypeFacts.AreEquivalent(handler.ErrorType, operandResult.ErrorType))
                {
                    Report("COPE-TRY-0003", $"Propagation error type '{operandResult.ErrorType.Name}' does not match inferred except error type '{handler.ErrorType.Name}'.", p.QuestionToken);
                    return new BoundErrorExpression();
                }

                handler.WasTargeted = true;
                return new BoundPropagateExpression(operand, operandResult, new BoundPropagationTarget.LexicalExcept(handler.HandlerId));
            }

            if (_currentFunction?.ReturnType is not ResultTypeSymbol functionResult)
            {
                Report("COPE-TYPE-0014", "'?' can only be used inside a function returning a compatible Result type or a protected try block.", p.QuestionToken);
                return new BoundErrorExpression();
            }
            if (!TypeFacts.AreEquivalent(functionResult.ErrorType, operandResult.ErrorType))
            {
                Report("COPE-TYPE-0015", $"Cannot propagate error type '{operandResult.ErrorType.Name}' from function returning error type '{functionResult.ErrorType.Name}'.", p.QuestionToken);
                return new BoundErrorExpression();
            }

            return new BoundPropagateExpression(operand, operandResult, new BoundPropagationTarget.FunctionReturn());
        }

        private BoundExpression BindUnwrap(UnwrapExpressionSyntax u)
        {
            var operand = BindExpression(u.Operand);
            if (operand.Type is not ResultTypeSymbol resultType)
            {
                Report("COPE-TYPE-0019", "'!' can only be applied to a Result expression.", u.BangToken);
                return new BoundErrorExpression();
            }

            return new BoundUnwrapExpression(operand, resultType);
        }

        private BoundExpression BindNullLiteral(LiteralExpressionSyntax l)
        {
            Report("COPE-PROFILE-0005", "Null is not supported in Browser TypeScript Profile v1. Use fallible functions or an explicit option type when available.", l.LiteralToken);
            return new BoundErrorExpression();
        }

        private TypeSymbol BindResultErrorType(TypeSyntax type, SyntaxToken anchor, string missingId, string missingPrefix)
        {
            return type switch
            {
                IdentifierTypeSyntax i when _enumTypes.TryGetValue(i.Identifier.Text, out var enumType) => enumType,
                IdentifierTypeSyntax i when _recordTypes.TryGetValue(i.Identifier.Text, out var recordType) => recordType,
                IdentifierTypeSyntax i => new ErrorNominalTypeSymbol(i.Identifier.Text),
                PredefinedTypeSyntax p when p.Keyword.Kind == SyntaxKind.NullKeyword => ReportedNullType(p.Keyword),
                PredefinedTypeSyntax => BindType(type, anchor, missingId, missingPrefix),
                ArrayTypeSyntax a => new ArrayTypeSymbol(BindResultErrorType(a.ElementType, anchor, missingId, missingPrefix)),
                ParenthesizedTypeSyntax p => BindResultErrorType(p.Type, anchor, missingId, missingPrefix),
                ResultTypeSyntax r => new ResultTypeSymbol(
                    BindType(r.SuccessType, anchor, missingId, missingPrefix),
                    BindResultErrorType(r.ErrorType, anchor, missingId, missingPrefix)),
                _ => PrimitiveTypeSymbol.Error
            };
        }

        private TypeSymbol ReportedNullType(SyntaxToken token)
        {
            Report("COPE-PROFILE-0005", "Null is not supported in Browser TypeScript Profile v1. Use fallible functions or an explicit option type when available.", token);
            return PrimitiveTypeSymbol.Error;
        }

        private TypeSymbol ResolveIdentifierType(IdentifierTypeSyntax i)
        {
            if (_enumTypes.TryGetValue(i.Identifier.Text, out var enumType))
                return enumType;
            if (_recordTypes.TryGetValue(i.Identifier.Text, out var recordType))
                return recordType;
            if (_tableTypes.TryGetValue(i.Identifier.Text, out var tableType))
                return tableType;
            Report("COPE-BIND-0004", $"Unknown type '{i.Identifier.Text}'.", i.Identifier);
            return PrimitiveTypeSymbol.Error;
        }

        private BoundExpression BindEnumConstructorCall(CallExpressionSyntax call, MemberAccessExpressionSyntax member, NameExpressionSyntax enumName)
        {
            if (!_enumTypes.TryGetValue(enumName.IdentifierToken.Text, out var enumType))
            {
                Report("COPE-ENUM-0010", "Expected enum type name.", enumName.IdentifierToken);
                return new BoundErrorExpression();
            }
            var @case = enumType.Cases.FirstOrDefault(c => c.Name == member.NameToken.Text);
            if (@case is null)
            {
                Report("COPE-ENUM-0004", $"Enum '{enumType.Name}' has no case '{member.NameToken.Text}'.", member.NameToken);
                return new BoundErrorExpression();
            }
            if (!@case.HasPayload)
            {
                Report("COPE-ENUM-0008", $"Enum case '{enumType.Name}.{@case.Name}' does not take arguments.", call.OpenParenToken);
                return new BoundErrorExpression();
            }
            if (call.Arguments.Count != @case.PayloadFields.Count)
                Report("COPE-ENUM-0005", $"Enum case '{enumType.Name}.{@case.Name}' expects {@case.PayloadFields.Count} argument{(@case.PayloadFields.Count == 1 ? "" : "s")}, got {call.Arguments.Count}.", call.OpenParenToken);
            var args = call.Arguments.Select((a, index) => BindExpression(a, index < @case.PayloadFields.Count ? @case.PayloadFields[index].Type : null)).ToArray();
            for (var i = 0; i < Math.Min(args.Length, @case.PayloadFields.Count); i++)
            {
                if (!IsAssignable(@case.PayloadFields[i].Type, args[i].Type))
                    Report("COPE-ENUM-0006", $"Argument {i + 1} for enum case '{enumType.Name}.{@case.Name}' expected '{@case.PayloadFields[i].Type.Name}', got '{args[i].Type.Name}'.", call.OpenParenToken);
            }
            return new BoundEnumValueExpression(@case, args);
        }

        private static bool IsAssignable(TypeSymbol target, TypeSymbol actual)
            => target == PrimitiveTypeSymbol.Error || actual == PrimitiveTypeSymbol.Error || TypeFacts.AreEquivalent(target, actual);

        private static bool IsPrimitiveEqualityType(TypeSymbol type)
            => type == PrimitiveTypeSymbol.Number
                || type == PrimitiveTypeSymbol.String
                || type == PrimitiveTypeSymbol.Boolean;

        private void ReportTypeMismatch(string fallbackId, TypeSymbol expected, TypeSymbol actual, SyntaxToken anchor)
        {
            if (expected is RecordTypeSymbol && actual is RecordTypeSymbol)
            {
                Report("COPE-REC-0015", $"Nominal record type mismatch: expected '{expected.Name}', got '{actual.Name}'.", anchor);
                return;
            }
            if (expected is TableRowTypeSymbol && actual is TableRowTypeSymbol)
            {
                Report("COPE-TABLE-0018", $"Nominal table row type mismatch: expected '{expected.Name}', got '{actual.Name}'.", anchor);
                return;
            }
            Report(fallbackId, $"Type mismatch: expected '{expected.Name}', got '{actual.Name}'.", anchor);
        }

        private BoundExpression EnsureBoolean(BoundExpression e, SyntaxToken at)
        {
            if (e.Type != PrimitiveTypeSymbol.Boolean && e.Type != PrimitiveTypeSymbol.Error)
                Report("COPE-TYPE-0001", $"Type mismatch: expected 'boolean', got '{e.Type.Name}'.", at);
            return e;
        }

        private static SyntaxToken AnchorToken(ExpressionSyntax s) => s switch
        {
            CallExpressionSyntax c => c.OpenParenToken,
            BinaryExpressionSyntax b => b.OperatorToken,
            UnaryExpressionSyntax u => u.OperatorToken,
            AssignmentExpressionSyntax a => a.EqualsToken,
            ParenthesizedExpressionSyntax p => p.OpenParenToken,
            PropagateExpressionSyntax p => p.QuestionToken,
            UnwrapExpressionSyntax u => u.BangToken,
            NameExpressionSyntax n => n.IdentifierToken,
            LiteralExpressionSyntax l => l.LiteralToken,
            ArrayLiteralExpressionSyntax a => a.OpenBracketToken,
            MatchExpressionSyntax m => m.MatchKeyword,
            TryExceptExpressionSyntax t => t.TryKeyword,
            ObjectLiteralExpressionSyntax o => o.OpenBraceToken,
            MemberAccessExpressionSyntax m => m.DotToken,
            WithExpressionSyntax w => w.WithKeyword,
            _ => throw new InvalidOperationException("No anchor token for expression kind.")
        };

        private void Report(string id, string msg, SyntaxToken at) => _diagnostics.Report(id, msg, at.Position, at.Text.Length);
    }
}
