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
    LocationId ScheduledDestination);

public static partial class TinyFarmNpcFlow
{
    private static readonly BbKey<string> CurrentLocation = new("TinyFarm.Observation.Location");
    private static readonly BbKey<string> Destination = new("TinyFarm.Observation.Destination");
    private static readonly BbKey<string> SelectedAction = new("TinyFarm.Decision.Action");

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
        context.Bb.Set(SelectedAction, "idle");
        yield return Ai.Succeed();
    }

    public static GameIntent Decide(AgentObservation observation)
    {
        var agent = new AiAgent(Definition.CreateBrain());
        agent.Bb.Set(CurrentLocation, observation.Location.Value);
        agent.Bb.Set(Destination, observation.ScheduledDestination.Value);

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
                return new MoveIntent(NextStep(observation.Location, observation.ScheduledDestination));
            }

            if (selected == "idle")
            {
                return new LookIntent();
            }
        }

        throw new InvalidOperationException("Dominatus did not produce a bounded NPC decision.");
    }

    private static bool NeedsMove(AiAgent agent)
    {
        return agent.Bb.GetOrDefault(CurrentLocation, string.Empty) !=
               agent.Bb.GetOrDefault(Destination, string.Empty);
    }

    private static LocationId NextStep(LocationId current, LocationId destination)
    {
        if (current == destination)
        {
            return current;
        }

        if (TinyFarmContent.Location(current).Exits.Contains(destination))
        {
            return destination;
        }

        return TinyFarmIds.TownSquare;
    }
}

public static class TinyFarmNpcController
{
    public static IReadOnlyList<IntentEnvelope> ObserveDecideAndSubmit(
        TinyFarmState state,
        IReadOnlyList<GameEvent> recentEvents,
        long firstSequence,
        int observationMinute)
    {
        var envelopes = new List<IntentEnvelope>();
        long sequence = firstSequence;

        foreach (ActorState actor in state.Actors
                     .Where(candidate => !candidate.IsPlayer)
                     .OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal))
        {
            LocationId destination = ScheduledDestination(actor.Id, observationMinute);
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
                destination);

            GameIntent intent = TinyFarmNpcFlow.Decide(observation);
            envelopes.Add(new IntentEnvelope(
                actor.Id,
                intent,
                state.Minute,
                sequence++,
                IntentSourceKind.Dominatus));
        }

        return envelopes;
    }

    private static LocationId ScheduledDestination(ActorId actor, int minute)
    {
        int minuteOfDay = minute % (24 * 60);

        if (actor == TinyFarmIds.Mara)
        {
            if (minuteOfDay < 12 * 60)
            {
                return TinyFarmIds.TownSquare;
            }

            return minuteOfDay < 17 * 60
                ? TinyFarmIds.Riverside
                : TinyFarmIds.Farmhouse;
        }

        if (actor == TinyFarmIds.Elias)
        {
            return minuteOfDay >= 12 * 60 && minuteOfDay < 18 * 60
                ? TinyFarmIds.Riverside
                : TinyFarmIds.Farmhouse;
        }

        return minuteOfDay >= 8 * 60 && minuteOfDay < 18 * 60
            ? TinyFarmIds.GeneralStore
            : TinyFarmIds.Farmhouse;
    }
}
