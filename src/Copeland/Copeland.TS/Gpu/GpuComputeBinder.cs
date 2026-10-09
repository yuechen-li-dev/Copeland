using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;
using Copeland.TS.Semantics;

namespace Copeland.TS.Gpu;

public static class GpuComputeBinder
{
    public static VdMirComputeModule Compile(GpuCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var binder = new Binder(request);
        return binder.Bind();
    }

    private sealed class Binder
    {
        private static readonly HashSet<string> KnownAnnotations = new(StringComparer.Ordinal)
        {
            "compute",
            "numthreads",
            "binding",
            "builtin",
        };

        private readonly GpuCompilationRequest _request;
        private GpuModuleGraph _modules = null!;
        private GpuConstants _constants = null!;
        private GpuEnums _enums = null!;
        private GpuValues _values = null!;
        private GpuGenericFunctions _generics = null!;
        private int _matchSequence;
        private readonly List<VdMirDiagnostic> _diagnostics = [];
        private readonly Dictionary<string, FunctionSource> _functions = new(StringComparer.Ordinal);
        private readonly List<VdMirResource> _resources = [];
        private readonly List<VdMirFunction> _boundFunctions = [];
        private readonly HashSet<string> _completedFunctions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _activeFunctions = new(StringComparer.Ordinal);
        private VdMirComputeEntryPoint? _entryPoint;

        public Binder(GpuCompilationRequest request)
        {
            _request = request;
        }

        public VdMirComputeModule Bind()
        {
            if (_request.Profile != CopelandCompilerProfile.Gpu)
            {
                AddDiagnostic(
                    "COPE-GPU-0001",
                    "SDSL-V4000",
                    "profile",
                    "The GPU binder requires the explicit Gpu compiler profile.",
                    new VdMirSourceSpan(string.Empty, 0, 0));
                return CreateModule();
            }

            ParseSources();
            _values = new GpuValues(_modules, (code, message, span) => AddDiagnostic(code, "SDSL-V4114", "value-storage", message, span));
            _generics = new GpuGenericFunctions(_modules, _values,
                (code, message, span) => AddDiagnostic(code, "SDSL-V4113", "generic", message, span),
                (path, type) => BindType(path, type, new SyntaxToken(SyntaxKind.IdentifierToken, 0, "", null)), BindOpenGeneric, AddEnumFunction, () => _boundFunctions);
            _values.BindStaticExpression = (path, expression) => BindExpression(path, expression, new(StringComparer.Ordinal));
            _values.StaticFunctions = () => _boundFunctions;
            _enums = new GpuEnums(_modules,
                (path, type) => BindType(path, type, new SyntaxToken(SyntaxKind.IdentifierToken, 0, string.Empty, null)),
                (code, message, span) => AddDiagnostic(code, "SDSL-V4200", "payload-enum", message, span));
            FunctionSource[] entries = _functions.Values
                .Where(item => HasAnnotation(item.Syntax.Annotations, "compute"))
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ThenBy(item => item.Syntax.Identifier.Position)
                .ToArray();

            if (entries.Length != 1)
            {
                VdMirSourceSpan span = entries.Length > 0
                    ? Span(entries[0].Path, entries[0].Syntax.Identifier)
                    : new VdMirSourceSpan(_request.Sources.FirstOrDefault()?.Path ?? string.Empty, 0, 0);
                AddDiagnostic(
                    "COPE-GPU-ENTRY-0001",
                    "SDSL-V4103",
                    "entry-point",
                    $"GPU compute M1 requires exactly one @compute entry; found {entries.Length}.",
                    span);
            }
            else
            {
                BindEntry(entries[0]);
            }

            return CreateModule();
        }

        private void ParseSources()
        {
            _modules = new GpuModuleGraph(_request, _diagnostics);
            _constants = new GpuConstants(_modules);
            foreach ((GpuSourceFile source, SyntaxTree tree) in _modules.Sources)
            {
                foreach (FunctionDeclarationSyntax function in tree.Root.Members.OfType<FunctionDeclarationSyntax>())
                {
                    if (function.Identifier.Text is "Sqrt" or "U32")
                    {
                        AddDiagnostic("COPE-GPU-SYMBOL-0001", "SDSL-V1509", "symbol",
                            $"{function.Identifier.Text} is a compiler-owned intrinsic and cannot be redefined.",
                            Span(source.Path, function.Identifier));
                    }
                    if (function.Identifier.Text == "RayQueryTraceClosest")
                    {
                        AddDiagnostic("COPE-GPU-RAYQUERY-0003", "SDSL-V4213", "ray-query",
                            "RayQueryTraceClosest is a compiler-owned command and cannot be redefined.", Span(source.Path, function.Identifier));
                    }
                    if (!_functions.TryAdd(_modules.Declare(source.Path, function.Identifier.Text), new FunctionSource(source.Path, function)))
                    {
                        AddDiagnostic(
                            "COPE-GPU-SYMBOL-0001",
                            "SDSL-V1509",
                            "symbol",
                            $"Duplicate function '{function.Identifier.Text}'.",
                            Span(source.Path, function.Identifier));
                    }
                }
            }
        }

        private void BindEntry(FunctionSource entry)
        {
            ValidateAnnotations(entry.Path, entry.Syntax.Annotations);
            string entryReturnType = BindType(entry.Path, entry.Syntax.ReturnType, entry.Syntax.Identifier);
            if (entryReturnType != "void")
            {
                AddDiagnostic(
                    "COPE-GPU-ENTRY-0002",
                    "SDSL-V4103",
                    "entry-point",
                    "Compute M1 entries must return void.",
                    Span(entry.Path, entry.Syntax.ReturnType ?? (SyntaxNode)new IdentifierTypeSyntax(entry.Syntax.Identifier)));
            }
            if (entry.Syntax.TypeParameters.Count > 0 || entry.Syntax.GenericParameters?.Values.Count > 0)
            {
                AddDiagnostic(
                    "COPE-GPU-MATERIALIZATION-0001",
                    "SDSL-V4113",
                    "materialization",
                    "Compute M1 entries must be concrete; open generic entries are deferred.",
                    Span(entry.Path, entry.Syntax.Identifier));
            }
            AnnotationSyntax? numthreads = FindAnnotation(entry.Syntax.Annotations, "numthreads");
            int[] dimensions = BindNumThreads(entry.Path, numthreads);
            var scope = new Dictionary<string, ValueBinding>(StringComparer.Ordinal);
            var builtins = new List<VdMirParameter>();
            var bindings = new Dictionary<(int Set, int Binding), VdMirResource>();

            foreach (ParameterSyntax parameter in entry.Syntax.Parameters)
            {
                ValidateAnnotations(entry.Path, parameter.Annotations);
                string type = BindType(entry.Path, parameter.Type, parameter.Identifier);
                AnnotationSyntax? builtin = FindAnnotation(parameter.Annotations, "builtin");
                AnnotationSyntax? binding = FindAnnotation(parameter.Annotations, "binding");

                if (builtin is not null)
                {
                    string? builtinName = SingleNameArgument(builtin);
                    if (builtinName != "dispatchThreadId" || type != "uint3")
                    {
                        AddDiagnostic(
                            "COPE-GPU-BUILTIN-0001",
                            builtinName == "dispatchThreadId" ? "SDSL-V4110" : "SDSL-V4109",
                            "builtin",
                            "Compute M1 supports only @builtin(dispatchThreadId) on uint3.",
                            Span(entry.Path, builtin.NameToken));
                        continue;
                    }

                    var parameterIr = new VdMirParameter(parameter.Identifier.Text, type, "dispatch_thread_id", Span(entry.Path, parameter));
                    builtins.Add(parameterIr);
                    scope[parameter.Identifier.Text] = new ValueBinding(type, false, null, true);
                    continue;
                }

                if ((type == "storage-buffer<f32>" || type == "acceleration_structure") && binding is not null)
                {
                    int? bindingIndex = SingleIntegerArgument(binding);
                    VdMirResourceAccess? access = parameter.AccessToken?.Text switch
                    {
                        "readonly" => VdMirResourceAccess.Readonly,
                        "readwrite" => VdMirResourceAccess.Readwrite,
                        _ => null,
                    };
                    if (bindingIndex is null || bindingIndex < 0 || access is null
                        || (type == "acceleration_structure" && access != VdMirResourceAccess.Readonly))
                    {
                        AddDiagnostic(
                            "COPE-GPU-RESOURCE-0001",
                            "SDSL-V3703",
                            "resource-binding",
                            "StorageBuffer<f32> parameters require readonly/readwrite and @binding(nonNegativeInteger).",
                            Span(entry.Path, parameter));
                        continue;
                    }

                    var resource = new VdMirResource(
                        parameter.Identifier.Text,
                        type == "acceleration_structure" ? "acceleration_structure" : "f32",
                        access.Value,
                        0,
                        bindingIndex.Value,
                        Span(entry.Path, parameter),
                        Span(entry.Path, binding.NameToken));
                    if (bindings.TryGetValue((0, bindingIndex.Value), out VdMirResource? conflict))
                    {
                        AddDiagnostic(
                            "COPE-GPU-BINDING-0001",
                            "SDSL-V4112",
                            "resource-binding",
                            $"Binding set 0, binding {bindingIndex.Value} is already claimed by '{conflict.Name}'.",
                            Span(entry.Path, binding.NameToken),
                            [new VdMirRelatedSpan("First binding annotation.", conflict.BindingSource)]);
                    }
                    else
                    {
                        bindings.Add((0, bindingIndex.Value), resource);
                        _resources.Add(resource);
                    }

                    scope[parameter.Identifier.Text] = new ValueBinding(type, access == VdMirResourceAccess.Readwrite, resource, false);
                    continue;
                }

                AddDiagnostic(
                    "COPE-GPU-PARAMETER-0001",
                    "SDSL-V4102",
                    "entry-parameter",
                    "Compute entry parameters must be the dispatch builtin or explicitly bound StorageBuffer<f32> resources.",
                    Span(entry.Path, parameter));
            }

            IReadOnlyList<VdMirStatement> statements = BindFunctionBody(entry, scope, "void");
            _entryPoint = new VdMirComputeEntryPoint(
                _modules.Declare(entry.Path, entry.Syntax.Identifier.Text),
                _modules.Declare(entry.Path, entry.Syntax.Identifier.Text),
                dimensions[0],
                dimensions[1],
                dimensions[2],
                builtins,
                Span(entry.Path, entry.Syntax));
            _boundFunctions.Add(new VdMirFunction(
                _modules.Declare(entry.Path, entry.Syntax.Identifier.Text),
                builtins,
                "void",
                statements,
                Span(entry.Path, entry.Syntax)));
            _completedFunctions.Add(_modules.Declare(entry.Path, entry.Syntax.Identifier.Text));
        }

        private IReadOnlyList<VdMirStatement> BindFunctionBody(
            FunctionSource function,
            Dictionary<string, ValueBinding> scope,
            string returnType)
        {
            if (!_activeFunctions.Add(_modules.Declare(function.Path, function.Syntax.Identifier.Text)))
            {
                AddDiagnostic(
                    "COPE-GPU-RECURSION-0001",
                    "SDSL-V4201",
                    "recursion",
                    $"Reachable recursion through '{_modules.Declare(function.Path, function.Syntax.Identifier.Text)}' is deferred in compute M1.",
                    Span(function.Path, function.Syntax.Identifier));
                return [];
            }

            if (function.Syntax.AsyncKeyword is not null)
            {
                AddHostOnly(function.Path, function.Syntax.AsyncKeyword, "async functions");
            }

            var statements = BindStatements(function.Path, function.Syntax.Body.Statements, scope, returnType);
            _activeFunctions.Remove(_modules.Declare(function.Path, function.Syntax.Identifier.Text));
            return statements;
        }

        private IReadOnlyList<VdMirStatement> BindStatements(
            string path,
            IReadOnlyList<StatementSyntax> source,
            Dictionary<string, ValueBinding> scope,
            string returnType)
        {
            var result = new List<VdMirStatement>();
            foreach (StatementSyntax statement in source)
            {
                switch (statement)
                {
                    case VariableDeclarationStatementSyntax local:
                    {
                        string declaredType = BindType(path, local.Type, local.Identifier);
                        if (declaredType == "acceleration_structure")
                        {
                            AddDiagnostic("COPE-GPU-RAYQUERY-0002", "SDSL-V4213", "ray-query",
                                "Acceleration structures are bound readonly resources, not copyable local values.", Span(path, local));
                        }
                        VdMirExpression initializer = BindExpression(path, local.Initializer, scope, declaredType);
                        if (declaredType != initializer.Type)
                        {
                            TypeMismatch(path, local.Initializer, declaredType, initializer.Type);
                        }
                        bool mutable = local.Keyword.Kind == SyntaxKind.VarKeyword;
                        scope[local.Identifier.Text] = new ValueBinding(declaredType, mutable, null, false);
                        result.Add(new VdMirStatement(
                            "local",
                            Span(path, local),
                            local.Identifier.Text,
                            declaredType,
                            mutable,
                            initializer));
                        break;
                    }
                    case ExpressionStatementSyntax expressionStatement when expressionStatement.Expression is AssignmentExpressionSyntax assignment:
                    {
                        if (assignment.Left is IndexExpressionSyntax { Target: NameExpressionSyntax arrayName } indexed
                            && scope.TryGetValue(arrayName.IdentifierToken.Text, out ValueBinding? arrayBinding) && _values.Find(arrayBinding.Type) is not null)
                        {
                            if (!arrayBinding.Mutable)
                            {
                                AddDiagnostic("COPE-GPU-MUTATION-0001", "SDSL-V3701", "binding", "Indexed writes require a var binding.", Span(path, indexed));
                            }
                            var subject = new VdMirExpression("name", arrayBinding.Type, Span(path, indexed), arrayName.IdentifierToken.Text);
                            var coordinate = BindExpression(path, indexed.Index, scope);
                            var assigned = BindExpression(path, assignment.Right, scope, _values.Find(arrayBinding.Type)!.ElementType);
                            VdMirExpression replacement = _values.Write(subject, [coordinate], assigned, AddEnumFunction, Span(path, assignment)) ?? ValueOperationError(path, assignment);
                            result.Add(new("assign", Span(path, assignment), Expression: new("assignment", subject.Type, Span(path, assignment), Operands: [subject, replacement])));
                            break;
                        }
                        VdMirExpression target = BindExpression(path, assignment.Left, scope);
                        VdMirExpression value = BindExpression(path, assignment.Right, scope, target.Type);
                        if (!IsMutableTarget(assignment.Left, scope))
                        {
                            AddDiagnostic(
                                "COPE-GPU-MUTATION-0001",
                                "SDSL-V3701",
                                "binding",
                                "The assignment target is immutable or readonly.",
                                Span(path, assignment.Left));
                        }
                        if (target.Type != value.Type) TypeMismatch(path, assignment.Right, target.Type, value.Type);
                        result.Add(new VdMirStatement(
                            "assign",
                            Span(path, expressionStatement),
                            Expression: new VdMirExpression(
                                "assignment",
                                target.Type,
                                Span(path, assignment),
                                Operands: [target, value])));
                        break;
                    }
                    case ExpressionStatementSyntax expressionStatement:
                        if (expressionStatement.Expression is CallExpressionSyntax { Target: MemberAccessExpressionSyntax { Target: NameExpressionSyntax setTarget, NameToken.Text: "set" } } update)
                        {
                            if (!scope.TryGetValue(setTarget.IdentifierToken.Text, out ValueBinding? binding) || _values.Find(binding.Type) is not { } valueType
                                || update.Arguments.Count != valueType.Shape.Count + 1)
                            {
                                ValueOperationError(path, update);
                                break;
                            }
                            if (!binding.Mutable)
                            {
                                AddDiagnostic("COPE-GPU-MUTATION-0001", "SDSL-V3701", "binding", "set requires a var binding.", Span(path, update));
                            }
                            var subject = new VdMirExpression("name", binding.Type, Span(path, setTarget), setTarget.IdentifierToken.Text);
                            var indices = update.Arguments.SkipLast(1).Select(argument => BindExpression(path, argument, scope)).ToArray();
                            var assigned = BindExpression(path, update.Arguments[^1], scope, valueType.ElementType);
                            var replacement = _values.Write(subject, indices, assigned, AddEnumFunction, Span(path, update)) ?? ValueOperationError(path, update);
                            result.Add(new("assign", Span(path, update), Expression: new("assignment", subject.Type, Span(path, update), Operands: [subject, replacement])));
                            break;
                        }
                        if (expressionStatement.Expression is CallExpressionSyntax queryCall
                            && queryCall.Target is NameExpressionSyntax queryName
                            && queryName.IdentifierToken.Text == "RayQueryTraceClosest")
                        {
                            result.Add(BindRayQuery(path, queryCall, scope));
                            break;
                        }
                        result.Add(new VdMirStatement(
                            "expression",
                            Span(path, expressionStatement),
                            Expression: BindExpression(path, expressionStatement.Expression, scope)));
                        break;
                    case StaticIfStatementSyntax conditional:
                    {
                        var request = new StaticExpressionSyntax(conditional.StaticKeyword, conditional.Condition);
                        VdMirExpression condition = BindExpression(path, request, scope);
                        if (condition is not { Kind: "literal", Type: "bool" })
                        {
                            AddDiagnostic("COPE-GPU-STATIC-0001", "SDSL-V4200", "static-control", "Static if requires a compile-time bool.", Span(path, conditional));
                            break;
                        }
                        StatementSyntax? selected = condition.Value == "true" ? conditional.ThenStatement : conditional.ElseStatement;
                        if (selected is not null)
                        {
                            result.Add(new("block", Span(path, conditional), Body: BindNestedStatement(path, selected, scope, returnType)));
                        }
                        break;
                    }
                    case IfStatementSyntax conditional:
                    {
                        VdMirExpression condition = BindExpression(path, conditional.Condition, scope);
                        if (condition.Type != "bool") TypeMismatch(path, conditional.Condition, "bool", condition.Type);
                        IReadOnlyList<VdMirStatement> body = BindNestedStatement(path, conditional.ThenStatement, scope, returnType);
                        IReadOnlyList<VdMirStatement>? elseBody = conditional.ElseStatement is null
                            ? null
                            : BindNestedStatement(path, conditional.ElseStatement, scope, returnType);
                        result.Add(new VdMirStatement(
                            "if",
                            Span(path, conditional),
                            Expression: condition,
                            Body: body,
                            ElseBody: elseBody));
                        break;
                    }
                    case ReturnStatementSyntax returnStatement:
                    {
                        VdMirExpression? value = returnStatement.Expression is null
                            ? null
                            : BindExpression(path, returnStatement.Expression, scope, returnType);
                        string actual = value?.Type ?? "void";
                        if (actual != returnType) TypeMismatch(path, returnStatement, returnType, actual);
                        result.Add(new VdMirStatement("return", Span(path, returnStatement), Expression: value));
                        break;
                    }
                    case BlockStatementSyntax block:
                        result.AddRange(BindStatements(path, block.Statements, CloneScope(scope), returnType));
                        break;
                    case WhileStatementSyntax or ForStatementSyntax or ForOfStatementSyntax:
                        AddDiagnostic(
                            "COPE-GPU-CONTROL-0001",
                            "SDSL-V4202",
                            "control-flow",
                            "Loops are deferred in compute M1.",
                            Span(path, statement));
                        break;
                    default:
                        AddHostOnly(path, statement, statement.Kind.ToString());
                        break;
                }
            }

            return result;
        }

        private IReadOnlyList<VdMirStatement> BindNestedStatement(
            string path,
            StatementSyntax statement,
            Dictionary<string, ValueBinding> scope,
            string returnType)
        {
            return statement is BlockStatementSyntax block
                ? BindStatements(path, block.Statements, CloneScope(scope), returnType)
                : BindStatements(path, [statement], CloneScope(scope), returnType);
        }

        private VdMirExpression BindExpression(
            string path,
            ExpressionSyntax expression,
            Dictionary<string, ValueBinding> scope, string? expected = null)
        {
            if (expected is not null && expression is ObjectLiteralExpressionSyntax or ArrayLiteralExpressionSyntax)
            {
                VdMirExpression? initialized = _values.BindLiteral(expected, expression,
                    (value, type) => BindExpression(path, value, scope, type), AddEnumFunction, Span(path, expression));
                if (initialized is not null)
                {
                    return initialized;
                }
            }
            switch (expression)
            {
                case GenericCallExpressionSyntax call when call.Target is NameExpressionSyntax genericName:
                {
                    var generic = _generics.Call(path, genericName.IdentifierToken.Text, call.TypeArguments, call.Arguments,
                        (argument, type) => BindExpression(path, argument, scope, type), Span(path, call));
                    if (generic is not null)
                    {
                        return generic;
                    }
                    if (genericName.IdentifierToken.Text == "Convert" && call.TypeArguments is [IdentifierTypeSyntax { Identifier.Text: "f32" }]
                        && call.Arguments.Count == 1)
                    {
                        var value = BindExpression(path, call.Arguments[0], scope);
                        if (value.Type == "u32")
                        {
                            return new("intrinsic", "f32", Span(path, call), "ConvertU32ToF32", [value]);
                        }
                    }
                    return ValueOperationError(path, call);
                }
                case NameExpressionSyntax name:
                    if (scope.TryGetValue(name.IdentifierToken.Text, out ValueBinding? binding))
                    {
                        return new VdMirExpression("name", binding.Type, Span(path, expression), name.IdentifierToken.Text);
                    }
                    VdMirExpression? parameter = _values.StaticParameter(name.IdentifierToken.Text);
                    if (parameter is not null) return parameter;
                    VdMirExpression? constant = BindConstant(path, name.IdentifierToken.Text);
                    if (constant is not null)
                    {
                        return constant with { Source = Span(path, name) };
                    }
                    AddDiagnostic("COPE-GPU-NAME-0001", "SDSL-V1501", "name", $"Unknown GPU value '{name.IdentifierToken.Text}'.", Span(path, name.IdentifierToken));
                    return ErrorExpression(path, expression);
                case MatchExpressionSyntax match:
                    string helperName;
                    do
                    {
                        helperName = "VtsMatch" + _matchSequence++;
                    }
                    while (_functions.ContainsKey(helperName));
                    return GpuEnumMatch.Bind(path, match, BindExpression(path, match.Expression, scope), _enums,
                        helperName, scope.ToDictionary(item => item.Key, item => item.Value.Type, StringComparer.Ordinal),
                        (expression, armScope) => BindExpression(path, expression, armScope.ToDictionary(item => item.Key, item => new ValueBinding(item.Value, false, null, false), StringComparer.Ordinal)),
                        AddEnumFunction, (code, message, span) => AddDiagnostic(code, "SDSL-V4200", "payload-match", message, span));
                case TemplateInstantiationExpressionSyntax instantiated:
                    return _generics.Instantiate(path, instantiated, (value, type) => BindExpression(path, value, scope, type));
                case StaticExpressionSyntax evaluated:
                    try
                    {
                        VdMirExpression value = BindExpression(path, evaluated.Expression, scope);
                        if (value.Type == "error")
                        {
                            return value;
                        }
                        if (_generics.BindingOpen) return new("generic-fold", value.Type, Span(path, evaluated), Operands: [value]);
                        return GpuStaticEvaluation.Fold(value, _boundFunctions, _values.Definitions,
                            aggregate => _values.EmbedStatic(aggregate, AddEnumFunction)) with { Source = Span(path, evaluated) };
                    }
                    catch (StaticEvaluationException exception)
                    {
                        AddDiagnostic(exception.DiagnosticId, "SDSL-V4200", "static-evaluation", exception.Message, Span(path, evaluated));
                        return ErrorExpression(path, evaluated);
                    }
                case LiteralExpressionSyntax literal:
                    return BindLiteral(path, literal);
                case ParenthesizedExpressionSyntax parenthesized:
                    return BindExpression(path, parenthesized.Expression, scope, expected);
                case MemberAccessExpressionSyntax member:
                {
                    if (member.Target is NameExpressionSyntax qualifier && !scope.ContainsKey(qualifier.IdentifierToken.Text))
                    {
                        VdMirExpression? constructed = _enums.Construct(path, member, [], AddEnumFunction);
                        if (constructed is not null)
                        {
                            return constructed;
                        }
                    }
                    VdMirExpression target = BindExpression(path, member.Target, scope);
                    VdMirExpression? query = _values.Query(target, member.NameToken.Text, AddEnumFunction, Span(path, member));
                    if (query is not null)
                    {
                        return query;
                    }
                    string? recordField = _values.MemberType(target.Type, member.NameToken.Text);
                    if (recordField is not null)
                    {
                        return new("field", recordField, Span(path, member), member.NameToken.Text, [target]);
                    }
                    if (target.Type == "uint3" && member.NameToken.Text is "x" or "y" or "z")
                    {
                        return new VdMirExpression("field", "u32", Span(path, member), member.NameToken.Text, [target]);
                    }
                    string? payloadType = _enums.MemberType(target.Type, member.NameToken.Text);
                    if (payloadType is not null)
                    {
                        return new("field", payloadType, Span(path, member), member.NameToken.Text, [target]);
                    }
                    AddDiagnostic("COPE-GPU-MEMBER-0001", "SDSL-V1502", "member", $"Member '{member.NameToken.Text}' is not available on '{target.Type}'.", Span(path, member.NameToken));
                    return ErrorExpression(path, expression);
                }
                case IndexExpressionSyntax index:
                {
                    VdMirExpression target = BindExpression(path, index.Target, scope);
                    VdMirExpression subscript = BindExpression(path, index.Index, scope);
                    VdMirExpression? shaped = _values.Read(target, [subscript], AddEnumFunction, Span(path, index));
                    if (shaped is not null)
                    {
                        return shaped;
                    }
                    if (target.Type != "storage-buffer<f32>" || subscript.Type != "u32")
                    {
                        AddDiagnostic("COPE-GPU-INDEX-0001", "SDSL-V1503", "indexing", "Compute M1 indexing requires StorageBuffer<f32>[u32].", Span(path, index));
                        return ErrorExpression(path, expression);
                    }
                    return new VdMirExpression("index", "f32", Span(path, index), Operands: [target, subscript]);
                }
                case BinaryExpressionSyntax binary:
                    return BindBinary(path, binary, scope);
                case WithExpressionSyntax updated:
                    return _values.UpdateRecord(BindExpression(path, updated.Source, scope), updated,
                        (value, type) => BindExpression(path, value, scope, type), AddEnumFunction, Span(path, updated));
                case CallExpressionSyntax call:
                    return BindCall(path, call, scope);
                case NewExpressionSyntax allocation:
                    AddHostOnly(path, allocation.NewKeyword, "managed allocation");
                    return ErrorExpression(path, expression);
                default:
                    AddHostOnly(path, expression, expression.Kind.ToString());
                    return ErrorExpression(path, expression);
            }
        }

        private VdMirExpression ValueOperationError(string path, SyntaxNode syntax)
        {
            AddDiagnostic("COPE-GPU-VALUE-0006", "SDSL-V1503", "value-operation", "Operation requires an admitted shaped value or tensor with the correct rank.", Span(path, syntax));
            return ErrorExpression(path, syntax);
        }

        private VdMirExpression? BindConstant(string path, string name)
        {
            try
            {
                return _constants.Bind(path, name, (sourcePath, declaration) =>
                {
                    string type = BindType(sourcePath, declaration.Type, declaration.Identifier);
                    VdMirExpression value = BindExpression(sourcePath, declaration.Initializer, new(StringComparer.Ordinal));
                    if (value.Type != type)
                    {
                        TypeMismatch(sourcePath, declaration.Initializer, type, value.Type);
                    }
                    return value;
                }, () => _boundFunctions);
            }
            catch (StaticEvaluationException exception)
            {
                AddDiagnostic(exception.DiagnosticId, "SDSL-V4200", "static-evaluation", exception.Message, new(path, 0, 1));
                return new("error", "error", new(path, 0, 1));
            }
        }

        private VdMirFunction BindOpenGeneric(string path, FunctionDeclarationSyntax syntax)
        {
            var scope = syntax.Parameters.ToDictionary(parameter => parameter.Identifier.Text,
                parameter => new ValueBinding(BindType(path, parameter.Type, parameter.Identifier), false, null, false), StringComparer.Ordinal);
            string result = BindType(path, syntax.ReturnType, syntax.Identifier);
            var statements = BindFunctionBody(new(path, syntax), scope, result);
            return new(_modules.Declare(path, syntax.Identifier.Text), syntax.Parameters.Select(parameter =>
                new VdMirParameter(parameter.Identifier.Text, scope[parameter.Identifier.Text].Type, null, Span(path, parameter))).ToArray(), result, statements, Span(path, syntax));
        }

        private void AddEnumFunction(VdMirFunction function)
        {
            if (_generics.Capture(function)) return;
            if (_boundFunctions.Any(existing => existing.Name == function.Name))
            {
                AddDiagnostic("COPE-GPU-SYMBOL-0002", "SDSL-V1509", "symbol", "A declaration collides with a compiler-generated enum helper.", function.Source);
                return;
            }
            _boundFunctions.Add(function);
            _completedFunctions.Add(function.Name);
        }

        private VdMirExpression BindCall(string path, CallExpressionSyntax call, Dictionary<string, ValueBinding> scope)
        {
            if (call.Target is NameExpressionSyntax genericName)
            {
                VdMirExpression? generic = _generics.Call(path, genericName.IdentifierToken.Text, null, call.Arguments,
                    (argument, type) => BindExpression(path, argument, scope, type), Span(path, call));
                if (generic is not null) return generic;
            }
            if (call.Target is MemberAccessExpressionSyntax { NameToken.Text: "at" } access)
            {
                return _values.Read(BindExpression(path, access.Target, scope), call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray(), AddEnumFunction, Span(path, call))
                    ?? ValueOperationError(path, call);
            }
            if (call.Target is NameExpressionSyntax { IdentifierToken.Text: "Dot" or "MatMul" } math)
            {
                return _values.Math(math.IdentifierToken.Text, call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray(), AddEnumFunction, Span(path, call))
                    ?? ValueOperationError(path, call);
            }
            if (call.Target is MemberAccessExpressionSyntax variant
                && variant.Target is NameExpressionSyntax qualifier && !scope.ContainsKey(qualifier.IdentifierToken.Text))
            {
                VdMirExpression[] payload = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
                VdMirExpression? constructed = _enums.Construct(path, variant, payload, AddEnumFunction);
                if (constructed is not null)
                {
                    return constructed;
                }
            }

            if (call.Target is NameExpressionSyntax intrinsic &&
                intrinsic.IdentifierToken.Text is "Sqrt" or "U32")
            {
                var operands = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
                string intrinsicName = intrinsic.IdentifierToken.Text;
                if (operands.Length != 1 || operands[0].Type != "f32")
                {
                    AddDiagnostic("COPE-GPU-CALL-0001", "SDSL-V1503", "call",
                        $"{intrinsicName} requires one f32 operand.", Span(path, call));
                    return ErrorExpression(path, call);
                }
                return new VdMirExpression("call", intrinsicName == "U32" ? "u32" : "f32",
                    Span(path, call), intrinsicName, operands);
            }
            if (call.Target is NameExpressionSyntax queryName && queryName.IdentifierToken.Text == "RayQueryTraceClosest")
            {
                AddDiagnostic("COPE-GPU-RAYQUERY-0002", "SDSL-V4213", "ray-query",
                    "RayQueryTraceClosest is statement-only; opaque mutable query state cannot escape as a value.", Span(path, call));
                return ErrorExpression(path, call);
            }
            if (call.Target is not NameExpressionSyntax name || !_functions.TryGetValue(_modules.Resolve(path, name.IdentifierToken.Text), out FunctionSource? function))
            {
                AddHostOnly(path, call, "host or unresolved call");
                return ErrorExpression(path, call);
            }

            string HelperType(TypeSyntax? type, SyntaxToken token)
            {
                using var context = _values.Enter(GpuValues.Empty);
                return BindType(function.Path, type, token);
            }
            string returnType = HelperType(function.Syntax.ReturnType, function.Syntax.Identifier);
            if (returnType == "acceleration_structure" || function.Syntax.Parameters.Any(parameter =>
                HelperType(parameter.Type, parameter.Identifier) == "acceleration_structure"))
            {
                AddDiagnostic("COPE-GPU-RAYQUERY-0002", "SDSL-V4213", "ray-query",
                    "Acceleration structures cannot escape through a helper ABI.", Span(path, call));
            }
            var arguments = call.Arguments.Select((argument, index) => BindExpression(path, argument, scope,
                index < function.Syntax.Parameters.Count
                    ? HelperType(function.Syntax.Parameters[index].Type, function.Syntax.Parameters[index].Identifier)
                    : null)).ToArray();
            if (function.Syntax.Parameters.Count != arguments.Length)
            {
                AddDiagnostic("COPE-GPU-CALL-0001", "SDSL-V1503", "call", $"Function '{_modules.Declare(function.Path, function.Syntax.Identifier.Text)}' expects {function.Syntax.Parameters.Count} argument(s).", Span(path, call));
            }

            var helperScope = new Dictionary<string, ValueBinding>(StringComparer.Ordinal);
            for (int index = 0; index < function.Syntax.Parameters.Count; index++)
            {
                ParameterSyntax parameter = function.Syntax.Parameters[index];
                string parameterType = HelperType(parameter.Type, parameter.Identifier);
                helperScope[parameter.Identifier.Text] = new ValueBinding(parameterType, false, null, false);
                if (index < arguments.Length && arguments[index].Type != parameterType)
                {
                    TypeMismatch(path, call.Arguments[index], parameterType, arguments[index].Type);
                }
            }

            if (!_completedFunctions.Contains(_modules.Declare(function.Path, function.Syntax.Identifier.Text)))
            {
                using var closedContext = _generics.EnterClosedBinding();
                IReadOnlyList<VdMirStatement> statements = BindFunctionBody(function, helperScope, returnType);
                if (!_completedFunctions.Contains(_modules.Declare(function.Path, function.Syntax.Identifier.Text)))
                {
                    _boundFunctions.Add(new VdMirFunction(
                        _modules.Declare(function.Path, function.Syntax.Identifier.Text),
                        function.Syntax.Parameters.Select(parameter => new VdMirParameter(
                            parameter.Identifier.Text,
                            BindType(function.Path, parameter.Type, parameter.Identifier),
                            null,
                            Span(function.Path, parameter))).ToArray(),
                        returnType,
                        statements,
                        Span(function.Path, function.Syntax)));
                    _completedFunctions.Add(_modules.Declare(function.Path, function.Syntax.Identifier.Text));
                }
            }

            return new VdMirExpression("call", returnType, Span(path, call), _modules.Declare(function.Path, function.Syntax.Identifier.Text), arguments);
        }

        // Port of Oct's SDSL-V4211/4212/4213 stateful command boundary. The query
        // cannot be copied, returned, stored, or invoked through a helper ABI.
        private VdMirStatement BindRayQuery(string path, CallExpressionSyntax call, Dictionary<string, ValueBinding> scope)
        {
            var arguments = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            bool valid = arguments.Length == 6;
            VdMirResourceAccess[] access = [VdMirResourceAccess.Readonly, VdMirResourceAccess.Readonly,
                VdMirResourceAccess.Readonly, VdMirResourceAccess.Readwrite, VdMirResourceAccess.Readonly];
            for (int index = 0; index < Math.Min(5, arguments.Length); index++)
            {
                string expected = index == 0 ? "acceleration_structure" : "storage-buffer<f32>";
                bool resourceValid = call.Arguments[index] is NameExpressionSyntax name
                    && scope.TryGetValue(name.IdentifierToken.Text, out ValueBinding? value)
                    && value.Resource is { } resource && resource.Access == access[index]
                    && arguments[index].Type == expected;
                valid &= resourceValid;
            }
            valid &= arguments.Length == 6 && arguments[5].Type == "u32";
            if (!valid)
            {
                AddDiagnostic("COPE-GPU-RAYQUERY-0001", "SDSL-V4211", "ray-query",
                    "RayQueryTraceClosest requires readonly acceleration_structure, readonly sphere/ray/triangle buffers, readwrite output and a u32 dispatch index.", Span(path, call));
            }
            return new VdMirStatement("ray-query", Span(path, call),
                Expression: new VdMirExpression("intrinsic", "void", Span(path, call), "RayQueryTraceClosest", arguments));
        }

        private VdMirExpression BindBinary(string path, BinaryExpressionSyntax binary, Dictionary<string, ValueBinding> scope)
        {
            VdMirExpression left = BindExpression(path, binary.Left, scope);
            VdMirExpression right = BindExpression(path, binary.Right, scope);
            VdMirExpression? tensor = _values.Math(binary.OperatorToken.Text, [left, right], AddEnumFunction, Span(path, binary));
            if (tensor is not null)
            {
                return tensor;
            }
            bool numeric = left.Type == right.Type && left.Type is "f32" or "u32";
            string? resultType = binary.OperatorToken.Text switch
            {
                "+" or "-" or "*" or "/" when numeric => left.Type,
                "<" or "<=" or ">" or ">=" or "==" or "!=" when numeric => "bool",
                _ => null,
            };
            if (resultType is null)
            {
                AddDiagnostic("COPE-GPU-OPERATOR-0001", "SDSL-V1503", "operator", $"Operator '{binary.OperatorToken.Text}' is not defined for '{left.Type}' and '{right.Type}' in compute M1.", Span(path, binary.OperatorToken));
                return ErrorExpression(path, binary);
            }

            return new VdMirExpression("binary", resultType, Span(path, binary), binary.OperatorToken.Text, [left, right]);
        }

        private VdMirExpression BindLiteral(string path, LiteralExpressionSyntax literal)
        {
            VdMirExpression? value = GpuScalarLiterals.Bind(path, literal);
            if (value is not null)
            {
                return value;
            }
            AddDiagnostic("COPE-GPU-LITERAL-0001", "SDSL-V1503", "literal", "GPU literals require finite f32, u32 or bool values.", Span(path, literal));
            return ErrorExpression(path, literal);
        }

        private bool IsMutableTarget(ExpressionSyntax target, IReadOnlyDictionary<string, ValueBinding> scope)
        {
            if (target is NameExpressionSyntax name && scope.TryGetValue(name.IdentifierToken.Text, out ValueBinding? binding))
            {
                return binding.Mutable;
            }
            if (target is IndexExpressionSyntax index && index.Target is NameExpressionSyntax resourceName
                && scope.TryGetValue(resourceName.IdentifierToken.Text, out ValueBinding? resource))
            {
                return resource.Resource?.Access == VdMirResourceAccess.Readwrite;
            }
            return false;
        }

        private string BindType(string path, TypeSyntax? type, SyntaxToken anchor)
        {
            string? value = _values?.BindType(path, type, (sourcePath, field) => BindType(sourcePath, field, anchor));
            if (value is not null)
            {
                return value;
            }
            string? result = type switch
            {
                IdentifierTypeSyntax identifier when _enums is not null && _enums.Contains(_modules.Resolve(path, identifier.Identifier.Text)) => _modules.Resolve(path, identifier.Identifier.Text),
                IdentifierTypeSyntax identifier when identifier.Identifier.Text is "f32" or "u32" or "bool" or "uint3" or "acceleration_structure" => identifier.Identifier.Text,
                PredefinedTypeSyntax predefined when predefined.Keyword.Kind == SyntaxKind.VoidKeyword => "void",
                IdentifierTypeSyntax identifier when identifier.Identifier.Text == "void" => "void",
                GenericTypeSyntax generic when generic.Identifier.Text == "StorageBuffer"
                    && generic.TypeArguments.Count == 1
                    && BindType(path, generic.TypeArguments[0], generic.Identifier) == "f32" => "storage-buffer<f32>",
                _ => null,
            };
            if (result is null)
            {
                AddDiagnostic("COPE-GPU-TYPE-0001", "SDSL-V1502", "type", "Type is not part of the compute M1 GPU subset.", Span(path, type ?? (SyntaxNode)new IdentifierTypeSyntax(anchor)));
                return "error";
            }
            return result;
        }

        private int[] BindNumThreads(string path, AnnotationSyntax? annotation)
        {
            if (annotation is null || annotation.Arguments.Count != 3)
            {
                AddDiagnostic("COPE-GPU-NUMTHREADS-0001", "SDSL-V4104", "numthreads", "@compute requires @numthreads(x, y, z).", annotation is null ? new VdMirSourceSpan(path, 0, 0) : Span(path, annotation));
                return [1, 1, 1];
            }
            int[] dimensions = annotation.Arguments.Select(IntegerLiteralValue).ToArray();
            if (dimensions.Any(value => value <= 0))
            {
                AddDiagnostic("COPE-GPU-NUMTHREADS-0002", "SDSL-V4104", "numthreads", "@numthreads values must be positive compile-time integers.", Span(path, annotation));
                return [1, 1, 1];
            }
            return dimensions;
        }

        private void ValidateAnnotations(string path, IReadOnlyList<AnnotationSyntax>? annotations)
        {
            foreach (AnnotationSyntax annotation in annotations ?? [])
            {
                if (!KnownAnnotations.Contains(annotation.NameToken.Text))
                {
                    AddDiagnostic("COPE-GPU-ANNOTATION-0001", "SDSL-V1401", "annotation", $"Unknown GPU annotation '@{annotation.NameToken.Text}'.", Span(path, annotation.NameToken));
                }
            }
        }

        private VdMirComputeModule CreateModule()
        {
            string[] types = _resources.Select(resource => resource.ElementType)
                .Concat(_boundFunctions.SelectMany(function => function.Parameters.Select(parameter => parameter.Type)))
                .Concat(_boundFunctions.Select(function => function.ReturnType))
                .Where(type => type != "error")
                .Append("bool")
                .Append("f32")
                .Append("u32")
                .Append("uint3")
                .Append("void")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(type => type, StringComparer.Ordinal)
                .ToArray();
            return new VdMirComputeModule(
                VdMirComputeModule.CurrentSchema,
                VdMirComputeModule.CanonicalConformanceSchema,
                _resources.Any(resource => resource.ElementType == "acceleration_structure") ? "compute.rayquery.m2" : VdMirComputeModule.ComputeM1FeatureLevel,
                _request.Sources.Select(source => source.Path).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                types,
                _resources.OrderBy(resource => resource.Set).ThenBy(resource => resource.Binding).ThenBy(resource => resource.Name, StringComparer.Ordinal).ToArray(),
                GpuReachability.Functions(_entryPoint is null ? [] : [_entryPoint.Name], _boundFunctions),
                _entryPoint,
                _diagnostics.OrderBy(diagnostic => diagnostic.PrimarySpan.File, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.PrimarySpan.Start).ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ToArray())
            {
                Enums = _enums is null || _enums.Definitions.Count == 0 ? null : _enums.Definitions,
                ValueTypes = _values is null || _values.Definitions.Count == 0 ? null : _values.Definitions,
                GenericSpecializations = _generics is null || _generics.Traces.Count == 0 ? null : _generics.Traces,
            };
        }

        private static Dictionary<string, ValueBinding> CloneScope(Dictionary<string, ValueBinding> source)
            => new(source, StringComparer.Ordinal);

        private static AnnotationSyntax? FindAnnotation(IReadOnlyList<AnnotationSyntax>? annotations, string name)
            => annotations?.FirstOrDefault(annotation => annotation.NameToken.Text == name);

        private static bool HasAnnotation(IReadOnlyList<AnnotationSyntax>? annotations, string name)
            => FindAnnotation(annotations, name) is not null;

        private static string? SingleNameArgument(AnnotationSyntax annotation)
            => annotation.Arguments.Count == 1 && annotation.Arguments[0] is NameExpressionSyntax name
                ? name.IdentifierToken.Text
                : null;

        private static int? SingleIntegerArgument(AnnotationSyntax annotation)
            => annotation.Arguments.Count == 1 ? IntegerLiteralValue(annotation.Arguments[0]) : null;

        private static int IntegerLiteralValue(ExpressionSyntax expression)
            => expression is LiteralExpressionSyntax literal
                && literal.LiteralToken.Kind == SyntaxKind.NumberToken
                && int.TryParse(literal.LiteralToken.Text, out int value)
                    ? value
                    : 0;

        private static VdMirExpression ErrorExpression(string path, SyntaxNode syntax)
            => new("error", "error", Span(path, syntax));

        private void TypeMismatch(string path, SyntaxNode syntax, string expected, string actual)
            => AddDiagnostic("COPE-GPU-TYPE-0002", "SDSL-V1503", "type", $"Expected '{expected}', got '{actual}'.", Span(path, syntax));

        private void AddHostOnly(string path, SyntaxNode syntax, string construct)
            => AddDiagnostic("COPE-GPU-CLOSURE-0001", "SDSL-V4200", "host-only", $"Reachable {construct} has no closed GPU semantics.", Span(path, syntax));

        private void AddHostOnly(string path, SyntaxToken token, string construct)
            => AddDiagnostic("COPE-GPU-CLOSURE-0001", "SDSL-V4200", "host-only", $"Reachable {construct} has no closed GPU semantics.", Span(path, token));

        private void AddDiagnostic(
            string code,
            string canonicalCode,
            string category,
            string message,
            VdMirSourceSpan primarySpan,
            IReadOnlyList<VdMirRelatedSpan>? relatedSpans = null)
        {
            _diagnostics.Add(new VdMirDiagnostic(code, canonicalCode, category, message, primarySpan, relatedSpans ?? []));
        }

        private static VdMirSourceSpan Span(string path, SyntaxToken token)
            => new(path, token.Position, Math.Max(1, token.Text.Length));

        private static VdMirSourceSpan Span(string path, SyntaxNode syntax)
        {
            SyntaxToken[] tokens = Tokens(syntax).ToArray();
            if (tokens.Length == 0) return new VdMirSourceSpan(path, 0, 0);
            int start = tokens.Min(token => token.Position);
            int end = tokens.Max(token => token.Position + token.Text.Length);
            return new VdMirSourceSpan(path, start, Math.Max(1, end - start));
        }

        private static IEnumerable<SyntaxToken> Tokens(SyntaxNode syntax)
        {
            foreach (object child in syntax.GetChildren())
            {
                if (child is SyntaxToken token) yield return token;
                if (child is SyntaxNode node)
                {
                    foreach (SyntaxToken nested in Tokens(node)) yield return nested;
                }
            }
        }

        private sealed record FunctionSource(string Path, FunctionDeclarationSyntax Syntax);

        private sealed record ValueBinding(
            string Type,
            bool Mutable,
            VdMirResource? Resource,
            bool Builtin);
    }
}
