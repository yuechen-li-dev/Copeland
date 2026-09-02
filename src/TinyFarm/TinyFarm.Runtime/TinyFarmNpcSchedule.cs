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

public sealed record TinyFarmScheduleWindow(
    ActorId Actor,
    int? Day,
    int FromMinute,
    int ToMinute,
    SceneAnchorId Anchor,
    int Priority,
    string Reason);

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

    private static readonly TinyFarmScheduleWindow[] ScheduleWindows =
    [
        new(TinyFarmIds.Mara, null, 0, 720, TinyFarmAnchorIds.TownSquare, 0, "daily-morning-town"),
        new(TinyFarmIds.Mara, null, 720, 1020, TinyFarmAnchorIds.RiversideMeetingPoint, 0, "daily-afternoon-riverside"),
        new(TinyFarmIds.Mara, null, 1020, 1440, TinyFarmAnchorIds.FarmHome, 0, "daily-evening-home"),
        new(TinyFarmIds.Mara, 6, 540, 1020, TinyFarmAnchorIds.StoreCounter, 1, "day-6-store"),
        new(TinyFarmIds.Mara, 7, 600, 1020, TinyFarmAnchorIds.RiversideMeetingPoint, 1, "day-7-riverside"),
        new(TinyFarmIds.Elias, null, 0, 720, TinyFarmAnchorIds.FarmWorkArea, 0, "daily-morning-work"),
        new(TinyFarmIds.Elias, null, 720, 1080, TinyFarmAnchorIds.RiversideMeetingPoint, 0, "daily-afternoon-riverside"),
        new(TinyFarmIds.Elias, null, 1080, 1440, TinyFarmAnchorIds.FarmWorkArea, 0, "daily-evening-work"),
        new(TinyFarmIds.Sela, null, 0, 480, TinyFarmAnchorIds.FarmHome, 0, "daily-morning-home"),
        new(TinyFarmIds.Sela, null, 480, 1080, TinyFarmAnchorIds.StoreCounter, 0, "daily-store"),
        new(TinyFarmIds.Sela, null, 1080, 1440, TinyFarmAnchorIds.FarmHome, 0, "daily-evening-home")
    ];

    private static readonly UtilityOption[] ScheduleOptions = CreateScheduleOptions();

    public static FlowDefinition Definition { get; } = Define();

    public static IReadOnlyList<TinyFarmScheduleWindow> Windows => ScheduleWindows;

    [DominatusFlow("tiny-farm.npc-schedule-goal")]
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

    public static TinyFarmScheduleDecision Decide(ActorId actor, int minute)
    {
        if (minute < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), minute, "Schedule time cannot be negative.");
        }

        EnsureSupportedActor(actor);

        var agent = new AiAgent(Definition.CreateBrain());
        agent.Bb.Set(Actor, actor.Value);
        agent.Bb.Set(AbsoluteMinute, minute);

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

            string selected = agent.Bb.GetOrDefault(SelectedAnchor, string.Empty);
            if (selected.Length == 0)
            {
                continue;
            }

            var anchor = new SceneAnchorId(selected);
            TinyFarmScheduleWindow window = SelectWindow(actor, minute);
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
                window.FromMinute,
                window.ToMinute,
                window.Priority);
        }

        throw new InvalidOperationException(
            $"Dominatus did not produce a bounded schedule decision for actor '{actor}' at minute {minute}.");
    }

    private static Consideration ScoreFor(SceneAnchorId anchor)
    {
        return new Consideration((_, agent) =>
        {
            var actor = new ActorId(agent.Bb.GetOrDefault(Actor, string.Empty));
            int minute = agent.Bb.GetOrDefault(AbsoluteMinute, -1);
            return Score(actor, minute, anchor);
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

    private static float Score(ActorId actor, int minute, SceneAnchorId anchor)
    {
        int day = minute / MinutesPerDay + 1;
        int minuteOfDay = minute % MinutesPerDay;
        int activePriority = int.MinValue;
        bool anchorMatches = false;
        foreach (TinyFarmScheduleWindow window in ScheduleWindows)
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

    private static TinyFarmScheduleWindow SelectWindow(ActorId actor, int minute)
    {
        int day = minute / MinutesPerDay + 1;
        int minuteOfDay = minute % MinutesPerDay;
        TinyFarmScheduleWindow? winner = null;
        foreach (TinyFarmScheduleWindow window in ScheduleWindows)
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

    private static void EnsureSupportedActor(ActorId actor)
    {
        if (!ScheduleWindows.Any(window => window.Actor == actor))
        {
            throw new KeyNotFoundException($"No TinyFarm NPC schedule is registered for actor '{actor}'.");
        }
    }

    private static bool Matches(
        TinyFarmScheduleWindow window,
        ActorId actor,
        int day,
        int minuteOfDay)
    {
        return window.Actor == actor
            && (window.Day is null || window.Day == day)
            && minuteOfDay >= window.FromMinute
            && minuteOfDay < window.ToMinute;
    }

    private static IEnumerator<AiStep> Select(AiCtx context, SceneAnchorId anchor)
    {
        context.Bb.Set(SelectedAnchor, anchor.Value);
        yield return Ai.Succeed();
    }
}
