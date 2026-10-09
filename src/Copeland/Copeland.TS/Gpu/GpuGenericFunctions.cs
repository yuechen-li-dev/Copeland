using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

/// <summary>Bind each open GPU body once, then close its typed plan before VdMir emission.</summary>
internal sealed class GpuGenericFunctions
{
    private readonly GpuModuleGraph modules;
    private readonly GpuValues values;
    private readonly Action<string, string, VdMirSourceSpan> error;
    private readonly Func<string, TypeSyntax, string> bindType;
    private readonly Func<string, FunctionDeclarationSyntax, VdMirFunction> bindBody;
    private readonly Action<VdMirFunction> add;
    private readonly Func<IEnumerable<VdMirFunction>> functions;
    private readonly Dictionary<string, (string Path, FunctionDeclarationSyntax Syntax)> definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Plan> plans = new(StringComparer.Ordinal);
    private readonly HashSet<string> activePlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Request> requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> completed = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeSpecializations = new(StringComparer.Ordinal);
    private readonly List<VdMirGenericSpecialization> traces = [];
    private readonly Dictionary<string, int> bodyBindings = new(StringComparer.Ordinal);
    private readonly HashSet<string> constructionTemplates = new(StringComparer.Ordinal);
    private List<VdMirFunction>? captured;
    private sealed record Plan(
        string Identity,
        string Path,
        FunctionDeclarationSyntax Syntax,
        GenericParameterListSyntax Parameters,
        GpuValues.Environment Open,
        VdMirFunction Body,
        IReadOnlyList<VdMirFunction> Helpers
    );
    private sealed record Request(Plan Plan, GpuValues.Environment Arguments, VdMirSourceSpan Source);
    public GpuGenericFunctions(
        GpuModuleGraph modules,
        GpuValues values,
        Action<string, string, VdMirSourceSpan> error,
        Func<string, TypeSyntax, string> bindType,
        Func<string, FunctionDeclarationSyntax, VdMirFunction> bindBody,
        Action<VdMirFunction> add,
        Func<IEnumerable<VdMirFunction>> functions
    )
    {
        this.modules = modules;
        this.values = values;
        this.error = error;
        this.bindType = bindType;
        this.bindBody = bindBody;
        this.add = add;
        this.functions = functions;
        foreach (var source in modules.Sources)
        {
            foreach (var function in source.Tree.Root.Members.OfType<FunctionDeclarationSyntax>())
            {
                if (function.TypeParameters.Count > 0 || function.GenericParameters?.Values.Count > 0)
                {
                    definitions.TryAdd(modules.Declare(source.Source.Path, function.Identifier.Text), (source.Source.Path, function));
                }
            }

            foreach (var template in source.Tree.Root.Members.OfType<TemplateDeclarationSyntax>())
            {
                string identity = modules.Declare(source.Source.Path, template.Identifier.Text);
                constructionTemplates.Add(identity);
                definitions.TryAdd(identity, (source.Source.Path, TemplateBody(template)));
            }
        }
    }

    private static FunctionDeclarationSyntax TemplateBody(TemplateDeclarationSyntax template)
    {
        // Adapt the declaration shell, retaining the authored body and parameter nodes.
        // The body is bound once by the same typed-plan path as a generic function.
        var functionToken = new SyntaxToken(SyntaxKind.FunctionKeyword, template.Identifier.Position, "function", null);
        var open = new SyntaxToken(SyntaxKind.OpenParenToken, template.Identifier.Position, "(", null);
        var close = open with
        {
            Kind = SyntaxKind.CloseParenToken,
            Text = ")"
        };
        return new FunctionDeclarationSyntax(
            null,
            null,
            functionToken,
            null,
            template.Identifier,
            template.LessToken,
            template.TypeParameters,
            [],
            template.GreaterToken,
            open,
            [],
            [],
            close,
            template.ReturnTypeColonToken,
            template.ReturnType,
            template.Body,
            []
        )
        {
            GenericParameters = new(
                template.TemplateKeyword,
                template.LessToken,
                template.TypeParameters,
                template.Parameters,
                template.CommaTokens,
                template.GreaterToken
            ),
        };
    }

    public VdMirExpression Instantiate(
        string path,
        TemplateInstantiationExpressionSyntax syntax,
        Func<ExpressionSyntax, string?, VdMirExpression> bindExpression
    )
    {
        var source = new VdMirSourceSpan(
            path,
            syntax.InstantiateKeyword.Position,
            syntax.GreaterToken.Position + syntax.GreaterToken.Text.Length - syntax.InstantiateKeyword.Position
        );
        if (!constructionTemplates.Contains(modules.Resolve(path, syntax.TemplateIdentifier.Text)))
        {
            error("COPE-GPU-GENERIC-0002", "instantiate requires a declared construction template.", source);
            return new("error", "error", source);
        }

        TypeSyntax[] arguments = [.. syntax.TypeArguments, .. syntax.StaticArguments.Select(argument => (TypeSyntax)new GenericValueArgumentTypeSyntax(argument.Identifier, argument.ColonToken, argument.Value))];
        var call = Call(path, syntax.TemplateIdentifier.Text, arguments, [], bindExpression, source, construction: true)!;
        if (BindingOpen)
        {
            return new("generic-fold", call.Type, source, Operands: [call]);
        }

        try
        {
            return GpuStaticEvaluation.Fold(call, functions(), values.Definitions, aggregate => values.EmbedStatic(aggregate, add));
        }
        catch (Semantics.StaticEvaluationException exception)
        {
            error(exception.DiagnosticId, exception.Message, source);
            return new("error", "error", source);
        }
    }

    public IReadOnlyList<VdMirGenericSpecialization> Traces => traces.OrderBy(trace => trace.Identity, StringComparer.Ordinal).ToArray();
    public bool BindingOpen => captured is not null;

    public IDisposable EnterClosedBinding()
    {
        var previous = captured;
        captured = null;
        var context = values.Enter(GpuValues.Empty);
        return new Restore(() =>
            {
                context.Dispose();
                captured = previous;
            });
    }

    public bool Capture(VdMirFunction function)
    {
        if (captured is null)
        {
            return false;
        }

        captured.Add(function);
        return true;
    }

    public VdMirExpression? Call(
        string path,
        string name,
        IReadOnlyList<TypeSyntax>? explicitArguments,
        IReadOnlyList<ExpressionSyntax> arguments,
        Func<ExpressionSyntax, string?, VdMirExpression> bindExpression,
        VdMirSourceSpan source,
        bool construction = false
    )
    {
        string identity = modules.Resolve(path, name);
        if (!definitions.ContainsKey(identity))
        {
            return null;
        }

        if (constructionTemplates.Contains(identity) && !construction)
        {
            error("COPE-GPU-GENERIC-0002", "Construction templates require instantiate; they are not runtime functions.", source);
            return new("error", "error", source);
        }

        if ((definitions[identity].Syntax.Annotations ?? []).Any(annotation => annotation.NameToken.Text is "compute" or "vertex" or "pixel"))
        {
            error("COPE-GPU-GENERIC-0001", "Entry points cannot be generic helpers.", source);
            return new("error", "error", source);
        }

        Plan? plan = GetPlan(identity);
        if (plan is null)
        {
            return new("error", "error", source);
        }

        GpuValues.Environment environment;
        var boundArguments = new VdMirExpression?[arguments.Count];
        if (explicitArguments is not null)
        {
            environment = values.Arguments(path, plan.Parameters, explicitArguments, bindType, plan.Path);
        }
        else
        {
            var bindings = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int index = 0; index < arguments.Count; index++)
            {
                if (arguments[index] is ObjectLiteralExpressionSyntax or ArrayLiteralExpressionSyntax)
                {
                    continue;
                }

                VdMirExpression argument = bindExpression(arguments[index], null);
                boundArguments[index] = argument;
                if (index < plan.Body.Parameters.Count &&
                    !values.Infer(plan.Body.Parameters[index].Type, argument.Type, bindings))
                {
                    error("COPE-GPU-GENERIC-0005", "Conflicting or incompatible direct argument evidence for '" + name + "'.", source);
                }
            }

            var types = plan.Open.Types.Where(item => bindings.ContainsKey(item.Value)).ToDictionary(item => item.Key, item => (string)bindings[item.Value], StringComparer.Ordinal);
            var statics = plan.Open.Values.Where(item => bindings.ContainsKey(item.Value.Value!)).ToDictionary(item => item.Key, item => (VdMirExpression)bindings[item.Value.Value!], StringComparer.Ordinal);
            environment = values.CompleteArguments(plan.Path, plan.Parameters, new(types, statics), bindType);
        }

        var substitutions = GpuValues.Substitutions(plan.Open, environment);
        string returnType = values.CloseType(plan.Body.ReturnType, substitutions);
        if (arguments.Count != plan.Body.Parameters.Count)
        {
            error("COPE-GPU-GENERIC-0002", "Generic call has the wrong execution argument count.", source);
        }

        for (int index = 0; index < arguments.Count; index++)
        {
            string? expected = index < plan.Body.Parameters.Count ? values.CloseType(plan.Body.Parameters[index].Type, substitutions) : null;
            boundArguments[index] ??= bindExpression(arguments[index], expected);
            if (expected is not null && boundArguments[index]!.Type != expected)
            {
                error("COPE-GPU-GENERIC-0005", "Expected '" + expected + "', got '" + boundArguments[index]!.Type + "'.", source);
            }
        }

        string key = identity + GpuValues.ArgumentIdentity(environment);
        string request = GpuValues.StableName("Request", key);
        requests.TryAdd(request, new(plan, environment, source));
        VdMirExpression[] operands = boundArguments.Select(argument => argument!).ToArray();
        if (BindingOpen || environment.Types.Values.Any(values.IsOpen) ||
            environment.Values.Values.Any(values.IsOpen))
        {
            return new("generic-call", returnType, source, request, operands);
        }

        string specialized = Materialize(requests[request]);
        return new("call", returnType, source, specialized, operands);
    }

    private Plan? GetPlan(string identity)
    {
        if (plans.TryGetValue(identity, out Plan? existing))
        {
            return existing;
        }

        var definition = definitions[identity];
        if (!activePlans.Add(identity) || activePlans.Count > 32)
        {
            error(
                "COPE-GPU-GENERIC-0004",
                "Recursive generic dependency: " + string.Join(" -> ", activePlans.Append(identity)),
                new(definition.Path, definition.Syntax.Identifier.Position, definition.Syntax.Identifier.Text.Length)
            );
            return null;
        }

        GenericParameterListSyntax parameters = definition.Syntax.GenericParameters ?? new(
            null,
            definition.Syntax.LessToken!,
            definition.Syntax.TypeParameters,
            [],
            definition.Syntax.TypeParameterCommas,
            definition.Syntax.GreaterToken!
        );
        GpuValues.Environment environment = values.OpenEnvironment(definition.Path, identity, parameters, bindType);
        var helpers = new List<VdMirFunction>();
        var previous = captured;
        captured = helpers;
        VdMirFunction body;
        try
        {
            using var context = values.Enter(environment);
            bodyBindings[identity] = bodyBindings.GetValueOrDefault(identity) + 1;
            body = bindBody(definition.Path, definition.Syntax);
        }
        finally
        {
            captured = previous;
            activePlans.Remove(identity);
        }

        var plan = new Plan(identity, definition.Path, definition.Syntax, parameters, environment, body, helpers);
        plans[identity] = plan;
        return plan;
    }

    private string Materialize(Request request)
    {
        string identity = request.Plan.Identity + GpuValues.ArgumentIdentity(request.Arguments);
        if (completed.TryGetValue(identity, out string? previous))
        {
            return previous;
        }

        string name = GpuValues.StableName("Generic", identity);
        if (completed.Count >= 128 ||
            completed.Keys.Count(key => key.StartsWith(request.Plan.Identity + "<", StringComparison.Ordinal)) >= 16 ||
            !activeSpecializations.Add(identity) ||
            activeSpecializations.Count > 32)
        {
            error(
                "COPE-GPU-GENERIC-0004",
                "Specialization recursion or 16-per-definition/128-total budget exceeded: " + identity,
                request.Source
            );
            return name;
        }

        var substitutions = GpuValues.Substitutions(request.Plan.Open, request.Arguments);
        var helperNames = request.Plan.Helpers.ToDictionary(helper => helper.Name, helper => GpuValues.StableName("Helper", identity + ":" + helper.Name), StringComparer.Ordinal);
        foreach (VdMirFunction helper in request.Plan.Helpers)
        {
            add(RewriteFunction(helper, helperNames[helper.Name], substitutions, helperNames));
        }

        VdMirFunction function = RewriteFunction(request.Plan.Body, name, substitutions, helperNames);
        add(function);
        completed[identity] = name;
        activeSpecializations.Remove(identity);
        traces.Add(new(
                identity,
                request.Plan.Identity,
                name,
                request.Arguments.Types.Values.ToArray(),
                request.Arguments.Values.Select(item => item.Key + ":" + item.Value.Type + "=" + item.Value.Value).ToArray(),
                request.Source,
                request.Plan.Body.Source,
                bodyBindings[request.Plan.Identity]
            ));
        return name;
    }

    private VdMirFunction RewriteFunction(
        VdMirFunction function,
        string name,
        IReadOnlyDictionary<string, object> substitutions,
        IReadOnlyDictionary<string, string> helpers
    ) => function with
    {
        Name = name,
        ReturnType = values.CloseType(function.ReturnType, substitutions),
        Parameters = function.Parameters.Select(parameter => parameter with { Type = values.CloseType(parameter.Type, substitutions) }).ToArray(),
        Statements = RewriteStatements(function.Statements, substitutions, helpers),
    };
    private IReadOnlyList<VdMirStatement> RewriteStatements(
        IReadOnlyList<VdMirStatement> statements,
        IReadOnlyDictionary<string, object> substitutions,
        IReadOnlyDictionary<string, string> helpers
    ) => statements.Select(statement => statement with
        {
            Type = statement.Type is null ? null : values.CloseType(statement.Type, substitutions),
            Expression = statement.Expression is null ? null : RewriteExpression(statement.Expression, substitutions, helpers),
            Body = statement.Body is null ? null : RewriteStatements(statement.Body, substitutions, helpers),
            ElseBody = statement.ElseBody is null ? null : RewriteStatements(statement.ElseBody, substitutions, helpers),
            Initializer = statement.Initializer is null ? null : RewriteStatements([statement.Initializer], substitutions, helpers)[0],
            Increment = statement.Increment is null ? null : RewriteStatements([statement.Increment], substitutions, helpers)[0],
        }).ToArray();
    private VdMirExpression RewriteExpression(
        VdMirExpression expression,
        IReadOnlyDictionary<string, object> substitutions,
        IReadOnlyDictionary<string, string> helpers
    )
    {
        if (expression.Kind == "generic-static")
        {
            return values.SubstituteStatic(expression, substitutions);
        }

        VdMirExpression result = expression with
        {
            Type = values.CloseType(expression.Type, substitutions),
            Operands = expression.Operands?.Select(operand => RewriteExpression(operand, substitutions, helpers)).ToArray(),
        };
        if (result.Kind == "generic-call")
        {
            Request request = requests[result.Value!];
            return result with
            {
                Kind = "call",
                Value = Materialize(request with { Arguments = values.CloseEnvironment(request.Arguments, substitutions) })
            };
        }

        if (result.Kind == "call" && helpers.TryGetValue(result.Value!, out string? name))
        {
            return result with
            {
                Value = name
            };
        }

        if (result.Kind == "generic-fold")
        {
            try
            {
                return GpuStaticEvaluation.Fold(result.Operands![0], functions(), values.Definitions, aggregate => values.EmbedStatic(aggregate, add));
            }
            catch (Semantics.StaticEvaluationException exception)
            {
                error(exception.DiagnosticId, exception.Message, result.Source);
                return new("error", "error", result.Source);
            }
        }

        return values.LowerDeferred(result, add);
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
