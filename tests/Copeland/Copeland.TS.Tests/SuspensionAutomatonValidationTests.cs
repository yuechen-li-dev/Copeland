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
