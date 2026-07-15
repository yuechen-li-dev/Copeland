using Copeland.TS.Diagnostics;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Tson;
using System.Security.Cryptography;
using System.Text;

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

    internal static IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> BindOpenGenericBodiesForTesting(SyntaxTree tree)
    {
        var impl = new BinderImpl(tree, null);
        _ = impl.Bind();
        return impl.GetOpenGenericBodiesForTesting();
    }

    internal static IReadOnlyDictionary<string, string> AllocateSpecializationNamesForTesting(
        string genericName,
        string displaySuffix,
        IEnumerable<string> semanticIdentities,
        Func<string, string>? hashProvider = null)
        => BinderImpl.AllocateSpecializationNamesForTesting(
            genericName,
            displaySuffix,
            semanticIdentities,
            hashProvider);

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
        private const int MaxTypeParametersPerFunction = 8;
        private const int MaxRequirementInterfacesPerTypeParameter = 8;
        private const int MaxNormalizedRequirementFields = 32;
        private const int MaxInterfaceFieldsPerCompilation = 128;
        private const int MaxClosedTypeDepth = 16;
        private const int MaxClosedInstantiationsPerGenericDefinition = 16;
        private const int MaxClosedInstantiationsPerCompilation = 128;
        private const int MaxDiagnosticRequirementFields = 4;
        private const int MaxInferenceMatchDepth = 16;
        private const int MaxInferenceMatchSteps = 128;
        private const int MaxInferenceEvidencePerTypeParameter = 16;
        private const int MaxCallableParameters = 32;
        private const int MaxCallableTypeDepth = 16;

        private int _loopDepth;
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
        private readonly Dictionary<string, TypeAliasSymbol> _aliases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, InterfaceSymbol> _interfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NominalUnionDeclarationSyntax> _unionDeclarations = new(StringComparer.Ordinal);
        private readonly Dictionary<FunctionSymbol, BoundFunctionDeclaration> _genericBodies = [];
        private readonly Dictionary<string, BoundFunctionDeclaration> _closedInstantiations = new(StringComparer.Ordinal);
        private readonly Dictionary<FunctionSymbol, int> _closedInstantiationCounts = [];
        private readonly Dictionary<string, string> _closedInstantiationNames = new(StringComparer.Ordinal);
        private Dictionary<string, TypeParameterSymbol>? _activeTypeParameters;
        private int _nextInterfaceId = 1;
        private int _totalInterfaceFieldCount;
        private readonly Dictionary<TypeAliasSymbol, TypeAliasDeclarationSyntax> _aliasDeclarations = [];
        private readonly List<TypeAliasSymbol> _aliasesInDeclarationOrder = [];
        private readonly HashSet<MemberSyntax> _rejectedTypeDeclarations = [];
        private readonly HashSet<VariableSymbol> _tableSingletonVariables = [];
        private EnumTypeSymbol? _tableBoundsErrorType;
        private EnumTypeSymbol? _tsonEncodeErrorType;
        private readonly Dictionary<TypeSymbol, BoundTsonEncodingPlan> _tsonEncodingPlans = [];
        private bool _usesTsonEncode;
        private readonly List<PropagationTargetContext> _propagationTargets = [];
        private int _nextHandlerId = 1;
        private int _nextRecordTypeId = 1;
        private int _nextTableTypeId = 1;
        private string? _schemaIdentity;
        private TypeAliasDeclarationSyntax? _currentAliasDeclaration;

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
            AnalyzeAliasTypeNameCollisions(_tree.Root);
            PredeclareInterfaces(_tree.Root);
            PredeclareAliases(_tree.Root);
            PredeclareRecords(_tree.Root);
            PredeclareTables(_tree.Root);
            PredeclareEnums(_tree.Root);
            PredeclareNominalUnions(_tree.Root);
            ResolveAliases();
            BindInterfaceBodies(_tree.Root);
            PredeclareFunctions(_tree.Root);
            BindRecordBodies(_tree.Root);
            BindEnumBodies(_tree.Root);
            BindNominalUnionBodies(_tree.Root);
            BindTableBodies(_tree.Root);
            ValidateRecordCycles();
            foreach (var generic in _tree.Root.Members.OfType<FunctionDeclarationSyntax>().Where(function => function.TypeParameters.Count > 0))
            {
                _genericBodies[(FunctionSymbol)_globalLookup(generic.Identifier.Text)!] = BindFunction(generic);
            }
            foreach (var m in _tree.Root.Members)
            {
                if (m is FunctionDeclarationSyntax f && f.TypeParameters.Count == 0) _functions.Add(BindFunction(f));
                else if (m is EnumDeclarationSyntax e && e.Identifier.Text != "TableBoundsError" && _enumTypes.TryGetValue(e.Identifier.Text, out var enumType)) _enums.Add(new BoundEnumDeclaration(enumType));
                else if (m is NominalUnionDeclarationSyntax union && _enumTypes.TryGetValue(union.Identifier.Text, out var unionType)) _enums.Add(new BoundEnumDeclaration(unionType));
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

        public IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> GetOpenGenericBodiesForTesting()
            => new Dictionary<FunctionSymbol, BoundFunctionDeclaration>(_genericBodies);

        private Symbol? _globalLookup(string name)
        {
            _global.TryLookup(name, out var symbol);
            return symbol;
        }

        private void AnalyzeAliasTypeNameCollisions(CompilationUnitSyntax root)
        {
            var owners = new Dictionary<string, MemberSyntax>(StringComparer.Ordinal);
            foreach (var member in root.Members)
            {
                string? name = member switch
                {
                    TypeAliasDeclarationSyntax alias => alias.Identifier.Text,
                    NominalUnionDeclarationSyntax union => union.Identifier.Text,
                    RecordDeclarationSyntax record => record.Identifier.Text,
                    EnumDeclarationSyntax @enum => @enum.Identifier.Text,
                    TableDeclarationSyntax table => table.Identifier.Text,
                    InterfaceDeclarationSyntax @interface => @interface.Identifier.Text,
                    _ => null
                };

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (!owners.TryGetValue(name, out var owner))
                {
                    owners.Add(name, member);
                    continue;
                }

                if (member is NominalUnionDeclarationSyntax || owner is NominalUnionDeclarationSyntax)
                {
                    SyntaxToken anchor = GetTypeDeclarationName(member);
                    Report(
                        "COPE-UNION-0005",
                        $"Type name '{name}' is already declared in this compilation unit.",
                        anchor);
                    _rejectedTypeDeclarations.Add(member);
                }
                else if (member is TypeAliasDeclarationSyntax || owner is TypeAliasDeclarationSyntax || member is InterfaceDeclarationSyntax || owner is InterfaceDeclarationSyntax)
                {
                    SyntaxToken anchor = GetTypeDeclarationName(member);
                    Report(
                        "COPE-ALIAS-0003",
                        $"Type name '{name}' is already declared in this compilation unit.",
                        anchor);
                    _rejectedTypeDeclarations.Add(member);
                }
            }
        }

        private static SyntaxToken GetTypeDeclarationName(MemberSyntax declaration)
        {
            return declaration switch
            {
                TypeAliasDeclarationSyntax alias => alias.Identifier,
                NominalUnionDeclarationSyntax union => union.Identifier,
                RecordDeclarationSyntax record => record.Identifier,
                EnumDeclarationSyntax @enum => @enum.Identifier,
                TableDeclarationSyntax table => table.Identifier,
                InterfaceDeclarationSyntax @interface => @interface.Identifier,
                _ => throw new InvalidOperationException("Expected a type declaration.")
            };
        }

        private void PredeclareInterfaces(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<InterfaceDeclarationSyntax>())
            {
                if (_rejectedTypeDeclarations.Contains(declaration) || string.IsNullOrEmpty(declaration.Identifier.Text)) continue;
                if (_interfaces.ContainsKey(declaration.Identifier.Text)) continue;
                _interfaces.Add(declaration.Identifier.Text, new InterfaceSymbol(declaration.Identifier.Text, _nextInterfaceId++));
            }
        }

        private void BindInterfaceBodies(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<InterfaceDeclarationSyntax>())
            {
                if (!_interfaces.TryGetValue(declaration.Identifier.Text, out var @interface)) continue;
                if (declaration.Fields.Count == 0)
                {
                    Report("COPE-INTERFACE-0001", "Interfaces must declare at least one field.", declaration.Identifier);
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in declaration.Fields)
                {
                    if (!field.HasExplicitType || !field.HasTerminator || field.UnsupportedTokens.Count > 0)
                    {
                        Report("COPE-INTERFACE-0002", $"Interface field '{field.Identifier.Text}' must be a readable field with an explicit type and semicolon.", field.Identifier);
                    }
                    if (!names.Add(field.Identifier.Text))
                    {
                        Report("COPE-INTERFACE-0003", $"Duplicate field '{field.Identifier.Text}' in interface '{@interface.Name}'.", field.Identifier);
                        continue;
                    }
                    var type = field.HasExplicitType
                        ? BindType(field.Type, field.Identifier, "COPE-INTERFACE-0004", "interface field")
                        : PrimitiveTypeSymbol.Error;
                    if (type is TypeParameterTypeSymbol || type == PrimitiveTypeSymbol.Void)
                    {
                        Report("COPE-INTERFACE-0004", $"Interface field '{field.Identifier.Text}' has an illegal type '{type.Name}'.", field.Identifier);
                    }
                    if (ContainsCallable(type))
                    {
                        Report("COPE-CALL-0007", "Callable types are not supported in interface fields.", field.Identifier);
                    }
                    @interface.AddField(new RequirementFieldSymbol(field.Identifier.Text, type, @interface.Fields.Count));
                    _totalInterfaceFieldCount++;
                    if (_totalInterfaceFieldCount > MaxInterfaceFieldsPerCompilation)
                    {
                        Report("COPE-INTERFACE-0006", $"The compilation exceeded the {MaxInterfaceFieldsPerCompilation} interface-field limit.", field.Identifier);
                    }
                }
            }
        }

        private void PredeclareAliases(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<TypeAliasDeclarationSyntax>())
            {
                if (_rejectedTypeDeclarations.Contains(declaration)
                    || string.IsNullOrEmpty(declaration.Identifier.Text))
                {
                    continue;
                }

                if (_enumTypes.ContainsKey(declaration.Identifier.Text))
                {
                    Report(
                        "COPE-ALIAS-0003",
                        $"Type name '{declaration.Identifier.Text}' is compiler-owned or already declared.",
                        declaration.Identifier);
                    continue;
                }

                var alias = new TypeAliasSymbol(declaration.Identifier.Text);
                _aliases.Add(alias.Name, alias);
                _aliasDeclarations.Add(alias, declaration);
                _aliasesInDeclarationOrder.Add(alias);
            }
        }

        private void ResolveAliases()
        {
            var declarationIndices = _aliasesInDeclarationOrder
                .Select((alias, index) => (alias, index))
                .ToDictionary(item => item.alias, item => item.index);
            var dependencies = new Dictionary<TypeAliasSymbol, IReadOnlyList<TypeAliasSymbol>>();
            var dependents = new Dictionary<TypeAliasSymbol, List<TypeAliasSymbol>>();

            foreach (var alias in _aliasesInDeclarationOrder)
            {
                var aliasDependencies = CollectAliasDependencies(_aliasDeclarations[alias].TargetType)
                    .Distinct()
                    .OrderBy(dependency => declarationIndices[dependency])
                    .ToArray();
                dependencies.Add(alias, aliasDependencies);
                dependents.Add(alias, []);
            }

            foreach (var alias in _aliasesInDeclarationOrder)
            {
                foreach (var dependency in dependencies[alias])
                {
                    dependents[dependency].Add(alias);
                }
            }

            DetectAliasCycles(dependencies, declarationIndices);

            var remainingDependencies = dependencies.ToDictionary(
                item => item.Key,
                item => item.Value.Count);
            var ready = new SortedSet<int>();
            for (var index = 0; index < _aliasesInDeclarationOrder.Count; index++)
            {
                if (remainingDependencies[_aliasesInDeclarationOrder[index]] == 0)
                {
                    ready.Add(index);
                }
            }

            while (ready.Count > 0)
            {
                int index = ready.Min;
                ready.Remove(index);
                TypeAliasSymbol alias = _aliasesInDeclarationOrder[index];
                ResolveAliasTarget(alias);

                foreach (var dependent in dependents[alias])
                {
                    remainingDependencies[dependent]--;
                    if (remainingDependencies[dependent] == 0)
                    {
                        ready.Add(declarationIndices[dependent]);
                    }
                }
            }

            foreach (var alias in _aliasesInDeclarationOrder)
            {
                if (!alias.IsResolved)
                {
                    alias.CanonicalType = PrimitiveTypeSymbol.Error;
                    alias.IsResolved = true;
                }
            }
        }

        private IReadOnlyList<TypeAliasSymbol> CollectAliasDependencies(TypeSyntax root)
        {
            var dependencies = new List<TypeAliasSymbol>();
            var pending = new Stack<TypeSyntax>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                TypeSyntax type = pending.Pop();
                switch (type)
                {
                    case IdentifierTypeSyntax identifier
                        when _aliases.TryGetValue(identifier.Identifier.Text, out var alias):
                        dependencies.Add(alias);
                        break;
                    case QualifiedRowTypeSyntax qualified
                        when _aliases.TryGetValue(qualified.TableIdentifier.Text, out var alias):
                        dependencies.Add(alias);
                        break;
                    case ArrayTypeSyntax array:
                        pending.Push(array.ElementType);
                        break;
                    case ColumnTypeSyntax column:
                        pending.Push(column.ElementType);
                        break;
                    case ParenthesizedTypeSyntax parenthesized:
                        pending.Push(parenthesized.Type);
                        break;
                    case ResultTypeSyntax result:
                        pending.Push(result.ErrorType);
                        pending.Push(result.SuccessType);
                        break;
                    case CallableTypeSyntax callable:
                        pending.Push(callable.ReturnType);
                        foreach (var parameter in callable.Parameters) pending.Push(parameter.Type);
                        break;
                }
            }

            return dependencies;
        }

        private void DetectAliasCycles(
            IReadOnlyDictionary<TypeAliasSymbol, IReadOnlyList<TypeAliasSymbol>> dependencies,
            IReadOnlyDictionary<TypeAliasSymbol, int> declarationIndices)
        {
            var states = _aliasesInDeclarationOrder.ToDictionary(alias => alias, _ => 0);
            var reportedCycleAliases = new HashSet<TypeAliasSymbol>();

            foreach (var start in _aliasesInDeclarationOrder)
            {
                if (states[start] != 0)
                {
                    continue;
                }

                var stack = new List<AliasVisitFrame>();
                states[start] = 1;
                stack.Add(new AliasVisitFrame(start));

                while (stack.Count > 0)
                {
                    AliasVisitFrame frame = stack[^1];
                    IReadOnlyList<TypeAliasSymbol> aliasDependencies = dependencies[frame.Alias];
                    if (frame.NextDependencyIndex >= aliasDependencies.Count)
                    {
                        states[frame.Alias] = 2;
                        stack.RemoveAt(stack.Count - 1);
                        continue;
                    }

                    TypeAliasSymbol dependency = aliasDependencies[frame.NextDependencyIndex];
                    frame.NextDependencyIndex++;
                    if (states[dependency] == 0)
                    {
                        states[dependency] = 1;
                        stack.Add(new AliasVisitFrame(dependency));
                        continue;
                    }

                    if (states[dependency] != 1)
                    {
                        continue;
                    }

                    int cycleStart = stack.FindIndex(item => ReferenceEquals(item.Alias, dependency));
                    if (cycleStart < 0)
                    {
                        continue;
                    }

                    var cycle = stack
                        .Skip(cycleStart)
                        .Select(item => item.Alias)
                        .ToList();
                    if (cycle.Any(reportedCycleAliases.Contains))
                    {
                        continue;
                    }

                    foreach (var cycleAlias in cycle)
                    {
                        reportedCycleAliases.Add(cycleAlias);
                    }

                    int primaryIndex = cycle
                        .Select((alias, index) => (alias, index))
                        .MinBy(item => declarationIndices[item.alias])
                        .index;
                    var orderedCycle = cycle
                        .Skip(primaryIndex)
                        .Concat(cycle.Take(primaryIndex))
                        .ToArray();
                    TypeAliasSymbol primary = orderedCycle[0];
                    string path = FormatAliasCyclePath(orderedCycle);
                    Report(
                        "COPE-ALIAS-0005",
                        $"Type alias cycle for '{primary.Name}': {path}.",
                        _aliasDeclarations[primary].Identifier);
                }
            }
        }

        private static string FormatAliasCyclePath(IReadOnlyList<TypeAliasSymbol> cycle)
        {
            const int maximumDisplayedAliases = 16;
            if (cycle.Count <= maximumDisplayedAliases)
            {
                return string.Join(" -> ", cycle.Select(alias => alias.Name).Append(cycle[0].Name));
            }

            return string.Join(" -> ", cycle.Take(maximumDisplayedAliases).Select(alias => alias.Name))
                + " -> ... -> "
                + cycle[0].Name;
        }

        private void ResolveAliasTarget(TypeAliasSymbol alias)
        {
            TypeAliasDeclarationSyntax declaration = _aliasDeclarations[alias];
            _currentAliasDeclaration = declaration;
            try
            {
                alias.CanonicalType = BindType(
                    declaration.TargetType,
                    declaration.Identifier,
                    "COPE-ALIAS-0004",
                    "type alias");
                alias.IsResolved = true;
            }
            finally
            {
                _currentAliasDeclaration = null;
            }
        }

        private sealed class AliasVisitFrame(TypeAliasSymbol alias)
        {
            public TypeAliasSymbol Alias { get; } = alias;
            public int NextDependencyIndex { get; set; }
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
                if (_rejectedTypeDeclarations.Contains(declaration))
                {
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
                string? identity = _schemaIdentity is null ? null : $"{_schemaIdentity}#{declaration.Identifier.Text}";
                var table = new TableTypeSymbol(declaration.Identifier.Text, new TableTypeId(_nextTableTypeId++), identity);
                var tableSingleton = new VariableSymbol(table.Name, table, true);
                if (!_global.TryDeclare(tableSingleton) || _tableTypes.ContainsKey(table.Name))
                {
                    Report("COPE-TABLE-0002", $"Duplicate table declaration '{table.Name}'.", declaration.Identifier);
                    continue;
                }
                _tableTypes.Add(table.Name, table);
                _tableSingletonVariables.Add(tableSingleton);
            }
        }

        private void BindTableBodies(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<TableDeclarationSyntax>())
            {
                if (!_tableTypes.TryGetValue(declaration.Identifier.Text, out var table)) continue;
                if (declaration.Columns.Count == 0) Report("COPE-TABLE-0003", "A table requires at least one column.", declaration.Identifier);
                if (declaration.AssetClause is not null)
                {
                    BindAssetTableBody(declaration, table);
                    continue;
                }
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
                    if (ContainsCallable(elementType))
                    {
                        Report("COPE-CALL-0009", "Callable types are not supported in record tables.", columnSyntax.Identifier);
                    }
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

        private void BindAssetTableBody(
            TableDeclarationSyntax declaration,
            TableTypeSymbol table)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var sourceColumns = new List<(TableColumnSyntax Syntax, TableColumnSymbol Symbol)>();
            foreach (var columnSyntax in declaration.Columns)
            {
                if (!names.Add(columnSyntax.Identifier.Text))
                {
                    Report(
                        "COPE-TABLE-0004",
                        $"Duplicate column '{columnSyntax.Identifier.Text}'.",
                        columnSyntax.Identifier);
                    continue;
                }

                if (columnSyntax.ExplicitType is null)
                {
                    Report(
                        "COPE-TSON-TABLE-0002",
                        "An asset-backed table column requires an explicit element type.",
                        columnSyntax.Identifier);
                }

                if (columnSyntax.HasInlineData || columnSyntax.EqualsToken is not null)
                {
                    Report(
                        "COPE-TSON-ASSET-0001",
                        "An asset-backed table declaration cannot also contain inline column data.",
                        columnSyntax.EqualsToken ?? columnSyntax.Identifier);
                }

                TypeSymbol elementType = columnSyntax.ExplicitType is null
                    ? PrimitiveTypeSymbol.Error
                    : BindType(
                        columnSyntax.ExplicitType,
                        columnSyntax.Identifier,
                        "COPE-TABLE-0019",
                        "table column");
                if (!IsEligibleTsonTableCellType(elementType))
                {
                    Report(
                        "COPE-TSON-TABLE-0004",
                        $"Asset-backed table column type '{elementType.Name}' is not in the supported TSON table cell family.",
                        columnSyntax.Identifier);
                }

                var column = new TableColumnSymbol(
                    columnSyntax.Identifier.Text,
                    new TableColumnId(table.Id, table.Columns.Count),
                    elementType);
                table.AddColumn(column);
                sourceColumns.Add((columnSyntax, column));
            }

            if (_diagnostics.Diagnostics.Any(diagnostic =>
                    diagnostic.Position >= declaration.RecordKeyword.Position
                    && diagnostic.Position <= declaration.CloseBraceToken.Position))
            {
                _tables.Add(new BoundTableDefinition(
                    table,
                    sourceColumns.Select(column => new BoundTableColumnDefinition(column.Symbol, [])).ToArray(),
                    0));
                return;
            }

            if (!TryReadTableAsset(declaration, out TsonDocument? document, out SyntaxToken? assetAnchor))
            {
                _tables.Add(new BoundTableDefinition(
                    table,
                    sourceColumns.Select(column => new BoundTableColumnDefinition(column.Symbol, [])).ToArray(),
                    0));
                return;
            }

            var tsonTable = (TsonTable)document!.Root;
            if (!ValidateTsonTableSchema(
                    document.Catalog,
                    tsonTable,
                    table,
                    sourceColumns,
                    assetAnchor!))
            {
                _tables.Add(new BoundTableDefinition(
                    table,
                    sourceColumns.Select(column => new BoundTableColumnDefinition(column.Symbol, [])).ToArray(),
                    0));
                return;
            }

            var columns = new List<BoundTableColumnDefinition>(sourceColumns.Count);
            for (var columnIndex = 0; columnIndex < sourceColumns.Count; columnIndex++)
            {
                var sourceColumn = sourceColumns[columnIndex].Symbol;
                var assetColumn = tsonTable.Columns[columnIndex];
                var cells = new List<BoundTableConstant>(assetColumn.Cells.Count);
                foreach (var value in assetColumn.Cells)
                {
                    if (!TryLowerTsonValue(
                            value,
                            sourceColumn.Type,
                            "table asset",
                            assetAnchor!,
                            out BoundExpression? expression))
                    {
                        continue;
                    }

                    BoundTableConstant? constant = BindTableConstant(expression!);
                    if (constant is null)
                    {
                        Report(
                            "COPE-TSON-TABLE-0004",
                            $"Asset cell in column '{sourceColumn.Name}' could not be projected to a closed table constant.",
                            assetAnchor!);
                        continue;
                    }

                    cells.Add(constant);
                }
                columns.Add(new BoundTableColumnDefinition(sourceColumn, cells));
            }

            _tables.Add(new BoundTableDefinition(table, columns, tsonTable.RowCount));
        }

        private bool TryReadTableAsset(
            TableDeclarationSyntax declaration,
            out TsonDocument? document,
            out SyntaxToken? assetAnchor)
        {
            document = null;
            assetAnchor = declaration.AssetClause?.FromToken;
            CallExpressionSyntax call = declaration.AssetClause!.AssetCall;
            if (call.Target is not NameExpressionSyntax intrinsic
                || intrinsic.IdentifierToken.Text != "tsonAsset")
            {
                Report(
                    "COPE-TSON-ASSET-0001",
                    "An asset-backed table declaration requires the reserved 'tsonAsset' intrinsic.",
                    declaration.AssetClause.FromToken);
                return false;
            }

            if (call.Arguments.Count != 1
                || call.Arguments[0] is not LiteralExpressionSyntax pathLiteral
                || pathLiteral.LiteralToken.Kind != SyntaxKind.StringToken)
            {
                Report(
                    "COPE-TSON-ASSET-0001",
                    "A table 'tsonAsset' clause requires exactly one string-literal relative path.",
                    call.OpenParenToken);
                return false;
            }

            assetAnchor = pathLiteral.LiteralToken;
            if (_schemaIdentity is null)
            {
                Report(
                    "COPE-TSON-ASSET-0004",
                    "An asset-backed table requires one valid top-level '$schema' declaration.",
                    declaration.AssetClause.FromToken);
                return false;
            }

            if (_assetResolver is null)
            {
                Report(
                    "COPE-TSON-ASSET-0002",
                    "This compilation has no source path, compilation root, and asset source for resolving TSON assets.",
                    pathLiteral.LiteralToken);
                return false;
            }

            string authoredPath = (string)pathLiteral.LiteralToken.Value!;
            if (!_assetResolver.TryResolve(authoredPath, out var asset, out string? resolutionError))
            {
                Report(
                    "COPE-TSON-ASSET-0002",
                    resolutionError ?? "The TSON table asset could not be resolved.",
                    pathLiteral.LiteralToken);
                return false;
            }

            TsonDocumentProfile profile = asset!.NormalizedPath.EndsWith(
                ".obj.ts",
                StringComparison.OrdinalIgnoreCase)
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
                        Math.Max(1, diagnostic.Length),
                        asset.NormalizedPath);
                }
                return false;
            }

            if (read.Document!.Root is not TsonTable)
            {
                Report(
                    "COPE-TSON-TABLE-0001",
                    "An asset-backed record table requires one table-root TSON document.",
                    pathLiteral.LiteralToken);
                return false;
            }

            document = read.Document;
            return true;
        }

        private static BoundTableConstant? BindTableConstant(BoundExpression expression)
            => expression switch
            {
                BoundLiteralExpression literal when literal.Value is not null => new BoundTableLiteralConstant(literal.Value, literal.Type),
                BoundUnaryExpression { OperatorKind: SyntaxKind.MinusToken, Operand: BoundLiteralExpression literal }
                    when literal.Type == PrimitiveTypeSymbol.Number && literal.Value is IConvertible number => new BoundTableLiteralConstant(-number.ToDouble(System.Globalization.CultureInfo.InvariantCulture), literal.Type),
                BoundEnumValueExpression value => BindTableEnumConstant(value),
                BoundArrayExpression array => BindTableArrayConstant(array),
                BoundOkExpression ok => BindTableResultConstant(true, ok.Payload, (ResultTypeSymbol)ok.Type),
                BoundErrExpression err => BindTableResultConstant(false, err.Payload, (ResultTypeSymbol)err.Type),
                BoundRecordConstructionExpression record => BindTableRecordConstant(record),
                _ => null,
            };

        private static BoundTableConstant? BindTableArrayConstant(BoundArrayExpression array)
        {
            if (array.Type is not ArrayTypeSymbol arrayType)
            {
                return null;
            }

            var elements = array.Elements.Select(BindTableConstant).ToArray();
            return elements.Any(element => element is null)
                ? null
                : new BoundTableArrayConstant(
                    arrayType,
                    elements.Select(element => element!).ToArray());
        }

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

        private bool IsEligibleTableCellType(TypeSymbol type, HashSet<TypeSymbol> visiting, out bool isCyclic)
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
                ArrayTypeSymbol array when _schemaIdentity is not null => IsEligibleTableCellType(array.ElementType, visiting, out _),
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

        private static bool IsEligibleTsonTableCellType(TypeSymbol type)
        {
            var visiting = new HashSet<TypeSymbol>();
            return IsEligibleTsonTableCellType(type, visiting);
        }

        private static bool IsEligibleTsonTableCellType(
            TypeSymbol type,
            HashSet<TypeSymbol> visiting)
        {
            if (!visiting.Add(type))
            {
                return false;
            }

            bool eligible = type switch
            {
                PrimitiveTypeSymbol primitive => primitive == PrimitiveTypeSymbol.Number
                    || primitive == PrimitiveTypeSymbol.String
                    || primitive == PrimitiveTypeSymbol.Boolean,
                ArrayTypeSymbol array => IsEligibleTsonTableCellType(array.ElementType, visiting),
                EnumTypeSymbol @enum => @enum.Cases.All(@case =>
                    @case.PayloadFields.All(field =>
                        IsEligibleTsonTableCellType(field.Type, visiting))),
                RecordTypeSymbol record => record.Fields.All(field =>
                    IsEligibleTsonTableCellType(field.Type, visiting)),
                _ => false,
            };
            visiting.Remove(type);
            return eligible;
        }

        private bool ValidateTsonTableSchema(
            TsonCatalog catalog,
            TsonTable assetTable,
            TableTypeSymbol sourceTable,
            IReadOnlyList<(TableColumnSyntax Syntax, TableColumnSymbol Symbol)> sourceColumns,
            SyntaxToken anchor)
        {
            string expectedTableIdentity = $"{_schemaIdentity}#{sourceTable.Name}";
            if (!string.Equals(catalog.SchemaIdentity, _schemaIdentity, StringComparison.Ordinal)
                || !string.Equals(
                    assetTable.Schema.IdentityValue.Value,
                    expectedTableIdentity,
                    StringComparison.Ordinal))
            {
                Report(
                    "COPE-TSON-ASSET-0003",
                    $"Table asset identity must be exactly '{expectedTableIdentity}'.",
                    anchor);
                return false;
            }

            if (assetTable.Columns.Count != sourceColumns.Count)
            {
                Report(
                    "COPE-TSON-ASSET-0003",
                    $"Table asset '{expectedTableIdentity}' does not have the exact source-declared column count.",
                    anchor);
                return false;
            }

            var visited = new HashSet<TypeSymbol>();
            for (var index = 0; index < sourceColumns.Count; index++)
            {
                TableColumnSymbol sourceColumn = sourceColumns[index].Symbol;
                TsonTableColumn assetColumn = assetTable.Columns[index];
                string expectedColumnIdentity = $"{expectedTableIdentity}.{sourceColumn.Name}";
                if (assetColumn.Schema.Name != sourceColumn.Name
                    || assetColumn.Schema.Identity.Value != expectedColumnIdentity
                    || !TsonTypeMatches(assetColumn.Schema.ElementType, sourceColumn.Type))
                {
                    Report(
                        "COPE-TSON-ASSET-0003",
                        $"Table asset column {index} must be exactly '{expectedColumnIdentity}: {sourceColumn.Type.Name}'.",
                        sourceColumns[index].Syntax.Identifier);
                    return false;
                }

                if (!ValidateTsonSchemaType(
                        catalog,
                        sourceColumn.Type,
                        visited,
                        out string? mismatch))
                {
                    Report(
                        "COPE-TSON-ASSET-0003",
                        mismatch ?? $"Reachable schema mismatch for '{expectedColumnIdentity}'.",
                        sourceColumns[index].Syntax.Identifier);
                    return false;
                }
            }

            return true;
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
                if (_rejectedTypeDeclarations.Contains(declaration))
                {
                    continue;
                }

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
                    ValidateRuntimeValueType(fieldType, fieldSyntax.Identifier, "record field");
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
                var typeParameters = new List<TypeParameterSymbol>();
                var typeParameterNames = new HashSet<string>(StringComparer.Ordinal);
                if (m.TypeParameters.Count > MaxTypeParametersPerFunction)
                {
                    Report("COPE-GENERIC-0011", $"Generic function '{m.Identifier.Text}' exceeds the {MaxTypeParametersPerFunction} type-parameter limit.", m.Identifier);
                }
                for (var index = 0; index < m.TypeParameters.Count; index++)
                {
                    var syntax = m.TypeParameters[index];
                    if (!typeParameterNames.Add(syntax.Identifier.Text))
                    {
                        Report("COPE-GENERIC-0001", $"Duplicate type parameter '{syntax.Identifier.Text}'.", syntax.Identifier);
                    }
                    if (_aliases.ContainsKey(syntax.Identifier.Text) || _recordTypes.ContainsKey(syntax.Identifier.Text) || _enumTypes.ContainsKey(syntax.Identifier.Text) || _tableTypes.ContainsKey(syntax.Identifier.Text) || _interfaces.ContainsKey(syntax.Identifier.Text))
                    {
                        Report("COPE-GENERIC-0002", $"Type parameter '{syntax.Identifier.Text}' cannot shadow a compilation-unit type declaration.", syntax.Identifier);
                    }
                    var requirements = BindRequirements(syntax);
                    typeParameters.Add(new TypeParameterSymbol(syntax.Identifier.Text, new TypeParameterTypeSymbol(syntax.Identifier.Text, index), requirements));
                }
                _activeTypeParameters = CreateTypeParameterScope(typeParameters);
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
                    ValidateRuntimeValueType(pt, p.Identifier, "parameter");
                    if (!seen.Add(p.Identifier.Text)) Report("COPE-BIND-0005", $"Duplicate parameter '{p.Identifier.Text}'.", p.Identifier);
                    ps.Add(new ParameterSymbol(p.Identifier.Text, pt, GetAuthoredAliasName(p.Type)));
                }
                var rt = BindType(m.ReturnType, m.Identifier, missingId: "COPE-TYPE-0002", missingPrefix: "function return");
                ValidateFunctionReturnType(rt, m.Identifier);
                _activeTypeParameters = null;
                var fn = new FunctionSymbol(
                    m.Identifier.Text,
                    ps,
                    rt,
                    GetAuthoredAliasName(m.ReturnType),
                    CreateFunctionStableIdentity(m.Identifier.Text))
                {
                    TypeParameters = typeParameters
                };
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
                if (_rejectedTypeDeclarations.Contains(m))
                {
                    continue;
                }

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
                        TypeSymbol payloadType = BindType(field.Type, field.Identifier, "COPE-TYPE-0002", "enum payload");
                        ValidateRuntimeValueType(payloadType, field.Identifier, "enum payload");
                        payloadFields.Add(new EnumPayloadFieldSymbol(field.Identifier.Text, payloadType));
                    }
                    enumType.AddCase(new EnumCaseSymbol(@case.Identifier.Text, enumType, payloadFields));
                }
            }
        }

        private void PredeclareNominalUnions(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<NominalUnionDeclarationSyntax>())
            {
                if (_rejectedTypeDeclarations.Contains(declaration)
                    || string.IsNullOrEmpty(declaration.Identifier.Text))
                {
                    continue;
                }

                string? identity = _schemaIdentity is null ? null : $"{_schemaIdentity}#{declaration.Identifier.Text}";
                var unionType = new EnumTypeSymbol(declaration.Identifier.Text, identity);
                if (!_global.TryDeclare(new VariableSymbol(unionType.Name, unionType, true))
                    || _enumTypes.ContainsKey(unionType.Name))
                {
                    Report(
                        "COPE-UNION-0005",
                        $"Type name '{unionType.Name}' is already declared in this compilation unit.",
                        declaration.Identifier);
                    continue;
                }

                _enumTypes.Add(unionType.Name, unionType);
                _unionDeclarations.Add(unionType.Name, declaration);
            }
        }

        private void BindNominalUnionBodies(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<NominalUnionDeclarationSyntax>())
            {
                if (!_unionDeclarations.TryGetValue(declaration.Identifier.Text, out var authored)
                    || !ReferenceEquals(authored, declaration)
                    || !_enumTypes.TryGetValue(declaration.Identifier.Text, out var unionType))
                {
                    continue;
                }

                bool isValid = true;
                if (declaration.Alternatives.Count < 2)
                {
                    Report("COPE-UNION-0002", "A nominal union declaration requires at least two alternatives.", declaration.Identifier);
                    isValid = false;
                }
                if (declaration.Alternatives.Count > 8)
                {
                    Report("COPE-UNION-0003", "A nominal union declaration supports at most 8 alternatives.", declaration.Alternatives[8]);
                    isValid = false;
                }

                var names = new HashSet<string>(StringComparer.Ordinal);
                var cases = new List<EnumCaseSymbol>();
                foreach (var alternative in declaration.Alternatives)
                {
                    if (!names.Add(alternative.Text))
                    {
                        Report("COPE-UNION-0004", $"Duplicate union alternative '{alternative.Text}'.", alternative);
                        isValid = false;
                        continue;
                    }

                    if (_recordTypes.TryGetValue(alternative.Text, out var recordType))
                    {
                        cases.Add(new EnumCaseSymbol(
                            alternative.Text,
                            unionType,
                            [new EnumPayloadFieldSymbol("value", recordType)]));
                        continue;
                    }

                    isValid = false;
                    if (_aliases.TryGetValue(alternative.Text, out var alias)
                        && alias.CanonicalType is RecordTypeSymbol canonicalRecord)
                    {
                        Report(
                            "COPE-UNION-0006",
                            $"Union alternatives must name nominal record declarations directly. '{alternative.Text}' is an alias of '{canonicalRecord.Name}'; use '{canonicalRecord.Name}'.",
                            alternative);
                    }
                    else if (_enumTypes.ContainsKey(alternative.Text))
                    {
                        Report("COPE-UNION-0007", $"Union alternative '{alternative.Text}' must name a nominal record declaration directly; enums and nominal unions are not allowed.", alternative);
                    }
                    else if (_interfaces.ContainsKey(alternative.Text)
                        || _tableTypes.ContainsKey(alternative.Text))
                    {
                        Report("COPE-UNION-0007", $"Union alternative '{alternative.Text}' must name a nominal record declaration directly.", alternative);
                    }
                    else
                    {
                        Report("COPE-UNION-0008", $"Unknown or unsupported union alternative '{alternative.Text}'. Union alternatives must name nominal record declarations directly.", alternative);
                    }
                }

                if (!isValid)
                {
                    continue;
                }

                unionType.UnionProvenance = new NominalUnionProvenance(
                    unionType.Name,
                    declaration.Alternatives.Select(alternative => alternative.Text).ToArray());
                foreach (var @case in cases)
                {
                    unionType.AddCase(@case);
                }
            }
        }

        private BoundFunctionDeclaration BindFunction(FunctionDeclarationSyntax s)
        {
            _global.TryLookup(s.Identifier.Text, out var sym);
            var fn = sym as FunctionSymbol ?? new FunctionSymbol(s.Identifier.Text, [], PrimitiveTypeSymbol.Error, stableIdentity: CreateFunctionStableIdentity(s.Identifier.Text));
            var prevFn = _currentFunction; _currentFunction = fn;
            var previousTypeParameters = _activeTypeParameters;
            _activeTypeParameters = CreateTypeParameterScope(fn.TypeParameters);
            var prev = _scope; _scope = new Scope(_global);
            foreach (var p in fn.Parameters)
            {
                if (!_scope.TryDeclare(p)) Report("COPE-BIND-0005", $"Duplicate parameter '{p.Name}'.", s.Identifier);
            }
            var body = (BoundBlockStatement)BindStatement(s.Body);
            _scope = prev; _currentFunction = prevFn; _activeTypeParameters = previousTypeParameters;
            return new BoundFunctionDeclaration(fn, body);
        }

        private BoundStatement BindStatement(StatementSyntax s) => s switch
        {
            BlockStatementSyntax b => BindBlock(b),
            VariableDeclarationStatementSyntax v => BindVariable(v),
            ExpressionStatementSyntax e => BindExpressionStatement(e),
            IfStatementSyntax i => BindIf(i),
            WhileStatementSyntax w => BindWhile(w),
            ForStatementSyntax f => BindFor(f),
            ReturnStatementSyntax r => BindReturn(r),
            BreakStatementSyntax b => BindBreak(b),
            ContinueStatementSyntax c => BindContinue(c),
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
            bool inferCallableReference = v.Type is null
                && v.Initializer is NameExpressionSyntax or GenericFunctionReferenceExpressionSyntax or CallExpressionSyntax;
            var type = inferCallableReference
                ? PrimitiveTypeSymbol.Error
                : BindType(v.Type, v.Identifier, "COPE-TYPE-0002", "variable");
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
                init = BindExpression(v.Initializer, inferCallableReference ? null : type);
            }
            if (inferCallableReference)
            {
                type = init.Type;
            }
            ValidateRuntimeValueType(type, v.Identifier, "variable");
            string? authoredAliasName = GetAuthoredAliasName(v.Type);
            if (!IsAssignable(type, init.Type))
            {
                ReportTypeMismatch("COPE-TYPE-0001", type, init.Type, v.Identifier, authoredAliasName);
            }
            var varSym = new VariableSymbol(
                v.Identifier.Text,
                type,
                v.Keyword.Kind == SyntaxKind.ConstKeyword,
                authoredAliasName);
            if (!_scope.TryDeclare(varSym)) Report("COPE-BIND-0002", $"Duplicate declaration '{varSym.Name}'.", v.Identifier);
            return new BoundVariableDeclaration(varSym, init);
        }

        private BoundStatement BindIf(IfStatementSyntax i)
            => new BoundIfStatement(EnsureBoolean(BindExpression(i.Condition), i.IfKeyword), BindStatement(i.ThenStatement), i.ElseStatement is null ? null : BindStatement(i.ElseStatement));

        private BoundStatement BindFor(ForStatementSyntax f)
        {
            var previousScope = _scope;
            _scope = new Scope(previousScope);
            _loopDepth++;
            try
            {
                BoundStatement? initializer = f.Initializer switch
                {
                    VariableDeclarationStatementSyntax v => BindVariable(v),
                    ExpressionSyntax e => new BoundExpressionStatement(BindExpression(e)),
                    _ => null
                };
                var condition = f.Condition is null ? null : EnsureBoolean(BindExpression(f.Condition), f.ForKeyword);
                var increment = f.Increment is null ? null : BindExpression(f.Increment);
                return new BoundForStatement(initializer, condition, increment, BindStatement(f.Body));
            }
            finally
            {
                _loopDepth--;
                _scope = previousScope;
            }
        }

        private RequirementSet BindRequirements(TypeParameterSyntax syntax)
        {
            if (syntax.ExtendsKeyword is null) return new RequirementSet([], []);
            var interfaces = new List<InterfaceSymbol>();
            var fields = new List<RequirementFieldSymbol>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (syntax.RequirementNames.Count > MaxRequirementInterfacesPerTypeParameter)
            {
                Report("COPE-REQUIREMENT-0009", $"Type parameter '{syntax.Identifier.Text}' exceeds the {MaxRequirementInterfacesPerTypeParameter} requirement-interface limit.", syntax.Identifier);
            }
            foreach (var operand in syntax.RequirementNames)
            {
                if (!_interfaces.TryGetValue(operand.Text, out var @interface))
                {
                    if (_aliases.ContainsKey(operand.Text)
                        || _recordTypes.ContainsKey(operand.Text)
                        || _enumTypes.ContainsKey(operand.Text)
                        || _tableTypes.ContainsKey(operand.Text))
                    {
                        Report("COPE-REQUIREMENT-0008", $"Constraint operand '{operand.Text}' is not an interface.", operand);
                    }
                    else
                    {
                        Report("COPE-REQUIREMENT-0001", $"Unknown interface requirement '{operand.Text}'.", operand);
                    }
                    continue;
                }
                if (!names.Add(@interface.Name))
                {
                    Report("COPE-REQUIREMENT-0002", $"Requirement '{@interface.Name}' is repeated.", operand);
                    continue;
                }
                interfaces.Add(@interface);
                foreach (var field in @interface.Fields)
                {
                    var existing = fields.FirstOrDefault(candidate => candidate.Name == field.Name);
                    if (existing is null)
                    {
                        fields.Add(field);
                    }
                    else if (!TypeFacts.AreEquivalent(existing.Type, field.Type))
                    {
                        Report("COPE-REQUIREMENT-0003", $"Requirements conflict on field '{field.Name}': '{existing.Type.Name}' versus '{field.Type.Name}'.", operand);
                    }
                }
            }
            if (fields.Count > MaxNormalizedRequirementFields)
            {
                Report("COPE-REQUIREMENT-0010", $"Type parameter '{syntax.Identifier.Text}' exceeds the {MaxNormalizedRequirementFields} normalized requirement-field limit.", syntax.Identifier);
            }
            return new RequirementSet(interfaces, fields);
        }

        private BoundStatement BindWhile(WhileStatementSyntax statement)
        {
            var condition = EnsureBoolean(BindExpression(statement.Condition), statement.WhileKeyword);
            _loopDepth++;
            try
            {
                return new BoundWhileStatement(condition, BindStatement(statement.Body));
            }
            finally
            {
                _loopDepth--;
            }
        }

        private BoundStatement BindBreak(BreakStatementSyntax statement)
        {
            if (_loopDepth == 0)
            {
                Report("COPE-CFLOW-0001", "'break' is valid only inside a loop.", statement.BreakKeyword);
            }

            return new BoundBreakStatement();
        }

        private BoundStatement BindContinue(ContinueStatementSyntax statement)
        {
            if (_loopDepth == 0)
            {
                Report("COPE-CFLOW-0002", "'continue' is valid only inside a loop.", statement.ContinueKeyword);
            }

            return new BoundContinueStatement();
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
                expr = InjectDirectNominalUnionCase(expr, result.SuccessType);
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
            else if (!IsAssignable(expected, expr.Type))
            {
                ReportTypeMismatch(
                    "COPE-TYPE-0003",
                    expected,
                    expr.Type,
                    r.ReturnKeyword,
                    _currentFunction?.AuthoredReturnAliasName);
            }
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
                GenericCallExpressionSyntax c => BindGenericCall(c, contextualType),
                GenericFunctionReferenceExpressionSyntax reference => BindGenericFunctionReference(reference),
                ArrayLiteralExpressionSyntax a => BindArray(a, contextualType),
                ObjectLiteralExpressionSyntax o => BindObject(o, contextualType),
                MemberAccessExpressionSyntax m => BindMember(m),
                IndexExpressionSyntax i => BindIndex(i),
                WithExpressionSyntax w => BindWith(w),
                IfExpressionSyntax i => BindIfExpression(i, contextualType),
                MatchExpressionSyntax m => BindMatch(m, contextualType),
                _ => new BoundErrorExpression()
            };

            return InjectDirectNominalUnionCase(expression, contextualType);
        }

        private static BoundExpression InjectDirectNominalUnionCase(BoundExpression expression, TypeSymbol? contextualType)
        {
            if (contextualType is not EnumTypeSymbol { UnionProvenance: not null } unionType
                || expression.Type is not RecordTypeSymbol recordType)
            {
                return expression;
            }

            EnumCaseSymbol? matchingCase = unionType.Cases.FirstOrDefault(@case =>
                @case.PayloadFields.Count == 1
                && ReferenceEquals(@case.PayloadFields[0].Type, recordType));
            return matchingCase is null
                ? expression
                : new BoundEnumValueExpression(matchingCase, [expression]);
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
                if (_aliases.ContainsKey(n.IdentifierToken.Text))
                {
                    Report(
                        "COPE-ALIAS-0006",
                        $"Type alias '{n.IdentifierToken.Text}' cannot be used as a runtime value or constructor.",
                        n.IdentifierToken);
                    return new BoundErrorExpression();
                }

                Report("COPE-BIND-0001", $"Undefined name '{n.IdentifierToken.Text}'.", n.IdentifierToken);
                return new BoundErrorExpression();
            }
            return symbol switch
            {
                VariableSymbol v when v.Type is TableTypeSymbol table && _tableSingletonVariables.Contains(v) => new BoundTableReferenceExpression(table),
                VariableSymbol v => new BoundVariableExpression(v),
                ParameterSymbol p => new BoundVariableExpression(new VariableSymbol(p.Name, p.Type, true)),
                FunctionSymbol function when function.IsGeneric => ReportOpenGenericFunctionValue(n),
                FunctionSymbol function => new BoundFunctionReferenceExpression(function),
                _ => new BoundErrorExpression()
            };
        }

        private BoundExpression ReportOpenGenericFunctionValue(NameExpressionSyntax name)
        {
            Report("COPE-CALL-0003", $"Generic function '{name.IdentifierToken.Text}' must be explicitly closed before it can be used as a callable value.", name.IdentifierToken);
            return new BoundErrorExpression();
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
                if (l.Type is CallableTypeSymbol || r.Type is CallableTypeSymbol)
                {
                    Report("COPE-CALL-0008", "Callable equality is not supported.", b.OperatorToken);
                    return new BoundErrorExpression();
                }
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
            if (!IsAssignable(variable.Type, expr.Type))
            {
                ReportTypeMismatch(
                    "COPE-TYPE-0001",
                    variable.Type,
                    expr.Type,
                    a.EqualsToken,
                    variable.AuthoredAliasName);
            }
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

            if (c.Target is MemberAccessExpressionSyntax aliasMember
                && aliasMember.Target is NameExpressionSyntax aliasName
                && _aliases.ContainsKey(aliasName.IdentifierToken.Text)
                && !_scope.TryLookup(aliasName.IdentifierToken.Text, out _))
            {
                Report(
                    "COPE-ALIAS-0006",
                    $"Type alias '{aliasName.IdentifierToken.Text}' cannot be used as a runtime value or constructor.",
                    aliasName.IdentifierToken);
                return new BoundErrorExpression();
            }

            if (c.Target is MemberAccessExpressionSyntax m && m.Target is NameExpressionSyntax enumName)
            {
                return BindEnumConstructorCall(c, m, enumName);
            }

            if (c.Target is not NameExpressionSyntax)
            {
                return BindInvoke(c, BindExpression(c.Target));
            }

            var targetName = (NameExpressionSyntax)c.Target;
            if (_scope.TryLookup(targetName.IdentifierToken.Text, out var lexicalSymbol)
                && lexicalSymbol is VariableSymbol or ParameterSymbol)
            {
                return BindInvoke(c, BindName(targetName));
            }

            if (!_scope.TryLookup(targetName.IdentifierToken.Text, out var s)
                || s is null)
            {
                if (_aliases.ContainsKey(targetName.IdentifierToken.Text))
                {
                    Report(
                        "COPE-ALIAS-0006",
                        $"Type alias '{targetName.IdentifierToken.Text}' cannot be used as a runtime value or constructor.",
                        targetName.IdentifierToken);
                    return new BoundErrorExpression();
                }

                Report("COPE-BIND-0001", "Undefined function name.", c.OpenParenToken);
                return new BoundErrorExpression();
            }
            if (s is not FunctionSymbol fn) { Report("COPE-BIND-0006", $"Cannot call non-function '{s.Name}'.", c.OpenParenToken); return new BoundErrorExpression(); }
            if (fn.IsGeneric) return BindInferredGenericCall(c, fn);
            if (c.Arguments.Count != fn.Parameters.Count) Report("COPE-TYPE-0004", $"Argument count mismatch: expected {fn.Parameters.Count}, got {c.Arguments.Count}.", c.OpenParenToken);
            var args = c.Arguments.Select((a, index) => BindExpression(a, index < fn.Parameters.Count ? fn.Parameters[index].Type : null)).ToArray();
            for (var i = 0; i < Math.Min(args.Length, fn.Parameters.Count); i++)
                if (!IsAssignable(fn.Parameters[i].Type, args[i].Type))
                {
                    ReportTypeMismatch(
                        "COPE-TYPE-0005",
                        fn.Parameters[i].Type,
                        args[i].Type,
                        c.Arguments[i] is LiteralExpressionSyntax literal ? literal.LiteralToken : c.OpenParenToken,
                        fn.Parameters[i].AuthoredAliasName);
                }
            return new BoundCallExpression(fn, args);
        }

        private BoundExpression BindInvoke(CallExpressionSyntax call, BoundExpression callee)
        {
            if (callee.Type is not CallableTypeSymbol callable)
            {
                foreach (var argument in call.Arguments) _ = BindExpression(argument);
                Report("COPE-CALL-0004", $"Cannot invoke non-callable value of type '{callee.Type.Name}'.", call.OpenParenToken);
                return new BoundErrorExpression();
            }

            if (call.Arguments.Count != callable.Parameters.Count)
            {
                Report("COPE-CALL-0005", $"Callable invocation argument count mismatch: expected {callable.Parameters.Count}, got {call.Arguments.Count}.", call.OpenParenToken);
            }

            var arguments = call.Arguments
                .Select((argument, index) => BindExpression(argument, index < callable.Parameters.Count ? callable.Parameters[index].Type : null))
                .ToArray();
            for (var index = 0; index < Math.Min(arguments.Length, callable.Parameters.Count); index++)
            {
                if (!IsAssignable(callable.Parameters[index].Type, arguments[index].Type))
                {
                    ReportTypeMismatch("COPE-CALL-0006", callable.Parameters[index].Type, arguments[index].Type, InferenceAnchor(call.Arguments[index]));
                }
            }

            return new BoundInvokeExpression(callee, arguments, callable);
        }

        private sealed class InferenceSlot(TypeParameterSymbol parameter)
        {
            public TypeParameterSymbol Parameter { get; } = parameter;
            public TypeSymbol? Candidate { get; private set; }
            public SyntaxToken? FirstEvidence { get; private set; }
            public int EvidenceCount { get; private set; }
            public bool HasConflict { get; private set; }

            public bool AddEvidence(TypeSymbol candidate, SyntaxToken evidence, BinderImpl binder)
            {
                if (Candidate is null)
                {
                    Candidate = candidate;
                    FirstEvidence = evidence;
                    EvidenceCount = 1;
                    return true;
                }

                if (!TypeFacts.AreEquivalent(Candidate, candidate))
                {
                    if (!HasConflict)
                    {
                        binder.Report(
                            "COPE-INFER-0002",
                            $"Conflicting inference for '{Parameter.Name}': argument at {FirstEvidence!.Position} gives canonical '{Candidate.Name}', but argument at {evidence.Position} gives canonical '{candidate.Name}'. Use explicit type arguments only if one concrete type makes every argument valid.",
                            evidence);
                        HasConflict = true;
                    }

                    return false;
                }

                EvidenceCount++;
                if (EvidenceCount > MaxInferenceEvidencePerTypeParameter)
                {
                    binder.Report(
                        "COPE-INFER-0007",
                        $"Inference for '{Parameter.Name}' exceeded the {MaxInferenceEvidencePerTypeParameter} evidence-entry limit.",
                        evidence);
                    return false;
                }

                return true;
            }
        }

        private BoundExpression BindInferredGenericCall(CallExpressionSyntax call, FunctionSymbol function)
        {
            if (_currentFunction?.IsGeneric == true)
            {
                string diagnosticId = _currentFunction.Name == function.Name ? "COPE-GENERIC-0014" : "COPE-GENERIC-0006";
                string message = diagnosticId == "COPE-GENERIC-0014"
                    ? $"Generic recursion through '{function.Name}' is not supported in CTS-TYPE-M1b."
                    : "Generic-to-generic calls are not supported in CTS-TYPE-M1b.";
                Report(diagnosticId, message, call.OpenParenToken);
                return new BoundErrorExpression();
            }

            if (call.Arguments.Count != function.Parameters.Count)
            {
                Report("COPE-TYPE-0004", $"Argument count mismatch: expected {function.Parameters.Count}, got {call.Arguments.Count}.", call.OpenParenToken);
                return new BoundErrorExpression();
            }

            var slots = function.TypeParameters.Select(parameter => new InferenceSlot(parameter)).ToArray();
            var arguments = new BoundExpression[call.Arguments.Count];
            var deferred = new List<int>();
            bool failed = false;

            for (var index = 0; index < call.Arguments.Count; index++)
            {
                ExpressionSyntax argument = call.Arguments[index];
                if (RequiresInferenceContext(argument))
                {
                    deferred.Add(index);
                    continue;
                }

                BoundExpression bound = BindExpression(argument);
                arguments[index] = bound;
                failed |= !CollectInferenceEvidence(function.Parameters[index].Type, bound.Type, slots, InferenceAnchor(argument));
            }

            var unresolved = slots.Where(slot => slot.Candidate is null).ToArray();
            if (unresolved.Length > 0)
            {
                string names = string.Join(", ", unresolved.Select(slot => slot.Parameter.Name));
                string explicitArguments = string.Join(", ", function.TypeParameters.Select(parameter => parameter.Name));
                SyntaxToken anchor = deferred.Count > 0 ? InferenceAnchor(call.Arguments[deferred[0]]) : call.OpenParenToken;
                string contextDetail = deferred.Count > 0 ? DescribeMissingContext(call.Arguments[deferred[0]]) + " " : string.Empty;
                Report(
                    "COPE-INFER-0001",
                    $"Cannot infer type parameter{(unresolved.Length == 1 ? string.Empty : "s")} {names} for '{function.Name}'. {contextDetail}Provide explicit arguments, for example '{function.Name}<{explicitArguments}>(...)'.",
                    anchor);
                return new BoundErrorExpression();
            }

            if (failed || slots.Any(slot => slot.HasConflict)) return new BoundErrorExpression();

            TypeSymbol[] typeArguments = slots.Select(slot => slot.Candidate!).ToArray();
            try
            {
                foreach (TypeSymbol typeArgument in typeArguments)
                {
                    ValidateClosedTypeDepth(typeArgument, MaxClosedTypeDepth);
                }
            }
            catch (InvalidOperationException)
            {
                Report("COPE-GENERIC-0015", $"Generic instantiation exceeded the closed-type nesting limit of {MaxClosedTypeDepth}.", call.OpenParenToken);
                return new BoundErrorExpression();
            }

            for (var index = 0; index < typeArguments.Length; index++)
            {
                if (!Satisfies(function.TypeParameters[index].Requirements, typeArguments[index], call.OpenParenToken))
                {
                    return new BoundErrorExpression();
                }
            }

            BoundFunctionDeclaration specialization = GetOrCreateClosedInstantiation(function, typeArguments, call.OpenParenToken);
            if (specialization.Symbol.Name == "<error>") return new BoundErrorExpression();

            foreach (int index in deferred)
            {
                arguments[index] = BindExpression(call.Arguments[index], specialization.Symbol.Parameters[index].Type);
            }

            for (var index = 0; index < arguments.Length; index++)
            {
                if (!IsAssignable(specialization.Symbol.Parameters[index].Type, arguments[index].Type))
                {
                    ReportTypeMismatch("COPE-TYPE-0005", specialization.Symbol.Parameters[index].Type, arguments[index].Type, InferenceAnchor(call.Arguments[index]));
                    failed = true;
                }
            }

            return failed ? new BoundErrorExpression() : new BoundCallExpression(specialization.Symbol, arguments);
        }

        private bool CollectInferenceEvidence(TypeSymbol pattern, TypeSymbol actual, IReadOnlyList<InferenceSlot> slots, SyntaxToken anchor)
        {
            var worklist = new Stack<(TypeSymbol Pattern, TypeSymbol Actual, int Depth)>();
            worklist.Push((pattern, actual, 0));
            int steps = 0;
            bool succeeded = true;

            while (worklist.Count > 0)
            {
                var item = worklist.Pop();
                if (++steps > MaxInferenceMatchSteps)
                {
                    Report("COPE-INFER-0006", $"Generic inference exceeded the {MaxInferenceMatchSteps} structural matching-step limit.", anchor);
                    return false;
                }
                if (item.Depth > MaxInferenceMatchDepth)
                {
                    Report("COPE-INFER-0005", $"Generic inference exceeded the {MaxInferenceMatchDepth} structural matching-depth limit.", anchor);
                    return false;
                }

                if (item.Pattern is TypeParameterTypeSymbol typeParameter)
                {
                    InferenceSlot? slot = slots.FirstOrDefault(candidate => ReferenceEquals(candidate.Parameter.Type, typeParameter));
                    if (slot is null)
                    {
                        Report("COPE-INFER-0003", $"Generic inference encountered unsupported open type parameter '{typeParameter.Name}'.", anchor);
                        return false;
                    }
                    succeeded &= slot.AddEvidence(item.Actual, anchor, this);
                    continue;
                }

                switch (item.Pattern, item.Actual)
                {
                    case (ArrayTypeSymbol expected, ArrayTypeSymbol received):
                        worklist.Push((expected.ElementType, received.ElementType, item.Depth + 1));
                        break;
                    case (ResultTypeSymbol expected, ResultTypeSymbol received):
                        worklist.Push((expected.ErrorType, received.ErrorType, item.Depth + 1));
                        worklist.Push((expected.SuccessType, received.SuccessType, item.Depth + 1));
                        break;
                    case (ColumnTypeSymbol expected, ColumnTypeSymbol received):
                        worklist.Push((expected.ElementType, received.ElementType, item.Depth + 1));
                        break;
                    default:
                        if (!TypeFacts.AreEquivalent(item.Pattern, item.Actual))
                        {
                            Report("COPE-INFER-0003", $"Generic inference structural mismatch: parameter pattern '{item.Pattern.Name}' does not exactly match argument type '{item.Actual.Name}'.", anchor);
                            return false;
                        }
                        break;
                }
            }

            return succeeded;
        }

        private static bool RequiresInferenceContext(ExpressionSyntax expression)
            => expression is ObjectLiteralExpressionSyntax
                || expression is ArrayLiteralExpressionSyntax { Elements.Count: 0 }
                || expression is CallExpressionSyntax
                {
                    Target: NameExpressionSyntax { IdentifierToken.Text: "ok" or "err" }
                };

        private static string DescribeMissingContext(ExpressionSyntax expression)
            => expression switch
            {
                ArrayLiteralExpressionSyntax => "An empty array provides no element-type evidence.",
                ObjectLiteralExpressionSyntax => "A record literal provides no nominal-type evidence.",
                CallExpressionSyntax => "A bare Result constructor provides incomplete Result evidence.",
                _ => "This argument requires a contextual parameter type."
            };

        private static SyntaxToken InferenceAnchor(ExpressionSyntax expression)
            => expression switch
            {
                ArrayLiteralExpressionSyntax array => array.OpenBracketToken,
                ObjectLiteralExpressionSyntax record => record.OpenBraceToken,
                CallExpressionSyntax call => call.OpenParenToken,
                LiteralExpressionSyntax literal => literal.LiteralToken,
                NameExpressionSyntax name => name.IdentifierToken,
                _ => expression.GetChildren().OfType<SyntaxToken>().FirstOrDefault()
                    ?? new SyntaxToken(SyntaxKind.BadToken, 0, "?", null)
            };

        private BoundExpression BindGenericCall(GenericCallExpressionSyntax call, TypeSymbol? contextualType)
        {
            if (call.Target is not NameExpressionSyntax name
                || !_global.TryLookup(name.IdentifierToken.Text, out var symbol)
                || symbol is not FunctionSymbol function)
            {
                Report("COPE-GENERIC-0004", "Explicit type arguments require a named function.", call.LessToken);
                return new BoundErrorExpression();
            }
            if (!function.IsGeneric)
            {
                Report("COPE-GENERIC-0005", $"Function '{function.Name}' does not accept type arguments.", call.LessToken);
                return new BoundErrorExpression();
            }
            if (_currentFunction?.IsGeneric == true)
            {
                string diagnosticId = _currentFunction.Name == function.Name
                    ? "COPE-GENERIC-0014"
                    : "COPE-GENERIC-0006";
                string message = diagnosticId == "COPE-GENERIC-0014"
                    ? $"Generic recursion through '{function.Name}' is not supported in CTS-TYPE-M1b."
                    : "Generic-to-generic calls are not supported in CTS-TYPE-M1b.";
                Report(diagnosticId, message, call.LessToken);
                return new BoundErrorExpression();
            }
            if (call.TypeArguments.Count != function.TypeParameters.Count)
            {
                Report("COPE-GENERIC-0007", $"Generic function '{function.Name}' expects {function.TypeParameters.Count} type arguments, got {call.TypeArguments.Count}.", call.LessToken);
                return new BoundErrorExpression();
            }
            if (call.TypeArguments.Any(argument => argument is IdentifierTypeSyntax identifier && _interfaces.ContainsKey(identifier.Identifier.Text)))
            {
                foreach (var argument in call.TypeArguments.OfType<IdentifierTypeSyntax>().Where(argument => _interfaces.ContainsKey(argument.Identifier.Text)))
                {
                    Report("COPE-GENERIC-0008", $"Interface '{argument.Identifier.Text}' cannot be used as a generic type argument.", argument.Identifier);
                }

                return new BoundErrorExpression();
            }
            var typeArguments = call.TypeArguments.Select(argument => BindType(argument, call.LessToken, "COPE-GENERIC-0008", "type argument")).ToArray();
            if (typeArguments.Any(IsOpenOrIllegalTypeArgument))
            {
                Report("COPE-GENERIC-0008", "Generic type arguments must be closed value types; interfaces and open type parameters are not allowed.", call.LessToken);
                return new BoundErrorExpression();
            }
            try
            {
                foreach (var typeArgument in typeArguments)
                {
                    ValidateClosedTypeDepth(typeArgument, MaxClosedTypeDepth);
                }
            }
            catch (InvalidOperationException)
            {
                Report("COPE-GENERIC-0015", $"Generic instantiation exceeded the closed-type nesting limit of {MaxClosedTypeDepth}.", call.LessToken);
                return new BoundErrorExpression();
            }
            for (var index = 0; index < typeArguments.Length; index++)
            {
                if (!Satisfies(function.TypeParameters[index].Requirements, typeArguments[index], call.LessToken))
                {
                    return new BoundErrorExpression();
                }
            }
            var specialization = GetOrCreateClosedInstantiation(function, typeArguments, call.LessToken);
            var arguments = call.Arguments.Select((argument, index) => BindExpression(argument, index < specialization.Symbol.Parameters.Count ? specialization.Symbol.Parameters[index].Type : null)).ToArray();
            if (arguments.Length != specialization.Symbol.Parameters.Count)
            {
                Report("COPE-TYPE-0004", $"Argument count mismatch: expected {specialization.Symbol.Parameters.Count}, got {arguments.Length}.", call.OpenParenToken);
            }
            for (var index = 0; index < Math.Min(arguments.Length, specialization.Symbol.Parameters.Count); index++)
            {
                if (!IsAssignable(specialization.Symbol.Parameters[index].Type, arguments[index].Type))
                {
                    ReportTypeMismatch("COPE-TYPE-0005", specialization.Symbol.Parameters[index].Type, arguments[index].Type, call.OpenParenToken);
                }
            }
            return new BoundCallExpression(specialization.Symbol, arguments);
        }

        private BoundExpression BindGenericFunctionReference(GenericFunctionReferenceExpressionSyntax reference)
        {
            if (reference.Target is not NameExpressionSyntax name
                || !_scope.TryLookup(name.IdentifierToken.Text, out var symbol)
                || symbol is not FunctionSymbol function)
            {
                Report("COPE-GENERIC-0004", "Explicit type arguments require an unshadowed named function.", reference.LessToken);
                return new BoundErrorExpression();
            }
            if (!function.IsGeneric)
            {
                Report("COPE-GENERIC-0005", $"Function '{function.Name}' does not accept type arguments.", reference.LessToken);
                return new BoundErrorExpression();
            }
            if (reference.TypeArguments.Count != function.TypeParameters.Count)
            {
                Report("COPE-GENERIC-0007", $"Generic function '{function.Name}' expects {function.TypeParameters.Count} type arguments, got {reference.TypeArguments.Count}.", reference.LessToken);
                return new BoundErrorExpression();
            }

            var typeArguments = reference.TypeArguments
                .Select(argument => BindType(argument, reference.LessToken, "COPE-GENERIC-0008", "type argument"))
                .ToArray();
            if (typeArguments.Any(IsOpenOrIllegalTypeArgument))
            {
                Report("COPE-GENERIC-0008", "Generic type arguments must be closed value types; interfaces and open type parameters are not allowed.", reference.LessToken);
                return new BoundErrorExpression();
            }
            try
            {
                foreach (var argument in typeArguments) ValidateClosedTypeDepth(argument, MaxClosedTypeDepth);
            }
            catch (InvalidOperationException)
            {
                Report("COPE-GENERIC-0015", $"Generic instantiation exceeded the closed-type nesting limit of {MaxClosedTypeDepth}.", reference.LessToken);
                return new BoundErrorExpression();
            }
            for (var index = 0; index < typeArguments.Length; index++)
            {
                if (!Satisfies(function.TypeParameters[index].Requirements, typeArguments[index], reference.LessToken)) return new BoundErrorExpression();
            }

            var specialization = GetOrCreateClosedInstantiation(function, typeArguments, reference.LessToken);
            return specialization.Symbol.Name == "<error>"
                ? new BoundErrorExpression()
                : new BoundFunctionReferenceExpression(specialization.Symbol);
        }

        private bool Satisfies(RequirementSet requirements, TypeSymbol candidate, SyntaxToken anchor)
        {
            if (requirements.Fields.Count == 0) return true;
            IReadOnlyList<(string Name, TypeSymbol Type)> fields = candidate switch
            {
                RecordTypeSymbol record => record.Fields.Select(field => (field.Name, field.Type)).ToArray(),
                TableRowTypeSymbol row => row.Fields.Select(field => (field.Name, field.Type)).ToArray(),
                _ => []
            };
            if (fields.Count == 0)
            {
                Report("COPE-REQUIREMENT-0005", $"Type '{candidate.Name}' cannot satisfy field requirements.", anchor);
                return false;
            }
            var missingFields = new List<RequirementFieldSymbol>();
            var mismatchedFields = new List<(RequirementFieldSymbol Requirement, TypeSymbol ActualType)>();
            foreach (var requirement in requirements.Fields)
            {
                var actual = fields.FirstOrDefault(field => field.Name == requirement.Name);
                if (actual.Name is null)
                {
                    missingFields.Add(requirement);
                    continue;
                }
                if (!TypeFacts.AreEquivalent(requirement.Type, actual.Type))
                {
                    mismatchedFields.Add((requirement, actual.Type));
                }
            }
            if (missingFields.Count > 0)
            {
                Report(
                    "COPE-REQUIREMENT-0006",
                    BuildRequirementFieldListMessage(
                        $"Type '{candidate.Name}' is missing required field",
                        missingFields.Select(field => $"{field.Name}: {field.Type.Name}").ToArray()),
                    anchor);
            }

            if (mismatchedFields.Count > 0)
            {
                var descriptions = mismatchedFields
                    .Select(item => $"{item.Requirement.Name}: required {item.Requirement.Type.Name}, actual {item.ActualType.Name}")
                    .ToArray();
                Report(
                    "COPE-REQUIREMENT-0007",
                    BuildRequirementFieldListMessage(
                        $"Type '{candidate.Name}' has incompatible required field",
                        descriptions),
                    anchor);
            }

            return missingFields.Count == 0 && mismatchedFields.Count == 0;
        }

        private static bool IsOpenOrIllegalTypeArgument(TypeSymbol type)
        {
            return type switch
            {
                TypeParameterTypeSymbol => true,
                ArrayTypeSymbol array => IsOpenOrIllegalTypeArgument(array.ElementType),
                ResultTypeSymbol result => IsOpenOrIllegalTypeArgument(result.SuccessType) || IsOpenOrIllegalTypeArgument(result.ErrorType),
                _ => false
            };
        }

        private BoundFunctionDeclaration GetOrCreateClosedInstantiation(FunctionSymbol generic, IReadOnlyList<TypeSymbol> typeArguments, SyntaxToken anchor)
        {
            string identity;
            try
            {
                identity = generic.StableIdentity + "<" + string.Join(",", typeArguments.Select(type => ClosedTypeIdentity(type, MaxClosedTypeDepth))) + ">";
            }
            catch (InvalidOperationException)
            {
                Report("COPE-GENERIC-0015", $"Generic instantiation exceeded the closed-type nesting limit of {MaxClosedTypeDepth}.", anchor);
                return new BoundFunctionDeclaration(new FunctionSymbol("<error>", [], PrimitiveTypeSymbol.Error), new BoundBlockStatement([]));
            }
            if (_closedInstantiations.TryGetValue(identity, out var existing)) return existing;
            if (_closedInstantiationCounts.TryGetValue(generic, out var perGenericCount)
                && perGenericCount >= MaxClosedInstantiationsPerGenericDefinition)
            {
                Report("COPE-GENERIC-0012", $"Generic function '{generic.Name}' exceeded the {MaxClosedInstantiationsPerGenericDefinition} closed-instantiation limit.", anchor);
                return new BoundFunctionDeclaration(new FunctionSymbol("<error>", [], PrimitiveTypeSymbol.Error), new BoundBlockStatement([]));
            }
            if (_closedInstantiations.Count >= MaxClosedInstantiationsPerCompilation)
            {
                Report("COPE-GENERIC-0009", $"The compilation exceeded the {MaxClosedInstantiationsPerCompilation} closed generic instantiation limit.", anchor);
                return new BoundFunctionDeclaration(new FunctionSymbol("<error>", [], PrimitiveTypeSymbol.Error), new BoundBlockStatement([]));
            }
            if (!_genericBodies.TryGetValue(generic, out var openBody))
            {
                Report("COPE-GENERIC-0010", $"Generic function '{generic.Name}' is not available for closed instantiation.", anchor);
                return new BoundFunctionDeclaration(new FunctionSymbol("<error>", [], PrimitiveTypeSymbol.Error), new BoundBlockStatement([]));
            }
            var substitutions = generic.TypeParameters
                .Select((parameter, index) => (Open: (TypeSymbol)parameter.Type, Closed: typeArguments[index]))
                .ToDictionary(pair => pair.Open, pair => pair.Closed);
            var specializedParameters = generic.Parameters
                .Select(parameter => new ParameterSymbol(parameter.Name, SubstituteType(parameter.Type, substitutions), parameter.AuthoredAliasName))
                .ToArray();
            var specializedName = CreateSpecializationName(generic, identity, typeArguments);
            var specializedSymbol = new FunctionSymbol(
                specializedName,
                specializedParameters,
                SubstituteType(generic.ReturnType, substitutions),
                generic.AuthoredReturnAliasName,
                identity);
            var rewriter = new ClosedInstantiationRewriter(substitutions);
            var specialized = new BoundFunctionDeclaration(specializedSymbol, rewriter.RewriteBlock(openBody.Body));
            _closedInstantiations.Add(identity, specialized);
            _closedInstantiationCounts[generic] = _closedInstantiationCounts.TryGetValue(generic, out perGenericCount)
                ? perGenericCount + 1
                : 1;
            _functions.Add(specialized);
            return specialized;
        }

        private string CreateSpecializationName(FunctionSymbol generic, string specializationIdentity, IReadOnlyList<TypeSymbol> typeArguments)
        {
            string displaySuffix = string.Join("_", typeArguments.Select(type => ClosedTypeIdentifier(type, MaxClosedTypeDepth)));
            string hash = ComputeStableHashHex(specializationIdentity);
            foreach (int suffixLength in new[] { 16, 24, 32, hash.Length })
            {
                string specializedName = generic.Name + "__" + displaySuffix + "__" + hash[..suffixLength];
                if (!_closedInstantiationNames.TryGetValue(specializedName, out var existingIdentity)
                    || string.Equals(existingIdentity, specializationIdentity, StringComparison.Ordinal))
                {
                    _closedInstantiationNames[specializedName] = specializationIdentity;
                    return specializedName;
                }
            }

            // A full SHA-256 collision is not expected, but a valid program must never fail
            // because a display hash is ambiguous. The canonical semantic identity is injective.
            string escapedIdentity = Convert.ToHexString(Encoding.UTF8.GetBytes(specializationIdentity));
            string fallbackName = generic.Name + "__" + displaySuffix + "__identity_" + escapedIdentity;
            _closedInstantiationNames[fallbackName] = specializationIdentity;
            return fallbackName;
        }

        private static string ClosedTypeIdentity(TypeSymbol type, int depthRemaining)
        {
            if (depthRemaining <= 0)
            {
                throw new InvalidOperationException("Closed type identity exceeded the configured nesting limit.");
            }

            return type switch
            {
                PrimitiveTypeSymbol primitive => "primitive:" + primitive.Name,
                ErrorNominalTypeSymbol error => "error:" + error.Name,
                EnumTypeSymbol @enum => "enum:" + (@enum.StableIdentity ?? @enum.Name),
                RecordTypeSymbol record => "record:" + (record.StableIdentity ?? record.Name),
                TableTypeSymbol table => "table:" + table.StableIdentity,
                TableRowTypeSymbol row => "row:" + row.StableIdentity,
                ColumnTypeSymbol column => "column(" + ClosedTypeIdentity(column.ElementType, depthRemaining - 1) + ")",
                ArrayTypeSymbol array => "array(" + ClosedTypeIdentity(array.ElementType, depthRemaining - 1) + ")",
                ResultTypeSymbol result => "result(" + ClosedTypeIdentity(result.SuccessType, depthRemaining - 1) + "," + ClosedTypeIdentity(result.ErrorType, depthRemaining - 1) + ")",
                CallableTypeSymbol callable => "callable(" + string.Join(",", callable.Parameters.Select(parameter => ClosedTypeIdentity(parameter.Type, depthRemaining - 1))) + ")->" + ClosedTypeIdentity(callable.ReturnType, depthRemaining - 1),
                _ => "type:" + type.Name
            };
        }

        private static string ClosedTypeIdentifier(TypeSymbol type, int depthRemaining)
        {
            var identity = ClosedTypeIdentity(type, depthRemaining);
            var builder = new StringBuilder(identity.Length);
            foreach (var ch in identity)
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }

            return builder.ToString();
        }

        private static string ComputeStableHashHex(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        internal static IReadOnlyDictionary<string, string> AllocateSpecializationNamesForTesting(
            string genericName,
            string displaySuffix,
            IEnumerable<string> semanticIdentities,
            Func<string, string>? hashProvider = null)
        {
            hashProvider ??= ComputeStableHashHex;
            var remaining = semanticIdentities
                .Distinct(StringComparer.Ordinal)
                .OrderBy(identity => identity, StringComparer.Ordinal)
                .ToList();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (int suffixLength in new[] { 16, 24, 32, 64 })
            {
                var groups = remaining.GroupBy(
                    identity => genericName + "__" + displaySuffix + "__" + hashProvider(identity)[..suffixLength],
                    StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToArray();
                remaining.Clear();

                foreach (var group in groups)
                {
                    if (group.Count() == 1)
                    {
                        names.Add(group.Single(), group.Key);
                    }
                    else if (suffixLength == 64)
                    {
                        foreach (string identity in group.OrderBy(identity => identity, StringComparer.Ordinal))
                        {
                            string escapedIdentity = Convert.ToHexString(Encoding.UTF8.GetBytes(identity));
                            names.Add(identity, group.Key + "__identity_" + escapedIdentity);
                        }
                    }
                    else
                    {
                        remaining.AddRange(group.OrderBy(identity => identity, StringComparer.Ordinal));
                    }
                }

                if (remaining.Count == 0)
                {
                    break;
                }
            }

            return names;
        }

        private static string CreateFunctionStableIdentity(string name)
            => "function:" + name;

        private static Dictionary<string, TypeParameterSymbol> CreateTypeParameterScope(IReadOnlyList<TypeParameterSymbol> typeParameters)
        {
            var scope = new Dictionary<string, TypeParameterSymbol>(StringComparer.Ordinal);
            foreach (var parameter in typeParameters)
            {
                if (!scope.ContainsKey(parameter.Name))
                {
                    scope.Add(parameter.Name, parameter);
                }
            }

            return scope;
        }

        private static void ValidateClosedTypeDepth(TypeSymbol type, int depthRemaining)
        {
            if (depthRemaining <= 0)
            {
                throw new InvalidOperationException("Closed type nesting exceeded the configured limit.");
            }

            switch (type)
            {
                case ArrayTypeSymbol array:
                    ValidateClosedTypeDepth(array.ElementType, depthRemaining - 1);
                    break;
                case ResultTypeSymbol result:
                    ValidateClosedTypeDepth(result.SuccessType, depthRemaining - 1);
                    ValidateClosedTypeDepth(result.ErrorType, depthRemaining - 1);
                    break;
                case ColumnTypeSymbol column:
                    ValidateClosedTypeDepth(column.ElementType, depthRemaining - 1);
                    break;
                case CallableTypeSymbol callable:
                    foreach (var parameter in callable.Parameters) ValidateClosedTypeDepth(parameter.Type, depthRemaining - 1);
                    ValidateClosedTypeDepth(callable.ReturnType, depthRemaining - 1);
                    break;
            }
        }

        private static string BuildRequirementFieldListMessage(string prefix, IReadOnlyList<string> descriptions)
        {
            var shown = descriptions.Take(MaxDiagnosticRequirementFields).ToArray();
            var builder = new StringBuilder();
            builder.Append(prefix);
            if (shown.Length == 1)
            {
                builder.Append(' ').Append('\'').Append(shown[0]).Append('\'');
            }
            else
            {
                builder.Append("s: ");
                builder.Append(string.Join("; ", shown));
            }

            if (descriptions.Count > shown.Length)
            {
                builder.Append(" (+").Append(descriptions.Count - shown.Length).Append(" more)");
            }

            builder.Append('.');
            return builder.ToString();
        }

        private static TypeSymbol SubstituteType(TypeSymbol type, IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions)
        {
            if (substitutions.TryGetValue(type, out var replacement)) return replacement;
            return type switch
            {
                ArrayTypeSymbol array => new ArrayTypeSymbol(SubstituteType(array.ElementType, substitutions)),
                ResultTypeSymbol result => new ResultTypeSymbol(SubstituteType(result.SuccessType, substitutions), SubstituteType(result.ErrorType, substitutions)),
                ColumnTypeSymbol column => new ColumnTypeSymbol(SubstituteType(column.ElementType, substitutions)),
                CallableTypeSymbol callable => new CallableTypeSymbol(callable.Parameters.Select(parameter => new CallableParameterTypeSymbol(parameter.Name, SubstituteType(parameter.Type, substitutions))).ToArray(), SubstituteType(callable.ReturnType, substitutions)),
                _ => type
            };
        }

        private sealed class ClosedInstantiationRewriter(IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions)
        {
            public BoundBlockStatement RewriteBlock(BoundBlockStatement block)
                => new(block.Statements.Select(RewriteStatement).ToArray());

            private BoundStatement RewriteStatement(BoundStatement statement) => statement switch
            {
                BoundBlockStatement block => RewriteBlock(block),
                BoundVariableDeclaration variable => new BoundVariableDeclaration(RewriteVariable(variable.Variable), RewriteExpression(variable.Initializer)),
                BoundExpressionStatement expression => new BoundExpressionStatement(RewriteExpression(expression.Expression)),
                BoundIfStatement conditional => new BoundIfStatement(RewriteExpression(conditional.Condition), RewriteStatement(conditional.ThenStatement), conditional.ElseStatement is null ? null : RewriteStatement(conditional.ElseStatement)),
                BoundWhileStatement loop => new BoundWhileStatement(RewriteExpression(loop.Condition), RewriteStatement(loop.Body)),
                BoundForStatement loop => new BoundForStatement(loop.Initializer is null ? null : RewriteStatement(loop.Initializer), loop.Condition is null ? null : RewriteExpression(loop.Condition), loop.Increment is null ? null : RewriteExpression(loop.Increment), RewriteStatement(loop.Body)),
                BoundReturnStatement @return => new BoundReturnStatement(@return.Expression is null ? null : RewriteExpression(@return.Expression)),
                _ => statement
            };

            private BoundExpression RewriteExpression(BoundExpression expression) => expression switch
            {
                BoundLiteralExpression literal => new BoundLiteralExpression(literal.Value, SubstituteType(literal.Type, substitutions)),
                BoundVariableExpression variable => new BoundVariableExpression(RewriteVariable(variable.Variable)),
                BoundAssignmentExpression assignment => new BoundAssignmentExpression(RewriteVariable(assignment.Variable), RewriteExpression(assignment.Expression)),
                BoundUnaryExpression unary => new BoundUnaryExpression(unary.OperatorKind, RewriteExpression(unary.Operand), SubstituteType(unary.Type, substitutions)),
                BoundBinaryExpression binary => new BoundBinaryExpression(RewriteExpression(binary.Left), binary.OperatorKind, RewriteExpression(binary.Right), SubstituteType(binary.Type, substitutions)),
                BoundCallExpression call => new BoundCallExpression(call.Function, call.Arguments.Select(RewriteExpression).ToArray()),
                BoundFunctionReferenceExpression reference => new BoundFunctionReferenceExpression(reference.Function),
                BoundInvokeExpression invoke => new BoundInvokeExpression(RewriteExpression(invoke.Callee), invoke.Arguments.Select(RewriteExpression).ToArray(), (CallableTypeSymbol)SubstituteType(invoke.CallableType, substitutions)),
                BoundEnumValueExpression value => new BoundEnumValueExpression(value.Case, value.Arguments.Select(RewriteExpression).ToArray()),
                BoundPropagateExpression propagate => new BoundPropagateExpression(RewriteExpression(propagate.Operand), (ResultTypeSymbol)SubstituteType(propagate.ResultType, substitutions), propagate.Target),
                BoundUnwrapExpression unwrap => new BoundUnwrapExpression(RewriteExpression(unwrap.Operand), (ResultTypeSymbol)SubstituteType(unwrap.ResultType, substitutions)),
                BoundOkExpression ok => new BoundOkExpression(RewriteExpression(ok.Payload), (ResultTypeSymbol)SubstituteType(ok.Type, substitutions)),
                BoundErrExpression err => new BoundErrExpression(RewriteExpression(err.Payload), (ResultTypeSymbol)SubstituteType(err.Type, substitutions)),
                BoundArrayExpression array => new BoundArrayExpression(array.Elements.Select(RewriteExpression).ToArray(), SubstituteType(array.Type, substitutions)),
                BoundRequirementFieldAccessExpression requirement => RewriteRequirementAccess(requirement),
                BoundRecordFieldAccessExpression access => new BoundRecordFieldAccessExpression(RewriteExpression(access.Receiver), access.RecordType, access.Field),
                BoundTableRowFieldAccessExpression access => new BoundTableRowFieldAccessExpression(RewriteExpression(access.Receiver), access.RowType, access.Field),
                BoundTableReferenceExpression table => table,
                BoundTableColumnAccessExpression access => new BoundTableColumnAccessExpression(RewriteExpression(access.Receiver), access.TableType, access.Column),
                BoundTableRowAccessExpression access => new BoundTableRowAccessExpression(RewriteExpression(access.Receiver), RewriteExpression(access.Index), access.TableType, (ResultTypeSymbol)SubstituteType(access.Type, substitutions)),
                BoundColumnElementAccessExpression access => new BoundColumnElementAccessExpression(RewriteExpression(access.Receiver), RewriteExpression(access.Index), (ResultTypeSymbol)SubstituteType(access.Type, substitutions)),
                BoundRecordConstructionExpression construction => new BoundRecordConstructionExpression(construction.RecordType, construction.Initializers.Select(field => new BoundRecordFieldInitializer(field.Field, RewriteExpression(field.Value))).ToArray()),
                BoundRecordWithExpression withExpression => new BoundRecordWithExpression(RewriteExpression(withExpression.Source), withExpression.RecordType, withExpression.Replacements.Select(field => new BoundRecordFieldInitializer(field.Field, RewriteExpression(field.Value))).ToArray()),
                BoundIfExpression conditional => new BoundIfExpression(RewriteExpression(conditional.Condition), RewriteExpression(conditional.ThenExpression), RewriteExpression(conditional.ElseExpression), SubstituteType(conditional.Type, substitutions)),
                BoundMatchExpression match => new BoundMatchExpression(RewriteExpression(match.Scrutinee), match.EnumType, match.Arms.Select(arm => new BoundMatchArm(arm.Case, arm.PayloadVariables.Select(RewriteVariable).ToArray(), RewriteExpression(arm.Expression))).ToArray(), SubstituteType(match.Type, substitutions)),
                BoundResultMatchExpression match => new BoundResultMatchExpression(RewriteExpression(match.Scrutinee), RewriteVariable(match.OkVariable), RewriteExpression(match.OkExpression), RewriteVariable(match.ErrVariable), RewriteExpression(match.ErrExpression), SubstituteType(match.Type, substitutions)),
                BoundTsonEncodeExpression encode => new BoundTsonEncodeExpression(RewriteExpression(encode.Operand), encode.Plan, (ResultTypeSymbol)SubstituteType(encode.ResultType, substitutions)),
                BoundTryExceptExpression attempt => new BoundTryExceptExpression(attempt.HandlerId, RewriteValueBlock(attempt.Protected), RewriteVariable(attempt.HandlerBinding), SubstituteType(attempt.HandledErrorType, substitutions), RewriteValueBlock(attempt.Handler), SubstituteType(attempt.Type, substitutions)),
                _ => expression
            };

            private BoundExpression RewriteRequirementAccess(BoundRequirementFieldAccessExpression access)
            {
                var receiver = RewriteExpression(access.Receiver);
                var candidate = SubstituteType(access.TypeParameter, substitutions);
                return candidate switch
                {
                    RecordTypeSymbol record => new BoundRecordFieldAccessExpression(receiver, record, record.Fields.Single(field => field.Name == access.Field.Name)),
                    TableRowTypeSymbol row => new BoundTableRowFieldAccessExpression(receiver, row, row.Fields.Single(field => field.Name == access.Field.Name)),
                    _ => new BoundErrorExpression()
                };
            }

            private BoundValueBlock RewriteValueBlock(BoundValueBlock block)
                => new(block.PrefixStatements.Select(RewriteStatement).ToArray(), RewriteExpression(block.ValueExpression));

            private VariableSymbol RewriteVariable(VariableSymbol variable)
                => new(variable.Name, SubstituteType(variable.Type, substitutions), variable.IsReadOnly, variable.AuthoredAliasName);
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
            bool isNominalRoot = operand.Type is RecordTypeSymbol or EnumTypeSymbol;
            bool isAuthoredTableSingleton = operand is BoundTableReferenceExpression
                && operand.Type is TableTypeSymbol;
            if (!isNominalRoot && !isAuthoredTableSingleton)
            {
                Report(
                    "COPE-TSON-ENCODE-0001",
                    $"'tsonEncode' requires one nominal record, payload enum, or authored table singleton, not '{operand.Type.Name}'.",
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

            if (!TryGetOrCreateTsonEncodingPlan(operand, intrinsicName.IdentifierToken, out BoundTsonEncodingPlan? plan))
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
            BoundExpression operand,
            SyntaxToken anchor,
            out BoundTsonEncodingPlan? plan)
        {
            TypeSymbol rootType = operand.Type;
            if (_tsonEncodingPlans.TryGetValue(rootType, out plan))
            {
                return true;
            }

            string schemaIdentity = _schemaIdentity
                ?? throw new InvalidOperationException("TSON encoding plan creation requires validated schema metadata.");

            var reachable = new HashSet<TypeSymbol>();
            var visiting = new HashSet<TypeSymbol>();
            BoundTsonTablePlan? tablePlan = null;
            bool valid;
            if (operand is BoundTableReferenceExpression tableReference)
            {
                BoundTableDefinition? table = _tables.SingleOrDefault(
                    definition => ReferenceEquals(definition.TableType, tableReference.TableType));
                if (table is null)
                {
                    Report("COPE-TSON-ENCODE-0003", "TSON table encoding requires one declaration-owned table singleton.", anchor);
                    plan = null;
                    return false;
                }

                var columns = new List<BoundTsonTableColumnPlan>(table.Columns.Count);
                valid = true;
                foreach (BoundTableColumnDefinition column in table.Columns)
                {
                    if (!IsEligibleTsonTableCellType(column.Column.Type))
                    {
                        Report("COPE-TSON-ENCODE-0003", $"TSON table encoding does not support column '{column.Column.Name}' of type '{column.Column.Type.Name}'.", anchor);
                        valid = false;
                        continue;
                    }

                    valid &= VisitTsonType(column.Column.Type, table.TableType.Name + "." + column.Column.Name, anchor, reachable, visiting);
                    columns.Add(new BoundTsonTableColumnPlan(column.Column, column.Cells.Count));
                }

                tablePlan = new BoundTsonTablePlan(table.TableType, table.RowCount, columns);
            }
            else
            {
                valid = VisitTsonType(rootType, rootType.Name, anchor, reachable, visiting);
            }
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
                definitions,
                tablePlan);
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
            if (receiver.Type is TypeParameterTypeSymbol typeParameter)
            {
                var parameter = _currentFunction?.TypeParameters.FirstOrDefault(candidate => ReferenceEquals(candidate.Type, typeParameter));
                var field = parameter?.Requirements.Fields.FirstOrDefault(candidate => candidate.Name == m.NameToken.Text);
                if (field is null)
                {
                    Report("COPE-REQUIREMENT-0004", $"Type parameter '{typeParameter.Name}' has no required field '{m.NameToken.Text}'.", m.NameToken);
                    return new BoundErrorExpression();
                }
                return new BoundRequirementFieldAccessExpression(receiver, typeParameter, field);
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
                CallableTypeSyntax c => BindCallableType(c, anchor, missingId, missingPrefix),
                QualifiedRowTypeSyntax q => ResolveQualifiedRowType(q),
                ParenthesizedTypeSyntax p => BindType(p.Type, anchor, missingId, missingPrefix),
                ResultTypeSyntax r => BindResultType(r, anchor, missingId, missingPrefix),
                IdentifierTypeSyntax i => ResolveIdentifierType(i),
                _ => PrimitiveTypeSymbol.Error
            };
        }

        private TypeSymbol BindCallableType(CallableTypeSyntax syntax, SyntaxToken anchor, string missingId, string missingPrefix)
        {
            if (syntax.Parameters.Count > MaxCallableParameters)
            {
                Report("COPE-CALL-0001", $"Callable types support at most {MaxCallableParameters} parameters.", syntax.Parameters[MaxCallableParameters].Identifier);
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var parameters = syntax.Parameters.Select(parameter =>
            {
                if (!names.Add(parameter.Identifier.Text))
                {
                    Report("COPE-BIND-0005", $"Duplicate callable parameter '{parameter.Identifier.Text}'.", parameter.Identifier);
                }
                return new CallableParameterTypeSymbol(parameter.Identifier.Text, BindType(parameter.Type, parameter.Identifier, missingId, missingPrefix));
            }).ToArray();
            var callable = new CallableTypeSymbol(parameters, BindType(syntax.ReturnType, anchor, missingId, missingPrefix));
            if (GetCallableTypeDepth(callable) > MaxCallableTypeDepth)
            {
                Report("COPE-CALL-0002", $"Callable type nesting exceeds the limit of {MaxCallableTypeDepth}.", syntax.ArrowToken);
            }
            return callable;
        }

        private static int GetCallableTypeDepth(TypeSymbol root)
        {
            var worklist = new Stack<(TypeSymbol Type, int CallableDepth)>();
            worklist.Push((root, 0));
            var maximum = 0;
            while (worklist.Count > 0)
            {
                var (type, callableDepth) = worklist.Pop();
                switch (type)
                {
                    case CallableTypeSymbol callable:
                        callableDepth++;
                        maximum = Math.Max(maximum, callableDepth);
                        foreach (var parameter in callable.Parameters) worklist.Push((parameter.Type, callableDepth));
                        worklist.Push((callable.ReturnType, callableDepth));
                        break;
                    case ArrayTypeSymbol array:
                        worklist.Push((array.ElementType, callableDepth));
                        break;
                    case ResultTypeSymbol result:
                        worklist.Push((result.SuccessType, callableDepth));
                        worklist.Push((result.ErrorType, callableDepth));
                        break;
                    case ColumnTypeSymbol column:
                        worklist.Push((column.ElementType, callableDepth));
                        break;
                }
            }
            return maximum;
        }

        private TypeSymbol ResolveQualifiedRowType(QualifiedRowTypeSyntax type)
        {
            if (_tableTypes.TryGetValue(type.TableIdentifier.Text, out var table) && type.RowIdentifier.Text == "Row") return table.RowType;
            if (_aliases.TryGetValue(type.TableIdentifier.Text, out var alias)
                && alias.CanonicalType is TableTypeSymbol aliasedTable
                && type.RowIdentifier.Text == "Row")
            {
                return aliasedTable.RowType;
            }
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
                IdentifierTypeSyntax i when _activeTypeParameters is not null && _activeTypeParameters.TryGetValue(i.Identifier.Text, out var typeParameter) => typeParameter.Type,
                IdentifierTypeSyntax i when _aliases.TryGetValue(i.Identifier.Text, out var alias) => alias.CanonicalType,
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
            if (_aliases.TryGetValue(i.Identifier.Text, out var alias))
                return alias.CanonicalType;
            if (_activeTypeParameters is not null && _activeTypeParameters.TryGetValue(i.Identifier.Text, out var typeParameter))
                return typeParameter.Type;
            if (_interfaces.ContainsKey(i.Identifier.Text))
            {
                Report("COPE-INTERFACE-0005", $"Interface '{i.Identifier.Text}' is a requirement and cannot be used as a storage type.", i.Identifier);
                return PrimitiveTypeSymbol.Error;
            }
            if (_enumTypes.TryGetValue(i.Identifier.Text, out var enumType))
                return enumType;
            if (_recordTypes.TryGetValue(i.Identifier.Text, out var recordType))
                return recordType;
            if (_tableTypes.TryGetValue(i.Identifier.Text, out var tableType))
                return tableType;
            if (_currentAliasDeclaration is not null)
            {
                Report(
                    "COPE-ALIAS-0004",
                    $"Unknown type '{i.Identifier.Text}' in alias '{_currentAliasDeclaration.Identifier.Text}'.",
                    i.Identifier);
            }
            else
            {
                Report("COPE-BIND-0004", $"Unknown type '{i.Identifier.Text}'.", i.Identifier);
            }
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

        private void ValidateRuntimeValueType(TypeSymbol type, SyntaxToken anchor, string position)
        {
            if (type is CallableTypeSymbol)
            {
                if (position is "record field" or "enum payload")
                {
                    Report("COPE-CALL-0007", $"Callable types are not supported in {position}s.", anchor);
                }
                return;
            }
            if (ContainsCallable(type))
            {
                Report("COPE-CALL-0007", $"Callable types are not supported inside {position} containers.", anchor);
            }
            if (!IsLegalTypeForPosition(type, allowDirectVoid: false))
            {
                Report(
                    "COPE-TYPE-0020",
                    $"Type 'void' is not legal in a {position} position.",
                    anchor);
            }
        }

        private void ValidateFunctionReturnType(TypeSymbol type, SyntaxToken anchor)
        {
            if (type is not CallableTypeSymbol && ContainsCallable(type))
            {
                Report("COPE-CALL-0007", "Callable types cannot be stored inside a function return container.", anchor);
            }
            if (!IsLegalTypeForPosition(type, allowDirectVoid: true))
            {
                Report(
                    "COPE-TYPE-0020",
                    "Type 'void' is legal only as a direct function return or Result success type.",
                    anchor);
            }
        }

        private static bool IsLegalTypeForPosition(TypeSymbol root, bool allowDirectVoid)
        {
            var pending = new Stack<(TypeSymbol Type, bool AllowDirectVoid)>();
            pending.Push((root, allowDirectVoid));
            while (pending.Count > 0)
            {
                (TypeSymbol type, bool allowVoid) = pending.Pop();
                if (type == PrimitiveTypeSymbol.Void)
                {
                    if (!allowVoid)
                    {
                        return false;
                    }

                    continue;
                }

                switch (type)
                {
                    case ArrayTypeSymbol array:
                        pending.Push((array.ElementType, false));
                        break;
                    case ColumnTypeSymbol column:
                        pending.Push((column.ElementType, false));
                        break;
                    case ResultTypeSymbol result:
                        pending.Push((result.ErrorType, false));
                        pending.Push((result.SuccessType, true));
                        break;
                    case CallableTypeSymbol callable:
                        foreach (var parameter in callable.Parameters)
                        {
                            pending.Push((parameter.Type, false));
                        }
                        pending.Push((callable.ReturnType, true));
                        break;
                }
            }

            return true;
        }

        private static bool ContainsCallable(TypeSymbol type) => type switch
        {
            CallableTypeSymbol => true,
            ArrayTypeSymbol array => ContainsCallable(array.ElementType),
            ResultTypeSymbol result => ContainsCallable(result.SuccessType) || ContainsCallable(result.ErrorType),
            ColumnTypeSymbol column => ContainsCallable(column.ElementType),
            _ => false,
        };

        private static bool IsPrimitiveEqualityType(TypeSymbol type)
            => type == PrimitiveTypeSymbol.Number
                || type == PrimitiveTypeSymbol.String
                || type == PrimitiveTypeSymbol.Boolean;

        private string? GetAuthoredAliasName(TypeSyntax? syntax)
        {
            return syntax is IdentifierTypeSyntax identifier
                && _aliases.ContainsKey(identifier.Identifier.Text)
                    ? identifier.Identifier.Text
                    : null;
        }

        private void ReportTypeMismatch(
            string fallbackId,
            TypeSymbol expected,
            TypeSymbol actual,
            SyntaxToken anchor,
            string? authoredAliasName = null)
        {
            if (expected is EnumTypeSymbol { UnionProvenance: not null } expectedUnion
                && actual is RecordTypeSymbol actualRecord)
            {
                Report(
                    "COPE-UNION-0009",
                    $"Record '{actualRecord.Name}' is not an alternative of nominal union '{expectedUnion.Name}'.",
                    anchor);
                return;
            }
            if (expected is RecordTypeSymbol expectedRecord
                && actual is EnumTypeSymbol { UnionProvenance: not null } actualUnion)
            {
                Report(
                    "COPE-UNION-0010",
                    $"Nominal union '{actualUnion.Name}' cannot be assigned to alternative record '{expectedRecord.Name}'.",
                    anchor);
                return;
            }
            if (expected is EnumTypeSymbol { UnionProvenance: not null } targetUnion
                && actual is EnumTypeSymbol { UnionProvenance: not null } sourceUnion)
            {
                Report(
                    "COPE-UNION-0011",
                    $"Nominal union '{sourceUnion.Name}' is not assignable to unrelated nominal union '{targetUnion.Name}'.",
                    anchor);
                return;
            }
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
            string expectedText = authoredAliasName is null
                ? $"'{expected.Name}'"
                : $"'{authoredAliasName}' (alias of '{expected.Name}')";
            Report(fallbackId, $"Type mismatch: expected {expectedText}, got '{actual.Name}'.", anchor);
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
