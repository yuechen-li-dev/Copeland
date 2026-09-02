using Aurelian.Runtime.Dominatus;
using Aurelian.Runtime.Sessions;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace TinyFarm.Core;

public sealed record AgentObservation(
    ActorId Self,
    LocationId Location,
    IReadOnlyList<ActorId> NearbyActors,
    IReadOnlyList<ItemId> Inventory,
    int Minute,
    IReadOnlyList<GameEventKind> RecentEvents,
    LocationId ScheduledDestination,
    SceneAnchorId ScheduledAnchor,
    bool HasReachedAnchor,
    bool ShouldBuySeed = false);

public static partial class TinyFarmNpcFlow
{
    private static readonly BbKey<string> CurrentLocation = new("TinyFarm.Observation.Location");
    private static readonly BbKey<string> Destination = new("TinyFarm.Observation.Destination");
    private static readonly BbKey<bool> HasReachedAnchor = new("TinyFarm.Observation.HasReachedAnchor");
    private static readonly BbKey<string> SelectedAction = new("TinyFarm.Decision.Action");
    private static readonly BbKey<bool> ShouldBuySeed = new("TinyFarm.Observation.ShouldBuySeed");

    public static FlowDefinition Definition { get; } = Define();

    [DominatusFlow("tiny-farm.npc-schedule")]
    public static partial FlowDefinition Define();

    [DominatusState("Choose", Root = true)]
    private static IEnumerator<AiStep> Choose(AiCtx context)
    {
        yield return Ai.Decide(
            new DecisionSlot("TinyFarm.ScheduleIntent"),
            [
                Ai.Option(
                    "Move",
                    new Consideration((_, agent) => NeedsMove(agent) ? 0.9f : 0f),
                    States.Move),
                Ai.Option("Idle", Consideration.Constant(0.1f), States.Idle)
            ],
            hysteresis: 0f,
            minCommitSeconds: 0f,
            tieEpsilon: 0.0001f);
    }

    [DominatusState("Move")]
    private static IEnumerator<AiStep> Move(AiCtx context)
    {
        context.Bb.Set(SelectedAction, "move");
        yield return Ai.Succeed();
    }

    [DominatusState("Idle")]
    private static IEnumerator<AiStep> Idle(AiCtx context)
    {
        context.Bb.Set(
            SelectedAction,
            context.Bb.GetOrDefault(ShouldBuySeed, false) ? "buy-seed" : "idle");
        yield return Ai.Succeed();
    }

    public static GameIntent Decide(AgentObservation observation)
    {
        var agent = new AiAgent(Definition.CreateBrain());
        agent.Bb.Set(CurrentLocation, observation.Location.Value);
        agent.Bb.Set(Destination, observation.ScheduledDestination.Value);
        agent.Bb.Set(HasReachedAnchor, observation.HasReachedAnchor);
        agent.Bb.Set(ShouldBuySeed, observation.ShouldBuySeed);

        var world = new AiWorld();
        world.Add(agent);
        var runner = new SequentialAurelianDominatusWorldRunner();

        for (ulong tick = 0; tick < 8; tick++)
        {
            runner.RunTickAsync(
                    world,
                    new AurelianRuntimeTickInput(tick, TimeSpan.FromMilliseconds(10)))
                .GetAwaiter()
                .GetResult();

            string selected = agent.Bb.GetOrDefault(SelectedAction, string.Empty);
            if (selected == "move")
            {
                return new NavigateToAnchorIntent(observation.ScheduledAnchor);
            }

            if (selected == "idle")
            {
                return new LookIntent();
            }


            if (selected == "buy-seed")
            {
                return new BuyProductIntent(TinyFarmIds.TurnipSeed);
            }
        }

        throw new InvalidOperationException("Dominatus did not produce a bounded NPC decision.");
    }

    private static bool NeedsMove(AiAgent agent)
    {
        return agent.Bb.GetOrDefault(CurrentLocation, string.Empty) !=
               agent.Bb.GetOrDefault(Destination, string.Empty)
            || !agent.Bb.GetOrDefault(HasReachedAnchor, false);
    }
}

public static class TinyFarmNpcController
{
    internal static IReadOnlyList<IntentEnvelope> ObserveDecideAndSubmit(
        TinyFarmState state,
        IReadOnlyList<GameEvent> recentEvents,
        long firstSequence,
        int observationMinute,
        TinyFarmSceneCatalog scenes,
        TinyFarmScheduleCatalog schedules,
        TinyFarmNpcSchedule.Runtime scheduleRuntime)
    {
        var envelopes = new List<IntentEnvelope>();
        long sequence = firstSequence;

        foreach (ActorState actor in state.Actors
                     .Where(candidate => !candidate.IsPlayer)
                     .OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal))
        {
            SceneAnchorId? currentAnchor = CurrentAnchor(state, actor, scenes, schedules);
            int energy = state.Version >= TinyFarmState.EnergySaveVersion
                ? state.EnergyFor(actor.Id).Energy
                : TinyFarmEnergy.MaximumUnits;
            SceneAnchorId scheduledAnchor = TinyFarmNpcSchedule.Decide(
                scheduleRuntime,
                actor.Id,
                observationMinute,
                currentAnchor,
                energy: energy).SelectedAnchor;
            SceneAnchorDefinition anchor = scenes.GetAnchor(scheduledAnchor);
            LocationId destination = anchor.SemanticLocation
                ?? throw new InvalidDataException($"NPC schedule anchor '{scheduledAnchor}' has no semantic location.");
            bool hasReachedAnchor = HasReachedAnchor(state, actor.Id, anchor);
            var observation = new AgentObservation(
                actor.Id,
                actor.Location,
                state.Actors
                    .Where(other => other.Id != actor.Id && other.Location == actor.Location)
                    .Select(other => other.Id)
                    .OrderBy(id => id.Value, StringComparer.Ordinal)
                    .ToArray(),
                actor.Inventory.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(),
                observationMinute,
                recentEvents.Select(gameEvent => gameEvent.Kind).ToArray(),
                destination,
                scheduledAnchor,
                hasReachedAnchor,
                actor.Id == TinyFarmIds.Mara
                    && observationMinute / 1440 + 1 == 6
                    && actor.Location == TinyFarmIds.GeneralStore
                    && state.ShopStock.Any(stock => stock.Product == TinyFarmIds.TurnipSeed && stock.Count > 0)
                    && actor.Money >= 2);

            GameIntent intent = state.Version >= TinyFarmState.EnergySaveVersion
                && hasReachedAnchor
                && TinyFarmAnchorIds.IsHomeBed(scheduledAnchor)
                && !state.EnergyFor(actor.Id).IsResting
                    ? new AnchorReachedIntent(scheduledAnchor)
                    : TinyFarmNpcFlow.Decide(observation);
            envelopes.Add(new IntentEnvelope(
                actor.Id,
                intent,
                state.Minute,
                sequence++,
                IntentSourceKind.Dominatus));
        }

        return envelopes;
    }

    public static SceneAnchorId? CurrentAnchor(
        TinyFarmState state,
        ActorState actor,
        TinyFarmSceneCatalog scenes,
        TinyFarmScheduleCatalog schedules)
    {
        IEnumerable<SceneAnchorId> candidateAnchors = schedules.Candidates
            .Select(candidate => candidate.Anchor)
            .Distinct();
        if (state.Version >= TinyFarmState.EnergySaveVersion)
        {
            ActorSceneState placement = state.ActorScene(actor.Id);
            return candidateAnchors
                .Select(scenes.GetAnchor)
                .Where(anchor => anchor.Scene == placement.Scene
                    && placement.WorldPosition.SquaredDistance(anchor.Position)
                        <= (long)anchor.ArrivalRadiusUnits * anchor.ArrivalRadiusUnits)
                .OrderBy(anchor => anchor.Id.Value, StringComparer.Ordinal)
                .Select(anchor => (SceneAnchorId?)anchor.Id)
                .FirstOrDefault();
        }

        return candidateAnchors
            .Where(candidate => scenes.GetAnchor(candidate).SemanticLocation == actor.Location)
            .OrderBy(candidate => candidate.Value, StringComparer.Ordinal)
            .Select(candidate => (SceneAnchorId?)candidate)
            .FirstOrDefault();
    }

    public static LocationId ScheduledDestination(
        ActorId actor,
        int minute,
        TinyFarmSceneCatalog scenes,
        TinyFarmScheduleCatalog schedules)
    {
        SceneAnchorId anchor = ScheduledAnchor(actor, minute, schedules);
        return scenes.GetAnchor(anchor).SemanticLocation
            ?? throw new InvalidDataException($"NPC schedule anchor for '{actor}' has no semantic location.");
    }

    public static SceneAnchorId ScheduledAnchor(
        ActorId actor,
        int minute,
        TinyFarmScheduleCatalog schedules)
    {
        return TinyFarmNpcSchedule.Decide(schedules, actor, minute).SelectedAnchor;
    }

    private static bool HasReachedAnchor(
        TinyFarmState state,
        ActorId actor,
        SceneAnchorDefinition anchor)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion)
        {
            return state.Actor(actor).Location == anchor.SemanticLocation;
        }

        ActorSceneState placement = state.ActorScene(actor);
        long radiusSquared = (long)anchor.ArrivalRadiusUnits * anchor.ArrivalRadiusUnits;
        return placement.Scene == anchor.Scene
            && placement.WorldPosition.SquaredDistance(anchor.Position) <= radiusSquared;
    }
}
