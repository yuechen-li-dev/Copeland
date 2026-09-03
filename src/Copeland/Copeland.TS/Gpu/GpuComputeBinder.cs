using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

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
            foreach (GpuSourceFile source in _request.Sources.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                SyntaxTree tree = SyntaxTree.Parse(source.Source, source.Path);
                foreach (Copeland.TS.Diagnostics.Diagnostic diagnostic in tree.Diagnostics)
                {
                    AddDiagnostic(
                        diagnostic.Id,
                        "SDSL-V1000",
                        "syntax",
                        diagnostic.Message,
                        new VdMirSourceSpan(source.Path, diagnostic.Position, diagnostic.Length));
                }

                foreach (FunctionDeclarationSyntax function in tree.Root.Members.OfType<FunctionDeclarationSyntax>())
                {
                    if (!_functions.TryAdd(function.Identifier.Text, new FunctionSource(source.Path, function)))
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
            if (entry.Syntax.TypeParameters.Count > 0)
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

                if (type == "storage-buffer<f32>" && binding is not null)
                {
                    int? bindingIndex = SingleIntegerArgument(binding);
                    VdMirResourceAccess? access = parameter.AccessToken?.Text switch
                    {
                        "readonly" => VdMirResourceAccess.Readonly,
                        "readwrite" => VdMirResourceAccess.Readwrite,
                        _ => null,
                    };
                    if (bindingIndex is null || bindingIndex < 0 || access is null)
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
                        "f32",
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
                entry.Syntax.Identifier.Text,
                entry.Syntax.Identifier.Text,
                dimensions[0],
                dimensions[1],
                dimensions[2],
                builtins,
                Span(entry.Path, entry.Syntax));
            _boundFunctions.Add(new VdMirFunction(
                entry.Syntax.Identifier.Text,
                builtins,
                "void",
                statements,
                Span(entry.Path, entry.Syntax)));
            _completedFunctions.Add(entry.Syntax.Identifier.Text);
        }

        private IReadOnlyList<VdMirStatement> BindFunctionBody(
            FunctionSource function,
            Dictionary<string, ValueBinding> scope,
            string returnType)
        {
            if (!_activeFunctions.Add(function.Syntax.Identifier.Text))
            {
                AddDiagnostic(
                    "COPE-GPU-RECURSION-0001",
                    "SDSL-V4201",
                    "recursion",
                    $"Reachable recursion through '{function.Syntax.Identifier.Text}' is deferred in compute M1.",
                    Span(function.Path, function.Syntax.Identifier));
                return [];
            }

            if (function.Syntax.AsyncKeyword is not null)
            {
                AddHostOnly(function.Path, function.Syntax.AsyncKeyword, "async functions");
            }

            var statements = BindStatements(function.Path, function.Syntax.Body.Statements, scope, returnType);
            _activeFunctions.Remove(function.Syntax.Identifier.Text);
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
                        VdMirExpression initializer = BindExpression(path, local.Initializer, scope);
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
                        VdMirExpression target = BindExpression(path, assignment.Left, scope);
                        VdMirExpression value = BindExpression(path, assignment.Right, scope);
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
                        result.Add(new VdMirStatement(
                            "expression",
                            Span(path, expressionStatement),
                            Expression: BindExpression(path, expressionStatement.Expression, scope)));
                        break;
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
                            : BindExpression(path, returnStatement.Expression, scope);
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
            Dictionary<string, ValueBinding> scope)
        {
            switch (expression)
            {
                case NameExpressionSyntax name:
                    if (scope.TryGetValue(name.IdentifierToken.Text, out ValueBinding? binding))
                    {
                        return new VdMirExpression("name", binding.Type, Span(path, expression), name.IdentifierToken.Text);
                    }
                    AddDiagnostic("COPE-GPU-NAME-0001", "SDSL-V1501", "name", $"Unknown GPU value '{name.IdentifierToken.Text}'.", Span(path, name.IdentifierToken));
                    return ErrorExpression(path, expression);
                case LiteralExpressionSyntax literal:
                    return BindLiteral(path, literal);
                case ParenthesizedExpressionSyntax parenthesized:
                    return BindExpression(path, parenthesized.Expression, scope);
                case MemberAccessExpressionSyntax member:
                {
                    VdMirExpression target = BindExpression(path, member.Target, scope);
                    if (target.Type == "uint3" && member.NameToken.Text is "x" or "y" or "z")
                    {
                        return new VdMirExpression("field", "u32", Span(path, member), member.NameToken.Text, [target]);
                    }
                    AddDiagnostic("COPE-GPU-MEMBER-0001", "SDSL-V1502", "member", $"Member '{member.NameToken.Text}' is not available on '{target.Type}'.", Span(path, member.NameToken));
                    return ErrorExpression(path, expression);
                }
                case IndexExpressionSyntax index:
                {
                    VdMirExpression target = BindExpression(path, index.Target, scope);
                    VdMirExpression subscript = BindExpression(path, index.Index, scope);
                    if (target.Type != "storage-buffer<f32>" || subscript.Type != "u32")
                    {
                        AddDiagnostic("COPE-GPU-INDEX-0001", "SDSL-V1503", "indexing", "Compute M1 indexing requires StorageBuffer<f32>[u32].", Span(path, index));
                        return ErrorExpression(path, expression);
                    }
                    return new VdMirExpression("index", "f32", Span(path, index), Operands: [target, subscript]);
                }
                case BinaryExpressionSyntax binary:
                    return BindBinary(path, binary, scope);
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

        private VdMirExpression BindCall(string path, CallExpressionSyntax call, Dictionary<string, ValueBinding> scope)
        {
            if (call.Target is not NameExpressionSyntax name || !_functions.TryGetValue(name.IdentifierToken.Text, out FunctionSource? function))
            {
                AddHostOnly(path, call, "host or unresolved call");
                return ErrorExpression(path, call);
            }

            string returnType = BindType(function.Path, function.Syntax.ReturnType, function.Syntax.Identifier);
            var arguments = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            if (function.Syntax.Parameters.Count != arguments.Length)
            {
                AddDiagnostic("COPE-GPU-CALL-0001", "SDSL-V1503", "call", $"Function '{function.Syntax.Identifier.Text}' expects {function.Syntax.Parameters.Count} argument(s).", Span(path, call));
            }

            var helperScope = new Dictionary<string, ValueBinding>(StringComparer.Ordinal);
            for (int index = 0; index < function.Syntax.Parameters.Count; index++)
            {
                ParameterSyntax parameter = function.Syntax.Parameters[index];
                string parameterType = BindType(function.Path, parameter.Type, parameter.Identifier);
                helperScope[parameter.Identifier.Text] = new ValueBinding(parameterType, false, null, false);
                if (index < arguments.Length && arguments[index].Type != parameterType)
                {
                    TypeMismatch(path, call.Arguments[index], parameterType, arguments[index].Type);
                }
            }

            if (!_completedFunctions.Contains(function.Syntax.Identifier.Text))
            {
                IReadOnlyList<VdMirStatement> statements = BindFunctionBody(function, helperScope, returnType);
                if (!_completedFunctions.Contains(function.Syntax.Identifier.Text))
                {
                    _boundFunctions.Add(new VdMirFunction(
                        function.Syntax.Identifier.Text,
                        function.Syntax.Parameters.Select(parameter => new VdMirParameter(
                            parameter.Identifier.Text,
                            BindType(function.Path, parameter.Type, parameter.Identifier),
                            null,
                            Span(function.Path, parameter))).ToArray(),
                        returnType,
                        statements,
                        Span(function.Path, function.Syntax)));
                    _completedFunctions.Add(function.Syntax.Identifier.Text);
                }
            }

            return new VdMirExpression("call", returnType, Span(path, call), function.Syntax.Identifier.Text, arguments);
        }

        private VdMirExpression BindBinary(string path, BinaryExpressionSyntax binary, Dictionary<string, ValueBinding> scope)
        {
            VdMirExpression left = BindExpression(path, binary.Left, scope);
            VdMirExpression right = BindExpression(path, binary.Right, scope);
            string? resultType = binary.OperatorToken.Kind switch
            {
                SyntaxKind.PlusToken when left.Type == "f32" && right.Type == "f32" => "f32",
                SyntaxKind.PlusToken when left.Type == "u32" && right.Type == "u32" => "u32",
                SyntaxKind.LessToken when left.Type == right.Type && left.Type is "f32" or "u32" => "bool",
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
            string type = literal.LiteralToken.Kind switch
            {
                SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword => "bool",
                SyntaxKind.NumberToken when literal.LiteralToken.Text.Contains('.', StringComparison.Ordinal) => "f32",
                SyntaxKind.NumberToken => "u32",
                _ => "error",
            };
            if (type == "error") AddHostOnly(path, literal, "non-GPU literal");
            return new VdMirExpression("literal", type, Span(path, literal), literal.LiteralToken.Text);
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
            string? result = type switch
            {
                IdentifierTypeSyntax identifier when identifier.Identifier.Text is "f32" or "u32" or "bool" or "uint3" => identifier.Identifier.Text,
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
                VdMirComputeModule.ComputeM1FeatureLevel,
                _request.Sources.Select(source => source.Path).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                types,
                _resources.OrderBy(resource => resource.Set).ThenBy(resource => resource.Binding).ThenBy(resource => resource.Name, StringComparer.Ordinal).ToArray(),
                _boundFunctions.OrderBy(function => function.Name == _entryPoint?.Name ? 1 : 0).ThenBy(function => function.Name, StringComparer.Ordinal).ToArray(),
                _entryPoint,
                _diagnostics.OrderBy(diagnostic => diagnostic.PrimarySpan.File, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.PrimarySpan.Start).ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ToArray());
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
