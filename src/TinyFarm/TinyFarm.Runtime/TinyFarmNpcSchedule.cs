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
    int Priority,
    string WindowId,
    TinyFarmScheduleRegime Regime,
    IReadOnlyList<TinyFarmUtilityScore> UtilityScores);

public sealed record TinyFarmUtilityScore(
    SceneAnchorId Candidate,
    double Score,
    bool Selected,
    string ConsiderationKind);

public readonly record struct TinyFarmScheduleExecutionCounts(
    long RequiredDecisions,
    long OpenUtilityDecisions);

public static partial class TinyFarmNpcSchedule
{
    public const string ScheduleDecisionSlot = "TinyFarm.NpcSchedule.Anchor";

    private const int MinutesPerDay = 24 * 60;

    private static readonly BbKey<string> Actor = new("TinyFarm.Schedule.Actor");
    private static readonly BbKey<int> AbsoluteMinute = new("TinyFarm.Schedule.Minute");
    private static readonly BbKey<string> SelectedAnchor = new("TinyFarm.Schedule.SelectedAnchor");
    private static readonly BbKey<TinyFarmScheduleCatalog> ScheduleCatalog = new("TinyFarm.Schedule.Catalog");
    private static readonly BbKey<string> ActiveWindowId = new("TinyFarm.Schedule.WindowId");
    private static readonly BbKey<string> CurrentAnchor = new("TinyFarm.Schedule.CurrentAnchor");

    private static readonly UtilityOption[] ScheduleOptions = CreateScheduleOptions();
    private static readonly ConditionalWeakTable<TinyFarmScheduleCatalog, ScheduleRuntime> Runtimes = new();
    private static long requiredDecisions;
    private static long openUtilityDecisions;

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
        int minute,
        SceneAnchorId? currentAnchor = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (minute < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), minute, "Schedule time cannot be negative.");
        }

        _ = catalog.ForActor(actor);

        TinyFarmScheduleWindow window = SelectWindow(catalog, actor, minute);
        if (window.Regime == TinyFarmScheduleRegime.Required)
        {
            Interlocked.Increment(ref requiredDecisions);
            SceneAnchorId requiredAnchor = window.RequiredAnchor
                ?? throw new InvalidOperationException($"Required window '{window.Id}' has no anchor.");
            return CreateDecision(window, actor, minute, requiredAnchor, []);
        }

        Interlocked.Increment(ref openUtilityDecisions);
        IReadOnlyList<TinyFarmUtilityCandidate> candidates = catalog.CandidatesFor(window);
        IReadOnlyList<TinyFarmUtilityScore> scores = candidates
            .Select(candidate => new TinyFarmUtilityScore(
                candidate.Anchor,
                CandidateScore(candidate, currentAnchor),
                false,
                candidate.ConsiderationKind))
            .ToArray();
        SceneAnchorId expectedAnchor = scores
            .OrderByDescending(score => score.Score)
            .ThenBy(score => AnchorOptionOrder(score.Candidate))
            .First()
            .Candidate;
        ScheduleRuntime runtime = Runtimes.GetValue(catalog, static value => new ScheduleRuntime(value));
        SceneAnchorId anchor = runtime.Decide(actor, minute, window.Id, currentAnchor, expectedAnchor);
        if (expectedAnchor != anchor)
        {
            throw new InvalidOperationException(
                $"Dominatus selected schedule anchor '{anchor}', but Open window '{window.Id}' expected '{expectedAnchor}'.");
        }
        return CreateDecision(
            window,
            actor,
            minute,
            anchor,
            scores.Select(score => score with { Selected = score.Candidate == anchor }).ToArray());
    }

    public static TinyFarmScheduleExecutionCounts ExecutionCounts => new(
        Interlocked.Read(ref requiredDecisions),
        Interlocked.Read(ref openUtilityDecisions));

    public static void ResetExecutionCounts()
    {
        Interlocked.Exchange(ref requiredDecisions, 0);
        Interlocked.Exchange(ref openUtilityDecisions, 0);
    }

    private static Consideration ScoreFor(SceneAnchorId anchor)
    {
        return new Consideration((_, agent) =>
        {
            var actor = new ActorId(agent.Bb.GetOrDefault(Actor, string.Empty));
            int minute = agent.Bb.GetOrDefault(AbsoluteMinute, -1);
            TinyFarmScheduleCatalog catalog = agent.Bb.GetOrDefault(ScheduleCatalog, null!)
                ?? throw new InvalidOperationException("TinyFarm schedule catalog was not observed.");
            string windowId = agent.Bb.GetOrDefault(ActiveWindowId, string.Empty);
            string currentAnchor = agent.Bb.GetOrDefault(CurrentAnchor, string.Empty);
            return Score(catalog, actor, minute, windowId, currentAnchor, anchor);
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
        string windowId,
        string currentAnchor,
        SceneAnchorId anchor)
    {
        TinyFarmScheduleWindow window = SelectWindow(catalog, actor, minute);
        if (window.Regime != TinyFarmScheduleRegime.Open || window.Id != windowId)
        {
            return 0f;
        }
        TinyFarmUtilityCandidate? candidate = catalog.CandidatesFor(window)
            .SingleOrDefault(item => item.Anchor == anchor);
        return candidate is null
            ? 0f
            : (float)CandidateScore(
                candidate,
                currentAnchor.Length == 0 ? null : new SceneAnchorId(currentAnchor));
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

            if (window.Priority == winner.Priority && window.Id != winner.Id)
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

    private static TinyFarmScheduleDecision CreateDecision(
        TinyFarmScheduleWindow window,
        ActorId actor,
        int minute,
        SceneAnchorId anchor,
        IReadOnlyList<TinyFarmUtilityScore> scores)
    {
        return new TinyFarmScheduleDecision(
            actor,
            minute,
            ScheduleDecisionSlot,
            anchor,
            window.Reason,
            window.StartMinute,
            window.EndMinuteExclusive,
            window.Priority,
            window.Id,
            window.Regime,
            scores);
    }

    private static double CandidateScore(
        TinyFarmUtilityCandidate candidate,
        SceneAnchorId? currentAnchor)
    {
        return candidate.BaseScore
            + (candidate.Anchor == currentAnchor ? candidate.CurrentLocationBonus : 0d);
    }

    private static int AnchorOptionOrder(SceneAnchorId anchor)
    {
        if (anchor == TinyFarmAnchorIds.FarmHome)
        {
            return 0;
        }
        if (anchor == TinyFarmAnchorIds.FarmWorkArea)
        {
            return 1;
        }
        if (anchor == TinyFarmAnchorIds.TownSquare)
        {
            return 2;
        }
        if (anchor == TinyFarmAnchorIds.StoreCounter)
        {
            return 3;
        }
        if (anchor == TinyFarmAnchorIds.RiversideMeetingPoint)
        {
            return 4;
        }
        throw new InvalidOperationException($"No Dominatus schedule option exists for anchor '{anchor}'.");
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

        public SceneAnchorId Decide(
            ActorId actor,
            int minute,
            string windowId,
            SceneAnchorId? currentAnchor,
            SceneAnchorId expectedAnchor)
        {
            lock (_gate)
            {
                AiAgent agent = GetOrCreateAgent(actor);
                agent.Bb.Set(Actor, actor.Value);
                agent.Bb.Set(AbsoluteMinute, minute);
                agent.Bb.Set(ScheduleCatalog, _catalog);
                agent.Bb.Set(ActiveWindowId, windowId);
                agent.Bb.Set(CurrentAnchor, currentAnchor?.Value ?? string.Empty);
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
