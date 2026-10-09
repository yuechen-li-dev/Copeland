using System.Globalization;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

/// <summary>
/// Adapts certified scalar shader IR to the ordinary bounded static evaluator.
/// This is representation conversion, not another interpreter or host compiler.
/// </summary>
internal sealed class GpuStaticEvaluation
{
    private readonly Dictionary<string, VdMirFunction> sources;
    private readonly Dictionary<string, FunctionSymbol> symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoundFunctionDeclaration> functions = new(StringComparer.Ordinal);
    private readonly HashSet<string> active = new(StringComparer.Ordinal);
    private static readonly TypeSymbol F32 = new ScalarType("f32");
    private static readonly TypeSymbol U32 = new ScalarType("u32");

    private GpuStaticEvaluation(IEnumerable<VdMirFunction> functions)
    {
        sources = functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
    }

    public static VdMirExpression Fold(VdMirExpression expression, IEnumerable<VdMirFunction> functions)
    {
        var adapter = new GpuStaticEvaluation(functions);
        BoundExpression bound = adapter.Expression(expression, new(StringComparer.Ordinal));
        BoundFunctionDeclaration[] declarations = adapter.functions.Values.ToArray();
        var evaluator = new StaticEvaluator(declarations, FunctionEffectClassifier.Classify(declarations), StaticEvaluationLimits.M1);
        StaticValue result = evaluator.Evaluate(bound);
        if (result is not StaticPrimitiveValue value)
        {
            throw StaticEvaluationException.Unsupported("This GPU static slice embeds scalar values only.");
        }
        string text = value.Value switch
        {
            float number when float.IsFinite(number) => FloatLiteral(number),
            uint integer => integer.ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => throw StaticEvaluationException.Failure("Static GPU result must be a finite f32, u32 or bool."),
        };
        return new("literal", expression.Type, expression.Source, text);
    }

    private static string FloatLiteral(float value)
    {
        string result = value.ToString("R", CultureInfo.InvariantCulture);
        return result.Contains('.') || result.Contains('E') ? result : result + ".0";
    }

    private BoundExpression Expression(VdMirExpression expression, Dictionary<string, VariableSymbol> scope)
    {
        TypeSymbol type = Type(expression.Type);
        VdMirExpression[] operands = expression.Operands?.ToArray() ?? [];
        switch (expression.Kind)
        {
            case "literal":
                object value = expression.Type switch
                {
                    "f32" => float.Parse(expression.Value!, CultureInfo.InvariantCulture),
                    "u32" => uint.Parse(expression.Value!, CultureInfo.InvariantCulture),
                    "bool" => bool.Parse(expression.Value!),
                    _ => throw StaticEvaluationException.Unsupported("Unsupported static literal type."),
                };
                return new BoundLiteralExpression(value, type);
            case "name":
                if (!scope.TryGetValue(expression.Value!, out VariableSymbol? variable))
                {
                    throw StaticEvaluationException.Ineligible($"Static expression reads runtime shader value '{expression.Value}'.");
                }
                return new BoundVariableExpression(variable);
            case "binary":
                return new BoundBinaryExpression(Expression(operands[0], scope), Operator(expression.Value!), Expression(operands[1], scope), type);
            case "unary":
                return new BoundUnaryExpression(Operator(expression.Value!), Expression(operands[0], scope), type);
            case "call":
                FunctionSymbol function = Function(expression.Value!);
                return new BoundCallExpression(function, operands.Select(operand => Expression(operand, scope)).ToArray());
            default:
                throw StaticEvaluationException.Ineligible($"Static scalar closure contains '{expression.Kind}' ({expression.Value}); resources and GPU intrinsics require runtime execution.");
        }
    }

    private FunctionSymbol Function(string name)
    {
        if (symbols.TryGetValue(name, out FunctionSymbol? existing))
        {
            if (active.Contains(name))
            {
                throw StaticEvaluationException.Budget("Recursive static GPU closure: " + name);
            }
            return existing;
        }
        if (!sources.TryGetValue(name, out VdMirFunction? source))
        {
            throw StaticEvaluationException.Ineligible("Static function body is unavailable: " + name);
        }
        var parameters = source.Parameters.Select(parameter => new ParameterSymbol(parameter.Name, Type(parameter.Type))).ToArray();
        var symbol = new FunctionSymbol(name, parameters, Type(source.ReturnType));
        symbols.Add(name, symbol);
        active.Add(name);
        var scope = parameters.ToDictionary(parameter => parameter.Name, parameter => new VariableSymbol(parameter.Name, parameter.Type, true), StringComparer.Ordinal);
        functions.Add(name, new BoundFunctionDeclaration(symbol, Statements(source.Statements, scope)));
        active.Remove(name);
        return symbol;
    }

    private BoundBlockStatement Statements(IReadOnlyList<VdMirStatement> statements, Dictionary<string, VariableSymbol> scope)
    {
        var result = new List<BoundStatement>();
        foreach (VdMirStatement statement in statements)
        {
            switch (statement.Kind)
            {
                case "local":
                    BoundExpression initializer = Expression(statement.Expression!, scope);
                    var variable = new VariableSymbol(statement.Name!, Type(statement.Type!), !statement.Mutable);
                    scope.Add(statement.Name!, variable);
                    result.Add(new BoundVariableDeclaration(variable, initializer));
                    break;
                case "assign":
                    string? targetName = statement.Name;
                    VdMirExpression assignedValue = statement.Expression!;
                    if (targetName is null && assignedValue.Kind == "assignment"
                        && assignedValue.Operands is { Count: 2 } operands
                        && operands[0].Kind == "name")
                    {
                        targetName = operands[0].Value;
                        assignedValue = operands[1];
                    }
                    if (targetName is null || !scope.TryGetValue(targetName, out VariableSymbol? target))
                    {
                        throw StaticEvaluationException.Ineligible("Static shader assignment requires a local scalar var.");
                    }
                    result.Add(new BoundExpressionStatement(new BoundAssignmentExpression(target, Expression(assignedValue, scope))));
                    break;
                case "return":
                    result.Add(new BoundReturnStatement(Expression(statement.Expression!, scope)));
                    break;
                case "if":
                    result.Add(new BoundIfStatement(Expression(statement.Expression!, scope),
                        Statements(statement.Body ?? [], new(scope, StringComparer.Ordinal)),
                        Statements(statement.ElseBody ?? [], new(scope, StringComparer.Ordinal))));
                    break;
                case "block":
                    result.Add(Statements(statement.Body ?? [], new(scope, StringComparer.Ordinal)));
                    break;
                case "for":
                    var loopScope = new Dictionary<string, VariableSymbol>(scope, StringComparer.Ordinal);
                    BoundBlockStatement initial = Statements([statement.Initializer!], loopScope);
                    BoundBlockStatement increment = Statements([statement.Increment!], loopScope);
                    result.Add(new BoundForStatement(initial, Expression(statement.Expression!, loopScope),
                        ((BoundExpressionStatement)increment.Statements[0]).Expression,
                        Statements(statement.Body ?? [], loopScope)));
                    break;
                default:
                    throw StaticEvaluationException.Ineligible("Static GPU function contains runtime statement: " + statement.Kind);
            }
        }
        return new BoundBlockStatement(result);
    }

    private static TypeSymbol Type(string type) => type switch
    {
        "f32" => F32,
        "u32" => U32,
        "bool" => PrimitiveTypeSymbol.Boolean,
        _ => throw StaticEvaluationException.Unsupported($"GPU static scalar closure cannot represent '{type}'."),
    };

    private static SyntaxKind Operator(string operation) => operation switch
    {
        "+" => SyntaxKind.PlusToken,
        "-" => SyntaxKind.MinusToken,
        "*" => SyntaxKind.StarToken,
        "/" => SyntaxKind.SlashToken,
        "<" => SyntaxKind.LessToken,
        "<=" => SyntaxKind.LessOrEqualsToken,
        ">" => SyntaxKind.GreaterToken,
        ">=" => SyntaxKind.GreaterOrEqualsToken,
        "==" => SyntaxKind.EqualsEqualsToken,
        "!=" => SyntaxKind.BangEqualsToken,
        "&&" => SyntaxKind.AmpersandAmpersandToken,
        "||" => SyntaxKind.PipePipeToken,
        "!" => SyntaxKind.BangToken,
        _ => throw StaticEvaluationException.Unsupported("Unsupported GPU static operator: " + operation),
    };

    private sealed class ScalarType(string name) : TypeSymbol
    {
        public override string Name { get; } = name;
    }
}
