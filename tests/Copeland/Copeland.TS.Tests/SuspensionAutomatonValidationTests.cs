using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class SuspensionAutomatonValidationTests
{
    [Fact]
    public void ValidAsyncAutomaton_IsAcceptedBySharedMirValidation()
    {
        MirFunction function = CreateFunction(CreateValidAutomaton());

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [function]));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AsyncFunctionWithoutAutomaton_IsRejectedBeforeEmission()
    {
        MirFunction function = new("load", [], new MirNamedType("number"), [], [], isAsync: true);

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [function]));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("async function 'load' has no automaton", StringComparison.Ordinal));
    }

    [Fact]
    public void SuspensionOfNonAsyncSlot_IsRejectedBeforeEmission()
    {
        MirSuspensionAutomaton automaton = CreateValidAutomaton(awaitedSlotType: new MirNamedType("number"));

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [CreateFunction(automaton)]));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("awaits a non-Async frame slot", StringComparison.Ordinal));
    }

    [Fact]
    public void CancellationMustTargetCancelledTerminal()
    {
        MirSuspensionAutomaton valid = CreateValidAutomaton();
        MirSuspensionAutomaton malformed = new(
            valid.Identity,
            valid.OwnerFunctionName,
            valid.EntryStateId,
            valid.FrameSlots,
            valid.States,
            valid.Transitions.Select(transition => transition.Kind == MirAutomatonTransitionKind.Cancellation
                ? new MirAutomatonTransition(transition.Source, new MirAutomatonStateId("complete"), transition.Kind, transition.Provenance)
                : transition).ToArray());

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [CreateFunction(malformed)]));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("does not target a Cancelled state", StringComparison.Ordinal));
    }

    [Fact]
    public void StateBoundRejectsFirstExcessValue()
    {
        MirSuspensionAutomaton valid = CreateValidAutomaton();
        var states = valid.States.ToList();
        for (int index = states.Count; index <= MirSuspensionAutomatonLimits.MaximumStates; index++)
        {
            states.Add(new MirTerminalAutomatonState(
                new MirAutomatonStateId("unused-" + index),
                MirAutomatonStateKind.InvariantFailure,
                "generated/unused/" + index));
        }
        MirSuspensionAutomaton malformed = new(
            valid.Identity,
            valid.OwnerFunctionName,
            valid.EntryStateId,
            valid.FrameSlots,
            states,
            valid.Transitions);

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [CreateFunction(malformed)]));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("257 states; maximum is 256", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecutablePlanRejectsUnknownAwaitFrameSlot()
    {
        MirSuspensionAutomaton valid = CreateValidAutomaton();
        MirAsyncExecutionStateId entry = new("exec-entry");
        MirAsyncExecutionStateId complete = new("exec-complete");
        MirFrameSlotId resumed = new("resumed");
        var frameSlots = valid.FrameSlots.Append(new MirFrameSlot(resumed, new MirNamedType("number"), "resume value")).ToArray();
        MirSuspensionAutomaton malformed = new(
            valid.Identity,
            valid.OwnerFunctionName,
            valid.EntryStateId,
            frameSlots,
            valid.States,
            valid.Transitions,
            new MirAsyncExecutionPlan(
                entry,
                [
                    new MirAsyncAwaitExecutionState(
                        entry,
                        new MirVariableExpression("operation", new MirAsyncType(new MirNamedType("number"))),
                        new MirFrameSlotId("missing-await-slot"),
                        resumed,
                        complete),
                    new MirAsyncReturnExecutionState(complete, new MirReturnStatement(null)),
                ]));

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [CreateFunction(malformed)]));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("unknown or non-Async computation frame slot", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecutableAwaitStateRejectsAnIncompatibleResumeSlot()
    {
        MirSuspensionAutomaton valid = CreateValidAutomaton();
        MirAsyncExecutionStateId entry = new("exec-entry");
        MirAsyncExecutionStateId complete = new("exec-complete");
        MirFrameSlotId operation = valid.FrameSlots.Single().Id;
        var frameSlots = valid.FrameSlots.Append(new MirFrameSlot(new MirFrameSlotId("resumed"), new MirNamedType("string"), "resume value")).ToArray();
        MirSuspensionAutomaton malformed = new(
            valid.Identity,
            valid.OwnerFunctionName,
            valid.EntryStateId,
            frameSlots,
            valid.States,
            valid.Transitions,
            new MirAsyncExecutionPlan(
                entry,
                [
                    new MirAsyncAwaitExecutionState(
                        entry,
                        new MirVariableExpression("operation", new MirAsyncType(new MirNamedType("number"))),
                        operation,
                        new MirFrameSlotId("resumed"),
                        complete),
                    new MirAsyncReturnExecutionState(complete, new MirReturnStatement(null)),
                ]));

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [CreateFunction(malformed)]));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("resume-value frame slot incompatible", StringComparison.Ordinal));
    }

    [Fact]
    public void FunctionReturnPropagationRejectsAnAccidentalHandlerTransfer()
    {
        MirSuspensionAutomaton valid = CreateValidAutomaton();
        MirAsyncExecutionStateId entry = new("exec-entry");
        MirAsyncExecutionStateId complete = new("exec-complete");
        MirFrameSlotId success = new("success");
        MirFrameSlotId error = new("error");
        MirResultType resultType = new(new MirNamedType("number"), new MirNamedType("string"));
        var frameSlots = valid.FrameSlots
            .Append(new MirFrameSlot(success, resultType.SuccessType, "propagation success"))
            .Append(new MirFrameSlot(error, resultType.ErrorType, "propagation error"))
            .ToArray();
        MirSuspensionAutomaton malformed = new(
            valid.Identity,
            valid.OwnerFunctionName,
            valid.EntryStateId,
            frameSlots,
            valid.States,
            valid.Transitions,
            new MirAsyncExecutionPlan(
                entry,
                [
                    new MirAsyncPropagateExecutionState(
                        entry,
                        new MirOkExpression(new MirLiteralExpression(1.0, resultType.SuccessType), resultType),
                        new MirPropagationTarget.FunctionReturn(),
                        success,
                        complete,
                        complete,
                        error),
                    new MirAsyncReturnExecutionState(complete, new MirReturnStatement(null)),
                ]));

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [CreateFunction(malformed)]));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("must not carry a lexical handler transfer", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecutableExpressionRejectsAnUnknownFrameSlotRead()
    {
        MirSuspensionAutomaton valid = CreateValidAutomaton();
        MirAsyncExecutionStateId entry = new("exec-entry");
        MirAsyncExecutionStateId complete = new("exec-complete");
        MirFrameSlotId target = new("target");
        var frameSlots = valid.FrameSlots.Append(new MirFrameSlot(target, new MirNamedType("number"), "expression target")).ToArray();
        MirSuspensionAutomaton malformed = new(
            valid.Identity,
            valid.OwnerFunctionName,
            valid.EntryStateId,
            frameSlots,
            valid.States,
            valid.Transitions,
            new MirAsyncExecutionPlan(
                entry,
                [
                    new MirAsyncEvaluateExpressionState(
                        entry,
                        target,
                        new MirAsyncFrameSlotExpression(new MirFrameSlotId("missing"), new MirNamedType("number")),
                        complete),
                    new MirAsyncReturnExecutionState(complete, new MirReturnStatement(null)),
                ]));

        IReadOnlyList<MirValidationDiagnostic> diagnostics = MirValidator.Validate(new MirProgram([], [CreateFunction(malformed)]));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("reads unknown or incompatible frame slot", StringComparison.Ordinal));
    }

    private static MirFunction CreateFunction(MirSuspensionAutomaton automaton)
        => new("load", [], new MirNamedType("number"), [], [], isAsync: true, suspensionAutomaton: automaton);

    private static MirSuspensionAutomaton CreateValidAutomaton(MirType? awaitedSlotType = null)
    {
        MirAutomatonStateId entry = new("entry");
        MirAutomatonStateId execute = new("execute");
        MirAutomatonStateId suspend = new("await-read");
        MirAutomatonStateId complete = new("complete");
        MirAutomatonStateId cancelled = new("cancelled");
        MirFrameSlotId operation = new("operation");

        return new MirSuspensionAutomaton(
            "module/load",
            "load",
            entry,
            [new MirFrameSlot(operation, awaitedSlotType ?? new MirAsyncType(new MirNamedType("number")), "body/await-read")],
            [
                new MirTerminalAutomatonState(entry, MirAutomatonStateKind.Entry, "body/entry"),
                new MirExecutionAutomatonState(execute, "body/prepare", [], [operation]),
                new MirAwaitSuspensionAutomatonState(suspend, "body/await-read", operation, new MirNamedType("number")),
                new MirCompletionAutomatonState(complete, "body/return", new MirNamedType("number")),
                new MirTerminalAutomatonState(cancelled, MirAutomatonStateKind.Cancelled, "body/cancelled"),
            ],
            [
                new MirAutomatonTransition(entry, execute, MirAutomatonTransitionKind.Unconditional, "body/entry"),
                new MirAutomatonTransition(execute, suspend, MirAutomatonTransitionKind.Unconditional, "body/await-read"),
                new MirAutomatonTransition(suspend, complete, MirAutomatonTransitionKind.ResumeSuccess, "body/await-read/resume"),
                new MirAutomatonTransition(suspend, cancelled, MirAutomatonTransitionKind.Cancellation, "body/await-read/cancel"),
            ]);
    }
}
