using System.Runtime.CompilerServices;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace TinyFarm.Core;

public sealed record TinyFarmScheduleDecision(
    ActorId Actor,
    int Minute,
    string DecisionSlot,
    SceneAnchorId SelectedAnchor,
    string Reason,
    int WindowStart,
    int WindowEnd,
    int Priority);

public static partial class TinyFarmNpcSchedule
{
    public const string ScheduleDecisionSlot = "TinyFarm.NpcSchedule.Anchor";

    private const int MinutesPerDay = 24 * 60;

    private static readonly BbKey<string> Actor = new("TinyFarm.Schedule.Actor");
    private static readonly BbKey<int> AbsoluteMinute = new("TinyFarm.Schedule.Minute");
    private static readonly BbKey<string> SelectedAnchor = new("TinyFarm.Schedule.SelectedAnchor");
    private static readonly BbKey<TinyFarmScheduleCatalog> ScheduleCatalog = new("TinyFarm.Schedule.Catalog");

    private static readonly UtilityOption[] ScheduleOptions = CreateScheduleOptions();
    private static readonly ConditionalWeakTable<TinyFarmScheduleCatalog, ScheduleRuntime> Runtimes = new();

    public static FlowDefinition Definition { get; } = Define();

    [DominatusFlow("tiny-farm.npc-schedule-goal", KeepRootFrame = true)]
    public static partial FlowDefinition Define();

    [DominatusState("ChooseScheduleGoal", Root = true)]
    private static IEnumerator<AiStep> ChooseScheduleGoal(AiCtx context)
    {
        yield return Ai.Decide(
            new DecisionSlot(ScheduleDecisionSlot),
            ScheduleOptions,
            hysteresis: 0f,
            minCommitSeconds: 0f,
            tieEpsilon: 0f);
    }

    [DominatusState("SelectFarmHome")]
    private static IEnumerator<AiStep> SelectFarmHome(AiCtx context)
    {
        return Select(context, TinyFarmAnchorIds.FarmHome);
    }

    [DominatusState("SelectFarmWorkArea")]
    private static IEnumerator<AiStep> SelectFarmWorkArea(AiCtx context)
    {
        return Select(context, TinyFarmAnchorIds.FarmWorkArea);
    }

    [DominatusState("SelectTownSquare")]
    private static IEnumerator<AiStep> SelectTownSquare(AiCtx context)
    {
        return Select(context, TinyFarmAnchorIds.TownSquare);
    }

    [DominatusState("SelectStoreCounter")]
    private static IEnumerator<AiStep> SelectStoreCounter(AiCtx context)
    {
        return Select(context, TinyFarmAnchorIds.StoreCounter);
    }

    [DominatusState("SelectRiversideMeetingPoint")]
    private static IEnumerator<AiStep> SelectRiversideMeetingPoint(AiCtx context)
    {
        return Select(context, TinyFarmAnchorIds.RiversideMeetingPoint);
    }

    public static TinyFarmScheduleDecision Decide(
        TinyFarmScheduleCatalog catalog,
        ActorId actor,
        int minute)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (minute < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), minute, "Schedule time cannot be negative.");
        }

        _ = catalog.ForActor(actor);

        TinyFarmScheduleWindow window = SelectWindow(catalog, actor, minute);
        ScheduleRuntime runtime = Runtimes.GetValue(catalog, static value => new ScheduleRuntime(value));
        SceneAnchorId anchor = runtime.Decide(actor, minute, window.Anchor);
        if (window.Anchor != anchor)
        {
            throw new InvalidOperationException(
                $"Dominatus selected schedule anchor '{anchor}', but the active authored window is '{window.Anchor}'.");
        }

        return new TinyFarmScheduleDecision(
            actor,
            minute,
            ScheduleDecisionSlot,
            anchor,
            window.Reason,
            window.StartMinute,
            window.EndMinuteExclusive,
            window.Priority);
    }

    private static Consideration ScoreFor(SceneAnchorId anchor)
    {
        return new Consideration((_, agent) =>
        {
            var actor = new ActorId(agent.Bb.GetOrDefault(Actor, string.Empty));
            int minute = agent.Bb.GetOrDefault(AbsoluteMinute, -1);
            TinyFarmScheduleCatalog catalog = agent.Bb.GetOrDefault(ScheduleCatalog, null!)
                ?? throw new InvalidOperationException("TinyFarm schedule catalog was not observed.");
            return Score(catalog, actor, minute, anchor);
        });
    }

    private static UtilityOption[] CreateScheduleOptions()
    {
        return
        [
            Ai.Option(
                TinyFarmAnchorIds.FarmHome.Value,
                ScoreFor(TinyFarmAnchorIds.FarmHome),
                States.SelectFarmHome),
            Ai.Option(
                TinyFarmAnchorIds.FarmWorkArea.Value,
                ScoreFor(TinyFarmAnchorIds.FarmWorkArea),
                States.SelectFarmWorkArea),
            Ai.Option(
                TinyFarmAnchorIds.TownSquare.Value,
                ScoreFor(TinyFarmAnchorIds.TownSquare),
                States.SelectTownSquare),
            Ai.Option(
                TinyFarmAnchorIds.StoreCounter.Value,
                ScoreFor(TinyFarmAnchorIds.StoreCounter),
                States.SelectStoreCounter),
            Ai.Option(
                TinyFarmAnchorIds.RiversideMeetingPoint.Value,
                ScoreFor(TinyFarmAnchorIds.RiversideMeetingPoint),
                States.SelectRiversideMeetingPoint)
        ];
    }

    private static float Score(
        TinyFarmScheduleCatalog catalog,
        ActorId actor,
        int minute,
        SceneAnchorId anchor)
    {
        int day = minute / MinutesPerDay + 1;
        int minuteOfDay = minute % MinutesPerDay;
        int activePriority = int.MinValue;
        bool anchorMatches = false;
        foreach (TinyFarmScheduleWindow window in catalog.ForActor(actor))
        {
            if (!Matches(window, actor, day, minuteOfDay))
            {
                continue;
            }

            if (window.Priority > activePriority)
            {
                activePriority = window.Priority;
                anchorMatches = window.Anchor == anchor;
            }
            else if (window.Priority == activePriority && window.Anchor == anchor)
            {
                anchorMatches = true;
            }
        }

        return activePriority != int.MinValue && anchorMatches ? 1f : 0f;
    }

    private static TinyFarmScheduleWindow SelectWindow(
        TinyFarmScheduleCatalog catalog,
        ActorId actor,
        int minute)
    {
        int day = minute / MinutesPerDay + 1;
        int minuteOfDay = minute % MinutesPerDay;
        TinyFarmScheduleWindow? winner = null;
        foreach (TinyFarmScheduleWindow window in catalog.ForActor(actor))
        {
            if (!Matches(window, actor, day, minuteOfDay))
            {
                continue;
            }

            if (winner is null || window.Priority > winner.Priority)
            {
                winner = window;
                continue;
            }

            if (window.Priority == winner.Priority && window.Anchor != winner.Anchor)
            {
                throw new InvalidOperationException(
                    $"Schedule windows tie for actor '{actor}' at minute {minute} and priority {window.Priority}.");
            }
        }

        if (winner is null)
        {
            throw new InvalidOperationException(
                $"No schedule window covers actor '{actor}' at minute {minute}.");
        }

        return winner;
    }

    private static bool Matches(
        TinyFarmScheduleWindow window,
        ActorId actor,
        int day,
        int minuteOfDay)
    {
        return window.Actor == actor
            && window.Day.Matches(day)
            && minuteOfDay >= window.StartMinute
            && minuteOfDay < window.EndMinuteExclusive;
    }

    private static IEnumerator<AiStep> Select(AiCtx context, SceneAnchorId anchor)
    {
        context.Bb.Set(SelectedAnchor, anchor.Value);
        yield return Ai.Succeed();
    }

    private sealed class ScheduleRuntime
    {
        private readonly TinyFarmScheduleCatalog _catalog;
        private readonly AiWorld _world = new();
        private readonly Dictionary<ActorId, AiAgent> _agents = [];
        private readonly object _gate = new();

        public ScheduleRuntime(TinyFarmScheduleCatalog catalog)
        {
            _catalog = catalog;
        }

        public SceneAnchorId Decide(ActorId actor, int minute, SceneAnchorId expectedAnchor)
        {
            lock (_gate)
            {
                AiAgent agent = GetOrCreateAgent(actor);
                agent.Bb.Set(Actor, actor.Value);
                agent.Bb.Set(AbsoluteMinute, minute);
                agent.Bb.Set(ScheduleCatalog, _catalog);
                for (int tick = 0; tick < 8; tick++)
                {
                    agent.Tick(_world);
                    string selected = agent.Bb.GetOrDefault(SelectedAnchor, string.Empty);
                    if (selected == expectedAnchor.Value)
                    {
                        return new SceneAnchorId(selected);
                    }
                }

                throw new InvalidOperationException(
                    $"Dominatus did not produce a bounded schedule decision for actor '{actor}' at minute {minute}.");
            }
        }

        private AiAgent GetOrCreateAgent(ActorId actor)
        {
            if (_agents.TryGetValue(actor, out AiAgent? existing))
            {
                return existing;
            }

            var agent = new AiAgent(Definition.CreateBrain());
            _world.Add(agent);
            _agents.Add(actor, agent);
            return agent;
        }
    }
}
