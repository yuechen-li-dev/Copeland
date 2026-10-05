using System.Globalization;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

/// <summary>
/// Fixed deterministic limits for the first general static evaluator. They are
/// centralized so a future compiler option can expose them without changing
/// evaluation semantics or cache identity.
/// </summary>
public sealed record StaticEvaluationLimits(
    int MaximumSteps,
    int MaximumCallDepth,
    int MaximumLoopIterations,
    int MaximumAllocatedValues,
    int MaximumArrayLength,
    int MaximumEmbeddedValues)
{
    public static StaticEvaluationLimits M1 { get; } = new(
        MaximumSteps: 100_000,
        MaximumCallDepth: 128,
        MaximumLoopIterations: 100_000,
        MaximumAllocatedValues: 200_000,
        MaximumArrayLength: 65_536,
        MaximumEmbeddedValues: 100_000);
}

public abstract record StaticValue(TypeSymbol Type);

public sealed record StaticPrimitiveValue(object? Value, TypeSymbol PrimitiveType)
    : StaticValue(PrimitiveType);

public sealed record StaticArrayValue(
    IReadOnlyList<StaticValue> Elements,
    ArrayTypeSymbol ArrayType)
    : StaticValue(ArrayType);

internal sealed record StaticMutableArrayValue(
    StaticValue[] Elements,
    MutableArrayTypeSymbol MutableArrayType)
    : StaticValue(MutableArrayType);

public sealed record StaticRecordValue(
    IReadOnlyDictionary<RecordFieldSymbol, StaticValue> Fields,
    RecordTypeSymbol RecordType)
    : StaticValue(RecordType);

public sealed record StaticEnumValue(
    EnumCaseSymbol Case,
    IReadOnlyList<StaticValue> Payloads)
    : StaticValue(Case.EnumType);

public sealed record StaticResultValue(
    bool IsOk,
    StaticValue Payload,
    ResultTypeSymbol ResultType)
    : StaticValue(ResultType);

/// <summary>
/// Executes and resolves every ordinary BoundStaticExpression before MIR. The
/// evaluator consumes the existing effect summaries; it never reclassifies a
/// function or probes a backend/host operation.
/// </summary>
public static class StaticEvaluationPass
{
    public static IReadOnlyList<Diagnostic> Evaluate(
        IReadOnlyList<BoundCompilation> compilations,
        StaticEvaluationLimits? limits = null,
        IReadOnlyDictionary<BoundCompilation, string?>? sourcePaths = null)
    {
        BoundFunctionDeclaration[] functions = compilations
            .SelectMany(compilation => compilation.Program.Functions)
            .ToArray();
        var summaries = new Dictionary<FunctionSymbol, FunctionEffectSummary>();
        foreach (BoundCompilation compilation in compilations)
        {
            foreach ((FunctionSymbol function, FunctionEffectSummary summary) in compilation.Program.FunctionEffects)
            {
                summaries[function] = summary;
            }
        }

        var evaluator = new StaticEvaluator(functions, summaries, limits ?? StaticEvaluationLimits.M1);
        var diagnostics = new List<Diagnostic>();
        foreach (BoundCompilation compilation in compilations)
        {
            var visitor = new StaticExpressionVisitor(
                evaluator,
                diagnostics,
                sourcePaths?.GetValueOrDefault(compilation));
            visitor.Visit(compilation.Program);
        }
        return diagnostics;
    }

    private sealed class StaticExpressionVisitor(
        StaticEvaluator evaluator,
        List<Diagnostic> diagnostics,
        string? sourcePath)
    {
        public void Visit(BoundProgram program)
        {
            foreach (BoundFunctionDeclaration function in program.Functions)
            {
                VisitStatement(function.Body);
            }
            foreach (BoundStatement statement in program.GlobalStatements)
            {
                VisitStatement(statement);
            }
        }

        private void VisitStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlockStatement block:
                    foreach (BoundStatement child in block.Statements) VisitStatement(child);
                    break;
                case BoundVariableDeclaration variable:
                    VisitExpression(variable.Initializer);
                    break;
                case BoundComponentStateDeclaration state:
                    VisitExpression(state.Initializer);
                    break;
                case BoundComponentEventHandler handler:
                    VisitExpression(handler.NextState);
                    foreach (BoundComponentEffect effect in handler.Effects)
                    {
                        VisitExpression(effect.Invocation);
                        if (effect.Completion is not null)
                        {
                            foreach (BoundExpression argument in effect.Completion.Arguments) VisitExpression(argument);
                        }
                    }
                    break;
                case BoundResourceUsingDeclaration resource:
                    VisitExpression(resource.Initializer);
                    break;
                case BoundExpressionStatement expression:
                    VisitExpression(expression.Expression);
                    break;
                case BoundIfStatement conditional:
                    VisitExpression(conditional.Condition);
                    VisitStatement(conditional.ThenStatement);
                    if (conditional.ElseStatement is not null) VisitStatement(conditional.ElseStatement);
                    break;
                case BoundWhileStatement loop:
                    VisitExpression(loop.Condition);
                    VisitStatement(loop.Body);
                    break;
                case BoundForStatement loop:
                    if (loop.Initializer is not null) VisitStatement(loop.Initializer);
                    if (loop.Condition is not null) VisitExpression(loop.Condition);
                    if (loop.Increment is not null) VisitExpression(loop.Increment);
                    VisitStatement(loop.Body);
                    break;
                case BoundForOfStatement loop:
                    VisitExpression(loop.Iterable);
                    VisitStatement(loop.Body);
                    break;
                case BoundReturnStatement returned:
                    if (returned.Expression is not null) VisitExpression(returned.Expression);
                    break;
                case BoundYieldStatement yielded:
                    if (yielded.Expression is not null) VisitExpression(yielded.Expression);
                    break;
            }
        }

        private void VisitExpression(BoundExpression expression)
        {
            if (expression is BoundStaticExpression staticExpression)
            {
                Resolve(staticExpression);
                return;
            }

            switch (expression)
            {
                case BoundAssignmentExpression assignment:
                    VisitExpression(assignment.Expression);
                    break;
                case BoundUnaryExpression unary:
                    VisitExpression(unary.Operand);
                    break;
                case BoundAwaitExpression awaited:
                    VisitExpression(awaited.Operand);
                    break;
                case BoundBinaryExpression binary:
                    VisitExpression(binary.Left);
                    VisitExpression(binary.Right);
                    break;
                case BoundNumericConversionExpression conversion:
                    VisitExpression(conversion.Operand);
                    break;
                case BoundCallExpression call:
                    VisitExpressions(call.Arguments);
                    break;
                case BoundNpmCallExpression call:
                    VisitExpressions(call.Arguments);
                    break;
                case BoundNpmDirectCallExpression call:
                    VisitExpressions(call.Arguments);
                    break;
                case BoundReactElementExpression react:
                    VisitExpression(react.ElementType);
                    foreach (BoundReactProperty property in react.Properties) VisitExpression(property.Value);
                    VisitExpressions(react.Children);
                    break;
                case BoundTextDocumentExpression document:
                    foreach (BoundTextValueSlot slot in document.Slots) VisitExpression(slot.Expression);
                    break;
                case BoundForeignComponentExpression foreign:
                    VisitExpression(foreign.Payload);
                    break;
                case BoundReactRootRenderExpression render:
                    VisitExpression(render.Root);
                    VisitExpression(render.Node);
                    break;
                case BoundReactRootUnmountExpression unmount:
                    VisitExpression(unmount.Root);
                    break;
                case BoundJavaScriptHostCallExpression call:
                    VisitExpressions(call.Arguments);
                    break;
                case BoundClrInvocationExpression call:
                    if (call.Receiver is not null) VisitExpression(call.Receiver);
                    VisitExpressions(call.Arguments);
                    break;
                case BoundClrPropertyAccessExpression access:
                    if (access.Receiver is not null) VisitExpression(access.Receiver);
                    break;
                case BoundCallableConstructionExpression callable:
                    VisitExpressions(callable.Captures);
                    break;
                case BoundInvokeExpression invoke:
                    VisitExpression(invoke.Callee);
                    VisitExpressions(invoke.Arguments);
                    break;
                case BoundEnumValueExpression value:
                    VisitExpressions(value.Arguments);
                    break;
                case BoundPropagateExpression propagate:
                    VisitExpression(propagate.Operand);
                    break;
                case BoundUnwrapExpression unwrap:
                    VisitExpression(unwrap.Operand);
                    break;
                case BoundBatchExpression batch:
                    VisitExpression(batch.Input);
                    VisitValueBlock(batch.Body);
                    break;
                case BoundTryExceptExpression attempt:
                    VisitValueBlock(attempt.Protected);
                    VisitValueBlock(attempt.Handler);
                    break;
                case BoundOkExpression ok:
                    VisitExpression(ok.Payload);
                    break;
                case BoundErrExpression err:
                    VisitExpression(err.Payload);
                    break;
                case BoundIfExpression conditional:
                    VisitExpression(conditional.Condition);
                    VisitExpression(conditional.ThenExpression);
                    VisitExpression(conditional.ElseExpression);
                    break;
                case BoundTsonEncodeExpression encode:
                    VisitExpression(encode.Operand);
                    break;
                case BoundTsonTransportExpression transport:
                    VisitExpression(transport.Operation);
                    VisitExpression(transport.Request);
                    break;
                case BoundMatchExpression match:
                    VisitExpression(match.Scrutinee);
                    foreach (BoundMatchArm arm in match.Arms) VisitExpression(arm.Expression);
                    break;
                case BoundResultMatchExpression match:
                    VisitExpression(match.Scrutinee);
                    VisitExpression(match.OkExpression);
                    VisitExpression(match.ErrExpression);
                    break;
                case BoundArrayExpression array:
                    VisitExpressions(array.Elements);
                    break;
                case BoundArrayLengthExpression length:
                    VisitExpression(length.Receiver);
                    break;
                case BoundArrayElementAccessExpression access:
                    VisitExpression(access.Receiver);
                    VisitExpression(access.Index);
                    break;
                case BoundArrayIterableExpression iterable:
                    VisitExpression(iterable.Receiver);
                    break;
                case BoundMutableArrayConstructionExpression construction:
                    VisitExpression(construction.Length);
                    break;
                case BoundMutableArrayLengthExpression length:
                    VisitExpression(length.Receiver);
                    break;
                case BoundMutableArrayElementAccessExpression access:
                    VisitExpression(access.Receiver);
                    VisitExpression(access.Index);
                    break;
                case BoundMutableArrayElementAssignmentExpression assignment:
                    VisitExpression(assignment.Receiver);
                    VisitExpression(assignment.Index);
                    VisitExpression(assignment.Value);
                    break;
                case BoundMutableArrayIterableExpression iterable:
                    VisitExpression(iterable.Receiver);
                    break;
                case BoundMutableArrayFreezeExpression freeze:
                    VisitExpression(freeze.Receiver);
                    break;
                case BoundRecordConstructionExpression record:
                    foreach (BoundRecordFieldInitializer field in record.Initializers) VisitExpression(field.Value);
                    break;
                case BoundRecordFieldAccessExpression access:
                    VisitExpression(access.Receiver);
                    break;
                case BoundRequirementFieldAccessExpression access:
                    VisitExpression(access.Receiver);
                    break;
                case BoundRecordWithExpression update:
                    VisitExpression(update.Source);
                    foreach (BoundRecordFieldInitializer field in update.Replacements) VisitExpression(field.Value);
                    break;
            }
        }

        private void Resolve(BoundStaticExpression expression)
        {
            if (expression.EvaluatedExpression is not null)
            {
                return;
            }
            try
            {
                StaticValue value = evaluator.Evaluate(expression.Expression);
                expression.Resolve(evaluator.Embed(value));
            }
            catch (StaticEvaluationException exception)
            {
                diagnostics.Add(new Diagnostic(
                    exception.DiagnosticId,
                    exception.Message,
                    expression.Anchor.Position,
                    Math.Max(1, expression.Anchor.Text.Length),
                    sourcePath));
            }
        }

        private void VisitValueBlock(BoundValueBlock block)
        {
            foreach (BoundStatement statement in block.PrefixStatements) VisitStatement(statement);
            VisitExpression(block.ValueExpression);
        }

        private void VisitExpressions(IEnumerable<BoundExpression> expressions)
        {
            foreach (BoundExpression expression in expressions) VisitExpression(expression);
        }
    }
}

internal sealed class StaticEvaluator
{
    private readonly IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> _functions;
    private readonly IReadOnlyDictionary<FunctionSymbol, FunctionEffectSummary> _summaries;
    private readonly StaticEvaluationLimits _limits;
    private readonly Dictionary<string, StaticValue> _completedCalls = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeCalls = new(StringComparer.Ordinal);
    private int _steps;
    private int _callDepth;
    private int _loopIterations;
    private int _allocatedValues;

    public StaticEvaluator(
        IReadOnlyList<BoundFunctionDeclaration> functions,
        IReadOnlyDictionary<FunctionSymbol, FunctionEffectSummary> summaries,
        StaticEvaluationLimits limits)
    {
        _functions = functions.ToDictionary(function => function.Symbol);
        _summaries = summaries;
        _limits = limits;
    }

    public StaticValue Evaluate(BoundExpression expression)
        => Evaluate(expression, new Dictionary<VariableSymbol, StaticValue>());

    internal StaticValue Evaluate(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, StaticValue> initialValues)
    {
        ResetBudgets();
        StaticValue value = EvaluateExpression(expression, new StaticEnvironment(initialValues));
        int embeddedValues = CountEmbeddedValues(value);
        if (embeddedValues > _limits.MaximumEmbeddedValues)
        {
            throw StaticEvaluationException.Budget(
                $"Static result contains {embeddedValues} values; the M1 embedded-value limit is {_limits.MaximumEmbeddedValues}.");
        }
        if (value is StaticMutableArrayValue)
        {
            throw StaticEvaluationException.Failure(
                "A MutableArray cannot cross the static/runtime boundary. Call freeze() and return an immutable array.");
        }
        return value;
    }

    public BoundExpression Embed(StaticValue value)
    {
        return value switch
        {
            StaticPrimitiveValue primitive => new BoundLiteralExpression(primitive.Value, primitive.Type),
            StaticArrayValue array => new BoundArrayExpression(array.Elements.Select(Embed).ToArray(), array.ArrayType),
            StaticRecordValue record => new BoundRecordConstructionExpression(
                record.RecordType,
                record.RecordType.Fields
                    .OrderBy(field => field.Id.Ordinal)
                    .Select(field => new BoundRecordFieldInitializer(field, Embed(record.Fields[field])))
                    .ToArray()),
            StaticEnumValue enumValue => new BoundEnumValueExpression(enumValue.Case, enumValue.Payloads.Select(Embed).ToArray()),
            StaticResultValue { IsOk: true } result => new BoundOkExpression(Embed(result.Payload), result.ResultType),
            StaticResultValue result => new BoundErrExpression(Embed(result.Payload), result.ResultType),
            StaticMutableArrayValue => throw StaticEvaluationException.Failure(
                "A MutableArray cannot be embedded; freeze it before returning from static evaluation."),
            _ => throw StaticEvaluationException.Unsupported($"Static value type '{value.Type.Name}' cannot be embedded."),
        };
    }

    private void ResetBudgets()
    {
        _steps = 0;
        _callDepth = 0;
        _loopIterations = 0;
        _allocatedValues = 0;
        _activeCalls.Clear();
    }

    private StaticValue EvaluateExpression(BoundExpression expression, StaticEnvironment environment)
    {
        Step();
        switch (expression)
        {
            case BoundStaticExpression nested:
                return EvaluateExpression(nested.Expression, environment);
            case BoundLiteralExpression literal:
                return Allocate(new StaticPrimitiveValue(literal.Value, literal.Type));
            case BoundUnitExpression:
                return Allocate(new StaticPrimitiveValue(null, PrimitiveTypeSymbol.Void));
            case BoundVariableExpression variable:
                return environment.Get(variable.Variable);
            case BoundAssignmentExpression assignment:
            {
                StaticValue value = EvaluateExpression(assignment.Expression, environment);
                environment.Set(assignment.Variable, value);
                return value;
            }
            case BoundUnaryExpression unary:
                return EvaluateUnary(unary, environment);
            case BoundBinaryExpression binary:
                return EvaluateBinary(binary, environment);
            case BoundNumericConversionExpression conversion:
                return EvaluateConversion(conversion, environment);
            case BoundCallExpression call:
                return EvaluateCall(call, environment);
            case BoundEnumValueExpression value:
                return Allocate(new StaticEnumValue(
                    value.Case,
                    value.Arguments.Select(argument => EvaluateExpression(argument, environment)).ToArray()));
            case BoundUnwrapExpression unwrap:
                return EvaluateUnwrap(unwrap, environment);
            case BoundPropagateExpression propagate:
                return EvaluatePropagation(propagate, environment);
            case BoundOkExpression ok:
                return Allocate(new StaticResultValue(
                    true,
                    EvaluateExpression(ok.Payload, environment),
                    (ResultTypeSymbol)ok.Type));
            case BoundErrExpression err:
                return Allocate(new StaticResultValue(
                    false,
                    EvaluateExpression(err.Payload, environment),
                    (ResultTypeSymbol)err.Type));
            case BoundIfExpression conditional:
                return AsBoolean(EvaluateExpression(conditional.Condition, environment))
                    ? EvaluateExpression(conditional.ThenExpression, environment)
                    : EvaluateExpression(conditional.ElseExpression, environment);
            case BoundMatchExpression match:
                return EvaluateMatch(match, environment);
            case BoundResultMatchExpression match:
                return EvaluateResultMatch(match, environment);
            case BoundArrayExpression array:
                return EvaluateArray(array, environment);
            case BoundArrayLengthExpression length:
                return EvaluateLength(length, environment);
            case BoundArrayElementAccessExpression access:
                return EvaluateArrayAccess(access, environment);
            case BoundArrayIterableExpression iterable:
                return EvaluateExpression(iterable.Receiver, environment);
            case BoundMutableArrayConstructionExpression construction:
                return EvaluateMutableArrayConstruction(construction, environment);
            case BoundMutableArrayLengthExpression length:
                return Allocate(new StaticPrimitiveValue(
                    AsMutableArray(EvaluateExpression(length.Receiver, environment)).Elements.Length,
                    PrimitiveTypeSymbol.Int));
            case BoundMutableArrayElementAccessExpression access:
                return EvaluateMutableArrayAccess(access, environment);
            case BoundMutableArrayElementAssignmentExpression assignment:
                return EvaluateMutableArrayAssignment(assignment, environment);
            case BoundMutableArrayIterableExpression iterable:
                return EvaluateExpression(iterable.Receiver, environment);
            case BoundMutableArrayFreezeExpression freeze:
                return EvaluateFreeze(freeze, environment);
            case BoundRecordConstructionExpression record:
                return EvaluateRecord(record, environment);
            case BoundRecordFieldAccessExpression access:
                return AsRecord(EvaluateExpression(access.Receiver, environment)).Fields[access.Field];
            case BoundRecordWithExpression update:
                return EvaluateRecordUpdate(update, environment);
            case BoundTryExceptExpression attempt:
                return EvaluateTryExcept(attempt, environment);
            default:
                throw StaticEvaluationException.Unsupported(
                    $"Language operation '{expression.GetType().Name}' is not supported by the M1 static evaluator.");
        }
    }

    private StaticValue EvaluateUnary(BoundUnaryExpression unary, StaticEnvironment environment)
    {
        StaticPrimitiveValue operand = AsPrimitive(EvaluateExpression(unary.Operand, environment));
        object value = unary.OperatorKind switch
        {
            SyntaxKind.MinusToken when operand.Value is int integer => unchecked(-integer),
            SyntaxKind.MinusToken when operand.Value is double number => -number,
            SyntaxKind.BangToken when operand.Value is bool boolean => !boolean,
            _ => throw StaticEvaluationException.Failure("Invalid unary operation during static evaluation."),
        };
        return Allocate(new StaticPrimitiveValue(value, unary.Type));
    }

    private StaticValue EvaluateBinary(BoundBinaryExpression binary, StaticEnvironment environment)
    {
        StaticPrimitiveValue left = AsPrimitive(EvaluateExpression(binary.Left, environment));
        if (binary.OperatorKind == SyntaxKind.AmpersandAmpersandToken && left.Value is false)
        {
            return Allocate(new StaticPrimitiveValue(false, PrimitiveTypeSymbol.Boolean));
        }
        if (binary.OperatorKind == SyntaxKind.PipePipeToken && left.Value is true)
        {
            return Allocate(new StaticPrimitiveValue(true, PrimitiveTypeSymbol.Boolean));
        }
        StaticPrimitiveValue right = AsPrimitive(EvaluateExpression(binary.Right, environment));
        object value;
        try
        {
            value = EvaluatePrimitiveBinary(left.Value, binary.OperatorKind, right.Value);
        }
        catch (DivideByZeroException)
        {
            throw StaticEvaluationException.Failure("Division by zero occurred during static evaluation.");
        }
        return Allocate(new StaticPrimitiveValue(value, binary.Type));
    }

    private static object EvaluatePrimitiveBinary(object? left, SyntaxKind operation, object? right)
    {
        if (left is int leftInteger && right is int rightInteger)
        {
            return operation switch
            {
                SyntaxKind.PlusToken => unchecked(leftInteger + rightInteger),
                SyntaxKind.MinusToken => unchecked(leftInteger - rightInteger),
                SyntaxKind.StarToken => unchecked(leftInteger * rightInteger),
                SyntaxKind.SlashToken => leftInteger / rightInteger,
                SyntaxKind.PercentToken => leftInteger % rightInteger,
                SyntaxKind.LessToken => leftInteger < rightInteger,
                SyntaxKind.LessOrEqualsToken => leftInteger <= rightInteger,
                SyntaxKind.GreaterToken => leftInteger > rightInteger,
                SyntaxKind.GreaterOrEqualsToken => leftInteger >= rightInteger,
                SyntaxKind.EqualsEqualsToken => leftInteger == rightInteger,
                SyntaxKind.BangEqualsToken => leftInteger != rightInteger,
                _ => throw StaticEvaluationException.Failure("Invalid integer operation during static evaluation."),
            };
        }
        if (left is double leftNumber && right is double rightNumber)
        {
            return operation switch
            {
                SyntaxKind.PlusToken => leftNumber + rightNumber,
                SyntaxKind.MinusToken => leftNumber - rightNumber,
                SyntaxKind.StarToken => leftNumber * rightNumber,
                SyntaxKind.SlashToken => leftNumber / rightNumber,
                SyntaxKind.PercentToken => leftNumber % rightNumber,
                SyntaxKind.LessToken => leftNumber < rightNumber,
                SyntaxKind.LessOrEqualsToken => leftNumber <= rightNumber,
                SyntaxKind.GreaterToken => leftNumber > rightNumber,
                SyntaxKind.GreaterOrEqualsToken => leftNumber >= rightNumber,
                SyntaxKind.EqualsEqualsToken => leftNumber == rightNumber,
                SyntaxKind.BangEqualsToken => leftNumber != rightNumber,
                _ => throw StaticEvaluationException.Failure("Invalid floating-point operation during static evaluation."),
            };
        }
        if (left is string leftText && right is string rightText)
        {
            return operation switch
            {
                SyntaxKind.PlusToken => leftText + rightText,
                SyntaxKind.EqualsEqualsToken => leftText == rightText,
                SyntaxKind.BangEqualsToken => leftText != rightText,
                _ => throw StaticEvaluationException.Failure("Invalid string operation during static evaluation."),
            };
        }
        if (left is bool leftBoolean && right is bool rightBoolean)
        {
            return operation switch
            {
                SyntaxKind.AmpersandAmpersandToken => leftBoolean && rightBoolean,
                SyntaxKind.PipePipeToken => leftBoolean || rightBoolean,
                SyntaxKind.EqualsEqualsToken => leftBoolean == rightBoolean,
                SyntaxKind.BangEqualsToken => leftBoolean != rightBoolean,
                _ => throw StaticEvaluationException.Failure("Invalid boolean operation during static evaluation."),
            };
        }
        throw StaticEvaluationException.Failure("Static binary operands have incompatible values.");
    }

    private StaticValue EvaluateConversion(BoundNumericConversionExpression conversion, StaticEnvironment environment)
    {
        object? operand = AsPrimitive(EvaluateExpression(conversion.Operand, environment)).Value;
        object value = conversion.Kind switch
        {
            BoundNumericConversionKind.StringFrom => Convert.ToString(operand, CultureInfo.InvariantCulture) ?? string.Empty,
            BoundNumericConversionKind.IntToFloat => Convert.ToDouble(operand, CultureInfo.InvariantCulture),
            BoundNumericConversionKind.IntFloor => checked((int)Math.Floor(Convert.ToDouble(operand, CultureInfo.InvariantCulture))),
            BoundNumericConversionKind.IntCeil => checked((int)Math.Ceiling(Convert.ToDouble(operand, CultureInfo.InvariantCulture))),
            BoundNumericConversionKind.IntRound => checked((int)Math.Round(Convert.ToDouble(operand, CultureInfo.InvariantCulture), MidpointRounding.ToEven)),
            BoundNumericConversionKind.IntTruncate => checked((int)Math.Truncate(Convert.ToDouble(operand, CultureInfo.InvariantCulture))),
            _ => throw StaticEvaluationException.Unsupported("Unknown numeric conversion in static evaluation."),
        };
        return Allocate(new StaticPrimitiveValue(value, conversion.Type));
    }

    private StaticValue EvaluateCall(BoundCallExpression call, StaticEnvironment callingEnvironment)
    {
        if (!_summaries.TryGetValue(call.Function, out FunctionEffectSummary? summary))
        {
            throw StaticEvaluationException.Ineligible(
                $"Function '{call.Function.Name}' has no static-safety summary.");
        }
        if (!summary.IsStaticSafe)
        {
            string provenance = string.Join(" -> ", summary.Provenance);
            throw StaticEvaluationException.Ineligible(
                $"Function '{call.Function.Name}' is not static-safe: {provenance}.");
        }
        if (!_functions.TryGetValue(call.Function, out BoundFunctionDeclaration? declaration))
        {
            throw StaticEvaluationException.Ineligible(
                $"Static-safe function '{call.Function.Name}' has no available ordinary body.");
        }

        StaticValue[] arguments = call.Arguments
            .Select(argument => EvaluateExpression(argument, callingEnvironment))
            .ToArray();
        string cacheKey = call.Function.StableIdentity + "(" + string.Join(",", arguments.Select(StableValueIdentity)) + ")";
        if (_completedCalls.TryGetValue(cacheKey, out StaticValue? cached))
        {
            return cached;
        }
        if (!_activeCalls.Add(cacheKey))
        {
            throw StaticEvaluationException.Budget(
                $"Recursive static call cycle detected at '{call.Function.Name}' for the same argument values.");
        }
        if (++_callDepth > _limits.MaximumCallDepth)
        {
            throw StaticEvaluationException.Budget(
                $"Static call depth exceeded the M1 limit of {_limits.MaximumCallDepth}.");
        }

        try
        {
            var environment = new StaticEnvironment();
            for (int index = 0; index < call.Function.Parameters.Count; index++)
            {
                environment.Declare(call.Function.Parameters[index], arguments[index]);
            }
            StaticValue result;
            try
            {
                ExecuteStatement(declaration.Body, environment);
                result = Allocate(new StaticPrimitiveValue(null, PrimitiveTypeSymbol.Void));
            }
            catch (ReturnSignal returned)
            {
                result = returned.Value;
            }
            catch (PropagationSignal propagated)
            {
                result = propagated.Result;
            }
            _completedCalls[cacheKey] = result;
            return result;
        }
        finally
        {
            _callDepth--;
            _activeCalls.Remove(cacheKey);
        }
    }

    private void ExecuteStatement(BoundStatement statement, StaticEnvironment environment)
    {
        Step();
        switch (statement)
        {
            case BoundBlockStatement block:
                foreach (BoundStatement child in block.Statements) ExecuteStatement(child, environment);
                return;
            case BoundVariableDeclaration variable:
                environment.Declare(variable.Variable, EvaluateExpression(variable.Initializer, environment));
                return;
            case BoundExpressionStatement expression:
                _ = EvaluateExpression(expression.Expression, environment);
                return;
            case BoundIfStatement conditional:
                if (AsBoolean(EvaluateExpression(conditional.Condition, environment)))
                {
                    ExecuteStatement(conditional.ThenStatement, environment);
                }
                else if (conditional.ElseStatement is not null)
                {
                    ExecuteStatement(conditional.ElseStatement, environment);
                }
                return;
            case BoundWhileStatement loop:
                while (AsBoolean(EvaluateExpression(loop.Condition, environment)))
                {
                    LoopIteration();
                    if (ExecuteLoopBody(loop.Body, environment)) break;
                }
                return;
            case BoundForStatement loop:
                if (loop.Initializer is not null) ExecuteStatement(loop.Initializer, environment);
                while (loop.Condition is null || AsBoolean(EvaluateExpression(loop.Condition, environment)))
                {
                    LoopIteration();
                    if (ExecuteLoopBody(loop.Body, environment)) break;
                    if (loop.Increment is not null) _ = EvaluateExpression(loop.Increment, environment);
                }
                return;
            case BoundForOfStatement loop:
                foreach (StaticValue item in AsSequence(EvaluateExpression(loop.Iterable, environment)))
                {
                    LoopIteration();
                    environment.SetOrDeclare(loop.Variable, item);
                    if (ExecuteLoopBody(loop.Body, environment)) break;
                }
                return;
            case BoundReturnStatement returned:
                throw new ReturnSignal(returned.Expression is null
                    ? Allocate(new StaticPrimitiveValue(null, PrimitiveTypeSymbol.Void))
                    : EvaluateExpression(returned.Expression, environment));
            case BoundBreakStatement:
                throw new BreakSignal();
            case BoundContinueStatement:
                throw new ContinueSignal();
            default:
                throw StaticEvaluationException.Unsupported(
                    $"Statement '{statement.GetType().Name}' is not supported by the M1 static evaluator.");
        }
    }

    private bool ExecuteLoopBody(BoundStatement body, StaticEnvironment environment)
    {
        try
        {
            ExecuteStatement(body, environment);
            return false;
        }
        catch (ContinueSignal)
        {
            return false;
        }
        catch (BreakSignal)
        {
            return true;
        }
    }

    private StaticValue EvaluateMatch(BoundMatchExpression match, StaticEnvironment environment)
    {
        StaticEnumValue input = AsEnum(EvaluateExpression(match.Scrutinee, environment));
        BoundMatchArm? arm = match.Arms.FirstOrDefault(candidate => ReferenceEquals(candidate.Case, input.Case));
        if (arm is null)
        {
            throw StaticEvaluationException.Failure(
                $"Static match has no arm for enum case '{input.Case.Name}'.");
        }
        var armEnvironment = environment.Fork();
        for (int index = 0; index < arm.PayloadVariables.Count; index++)
        {
            armEnvironment.Declare(arm.PayloadVariables[index], input.Payloads[index]);
        }
        return EvaluateExpression(arm.Expression, armEnvironment);
    }

    private StaticValue EvaluateResultMatch(BoundResultMatchExpression match, StaticEnvironment environment)
    {
        StaticResultValue input = AsResult(EvaluateExpression(match.Scrutinee, environment));
        var armEnvironment = environment.Fork();
        armEnvironment.Declare(input.IsOk ? match.OkVariable : match.ErrVariable, input.Payload);
        return EvaluateExpression(input.IsOk ? match.OkExpression : match.ErrExpression, armEnvironment);
    }

    private StaticValue EvaluateUnwrap(BoundUnwrapExpression unwrap, StaticEnvironment environment)
    {
        StaticResultValue result = AsResult(EvaluateExpression(unwrap.Operand, environment));
        if (!result.IsOk)
        {
            throw StaticEvaluationException.Failure("A Result error was unwrapped during static evaluation.");
        }
        return result.Payload;
    }

    private StaticValue EvaluatePropagation(BoundPropagateExpression propagate, StaticEnvironment environment)
    {
        StaticResultValue result = AsResult(EvaluateExpression(propagate.Operand, environment));
        if (!result.IsOk)
        {
            throw new PropagationSignal(result);
        }
        return result.Payload;
    }

    private StaticValue EvaluateTryExcept(BoundTryExceptExpression attempt, StaticEnvironment environment)
    {
        try
        {
            return EvaluateValueBlock(attempt.Protected, environment.Fork());
        }
        catch (PropagationSignal propagated)
        {
            var handlerEnvironment = environment.Fork();
            handlerEnvironment.Declare(attempt.HandlerBinding, propagated.Result.Payload);
            return EvaluateValueBlock(attempt.Handler, handlerEnvironment);
        }
    }

    private StaticValue EvaluateValueBlock(BoundValueBlock block, StaticEnvironment environment)
    {
        foreach (BoundStatement statement in block.PrefixStatements) ExecuteStatement(statement, environment);
        return EvaluateExpression(block.ValueExpression, environment);
    }

    private StaticValue EvaluateArray(BoundArrayExpression array, StaticEnvironment environment)
    {
        if (array.Elements.Count > _limits.MaximumArrayLength)
        {
            throw StaticEvaluationException.Budget(
                $"Static array length exceeds the M1 limit of {_limits.MaximumArrayLength}.");
        }
        return Allocate(new StaticArrayValue(
            array.Elements.Select(element => EvaluateExpression(element, environment)).ToArray(),
            (ArrayTypeSymbol)array.Type));
    }

    private StaticValue EvaluateLength(BoundArrayLengthExpression length, StaticEnvironment environment)
    {
        StaticValue receiver = EvaluateExpression(length.Receiver, environment);
        int result = receiver switch
        {
            StaticArrayValue array => array.Elements.Count,
            StaticPrimitiveValue { Value: string text } => text.Length,
            _ => throw StaticEvaluationException.Failure("length requires a string or immutable array."),
        };
        return Allocate(new StaticPrimitiveValue(result, PrimitiveTypeSymbol.Int));
    }

    private StaticValue EvaluateArrayAccess(BoundArrayElementAccessExpression access, StaticEnvironment environment)
    {
        StaticArrayValue array = AsArray(EvaluateExpression(access.Receiver, environment));
        int index = AsInt(EvaluateExpression(access.Index, environment));
        if ((uint)index >= (uint)array.Elements.Count)
        {
            throw StaticEvaluationException.Failure("Immutable array index was out of bounds during static evaluation.");
        }
        return array.Elements[index];
    }

    private StaticValue EvaluateMutableArrayConstruction(
        BoundMutableArrayConstructionExpression construction,
        StaticEnvironment environment)
    {
        int length = AsInt(EvaluateExpression(construction.Length, environment));
        if (length < 0)
        {
            throw StaticEvaluationException.Failure("MutableArray length was negative during static evaluation.");
        }
        if (length > _limits.MaximumArrayLength)
        {
            throw StaticEvaluationException.Budget(
                $"MutableArray length exceeds the M1 limit of {_limits.MaximumArrayLength}.");
        }
        var mutableArrayType = (MutableArrayTypeSymbol)construction.Type;
        StaticValue defaultValue = DefaultValue(mutableArrayType.ElementType);
        StaticValue[] elements = Enumerable.Repeat(defaultValue, length).ToArray();
        AllocateValues(length);
        return Allocate(new StaticMutableArrayValue(elements, mutableArrayType));
    }

    private StaticValue EvaluateMutableArrayAccess(
        BoundMutableArrayElementAccessExpression access,
        StaticEnvironment environment)
    {
        StaticMutableArrayValue array = AsMutableArray(EvaluateExpression(access.Receiver, environment));
        int index = AsInt(EvaluateExpression(access.Index, environment));
        CheckMutableIndex(array, index);
        return array.Elements[index];
    }

    private StaticValue EvaluateMutableArrayAssignment(
        BoundMutableArrayElementAssignmentExpression assignment,
        StaticEnvironment environment)
    {
        StaticMutableArrayValue array = AsMutableArray(EvaluateExpression(assignment.Receiver, environment));
        int index = AsInt(EvaluateExpression(assignment.Index, environment));
        CheckMutableIndex(array, index);
        StaticValue value = EvaluateExpression(assignment.Value, environment);
        array.Elements[index] = value;
        return value;
    }

    private StaticValue EvaluateFreeze(BoundMutableArrayFreezeExpression freeze, StaticEnvironment environment)
    {
        StaticMutableArrayValue array = AsMutableArray(EvaluateExpression(freeze.Receiver, environment));
        StaticValue[] snapshot = array.Elements.ToArray();
        AllocateValues(snapshot.Length);
        return Allocate(new StaticArrayValue(snapshot, (ArrayTypeSymbol)freeze.Type));
    }

    private StaticValue EvaluateRecord(BoundRecordConstructionExpression record, StaticEnvironment environment)
    {
        var fields = new Dictionary<RecordFieldSymbol, StaticValue>();
        foreach (BoundRecordFieldInitializer initializer in record.Initializers)
        {
            fields[initializer.Field] = EvaluateExpression(initializer.Value, environment);
        }
        return Allocate(new StaticRecordValue(fields, record.RecordType));
    }

    private StaticValue EvaluateRecordUpdate(BoundRecordWithExpression update, StaticEnvironment environment)
    {
        StaticRecordValue source = AsRecord(EvaluateExpression(update.Source, environment));
        var fields = new Dictionary<RecordFieldSymbol, StaticValue>(source.Fields);
        foreach (BoundRecordFieldInitializer replacement in update.Replacements)
        {
            fields[replacement.Field] = EvaluateExpression(replacement.Value, environment);
        }
        return Allocate(new StaticRecordValue(fields, update.RecordType));
    }

    private StaticValue DefaultValue(TypeSymbol type)
    {
        if (TypeFacts.IsInt(type)) return Allocate(new StaticPrimitiveValue(0, type));
        if (TypeFacts.IsFloat(type)) return Allocate(new StaticPrimitiveValue(0d, type));
        if (type == PrimitiveTypeSymbol.Boolean) return Allocate(new StaticPrimitiveValue(false, type));
        throw StaticEvaluationException.Unsupported(
            $"MutableArray<{type.Name}> has no compiler-owned static default value.");
    }

    private void CheckMutableIndex(StaticMutableArrayValue array, int index)
    {
        if ((uint)index >= (uint)array.Elements.Length)
        {
            throw StaticEvaluationException.Failure("MutableArray index was out of bounds during static evaluation.");
        }
    }

    private IReadOnlyList<StaticValue> AsSequence(StaticValue value)
    {
        return value switch
        {
            StaticArrayValue array => array.Elements,
            StaticMutableArrayValue array => array.Elements,
            _ => throw StaticEvaluationException.Failure("Static for-of requires an array value."),
        };
    }

    private static StaticPrimitiveValue AsPrimitive(StaticValue value)
        => value as StaticPrimitiveValue
            ?? throw StaticEvaluationException.Failure("Expected a primitive static value.");

    private static StaticArrayValue AsArray(StaticValue value)
        => value as StaticArrayValue
            ?? throw StaticEvaluationException.Failure("Expected an immutable static array.");

    private static StaticMutableArrayValue AsMutableArray(StaticValue value)
        => value as StaticMutableArrayValue
            ?? throw StaticEvaluationException.Failure("Expected a static MutableArray value.");

    private static StaticRecordValue AsRecord(StaticValue value)
        => value as StaticRecordValue
            ?? throw StaticEvaluationException.Failure("Expected a static record value.");

    private static StaticEnumValue AsEnum(StaticValue value)
        => value as StaticEnumValue
            ?? throw StaticEvaluationException.Failure("Expected a static enum value.");

    private static StaticResultValue AsResult(StaticValue value)
        => value as StaticResultValue
            ?? throw StaticEvaluationException.Failure("Expected a static Result value.");

    private static bool AsBoolean(StaticValue value)
        => AsPrimitive(value).Value as bool?
            ?? throw StaticEvaluationException.Failure("Expected a boolean during static evaluation.");

    private static int AsInt(StaticValue value)
        => AsPrimitive(value).Value as int?
            ?? throw StaticEvaluationException.Failure("Expected an int during static evaluation.");

    private T Allocate<T>(T value) where T : StaticValue
    {
        AllocateValues(1);
        return value;
    }

    private void AllocateValues(int count)
    {
        _allocatedValues += count;
        if (_allocatedValues > _limits.MaximumAllocatedValues)
        {
            throw StaticEvaluationException.Budget(
                $"Static temporary allocation exceeded the M1 value limit of {_limits.MaximumAllocatedValues}.");
        }
    }

    private void Step()
    {
        if (++_steps > _limits.MaximumSteps)
        {
            throw StaticEvaluationException.Budget(
                $"Static execution exceeded the M1 step limit of {_limits.MaximumSteps}.");
        }
    }

    private void LoopIteration()
    {
        if (++_loopIterations > _limits.MaximumLoopIterations)
        {
            throw StaticEvaluationException.Budget(
                $"Static execution exceeded the M1 loop-iteration limit of {_limits.MaximumLoopIterations}.");
        }
    }

    private static int CountEmbeddedValues(StaticValue value)
    {
        return value switch
        {
            StaticPrimitiveValue => 1,
            StaticArrayValue array => 1 + array.Elements.Sum(CountEmbeddedValues),
            StaticMutableArrayValue array => 1 + array.Elements.Sum(CountEmbeddedValues),
            StaticRecordValue record => 1 + record.Fields.Values.Sum(CountEmbeddedValues),
            StaticEnumValue enumValue => 1 + enumValue.Payloads.Sum(CountEmbeddedValues),
            StaticResultValue result => 1 + CountEmbeddedValues(result.Payload),
            _ => 1,
        };
    }

    private static string StableValueIdentity(StaticValue value)
    {
        return value switch
        {
            StaticPrimitiveValue primitive => primitive.Type.Name + ":" + Convert.ToString(primitive.Value, CultureInfo.InvariantCulture),
            StaticArrayValue array => array.Type.Name + "[" + string.Join(",", array.Elements.Select(StableValueIdentity)) + "]",
            StaticRecordValue record => record.Type.Name + "{" + string.Join(",", record.RecordType.Fields.OrderBy(field => field.Id.Ordinal).Select(field => field.Name + "=" + StableValueIdentity(record.Fields[field]))) + "}",
            StaticEnumValue enumValue => enumValue.Type.Name + "." + enumValue.Case.Name + "(" + string.Join(",", enumValue.Payloads.Select(StableValueIdentity)) + ")",
            StaticResultValue result => result.Type.Name + (result.IsOk ? ":ok:" : ":err:") + StableValueIdentity(result.Payload),
            StaticMutableArrayValue => throw StaticEvaluationException.Unsupported("MutableArray arguments cannot participate in static call memoization."),
            _ => value.Type.Name,
        };
    }

    private sealed class StaticEnvironment
    {
        private readonly Dictionary<string, StaticValue> _values;

        public StaticEnvironment()
        {
            _values = new Dictionary<string, StaticValue>(StringComparer.Ordinal);
        }

        public StaticEnvironment(IReadOnlyDictionary<VariableSymbol, StaticValue> values)
        {
            _values = values.ToDictionary(pair => pair.Key.Name, pair => pair.Value, StringComparer.Ordinal);
        }

        private StaticEnvironment(Dictionary<string, StaticValue> values)
        {
            _values = values;
        }

        public StaticEnvironment Fork()
            => new(new Dictionary<string, StaticValue>(_values, StringComparer.Ordinal));

        public void Declare(Symbol symbol, StaticValue value)
            => _values[symbol.Name] = value;

        public void Set(Symbol symbol, StaticValue value)
        {
            if (!_values.ContainsKey(symbol.Name))
            {
                throw StaticEvaluationException.Ineligible(
                    $"Value '{symbol.Name}' is not available at compile time.");
            }
            _values[symbol.Name] = value;
        }

        public void SetOrDeclare(Symbol symbol, StaticValue value)
            => _values[symbol.Name] = value;

        public StaticValue Get(Symbol symbol)
        {
            if (_values.TryGetValue(symbol.Name, out StaticValue? value)) return value;
            throw StaticEvaluationException.Ineligible(
                $"Value '{symbol.Name}' is not available at compile time.");
        }
    }

    private sealed class ReturnSignal(StaticValue value) : Exception
    {
        public StaticValue Value { get; } = value;
    }

    private sealed class PropagationSignal(StaticResultValue result) : Exception
    {
        public StaticResultValue Result { get; } = result;
    }

    private sealed class BreakSignal : Exception;
    private sealed class ContinueSignal : Exception;
}

internal sealed class StaticEvaluationException(string diagnosticId, string message) : Exception(message)
{
    public string DiagnosticId { get; } = diagnosticId;

    public static StaticEvaluationException Ineligible(string message)
        => new("COPE-STATIC-0012", "Static expression is not eligible: " + message);

    public static StaticEvaluationException Unsupported(string message)
        => new("COPE-STATIC-0013", "Static evaluator does not support this operation: " + message);

    public static StaticEvaluationException Failure(string message)
        => new("COPE-STATIC-0014", "Static evaluation failed: " + message);

    public static StaticEvaluationException Budget(string message)
        => new("COPE-STATIC-0015", "Static evaluation budget exceeded: " + message);
}
