using System.Text.Json;

namespace Marionette.Skyrim.App;

public sealed record OwnedNpcCycleProof(int Cycle, OwnedNpcIdentity Identity, uint BystanderFormId,
    float BeforeSpeed, float ObservedChangedSpeed, uint BaselineCount, uint FinalCount,
    ulong CreatedCount, ulong RetiredCount, string ReleaseState);

/// <summary>Two bounded native ownership cycles. No AI, persistence, or movement policy.</summary>
public sealed class OwnedNpcProofBehavior : ISkyrimWorldBehavior
{
    private enum Phase { Baseline, Spawn, WaitForActor, ObserveChange, WaitForRelease, Complete }

    private Phase phase;
    private OwnedNpcIdentity? identity;
    private OwnedNpcIdentity? previous;
    private WorldNpcObservation? bystander;
    private uint baseline;
    private ulong initialCreated;
    private ulong initialRetired;
    private float before;
    private float changed;
    private int waitTicks;
    private readonly List<OwnedNpcCycleProof> proofs = [];

    public IReadOnlyList<OwnedNpcCycleProof> CompletedCycles => proofs;

    public async ValueTask TickAsync(SkyrimHostContext context, CancellationToken cancellationToken)
    {
        if (phase == Phase.Complete) return;
        if (identity is not null && identity.WorldGeneration != context.State.WorldGeneration)
        {
            throw new InvalidDataException("owned_npc_proof_interrupted_by_world_reset");
        }
        switch (phase)
        {
            case Phase.Baseline:
            {
                WorldNpcResult state = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                Require(state.OwnedActiveCount == 0 && state.OwnedBaseCountComplete, "owned_baseline_unavailable");
                baseline = state.OwnedBaseLiveCount;
                initialCreated = state.OwnedCreatedCount;
                initialRetired = state.OwnedRetiredCount;
                WorldNpcResult ordinary = await context.ObserveNpcsAsync(cancellationToken, refresh: true).ConfigureAwait(false);
                bystander = ordinary.Npcs.FirstOrDefault(npc => npc.Loaded && !npc.Dead && npc.FormId < 0xFF000000);
                Require(bystander is not null, "owned_proof_needs_ordinary_bystander");
                phase = Phase.Spawn;
                break;
            }
            case Phase.Spawn:
            {
                WorldNpcResult spawned = await context.SpawnOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                identity = spawned.OwnedNpc ?? throw new InvalidDataException("owned_spawn_identity_missing");
                Require(identity.FormId >= 0xFF000000 && identity.BaseFormId == 0x73FBF
                    && identity.WorldGeneration == context.State.WorldGeneration, "owned_spawn_identity_invalid");
                Require(previous is null || previous.LeaseId != identity.LeaseId, "owned_lease_reused");
                WorldNpcResult duplicate = await context.SpawnOwnedNpcAsync(cancellationToken, "owned_npc_already_exists").ConfigureAwait(false);
                Require(duplicate.OwnedNpc == identity && duplicate.OwnedCreatedCount == initialCreated + (ulong)proofs.Count + 1,
                    "duplicate_spawn_changed_ledger");
                await context.ReleaseOwnedNpcAsync(identity, cancellationToken, "owned_npc_identity_mismatch",
                    bystander!.FormId).ConfigureAwait(false);
                if (previous is not null)
                {
                    await context.ReleaseOwnedNpcAsync(previous, cancellationToken, "owned_npc_identity_mismatch").ConfigureAwait(false);
                }
                phase = Phase.WaitForActor;
                waitTicks = 0;
                break;
            }
            case Phase.WaitForActor:
            {
                WorldNpcResult state = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                Require(state.OwnedNpc == identity && state.OwnedNpcState == "live", "owned_actor_identity_lost");
                WorldNpcObservation? actor = state.Npcs.SingleOrDefault(npc => npc.FormId == identity!.FormId);
                if (actor is null || !actor.Loaded)
                {
                    Require(++waitTicks <= 100, "owned_actor_never_loaded");
                    return;
                }
                Require(!actor.Dead && state.OwnedBaseCountComplete && state.OwnedBaseLiveCount == baseline + 1,
                    "owned_spawn_count_or_liveness_invalid");
                before = actor.BaseSpeedMultiplier;
                WorldNpcResult tuned = await context.SetBaseSpeedAsync(state, actor, before + 5,
                    cancellationToken, identity).ConfigureAwait(false);
                Require(Math.Abs(tuned.AfterBaseSpeed - before - 5) < 0.001f, "owned_tune_readback_failed");
                phase = Phase.ObserveChange;
                break;
            }
            case Phase.ObserveChange:
            {
                WorldNpcResult state = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                WorldNpcObservation actor = state.Npcs.Single(npc => npc.FormId == identity!.FormId);
                changed = actor.BaseSpeedMultiplier;
                Require(actor.Loaded && !actor.Dead && Math.Abs(changed - before - 5) < 0.001f,
                    "owned_tune_not_observed");
                await context.ReleaseOwnedNpcAsync(identity!, cancellationToken).ConfigureAwait(false);
                phase = Phase.WaitForRelease;
                waitTicks = 0;
                break;
            }
            case Phase.WaitForRelease:
            {
                WorldNpcResult state = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                Require(state.OwnedNpc == identity, "owned_retirement_identity_changed");
                if (state.OwnedNpcState == "retiring")
                {
                    Require(++waitTicks <= 100, "owned_actor_retirement_stalled");
                    return;
                }
                Require(state.OwnedNpcState == "released" && state.OwnedActiveCount == 0
                    && state.OwnedBaseCountComplete && state.OwnedBaseLiveCount == baseline,
                    "owned_actor_not_released");
                Require(state.OwnedCreatedCount == initialCreated + (ulong)proofs.Count + 1
                    && state.OwnedRetiredCount == initialRetired + (ulong)proofs.Count + 1, "owned_ledger_unbalanced");
                WorldNpcResult ordinary = await context.ObserveNpcsAsync(cancellationToken, refresh: true).ConfigureAwait(false);
                Require(ordinary.Npcs.Any(npc => npc.FormId == bystander!.FormId && npc.Loaded && !npc.Dead),
                    "ordinary_bystander_missing_after_release");
                await context.ReleaseOwnedNpcAsync(identity!, cancellationToken).ConfigureAwait(false);
                var proof = new OwnedNpcCycleProof(proofs.Count + 1, identity!, bystander!.FormId,
                    before, changed, baseline, state.OwnedBaseLiveCount,
                    state.OwnedCreatedCount, state.OwnedRetiredCount, state.OwnedNpcState);
                proofs.Add(proof);
                Console.WriteLine("SKYRIM_OWNED_NPC_CYCLE " +
                    JsonSerializer.Serialize(proof, MarionetteWireJsonContext.Default.OwnedNpcCycleProof));
                previous = identity;
                identity = null;
                phase = proofs.Count == 2 ? Phase.Complete : Phase.Spawn;
                if (phase == Phase.Complete) Console.WriteLine("SKYRIM_OWNED_NPC_PROOF_PASSED cycles=2");
                break;
            }
        }
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }
}
