using System.Numerics;
using System.Text.Json;

namespace Marionette.Skyrim.App;

public sealed record OwnedMovementProof(OwnedNpcIdentity Identity, string[] TerminalStates,
    int Samples, int MovingSamples, int AnimationSamples, float TravelledDistance,
    uint FinalNearbyCount, bool MarkerReleased);

/// <summary>Bounded walking, cancellation, unreachable-goal and deadline proof.</summary>
public sealed class OwnedNpcMovementProofBehavior(TimeSpan? startupDelay = null) : ISkyrimWorldBehavior
{
    private enum Phase { Startup, LoadActor, StartGoal, ObserveGoal, Settle, Release, Released, Complete }
    private Phase phase;
    private DateTimeOffset notBefore;
    private OwnedNpcIdentity? identity;
    private int goalIndex;
    private ulong firstMovementId;
    private Vector3 start;
    private Vector3 previous;
    private Vector3 stopped;
    private float travelled;
    private int samples;
    private int movingSamples;
    private int animationSamples;
    private int waitingTicks;
    private uint baseline;
    private readonly List<string> terminalStates = [];

    public OwnedMovementProof? CompletedProof { get; private set; }

    public async ValueTask TickAsync(SkyrimHostContext context, CancellationToken cancellationToken)
    {
        if (phase == Phase.Complete) return;
        if (identity is not null && identity.WorldGeneration != context.State.WorldGeneration)
        {
            throw new InvalidDataException("owned_movement_proof_world_changed");
        }
        if (phase == Phase.Startup)
        {
            if (notBefore == default) notBefore = DateTimeOffset.UtcNow + (startupDelay ?? TimeSpan.FromSeconds(8));
            if (DateTimeOffset.UtcNow < notBefore) return;
            WorldNpcResult before = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
            Require(before.OwnedActiveCount == 0 && before.OwnedBaseCountComplete, "movement_baseline_unavailable");
            baseline = before.OwnedBaseLiveCount;
            WorldNpcResult spawned = await context.SpawnOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
            identity = spawned.OwnedNpc ?? throw new InvalidDataException("movement_owned_identity_missing");
            phase = Phase.LoadActor;
            return;
        }
        WorldNpcResult source = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
        Require(source.OwnedNpc == identity, "movement_owned_identity_lost");
        WorldNpcObservation? actor = source.Npcs.SingleOrDefault();
        if (phase == Phase.LoadActor)
        {
            if (actor is null || !actor.Loaded)
            {
                Require(++waitingTicks < 100, "movement_actor_never_loaded");
                return;
            }
            phase = Phase.StartGoal;
        }
        if (phase == Phase.StartGoal)
        {
            Require(actor is not null && actor.Loaded && !actor.Dead, "movement_actor_unavailable");
            start = Position(actor!);
            previous = start;
            Vector3 goal = goalIndex switch
            {
                0 => start + new Vector3(256, 256, 0),
                1 => start + new Vector3(-512, 0, 0),
                2 => start + new Vector3(0, 0, 512),
                _ => start + new Vector3(512, 0, 0),
            };
            WorldNpcResult accepted = await context.MoveOwnedNpcAsync(source, identity!, goal.X, goal.Y, goal.Z,
                cancellationToken, durationMilliseconds: goalIndex == 3 ? 500U : 12000U).ConfigureAwait(false);
            Require(accepted.OwnedMovement?.State == "walking", "movement_not_accepted");
            if (goalIndex == 0) firstMovementId = accepted.OwnedMovement!.MovementId;
            phase = Phase.ObserveGoal;
            if (goalIndex == 3)
            {
                // Deliberately leave the connection idle beyond the 500 ms
                // goal deadline. Native maintenance must stop without host RPCs.
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            return;
        }
        if (phase == Phase.ObserveGoal)
        {
            Require(actor is not null && actor.Loaded && !actor.Dead, "movement_actor_lost");
            Vector3 position = Position(actor!);
            travelled += Vector3.Distance(previous, position);
            previous = position;
            samples++;
            if (actor!.Moving) movingSamples++;
            if (actor.AnimationSpeedAvailable && actor.AnimationSpeed > 1) animationSamples++;
            OwnedNpcMovement movement = source.OwnedMovement ?? throw new InvalidDataException("movement_state_missing");
            if (goalIndex == 1 && movement.State == "walking" && Vector3.Distance(start, position) >= 32)
            {
                await context.CancelOwnedMovementAsync(identity!, firstMovementId, cancellationToken,
                    "owned_movement_identity_mismatch").ConfigureAwait(false);
                WorldNpcResult cancelled = await context.CancelOwnedMovementAsync(identity!, movement.MovementId,
                    cancellationToken).ConfigureAwait(false);
                Require(cancelled.OwnedMovement?.State is "cancelled" or "stopping", "movement_cancel_not_observed");
                movement = cancelled.OwnedMovement!;
                position = Position(cancelled.Npcs.Single());
            }
            if (movement.State is "walking" or "stopping") return;
            string expected = goalIndex switch { 0 => "arrived", 1 => "cancelled", 2 => "blocked", _ => "timed_out" };
            Require(movement.State == expected, "movement_terminal_unexpected:" + movement.State + ":" + movement.Reason);
            if (goalIndex == 3) Require(movement.StopRequestedSeconds is > 0 and <= 1.0f,
                "movement_deadline_depended_on_host_polling");
            if (goalIndex == 0)
            {
                Require(movement.Distance <= movement.StoppingDistance && Vector3.Distance(start, position) >= 128
                    && samples >= 4 && movingSamples >= 2, "walking_not_demonstrated");
            }
            terminalStates.Add(movement.State);
            stopped = position;
            notBefore = DateTimeOffset.UtcNow.AddMilliseconds(600);
            phase = Phase.Settle;
            return;
        }
        if (phase == Phase.Settle)
        {
            if (DateTimeOffset.UtcNow < notBefore) return;
            Require(actor is not null && !actor.AiEnabled && !actor.Moving
                && Vector3.Distance(stopped, Position(actor)) <= 8, "movement_did_not_stop");
            phase = ++goalIndex < 4 ? Phase.StartGoal : Phase.Release;
            return;
        }
        if (phase == Phase.Release)
        {
            await context.ReleaseOwnedNpcAsync(identity!, cancellationToken).ConfigureAwait(false);
            phase = Phase.Released;
            waitingTicks = 0;
            return;
        }
        if (phase == Phase.Released)
        {
            if (source.OwnedNpcState == "retiring")
            {
                Require(++waitingTicks < 100, "movement_actor_retirement_stalled");
                return;
            }
            Require(source.OwnedNpcState == "released" && source.OwnedActiveCount == 0
                && source.OwnedBaseCountComplete && source.OwnedBaseLiveCount == baseline
                && source.OwnedMovement?.MarkerActive == false, "movement_cleanup_unconfirmed");
            CompletedProof = new OwnedMovementProof(identity!, terminalStates.ToArray(), samples, movingSamples,
                animationSamples, travelled, source.OwnedBaseLiveCount, true);
            Console.WriteLine("SKYRIM_OWNED_MOVEMENT_PROOF_PASSED " +
                JsonSerializer.Serialize(CompletedProof, MarionetteWireJsonContext.Default.OwnedMovementProof));
            phase = Phase.Complete;
        }
    }

    private static Vector3 Position(WorldNpcObservation actor) => new(actor.PositionX, actor.PositionY, actor.PositionZ);

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }
}
