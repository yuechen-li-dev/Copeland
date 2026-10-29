using System.Numerics;
using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;

namespace Marionette.Skyrim.App;

public enum OwnedNpcAgentAction { Walk, Release }

public sealed record OwnedNpcAgentIntent(long Id, OwnedNpcAgentAction Action,
    OwnedNpcIdentity Identity, ulong ObservationSequence, Vector3 Target);

public sealed record OwnedNpcAgentReceipt(long IntentId, OwnedNpcIdentity Identity,
    bool Accepted, ulong MovementId, string Reason);

public sealed record OwnedNpcAgentFact(OwnedNpcIdentity Identity, ulong Sequence,
    bool Available, Vector3 Position, OwnedNpcMovement? Movement);

public sealed record OwnedNpcAgentStatus(OwnedNpcIdentity Identity, long Tick,
    string State, string? SelectedOption, int Waypoint, int CompletedLegs,
    int ConsecutiveFailures, ulong MovementId, string Reason);

/// <summary>
/// One generation-bound Dominatus brain. Skyrim facts are inputs; typed intents
/// leave through the serial host adapter. No native object or transport is retained.
/// </summary>
public sealed class OwnedNpcAgentRuntime
{
    private readonly AiWorld world = new();
    private readonly AiAgent agent;
    private readonly DecisionTrace trace = new();
    private readonly Vector3[] waypoints;
    private OwnedNpcAgentFact fact;
    private ulong lastPhysicalSequence;
    private OwnedNpcAgentIntent? pending;
    private long nextIntentId;
    private long awaitingReceipt;
    private long ticks;
    private bool receiptAccepted;
    private bool stopped;
    private ulong movementId;
    private string state = "starting";
    private string reason = "";
    private int waypoint;
    private int completedLegs;
    private int failures;

    public OwnedNpcAgentRuntime(OwnedNpcAgentFact initial, float restSeconds = 1.5f,
        int maximumRetries = 2, Vector3? patrolOffset = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        Vector3 offset = patrolOffset ?? new Vector3(256, 256, 0);
        if (!initial.Available || initial.Identity.WorldGeneration == 0
            || initial.Identity.LeaseId == 0 || initial.Sequence == 0
            || !Finite(initial.Position) || !Finite(offset)
            || offset.Length() is < 128 or > 512 || restSeconds is < 0 or > 30
            || !float.IsFinite(restSeconds) || maximumRetries is < 0 or > 5)
        {
            throw new ArgumentException("owned_agent_definition_invalid");
        }
        fact = initial;
        lastPhysicalSequence = initial.Sequence;
        waypoints = [initial.Position + offset, initial.Position];
        RestSeconds = restSeconds;
        MaximumRetries = maximumRetries;
        var brain = OwnedNpcAgentFlow.Define(this).CreateBrain();
        brain.Trace = trace;
        agent = new AiAgent(brain);
        world.Add(agent);
    }

    public float RestSeconds { get; }
    public int MaximumRetries { get; }
    public OwnedNpcIdentity Identity => fact.Identity;
    public bool BodyLost => !fact.Available;
    public bool MustStop => stopped || failures > MaximumRetries;
    public bool NeedsRecovery => failures > 0;
    public bool ReceiptAccepted => receiptAccepted;
    public OwnedNpcAgentStatus Status => new(Identity, ticks, state, trace.SelectedOption,
        waypoint, completedLegs, failures, movementId, reason);
    public FlowInspection FlowInspection => OwnedNpcAgentFlow.Define(this).Inspect();

    public void Observe(OwnedNpcAgentFact next)
    {
        if (next.Identity != Identity || !Finite(next.Position)
            || (next.Available && next.Sequence <= lastPhysicalSequence))
        {
            throw new InvalidDataException("owned_agent_fact_identity_or_sequence_invalid");
        }
        // Retirement observations legitimately carry zero: there is no physical
        // actor sample. They can revoke availability, never authorize movement or
        // reset the ordering cursor of subsequent physical observations.
        if (next.Available)
        {
            lastPhysicalSequence = next.Sequence;
        }
        fact = next;
    }

    public void Tick(float readySeconds)
    {
        if (!float.IsFinite(readySeconds) || readySeconds is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(readySeconds));
        }
        ticks++;
        world.Tick(readySeconds);
    }

    public OwnedNpcAgentIntent? TakeIntent()
    {
        OwnedNpcAgentIntent? result = pending;
        pending = null;
        return result;
    }

    public void Receive(OwnedNpcAgentReceipt receipt)
    {
        if (receipt.Identity != Identity || receipt.IntentId != awaitingReceipt)
        {
            throw new InvalidDataException("owned_agent_receipt_correlation_invalid");
        }
        // The event waiter is already installed before the adapter receives an intent.
        // A receipt may be delivered exactly once, even before the next kernel tick.
        awaitingReceipt = 0;
        if (!world.Mail.Send(agent.Id, receipt))
        {
            throw new InvalidOperationException("owned_agent_mailbox_unavailable");
        }
    }

    internal long Begin(OwnedNpcAgentAction action)
    {
        if (pending is not null || awaitingReceipt != 0)
        {
            throw new InvalidOperationException("owned_agent_operation_already_pending");
        }
        long id = ++nextIntentId;
        pending = new OwnedNpcAgentIntent(id, action, Identity, fact.Sequence, waypoints[waypoint]);
        awaitingReceipt = id;
        receiptAccepted = false;
        state = action == OwnedNpcAgentAction.Walk ? "requesting_walk" : "releasing";
        return id;
    }

    internal void Accept(OwnedNpcAgentReceipt receipt)
    {
        receiptAccepted = receipt.Accepted;
        movementId = receipt.MovementId;
        reason = receipt.Reason;
        if (receipt.Accepted && movementId != 0)
        {
            state = "walking";
        }
    }

    internal bool MovementFinished()
    {
        return BodyLost || fact.Movement is null || fact.Movement.MovementId != movementId
            || fact.Movement.State is not ("walking" or "stopping");
    }

    internal bool FinishMovement()
    {
        if (!BodyLost && fact.Movement is { State: "arrived" } movement
            && movement.MovementId == movementId
            && movement.Distance <= movement.StoppingDistance
            && Vector3.Distance(fact.Position, waypoints[waypoint]) <= movement.StoppingDistance)
        {
            completedLegs++;
            waypoint = (waypoint + 1) % waypoints.Length;
            failures = 0;
            reason = "arrived";
            return true;
        }
        reason = BodyLost ? "body_lost" : fact.Movement?.Reason ?? "movement_missing";
        if (reason.Length == 0)
        {
            reason = fact.Movement?.State ?? "movement_missing";
        }
        failures++;
        return false;
    }

    internal void RejectMovement()
    {
        failures++;
    }

    internal void SetState(string value)
    {
        state = value;
    }

    internal void Stop()
    {
        stopped = true;
        state = "stopped";
    }

    private static bool Finite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    private sealed class DecisionTrace : IAiTraceSink
    {
        public string? SelectedOption { get; private set; }
        public void OnEnter(StateId state, float time, string reason) { }
        public void OnExit(StateId state, float time, string reason) { }
        public void OnTransition(StateId from, StateId to, float time, string reason) { }
        public void OnYield(StateId state, float time, object yielded)
        {
            if (yielded is DecisionReport report)
            {
                SelectedOption = report.Switched ? report.BestId : report.CurrentId ?? report.BestId;
            }
        }
    }
}

public static partial class OwnedNpcAgentFlow
{
    [DominatusFlow("aurelian.skyrim.owned-npc.m4")]
    public static partial FlowDefinition Define(OwnedNpcAgentRuntime runtime);

    [DominatusState("aurelian.skyrim.owned-npc.choose", Root = true)]
    private static IEnumerator<AiStep> Choose(AiCtx context, OwnedNpcAgentRuntime runtime)
    {
        runtime.SetState("deciding");
        yield return Ai.Decide(
        [
            Ai.Option("BodyLost", Consideration.FromBool((_, _) => runtime.BodyLost), States.Release),
            Ai.Option("RetryBudgetExhausted", Consideration.FromBool((_, _) => runtime.MustStop)
                * Consideration.Constant(0.9f), States.Release),
            Ai.Option("Recover", Consideration.FromBool((_, _) => runtime.NeedsRecovery)
                * Consideration.Constant(0.75f), States.Recover),
            Ai.Option("Patrol", Consideration.Constant(0.5f), States.Walk),
        ], hysteresis: 0, minCommitSeconds: 0);
        // Decide retains the current option and emits no transition when that
        // option wins again. This cyclic goal explicitly re-enters its selected
        // authored state after an action returns; it does not rescore options.
        yield return Ai.Goto(runtime.Status.SelectedOption switch
        {
            "BodyLost" or "RetryBudgetExhausted" => States.Release,
            "Recover" => States.Recover,
            "Patrol" => States.Walk,
            _ => throw new InvalidOperationException("owned_agent_decision_missing"),
        }, "resume retained utility option");
    }

    [DominatusState("aurelian.skyrim.owned-npc.walk")]
    private static IEnumerator<AiStep> Walk(AiCtx context, OwnedNpcAgentRuntime runtime)
    {
        long intent = runtime.Begin(OwnedNpcAgentAction.Walk);
        yield return Ai.Event<OwnedNpcAgentReceipt>(
            filter: receipt => receipt.IntentId == intent,
            onConsumed: (_, receipt) => runtime.Accept(receipt));
        if (!runtime.ReceiptAccepted)
        {
            runtime.RejectMovement();
            yield return Ai.Goto(States.Choose, "native request refused");
            yield break;
        }
        yield return Ai.Until(_ => runtime.MovementFinished());
        if (runtime.FinishMovement())
        {
            yield return Ai.Goto(States.Rest, "observed arrival");
        }
        else
        {
            yield return Ai.Goto(States.Choose, "observed movement failure");
        }
    }

    [DominatusState("aurelian.skyrim.owned-npc.rest")]
    private static IEnumerator<AiStep> Rest(AiCtx context, OwnedNpcAgentRuntime runtime)
    {
        runtime.SetState("resting");
        yield return Ai.Wait(runtime.RestSeconds);
        yield return Ai.Goto(States.Choose, "rest complete");
    }

    [DominatusState("aurelian.skyrim.owned-npc.recover")]
    private static IEnumerator<AiStep> Recover(AiCtx context, OwnedNpcAgentRuntime runtime)
    {
        runtime.SetState("recovering");
        yield return Ai.Wait(1.0f);
        // Retry the same waypoint after backoff; only a real arrival advances it.
        if (runtime.BodyLost || runtime.MustStop)
        {
            yield return Ai.Goto(States.Release, "recovery interrupted");
        }
        else
        {
            yield return Ai.Goto(States.Walk, "bounded retry after backoff");
        }
    }

    [DominatusState("aurelian.skyrim.owned-npc.release")]
    private static IEnumerator<AiStep> Release(AiCtx context, OwnedNpcAgentRuntime runtime)
    {
        long intent = runtime.Begin(OwnedNpcAgentAction.Release);
        yield return Ai.Event<OwnedNpcAgentReceipt>(
            filter: receipt => receipt.IntentId == intent,
            onConsumed: (_, receipt) => runtime.Accept(receipt));
        yield return Ai.Goto(States.Stopped, "retry budget exhausted; retirement requested");
    }

    [DominatusState("aurelian.skyrim.owned-npc.stopped")]
    private static IEnumerator<AiStep> Stopped(AiCtx context, OwnedNpcAgentRuntime runtime)
    {
        runtime.Stop();
        while (true)
        {
            yield return Ai.Steady("owned body lost or agent stopped");
        }
    }
}
