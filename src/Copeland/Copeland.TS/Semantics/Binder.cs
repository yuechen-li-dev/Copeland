using Copeland.TS.Diagnostics;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Tson;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Copeland.TS.Semantics;

public static class Binder
{
    public static BoundCompilation Bind(SyntaxTree tree)
    {
        var impl = new BinderImpl(tree, null, new CopelandNpmContractResolver(new CopelandNpmDependencyGraph([])), new CopelandJavaScriptHostContractResolver([]), new CopelandClrMetadataResolver([]), new CopelandPackageContractMap([]), CopelandPackageBackend.Clr, CopelandTsXmlProfile.None, null, null, null);
        return impl.Bind();
    }

    internal static BoundCompilation Bind(SyntaxTree tree, CopelandAssetResolver? assetResolver, CopelandNpmContractResolver npmResolver, CopelandJavaScriptHostContractResolver hostResolver, CopelandClrMetadataResolver clrResolver, CopelandPackageContractMap packageContracts, CopelandPackageBackend packageBackend, CopelandTsXmlProfile tsXmlProfile = CopelandTsXmlProfile.None, string? sourcePath = null, string? moduleIdentity = null, BoundModuleImports? imports = null)
    {
        var impl = new BinderImpl(tree, assetResolver, npmResolver, hostResolver, clrResolver, packageContracts, packageBackend, tsXmlProfile, sourcePath, moduleIdentity, imports);
        return impl.Bind();
    }

    internal static IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> BindOpenGenericBodiesForTesting(SyntaxTree tree)
    {
        var impl = new BinderImpl(tree, null, new CopelandNpmContractResolver(new CopelandNpmDependencyGraph([])), new CopelandJavaScriptHostContractResolver([]), new CopelandClrMetadataResolver([]), new CopelandPackageContractMap([]), CopelandPackageBackend.Clr, CopelandTsXmlProfile.None, null, null, null);
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
        public bool TryDeclare(string localName, Symbol symbol) => _symbols.TryAdd(localName, symbol);
        public bool TryLookup(string n, out Symbol? symbol)
        {
            for (var c = this; c is not null; c = c.Parent)
                if (c._symbols.TryGetValue(n, out symbol)) return true;
            symbol = null; return false;
        }

        public IReadOnlyDictionary<string, Symbol> VisibleSymbols()
        {
            var visible = new Dictionary<string, Symbol>(StringComparer.Ordinal);
            for (var current = this; current is not null; current = current.Parent)
            {
                foreach (var pair in current._symbols)
                {
                    visible.TryAdd(pair.Key, pair.Value);
                }
            }

            return visible;
        }
    }

    private sealed class BinderImpl(SyntaxTree tree, CopelandAssetResolver? assetResolver, CopelandNpmContractResolver npmResolver, CopelandJavaScriptHostContractResolver hostResolver, CopelandClrMetadataResolver clrResolver, CopelandPackageContractMap packageContracts, CopelandPackageBackend packageBackend, CopelandTsXmlProfile tsXmlProfile, string? sourcePath, string? moduleIdentity, BoundModuleImports? imports)
    {
        private sealed class BatchBindingContext
        {
            public required SyntaxToken Anchor { get; init; }
            public HashSet<Symbol> LocalBindings { get; } = [];
            public HashSet<Symbol> Captures { get; } = [];
        }

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
        private const int MaxCallableExpressionNesting = 16;
        private const int MaxLiftedCallableDefinitions = 512;
        private const int MaxCaptureCount = 16;
        private const int MaxClassFields = 64;
        private const int MaxClassAssociatedFunctions = 64;
        private const int MaxPrivateClassAssociatedFunctions = 32;
        private const int MaxClassesPerCompilation = 256;

        private int _loopDepth;
        private readonly Stack<BatchBindingContext> _batchContexts = [];
        private readonly SyntaxTree _tree = tree;
        private readonly CopelandAssetResolver? _assetResolver = assetResolver;
        private readonly CopelandNpmContractResolver _npmResolver = npmResolver;
        private readonly CopelandJavaScriptHostContractResolver _hostResolver = hostResolver;
        private readonly CopelandClrMetadataResolver _clrResolver = clrResolver;
        private readonly CopelandPackageContractMap _packageContracts = packageContracts;
        private readonly CopelandPackageBackend _packageBackend = packageBackend;
        private readonly CopelandTsXmlProfile _tsXmlProfile = tsXmlProfile;
        private readonly string? _sourcePath = sourcePath;
        private readonly string? _moduleIdentity = moduleIdentity;
        private readonly BoundModuleImports? _imports = imports;
        private readonly DiagnosticBag _diagnostics = new();
        private readonly Scope _global = new(null);
        private Scope _scope = null!;
        private FunctionSymbol? _currentFunction;
        private ClassTypeSymbol? _currentClass;
        private readonly List<BoundFunctionDeclaration> _functions = [];
        private readonly List<BoundEnumDeclaration> _enums = [];
        private readonly List<BoundRecordDeclaration> _records = [];
        private readonly List<BoundTableDefinition> _tables = [];
        private readonly List<BoundFlowDefinition> _flows = [];
        private readonly List<BoundStatement> _globals = [];
        private readonly List<BoundNpmImport> _npmImports = [];
        private readonly List<BoundPackageImport> _packageImports = [];
        private readonly List<BoundJavaScriptHostImport> _javaScriptHostImports = [];
        private readonly HashSet<string> _clrNamespaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Type>> _clrImportedTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EnumTypeSymbol> _enumTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RecordTypeSymbol> _recordTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClassTypeSymbol> _classTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<FunctionSymbol, ClassAssociatedFunctionDeclarationSyntax> _classFunctionDeclarations = [];
        private readonly Dictionary<FunctionSymbol, ClassConstructorDeclarationSyntax> _classConstructorDeclarations = [];
        private readonly Dictionary<string, TableTypeSymbol> _tableTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TypeAliasSymbol> _aliases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, InterfaceSymbol> _interfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NominalUnionDeclarationSyntax> _unionDeclarations = new(StringComparer.Ordinal);
        private readonly Dictionary<FunctionSymbol, BoundFunctionDeclaration> _genericBodies = [];
        private readonly Dictionary<string, BoundFunctionDeclaration> _closedInstantiations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RecordTypeSymbol> _npmTransportRecords = new(StringComparer.Ordinal);
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
        private int _nextLiftedCallableId;
        private int _callableExpressionDepth;
        private int _arrowBodyDepth;

        private sealed class PropagationTargetContext(BoundHandlerId handlerId)
        {
            public BoundHandlerId HandlerId { get; } = handlerId;
            public TypeSymbol? ErrorType { get; set; }
            public bool WasTargeted { get; set; }
        }

        public BoundCompilation Bind()
        {
            _scope = _global;
            InitializeModuleOwnedTypeIdentityRange();
            ImportModuleSymbols();
            BindSchemaMetadata(_tree.Root);
            PredeclareTableBoundsError();
            PredeclareTsonEncodeError();
            AnalyzeAliasTypeNameCollisions(_tree.Root);
            PredeclareInterfaces(_tree.Root);
            PredeclareAliases(_tree.Root);
            PredeclareRecords(_tree.Root);
            PredeclareClasses(_tree.Root);
            PredeclareTables(_tree.Root);
            PredeclareEnums(_tree.Root);
            PredeclareNominalUnions(_tree.Root);
            ResolveAliases();
            BindInterfaceBodies(_tree.Root);
            PredeclareFunctions(_tree.Root);
            BindClrUsingDirectives(_tree.Root);
            BindCopelandPackageImports(_tree.Root);
            BindNpmImports(_tree.Root);
            BindJavaScriptHostImports(_tree.Root);
            BindRecordBodies(_tree.Root);
            BindClassFields(_tree.Root);
            PredeclareClassMembers(_tree.Root);
            BindEnumBodies(_tree.Root);
            BindNominalUnionBodies(_tree.Root);
            BindTableBodies(_tree.Root);
            BindFlows(_tree.Root);
            ValidateRecordCycles();
            foreach (var generic in _tree.Root.Members.OfType<FunctionDeclarationSyntax>().Where(function => function.TypeParameters.Count > 0))
            {
                _genericBodies[(FunctionSymbol)_globalLookup(generic.Identifier.Text)!] = BindFunction(generic);
            }
            foreach (var pair in _classFunctionDeclarations.Where(pair => pair.Key.IsGeneric))
            {
                _genericBodies[pair.Key] = BindClassFunction(pair.Key, pair.Value);
            }
            foreach (var pair in _classConstructorDeclarations)
            {
                _functions.Add(BindClassConstructor(pair.Key, pair.Value));
            }
            foreach (var pair in _classFunctionDeclarations.Where(pair => !pair.Key.IsGeneric))
            {
                _functions.Add(BindClassFunction(pair.Key, pair.Value));
            }
            foreach (var m in _tree.Root.Members)
            {
                if (m is FunctionDeclarationSyntax f && f.TypeParameters.Count == 0) _functions.Add(BindFunction(f));
                else if (m is EnumDeclarationSyntax e && e.Identifier.Text != "TableBoundsError" && _enumTypes.TryGetValue(e.Identifier.Text, out var enumType)) _enums.Add(new BoundEnumDeclaration(enumType));
                else if (m is NominalUnionDeclarationSyntax union && _enumTypes.TryGetValue(union.Identifier.Text, out var unionType)) _enums.Add(new BoundEnumDeclaration(unionType));
                else if (m is RecordDeclarationSyntax r && _recordTypes.TryGetValue(r.Identifier.Text, out var recordType)) _records.Add(new BoundRecordDeclaration(recordType));
                else if (m is ClassDeclarationSyntax c && _classTypes.TryGetValue(c.Identifier.Text, out var classType)) _records.Add(new BoundRecordDeclaration(classType));
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
                    _tsonEncodingPlans.Values.OrderBy(plan => plan.Id, StringComparer.Ordinal).ToArray(),
                    _npmImports.OrderBy(import => import.Function.PackageName, StringComparer.Ordinal).ThenBy(import => import.Function.ExportName, StringComparer.Ordinal).ThenBy(import => import.Function.Name, StringComparer.Ordinal).ToArray(),
                    _packageImports.OrderBy(import => import.Function.PackageId, StringComparer.Ordinal).ThenBy(import => import.Function.ModuleSpecifier, StringComparer.Ordinal).ThenBy(import => import.Function.ExportName, StringComparer.Ordinal).ToArray(),
                    _javaScriptHostImports.OrderBy(import => import.Function.ModuleSpecifier, StringComparer.Ordinal).ThenBy(import => import.Function.ExportName, StringComparer.Ordinal).ThenBy(import => import.Function.Name, StringComparer.Ordinal).ToArray(),
                    _clrNamespaces.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                    _sourcePath,
                    _flows),
                _tree.Diagnostics.Concat(_diagnostics.Diagnostics).ToArray(),
                CreateModuleScope());
        }

        private void ImportModuleSymbols()
        {
            if (_imports is null) return;
            foreach (var pair in _imports.Declarations)
            {
                if (!_global.TryDeclare(pair.Key, pair.Value)) continue;
                if (pair.Value is VariableSymbol variable)
                {
                    switch (variable.Type)
                    {
                        case RecordTypeSymbol record: _recordTypes[pair.Key] = record; break;
                        case EnumTypeSymbol @enum: _enumTypes[pair.Key] = @enum; break;
                        case TableTypeSymbol table: _tableTypes[pair.Key] = table; break;
                    }
                }
            }
            foreach (var pair in _imports.Aliases) _aliases[pair.Key] = pair.Value;
            foreach (var pair in _imports.Interfaces) _interfaces[pair.Key] = pair.Value;
            foreach (var pair in _imports.GenericBodies) _genericBodies[pair.Key] = pair.Value;
        }

        private void InitializeModuleOwnedTypeIdentityRange()
        {
            if (_moduleIdentity is null) return;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(_moduleIdentity));
            int bucket = ((hash[0] << 16) | (hash[1] << 8) | hash[2]) % 1_000_000;
            // The range is a deterministic encoding of the logical module path,
            // not source ordering or a generated class name. A module's local
            // declaration counter occupies only its own range.
            _nextRecordTypeId = checked(bucket * 1_000 + 1);
            _nextTableTypeId = checked(bucket * 1_000 + 1);
        }

        private BoundModuleScope CreateModuleScope()
        {
            var imported = _imports?.Declarations.Keys.ToHashSet(StringComparer.Ordinal) ?? [];
            var declarations = _global.VisibleSymbols()
                .Where(pair => !imported.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var aliases = _aliases.Where(pair => _imports is null || !_imports.Aliases.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var interfaces = _interfaces.Where(pair => _imports is null || !_imports.Interfaces.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            return new BoundModuleScope(_moduleIdentity ?? _sourcePath ?? "<standalone>", declarations, aliases, interfaces, new Dictionary<FunctionSymbol, BoundFunctionDeclaration>(_genericBodies));
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
                string? identity = CreateDeclarationStableIdentity(declaration.Identifier.Text);
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
                    when literal.Type == PrimitiveTypeSymbol.Int && literal.Value is int integer => new BoundTableLiteralConstant(-integer, expression.Type),
                BoundUnaryExpression { OperatorKind: SyntaxKind.MinusToken, Operand: BoundLiteralExpression literal }
                    when TypeFacts.IsFloat(literal.Type) && literal.Value is IConvertible number => new BoundTableLiteralConstant(-number.ToDouble(System.Globalization.CultureInfo.InvariantCulture), expression.Type),
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
                PrimitiveTypeSymbol primitive when TypeFacts.IsNumeric(primitive)
                    || primitive == PrimitiveTypeSymbol.String
                    || primitive == PrimitiveTypeSymbol.Boolean => true,
                ArrayTypeSymbol array when _schemaIdentity is not null => IsEligibleTableCellType(array.ElementType, visiting, out _),
                EnumTypeSymbol @enum => @enum.Cases.All(@case => @case.PayloadFields.All(field => IsEligibleTableCellType(field.Type, visiting, out _))),
                ClassTypeSymbol => false,
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
                PrimitiveTypeSymbol primitive => TypeFacts.IsNumeric(primitive)
                    || primitive == PrimitiveTypeSymbol.String
                    || primitive == PrimitiveTypeSymbol.Boolean,
                ArrayTypeSymbol array => IsEligibleTsonTableCellType(array.ElementType, visiting),
                EnumTypeSymbol @enum => @enum.Cases.All(@case =>
                    @case.PayloadFields.All(field =>
                        IsEligibleTsonTableCellType(field.Type, visiting))),
                ClassTypeSymbol => false,
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

                string? identity = CreateDeclarationStableIdentity(declaration.Identifier.Text);
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

        private void PredeclareClasses(CompilationUnitSyntax root)
        {
            var declarations = root.Members.OfType<ClassDeclarationSyntax>().ToArray();
            if (declarations.Length > MaxClassesPerCompilation)
            {
                Report("COPE-CLASS-0018", $"A compilation supports at most {MaxClassesPerCompilation} class declarations.", declarations[MaxClassesPerCompilation].Identifier);
            }

            foreach (var declaration in declarations)
            {
                if (declaration.Identifier.Text is "tsonAsset" or "tsonEncode" or "TsonEncodeError")
                {
                    Report("COPE-CLASS-0002", $"'{declaration.Identifier.Text}' is compiler-owned and cannot be a class name.", declaration.Identifier);
                    continue;
                }

                if (_aliases.ContainsKey(declaration.Identifier.Text)
                    || _interfaces.ContainsKey(declaration.Identifier.Text)
                    || _enumTypes.ContainsKey(declaration.Identifier.Text)
                    || _tableTypes.ContainsKey(declaration.Identifier.Text)
                    || _recordTypes.ContainsKey(declaration.Identifier.Text)
                    || _classTypes.ContainsKey(declaration.Identifier.Text))
                {
                    Report("COPE-CLASS-0002", $"Class name '{declaration.Identifier.Text}' collides with an existing type declaration.", declaration.Identifier);
                    continue;
                }

                string? identity = _schemaIdentity is null ? null : $"{_schemaIdentity}#{declaration.Identifier.Text}";
                var classType = new ClassTypeSymbol(
                    declaration.Identifier.Text,
                    new RecordTypeId(_nextRecordTypeId++),
                    identity);
                if (!_global.TryDeclare(new ClassValueSymbol(classType.Name, classType)))
                {
                    Report("COPE-CLASS-0002", $"Duplicate declaration '{classType.Name}'.", declaration.Identifier);
                    continue;
                }
                _recordTypes.Add(classType.Name, classType);
                _classTypes.Add(classType.Name, classType);
            }
        }

        private void BindClassFields(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<ClassDeclarationSyntax>())
            {
                if (!_classTypes.TryGetValue(declaration.Identifier.Text, out var classType))
                {
                    continue;
                }

                if (declaration.ExtendsKeyword is not null)
                {
                    Report("COPE-CLASS-0014", "Class inheritance is not supported; a Copeland class is a closed immutable nominal value.", declaration.ExtendsKeyword);
                }

                var names = new HashSet<string>(StringComparer.Ordinal);
                var fields = declaration.Members.OfType<ClassFieldSyntax>().ToArray();
                if (fields.Length > MaxClassFields)
                {
                    Report("COPE-CLASS-0018", $"Class '{classType.Name}' supports at most {MaxClassFields} fields.", fields[MaxClassFields].Identifier);
                }
                foreach (var fieldSyntax in fields)
                {
                    ValidateClassVisibility(fieldSyntax.VisibilityKeyword, fieldSyntax.Identifier);
                    if (!fieldSyntax.HasExplicitType || !fieldSyntax.HasTerminator)
                    {
                        Report("COPE-CLASS-0001", $"Class field '{fieldSyntax.Identifier.Text}' requires an explicit type and terminating semicolon.", fieldSyntax.Identifier);
                    }
                    if (fieldSyntax.EqualsToken is not null || fieldSyntax.Initializer is not null)
                    {
                        Report("COPE-CLASS-0015", $"Class field '{fieldSyntax.Identifier.Text}' cannot have an initializer; construction must return one complete value.", fieldSyntax.EqualsToken ?? fieldSyntax.Identifier);
                    }
                    if (fieldSyntax.Modifiers.Count > 0)
                    {
                        Report("COPE-CLASS-0015", "Class fields are immutable and do not support readonly, static, accessors, or other modifiers.", fieldSyntax.Modifiers[0]);
                    }
                    if (!names.Add(fieldSyntax.Identifier.Text))
                    {
                        Report("COPE-CLASS-0003", $"Duplicate class member '{fieldSyntax.Identifier.Text}' in '{classType.Name}'.", fieldSyntax.Identifier);
                        continue;
                    }

                    TypeSymbol fieldType = fieldSyntax.HasExplicitType
                        ? BindType(fieldSyntax.Type, fieldSyntax.Identifier, "COPE-CLASS-0001", "class field")
                        : PrimitiveTypeSymbol.Error;
                    ValidateRuntimeValueType(fieldType, fieldSyntax.Identifier, "class field");
                    bool isPublic = !string.Equals(fieldSyntax.VisibilityKeyword?.Text, "private", StringComparison.Ordinal);
                    classType.AddField(new RecordFieldSymbol(
                        fieldSyntax.Identifier.Text,
                        new RecordFieldId(classType.Id, classType.Fields.Count),
                        fieldType,
                        isPublic));
                }
            }
        }

        private void PredeclareClassMembers(CompilationUnitSyntax root)
        {
            foreach (var declaration in root.Members.OfType<ClassDeclarationSyntax>())
            {
                if (!_classTypes.TryGetValue(declaration.Identifier.Text, out var classType))
                {
                    continue;
                }

                var memberNames = classType.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
                var constructors = declaration.Members.OfType<ClassConstructorDeclarationSyntax>().ToArray();
                if (constructors.Length == 0)
                {
                    Report("COPE-CLASS-0004", $"Class '{classType.Name}' requires exactly one primary constructor.", declaration.Identifier);
                }
                if (constructors.Length > 1)
                {
                    foreach (var constructor in constructors.Skip(1))
                    {
                        Report("COPE-CLASS-0004", $"Class '{classType.Name}' has more than one constructor.", constructor.ConstructorKeyword);
                    }
                }
                if (constructors.Length > 0)
                {
                    ClassConstructorDeclarationSyntax constructor = constructors[0];
                    ValidateClassVisibility(constructor.VisibilityKeyword, constructor.ConstructorKeyword);
                    if (string.Equals(constructor.VisibilityKeyword?.Text, "private", StringComparison.Ordinal))
                    {
                        Report("COPE-CLASS-0005", "The primary constructor is the public pure class call and cannot be private.", constructor.VisibilityKeyword!);
                    }
                    if (constructor.Modifiers.Count > 0)
                    {
                        Report("COPE-CLASS-0005", "Constructors cannot be static, readonly, accessors, or use other modifiers.", constructor.Modifiers[0]);
                    }
                    var parameters = BindClassParameters(constructor.Parameters, constructor.ConstructorKeyword, []);
                    TypeSymbol returnType = constructor.ReturnType is null
                        ? classType
                        : BindType(constructor.ReturnType, constructor.ConstructorKeyword, "COPE-CLASS-0005", "constructor return");
                    bool validReturn = ReferenceEquals(returnType, classType)
                        || returnType is ResultTypeSymbol { SuccessType: var success } && ReferenceEquals(success, classType);
                    if (!validReturn)
                    {
                        Report("COPE-CLASS-0005", $"Constructor '{classType.Name}' must return '{classType.Name}' or '{classType.Name} ! E'.", constructor.ReturnTypeColonToken ?? constructor.ConstructorKeyword);
                    }
                    var symbol = new FunctionSymbol(
                        classType.Name + "__constructor",
                        parameters,
                        validReturn ? returnType : classType,
                        stableIdentity: "class:" + classType.Name + ".constructor")
                    {
                        ClassOwner = classType,
                        MemberName = "constructor",
                        IsClassConstructor = true,
                        IsPublic = true,
                    };
                    classType.SetConstructor(symbol);
                    _classConstructorDeclarations.Add(symbol, constructor);
                }

                var functions = declaration.Members.OfType<ClassAssociatedFunctionDeclarationSyntax>().ToArray();
                if (functions.Length > MaxClassAssociatedFunctions)
                {
                    Report("COPE-CLASS-0018", $"Class '{classType.Name}' supports at most {MaxClassAssociatedFunctions} associated functions.", functions[MaxClassAssociatedFunctions].Identifier);
                }
                int privateCount = 0;
                foreach (var method in functions)
                {
                    ValidateClassVisibility(method.VisibilityKeyword, method.Identifier);
                    if (method.Modifiers.Count > 0)
                    {
                        Report("COPE-CLASS-0015", "Associated functions do not use static, accessors, or other TypeScript method modifiers.", method.Modifiers[0]);
                    }
                    if (!memberNames.Add(method.Identifier.Text))
                    {
                        Report("COPE-CLASS-0003", $"Duplicate class member '{method.Identifier.Text}' in '{classType.Name}'.", method.Identifier);
                        continue;
                    }

                    bool isPublic = !string.Equals(method.VisibilityKeyword?.Text, "private", StringComparison.Ordinal);
                    if (!isPublic && ++privateCount > MaxPrivateClassAssociatedFunctions)
                    {
                        Report("COPE-CLASS-0018", $"Class '{classType.Name}' supports at most {MaxPrivateClassAssociatedFunctions} private associated functions.", method.Identifier);
                    }
                    var typeParameters = BindClassTypeParameters(method);
                    _activeTypeParameters = CreateTypeParameterScope(typeParameters);
                    var parameters = BindClassParameters(method.Parameters, method.Identifier, typeParameters);
                    TypeSymbol returnType = BindType(method.ReturnType, method.Identifier, "COPE-TYPE-0002", "associated function return");
                    ValidateFunctionReturnType(returnType, method.Identifier);
                    _activeTypeParameters = null;
                    var symbol = new FunctionSymbol(
                        classType.Name + "__" + method.Identifier.Text,
                        parameters,
                        returnType,
                        GetAuthoredAliasName(method.ReturnType),
                        "class:" + classType.Name + "." + method.Identifier.Text)
                    {
                        TypeParameters = typeParameters,
                        ClassOwner = classType,
                        MemberName = method.Identifier.Text,
                        IsPublic = isPublic,
                    };
                    classType.AddAssociatedFunction(symbol);
                    _classFunctionDeclarations.Add(symbol, method);
                }
            }
        }

        private IReadOnlyList<TypeParameterSymbol> BindClassTypeParameters(ClassAssociatedFunctionDeclarationSyntax method)
        {
            var result = new List<TypeParameterSymbol>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (method.TypeParameters.Count > MaxTypeParametersPerFunction)
            {
                Report("COPE-GENERIC-0011", $"Generic associated function '{method.Identifier.Text}' exceeds the {MaxTypeParametersPerFunction} type-parameter limit.", method.Identifier);
            }
            for (int index = 0; index < method.TypeParameters.Count; index++)
            {
                TypeParameterSyntax syntax = method.TypeParameters[index];
                if (!names.Add(syntax.Identifier.Text))
                {
                    Report("COPE-GENERIC-0001", $"Duplicate type parameter '{syntax.Identifier.Text}'.", syntax.Identifier);
                }
                if (_aliases.ContainsKey(syntax.Identifier.Text)
                    || _recordTypes.ContainsKey(syntax.Identifier.Text)
                    || _enumTypes.ContainsKey(syntax.Identifier.Text)
                    || _tableTypes.ContainsKey(syntax.Identifier.Text)
                    || _interfaces.ContainsKey(syntax.Identifier.Text))
                {
                    Report("COPE-GENERIC-0002", $"Type parameter '{syntax.Identifier.Text}' cannot shadow a compilation-unit type declaration.", syntax.Identifier);
                }
                result.Add(new TypeParameterSymbol(syntax.Identifier.Text, new TypeParameterTypeSymbol(syntax.Identifier.Text, index), BindRequirements(syntax)));
            }
            return result;
        }

        private IReadOnlyList<ParameterSymbol> BindClassParameters(
            IReadOnlyList<ParameterSyntax> syntax,
            SyntaxToken anchor,
            IReadOnlyList<TypeParameterSymbol> typeParameters)
        {
            var result = new List<ParameterSymbol>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ParameterSyntax parameter in syntax)
            {
                TypeSymbol type = BindType(parameter.Type, parameter.Identifier, "COPE-TYPE-0002", "parameter");
                ValidateRuntimeValueType(type, parameter.Identifier, "parameter");
                if (!names.Add(parameter.Identifier.Text))
                {
                    Report("COPE-BIND-0005", $"Duplicate parameter '{parameter.Identifier.Text}'.", parameter.Identifier);
                }
                result.Add(new ParameterSymbol(parameter.Identifier.Text, type, GetAuthoredAliasName(parameter.Type)));
            }
            return result;
        }

        private void ValidateClassVisibility(SyntaxToken? visibility, SyntaxToken anchor)
        {
            if (string.Equals(visibility?.Text, "protected", StringComparison.Ordinal))
            {
                Report("COPE-CLASS-0014", "'protected' is not supported because Copeland classes have no inheritance.", visibility!);
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
                SyntaxToken anchor = recordType is ClassTypeSymbol
                    ? _tree.Root.Members.OfType<ClassDeclarationSyntax>().First(item => item.Identifier.Text == recordType.Name).Identifier
                    : _tree.Root.Members.OfType<RecordDeclarationSyntax>().First(item => item.Identifier.Text == recordType.Name).Identifier;
                string diagnosticId = recordType is ClassTypeSymbol ? "COPE-CLASS-0013" : "COPE-REC-0004";
                Report(diagnosticId, $"Recursive nominal storage involving '{recordType.Name}' is not supported.", anchor);
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
                if (m.GeneratorStarToken is not null && rt is not IterableTypeSymbol)
                {
                    Report("COPE-GEN-0001", "Generator functions must declare a return type of Iterable<T>.", m.Identifier);
                }
                if (m.GeneratorStarToken is not null && m.AsyncKeyword is not null)
                {
                    Report("COPE-GEN-0002", "Async generators are not supported.", m.AsyncKeyword);
                }
                _activeTypeParameters = null;
                var fn = new FunctionSymbol(
                    m.Identifier.Text,
                    ps,
                    rt,
                    GetAuthoredAliasName(m.ReturnType),
                    CreateFunctionStableIdentity(m.Identifier.Text),
                    m.AsyncKeyword is not null,
                    m.GeneratorStarToken is not null,
                    m.RemoteKeyword is not null)
                {
                    TypeParameters = typeParameters
                };
                if (m.RemoteKeyword is not null)
                {
                    ValidateRemoteFunction(m, fn);
                }
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
                string? identity = CreateDeclarationStableIdentity(m.Identifier.Text);
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

                    if (_recordTypes.TryGetValue(alternative.Text, out var recordType)
                        && recordType is not ClassTypeSymbol)
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
                    else if (_classTypes.ContainsKey(alternative.Text))
                    {
                        Report("COPE-UNION-0007", $"Class '{alternative.Text}' is not an approved nominal-union alternative.", alternative);
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

        private void BindFlows(CompilationUnitSyntax root)
        {
            foreach (FlowDeclarationSyntax declaration in root.Members.OfType<FlowDeclarationSyntax>())
            {
                BindFlow(declaration);
            }
        }

        private void BindFlow(FlowDeclarationSyntax declaration)
        {
            string flowName = declaration.Identifier.Text;
            TypeSymbol declaredResult = declaration.ResultType is null
                ? PrimitiveTypeSymbol.Void
                : BindType(declaration.ResultType, declaration.Identifier, "COPE-FLOW-0027", "flow result");
            TypeSymbol? declaredFailure = declaredResult is ResultTypeSymbol result ? result.ErrorType : null;
            if (declaredResult is ResultTypeSymbol resultType)
            {
                declaredResult = resultType.SuccessType;
            }
            if (_global.TryLookup(flowName, out _))
            {
                Report("COPE-FLOW-0002", $"Duplicate flow declaration '{flowName}'.", declaration.Identifier);
            }

            var boardType = new RecordTypeSymbol(
                flowName + "Board",
                new RecordTypeId(_nextRecordTypeId++),
                "flow:" + flowName + ".board");
            var boardFields = new List<BoundFlowBoardField>();
            if (declaration.Board is null)
            {
                Report("COPE-FLOW-0003", $"Flow '{flowName}' must declare a fixed board.", declaration.Identifier);
            }
            else
            {
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (FlowBoardFieldSyntax fieldSyntax in declaration.Board.Fields)
                {
                    if (!fieldNames.Add(fieldSyntax.Identifier.Text))
                    {
                        Report("COPE-FLOW-0004", $"Flow board '{flowName}' has duplicate field '{fieldSyntax.Identifier.Text}'.", fieldSyntax.Identifier);
                        continue;
                    }

                    TypeSymbol type = BindType(fieldSyntax.Type, fieldSyntax.Identifier, "COPE-FLOW-0005", "board field");
                    var field = new RecordFieldSymbol(fieldSyntax.Identifier.Text, new RecordFieldId(boardType.Id, boardType.Fields.Count), type);
                    boardType.AddField(field);
                    BoundExpression initializer;
                    if (fieldSyntax.Initializer is null)
                    {
                        Report("COPE-FLOW-0006", $"Board field '{field.Name}' requires an explicit initializer in FLOW-M1.", fieldSyntax.Identifier);
                        initializer = new BoundErrorExpression();
                    }
                    else
                    {
                        initializer = BindExpression(fieldSyntax.Initializer, type);
                        if (initializer.Type != PrimitiveTypeSymbol.Error && !IsAssignable(type, initializer.Type))
                        {
                            Report("COPE-FLOW-0007", $"Board initializer for '{field.Name}' must have type '{type.Name}', got '{initializer.Type.Name}'.", fieldSyntax.Identifier);
                        }
                    }
                    boardFields.Add(new BoundFlowBoardField(field, initializer));
                }
            }
            _records.Add(new BoundRecordDeclaration(boardType));

            var events = new List<BoundFlowEvent>();
            var eventsByName = new Dictionary<string, BoundFlowEvent>(StringComparer.Ordinal);
            foreach (FlowEventSyntax eventSyntax in declaration.Events)
            {
                if (eventsByName.ContainsKey(eventSyntax.Identifier.Text))
                {
                    Report("COPE-FLOW-0008", $"Flow '{flowName}' has duplicate event '{eventSyntax.Identifier.Text}'.", eventSyntax.Identifier);
                    continue;
                }
                var parameters = new List<ParameterSymbol>();
                var parameterNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (ParameterSyntax parameterSyntax in eventSyntax.Parameters)
                {
                    if (!parameterNames.Add(parameterSyntax.Identifier.Text))
                    {
                        Report("COPE-FLOW-0009", $"Event '{eventSyntax.Identifier.Text}' has duplicate payload binding '{parameterSyntax.Identifier.Text}'.", parameterSyntax.Identifier);
                        continue;
                    }
                    TypeSymbol type = BindType(parameterSyntax.Type, parameterSyntax.Identifier, "COPE-FLOW-0010", "event payload");
                    parameters.Add(new ParameterSymbol(parameterSyntax.Identifier.Text, type));
                }
                var @event = new BoundFlowEvent(eventSyntax.Identifier.Text, "flow:" + flowName + ".event:" + eventSyntax.Identifier.Text, parameters);
                events.Add(@event);
                eventsByName.Add(@event.Name, @event);
            }

            var stateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (FlowStateSyntax state in declaration.States)
            {
                if (!stateNames.Add(state.Identifier.Text))
                {
                    Report("COPE-FLOW-0011", $"Flow '{flowName}' has duplicate state '{state.Identifier.Text}'.", state.Identifier);
                }
            }
            FlowStateSyntax[] initialStates = declaration.States.Where(state => state.InitialKeyword is not null).ToArray();
            if (initialStates.Length == 0)
            {
                Report("COPE-FLOW-0012", $"Flow '{flowName}' must have exactly one initial state.", declaration.Identifier);
            }
            else if (initialStates.Length > 1)
            {
                foreach (FlowStateSyntax state in initialStates.Skip(1))
                {
                    Report("COPE-FLOW-0013", $"Flow '{flowName}' has multiple initial states.", state.InitialKeyword!);
                }
            }

            var states = new List<BoundFlowState>();
            foreach (FlowStateSyntax stateSyntax in declaration.States)
            {
                var transitions = new List<BoundFlowTransition>();
                foreach (FlowTransitionSyntax transitionSyntax in stateSyntax.Transitions)
                {
                    if (!eventsByName.TryGetValue(transitionSyntax.EventIdentifier.Text, out BoundFlowEvent? @event))
                    {
                        Report("COPE-FLOW-0014", $"State '{stateSyntax.Identifier.Text}' handles unknown event '{transitionSyntax.EventIdentifier.Text}'.", transitionSyntax.EventIdentifier);
                        continue;
                    }
                    if (!stateNames.Contains(transitionSyntax.TargetIdentifier.Text))
                    {
                        Report("COPE-FLOW-0015", $"Transition target '{transitionSyntax.TargetIdentifier.Text}' is not a state in flow '{flowName}'.", transitionSyntax.TargetIdentifier);
                    }
                    if (transitionSyntax.Bindings.Count != @event.Parameters.Count)
                    {
                        Report("COPE-FLOW-0016", $"Event '{@event.Name}' requires {@event.Parameters.Count} payload binding(s), got {transitionSyntax.Bindings.Count}.", transitionSyntax.EventIdentifier);
                    }

                    Scope previousScope = _scope;
                    _scope = new Scope(_global);
                    try
                    {
                        _scope.TryDeclare(new VariableSymbol("board", boardType, true));
                        var bindings = new List<ParameterSymbol>();
                        for (int index = 0; index < transitionSyntax.Bindings.Count && index < @event.Parameters.Count; index++)
                        {
                            var binding = new ParameterSymbol(transitionSyntax.Bindings[index].Text, @event.Parameters[index].Type);
                            bindings.Add(binding);
                            if (!_scope.TryDeclare(binding)) Report("COPE-FLOW-0017", $"Payload binding '{binding.Name}' is duplicated or shadows board.", transitionSyntax.Bindings[index]);
                        }
                        BoundExpression? guard = transitionSyntax.Guard is null ? null : EnsureBoolean(BindExpression(transitionSyntax.Guard), transitionSyntax.WhenKeyword!);
                        if (guard is not null && !IsFlowPure(guard))
                        {
                            Report("COPE-FLOW-0018", "Flow guards may use only pure local expressions and board reads in FLOW-M1.", transitionSyntax.WhenKeyword!);
                        }
                        transitions.Add(new BoundFlowTransition(@event.Name, transitionSyntax.TargetIdentifier.Text, guard, bindings, BindFlowUpdates(transitionSyntax.Body, boardType)));
                    }
                    finally
                    {
                        _scope = previousScope;
                    }
                }

                BoundFlowTerminal? terminal = BindFlowTerminal(stateSyntax.Terminal, boardType, declaredResult, declaredFailure);
                states.Add(new BoundFlowState(
                    stateSyntax.Identifier.Text,
                    "flow:" + flowName + ".state:" + stateSyntax.Identifier.Text,
                    stateSyntax.InitialKeyword is not null,
                    transitions,
                    terminal));
            }

            foreach (BoundFlowState state in states)
            {
                foreach (IGrouping<string, BoundFlowTransition> group in state.Transitions.GroupBy(transition => transition.EventName, StringComparer.Ordinal))
                {
                    if (group.Count() > 1)
                    {
                        Report("COPE-FLOW-0019", $"State '{state.Name}' has multiple transitions for event '{group.Key}'. FLOW-M1 rejects ambiguous transition declarations.", declaration.Identifier);
                    }
                }
            }
            string initialState = initialStates.FirstOrDefault()?.Identifier.Text ?? declaration.States.FirstOrDefault()?.Identifier.Text ?? "<missing>";
            _flows.Add(new BoundFlowDefinition(flowName, "flow:" + flowName, boardType, boardFields, events, states, initialState, declaredResult, declaredFailure));
        }

        private IReadOnlyList<BoundFlowBoardUpdate> BindFlowUpdates(BlockStatementSyntax? body, RecordTypeSymbol boardType)
        {
            if (body is null) return [];
            var updates = new List<BoundFlowBoardUpdate>();
            foreach (StatementSyntax statement in body.Statements)
            {
                if (statement is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax member, Right: ExpressionSyntax value } })
                {
                    Report("COPE-FLOW-0020", "FLOW-M1 transition bodies may contain only 'board.field = expression;' updates.", body.OpenBraceToken);
                    continue;
                }
                if (member.Target is not NameExpressionSyntax { IdentifierToken.Text: "board" })
                {
                    Report("COPE-FLOW-0021", "Transition updates must assign an explicit board field.", member.NameToken);
                    continue;
                }
                RecordFieldSymbol? field = boardType.Fields.FirstOrDefault(candidate => candidate.Name == member.NameToken.Text);
                if (field is null)
                {
                    Report("COPE-FLOW-0022", $"Flow board has no field '{member.NameToken.Text}'.", member.NameToken);
                    continue;
                }
                BoundExpression expression = BindExpression(value, field.Type);
                if (expression.Type != PrimitiveTypeSymbol.Error && !IsAssignable(field.Type, expression.Type))
                {
                    Report("COPE-FLOW-0023", $"Board update for '{field.Name}' must have type '{field.Type.Name}', got '{expression.Type.Name}'.", member.NameToken);
                }
                if (!IsFlowPure(expression))
                {
                    Report("COPE-FLOW-0024", "FLOW-M1 transition updates may not call async, npm, CLR, batch, or inline-C# operations.", member.NameToken);
                }
                updates.Add(new BoundFlowBoardUpdate(field, expression));
            }
            return updates;
        }

        private BoundFlowTerminal? BindFlowTerminal(FlowTerminalSyntax? syntax, RecordTypeSymbol boardType, TypeSymbol resultType, TypeSymbol? failureType)
        {
            if (syntax is null) return null;
            Scope previousScope = _scope;
            _scope = new Scope(_global);
            try
            {
                _scope.TryDeclare(new VariableSymbol("board", boardType, true));
                TypeSymbol? expectedTerminalType = syntax.Keyword.Text == "finish" ? resultType : failureType;
                BoundExpression? expression = syntax.Expression is null ? null : BindExpression(syntax.Expression, expectedTerminalType);
                if (syntax.Keyword.Text == "finish" && resultType != PrimitiveTypeSymbol.Void && expression is null)
                {
                    Report("COPE-FLOW-0029", $"Flow completion requires a value of type '{resultType.Name}'.", syntax.Keyword);
                }
                if (syntax.Keyword.Text == "finish" && expression is not null && !IsAssignable(resultType, expression.Type))
                {
                    Report("COPE-FLOW-0028", $"Flow completion requires '{resultType.Name}', got '{expression.Type.Name}'.", syntax.Keyword);
                }
                if (syntax.Keyword.Text == "fail"
                    && failureType is not null
                    && expression is null)
                {
                    Report("COPE-FLOW-0030", $"Flow failure requires a value of type '{failureType.Name}'.", syntax.Keyword);
                }
                if (syntax.Keyword.Text == "fail"
                    && expression is not null
                    && expression.Type != PrimitiveTypeSymbol.Error
                    && (failureType is null || !IsAssignable(failureType, expression.Type)))
                {
                    Report("COPE-FLOW-0026", "Flow failure does not match its declared failure type.", syntax.Keyword);
                }
                if (expression is not null && !IsFlowPure(expression))
                {
                    Report("COPE-FLOW-0025", "FLOW-M1 terminal outcomes may use only pure expressions and board reads.", syntax.Keyword);
                }
                return new BoundFlowTerminal(syntax.Keyword.Text == "fail", expression);
            }
            finally
            {
                _scope = previousScope;
            }
        }

        private static bool IsFlowPure(BoundExpression expression)
            => expression switch
            {
                BoundLiteralExpression or BoundVariableExpression or BoundUnitExpression => true,
                BoundUnaryExpression unary => IsFlowPure(unary.Operand),
                BoundBinaryExpression binary => IsFlowPure(binary.Left) && IsFlowPure(binary.Right),
                BoundRecordFieldAccessExpression access => IsFlowPure(access.Receiver),
                _ => false,
            };

        private BoundFunctionDeclaration BindClassConstructor(FunctionSymbol function, ClassConstructorDeclarationSyntax syntax)
            => BindClassFunctionBody(function, syntax.Body, syntax.ConstructorKeyword);

        private BoundFunctionDeclaration BindClassFunction(FunctionSymbol function, ClassAssociatedFunctionDeclarationSyntax syntax)
            => BindClassFunctionBody(function, syntax.Body, syntax.Identifier);

        private BoundFunctionDeclaration BindClassFunctionBody(FunctionSymbol function, BlockStatementSyntax bodySyntax, SyntaxToken anchor)
        {
            FunctionSymbol? previousFunction = _currentFunction;
            ClassTypeSymbol? previousClass = _currentClass;
            Dictionary<string, TypeParameterSymbol>? previousTypeParameters = _activeTypeParameters;
            Scope previousScope = _scope;
            _currentFunction = function;
            _currentClass = function.ClassOwner;
            _activeTypeParameters = CreateTypeParameterScope(function.TypeParameters);
            _scope = new Scope(_global);
            try
            {
                foreach (ParameterSymbol parameter in function.Parameters)
                {
                    if (!_scope.TryDeclare(parameter))
                    {
                        Report("COPE-BIND-0005", $"Duplicate parameter '{parameter.Name}'.", anchor);
                    }
                }
                var body = (BoundBlockStatement)BindStatement(bodySyntax);
                if (function.IsClassConstructor && !AlwaysReturns(body))
                {
                    Report("COPE-CLASS-0008", $"Constructor '{function.ClassOwner!.Name}' can fall through without returning one complete class value.", anchor);
                }
                return new BoundFunctionDeclaration(function, body);
            }
            finally
            {
                _scope = previousScope;
                _currentFunction = previousFunction;
                _currentClass = previousClass;
                _activeTypeParameters = previousTypeParameters;
            }
        }

        private static bool AlwaysReturns(BoundStatement statement)
        {
            return statement switch
            {
                BoundReturnStatement => true,
                BoundCSharpBlockStatement block when block.ExpectedResultType != PrimitiveTypeSymbol.Void => true,
                BoundBlockStatement block => block.Statements.Any(AlwaysReturns),
                BoundIfStatement conditional when conditional.ElseStatement is not null
                    => AlwaysReturns(conditional.ThenStatement) && AlwaysReturns(conditional.ElseStatement),
                _ => false,
            };
        }

        private BoundStatement BindStatement(StatementSyntax s) => s switch
        {
            BlockStatementSyntax b => BindBlock(b),
            VariableDeclarationStatementSyntax v => BindVariable(v),
            ResourceUsingDeclarationStatementSyntax u => BindResourceUsing(u),
            CSharpBlockStatementSyntax c => BindCSharpBlock(c),
            ExpressionStatementSyntax e => BindExpressionStatement(e),
            IfStatementSyntax i => BindIf(i),
            WhileStatementSyntax w => BindWhile(w),
            ForStatementSyntax f => BindFor(f),
            ForOfStatementSyntax f => BindForOf(f),
            ReturnStatementSyntax r => BindReturn(r),
            YieldStatementSyntax y => BindYield(y),
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
                && v.Initializer is NameExpressionSyntax or GenericFunctionReferenceExpressionSyntax or CallExpressionSyntax or ArrowExpressionSyntax or CaptureExpressionSyntax;
            bool inferArrowLocal = v.Type is null && _arrowBodyDepth > 0;
            bool inferNumericLiteral = v.Type is null && v.Initializer is LiteralExpressionSyntax;
            bool inferInitializer = inferCallableReference || inferArrowLocal || inferNumericLiteral;
            var type = inferInitializer
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
                init = BindExpression(v.Initializer, inferInitializer ? null : type);
            }
            if (inferInitializer)
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
            if (_batchContexts.TryPeek(out BatchBindingContext? batch))
            {
                batch.LocalBindings.Add(varSym);
            }
            return new BoundVariableDeclaration(varSym, init);
        }

        private BoundStatement BindResourceUsing(ResourceUsingDeclarationStatementSyntax declaration)
        {
            BoundExpression initializer = BindExpression(declaration.Initializer);
            if (declaration.AwaitKeyword is not null)
            {
                Report("COPE-CLR-0008", "'await using' is parsed as TypeScript resource management but asynchronous CLR disposal is deferred beyond CTS-CLR-M1.", declaration.AwaitKeyword);
            }

            if (initializer.Type is not ClrTypeSymbol clrType
                || !typeof(IDisposable).IsAssignableFrom(clrType.RuntimeType))
            {
                Report("COPE-CLR-0007", $"Resource 'using' requires a CLR IDisposable value, got '{initializer.Type.Name}'.", declaration.Identifier);
            }

            var variable = new VariableSymbol(declaration.Identifier.Text, initializer.Type, true);
            if (!_scope.TryDeclare(variable))
            {
                Report("COPE-BIND-0002", $"Duplicate declaration '{variable.Name}'.", declaration.Identifier);
            }

            return new BoundResourceUsingDeclaration(variable, initializer);
        }

        private BoundStatement BindCSharpBlock(CSharpBlockStatementSyntax block)
        {
            if (_currentFunction is null)
            {
                Report("COPE-CSHARP-0002", "Inline C# blocks are valid only inside a Copeland function.", block.CSharpKeyword);
                return new BoundCSharpBlockStatement(block.BodyText, GetLineNumber(block.BodyPosition), PrimitiveTypeSymbol.Void, []);
            }

            if (_currentFunction.IsGenerator)
            {
                Report("COPE-GEN-0010", "Inline C# blocks are not supported inside generator bodies.", block.CSharpKeyword);
            }
            if (_currentFunction.IsAsync || ContainsCSharpAwait(block.BodyText))
            {
                Report("COPE-CSHARP-0003", "Inline C# async and await are deferred beyond CTS-CSHARP-BLOCKS-M1.", block.CSharpKeyword);
            }

            IReadOnlyDictionary<string, Symbol> visible = _scope.VisibleSymbols();
            var captureCandidates = visible
                .Where(pair => pair.Value is VariableSymbol or ParameterSymbol)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            IReadOnlySet<string> names = captureCandidates.Keys.ToHashSet(StringComparer.Ordinal);
            IReadOnlySet<string> referenced = CSharpCaptureAnalyzer.FindReferencedNames(block.BodyText, names);
            IReadOnlySet<string> assigned = CSharpCaptureAnalyzer.FindAssignedNames(block.BodyText, names);
            var captures = new List<BoundCSharpCapture>();
            foreach (string name in referenced.OrderBy(name => name, StringComparer.Ordinal))
            {
                TypeSymbol type = captureCandidates[name] switch
                {
                    VariableSymbol variable => variable.Type,
                    ParameterSymbol parameter => parameter.Type,
                    _ => PrimitiveTypeSymbol.Error,
                };
                if (!IsCSharpProjectable(type))
                {
                    Report("COPE-CSHARP-0004", $"Inline C# cannot capture '{name}' because '{type.Name}' has no CLR projection.", block.CSharpKeyword);
                    continue;
                }

                if (assigned.Contains(name))
                {
                    Report("COPE-CSHARP-0005", $"Inline C# cannot assign to captured Copeland binding '{name}'.", block.CSharpKeyword);
                }

                captures.Add(new BoundCSharpCapture(name, type));
            }

            TypeSymbol result = _currentFunction.ReturnType;
            if (!IsCSharpProjectable(result, allowVoid: true))
            {
                Report("COPE-CSHARP-0006", $"Inline C# cannot return '{result.Name}' because it has no CLR projection.", block.CSharpKeyword);
            }

            return new BoundCSharpBlockStatement(block.BodyText, GetLineNumber(block.BodyPosition), result, captures);
        }

        private int GetLineNumber(int position)
        {
            var line = 1;
            for (var index = 0; index < position && index < _tree.Text.Length; index++)
            {
                if (_tree.Text[index] == '\n') line++;
            }

            return line;
        }

        private static bool ContainsCSharpAwait(string bodyText)
            => CSharpCaptureAnalyzer.FindReferencedNames(bodyText, new HashSet<string>(["await"], StringComparer.Ordinal)).Contains("await");

        private static bool IsCSharpProjectable(TypeSymbol type, bool allowVoid = false)
        {
            if (type == PrimitiveTypeSymbol.Void)
            {
                return allowVoid;
            }

            return type is PrimitiveTypeSymbol or ArrayTypeSymbol or RecordTypeSymbol or ClrTypeSymbol;
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

        private BoundStatement BindForOf(ForOfStatementSyntax statement)
        {
            BoundExpression iterable = BindExpression(statement.Iterable);
            TypeSymbol elementType;
            if (iterable.Type is ArrayTypeSymbol array)
            {
                elementType = array.ElementType;
                iterable = new BoundArrayIterableExpression(iterable, new IterableTypeSymbol(array.ElementType));
            }
            else if (iterable.Type is IterableTypeSymbol sequence)
            {
                elementType = sequence.ElementType;
            }
            else
            {
                elementType = PrimitiveTypeSymbol.Error;
                Report("COPE-GEN-0008", "The source of 'for...of' must have type Iterable<T> or T[].", statement.OfKeyword);
            }

            Scope previousScope = _scope;
            _scope = new Scope(previousScope);
            _loopDepth++;
            try
            {
                var variable = new VariableSymbol(
                    statement.Identifier.Text,
                    elementType,
                    statement.DeclarationKeyword.Kind == SyntaxKind.ConstKeyword);
                if (!_scope.TryDeclare(variable))
                {
                    Report("COPE-BIND-0002", $"Duplicate declaration '{variable.Name}'.", statement.Identifier);
                }
                return new BoundForOfStatement(variable, iterable, BindStatement(statement.Body));
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
            if (_currentFunction?.IsGenerator == true)
            {
                if (r.Expression is not null)
                {
                    Report("COPE-GEN-0005", "Generator functions cannot return a value; use 'return;' or 'yield break;'.", r.ReturnKeyword);
                }
                return new BoundReturnStatement(null);
            }
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
                expr = AdaptExactIntegerLiteral(expr, result.SuccessType);
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

        private BoundStatement BindYield(YieldStatementSyntax statement)
        {
            if (_currentFunction?.IsGenerator != true)
            {
                Report("COPE-GEN-0003", "'yield' is valid only inside a generator function.", statement.YieldKeyword);
                return new BoundExpressionStatement(new BoundErrorExpression());
            }

            if (statement.BreakKeyword is not null)
            {
                return new BoundReturnStatement(null);
            }

            if (statement.Expression is null)
            {
                Report("COPE-GEN-0004", "A yielded value is required; use 'yield break;' to complete a generator.", statement.YieldKeyword);
                return new BoundYieldStatement(new BoundErrorExpression());
            }

            IterableTypeSymbol sequence = (IterableTypeSymbol)_currentFunction.ReturnType;
            BoundExpression expression = BindExpression(statement.Expression, sequence.ElementType);
            if (statement.StarToken is not null)
            {
                if (expression.Type is not IterableTypeSymbol delegated)
                {
                    Report("COPE-GEN-0009", "The source of 'yield*' must have type Iterable<T>.", statement.StarToken);
                }
                else if (!IsAssignable(sequence.ElementType, delegated.ElementType))
                {
                    ReportTypeMismatch("COPE-GEN-0006", sequence.ElementType, delegated.ElementType, statement.StarToken, null);
                }
                return new BoundYieldStatement(expression, isDelegating: true);
            }

            if (!IsAssignable(sequence.ElementType, expression.Type))
            {
                ReportTypeMismatch("COPE-GEN-0006", sequence.ElementType, expression.Type, statement.YieldKeyword, null);
            }
            return new BoundYieldStatement(expression);
        }

        private BoundExpression BindExpression(ExpressionSyntax s, TypeSymbol? contextualType = null)
        {
            var expression = s switch
            {
                LiteralExpressionSyntax l => BindLiteral(l),
                TemplateExpressionSyntax template => BindTemplate(template),
                NameExpressionSyntax n => BindName(n),
                ParenthesizedExpressionSyntax p => BindExpression(p.Expression, contextualType),
                PropagateExpressionSyntax p => BindPropagate(p),
                UnwrapExpressionSyntax u => BindUnwrap(u),
                TryExceptExpressionSyntax t => BindTryExcept(t, contextualType),
                BatchExpressionSyntax batch => BindBatch(batch),
                AwaitExpressionSyntax a => BindAwait(a),
                UnaryExpressionSyntax u => BindUnary(u),
                BinaryExpressionSyntax b => BindBinary(b),
                AssignmentExpressionSyntax a => BindAssignment(a),
                CallExpressionSyntax c => BindCall(c, contextualType),
                NewExpressionSyntax n => BindNew(n),
                GenericCallExpressionSyntax c => BindGenericCall(c, contextualType),
                GenericFunctionReferenceExpressionSyntax reference => BindGenericFunctionReference(reference),
                ArrowExpressionSyntax arrow => BindArrow(arrow, contextualType, []),
                CaptureExpressionSyntax capture => BindCapture(capture, contextualType),
                ArrayLiteralExpressionSyntax a => BindArray(a, contextualType),
                ObjectLiteralExpressionSyntax o => BindObject(o, contextualType),
                MemberAccessExpressionSyntax m => BindMember(m),
                IndexExpressionSyntax i => BindIndex(i),
                WithExpressionSyntax w => BindWith(w),
                IfExpressionSyntax i => BindIfExpression(i, contextualType),
                MatchExpressionSyntax m => BindMatch(m, contextualType),
                TsXmlElementExpressionSyntax element => BindTsXml(element),
                TsXmlFragmentExpressionSyntax fragment => BindTsXml(fragment),
                UnsupportedExpressionSyntax u => BindUnsupportedClassExpression(u),
                _ => new BoundErrorExpression()
            };

            expression = AdaptExactIntegerLiteral(expression, contextualType);
            return InjectDirectNominalUnionCase(expression, contextualType);
        }

        private static BoundExpression AdaptExactIntegerLiteral(BoundExpression expression, TypeSymbol? contextualType)
        {
            if (expression is BoundLiteralExpression { Value: int } literal
                && contextualType is not null
                && TypeFacts.IsFloat(contextualType))
            {
                return new BoundLiteralExpression(Convert.ToDouble(literal.Value, System.Globalization.CultureInfo.InvariantCulture), contextualType);
            }

            if (expression is BoundUnaryExpression
                {
                    OperatorKind: SyntaxKind.MinusToken,
                    Operand: BoundLiteralExpression { Value: int } negativeLiteral
                }
                && contextualType is not null
                && TypeFacts.IsFloat(contextualType))
            {
                var adaptedOperand = new BoundLiteralExpression(
                    Convert.ToDouble(negativeLiteral.Value, System.Globalization.CultureInfo.InvariantCulture),
                    contextualType);
                return new BoundUnaryExpression(SyntaxKind.MinusToken, adaptedOperand, contextualType);
            }

            return expression;
        }

        private BoundExpression BindTemplate(TemplateExpressionSyntax template)
        {
            BoundExpression result = new BoundLiteralExpression(string.Empty, PrimitiveTypeSymbol.String);
            foreach (TemplatePartSyntax part in template.Parts)
            {
                BoundExpression next = part switch
                {
                    TemplateTextPartSyntax text => new BoundLiteralExpression(text.Text, PrimitiveTypeSymbol.String),
                    TemplateInterpolationPartSyntax interpolation => BindStringConversion(BindExpression(interpolation.Expression), template.TemplateToken, true),
                    _ => new BoundErrorExpression(),
                };
                result = new BoundBinaryExpression(result, SyntaxKind.PlusToken, next, PrimitiveTypeSymbol.String);
            }
            return result;
        }

        private BoundExpression BindTsXml(TsXmlExpressionSyntax expression)
        {
            if (_tsXmlProfile == CopelandTsXmlProfile.ReactM0)
            {
                return expression switch
                {
                    TsXmlElementExpressionSyntax element => BindReactTsXmlElement(element),
                    TsXmlFragmentExpressionSyntax fragment => BindReactTsXmlFragment(fragment),
                    _ => new BoundErrorExpression(),
                };
            }

            SyntaxToken token = expression switch
            {
                TsXmlElementExpressionSyntax element => element.LessToken,
                TsXmlFragmentExpressionSyntax fragment => fragment.LessToken,
                _ => throw new InvalidOperationException("Unknown TS-XML expression."),
            };
            Report(
                "COPE-TSXML-0101",
                "TS-XML syntax requires a semantic profile; no manifest, test, component, or compatibility profile is selected by this compilation.",
                token);
            return new BoundErrorExpression();
        }

        private BoundExpression BindReactTsXmlElement(TsXmlElementExpressionSyntax element)
        {
            if (!_scope.TryLookup("createElement", out Symbol? symbol)
                || symbol is not NpmFunctionSymbol createElement
                || createElement.PackageName != "react"
                || createElement.ExportName != "createElement")
            {
                Report("COPE-REACT-0001", "React TS-XML requires the bounded 'createElement' import from the materialized 'react' package.", element.NameToken);
                return new BoundErrorExpression();
            }

            string tagName = element.NameToken.Text;
            if (!IsSupportedReactIntrinsic(tagName))
            {
                Report("COPE-REACT-0002", $"React TS-XML intrinsic '<{tagName}>' is not supported by the bounded React M0 profile.", element.NameToken);
            }

            var properties = new List<BoundReactProperty>();
            foreach (TsXmlAttributeSyntax attribute in element.Attributes)
            {
                string propertyName = attribute.NameToken.Text;
                if (!IsSupportedReactProperty(tagName, propertyName))
                {
                    Report("COPE-REACT-0003", $"React TS-XML property '{propertyName}' is not supported on '<{tagName}>' in the bounded React M0 profile.", attribute.NameToken);
                    continue;
                }

                if (attribute.ExpressionValue is null && attribute.StringValueToken is null)
                {
                    Report("COPE-REACT-0004", $"React TS-XML property '{propertyName}' requires a value.", attribute.NameToken);
                    continue;
                }

                TypeSymbol? expectedType = propertyName == "onClick"
                    ? new CallableTypeSymbol([], PrimitiveTypeSymbol.Void)
                    : PrimitiveTypeSymbol.String;
                BoundExpression value = attribute.ExpressionValue is not null
                    ? BindExpression(attribute.ExpressionValue, expectedType)
                    : new BoundLiteralExpression(attribute.StringValueToken!.Value, PrimitiveTypeSymbol.String);
                bool isValidClickCallback = value.Type is CallableTypeSymbol callback
                    && callback.Parameters.Count == 0
                    && TypeFacts.AreEquivalent(callback.ReturnType, PrimitiveTypeSymbol.Void);
                if (propertyName == "onClick" && !isValidClickCallback)
                {
                    Report("COPE-REACT-0005", "React TS-XML 'onClick' requires a zero-parameter callback returning void. React's event argument is intentionally not exposed to Copeland M0 application logic.", attribute.NameToken);
                }
                else if (propertyName is "id" or "className" && value.Type != PrimitiveTypeSymbol.String)
                {
                    ReportTypeMismatch("COPE-TYPE-0005", PrimitiveTypeSymbol.String, value.Type, attribute.NameToken);
                }

                properties.Add(new BoundReactProperty(propertyName, value));
            }

            return new BoundReactElementExpression(createElement.Name, tagName, properties, BindReactChildren(element.Children));
        }

        private BoundExpression BindReactTsXmlFragment(TsXmlFragmentExpressionSyntax fragment)
        {
            Report("COPE-REACT-0006", "React TS-XML fragments are outside the bounded React M0 profile.", fragment.LessToken);
            return new BoundErrorExpression();
        }

        private IReadOnlyList<BoundExpression> BindReactChildren(IReadOnlyList<TsXmlChildSyntax> children)
        {
            var result = new List<BoundExpression>();
            foreach (TsXmlChildSyntax child in children)
            {
                switch (child)
                {
                    case TsXmlTextSyntax text when !string.IsNullOrWhiteSpace(text.TextToken.Text):
                        result.Add(new BoundLiteralExpression(text.TextToken.Text.Trim(), PrimitiveTypeSymbol.String));
                        break;
                    case TsXmlExpressionChildSyntax expression:
                    {
                        BoundExpression value = BindExpression(expression.Expression);
                        if (!IsReactChild(value.Type))
                        {
                            Report("COPE-REACT-0007", $"React TS-XML child expressions must be string, numeric, or ReactNode values; got '{value.Type.Name}'.", expression.OpenBraceToken);
                        }
                        result.Add(value);
                        break;
                    }
                    case TsXmlElementChildSyntax nested:
                        result.Add(BindTsXml(nested.Element));
                        break;
                }
            }
            return result;
        }

        private static bool IsSupportedReactIntrinsic(string name)
            => name is "main" or "h1" or "p" or "pre" or "button";

        private static bool IsSupportedReactProperty(string tagName, string name)
            => name is "id" or "className" || tagName == "button" && name == "onClick";

        private static bool IsReactChild(TypeSymbol type)
            => type == PrimitiveTypeSymbol.String || TypeFacts.IsNumeric(type) || type == ReactNodeTypeSymbol.Instance;

        private BoundExpression BindAwait(AwaitExpressionSyntax awaitExpression)
        {
            BoundExpression operand = BindExpression(awaitExpression.Operand);
            if (_currentFunction?.IsGenerator == true)
            {
                Report("COPE-GEN-0007", "'await' is not supported inside a synchronous generator.", awaitExpression.AwaitKeyword);
                return new BoundErrorExpression();
            }
            if (_currentFunction?.IsAsync != true)
            {
                Report("COPE-ASYNC-0001", "'await' is valid only inside an async function.", awaitExpression.AwaitKeyword);
                return new BoundErrorExpression();
            }

            if (operand.Type is not AsyncTypeSymbol asyncType)
            {
                Report("COPE-ASYNC-0002", $"'await' requires Async<T>, got '{operand.Type.Name}'.", awaitExpression.AwaitKeyword);
                return new BoundErrorExpression();
            }

            return new BoundAwaitExpression(operand, asyncType.EventualType);
        }

        private BoundExpression BindUnsupportedClassExpression(UnsupportedExpressionSyntax expression)
        {
            string message = expression.Token.Text switch
            {
                "new" => "'new' is not supported. Class construction is the pure call 'Person(...)'.",
                "this" => "'this' is not supported. Class operations declare ordinary explicit parameters.",
                "super" => "'super' is not supported because Copeland classes have no inheritance.",
                _ => "Unsupported class expression.",
            };
            Report("COPE-CLASS-0014", message, expression.Token);
            return new BoundErrorExpression();
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
            RecordBatchCapture(symbol);
            return symbol switch
            {
                ClassValueSymbol classValue => ReportClassValueUse(classValue, n.IdentifierToken),
                VariableSymbol v when v.Type is TableTypeSymbol table && _tableSingletonVariables.Contains(v) => new BoundTableReferenceExpression(table),
                VariableSymbol v => new BoundVariableExpression(v),
                ParameterSymbol p => new BoundVariableExpression(new VariableSymbol(p.Name, p.Type, true)),
                FunctionSymbol function when function.IsGeneric => ReportOpenGenericFunctionValue(n),
                FunctionSymbol function => new BoundFunctionReferenceExpression(function),
                NpmFunctionSymbol => ReportNpmFunctionValue(n),
                JavaScriptHostFunctionSymbol => ReportJavaScriptHostFunctionValue(n),
                _ => new BoundErrorExpression()
            };
        }

        private BoundExpression BindBatch(BatchExpressionSyntax batch)
        {
            BoundExpression input = BindExpression(batch.Input);
            if (input.Type is not ArrayTypeSymbol inputArray)
            {
                Report("COPE-BATCH-0002", $"Batch input must be a supported array, got '{input.Type.Name}'.", batch.BatchKeyword);
                return new BoundErrorExpression();
            }

            if (!IsBatchPortableType(inputArray.ElementType))
            {
                Report("COPE-BATCH-0003", $"Batch element type '{inputArray.ElementType.Name}' is not supported. Batch accepts primitive, string, and immutable record elements.", batch.BatchKeyword);
            }

            if (_batchContexts.Count != 0)
            {
                Report("COPE-BATCH-0011", "Nested batch expressions are not supported in CTS-BATCH-M1.", batch.BatchKeyword);
                return new BoundErrorExpression();
            }

            if (batch.Body.Statements.Count == 0 || batch.Body.Statements[^1] is not ReturnStatementSyntax finalReturn || finalReturn.Expression is null)
            {
                Report("COPE-BATCH-0004", "A batch body must end with exactly one value-producing 'return' statement.", batch.Body.OpenBraceToken);
                return new BoundErrorExpression();
            }

            if (batch.Body.Statements.Take(batch.Body.Statements.Count - 1).Any(statement => statement is not VariableDeclarationStatementSyntax and not ExpressionStatementSyntax))
            {
                Report("COPE-BATCH-0004", "A CTS-BATCH-M1 body may contain item-local declarations and expressions before its final return.", batch.Body.OpenBraceToken);
            }

            Scope previousScope = _scope;
            var context = new BatchBindingContext { Anchor = batch.BatchKeyword };
            var item = new VariableSymbol(batch.ItemIdentifier.Text, inputArray.ElementType, true);
            _scope = new Scope(previousScope);
            if (!_scope.TryDeclare(item))
            {
                Report("COPE-BATCH-0005", $"Batch item binding '{item.Name}' conflicts with an item-local declaration.", batch.ItemIdentifier);
            }
            context.LocalBindings.Add(item);
            _batchContexts.Push(context);
            try
            {
                var prefix = new List<BoundStatement>();
                foreach (StatementSyntax statement in batch.Body.Statements.Take(batch.Body.Statements.Count - 1))
                {
                    if (statement is CSharpBlockStatementSyntax)
                    {
                        Report("COPE-BATCH-0009", "Inline C# blocks are not supported inside batch bodies.", batch.BatchKeyword);
                    }
                    prefix.Add(BindStatement(statement));
                }

                BoundExpression value = BindExpression(finalReturn.Expression);
                ValidateBatchBodyEffects(value, batch.BatchKeyword);
                foreach (BoundStatement statement in prefix)
                {
                    ValidateBatchStatementEffects(statement, batch.BatchKeyword);
                }

                if (!IsBatchPortableType(value.Type))
                {
                    Report("COPE-BATCH-0006", $"Batch result type '{value.Type.Name}' is not supported.", finalReturn.ReturnKeyword);
                }

                return new BoundBatchExpression(input, item, new BoundValueBlock(prefix, value), new ArrayTypeSymbol(value.Type));
            }
            finally
            {
                _batchContexts.Pop();
                _scope = previousScope;
            }
        }

        private void RecordBatchCapture(Symbol symbol)
        {
            if (!_batchContexts.TryPeek(out BatchBindingContext? batch)
                || symbol is not VariableSymbol and not ParameterSymbol
                || batch.LocalBindings.Contains(symbol)
                || !batch.Captures.Add(symbol))
            {
                return;
            }

            TypeSymbol type = symbol is VariableSymbol variable ? variable.Type : ((ParameterSymbol)symbol).Type;
            bool isReadOnly = symbol is not VariableSymbol mutable || mutable.IsReadOnly;
            if (!isReadOnly)
            {
                Report("COPE-BATCH-0007", $"Batch cannot capture mutable binding '{symbol.Name}'.", batch.Anchor);
            }
            else if (!IsBatchPortableType(type))
            {
                Report("COPE-BATCH-0008", $"Batch capture '{symbol.Name}' of type '{type.Name}' is not proven immutable and portable.", batch.Anchor);
            }
        }

        private static bool IsBatchPortableType(TypeSymbol type)
            => type is PrimitiveTypeSymbol primitive
                && primitive != PrimitiveTypeSymbol.Void
                && primitive != PrimitiveTypeSymbol.Error
                || type is RecordTypeSymbol and not ClassTypeSymbol
                || type is ArrayTypeSymbol array && IsBatchPortableType(array.ElementType);

        private void ValidateBatchStatementEffects(BoundStatement statement, SyntaxToken anchor)
        {
            switch (statement)
            {
                case BoundVariableDeclaration declaration:
                    ValidateBatchBodyEffects(declaration.Initializer, anchor);
                    break;
                case BoundExpressionStatement expression:
                    ValidateBatchBodyEffects(expression.Expression, anchor);
                    break;
                default:
                    Report("COPE-BATCH-0010", "This statement is not supported inside a batch body.", anchor);
                    break;
            }
        }

        private void ValidateBatchBodyEffects(BoundExpression expression, SyntaxToken anchor)
        {
            switch (expression)
            {
                case BoundAwaitExpression or BoundNpmCallExpression or BoundNpmDirectCallExpression:
                    Report("COPE-BATCH-0012", "Async and npm operations are not supported inside batch bodies.", anchor);
                    return;
                case BoundClrInvocationExpression or BoundClrPropertyAccessExpression:
                    Report("COPE-BATCH-0013", "CLR interop is not supported inside batch bodies.", anchor);
                    return;
                case BoundInvokeExpression or BoundCallableConstructionExpression or BoundFunctionReferenceExpression:
                    Report("COPE-BATCH-0014", "Only direct synchronous Copeland function calls are supported inside batch bodies.", anchor);
                    return;
                case BoundPropagateExpression:
                    Report("COPE-BATCH-0015", "Result propagation cannot escape a batch item body; return an explicit Result value instead.", anchor);
                    return;
                case BoundAssignmentExpression assignment:
                    ValidateBatchBodyEffects(assignment.Expression, anchor);
                    return;
                case BoundUnaryExpression unary:
                    ValidateBatchBodyEffects(unary.Operand, anchor);
                    return;
                case BoundBinaryExpression binary:
                    ValidateBatchBodyEffects(binary.Left, anchor);
                    ValidateBatchBodyEffects(binary.Right, anchor);
                    return;
                case BoundCallExpression call:
                    if (call.Function.IsAsync)
                    {
                        Report("COPE-BATCH-0012", "Async Copeland calls are not supported inside batch bodies.", anchor);
                    }
                    foreach (BoundExpression argument in call.Arguments) ValidateBatchBodyEffects(argument, anchor);
                    return;
                case BoundArrayExpression array:
                    foreach (BoundExpression element in array.Elements) ValidateBatchBodyEffects(element, anchor);
                    return;
                case BoundArrayLengthExpression length:
                    ValidateBatchBodyEffects(length.Receiver, anchor);
                    return;
                case BoundArrayElementAccessExpression access:
                    ValidateBatchBodyEffects(access.Receiver, anchor);
                    ValidateBatchBodyEffects(access.Index, anchor);
                    return;
                case BoundArrayIterableExpression iterable:
                    ValidateBatchBodyEffects(iterable.Receiver, anchor);
                    return;
                case BoundRecordConstructionExpression record:
                    foreach (BoundRecordFieldInitializer initializer in record.Initializers) ValidateBatchBodyEffects(initializer.Value, anchor);
                    return;
                case BoundRecordFieldAccessExpression access:
                    ValidateBatchBodyEffects(access.Receiver, anchor);
                    return;
                case BoundRecordWithExpression update:
                    ValidateBatchBodyEffects(update.Source, anchor);
                    foreach (BoundRecordFieldInitializer replacement in update.Replacements) ValidateBatchBodyEffects(replacement.Value, anchor);
                    return;
                case BoundIfExpression conditional:
                    ValidateBatchBodyEffects(conditional.Condition, anchor);
                    ValidateBatchBodyEffects(conditional.ThenExpression, anchor);
                    ValidateBatchBodyEffects(conditional.ElseExpression, anchor);
                    return;
            }
        }

        private BoundExpression ReportNpmFunctionValue(NameExpressionSyntax name)
        {
            Report("COPE-NPM-0006", $"Imported npm function '{name.IdentifierToken.Text}' may only be called directly.", name.IdentifierToken);
            return new BoundErrorExpression();
        }

        private BoundExpression ReportJavaScriptHostFunctionValue(NameExpressionSyntax name)
        {
            Report("COPE-HOST-0003", $"Imported JavaScript host function '{name.IdentifierToken.Text}' may only be called directly or passed through its declared callable parameter.", name.IdentifierToken);
            return new BoundErrorExpression();
        }

        private BoundExpression ReportClassValueUse(ClassValueSymbol classValue, SyntaxToken anchor)
        {
            Report("COPE-CLASS-0005", $"Class '{classValue.Name}' is a constructor namespace, not a first-class callable value. Call '{classValue.Name}(...)' or reference one of its public associated functions.", anchor);
            return new BoundErrorExpression();
        }

        private BoundExpression BindCapture(CaptureExpressionSyntax capture, TypeSymbol? contextualType)
        {
            if (capture.Identifiers.Count > MaxCaptureCount)
            {
                Report("COPE-CALL-0020", $"Callable captures support at most {MaxCaptureCount} bindings.", capture.Identifiers[MaxCaptureCount]);
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var captures = new List<BoundExpression>();
            foreach (var identifier in capture.Identifiers)
            {
                if (!names.Add(identifier.Text))
                {
                    Report("COPE-CALL-0012", $"Capture '{identifier.Text}' is listed more than once.", identifier);
                    continue;
                }

                if (!_scope.TryLookup(identifier.Text, out var symbol) || symbol is not VariableSymbol and not ParameterSymbol)
                {
                    Report("COPE-CALL-0013", $"Capture '{identifier.Text}' must name an outer lexical runtime binding.", identifier);
                    continue;
                }

                captures.Add(BindName(new NameExpressionSyntax(identifier)));
            }

            return BindArrow(capture.Arrow, contextualType, captures);
        }

        private BoundExpression BindArrow(ArrowExpressionSyntax arrow, TypeSymbol? contextualType, IReadOnlyList<BoundExpression> captures)
        {
            if (++_callableExpressionDepth > MaxCallableExpressionNesting)
            {
                Report("COPE-CALL-0015", $"Callable expression nesting exceeds the {MaxCallableExpressionNesting} limit.", arrow.ArrowToken);
                _callableExpressionDepth--;
                return new BoundErrorExpression();
            }

            try
            {
                if (_nextLiftedCallableId >= MaxLiftedCallableDefinitions)
                {
                    Report("COPE-CALL-0016", $"Lifted callable definitions exceed the {MaxLiftedCallableDefinitions} limit.", arrow.ArrowToken);
                    return new BoundErrorExpression();
                }

                CallableTypeSymbol? expectedCallable = contextualType as CallableTypeSymbol;
                if (contextualType is not null && expectedCallable is null && contextualType != PrimitiveTypeSymbol.Error)
                {
                    Report("COPE-CALL-0007", $"Arrow expression requires a callable expected type, not '{contextualType.Name}'.", arrow.ArrowToken);
                }
                if (arrow.Parameters.Count > MaxCallableParameters)
                {
                    Report("COPE-CALL-0002", $"Callable types support at most {MaxCallableParameters} parameters.", arrow.ArrowToken);
                }

                var parameters = new List<ParameterSymbol>();
                for (var index = 0; index < arrow.Parameters.Count; index++)
                {
                    ArrowParameterSyntax parameter = arrow.Parameters[index];
                    TypeSymbol? expectedParameter = expectedCallable is not null && index < expectedCallable.Parameters.Count
                        ? expectedCallable.Parameters[index].Type
                        : null;
                    TypeSymbol parameterType;
                    if (parameter.Type is not null)
                    {
                        parameterType = BindType(parameter.Type, parameter.Identifier, "COPE-CALL-0011", "arrow parameter");
                        if (expectedParameter is not null && !TypeFacts.AreEquivalent(parameterType, expectedParameter))
                        {
                            ReportTypeMismatch("COPE-CALL-0006", expectedParameter, parameterType, parameter.Identifier);
                        }
                    }
                    else if (expectedParameter is not null)
                    {
                        parameterType = expectedParameter;
                    }
                    else
                    {
                        Report("COPE-CALL-0011", $"Arrow parameter '{parameter.Identifier.Text}' needs an explicit type or an exact contextual callable signature.", parameter.Identifier);
                        parameterType = PrimitiveTypeSymbol.Error;
                    }
                    parameters.Add(new ParameterSymbol(parameter.Identifier.Text, parameterType));
                }

                if (expectedCallable is not null && expectedCallable.Parameters.Count != parameters.Count)
                {
                    Report("COPE-CALL-0005", $"Callable invocation argument count mismatch: expected {expectedCallable.Parameters.Count}, got {parameters.Count}.", arrow.ArrowToken);
                }

                TypeSymbol returnType = arrow.ReturnType is not null
                    ? BindType(arrow.ReturnType, arrow.ArrowToken, "COPE-CALL-0011", "arrow return")
                    : expectedCallable?.ReturnType ?? PrimitiveTypeSymbol.Error;
                if (arrow.ReturnType is null && expectedCallable is null && arrow.BlockBody is not null)
                {
                    Report("COPE-CALL-0011", "A block-bodied arrow needs an explicit return type or an exact contextual callable signature.", arrow.ArrowToken);
                }

                string name = "__cope_arrow_" + _nextLiftedCallableId++.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var captureParameters = captures.Select((capture, index) =>
                    new ParameterSymbol(GetCaptureName(capture, index), capture.Type, isCaptured: true)).ToArray();
                var codeParameters = captureParameters.Concat(parameters).ToArray();
                var bindingFunction = new FunctionSymbol(name, codeParameters, returnType, stableIdentity: "callable-arrow:" + name);
                var previousScope = _scope;
                var previousFunction = _currentFunction;
                _scope = new Scope(_global);
                _currentFunction = bindingFunction;
                foreach (ParameterSymbol parameter in codeParameters)
                {
                    if (!_scope.TryDeclare(parameter)) Report("COPE-BIND-0005", $"Duplicate parameter '{parameter.Name}'.", arrow.ArrowToken);
                }

                BoundBlockStatement body;
                _arrowBodyDepth++;
                try
                {
                    if (arrow.ExpressionBody is not null)
                    {
                        ReportImplicitArrowCaptures(arrow.ExpressionBody, previousScope, parameters, captureParameters.Select(parameter => parameter.Name).ToArray());
                        BoundExpression expression = BindExpression(arrow.ExpressionBody, returnType == PrimitiveTypeSymbol.Error ? null : returnType);
                        if (returnType == PrimitiveTypeSymbol.Error) returnType = expression.Type;
                        body = new BoundBlockStatement([new BoundReturnStatement(expression)]);
                    }
                    else
                    {
                        ReportImplicitArrowCaptures(arrow.BlockBody!, previousScope, parameters, captureParameters.Select(parameter => parameter.Name).ToArray());
                        body = (BoundBlockStatement)BindStatement(arrow.BlockBody!);
                    }
                }
                finally
                {
                    _arrowBodyDepth--;
                }
                _scope = previousScope;
                _currentFunction = previousFunction;

                if (expectedCallable is not null && !TypeFacts.AreEquivalent(returnType, expectedCallable.ReturnType))
                {
                    ReportTypeMismatch("COPE-CALL-0006", expectedCallable.ReturnType, returnType, arrow.ArrowToken);
                }
                var function = new FunctionSymbol(name, codeParameters, returnType, stableIdentity: "callable-arrow:" + name);
                _functions.Add(new BoundFunctionDeclaration(function, body));
                var callableType = new CallableTypeSymbol(
                    parameters.Select(parameter => new CallableParameterTypeSymbol(parameter.Name, parameter.Type)).ToArray(),
                    returnType);
                return captures.Count == 0
                    ? new BoundFunctionReferenceExpression(function)
                    : new BoundCallableConstructionExpression(function, captures, callableType);
            }
            finally
            {
                _callableExpressionDepth--;
            }
        }

        private void ReportImplicitArrowCaptures(SyntaxNode body, Scope outerScope, IReadOnlyList<ParameterSymbol> parameters, IReadOnlyList<string> captureIdentifiers)
        {
            var parameterNames = parameters.Select(parameter => parameter.Name)
                .Concat(captureIdentifiers)
                .ToHashSet(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var work = new Stack<object>();
            work.Push(body);
            while (work.Count > 0)
            {
                object current = work.Pop();
                if (current is NameExpressionSyntax name
                    && !parameterNames.Contains(name.IdentifierToken.Text)
                    && outerScope.TryLookup(name.IdentifierToken.Text, out var symbol)
                    && symbol is VariableSymbol or ParameterSymbol
                    && visited.Add(name.IdentifierToken.Text))
                {
                    Report("COPE-CALL-0017", $"Implicit lexical capture of '{name.IdentifierToken.Text}' is forbidden. Use 'capture {{ {name.IdentifierToken.Text} }} ...' to snapshot it into an immutable callable environment.", name.IdentifierToken);
                }

                if (current is SyntaxNode node)
                {
                    foreach (object child in node.GetChildren()) work.Push(child);
                }
            }
        }

        private static string GetCaptureName(BoundExpression capture, int index)
            => capture is BoundVariableExpression variable
                ? variable.Variable.Name
                : "__capture_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
                SyntaxKind.NumberToken => BindNumberLiteral(l),
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
            if (op == SyntaxKind.MinusToken && TypeFacts.IsNumeric(operand.Type)) return new BoundUnaryExpression(op, operand, operand.Type);
            if (op == SyntaxKind.BangToken && operand.Type == PrimitiveTypeSymbol.Boolean) return new BoundUnaryExpression(op, operand, PrimitiveTypeSymbol.Boolean);
            Report("COPE-TYPE-0006", $"Invalid unary operand for '{u.OperatorToken.Text}'.", u.OperatorToken);
            return new BoundErrorExpression();
        }

        private BoundExpression BindBinary(BinaryExpressionSyntax b)
        {
            if (b.OperatorToken.Kind == SyntaxKind.PipeGreaterToken)
            {
                return BindPipeline(b);
            }

            var l = BindExpression(b.Left); var r = BindExpression(b.Right); var op = b.OperatorToken.Kind;
            if (TypeFacts.IsFloat(l.Type) && r is BoundLiteralExpression { Value: int } integerLiteral)
            {
                r = new BoundLiteralExpression(Convert.ToDouble(integerLiteral.Value, System.Globalization.CultureInfo.InvariantCulture), l.Type);
            }
            else if (TypeFacts.IsFloat(r.Type) && l is BoundLiteralExpression { Value: int } integerLiteralLeft)
            {
                l = new BoundLiteralExpression(Convert.ToDouble(integerLiteralLeft.Value, System.Globalization.CultureInfo.InvariantCulture), r.Type);
            }
            if (op is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.BangEqualsEqualsToken)
            {
                if (l.Type is AsyncTypeSymbol || r.Type is AsyncTypeSymbol)
                {
                    Report("COPE-ASYNC-0003", "Equality is not supported for Async values.", b.OperatorToken);
                    return new BoundErrorExpression();
                }
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
                if (l.Type is ClassTypeSymbol || r.Type is ClassTypeSymbol)
                {
                    Report("COPE-CLASS-0016", "Class equality is not supported; source programs have no class identity law.", b.OperatorToken);
                    return new BoundErrorExpression();
                }
                Report("COPE-REC-0016", "Record equality is not supported.", b.OperatorToken);
                return new BoundErrorExpression();
            }
            if (TypeFacts.IsNumeric(l.Type) && TypeFacts.IsNumeric(r.Type)
                && op is SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken)
            {
                if (TypeFacts.AreEquivalent(l.Type, r.Type))
                {
                    return new BoundBinaryExpression(l, op, r, l.Type);
                }

                Report("COPE-NUM-0002", $"Cannot apply '{b.OperatorToken.Text}' to {l.Type.Name} and {r.Type.Name}. Copeland does not implicitly widen stored int values; use Float.From(integerValue).", b.OperatorToken);
                return new BoundErrorExpression();
            }
            if (op == SyntaxKind.PlusToken && l.Type == PrimitiveTypeSymbol.String && r.Type == PrimitiveTypeSymbol.String)
                return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.String);
            if (op is SyntaxKind.LessToken or SyntaxKind.LessOrEqualsToken or SyntaxKind.GreaterToken or SyntaxKind.GreaterOrEqualsToken)
            {
                if (TypeFacts.IsNumeric(l.Type) && TypeFacts.AreEquivalent(l.Type, r.Type)) return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Boolean);
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
            string diagnosticMessage = l.Type is ResultTypeSymbol || r.Type is ResultTypeSymbol
                ? $"Operator '{b.OperatorToken.Text}' cannot operate on Result values ('{l.Type.Name}' and '{r.Type.Name}'). Propagate with '?' or handle the Result before applying the operator."
                : op == SyntaxKind.PlusToken
                    && ((l.Type == PrimitiveTypeSymbol.String && TypeFacts.IsNumeric(r.Type))
                        || (TypeFacts.IsNumeric(l.Type) && r.Type == PrimitiveTypeSymbol.String))
                    ? $"Cannot add string and {(l.Type == PrimitiveTypeSymbol.String ? r.Type.Name : l.Type.Name)}. Copeland does not perform implicit conversions or host coercion; use String.From(value), String(value), interpolation, or a typed CLR formatting API where interop formatting is intentionally required."
                    : $"Operator '{b.OperatorToken.Text}' does not support operands of type '{l.Type.Name}' and '{r.Type.Name}'.";
            Report("COPE-TYPE-0007", diagnosticMessage, b.OperatorToken);
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
            if (symbol is ParameterSymbol { IsCaptured: true })
            {
                Report("COPE-CALL-0018", $"Captured binding '{n.IdentifierToken.Text}' is immutable inside the callable.", n.IdentifierToken);
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
            if (TryBindNumericConversion(c, out BoundExpression? conversion))
            {
                return conversion!;
            }

            if (_tsXmlProfile == CopelandTsXmlProfile.ReactM0
                && c.Target is MemberAccessExpressionSyntax reactMember)
            {
                BoundExpression receiver = BindExpression(reactMember.Target);
                if (receiver.Type == ReactRootTypeSymbol.Instance)
                {
                    return BindReactRootMember(c, reactMember, receiver);
                }
            }

            if (c.Target is MemberAccessExpressionSyntax staticMember
                && TryResolveClrTypeReference(staticMember.Target, out Type? staticType))
            {
                return BindClrMethodCall(c, staticType!, receiver: null, staticMember.NameToken);
            }

            if (c.Target is MemberAccessExpressionSyntax unresolvedClrMember
                && _clrNamespaces.Count > 0
                && TryGetQualifiedName(unresolvedClrMember.Target, out string unresolvedClrName, out SyntaxToken unresolvedClrAnchor)
                && !_scope.TryLookup(unresolvedClrName.Split('.')[0], out _)
                && !_classTypes.ContainsKey(unresolvedClrName.Split('.')[0])
                && !_enumTypes.ContainsKey(unresolvedClrName.Split('.')[0]))
            {
                Report("COPE-CLR-0001", $"CLR type '{unresolvedClrName}' was not found in imported CLR namespaces or supplied references.", unresolvedClrAnchor);
                return new BoundErrorExpression();
            }

            if (c.Target is MemberAccessExpressionSyntax instanceMember)
            {
                if (instanceMember.Target is not NameExpressionSyntax instanceTargetName
                    || (!_classTypes.ContainsKey(instanceTargetName.IdentifierToken.Text)
                        && !_enumTypes.ContainsKey(instanceTargetName.IdentifierToken.Text)))
                {
                    BoundExpression receiver = BindExpression(instanceMember.Target);
                    if (receiver.Type is ClrTypeSymbol clrReceiver)
                    {
                        return BindClrMethodCall(c, clrReceiver.RuntimeType, receiver, instanceMember.NameToken);
                    }
                }
            }

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
                if (_classTypes.TryGetValue(enumName.IdentifierToken.Text, out var classType))
                {
                    return BindAssociatedFunctionCall(c, m, classType);
                }
                return BindEnumConstructorCall(c, m, enumName);
            }

            if (c.Target is not NameExpressionSyntax)
            {
                return BindInvoke(c, BindExpression(c.Target));
            }

            var targetName = (NameExpressionSyntax)c.Target;
            if (_classTypes.TryGetValue(targetName.IdentifierToken.Text, out var targetClass))
            {
                return BindClassConstructorCall(c, targetName, targetClass);
            }
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
            if (s is NpmFunctionSymbol npm) return BindNpmCall(c, npm);
            if (s is CopelandPackageFunctionSymbol packageFunction) return BindCopelandPackageCall(c, packageFunction);
            if (s is JavaScriptHostFunctionSymbol host) return BindJavaScriptHostCall(c, host);
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

        private BoundExpression BindReactRootMember(CallExpressionSyntax call, MemberAccessExpressionSyntax member, BoundExpression root)
        {
            if (member.NameToken.Text == "render")
            {
                if (call.Arguments.Count != 1)
                {
                    Report("COPE-REACT-0008", "ReactRoot.render requires exactly one ReactNode argument.", call.OpenParenToken);
                    return new BoundErrorExpression();
                }

                BoundExpression node = BindExpression(call.Arguments[0], ReactNodeTypeSymbol.Instance);
                if (node.Type != ReactNodeTypeSymbol.Instance)
                {
                    ReportTypeMismatch("COPE-TYPE-0005", ReactNodeTypeSymbol.Instance, node.Type, call.OpenParenToken);
                }
                return new BoundReactRootRenderExpression(root, node);
            }

            if (member.NameToken.Text == "unmount" && call.Arguments.Count == 0)
            {
                return new BoundReactRootUnmountExpression(root);
            }

            Report("COPE-REACT-0009", $"ReactRoot supports only render(ReactNode) and unmount() in the bounded React M0 profile, not '{member.NameToken.Text}'.", member.NameToken);
            return new BoundErrorExpression();
        }

        private BoundExpression BindNumberLiteral(LiteralExpressionSyntax literal)
        {
            if (literal.LiteralToken.Value is int integer)
            {
                return new BoundLiteralExpression(integer, PrimitiveTypeSymbol.Int);
            }

            if (literal.LiteralToken.Value is double floating && double.IsFinite(floating))
            {
                return new BoundLiteralExpression(floating, PrimitiveTypeSymbol.Float);
            }

            Report("COPE-NUM-0001", "Invalid or unsupported numeric literal. Copeland int literals must be signed 32-bit values and float literals must be finite.", literal.LiteralToken);
            return new BoundErrorExpression();
        }

        /// <summary>
        /// Binds <c>value |&gt; callable</c> by presenting the existing call binder
        /// with the exact same syntax shape as <c>callable(value)</c>. There is no
        /// bound pipeline node: every later phase sees an ordinary call or invoke.
        /// </summary>
        private BoundExpression BindPipeline(BinaryExpressionSyntax pipeline)
        {
            if (pipeline.Right is CallExpressionSyntax or GenericCallExpressionSyntax)
            {
                Report(
                    "COPE-PIPE-0001",
                    "The right side of '|>' must be a callable reference, not a completed call. For additional arguments, wrap the call in an arrow: value |> ((item: T) => f(item, x)).",
                    pipeline.OperatorToken);
                return new BoundErrorExpression();
            }

            var syntheticCall = new CallExpressionSyntax(
                pipeline.Right,
                pipeline.OperatorToken,
                [pipeline.Left],
                [],
                pipeline.OperatorToken);

            return BindCall(syntheticCall, contextualType: null);
        }

        private bool TryBindNumericConversion(CallExpressionSyntax call, out BoundExpression? conversion)
        {
            conversion = null;
            if (call.Target is NameExpressionSyntax name)
            {
                if (name.IdentifierToken.Text == "String")
                {
                    conversion = BindStringConversionArgument(call, name.IdentifierToken);
                    return true;
                }
                if (name.IdentifierToken.Text == "Float")
                {
                    conversion = BindFloatConversion(call, name.IdentifierToken);
                    return true;
                }
                if (name.IdentifierToken.Text == "Int")
                {
                    Report("COPE-NUM-0005", "Cannot convert float to int without a rounding policy. Use Int.Floor(value), Int.Ceil(value), Int.Round(value), or Int.Truncate(value).", name.IdentifierToken);
                    conversion = new BoundErrorExpression();
                    return true;
                }
                return false;
            }

            if (call.Target is not MemberAccessExpressionSyntax { Target: NameExpressionSyntax typeName } member)
            {
                return false;
            }

            if (typeName.IdentifierToken.Text == "String" && member.NameToken.Text == "From")
            {
                conversion = BindStringConversionArgument(call, member.NameToken);
                return true;
            }
            if (typeName.IdentifierToken.Text == "Float" && member.NameToken.Text == "From")
            {
                conversion = BindFloatConversion(call, member.NameToken);
                return true;
            }
            if (typeName.IdentifierToken.Text != "Int")
            {
                return false;
            }

            BoundNumericConversionKind? kind = member.NameToken.Text switch
            {
                "Floor" => BoundNumericConversionKind.IntFloor,
                "Ceil" => BoundNumericConversionKind.IntCeil,
                "Round" => BoundNumericConversionKind.IntRound,
                "Truncate" => BoundNumericConversionKind.IntTruncate,
                _ => null,
            };
            if (kind is not null)
            {
                conversion = BindIntRoundingConversion(call, member.NameToken, kind.Value);
                return true;
            }
            if (member.NameToken.Text == "From")
            {
                Report("COPE-NUM-0005", "Int.From accepts only int identity values. Convert float values with Int.Floor, Int.Ceil, Int.Round, or Int.Truncate.", member.NameToken);
                conversion = new BoundErrorExpression();
                return true;
            }
            return false;
        }

        private BoundExpression BindStringConversionArgument(CallExpressionSyntax call, SyntaxToken anchor)
        {
            if (call.Arguments.Count != 1)
            {
                Report("COPE-NUM-0003", "String.From expects exactly one argument.", anchor);
                return new BoundErrorExpression();
            }
            return BindStringConversion(BindExpression(call.Arguments[0]), anchor, false);
        }

        private BoundExpression BindStringConversion(BoundExpression operand, SyntaxToken anchor, bool interpolation)
        {
            if (operand.Type == PrimitiveTypeSymbol.String)
            {
                return operand;
            }
            if (operand.Type == PrimitiveTypeSymbol.Boolean || TypeFacts.IsNumeric(operand.Type))
            {
                return new BoundNumericConversionExpression(BoundNumericConversionKind.StringFrom, operand, PrimitiveTypeSymbol.String);
            }
            string action = interpolation ? "interpolate" : "convert";
            Report("COPE-NUM-0004", $"Cannot {action} value of type '{operand.Type.Name}' as a canonical string. String.From supports string, boolean, int, and float.", anchor);
            return new BoundErrorExpression();
        }

        private BoundExpression BindFloatConversion(CallExpressionSyntax call, SyntaxToken anchor)
        {
            if (call.Arguments.Count != 1)
            {
                Report("COPE-NUM-0003", "Float.From expects exactly one argument.", anchor);
                return new BoundErrorExpression();
            }
            BoundExpression operand = BindExpression(call.Arguments[0]);
            if (TypeFacts.IsFloat(operand.Type)) return operand;
            if (TypeFacts.IsInt(operand.Type)) return new BoundNumericConversionExpression(BoundNumericConversionKind.IntToFloat, operand, PrimitiveTypeSymbol.Float);
            Report("COPE-NUM-0006", $"Cannot convert '{operand.Type.Name}' to float. Float.From supports int and float values; parsing text is not part of conversion.", anchor);
            return new BoundErrorExpression();
        }

        private BoundExpression BindIntRoundingConversion(CallExpressionSyntax call, SyntaxToken anchor, BoundNumericConversionKind kind)
        {
            if (call.Arguments.Count != 1)
            {
                Report("COPE-NUM-0003", $"{anchor.Text} expects exactly one argument.", anchor);
                return new BoundErrorExpression();
            }
            BoundExpression operand = BindExpression(call.Arguments[0]);
            if (!TypeFacts.IsFloat(operand.Type))
            {
                Report("COPE-NUM-0007", $"Int.{anchor.Text} requires float, got '{operand.Type.Name}'.", anchor);
                return new BoundErrorExpression();
            }
            return new BoundNumericConversionExpression(kind, operand, PrimitiveTypeSymbol.Int);
        }

        private BoundExpression BindNew(NewExpressionSyntax expression)
        {
            if (!TryResolveClrTypeReference(expression.Target, out Type? type))
            {
                Report("COPE-CLR-0001", "CLR constructor target was not found. CLR 'using' directives resolve only CLR namespaces and types.", expression.NewKeyword);
                return new BoundErrorExpression();
            }

            ConstructorInfo[] publicConstructors = type!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(_clrResolver.IsMemberVisible)
                .ToArray();
            if (publicConstructors.Length == 0
                && type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Length > 0)
            {
                Report("COPE-CLR-0004", $"CLR constructor '{type.FullName}' is inaccessible.", expression.NewKeyword);
                return new BoundErrorExpression();
            }

            return BindClrInvocation(
                expression.Arguments,
                expression.OpenParenToken,
                publicConstructors.Cast<MethodBase>(),
                receiver: null,
                memberDisplayName: type.FullName ?? type.Name);
        }

        private BoundExpression BindClrMethodCall(CallExpressionSyntax call, Type declaringType, BoundExpression? receiver, SyntaxToken memberName)
        {
            BindingFlags dispatchFlags = receiver is null ? BindingFlags.Static : BindingFlags.Instance;
            MethodInfo[] publicMethods = declaringType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | dispatchFlags)
                .Where(method => string.Equals(method.Name, memberName.Text, StringComparison.Ordinal)
                    && !method.IsSpecialName
                    && _clrResolver.IsMemberVisible(method))
                .ToArray();
            if (publicMethods.Length == 0
                && declaringType.GetMethods(BindingFlags.NonPublic | dispatchFlags).Any(method => string.Equals(method.Name, memberName.Text, StringComparison.Ordinal)))
            {
                Report("COPE-CLR-0004", $"CLR member '{declaringType.FullName}.{memberName.Text}' is inaccessible.", memberName);
                return new BoundErrorExpression();
            }

            return BindClrInvocation(call.Arguments, call.OpenParenToken, publicMethods.Cast<MethodBase>(), receiver, declaringType.FullName + "." + memberName.Text);
        }

        private BoundExpression BindClrInvocation(
            IReadOnlyList<ExpressionSyntax> argumentSyntax,
            SyntaxToken anchor,
            IEnumerable<MethodBase> members,
            BoundExpression? receiver,
            string memberDisplayName)
        {
            MethodBase[] candidates = members.ToArray();
            BoundExpression[] arguments = BindClrArguments(argumentSyntax, candidates);
            var applicable = new List<(MethodBase Member, IReadOnlyList<TypeSymbol> GenericArguments)>();
            var unsupportedShape = false;

            foreach (MethodBase candidateMember in candidates)
            {
                if (!TryGetClrInvocationShape(candidateMember, arguments, out IReadOnlyList<TypeSymbol> candidateGenericArguments, out bool shapeUnsupported))
                {
                    unsupportedShape |= shapeUnsupported;
                    continue;
                }

                applicable.Add((candidateMember, candidateGenericArguments));
            }

            if (applicable.Count == 0)
            {
                string diagnosticId = unsupportedShape ? "COPE-CLR-0007" : "COPE-CLR-0005";
                string message = unsupportedShape
                    ? $"CLR member '{memberDisplayName}' has an unsupported member or type shape for CTS-CLR-M1."
                    : $"No applicable CLR overload for '{memberDisplayName}' with ({string.Join(", ", arguments.Select(argument => argument.Type.Name))}).";
                Report(diagnosticId, message, anchor);
                return new BoundErrorExpression();
            }

            if (applicable.Count > 1)
            {
                Report("COPE-CLR-0006", $"CLR overload resolution for '{memberDisplayName}' is ambiguous for ({string.Join(", ", arguments.Select(argument => argument.Type.Name))}).", anchor);
                return new BoundErrorExpression();
            }

            (MethodBase selectedMember, IReadOnlyList<TypeSymbol> selectedGenericArguments) = applicable[0];
            if (!TryProjectClrInvocationReturnType(selectedMember, selectedGenericArguments, out TypeSymbol resultType))
            {
                Report("COPE-CLR-0007", $"CLR return type for '{memberDisplayName}' is not supported by CTS-CLR-M1.", anchor);
                return new BoundErrorExpression();
            }

            return new BoundClrInvocationExpression(selectedMember, receiver, selectedGenericArguments, arguments, resultType);
        }

        private BoundExpression[] BindClrArguments(IReadOnlyList<ExpressionSyntax> argumentSyntax, IReadOnlyList<MethodBase> candidates)
        {
            if (candidates.Count != 1)
            {
                return argumentSyntax.Select(argument => BindExpression(argument)).ToArray();
            }

            ParameterInfo[] parameters = candidates[0].GetParameters();
            return argumentSyntax.Select((argument, index) =>
            {
                TypeSymbol? expected = index < parameters.Length && TryProjectClrType(parameters[index].ParameterType, out TypeSymbol projected)
                    ? projected
                    : null;
                return BindExpression(argument, expected);
            }).ToArray();
        }

        private bool TryGetClrInvocationShape(MethodBase member, IReadOnlyList<BoundExpression> arguments, out IReadOnlyList<TypeSymbol> genericArguments, out bool shapeUnsupported)
        {
            genericArguments = [];
            shapeUnsupported = false;
            ParameterInfo[] parameters = member.GetParameters();
            int requiredParameterCount = parameters.Count(parameter => !parameter.IsOptional);
            if (arguments.Count < requiredParameterCount
                || arguments.Count > parameters.Length
                || parameters.Any(parameter => parameter.ParameterType.IsByRef || parameter.IsOut || parameter.GetCustomAttributes(typeof(ParamArrayAttribute), inherit: false).Length > 0))
            {
                shapeUnsupported = parameters.Any(parameter => parameter.ParameterType.IsByRef || parameter.IsOut);
                return false;
            }

            var substitutions = new Dictionary<Type, TypeSymbol>();
            if (member is MethodInfo method && method.IsGenericMethodDefinition)
            {
                foreach ((ParameterInfo parameter, BoundExpression argument) in parameters.Zip(arguments))
                {
                    if (parameter.ParameterType.IsGenericParameter)
                    {
                        if (substitutions.TryGetValue(parameter.ParameterType, out TypeSymbol? existing)
                            && !TypeFacts.AreEquivalent(existing, argument.Type))
                        {
                            return false;
                        }

                        substitutions[parameter.ParameterType] = argument.Type;
                    }
                    else if (!IsClrArgumentCompatible(argument.Type, parameter.ParameterType))
                    {
                        return false;
                    }
                }

                if (method.GetGenericArguments().Any(argument => !substitutions.ContainsKey(argument)))
                {
                    shapeUnsupported = true;
                    return false;
                }

                genericArguments = method.GetGenericArguments().Select(argument => substitutions[argument]).ToArray();
                return true;
            }

            if (member is MethodInfo nonGeneric && nonGeneric.ContainsGenericParameters)
            {
                shapeUnsupported = true;
                return false;
            }

            return parameters.Zip(arguments).All(pair => IsClrArgumentCompatible(pair.Second.Type, pair.First.ParameterType));
        }

        private bool TryProjectClrInvocationReturnType(MethodBase member, IReadOnlyList<TypeSymbol> genericArguments, out TypeSymbol projected)
        {
            if (member is ConstructorInfo constructor)
            {
                return TryProjectClrType(constructor.DeclaringType!, out projected);
            }

            MethodInfo method = (MethodInfo)member;
            if (!method.IsGenericMethodDefinition || !method.ReturnType.IsGenericParameter)
            {
                return TryProjectClrType(method.ReturnType, out projected);
            }

            int ordinal = method.ReturnType.GenericParameterPosition;
            projected = genericArguments[ordinal];
            return true;
        }

        private static bool IsClrArgumentCompatible(TypeSymbol source, Type target)
        {
            if (target == typeof(int) && source == PrimitiveTypeSymbol.Int)
            {
                return true;
            }

            if (target == typeof(double) && TypeFacts.IsFloat(source))
            {
                return true;
            }

            if (target == typeof(string) && source == PrimitiveTypeSymbol.String)
            {
                return true;
            }

            if (target == typeof(bool) && source == PrimitiveTypeSymbol.Boolean)
            {
                return true;
            }

            if (target.IsArray && target.GetArrayRank() == 1 && source is ArrayTypeSymbol array)
            {
                return IsClrArgumentCompatible(array.ElementType, target.GetElementType()!);
            }

            return source is ClrTypeSymbol clr && clr.RuntimeType == target;
        }

        private bool TryProjectClrType(Type type, out TypeSymbol projected)
        {
            if (type == typeof(void)) { projected = PrimitiveTypeSymbol.Void; return true; }
            if (type == typeof(string)) { projected = PrimitiveTypeSymbol.String; return true; }
            if (type == typeof(bool)) { projected = PrimitiveTypeSymbol.Boolean; return true; }
            if (type == typeof(int)) { projected = PrimitiveTypeSymbol.Int; return true; }
            if (type == typeof(double)) { projected = PrimitiveTypeSymbol.Float; return true; }
            if (type == typeof(float) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte)) { projected = PrimitiveTypeSymbol.Error; return false; }
            if (type.IsArray && type.GetArrayRank() == 1 && TryProjectClrType(type.GetElementType()!, out TypeSymbol element)) { projected = new ArrayTypeSymbol(element); return true; }
            if (type.IsGenericParameter) { projected = PrimitiveTypeSymbol.Error; return false; }
            if (type == typeof(object) || type.IsEnum || Nullable.GetUnderlyingType(type) is not null || type.IsPointer || type.IsByRef || type.ContainsGenericParameters) { projected = PrimitiveTypeSymbol.Error; return false; }
            if (_clrResolver.IsTypeVisible(type)) { projected = new ClrTypeSymbol(type); return true; }
            projected = PrimitiveTypeSymbol.Error;
            return false;
        }


        private void BindClrUsingDirectives(CompilationUnitSyntax root)
        {
            foreach (ClrUsingDirectiveSyntax directive in root.Members.OfType<ClrUsingDirectiveSyntax>())
            {
                string qualifiedName = directive.QualifiedName;
                IReadOnlyList<Type> exactTypes = _clrResolver.FindTypes(qualifiedName);
                IReadOnlyList<Type> namespaceTypes = _clrResolver.FindTypesInNamespace(qualifiedName);
                if (exactTypes.Count == 0 && namespaceTypes.Count == 0)
                {
                    Report("COPE-CLR-0001", $"CLR namespace or type '{qualifiedName}' was not found in the supplied framework/reference metadata.", directive.NameParts[0]);
                    continue;
                }

                if (namespaceTypes.Count > 0)
                {
                    _clrNamespaces.Add(qualifiedName);
                }

                foreach (Type type in exactTypes)
                {
                    AddClrImportedType(type, directive.NameParts[^1]);
                }

                foreach (Type type in namespaceTypes)
                {
                    AddClrImportedType(type, directive.NameParts[^1]);
                }
            }
        }

        private void AddClrImportedType(Type type, SyntaxToken anchor)
        {
            if (HasLocalClrConflict(type.Name))
            {
                Report("COPE-CLR-0009", $"CLR-imported type '{type.Name}' conflicts with a local Copeland declaration.", anchor);
                return;
            }

            if (!_clrImportedTypes.TryGetValue(type.Name, out List<Type>? candidates))
            {
                candidates = [];
                _clrImportedTypes.Add(type.Name, candidates);
            }

            if (!candidates.Contains(type))
            {
                candidates.Add(type);
            }
        }

        private bool HasLocalClrConflict(string name)
            => _recordTypes.ContainsKey(name)
                || _classTypes.ContainsKey(name)
                || _enumTypes.ContainsKey(name)
                || _tableTypes.ContainsKey(name)
                || _aliases.ContainsKey(name)
                || _interfaces.ContainsKey(name)
                || _global.TryLookup(name, out _);

        private bool TryResolveClrTypeReference(ExpressionSyntax syntax, out Type? type)
        {
            type = null;
            if (!TryGetQualifiedName(syntax, out string qualifiedName, out SyntaxToken anchor))
            {
                return false;
            }

            if (!qualifiedName.Contains('.', StringComparison.Ordinal))
            {
                if (!_clrImportedTypes.TryGetValue(qualifiedName, out List<Type>? candidates))
                {
                    return false;
                }

                if (candidates.Count != 1)
                {
                    Report("COPE-CLR-0002", $"CLR type '{qualifiedName}' is ambiguous across imported CLR namespaces: {string.Join(", ", candidates.Select(candidate => candidate.FullName))}.", anchor);
                    return false;
                }

                type = candidates[0];
                return true;
            }

            IReadOnlyList<Type> exact = _clrResolver.FindTypes(qualifiedName);
            if (exact.Count == 1)
            {
                type = exact[0];
                return true;
            }

            if (exact.Count > 1)
            {
                Report("COPE-CLR-0002", $"CLR type '{qualifiedName}' is ambiguous across supplied references.", anchor);
            }

            return false;
        }

        private static bool TryGetQualifiedName(ExpressionSyntax syntax, out string name, out SyntaxToken anchor)
        {
            var parts = new Stack<string>();
            anchor = new SyntaxToken(SyntaxKind.IdentifierToken, 0, string.Empty, null);
            ExpressionSyntax current = syntax;
            while (current is MemberAccessExpressionSyntax member)
            {
                parts.Push(member.NameToken.Text);
                anchor = member.NameToken;
                current = member.Target;
            }

            if (current is not NameExpressionSyntax root)
            {
                name = string.Empty;
                return false;
            }

            parts.Push(root.IdentifierToken.Text);
            anchor = root.IdentifierToken;
            name = string.Join('.', parts);
            return true;
        }

        private void BindCopelandPackageImports(CompilationUnitSyntax root)
        {
            foreach (ImportDeclarationSyntax import in root.Members.OfType<ImportDeclarationSyntax>())
            {
                SyntaxToken[] tokens = import.Tokens.ToArray();
                SyntaxToken? moduleToken = tokens.LastOrDefault(token => token.Kind == SyntaxKind.StringToken);
                if (moduleToken?.Value is not string specifier || IsRelativeSpecifier(specifier))
                {
                    continue;
                }

                if (!_packageContracts.TryGetModules(specifier, out IReadOnlyList<CopelandPackageModuleContract>? candidates))
                {
                    continue;
                }

                if (candidates.Count != 1)
                {
                    string owners = string.Join(", ", _packageContracts.Contracts
                        .Where(contract => contract.Modules.Any(module => module.Specifier == specifier))
                        .Select(contract => contract.PackageId)
                        .OrderBy(id => id, StringComparer.Ordinal));
                    Report("COPE-PACKAGE-0006", $"Copeland module '{specifier}' is ambiguous across NuGet package contracts: {owners}.", moduleToken);
                    continue;
                }

                if (_npmResolver.TryGetPackage(specifier, out CopelandNpmPackageContract? npmPackage) && npmPackage is not null)
                {
                    Report("COPE-PACKAGE-0006", $"Copeland module '{specifier}' is ambiguous between NuGet package contract '{_packageContracts.Contracts.Single(contract => contract.Modules.Contains(candidates[0])).PackageId}' and npm contract '{npmPackage.PackageName}'.", moduleToken);
                    continue;
                }

                CopelandPackageModuleContract module = candidates[0];
                CopelandPackageContract owner = _packageContracts.Contracts.Single(contract => contract.Modules.Contains(module));
                if (_packageBackend != CopelandPackageBackend.Clr || module.ClrRealization is null)
                {
                    string backend = _packageBackend == CopelandPackageBackend.JavaScriptNode ? "Node" : _packageBackend == CopelandPackageBackend.JavaScriptBrowser ? "browser" : "CLR";
                    string available = module.ClrRealization is null ? "none" : "clr.binary";
                    Report("COPE-PACKAGE-0007", $"Copeland module '{specifier}' from package '{owner.PackageId}' has no realization for {backend}; available realizations: {available}.", moduleToken);
                    continue;
                }

                int open = Array.FindIndex(tokens, token => token.Kind == SyntaxKind.OpenBraceToken);
                int close = Array.FindIndex(tokens, token => token.Kind == SyntaxKind.CloseBraceToken);
                if (open < 0 || close <= open)
                {
                    Report("COPE-PACKAGE-0008", "Copeland package imports must use named imports of the form 'import { name } from \"module\"'.", tokens[0]);
                    continue;
                }

                for (int index = open + 1; index < close; index += 1)
                {
                    SyntaxToken exportToken = tokens[index];
                    if (exportToken.Kind != SyntaxKind.IdentifierToken)
                    {
                        continue;
                    }

                    SyntaxToken localToken = exportToken;
                    if (index + 2 < close && tokens[index + 1].Text == "as" && tokens[index + 2].Kind == SyntaxKind.IdentifierToken)
                    {
                        localToken = tokens[index + 2];
                        index += 2;
                    }

                    CopelandPackageExportContract? export = module.Exports.SingleOrDefault(candidate => candidate.Name == exportToken.Text);
                    if (export is null)
                    {
                        Report("COPE-PACKAGE-0009", $"Copeland module '{specifier}' from package '{owner.PackageId}' has no named export '{exportToken.Text}'.", exportToken);
                        continue;
                    }
                    if (export.Kind != "function")
                    {
                        Report("COPE-PACKAGE-0010", $"Copeland package export '{export.Name}' from module '{specifier}' has unsupported kind '{export.Kind}'. M1 supports only functions.", exportToken);
                        continue;
                    }

                    if (!TryBindCopelandPackageFunction(owner, module, export, localToken, out CopelandPackageFunctionSymbol? function))
                    {
                        continue;
                    }
                    CopelandPackageFunctionSymbol resolvedFunction = function!;
                    if (_global.TryLookup(localToken.Text, out Symbol? existing))
                    {
                        if (existing is CopelandPackageFunctionSymbol existingPackage && existingPackage.StableIdentity == resolvedFunction.StableIdentity)
                        {
                            continue;
                        }
                        Report("COPE-PACKAGE-0011", $"Imported Copeland package binding '{localToken.Text}' conflicts with an existing declaration.", localToken);
                        continue;
                    }

                    _global.TryDeclare(resolvedFunction);
                    _packageImports.Add(new BoundPackageImport(resolvedFunction));
                }
            }
        }

        private bool TryBindCopelandPackageFunction(
            CopelandPackageContract owner,
            CopelandPackageModuleContract module,
            CopelandPackageExportContract export,
            SyntaxToken anchor,
            out CopelandPackageFunctionSymbol? function)
        {
            function = null;
            CopelandClrBinaryRealization realization = module.ClrRealization!;
            Type[] types = _clrResolver.FindTypes(realization.AssemblyIdentity, export.ClrType).ToArray();
            if (types.Length != 1)
            {
                Report("COPE-PACKAGE-0012", $"Copeland package '{owner.PackageId}' contract for module '{module.Specifier}' names CLR facade '{export.ClrType}' in assembly '{realization.AssemblyIdentity}', but that public type was not resolved exactly once.", anchor);
                return false;
            }

            TypeSymbol[] parameterTypes = export.Parameters.Select(parameter => ResolveCopelandPackageType(parameter.Type, anchor)).ToArray();
            TypeSymbol returnType = ResolveCopelandPackageType(export.ReturnType, anchor);
            if (parameterTypes.Any(type => type == PrimitiveTypeSymbol.Error) || returnType == PrimitiveTypeSymbol.Error)
            {
                return false;
            }

            MethodInfo[] methods = types[0].GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == export.ClrMethod && method.GetParameters().Length == parameterTypes.Length)
                .Where(method => IsCopelandPackageMethodShape(method, parameterTypes, returnType))
                .ToArray();
            if (methods.Length != 1)
            {
                Report("COPE-PACKAGE-0013", $"Copeland package '{owner.PackageId}' contract/binary mismatch for '{module.Specifier}' export '{export.Name}': expected public static {export.ClrType}.{export.ClrMethod} with the declared signature.", anchor);
                return false;
            }

            function = new CopelandPackageFunctionSymbol(
                anchor.Text,
                owner.PackageId,
                module.Specifier,
                module.NominalScope,
                export.Name,
                export.Parameters.Select((parameter, index) => new ParameterSymbol(parameter.Name, parameterTypes[index])).ToArray(),
                returnType,
                methods[0]);
            return true;
        }

        private bool IsCopelandPackageMethodShape(MethodInfo method, IReadOnlyList<TypeSymbol> parameterTypes, TypeSymbol returnType)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Select((parameter, index) => IsClrArgumentCompatible(parameterTypes[index], parameter.ParameterType)).All(result => result)
                && TryProjectClrType(method.ReturnType, out TypeSymbol projectedReturn)
                && projectedReturn == returnType;
        }

        private TypeSymbol ResolveCopelandPackageType(string type, SyntaxToken anchor)
        {
            return type switch
            {
                "int" => PrimitiveTypeSymbol.Int,
                "float" => PrimitiveTypeSymbol.Float,
                "string" => PrimitiveTypeSymbol.String,
                "boolean" => PrimitiveTypeSymbol.Boolean,
                "void" => PrimitiveTypeSymbol.Void,
                _ => ReportUnsupportedCopelandPackageType(type, anchor),
            };
        }

        private TypeSymbol ReportUnsupportedCopelandPackageType(string type, SyntaxToken anchor)
        {
            Report("COPE-PACKAGE-0014", $"Copeland package contract type '{type}' is unsupported in M1. Supported function contract types are int, float, string, boolean, and void.", anchor);
            return PrimitiveTypeSymbol.Error;
        }

        private BoundExpression BindCopelandPackageCall(CallExpressionSyntax call, CopelandPackageFunctionSymbol function)
        {
            if (call.Arguments.Count != function.Parameters.Count)
            {
                Report("COPE-TYPE-0004", $"Argument count mismatch: expected {function.Parameters.Count}, got {call.Arguments.Count}.", call.OpenParenToken);
            }
            BoundExpression[] arguments = call.Arguments.Select((argument, index) => BindExpression(argument, index < function.Parameters.Count ? function.Parameters[index].Type : null)).ToArray();
            for (int index = 0; index < Math.Min(arguments.Length, function.Parameters.Count); index += 1)
            {
                if (!IsAssignable(function.Parameters[index].Type, arguments[index].Type))
                {
                    ReportTypeMismatch("COPE-TYPE-0005", function.Parameters[index].Type, arguments[index].Type, call.OpenParenToken);
                }
            }
            return call.Arguments.Count == function.Parameters.Count
                ? new BoundClrInvocationExpression(function.Method, null, [], arguments, function.ReturnType)
                : new BoundErrorExpression();
        }

        private static bool IsRelativeSpecifier(string specifier)
            => specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal);

        private void BindNpmImports(CompilationUnitSyntax root)
        {
            foreach (ImportDeclarationSyntax import in root.Members.OfType<ImportDeclarationSyntax>())
            {
                SyntaxToken[] tokens = import.Tokens.ToArray();
                SyntaxToken? module = tokens.LastOrDefault(token => token.Kind == SyntaxKind.StringToken);
                if (module?.Value is not string packageName)
                {
                    Report("COPE-NPM-0001", "npm imports require a string package specifier.", tokens[0]);
                    continue;
                }
                if (packageName.StartsWith("./", StringComparison.Ordinal)
                    || packageName.StartsWith("../", StringComparison.Ordinal))
                {
                    Report(
                        "COPE-MODULE-0001",
                        $"Relative import '{packageName}' is not supported because Copeland has no source-module resolver. Keep related declarations in one Copeland file or compose generated file-module APIs from C#; npm imports require a declared package contract.",
                        module);
                    continue;
                }
                // A declared native package module owns its bare specifier.
                // It never falls through into npm semantics after partial or
                // failed export binding.
                if (_packageContracts.TryGetModules(packageName, out _))
                {
                    continue;
                }
                if (!_npmResolver.TryGetPackage(packageName, out CopelandNpmPackageContract? package) || package is null)
                {
                    if (_hostResolver.TryGetModule(packageName, out _))
                    {
                        continue;
                    }
                    Report("COPE-NPM-0001", $"npm package '{packageName}' is unavailable in project configuration.", module);
                    continue;
                }
                if (!package.IsMaterialized)
                {
                    Report("COPE-NPM-0007", $"npm package '{packageName}' has a valid contract but no available runtime materialization.", module);
                    continue;
                }
                int open = Array.FindIndex(tokens, token => token.Kind == SyntaxKind.OpenBraceToken);
                int close = Array.FindIndex(tokens, token => token.Kind == SyntaxKind.CloseBraceToken);
                if (open < 0 || close <= open || tokens.Skip(close + 1).FirstOrDefault(token => token.Text == "from") is null)
                {
                    Report("COPE-NPM-0002", "Only named npm imports of the form 'import { name } from \"package\"' are supported.", tokens[0]);
                    continue;
                }
                SyntaxToken[] importTokens = tokens.Skip(open + 1).Take(close - open - 1).ToArray();
                for (int index = 0; index < importTokens.Length; index++)
                {
                    SyntaxToken exportToken = importTokens[index];
                    if (exportToken.Kind != SyntaxKind.IdentifierToken)
                    {
                        continue;
                    }

                    SyntaxToken localToken = exportToken;
                    if (index + 2 < importTokens.Length
                        && importTokens[index + 1].Kind == SyntaxKind.IdentifierToken
                        && string.Equals(importTokens[index + 1].Text, "as", StringComparison.Ordinal)
                        && importTokens[index + 2].Kind == SyntaxKind.IdentifierToken)
                    {
                        localToken = importTokens[index + 2];
                        index += 2;
                    }

                    if (package.Exports.Count == 0)
                    {
                        Report("COPE-NPM-0006", $"npm package '{packageName}' is declared but exposes no supported static contract.", exportToken);
                        continue;
                    }

                    if (localToken.Text.StartsWith("__cope_", StringComparison.Ordinal))
                    {
                        Report("COPE-NPM-0008", $"npm binding '{localToken.Text}' conflicts with compiler-reserved JavaScript helper names.", localToken);
                        continue;
                    }

                    CopelandNpmFunctionContract? export = package.Exports.SingleOrDefault(candidate => candidate.ExportName == exportToken.Text);
                    if (export is null)
                    {
                        Report("COPE-NPM-0003", $"npm package '{packageName}' has no supported named export '{exportToken.Text}'.", exportToken);
                        continue;
                    }
                    TypeSymbol[] parameters = export.ParameterTypes.Select(type => ResolveNpmType(type, exportToken)).ToArray();
                    TypeSymbol result = ResolveNpmType(export.ResultType, exportToken);
                    TypeSymbol? remoteError = export.RemoteErrorType is null ? null : ResolveNpmType(export.RemoteErrorType, exportToken);
                    var symbol = new NpmFunctionSymbol(localToken.Text, package.PackageName, package.Version, export.ExportName, parameters.Select((type, parameterIndex) => new ParameterSymbol("arg" + parameterIndex, type)).ToArray(), result, remoteError, export.IsPromise, package.IsAvailableToJavaScript, package.IsAvailableToClrSidecar);
                    if (_global.TryLookup(symbol.Name, out Symbol? existing))
                    {
                        if (existing is NpmFunctionSymbol existingNpm
                            && existingNpm.PackageName == symbol.PackageName
                            && existingNpm.PackageVersion == symbol.PackageVersion
                            && existingNpm.ExportName == symbol.ExportName)
                        {
                            continue;
                        }

                        Report("COPE-NPM-0004", $"Imported npm binding '{localToken.Text}' conflicts with an existing declaration.", localToken);
                        continue;
                    }
                    if (!_global.TryDeclare(symbol))
                    {
                        Report("COPE-NPM-0004", $"Imported npm binding '{localToken.Text}' conflicts with an existing declaration.", localToken);
                        continue;
                    }
                    _npmImports.Add(new BoundNpmImport(symbol));
                }
            }
        }

        private void BindJavaScriptHostImports(CompilationUnitSyntax root)
        {
            foreach (ImportDeclarationSyntax import in root.Members.OfType<ImportDeclarationSyntax>())
            {
                SyntaxToken[] tokens = import.Tokens.ToArray();
                SyntaxToken? module = tokens.LastOrDefault(token => token.Kind == SyntaxKind.StringToken);
                if (module?.Value is not string moduleSpecifier || !_hostResolver.TryGetModule(moduleSpecifier, out CopelandJavaScriptHostModuleContract? hostModule) || hostModule is null)
                {
                    continue;
                }

                int open = Array.FindIndex(tokens, token => token.Kind == SyntaxKind.OpenBraceToken);
                int close = Array.FindIndex(tokens, token => token.Kind == SyntaxKind.CloseBraceToken);
                if (open < 0 || close <= open || tokens.Skip(close + 1).FirstOrDefault(token => token.Text == "from") is null)
                {
                    Report("COPE-HOST-0001", "JavaScript host imports must use named imports of the form 'import { name } from \"host\"'.", tokens[0]);
                    continue;
                }

                SyntaxToken[] importTokens = tokens.Skip(open + 1).Take(close - open - 1).ToArray();
                for (int index = 0; index < importTokens.Length; index += 1)
                {
                    SyntaxToken exportToken = importTokens[index];
                    if (exportToken.Kind != SyntaxKind.IdentifierToken)
                    {
                        continue;
                    }

                    SyntaxToken localToken = exportToken;
                    if (index + 2 < importTokens.Length
                        && importTokens[index + 1].Kind == SyntaxKind.IdentifierToken
                        && string.Equals(importTokens[index + 1].Text, "as", StringComparison.Ordinal)
                        && importTokens[index + 2].Kind == SyntaxKind.IdentifierToken)
                    {
                        localToken = importTokens[index + 2];
                        index += 2;
                    }

                    CopelandJavaScriptHostFunctionContract? export = hostModule.Exports.SingleOrDefault(candidate => candidate.ExportName == exportToken.Text);
                    if (export is null)
                    {
                        Report("COPE-HOST-0002", $"JavaScript host module '{moduleSpecifier}' has no declared export '{exportToken.Text}'.", exportToken);
                        continue;
                    }

                    var typeParameters = new List<TypeParameterSymbol>();
                    var hostTypeParameters = new Dictionary<string, TypeParameterTypeSymbol>(StringComparer.Ordinal);
                    for (int typeParameterIndex = 0; typeParameterIndex < export.TypeParameters.Count; typeParameterIndex++)
                    {
                        string typeParameterName = export.TypeParameters[typeParameterIndex];
                        if (!hostTypeParameters.TryAdd(typeParameterName, new TypeParameterTypeSymbol(typeParameterName, typeParameterIndex)))
                        {
                            Report("COPE-HOST-0004", $"JavaScript host export '{export.ExportName}' declares duplicate type parameter '{typeParameterName}'.", exportToken);
                            continue;
                        }

                        typeParameters.Add(new TypeParameterSymbol(typeParameterName, hostTypeParameters[typeParameterName], new RequirementSet([], [])));
                    }

                    TypeSymbol[] parameters = export.ParameterTypes.Select(type => ResolveJavaScriptHostType(type, exportToken, hostTypeParameters)).ToArray();
                    TypeSymbol result = ResolveJavaScriptHostType(export.ResultType, exportToken, hostTypeParameters);
                    var symbol = new JavaScriptHostFunctionSymbol(
                        localToken.Text,
                        hostModule.ModuleSpecifier,
                        export.ExportName,
                        parameters.Select((type, parameterIndex) => new ParameterSymbol("arg" + parameterIndex, type)).ToArray(),
                        result,
                        typeParameters);
                    if (_global.TryLookup(symbol.Name, out _))
                    {
                        Report("COPE-HOST-0002", $"JavaScript host binding '{localToken.Text}' conflicts with an existing declaration.", localToken);
                        continue;
                    }

                    if (!_global.TryDeclare(symbol))
                    {
                        Report("COPE-HOST-0002", $"JavaScript host binding '{localToken.Text}' conflicts with an existing declaration.", localToken);
                        continue;
                    }

                    _javaScriptHostImports.Add(new BoundJavaScriptHostImport(symbol));
                }
            }
        }

        private TypeSymbol ResolveJavaScriptHostType(
            CopelandJavaScriptHostType type,
            SyntaxToken anchor,
            IReadOnlyDictionary<string, TypeParameterTypeSymbol>? typeParameters = null)
            => type switch
            {
                CopelandJavaScriptHostType.Primitive { Name: "int" } => PrimitiveTypeSymbol.Int,
                CopelandJavaScriptHostType.Primitive { Name: "string" } => PrimitiveTypeSymbol.String,
                CopelandJavaScriptHostType.Primitive { Name: "void" } => PrimitiveTypeSymbol.Void,
                CopelandJavaScriptHostType.Named { Name: "ReactMountElement" } when _tsXmlProfile == CopelandTsXmlProfile.ReactM0 => ReactMountElementTypeSymbol.Instance,
                CopelandJavaScriptHostType.Callable callable => new CallableTypeSymbol(
                    callable.Parameters.Select((parameter, index) => new CallableParameterTypeSymbol("arg" + index, ResolveJavaScriptHostType(parameter, anchor, typeParameters))).ToArray(),
                    ResolveJavaScriptHostType(callable.ReturnType, anchor, typeParameters)),
                CopelandJavaScriptHostType.TypeParameter parameter when typeParameters is not null && typeParameters.TryGetValue(parameter.Name, out TypeParameterTypeSymbol? resolved) => resolved,
                _ => ReportUnsupportedJavaScriptHostType(type, anchor),
            };

        private TypeSymbol ReportUnsupportedJavaScriptHostType(CopelandJavaScriptHostType type, SyntaxToken anchor)
        {
            Report("COPE-HOST-0004", $"JavaScript host contract type '{type}' is unsupported. Host contracts permit declared primitives, callable values, export-local type parameters, and selected opaque renderer identities.", anchor);
            return PrimitiveTypeSymbol.Error;
        }

        private BoundExpression BindJavaScriptHostCall(CallExpressionSyntax call, JavaScriptHostFunctionSymbol host)
        {
            if (call.Arguments.Count != host.Parameters.Count)
            {
                Report("COPE-TYPE-0004", $"Argument count mismatch: expected {host.Parameters.Count}, got {call.Arguments.Count}.", call.OpenParenToken);
            }

            BoundExpression[] arguments = call.Arguments
                .Select((argument, index) => BindExpression(argument, index < host.Parameters.Count ? host.Parameters[index].Type : null))
                .ToArray();
            for (int index = 0; index < Math.Min(arguments.Length, host.Parameters.Count); index += 1)
            {
                if (!IsAssignable(host.Parameters[index].Type, arguments[index].Type))
                {
                    ReportTypeMismatch("COPE-TYPE-0005", host.Parameters[index].Type, arguments[index].Type, call.OpenParenToken);
                }
            }

            return new BoundJavaScriptHostCallExpression(host, arguments);
        }

        private TypeSymbol ResolveNpmType(string name, SyntaxToken anchor)
        {
            if (_tsXmlProfile == CopelandTsXmlProfile.ReactM0)
            {
                if (name == "ReactNode") return ReactNodeTypeSymbol.Instance;
                if (name == "ReactRoot") return ReactRootTypeSymbol.Instance;
                if (name == "ReactMountElement") return ReactMountElementTypeSymbol.Instance;
            }
            if (name.EndsWith("[]", StringComparison.Ordinal))
            {
                string elementName = name[..^2];
                if (elementName.EndsWith("[]", StringComparison.Ordinal))
                {
                    Report("COPE-NPM-0005", $"npm contract type '{name}' is unsupported; nested arrays are outside the M1 value surface.", anchor);
                    return PrimitiveTypeSymbol.Error;
                }

                return new ArrayTypeSymbol(ResolveNpmType(elementName, anchor));
            }
            if (name is "number") return PrimitiveTypeSymbol.Number;
            if (name is "string") return PrimitiveTypeSymbol.String;
            if (name is "boolean") return PrimitiveTypeSymbol.Boolean;
            if (_recordTypes.TryGetValue(name, out RecordTypeSymbol? record) && record is not ClassTypeSymbol) return record;
            Report("COPE-NPM-0005", $"npm contract type '{name}' is unsupported or not a declared flat record.", anchor);
            return PrimitiveTypeSymbol.Error;
        }

        private BoundExpression BindNpmCall(CallExpressionSyntax call, NpmFunctionSymbol npm)
        {
            if (call.Arguments.Count != npm.Parameters.Count) Report("COPE-TYPE-0004", $"Argument count mismatch: expected {npm.Parameters.Count}, got {call.Arguments.Count}.", call.OpenParenToken);
            BoundExpression[] arguments = call.Arguments.Select((argument, index) => BindExpression(argument, index < npm.Parameters.Count ? npm.Parameters[index].Type : null)).ToArray();
            for (int index = 0; index < Math.Min(arguments.Length, npm.Parameters.Count); index++)
                if (!IsAssignable(npm.Parameters[index].Type, arguments[index].Type)) ReportTypeMismatch("COPE-TYPE-0005", npm.Parameters[index].Type, arguments[index].Type, call.OpenParenToken);
            if (call.Arguments.Count != npm.Parameters.Count)
            {
                return new BoundErrorExpression();
            }

            if (npm.RemoteErrorType is null && !npm.IsPromise)
            {
                return new BoundNpmDirectCallExpression(npm, arguments);
            }

            if (npm.RemoteErrorType is null)
            {
                Report("COPE-NPM-0009", "Promise-returning npm functions require a declared remote error type in this profile.", call.OpenParenToken);
                return new BoundErrorExpression();
            }

            RecordTypeSymbol argumentTupleType = GetOrCreateNpmTransportRecord("arguments", npm, npm.Parameters.Select(parameter => parameter.Type).ToArray());
            RecordTypeSymbol responseWrapperType = GetOrCreateNpmTransportRecord("response", npm, [npm.ResultType]);
            RecordTypeSymbol errorWrapperType = GetOrCreateNpmTransportRecord("error", npm, [npm.RemoteErrorType]);
            var argumentTuple = new BoundRecordConstructionExpression(
                argumentTupleType,
                arguments.Select((argument, index) => new BoundRecordFieldInitializer(argumentTupleType.Fields[index], argument)).ToArray());
            var responseWrapper = new BoundSyntheticTypeExpression(responseWrapperType);
            var errorWrapper = new BoundSyntheticTypeExpression(errorWrapperType);
            if (!TryGetOrCreateTsonEncodingPlan(argumentTuple, call.OpenParenToken, out BoundTsonEncodingPlan? requestPlan)
                || !TryGetOrCreateTsonEncodingPlan(responseWrapper, call.OpenParenToken, out BoundTsonEncodingPlan? responsePlan)
                || !TryGetOrCreateTsonEncodingPlan(errorWrapper, call.OpenParenToken, out BoundTsonEncodingPlan? errorPlan))
            {
                Report("COPE-NPM-0005", "npm function arguments, result, or remote error contain an unsupported transport value shape.", call.OpenParenToken);
                return new BoundErrorExpression();
            }
            _usesTsonEncode = true;
            return new BoundNpmCallExpression(npm, arguments, argumentTuple, requestPlan!, responsePlan!, errorPlan!, responseWrapperType.Fields[0], errorWrapperType.Fields[0]);
        }

        private RecordTypeSymbol GetOrCreateNpmTransportRecord(string role, NpmFunctionSymbol npm, IReadOnlyList<TypeSymbol> fields)
        {
            string signature = string.Join("|", new[] { role, npm.PackageName, npm.PackageVersion, npm.ExportName }.Concat(fields.Select(field => field.Name)));
            if (_npmTransportRecords.TryGetValue(signature, out RecordTypeSymbol? existing))
            {
                return existing;
            }

            string identitySuffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signature)))[..16].ToLowerInvariant();
            string name = "__NpmTransport_" + role + "_" + identitySuffix;
            string schemaIdentity = _schemaIdentity ?? throw new InvalidOperationException("npm transport records require validated schema metadata.");
            var record = new RecordTypeSymbol(name, new RecordTypeId(_nextRecordTypeId++), schemaIdentity + "#" + name);
            for (int index = 0; index < fields.Count; index++)
            {
                string fieldName = role == "arguments" ? "arg" + index : "value";
                record.AddField(new RecordFieldSymbol(fieldName, new RecordFieldId(record.Id, index), fields[index]));
            }

            _npmTransportRecords.Add(signature, record);
            _records.Add(new BoundRecordDeclaration(record));
            return record;
        }

        private BoundExpression BindClassConstructorCall(
            CallExpressionSyntax call,
            NameExpressionSyntax name,
            ClassTypeSymbol classType)
        {
            if (classType.Constructor is null)
            {
                Report("COPE-CLASS-0004", $"Class '{classType.Name}' has no valid primary constructor.", name.IdentifierToken);
                foreach (ExpressionSyntax argument in call.Arguments) _ = BindExpression(argument);
                return new BoundErrorExpression();
            }
            return BindKnownFunctionCall(call, classType.Constructor, "COPE-CLASS-0005");
        }

        private BoundExpression BindAssociatedFunctionCall(
            CallExpressionSyntax call,
            MemberAccessExpressionSyntax member,
            ClassTypeSymbol classType)
        {
            FunctionSymbol? function = classType.FindAssociatedFunction(member.NameToken.Text);
            if (function is null)
            {
                Report("COPE-CLASS-0006", $"Class '{classType.Name}' has no associated function '{member.NameToken.Text}'.", member.NameToken);
                foreach (ExpressionSyntax argument in call.Arguments) _ = BindExpression(argument);
                return new BoundErrorExpression();
            }
            if (!CanAccessClassMember(function.ClassOwner!, function.IsPublic))
            {
                Report("COPE-CLASS-0009", $"Private associated function '{classType.Name}.{function.MemberName}' is accessible only from code owned by '{classType.Name}'.", member.NameToken);
                foreach (ExpressionSyntax argument in call.Arguments) _ = BindExpression(argument);
                return new BoundErrorExpression();
            }
            if (function.IsGeneric)
            {
                return BindInferredGenericCall(call, function);
            }
            return BindKnownFunctionCall(call, function, "COPE-TYPE-0005");
        }

        private BoundExpression BindKnownFunctionCall(CallExpressionSyntax call, FunctionSymbol function, string typeMismatchId)
        {
            if (call.Arguments.Count != function.Parameters.Count)
            {
                Report("COPE-TYPE-0004", $"Argument count mismatch: expected {function.Parameters.Count}, got {call.Arguments.Count}.", call.OpenParenToken);
            }
            BoundExpression[] arguments = call.Arguments
                .Select((argument, index) => BindExpression(argument, index < function.Parameters.Count ? function.Parameters[index].Type : null))
                .ToArray();
            for (int index = 0; index < Math.Min(arguments.Length, function.Parameters.Count); index++)
            {
                if (!IsAssignable(function.Parameters[index].Type, arguments[index].Type))
                {
                    ReportTypeMismatch(
                        typeMismatchId,
                        function.Parameters[index].Type,
                        arguments[index].Type,
                        InferenceAnchor(call.Arguments[index]),
                        function.Parameters[index].AuthoredAliasName);
                }
            }
            return new BoundCallExpression(function, arguments);
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
            if (call.Target is NameExpressionSyntax transportName
                && transportName.IdentifierToken.Text == "tsonCall")
            {
                return BindTsonTransport(call, transportName);
            }

            if (call.Target is NameExpressionSyntax hostName
                && _global.TryLookup(hostName.IdentifierToken.Text, out var hostSymbol)
                && hostSymbol is JavaScriptHostFunctionSymbol host)
            {
                return BindGenericJavaScriptHostCall(call, host);
            }

            FunctionSymbol? function = null;
            if (call.Target is NameExpressionSyntax name
                && _global.TryLookup(name.IdentifierToken.Text, out var symbol)
                && symbol is FunctionSymbol namedFunction)
            {
                function = namedFunction;
            }
            else if (call.Target is MemberAccessExpressionSyntax member
                && member.Target is NameExpressionSyntax className
                && _classTypes.TryGetValue(className.IdentifierToken.Text, out var classType))
            {
                function = classType.FindAssociatedFunction(member.NameToken.Text);
                if (function is null)
                {
                    Report("COPE-CLASS-0006", $"Class '{classType.Name}' has no associated function '{member.NameToken.Text}'.", member.NameToken);
                    return new BoundErrorExpression();
                }
                if (!CanAccessClassMember(classType, function.IsPublic))
                {
                    Report("COPE-CLASS-0009", $"Private associated function '{classType.Name}.{function.MemberName}' is accessible only from code owned by '{classType.Name}'.", member.NameToken);
                    return new BoundErrorExpression();
                }
            }
            if (function is null)
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

        private BoundExpression BindGenericJavaScriptHostCall(
            GenericCallExpressionSyntax call,
            JavaScriptHostFunctionSymbol host)
        {
            if (!host.IsGeneric)
            {
                Report("COPE-GENERIC-0005", $"Function '{host.Name}' does not accept type arguments.", call.LessToken);
                return new BoundErrorExpression();
            }

            if (call.TypeArguments.Count != host.TypeParameters.Count)
            {
                Report("COPE-GENERIC-0007", $"Generic host function '{host.Name}' expects {host.TypeParameters.Count} type arguments, got {call.TypeArguments.Count}.", call.LessToken);
                return new BoundErrorExpression();
            }

            TypeSymbol[] typeArguments = call.TypeArguments
                .Select(argument => BindType(argument, call.LessToken, "COPE-GENERIC-0008", "type argument"))
                .ToArray();
            if (typeArguments.Any(IsOpenOrIllegalTypeArgument))
            {
                Report("COPE-GENERIC-0008", "Generic type arguments must be closed value types; interfaces and open type parameters are not allowed.", call.LessToken);
                return new BoundErrorExpression();
            }

            var substitutions = host.TypeParameters
                .Select((parameter, index) => new KeyValuePair<TypeSymbol, TypeSymbol>(parameter.Type, typeArguments[index]))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var specialized = new JavaScriptHostFunctionSymbol(
                host.Name,
                host.ModuleSpecifier,
                host.ExportName,
                host.Parameters
                    .Select(parameter => new ParameterSymbol(parameter.Name, SubstituteType(parameter.Type, substitutions), parameter.AuthoredAliasName))
                    .ToArray(),
                SubstituteType(host.ReturnType, substitutions));

            if (call.Arguments.Count != specialized.Parameters.Count)
            {
                Report("COPE-TYPE-0004", $"Argument count mismatch: expected {specialized.Parameters.Count}, got {call.Arguments.Count}.", call.OpenParenToken);
            }

            BoundExpression[] arguments = call.Arguments
                .Select((argument, index) => BindExpression(argument, index < specialized.Parameters.Count ? specialized.Parameters[index].Type : null))
                .ToArray();
            for (int index = 0; index < Math.Min(arguments.Length, specialized.Parameters.Count); index++)
            {
                if (!IsAssignable(specialized.Parameters[index].Type, arguments[index].Type))
                {
                    ReportTypeMismatch("COPE-TYPE-0005", specialized.Parameters[index].Type, arguments[index].Type, call.OpenParenToken);
                }
            }

            return new BoundJavaScriptHostCallExpression(specialized, arguments);
        }

        private BoundExpression BindTsonTransport(GenericCallExpressionSyntax call, NameExpressionSyntax intrinsicName)
        {
            if (call.TypeArguments.Count != 2 || call.Arguments.Count != 2)
            {
                foreach (ExpressionSyntax argument in call.Arguments)
                {
                    _ = BindExpression(argument);
                }
                Report("COPE-TSON-TRANSPORT-0001", "'tsonCall<Response, RemoteError>' requires two type arguments and two arguments: an operation string and a request record.", intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }

            TypeSymbol responseType = BindType(call.TypeArguments[0], call.LessToken, "COPE-TSON-TRANSPORT-0001", "response type");
            TypeSymbol remoteErrorType = BindType(call.TypeArguments[1], call.LessToken, "COPE-TSON-TRANSPORT-0001", "remote error type");
            BoundExpression operation = BindExpression(call.Arguments[0], PrimitiveTypeSymbol.String);
            BoundExpression request = BindExpression(call.Arguments[1]);
            if (!IsAssignable(PrimitiveTypeSymbol.String, operation.Type)
                || request.Type is not RecordTypeSymbol
                || responseType is not RecordTypeSymbol
                || remoteErrorType is not RecordTypeSymbol)
            {
                Report("COPE-TSON-TRANSPORT-0001", "'tsonCall' requires a string operation plus nominal record request, response, and remote-error types.", intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }
            if (_schemaIdentity is null)
            {
                Report("COPE-TSON-TRANSPORT-0002", "A compilation unit using 'tsonCall' requires one valid top-level '$schema' declaration.", intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }
            if (!TryGetOrCreateTsonEncodingPlan(request, intrinsicName.IdentifierToken, out BoundTsonEncodingPlan? requestPlan)
                || !TryGetOrCreateTsonEncodingPlan(new BoundSyntheticTypeExpression(responseType), intrinsicName.IdentifierToken, out BoundTsonEncodingPlan? responsePlan)
                || !TryGetOrCreateTsonEncodingPlan(new BoundSyntheticTypeExpression(remoteErrorType), intrinsicName.IdentifierToken, out BoundTsonEncodingPlan? remoteErrorPlan))
            {
                return new BoundErrorExpression();
            }
            if (!IsFlatTsonRecord((RecordTypeSymbol)request.Type)
                || !IsFlatTsonRecord((RecordTypeSymbol)responseType)
                || !IsFlatTsonRecord((RecordTypeSymbol)remoteErrorType))
            {
                Report("COPE-TSON-TRANSPORT-0003", "CTS-SIDECAR-M1 transport records may contain only boolean, number, and string fields.", intrinsicName.IdentifierToken);
                return new BoundErrorExpression();
            }

            _usesTsonEncode = true;
            var resultType = new ResultTypeSymbol(responseType, remoteErrorType);
            return new BoundTsonTransportExpression(operation, request, requestPlan!, responsePlan!, remoteErrorPlan!, new AsyncTypeSymbol(resultType));
        }

        private static bool IsFlatTsonRecord(RecordTypeSymbol record)
            => record.Fields.All(field => field.Type == PrimitiveTypeSymbol.Boolean
                || TypeFacts.IsNumeric(field.Type)
                || field.Type == PrimitiveTypeSymbol.String);

        private BoundExpression BindGenericFunctionReference(GenericFunctionReferenceExpressionSyntax reference)
        {
            FunctionSymbol? function = null;
            if (reference.Target is NameExpressionSyntax name
                && _scope.TryLookup(name.IdentifierToken.Text, out var symbol)
                && symbol is FunctionSymbol namedFunction)
            {
                function = namedFunction;
            }
            else if (reference.Target is MemberAccessExpressionSyntax member
                && member.Target is NameExpressionSyntax className
                && _classTypes.TryGetValue(className.IdentifierToken.Text, out var classType))
            {
                function = classType.FindAssociatedFunction(member.NameToken.Text);
                if (function is not null && !CanAccessClassMember(classType, function.IsPublic))
                {
                    Report("COPE-CLASS-0009", $"Private associated function '{classType.Name}.{function.MemberName}' is accessible only from code owned by '{classType.Name}'.", member.NameToken);
                    return new BoundErrorExpression();
                }
            }
            if (function is null)
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
                ClassTypeSymbol @class => @class.Fields.Where(field => field.IsPublic).Select(field => (field.Name, field.Type)).ToArray(),
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

        private string CreateFunctionStableIdentity(string name)
            => "function:" + (CreateDeclarationStableIdentity(name) ?? name);

        private string? CreateDeclarationStableIdentity(string name)
        {
            // A declared schema owns the transport identity even when the
            // declaration is compiled as one module in a project graph.  The
            // previous module-first order made otherwise valid npm contracts
            // unusable from tscl projects because their TSON plans require a
            // schema-owned record identity.
            if (_schemaIdentity is not null) return $"{_schemaIdentity}#{name}";
            return _moduleIdentity is null ? null : $"module:{_moduleIdentity}#{name}";
        }

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
                IterableTypeSymbol iterable => new IterableTypeSymbol(SubstituteType(iterable.ElementType, substitutions)),
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
                BoundForOfStatement loop => new BoundForOfStatement(RewriteVariable(loop.Variable), RewriteExpression(loop.Iterable), RewriteStatement(loop.Body)),
                BoundReturnStatement @return => new BoundReturnStatement(@return.Expression is null ? null : RewriteExpression(@return.Expression)),
                BoundYieldStatement yield => new BoundYieldStatement(yield.Expression is null ? null : RewriteExpression(yield.Expression), yield.IsDelegating),
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
                BoundCallableConstructionExpression construction => new BoundCallableConstructionExpression(
                    construction.Code,
                    construction.Captures.Select(RewriteExpression).ToArray(),
                    (CallableTypeSymbol)SubstituteType(construction.CallableType, substitutions)),
                BoundInvokeExpression invoke => new BoundInvokeExpression(RewriteExpression(invoke.Callee), invoke.Arguments.Select(RewriteExpression).ToArray(), (CallableTypeSymbol)SubstituteType(invoke.CallableType, substitutions)),
                BoundEnumValueExpression value => new BoundEnumValueExpression(value.Case, value.Arguments.Select(RewriteExpression).ToArray()),
                BoundPropagateExpression propagate => new BoundPropagateExpression(RewriteExpression(propagate.Operand), (ResultTypeSymbol)SubstituteType(propagate.ResultType, substitutions), propagate.Target),
                BoundUnwrapExpression unwrap => new BoundUnwrapExpression(RewriteExpression(unwrap.Operand), (ResultTypeSymbol)SubstituteType(unwrap.ResultType, substitutions)),
                BoundOkExpression ok => new BoundOkExpression(RewriteExpression(ok.Payload), (ResultTypeSymbol)SubstituteType(ok.Type, substitutions)),
                BoundErrExpression err => new BoundErrExpression(RewriteExpression(err.Payload), (ResultTypeSymbol)SubstituteType(err.Type, substitutions)),
                BoundArrayExpression array => new BoundArrayExpression(array.Elements.Select(RewriteExpression).ToArray(), SubstituteType(array.Type, substitutions)),
                BoundArrayLengthExpression length => new BoundArrayLengthExpression(RewriteExpression(length.Receiver)),
                BoundArrayElementAccessExpression access => new BoundArrayElementAccessExpression(RewriteExpression(access.Receiver), RewriteExpression(access.Index), (ArrayTypeSymbol)SubstituteType(access.ArrayType, substitutions)),
                BoundArrayIterableExpression iterable => new BoundArrayIterableExpression(RewriteExpression(iterable.Receiver), (IterableTypeSymbol)SubstituteType(iterable.Type, substitutions)),
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
            if (type is ClassTypeSymbol)
            {
                Report("COPE-CLASS-0017", $"Classes are invariant boundaries and cannot participate in TSON at '{path}'. Project to an ordinary record DTO first.", anchor);
                return false;
            }
            if (type == PrimitiveTypeSymbol.Boolean
                || TypeFacts.IsNumeric(type)
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

            if (expectedType is ClassTypeSymbol)
            {
                Report(
                    "COPE-CLASS-0017",
                    $"'tsonAsset' cannot construct class '{expectedType.Name}'. Use a public associated projection to a record DTO.",
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
                && !TypeFacts.IsNumeric(primitive)
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
                        when TypeFacts.IsNumeric(number):
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
                    || TypeFacts.IsNumeric(primitive)
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
                case PrimitiveTypeSymbol primitive when TypeFacts.IsNumeric(primitive) && value is TsonNumber number:
                    expression = primitive == PrimitiveTypeSymbol.Int && number.Value == Math.Truncate(number.Value) && number.Value >= int.MinValue && number.Value <= int.MaxValue
                        ? new BoundLiteralExpression((int)number.Value, primitive)
                        : new BoundLiteralExpression(number.Value, primitive);
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
                        || TypeFacts.IsNumeric(primitive)
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
            if (recordType is ClassTypeSymbol classType && !ReferenceEquals(_currentClass, classType))
            {
                Report("COPE-CLASS-0010", $"Class '{classType.Name}' can be constructed only by its primary constructor or associated code.", literal.OpenBraceToken);
                return new BoundErrorExpression();
            }

            var initializers = new List<BoundRecordFieldInitializer>();
            var seen = new HashSet<RecordFieldId>();
            foreach (var property in literal.Properties)
            {
                var field = recordType.Fields.FirstOrDefault(candidate => candidate.Name == property.NameToken.Text);
                if (field is null || property.NameToken.Kind != SyntaxKind.IdentifierToken)
                {
                    string diagnosticId = recordType is ClassTypeSymbol ? "COPE-CLASS-0007" : "COPE-REC-0007";
                    string subject = recordType is ClassTypeSymbol ? "Class" : "Record";
                    Report(diagnosticId, $"{subject} '{recordType.Name}' has no returned field '{property.NameToken.Text}'.", property.NameToken);
                    BindExpression(property.ValueExpression);
                    continue;
                }
                if (!seen.Add(field.Id))
                {
                    Report(recordType is ClassTypeSymbol ? "COPE-CLASS-0007" : "COPE-REC-0008", $"Field '{field.Name}' is initialized more than once.", property.NameToken);
                }

                var value = BindExpression(property.ValueExpression, field.Type);
                if (!IsAssignable(field.Type, value.Type))
                {
                    Report(recordType is ClassTypeSymbol ? "COPE-CLASS-0007" : "COPE-REC-0009", $"Initializer for '{recordType.Name}.{field.Name}' expected '{field.Type.Name}', got '{value.Type.Name}'.", property.NameToken);
                }
                initializers.Add(new BoundRecordFieldInitializer(field, value));
            }

            var missing = recordType.Fields.Where(field => !seen.Contains(field.Id)).Select(field => field.Name).ToArray();
            if (missing.Length > 0)
            {
                string subject = recordType is ClassTypeSymbol ? "Class" : "Record";
                Report(recordType is ClassTypeSymbol ? "COPE-CLASS-0007" : "COPE-REC-0006", $"{subject} '{recordType.Name}' is missing fields: {string.Join(", ", missing)}.", literal.OpenBraceToken);
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

                var armExpression = BindExpression(arm.Expression, contextualType ?? expectedArmType);
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

                var expression = BindExpression(arm.Expression, contextualType ?? armType);
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
            if (TryResolveClrTypeReference(m.Target, out Type? staticType))
            {
                return BindClrProperty(m, staticType!, receiver: null);
            }

            BoundExpression? clrReceiver = null;
            if (m.Target is not NameExpressionSyntax name || !_classTypes.ContainsKey(name.IdentifierToken.Text))
            {
                clrReceiver = BindExpression(m.Target);
                if (clrReceiver.Type is ClrTypeSymbol clrType)
                {
                    return BindClrProperty(m, clrType.RuntimeType, clrReceiver);
                }
            }

            if (m.Target is NameExpressionSyntax className
                && _classTypes.TryGetValue(className.IdentifierToken.Text, out var classType))
            {
                FunctionSymbol? function = classType.FindAssociatedFunction(m.NameToken.Text);
                if (function is null)
                {
                    Report("COPE-CLASS-0006", $"Class '{classType.Name}' has no associated function '{m.NameToken.Text}'.", m.NameToken);
                    return new BoundErrorExpression();
                }
                if (!CanAccessClassMember(classType, function.IsPublic))
                {
                    Report("COPE-CLASS-0009", $"Private associated function '{classType.Name}.{function.MemberName}' is accessible only from code owned by '{classType.Name}'.", m.NameToken);
                    return new BoundErrorExpression();
                }
                if (function.IsGeneric)
                {
                    Report("COPE-CALL-0003", $"Generic associated function '{classType.Name}.{function.MemberName}' must be explicitly closed before it can be used as a callable value.", m.NameToken);
                    return new BoundErrorExpression();
                }
                return new BoundFunctionReferenceExpression(function);
            }
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
            var receiver = clrReceiver ?? BindExpression(m.Target);
            if (receiver.Type == PrimitiveTypeSymbol.Error)
            {
                return new BoundErrorExpression();
            }
            if (receiver.Type is ArrayTypeSymbol)
            {
                if (m.NameToken.Text == "length")
                {
                    return new BoundArrayLengthExpression(receiver);
                }

                Report("COPE-ARRAY-0001", $"Array values support only the 'length' property; '{m.NameToken.Text}' is not available.", m.NameToken);
                return new BoundErrorExpression();
            }
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
                    if (recordType is ClassTypeSymbol classReceiver
                        && classReceiver.FindAssociatedFunction(m.NameToken.Text) is not null)
                    {
                        Report("COPE-CLASS-0011", $"Instance call syntax is not supported. Use '{classReceiver.Name}.{m.NameToken.Text}({GuessInstanceArgument(m.Target)})'.", m.NameToken);
                        return new BoundErrorExpression();
                    }
                    Report("COPE-REC-0010", $"Record '{recordType.Name}' has no field '{m.NameToken.Text}'.", m.NameToken);
                    return new BoundErrorExpression();
                }
                if (recordType is ClassTypeSymbol owningClass && !field.IsPublic && !CanAccessClassMember(owningClass, false))
                {
                    Report("COPE-CLASS-0009", $"Private field '{owningClass.Name}.{field.Name}' is accessible only from code owned by '{owningClass.Name}'.", m.NameToken);
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

        private BoundExpression BindClrProperty(MemberAccessExpressionSyntax syntax, Type declaringType, BoundExpression? receiver)
        {
            BindingFlags flags = BindingFlags.Public | (receiver is null ? BindingFlags.Static : BindingFlags.Instance);
            PropertyInfo[] properties = declaringType
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | (receiver is null ? BindingFlags.Static : BindingFlags.Instance))
                .Where(property => string.Equals(property.Name, syntax.NameToken.Text, StringComparison.Ordinal)
                    && property.GetMethod is not null
                    && property.GetIndexParameters().Length == 0
                    && _clrResolver.IsMemberVisible(property.GetMethod))
                .ToArray();
            if (properties.Length == 0)
            {
                if (declaringType.GetProperties(BindingFlags.NonPublic | (receiver is null ? BindingFlags.Static : BindingFlags.Instance))
                    .Any(property => string.Equals(property.Name, syntax.NameToken.Text, StringComparison.Ordinal)))
                {
                    Report("COPE-CLR-0004", $"CLR property '{declaringType.FullName}.{syntax.NameToken.Text}' is inaccessible.", syntax.NameToken);
                    return new BoundErrorExpression();
                }
                Report("COPE-CLR-0003", $"CLR type '{declaringType.FullName}' has no readable supported member '{syntax.NameToken.Text}'.", syntax.NameToken);
                return new BoundErrorExpression();
            }

            if (properties.Length > 1)
            {
                Report("COPE-CLR-0006", $"CLR property lookup for '{declaringType.FullName}.{syntax.NameToken.Text}' is ambiguous.", syntax.NameToken);
                return new BoundErrorExpression();
            }

            PropertyInfo property = properties[0];
            if (!TryProjectClrType(property.PropertyType, out TypeSymbol resultType))
            {
                Report("COPE-CLR-0007", $"CLR property '{declaringType.FullName}.{property.Name}' has unsupported type '{property.PropertyType}'.", syntax.NameToken);
                return new BoundErrorExpression();
            }

            return new BoundClrPropertyAccessExpression(property, receiver, resultType);
        }

        private bool CanAccessClassMember(ClassTypeSymbol owner, bool isPublic)
            => isPublic || ReferenceEquals(_currentClass, owner);

        private static string GuessInstanceArgument(ExpressionSyntax expression)
            => expression is NameExpressionSyntax name ? name.IdentifierToken.Text : "value";

        private BoundExpression BindIndex(IndexExpressionSyntax index)
        {
            var receiver = BindExpression(index.Target);
            var boundIndex = BindExpression(index.Index);
            if (receiver.Type is ArrayTypeSymbol array)
            {
                if (boundIndex.Type != PrimitiveTypeSymbol.Int)
                {
                    Report("COPE-ARRAY-0002", $"Array indexes must have type int. Found: {boundIndex.Type.Name}. Use Int.Floor, Int.Ceil, Int.Round, or Int.Truncate if an explicit float-to-int policy is intended.", index.OpenBracketToken);
                    return new BoundErrorExpression();
                }

                if (boundIndex is BoundUnaryExpression { OperatorKind: SyntaxKind.MinusToken, Operand: BoundLiteralExpression { Value: int } }
                    || boundIndex is BoundLiteralExpression { Value: int value } && value < 0)
                {
                    Report("COPE-ARRAY-0003", "Array indexes must be greater than or equal to zero.", index.OpenBracketToken);
                    return new BoundErrorExpression();
                }

                return new BoundArrayElementAccessExpression(receiver, boundIndex, array);
            }
            if (!TypeFacts.IsNumeric(boundIndex.Type))
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
            Report("COPE-TABLE-0011", "Indexing is supported for arrays, record tables, and columns.", index.OpenBracketToken);
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
            if (recordType is ClassTypeSymbol classType && !ReferenceEquals(_currentClass, classType))
            {
                Report("COPE-CLASS-0012", $"Class '{classType.Name}' can be updated with 'with' only by its associated code.", withExpression.WithKeyword);
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
                if (recordType is ClassTypeSymbol owningClass && !field.IsPublic && !CanAccessClassMember(owningClass, false))
                {
                    Report("COPE-CLASS-0009", $"Private field '{owningClass.Name}.{field.Name}' is accessible only from code owned by '{owningClass.Name}'.", property.NameToken);
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
                    SyntaxKind.IntKeyword => PrimitiveTypeSymbol.Int,
                    SyntaxKind.FloatKeyword => PrimitiveTypeSymbol.Float,
                    SyntaxKind.StringKeyword => PrimitiveTypeSymbol.String,
                    SyntaxKind.BooleanKeyword => PrimitiveTypeSymbol.Boolean,
                    SyntaxKind.VoidKeyword => PrimitiveTypeSymbol.Void,
                    SyntaxKind.NullKeyword => ReportedNullType(p.Keyword),
                    _ => PrimitiveTypeSymbol.Error
                },
                ArrayTypeSyntax a => new ArrayTypeSymbol(BindType(a.ElementType, anchor, missingId, missingPrefix)),
                AsyncTypeSyntax a => new AsyncTypeSymbol(BindType(a.EventualType, anchor, missingId, missingPrefix)),
                IterableTypeSyntax i => new IterableTypeSymbol(BindType(i.ElementType, anchor, missingId, missingPrefix)),
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
            if (_tsXmlProfile == CopelandTsXmlProfile.ReactM0)
            {
                if (i.Identifier.Text == "ReactNode") return ReactNodeTypeSymbol.Instance;
                if (i.Identifier.Text == "ReactRoot") return ReactRootTypeSymbol.Instance;
                if (i.Identifier.Text == "ReactMountElement") return ReactMountElementTypeSymbol.Instance;
            }
            if (_aliases.TryGetValue(i.Identifier.Text, out var alias))
                return alias.CanonicalType;
            if (_activeTypeParameters is not null && _activeTypeParameters.TryGetValue(i.Identifier.Text, out var typeParameter))
                return typeParameter.Type;
            if (_interfaces.ContainsKey(i.Identifier.Text))
            {
                Report(
                    "COPE-INTERFACE-0005",
                    $"Interface '{i.Identifier.Text}' is a field requirement, not a storage type. Use it as a generic constraint, for example '<T extends {i.Identifier.Text}>(value: T)'.",
                    i.Identifier);
                return PrimitiveTypeSymbol.Error;
            }
            if (_enumTypes.TryGetValue(i.Identifier.Text, out var enumType))
                return enumType;
            if (_recordTypes.TryGetValue(i.Identifier.Text, out var recordType))
                return recordType;
            if (_tableTypes.TryGetValue(i.Identifier.Text, out var tableType))
                return tableType;
            if (_clrImportedTypes.TryGetValue(i.Identifier.Text, out List<Type>? clrCandidates))
            {
                if (clrCandidates.Count == 1 && _clrResolver.IsTypeVisible(clrCandidates[0]))
                {
                    return new ClrTypeSymbol(clrCandidates[0]);
                }

                Report("COPE-CLR-0002", $"CLR type '{i.Identifier.Text}' is ambiguous across imported CLR namespaces.", i.Identifier);
                return PrimitiveTypeSymbol.Error;
            }
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
            => target == PrimitiveTypeSymbol.Error
                || actual == PrimitiveTypeSymbol.Error
                || TypeFacts.AreEquivalent(target, actual);

        private void ValidateRuntimeValueType(TypeSymbol type, SyntaxToken anchor, string position)
        {
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
                    case IterableTypeSymbol iterable:
                        pending.Push((iterable.ElementType, false));
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
            => TypeFacts.IsNumeric(type)
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

        private void ValidateRemoteFunction(FunctionDeclarationSyntax declaration, FunctionSymbol function)
        {
            if (function.IsAsync || function.IsGenerator)
            {
                Report("COPE-BRIDGE-0001", "Remote operations must use the synchronous CLR realization; the browser boundary supplies asynchrony.", declaration.RemoteKeyword!);
            }

            if (function.IsGeneric)
            {
                Report("COPE-BRIDGE-0002", "Remote operations cannot declare type parameters in bridge M0.", declaration.RemoteKeyword!);
            }

            if (function.Parameters.Count != 1 || function.Parameters[0].Type is not RecordTypeSymbol request)
            {
                Report("COPE-BRIDGE-0003", "Remote operations require exactly one nominal record request parameter.", declaration.RemoteKeyword!);
            }
            else
            {
                ValidateRemoteRecord(request, "request", declaration.RemoteKeyword!);
            }

            if (function.ReturnType is not ResultTypeSymbol result
                || !TypeFacts.AreEquivalent(result.SuccessType, PrimitiveTypeSymbol.String)
                || result.ErrorType is not RecordTypeSymbol error)
            {
                Report("COPE-BRIDGE-0004", "Remote operations must return string ! <nominal error record> in bridge M0.", declaration.RemoteKeyword!);
            }
            else
            {
                ValidateRemoteRecord(error, "error", declaration.RemoteKeyword!);
            }
        }

        private void ValidateRemoteRecord(RecordTypeSymbol record, string role, SyntaxToken anchor)
        {
            foreach (RecordFieldSymbol field in record.Fields)
            {
                bool supported = TypeFacts.AreEquivalent(field.Type, PrimitiveTypeSymbol.Int)
                    || TypeFacts.AreEquivalent(field.Type, PrimitiveTypeSymbol.Boolean)
                    || TypeFacts.AreEquivalent(field.Type, PrimitiveTypeSymbol.String);
                if (!supported)
                {
                    Report(
                        "COPE-BRIDGE-0005",
                        $"Remote {role} record field '{field.Name}' has unsupported type '{field.Type.Name}'. Bridge M0 permits only int, bool, and string fields.",
                        anchor);
                }
            }
        }
    }
}
