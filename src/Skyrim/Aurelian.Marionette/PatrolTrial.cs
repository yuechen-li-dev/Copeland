using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dominatus.Core.Blackboard;

namespace Marionette.Skyrim.App;

public sealed record QuestUiObservation(ulong PromptId, int Choice, bool RewardPaid, int Gold,
    uint CellFormId, float PlayerX, float PlayerY, float PlayerZ)
{
    [JsonIgnore]
    public Vector3 Position => new(PlayerX, PlayerY, PlayerZ);
}

public sealed record PatrolTrialState(int Version, Guid AgentId, string Stage, int LastLeg,
    int AccompaniedLegs, uint CellFormId, OwnedAgentPoint Home, int GoldBeforeReward = -1)
{
    public void Validate()
    {
        if (Version != 1 || AgentId == Guid.Empty || LastLeg < 0 || AccompaniedLegs is < 0 or > 2
            || CellFormId == 0 || Home is null || !float.IsFinite(Home.X) || !float.IsFinite(Home.Y)
            || !float.IsFinite(Home.Z) || GoldBeforeReward < -1
            || Stage is not ("available" or "declined" or "accompany" or "return" or "reward_pending" or "completed"))
            throw new InvalidDataException("patrol_trial_checkpoint_invalid");
    }
}

public sealed partial class SkyrimWorldOwnerRuntime
{
    private static readonly BbKey<string> PatrolTrialKey = new("skyrim.patrol-trial.v1");

    public PatrolTrialState? ReadPatrolTrial()
    {
        foreach (var entry in world.Bb.EnumerateSnapshotEntries())
        {
            if (entry.Key != PatrolTrialKey.Name) continue;
            if (entry.Value is not string value || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("patrol_trial_checkpoint_empty");
            try
            {
                var state = JsonSerializer.Deserialize(value, OwnedNpcCheckpointJsonContext.Default.PatrolTrialState)
                    ?? throw new InvalidDataException("patrol_trial_checkpoint_empty");
                state.Validate();
                return state;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("patrol_trial_checkpoint_json_invalid", exception);
            }
        }
        return null;
    }

    public void WritePatrolTrial(PatrolTrialState state)
    {
        state.Validate();
        world.Bb.Set(PatrolTrialKey, JsonSerializer.Serialize(state, OwnedNpcCheckpointJsonContext.Default.PatrolTrialState));
    }

    public void ClearPatrolTrial() => world.Bb.Remove(PatrolTrialKey);
}

/// <summary>Quest rules and prose stay in C#. The native adapter only presents
/// choices, observes the player, and grants an idempotent inventory effect.</summary>
public sealed class PatrolTrialBehavior(OwnedNpcAgentBehavior agent) : ISkyrimWorldBehavior
{
    private PatrolTrialState? lastReported;
    private bool reofferClaim;

    public async ValueTask TickAsync(SkyrimHostContext context, CancellationToken cancellationToken)
    {
        var owner = context.WorldOwner ?? throw new InvalidOperationException("patrol_trial_owner_missing");
        var route = owner.ReadOwnedAgentState();
        var status = agent.Status;
        if (route is null || status is null) return;
        var state = owner.ReadPatrolTrial() ?? new PatrolTrialState(1, route.AgentId, "available",
            status.CompletedLegs, 0, route.CellFormId, route.Origin);
        if (state.AgentId != status.SemanticId) throw new InvalidDataException("patrol_trial_agent_mismatch");
        var observation = await context.QuestUiAsync("query", cancellationToken);
        var ui = observation.QuestUi ?? throw new InvalidDataException("patrol_trial_observation_missing");
        if (ui.RewardPaid && state.Stage is "return" or "reward_pending")
            state = state with { Stage = "completed" };
        switch (state.Stage)
        {
            case "available":
                if (ui.PromptId != 1)
                    await context.QuestUiAsync("prompt", cancellationToken,
                        "Patrol Trial: accompany the hunter through two patrol checkpoints, then return here for 50 gold. Join the trial?", 1);
                else if (ui.Choice == 0)
                {
                    state = state with { Stage = "accompany", LastLeg = status.CompletedLegs };
                    await context.QuestUiAsync("notify", cancellationToken, "Patrol Trial: accompany the hunter to two checkpoints.");
                }
                else if (ui.Choice == 1) state = state with { Stage = "declined" };
                break;
            case "accompany":
                if (status.CompletedLegs > state.LastLeg)
                {
                    var source = await context.ObserveOwnedNpcAsync(cancellationToken);
                    var npc = source.Npcs.SingleOrDefault();
                    bool near = npc is not null && ui.CellFormId == state.CellFormId
                        && Vector3.Distance(ui.Position, new(npc.PositionX, npc.PositionY, npc.PositionZ)) <= 512;
                    int count = near ? Math.Min(2, state.AccompaniedLegs + 1) : state.AccompaniedLegs;
                    state = state with { LastLeg = status.CompletedLegs, AccompaniedLegs = count,
                        Stage = count == 2 ? "return" : "accompany" };
                    if (count == 2) await context.QuestUiAsync("notify", cancellationToken,
                        "Patrol Trial: checkpoints complete. Return to the starting point to collect your reward.");
                }
                break;
            case "return":
                bool nearHome = ui.CellFormId == state.CellFormId && Vector3.Distance(ui.Position, state.Home.Vector) <= 256;
                if (!nearHome) reofferClaim = true;
                if (nearHome)
                {
                    if (ui.PromptId != 2 || (ui.Choice == 1 && reofferClaim))
                    {
                        await context.QuestUiAsync("prompt", cancellationToken,
                            "Patrol Trial complete. You accompanied the hunter and returned. Collect your 50 gold reward?", 2);
                        reofferClaim = false;
                    }
                    else if (ui.Choice == 0)
                        state = state with { Stage = "reward_pending", GoldBeforeReward = ui.Gold };
                }
                break;
            case "reward_pending":
                var rewarded = await context.QuestUiAsync("reward", cancellationToken);
                if (rewarded.QuestUi?.RewardPaid != true) throw new InvalidDataException("patrol_trial_reward_unconfirmed");
                state = state with { Stage = "completed" };
                await context.QuestUiAsync("notify", cancellationToken, "Completed: Patrol Trial. Reward: 50 gold.");
                break;
            case "completed":
                if (!ui.RewardPaid) throw new InvalidDataException("patrol_trial_reward_checkpoint_mismatch");
                break;
        }
        owner.WritePatrolTrial(state);
        if (state != lastReported)
        {
            Console.WriteLine("SKYRIM_PATROL_TRIAL " + JsonSerializer.Serialize(state,
                MarionetteWireJsonContext.Default.PatrolTrialState));
            lastReported = state;
        }
    }
}
