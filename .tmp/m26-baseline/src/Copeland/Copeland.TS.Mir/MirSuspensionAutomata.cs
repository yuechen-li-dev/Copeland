namespace Copeland.TS.Mir;

/// <summary>
/// Compiler-local lower representation for an async function after structured
/// MIR has been split at real suspension boundaries. It intentionally models a
/// generated continuation, not a user-authored state-machine language.
/// </summary>
public sealed class MirSuspensionAutomaton(
    string identity,
    string ownerFunctionName,
    MirAutomatonStateId entryStateId,
    IReadOnlyList<MirFrameSlot> frameSlots,
    IReadOnlyList<MirAutomatonState> states,
    IReadOnlyList<MirAutomatonTransition> transitions,
    MirAsyncExecutionPlan? executionPlan = null)
{
    public string Identity { get; } = identity;
    public string OwnerFunctionName { get; } = ownerFunctionName;
    public MirAutomatonStateId EntryStateId { get; } = entryStateId;
    public IReadOnlyList<MirFrameSlot> FrameSlots { get; } = Array.AsReadOnly(frameSlots.ToArray());
    public IReadOnlyList<MirAutomatonState> States { get; } = Array.AsReadOnly(states.ToArray());
    public IReadOnlyList<MirAutomatonTransition> Transitions { get; } = Array.AsReadOnly(transitions.ToArray());
    public MirAsyncExecutionPlan? ExecutionPlan { get; } = executionPlan;
}

/// <summary>
/// Backend-neutral executable control plan for an async function. Structured
/// MIR is split here, before either target realizes the state discriminator.
/// </summary>
public sealed class MirAsyncExecutionPlan(
    MirAsyncExecutionStateId entryStateId,
    IReadOnlyList<MirAsyncExecutionState> states)
{
    public MirAsyncExecutionStateId EntryStateId { get; } = entryStateId;
    public IReadOnlyList<MirAsyncExecutionState> States { get; } = Array.AsReadOnly(states.ToArray());
}

public readonly record struct MirAsyncExecutionStateId(string Value)
{
    public override string ToString() => Value;
}

public abstract class MirAsyncExecutionState(MirAsyncExecutionStateId id)
{
    public MirAsyncExecutionStateId Id { get; } = id;
}

public sealed class MirAsyncStatementExecutionState(
    MirAsyncExecutionStateId id,
    MirStatement statement,
    MirAsyncExecutionStateId nextStateId) : MirAsyncExecutionState(id)
{
    public MirStatement Statement { get; } = statement;
    public MirAsyncExecutionStateId NextStateId { get; } = nextStateId;
}

public sealed class MirAsyncReturnExecutionState(
    MirAsyncExecutionStateId id,
    MirReturnStatement statement) : MirAsyncExecutionState(id)
{
    public MirReturnStatement Statement { get; } = statement;
}

public sealed class MirAsyncBranchExecutionState(
    MirAsyncExecutionStateId id,
    MirExpression condition,
    MirAsyncExecutionStateId thenStateId,
    MirAsyncExecutionStateId elseStateId) : MirAsyncExecutionState(id)
{
    public MirExpression Condition { get; } = condition;
    public MirAsyncExecutionStateId ThenStateId { get; } = thenStateId;
    public MirAsyncExecutionStateId ElseStateId { get; } = elseStateId;
}

public sealed class MirAsyncJumpExecutionState(
    MirAsyncExecutionStateId id,
    MirAsyncExecutionStateId targetStateId) : MirAsyncExecutionState(id)
{
    public MirAsyncExecutionStateId TargetStateId { get; } = targetStateId;
}

/// <summary>
/// Reads a compiler-generated frame slot after a preceding async execution
/// state has stored its value. This node exists only inside an executable
/// async plan; authored structured MIR never contains it.
/// </summary>
public sealed record MirAsyncFrameSlotExpression(MirFrameSlotId SlotId, MirType Type) : MirExpression(Type);

public sealed class MirAsyncAwaitExecutionState(
    MirAsyncExecutionStateId id,
    MirExpression awaitedComputation,
    MirFrameSlotId awaitedComputationSlot,
    MirFrameSlotId resumedValueSlot,
    MirAsyncExecutionStateId nextStateId) : MirAsyncExecutionState(id)
{
    public MirExpression AwaitedComputation { get; } = awaitedComputation;
    public MirFrameSlotId AwaitedComputationSlot { get; } = awaitedComputationSlot;
    public MirFrameSlotId ResumedValueSlot { get; } = resumedValueSlot;
    public MirAsyncExecutionStateId NextStateId { get; } = nextStateId;
}

public sealed class MirAsyncEvaluateExpressionState(
    MirAsyncExecutionStateId id,
    MirFrameSlotId targetSlot,
    MirExpression expression,
    MirAsyncExecutionStateId nextStateId) : MirAsyncExecutionState(id)
{
    public MirFrameSlotId TargetSlot { get; } = targetSlot;
    public MirExpression Expression { get; } = expression;
    public MirAsyncExecutionStateId NextStateId { get; } = nextStateId;
}

public sealed class MirAsyncPropagateExecutionState(
    MirAsyncExecutionStateId id,
    MirExpression resultExpression,
    MirPropagationTarget target,
    MirFrameSlotId successValueSlot,
    MirAsyncExecutionStateId nextStateId,
    MirAsyncExecutionStateId? handlerStateId = null,
    MirFrameSlotId? handlerErrorSlot = null) : MirAsyncExecutionState(id)
{
    public MirExpression ResultExpression { get; } = resultExpression;
    public MirPropagationTarget Target { get; } = target;
    public MirFrameSlotId SuccessValueSlot { get; } = successValueSlot;
    public MirAsyncExecutionStateId NextStateId { get; } = nextStateId;
    /// <summary>
    /// The executable handler entry for lexical propagation. Function-return
    /// propagation deliberately has no handler transfer.
    /// </summary>
    public MirAsyncExecutionStateId? HandlerStateId { get; } = handlerStateId;
    public MirFrameSlotId? HandlerErrorSlot { get; } = handlerErrorSlot;
}

public static class MirSuspensionAutomatonLimits
{
    public const int MaximumStates = 256;
    public const int MaximumTransitions = 512;
    public const int MaximumSuspensionPoints = 128;
    public const int MaximumFrameSlots = 256;
    public const int MaximumStructuredNesting = 32;
    public const int MaximumWorklistSteps = 8_192;
}

public readonly record struct MirAutomatonStateId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct MirFrameSlotId(string Value)
{
    public override string ToString() => Value;
}

public sealed class MirFrameSlot(MirFrameSlotId id, MirType type, string provenance, bool isReadOnly = false)
{
    public MirFrameSlotId Id { get; } = id;
    public MirType Type { get; } = type;
    public string Provenance { get; } = provenance;
    public bool IsReadOnly { get; } = isReadOnly;
}

public enum MirAutomatonStateKind
{
    Entry,
    Execution,
    Suspension,
    Completion,
    Cancelled,
    InvariantFailure,
}

public abstract class MirAutomatonState(MirAutomatonStateId id, MirAutomatonStateKind kind, string provenance)
{
    public MirAutomatonStateId Id { get; } = id;
    public MirAutomatonStateKind Kind { get; } = kind;
    public string Provenance { get; } = provenance;
}

public sealed class MirExecutionAutomatonState(
    MirAutomatonStateId id,
    string provenance,
    IReadOnlyList<MirFrameSlotId> reads,
    IReadOnlyList<MirFrameSlotId> writes)
    : MirAutomatonState(id, MirAutomatonStateKind.Execution, provenance)
{
    public IReadOnlyList<MirFrameSlotId> Reads { get; } = Array.AsReadOnly(reads.ToArray());
    public IReadOnlyList<MirFrameSlotId> Writes { get; } = Array.AsReadOnly(writes.ToArray());
}

public sealed class MirAwaitSuspensionAutomatonState(
    MirAutomatonStateId id,
    string provenance,
    MirFrameSlotId awaitedComputationSlot,
    MirType resumeValueType)
    : MirAutomatonState(id, MirAutomatonStateKind.Suspension, provenance)
{
    public MirFrameSlotId AwaitedComputationSlot { get; } = awaitedComputationSlot;
    public MirType ResumeValueType { get; } = resumeValueType;
}

public sealed class MirCompletionAutomatonState(MirAutomatonStateId id, string provenance, MirType completedValueType)
    : MirAutomatonState(id, MirAutomatonStateKind.Completion, provenance)
{
    public MirType CompletedValueType { get; } = completedValueType;
}

public sealed class MirTerminalAutomatonState(MirAutomatonStateId id, MirAutomatonStateKind kind, string provenance)
    : MirAutomatonState(id, kind, provenance)
{
}

public enum MirAutomatonTransitionKind
{
    Unconditional,
    ResumeSuccess,
    ResultError,
    Cancellation,
}

public sealed class MirAutomatonTransition(
    MirAutomatonStateId source,
    MirAutomatonStateId target,
    MirAutomatonTransitionKind kind,
    string provenance)
{
    public MirAutomatonStateId Source { get; } = source;
    public MirAutomatonStateId Target { get; } = target;
    public MirAutomatonTransitionKind Kind { get; } = kind;
    public string Provenance { get; } = provenance;
}

/// <summary>
/// Shared pre-emission verification. Backends must consume only a validated
/// automaton; malformed generated control flow is reported as malformed MIR,
/// never as a C# or JavaScript emitter failure.
/// </summary>
public static class MirSuspensionAutomatonValidator
{
    private const string Prefix = "Malformed MIR suspension automaton";

    public static void Validate(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (MirFunction function in program.Functions)
        {
            if (function.IsAsync && function.SuspensionAutomaton is null)
            {
                Add(diagnostics, $"async function '{function.Name}' has no automaton.");
                continue;
            }

            if (!function.IsAsync && function.SuspensionAutomaton is not null)
            {
                Add(diagnostics, $"synchronous function '{function.Name}' references async-only state.");
                continue;
            }

            if (function.SuspensionAutomaton is not null)
            {
                ValidateAutomaton(function, function.SuspensionAutomaton, diagnostics);
            }
        }
    }

    private static void ValidateAutomaton(
        MirFunction function,
        MirSuspensionAutomaton automaton,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(automaton.Identity))
        {
            Add(diagnostics, $"function '{function.Name}' has a blank automaton identity.");
        }

        if (!string.Equals(function.Name, automaton.OwnerFunctionName, StringComparison.Ordinal))
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' is not owned by async function '{function.Name}'.");
        }

        CheckLimit(automaton.States.Count, MirSuspensionAutomatonLimits.MaximumStates, "states", automaton, diagnostics);
        CheckLimit(automaton.Transitions.Count, MirSuspensionAutomatonLimits.MaximumTransitions, "transitions", automaton, diagnostics);
        CheckLimit(automaton.FrameSlots.Count, MirSuspensionAutomatonLimits.MaximumFrameSlots, "frame slots", automaton, diagnostics);

        var slots = new Dictionary<MirFrameSlotId, MirFrameSlot>();
        foreach (MirFrameSlot slot in automaton.FrameSlots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id.Value) || !slots.TryAdd(slot.Id, slot))
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' has a blank or duplicate frame slot identity '{slot.Id}'.");
            }
            if (slot.Type is null || string.IsNullOrWhiteSpace(slot.Provenance))
            {
                Add(diagnostics, $"frame slot '{slot.Id}' has an invalid type or provenance.");
            }
        }

        var states = new Dictionary<MirAutomatonStateId, MirAutomatonState>();
        foreach (MirAutomatonState state in automaton.States)
        {
            if (string.IsNullOrWhiteSpace(state.Id.Value) || !states.TryAdd(state.Id, state))
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' has a blank or duplicate state identity '{state.Id}'.");
            }
            if (string.IsNullOrWhiteSpace(state.Provenance))
            {
                Add(diagnostics, $"state '{state.Id}' has no provenance.");
            }
        }

        if (!states.TryGetValue(automaton.EntryStateId, out MirAutomatonState? entry))
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' has a missing entry state '{automaton.EntryStateId}'.");
            return;
        }
        if (entry.Kind != MirAutomatonStateKind.Entry)
        {
            Add(diagnostics, $"entry state '{entry.Id}' does not have Entry kind.");
        }
        if (states.Values.Count(state => state.Kind == MirAutomatonStateKind.Entry) != 1)
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' must have exactly one Entry state.");
        }

        int suspensionCount = 0;
        foreach (MirAutomatonState state in states.Values)
        {
            switch (state)
            {
                case MirExecutionAutomatonState execution:
                    ValidateSlots(execution.Reads, slots, state, "read", diagnostics);
                    ValidateSlots(execution.Writes, slots, state, "write", diagnostics);
                    break;
                case MirAwaitSuspensionAutomatonState suspension:
                    suspensionCount++;
                    if (!slots.TryGetValue(suspension.AwaitedComputationSlot, out MirFrameSlot? awaitedSlot))
                    {
                        Add(diagnostics, $"suspension state '{state.Id}' has no awaited computation slot.");
                    }
                    else if (awaitedSlot.Type is not MirAsyncType asyncType)
                    {
                        Add(diagnostics, $"suspension state '{state.Id}' awaits a non-Async frame slot.");
                    }
                    else if (!MirTypeFacts.AreEquivalent(asyncType.EventualType, suspension.ResumeValueType))
                    {
                        Add(diagnostics, $"suspension state '{state.Id}' resume type does not match its awaited Async value.");
                    }
                    break;
                case MirCompletionAutomatonState completion:
                    if (!MirTypeFacts.AreEquivalent(completion.CompletedValueType, function.ReturnType))
                    {
                        Add(diagnostics, $"completion state '{state.Id}' does not match function '{function.Name}' eventual return type.");
                    }
                    break;
                case MirTerminalAutomatonState terminal when terminal.Kind is not MirAutomatonStateKind.Entry and not MirAutomatonStateKind.Cancelled and not MirAutomatonStateKind.InvariantFailure:
                    Add(diagnostics, $"state '{state.Id}' has illegal terminal kind '{terminal.Kind}'.");
                    break;
            }
        }
        CheckLimit(suspensionCount, MirSuspensionAutomatonLimits.MaximumSuspensionPoints, "suspension points", automaton, diagnostics);

        var outgoing = states.Keys.ToDictionary(id => id, _ => new List<MirAutomatonTransition>());
        foreach (MirAutomatonTransition transition in automaton.Transitions)
        {
            bool knownSource = outgoing.TryGetValue(transition.Source, out List<MirAutomatonTransition>? sourceTransitions);
            if (!knownSource || !states.ContainsKey(transition.Target))
            {
                Add(diagnostics, $"transition '{transition.Source}' -> '{transition.Target}' has an unknown state target or source.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(transition.Provenance))
            {
                Add(diagnostics, $"transition '{transition.Source}' -> '{transition.Target}' has no provenance.");
            }
            if (transition.Kind == MirAutomatonTransitionKind.Cancellation && states[transition.Target].Kind != MirAutomatonStateKind.Cancelled)
            {
                Add(diagnostics, $"cancellation transition from '{transition.Source}' does not target a Cancelled state.");
            }
            if (transition.Kind == MirAutomatonTransitionKind.ResumeSuccess && states[transition.Source] is not MirAwaitSuspensionAutomatonState)
            {
                Add(diagnostics, $"resume-success transition from '{transition.Source}' does not leave a suspension state.");
            }
            sourceTransitions!.Add(transition);
        }

        foreach (MirAutomatonState state in states.Values)
        {
            bool terminal = state.Kind is MirAutomatonStateKind.Completion or MirAutomatonStateKind.Cancelled or MirAutomatonStateKind.InvariantFailure;
            if (!terminal && outgoing[state.Id].Count == 0)
            {
                Add(diagnostics, $"nonterminal state '{state.Id}' has no transition, suspension, or completion.");
            }
            if (state is MirAwaitSuspensionAutomatonState && !outgoing[state.Id].Any(edge => edge.Kind == MirAutomatonTransitionKind.ResumeSuccess))
            {
                Add(diagnostics, $"suspension state '{state.Id}' has no resume-success transition.");
            }
        }

        ValidateReachability(automaton, states, outgoing, diagnostics);
        if (automaton.ExecutionPlan is not null)
        {
            ValidateExecutionPlan(automaton, automaton.ExecutionPlan, diagnostics);
        }
    }

    private static void ValidateExecutionPlan(MirSuspensionAutomaton automaton, MirAsyncExecutionPlan plan, List<MirValidationDiagnostic> diagnostics)
    {
        CheckLimit(plan.States.Count, MirSuspensionAutomatonLimits.MaximumStates, "executable states", automaton, diagnostics);
        var states = new Dictionary<MirAsyncExecutionStateId, MirAsyncExecutionState>();
        foreach (MirAsyncExecutionState state in plan.States)
        {
            if (string.IsNullOrWhiteSpace(state.Id.Value) || !states.TryAdd(state.Id, state))
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' has a blank or duplicate executable state identity '{state.Id}'.");
            }
        }

        if (!states.ContainsKey(plan.EntryStateId))
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' has a missing executable entry state '{plan.EntryStateId}'.");
        }

        foreach (MirAsyncExecutionState state in states.Values)
        {
            switch (state)
            {
                case MirAsyncStatementExecutionState statement:
                    if (statement.Statement is not MirVariableDeclarationStatement and not MirExpressionStatement)
                    {
                        Add(diagnostics, $"executable state '{state.Id}' contains an unsplit statement '{statement.Statement.GetType().Name}'.");
                    }
                    CheckExecutionTarget(automaton, states, statement.NextStateId, state.Id, diagnostics);
                    ValidatePlanStatementFrameSlots(automaton, statement.Statement, state.Id, diagnostics);
                    break;
                case MirAsyncBranchExecutionState branch:
                    CheckExecutionTarget(automaton, states, branch.ThenStateId, state.Id, diagnostics);
                    CheckExecutionTarget(automaton, states, branch.ElseStateId, state.Id, diagnostics);
                    ValidatePlanExpressionFrameSlots(automaton, branch.Condition, state.Id, diagnostics);
                    break;
                case MirAsyncReturnExecutionState returned:
                    ValidatePlanStatementFrameSlots(automaton, returned.Statement, state.Id, diagnostics);
                    break;
                case MirAsyncAwaitExecutionState awaitState:
                    CheckExecutionTarget(automaton, states, awaitState.NextStateId, state.Id, diagnostics);
                    ValidatePlanExpressionFrameSlots(automaton, awaitState.AwaitedComputation, state.Id, diagnostics);
                    MirFrameSlot? awaitedComputationSlotDef = automaton.FrameSlots.FirstOrDefault(slot => slot.Id == awaitState.AwaitedComputationSlot);
                    if (awaitedComputationSlotDef?.Type is not MirAsyncType asyncType)
                    {
                        Add(diagnostics, $"await state '{state.Id}' references unknown or non-Async computation frame slot '{awaitState.AwaitedComputationSlot}'.");
                    }
                    else
                    {
                        MirFrameSlot? resumedSlot = automaton.FrameSlots.FirstOrDefault(slot => slot.Id == awaitState.ResumedValueSlot);
                        if (resumedSlot is null || !MirTypeFacts.AreEquivalent(asyncType.EventualType, resumedSlot.Type))
                        {
                            Add(diagnostics, $"await state '{state.Id}' has a resume-value frame slot incompatible with '{asyncType.EventualType.Name}'.");
                        }
                    }
                    break;
                case MirAsyncEvaluateExpressionState evaluation:
                    CheckExecutionTarget(automaton, states, evaluation.NextStateId, state.Id, diagnostics);
                    ValidatePlanExpressionFrameSlots(automaton, evaluation.Expression, state.Id, diagnostics);
                    if (!automaton.FrameSlots.Any(slot => slot.Id == evaluation.TargetSlot && MirTypeFacts.AreEquivalent(slot.Type, evaluation.Expression.Type)))
                    {
                        Add(diagnostics, $"expression state '{state.Id}' references unknown or incompatible target frame slot '{evaluation.TargetSlot}'.");
                    }
                    break;
                case MirAsyncPropagateExecutionState propagation:
                    CheckExecutionTarget(automaton, states, propagation.NextStateId, state.Id, diagnostics);
                    ValidatePlanExpressionFrameSlots(automaton, propagation.ResultExpression, state.Id, diagnostics);
                    MirResultType? resultType = propagation.ResultExpression.Type as MirResultType;
                    if (resultType is null)
                    {
                        Add(diagnostics, $"propagation state '{state.Id}' does not evaluate a Result expression.");
                    }
                    else if (!automaton.FrameSlots.Any(slot => slot.Id == propagation.SuccessValueSlot && MirTypeFacts.AreEquivalent(slot.Type, resultType.SuccessType)))
                    {
                        Add(diagnostics, $"propagation state '{state.Id}' references unknown or incompatible success-value frame slot '{propagation.SuccessValueSlot}'.");
                    }
                    if (propagation.Target is MirPropagationTarget.FunctionReturn)
                    {
                        if (propagation.HandlerStateId is not null || propagation.HandlerErrorSlot is not null)
                        {
                            Add(diagnostics, $"function-return propagation state '{state.Id}' must not carry a lexical handler transfer.");
                        }
                    }
                    else if (propagation.Target is MirPropagationTarget.LexicalExcept)
                    {
                        if (propagation.HandlerStateId is not { } handlerStateId
                            || propagation.HandlerErrorSlot is not { } handlerErrorSlot)
                        {
                            Add(diagnostics, $"lexical propagation state '{state.Id}' is missing its handler transfer.");
                        }
                        else
                        {
                            CheckExecutionTarget(automaton, states, handlerStateId, state.Id, diagnostics);
                            if (resultType is { } result
                                && !automaton.FrameSlots.Any(slot => slot.Id == handlerErrorSlot && MirTypeFacts.AreEquivalent(slot.Type, result.ErrorType)))
                            {
                                Add(diagnostics, $"lexical propagation state '{state.Id}' references unknown or incompatible handler-error frame slot '{handlerErrorSlot}'.");
                            }
                        }
                    }
                    break;
                case MirAsyncJumpExecutionState jump:
                    CheckExecutionTarget(automaton, states, jump.TargetStateId, state.Id, diagnostics);
                    break;
            }
        }
    }

    private static void CheckExecutionTarget(MirSuspensionAutomaton automaton, IReadOnlyDictionary<MirAsyncExecutionStateId, MirAsyncExecutionState> states, MirAsyncExecutionStateId target, MirAsyncExecutionStateId source, List<MirValidationDiagnostic> diagnostics)
    {
        if (!states.ContainsKey(target))
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' executable state '{source}' targets unknown state '{target}'.");
        }
    }

    private static void ValidatePlanStatementFrameSlots(
        MirSuspensionAutomaton automaton,
        MirStatement statement,
        MirAsyncExecutionStateId stateId,
        List<MirValidationDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                ValidatePlanExpressionFrameSlots(automaton, declaration.Initializer, stateId, diagnostics);
                break;
            case MirExpressionStatement expression:
                ValidatePlanExpressionFrameSlots(automaton, expression.Expression, stateId, diagnostics);
                break;
            case MirReturnStatement { Expression: not null } returned:
                ValidatePlanExpressionFrameSlots(automaton, returned.Expression, stateId, diagnostics);
                break;
        }
    }

    private static void ValidatePlanExpressionFrameSlots(
        MirSuspensionAutomaton automaton,
        MirExpression expression,
        MirAsyncExecutionStateId stateId,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (expression is MirAsyncFrameSlotExpression frameSlot
            && !automaton.FrameSlots.Any(slot => slot.Id == frameSlot.SlotId && MirTypeFacts.AreEquivalent(slot.Type, frameSlot.Type)))
        {
            Add(diagnostics, $"executable state '{stateId}' reads unknown or incompatible frame slot '{frameSlot.SlotId}'.");
        }

        IEnumerable<MirExpression> children = expression switch
        {
            MirAssignmentExpression assignment => [assignment.Expression],
            MirUnaryExpression unary => [unary.Operand],
            MirAwaitExpression awaited => [awaited.Operand],
            MirBinaryExpression binary => [binary.Left, binary.Right],
            MirNumericConversionExpression conversion => [conversion.Operand],
            MirCallExpression call => call.Arguments,
            MirCallableConstructionExpression construction => construction.Captures,
            MirInvokeExpression invoke => [invoke.Callee, .. invoke.Arguments],
            MirArrayExpression array => array.Elements,
            MirOkExpression ok => [ok.Payload],
            MirErrExpression err => [err.Payload],
            MirPropagateExpression propagation => [propagation.Operand],
            MirIfExpression conditional => [conditional.Condition, conditional.ThenExpression, conditional.ElseExpression],
            _ => [],
        };
        foreach (MirExpression child in children)
        {
            ValidatePlanExpressionFrameSlots(automaton, child, stateId, diagnostics);
        }
    }

    private static void ValidateSlots(
        IReadOnlyList<MirFrameSlotId> slotIds,
        IReadOnlyDictionary<MirFrameSlotId, MirFrameSlot> slots,
        MirAutomatonState state,
        string operation,
        List<MirValidationDiagnostic> diagnostics)
    {
        foreach (MirFrameSlotId slotId in slotIds)
        {
            if (!slots.ContainsKey(slotId))
            {
                Add(diagnostics, $"state '{state.Id}' attempts to {operation} unknown frame slot '{slotId}'.");
            }
        }
    }

    private static void ValidateReachability(
        MirSuspensionAutomaton automaton,
        IReadOnlyDictionary<MirAutomatonStateId, MirAutomatonState> states,
        IReadOnlyDictionary<MirAutomatonStateId, List<MirAutomatonTransition>> outgoing,
        List<MirValidationDiagnostic> diagnostics)
    {
        var reachable = new HashSet<MirAutomatonStateId>();
        var pending = new Queue<MirAutomatonStateId>();
        pending.Enqueue(automaton.EntryStateId);
        int steps = 0;
        while (pending.Count > 0)
        {
            if (++steps > MirSuspensionAutomatonLimits.MaximumWorklistSteps)
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' exceeds the {MirSuspensionAutomatonLimits.MaximumWorklistSteps} worklist-step bound.");
                return;
            }
            MirAutomatonStateId stateId = pending.Dequeue();
            if (!reachable.Add(stateId)) continue;
            foreach (MirAutomatonTransition edge in outgoing[stateId]) pending.Enqueue(edge.Target);
        }
        foreach (MirAutomatonState state in states.Values)
        {
            if (!reachable.Contains(state.Id))
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' contains unreachable generated state '{state.Id}'.");
            }
        }
    }

    private static void CheckLimit(int actual, int maximum, string noun, MirSuspensionAutomaton automaton, List<MirValidationDiagnostic> diagnostics)
    {
        if (actual > maximum)
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' has {actual} {noun}; maximum is {maximum}.");
        }
    }

    private static void Add(List<MirValidationDiagnostic> diagnostics, string message)
        => diagnostics.Add(new MirValidationDiagnostic($"{Prefix}: {message}"));
}
