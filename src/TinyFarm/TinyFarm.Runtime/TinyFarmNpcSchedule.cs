using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace TinyFarm.Core;

public readonly record struct TinyFarmScheduleDecision(
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

public readonly record struct TinyFarmUtilityScore(
    SceneAnchorId Candidate,
    double BaseScore,
    double StickinessContribution,
    double EnergyContribution,
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

    private static readonly BbKey<string> SelectedAnchor = new("TinyFarm.Schedule.SelectedAnchor");
    private static readonly BbKey<TinyFarmScheduleCatalog> ScheduleCatalog = new("TinyFarm.Schedule.Catalog");
    private static readonly BbKey<TinyFarmScheduleWindow> ActiveWindow = new("TinyFarm.Schedule.Window");
    private static readonly BbKey<string> CurrentAnchor = new("TinyFarm.Schedule.CurrentAnchor");
    private static readonly BbKey<EnergyObservation> Energy = new("TinyFarm.Schedule.Energy");

    private static readonly UtilityOption[] ScheduleOptions = CreateScheduleOptions();
    private static readonly Decide ScheduleDecisionStep = Ai.Decide(
        new DecisionSlot(ScheduleDecisionSlot),
        ScheduleOptions,
        hysteresis: 0f,
        minCommitSeconds: 0f,
        tieEpsilon: 0f);
    private static readonly ConditionalWeakTable<TinyFarmScheduleCatalog, Runtime> Runtimes = new();
    private static long requiredDecisions;
    private static long openUtilityDecisions;

    public static FlowDefinition Definition { get; } = Define();

    [DominatusFlow("tiny-farm.npc-schedule-goal", KeepRootFrame = true)]
    public static partial FlowDefinition Define();

    [DominatusState("ChooseScheduleGoal", Root = true)]
    private static IEnumerator<AiStep> ChooseScheduleGoal(AiCtx context)
    {
        while (true)
        {
            yield return ScheduleDecisionStep;
        }
    }

    [DominatusState("SelectFarmHome")]
    private static IEnumerator<AiStep> SelectFarmHome(AiCtx context)
    {
        TinyFarmScheduleCatalog catalog = context.Bb.GetOrDefault(ScheduleCatalog, null!);
        TinyFarmScheduleWindow window = context.Bb.GetOrDefault(ActiveWindow, null!);
        if (catalog is not null && window is not null)
        {
            foreach (TinyFarmUtilityCandidate candidate in catalog.CandidatesFor(window))
            {
                if (candidate.ConsiderationKind == "energy-rest")
                {
                    return Select(context, candidate.Anchor);
                }
            }
        }
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

    internal static Runtime CreateRuntime(TinyFarmScheduleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new Runtime(catalog);
    }

    internal static TinyFarmScheduleDecision Decide(
        Runtime runtime,
        ActorId actor,
        int minute,
        SceneAnchorId? currentAnchor = null,
        bool includeTrace = false,
        int energy = TinyFarmEnergy.MaximumUnits)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return DecideCore(runtime.Catalog, actor, minute, currentAnchor, includeTrace, energy, runtime);
    }

    public static TinyFarmScheduleDecision Decide(
        TinyFarmScheduleCatalog catalog,
        ActorId actor,
        int minute,
        SceneAnchorId? currentAnchor = null,
        bool includeTrace = false,
        int energy = TinyFarmEnergy.MaximumUnits)
    {
        return DecideCore(catalog, actor, minute, currentAnchor, includeTrace, energy, null);
    }

    private static TinyFarmScheduleDecision DecideCore(
        TinyFarmScheduleCatalog catalog,
        ActorId actor,
        int minute,
        SceneAnchorId? currentAnchor,
        bool includeTrace,
        int energy,
        Runtime? suppliedRuntime)
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
        ArraySegment<TinyFarmUtilityCandidate> candidates = catalog.CandidatesFor(window);
        SceneAnchorId expectedAnchor = SelectExpectedAnchor(candidates, currentAnchor, energy);
        Runtime runtime = suppliedRuntime
            ?? Runtimes.GetValue(catalog, static value => new Runtime(value));
        SceneAnchorId anchor = runtime.Decide(actor, window, currentAnchor, energy, expectedAnchor);
        if (expectedAnchor != anchor)
        {
            throw new InvalidOperationException(
                $"Dominatus selected schedule anchor '{anchor}', but Open window '{window.Id}' expected '{expectedAnchor}'.");
        }
        IReadOnlyList<TinyFarmUtilityScore> scores = includeTrace
            ? MaterializeScores(candidates, currentAnchor, energy, anchor)
            : Array.Empty<TinyFarmUtilityScore>();
        return CreateDecision(window, actor, minute, anchor, scores);
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
            TinyFarmScheduleCatalog catalog = agent.Bb.GetOrDefault(ScheduleCatalog, null!)
                ?? throw new InvalidOperationException("TinyFarm schedule catalog was not observed.");
            TinyFarmScheduleWindow window = agent.Bb.GetOrDefault(ActiveWindow, null!)
                ?? throw new InvalidOperationException("TinyFarm active schedule window was not observed.");
            string currentAnchor = agent.Bb.GetOrDefault(CurrentAnchor, string.Empty);
            int energy = agent.Bb.GetOrDefault(Energy, null!)?.Value
                ?? TinyFarmEnergy.MaximumUnits;
            return Score(catalog, window, currentAnchor, energy, anchor);
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
        TinyFarmScheduleWindow window,
        string currentAnchor,
        int energy,
        SceneAnchorId anchor)
    {
        if (window.Regime != TinyFarmScheduleRegime.Open)
        {
            return 0f;
        }

        ArraySegment<TinyFarmUtilityCandidate> candidates = catalog.CandidatesFor(window);
        for (int index = 0; index < candidates.Count; index++)
        {
            TinyFarmUtilityCandidate candidate = candidates[index];
            bool personalBedOption = anchor == TinyFarmAnchorIds.FarmHome
                && candidate.ConsiderationKind == "energy-rest";
            if (candidate.Anchor == anchor || personalBedOption)
            {
                return (float)CandidateScore(
                    candidate,
                    currentAnchor.Length == 0 ? null : new SceneAnchorId(currentAnchor),
                    energy);
            }
        }

        return 0f;
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

    private static SceneAnchorId SelectExpectedAnchor(
        ArraySegment<TinyFarmUtilityCandidate> candidates,
        SceneAnchorId? currentAnchor,
        int energy)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("An Open schedule decision requires at least one candidate.");
        }

        TinyFarmUtilityCandidate winner = candidates[0];
        double winningScore = CandidateScore(winner, currentAnchor, energy);
        for (int index = 1; index < candidates.Count; index++)
        {
            TinyFarmUtilityCandidate candidate = candidates[index];
            double candidateScore = CandidateScore(candidate, currentAnchor, energy);
            if (candidateScore > winningScore
                || (candidateScore == winningScore
                    && CandidateOptionOrder(candidate) < CandidateOptionOrder(winner)))
            {
                winner = candidate;
                winningScore = candidateScore;
            }
        }

        return winner.Anchor;
    }

    private static int CandidateOptionOrder(TinyFarmUtilityCandidate candidate)
    {
        return candidate.ConsiderationKind == "energy-rest"
            ? AnchorOptionOrder(TinyFarmAnchorIds.FarmHome)
            : AnchorOptionOrder(candidate.Anchor);
    }

    private static TinyFarmUtilityScore[] MaterializeScores(
        ArraySegment<TinyFarmUtilityCandidate> candidates,
        SceneAnchorId? currentAnchor,
        int energy,
        SceneAnchorId selectedAnchor)
    {
        var scores = new TinyFarmUtilityScore[candidates.Count];
        for (int index = 0; index < candidates.Count; index++)
        {
            TinyFarmUtilityCandidate candidate = candidates[index];
            double stickiness = candidate.Anchor == currentAnchor ? candidate.CurrentLocationBonus : 0d;
            double energyContribution = candidate.ConsiderationKind == "energy-rest"
                ? TinyFarmEnergy.RestContribution(energy)
                : 0d;
            scores[index] = new TinyFarmUtilityScore(
                candidate.Anchor,
                candidate.BaseScore,
                stickiness,
                energyContribution,
                candidate.BaseScore + stickiness + energyContribution,
                candidate.Anchor == selectedAnchor,
                candidate.ConsiderationKind);
        }

        return scores;
    }

    private static double CandidateScore(
        TinyFarmUtilityCandidate candidate,
        SceneAnchorId? currentAnchor,
        int energy)
    {
        double energyContribution = candidate.ConsiderationKind == "energy-rest"
            ? TinyFarmEnergy.RestContribution(energy)
            : 0d;
        return candidate.BaseScore
            + (candidate.Anchor == currentAnchor ? candidate.CurrentLocationBonus : 0d)
            + energyContribution;
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
        if (anchor == TinyFarmAnchorIds.EliasHomeBed) return 5;
        if (anchor == TinyFarmAnchorIds.MaraHomeBed) return 6;
        if (anchor == TinyFarmAnchorIds.SelaHomeBed) return 7;
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

    internal sealed class Runtime
    {
        private readonly TinyFarmScheduleCatalog _catalog;
        private readonly ConcurrentDictionary<ActorId, ActorScheduleRuntime> _actors = new();

        internal Runtime(TinyFarmScheduleCatalog catalog)
        {
            _catalog = catalog;
        }

        internal TinyFarmScheduleCatalog Catalog => _catalog;

        internal SceneAnchorId Decide(
            ActorId actor,
            TinyFarmScheduleWindow window,
            SceneAnchorId? currentAnchor,
            int energy,
            SceneAnchorId expectedAnchor)
        {
            ActorScheduleRuntime runtime = _actors.GetOrAdd(
                actor,
                static (actorId, catalog) => new ActorScheduleRuntime(actorId, catalog),
                _catalog);
            return runtime.Decide(window, currentAnchor, energy, expectedAnchor);
        }
    }

    private sealed class ActorScheduleRuntime
    {
        private readonly ActorId _actor;
        private readonly TinyFarmScheduleCatalog _catalog;
        private AiWorld _world = null!;
        private AiAgent _agent = null!;
        private readonly object _gate = new();
        private readonly EnergyObservation _energy = new();
        private SceneAnchorId? _lastExpectedAnchor;

        public ActorScheduleRuntime(ActorId actor, TinyFarmScheduleCatalog catalog)
        {
            _actor = actor;
            _catalog = catalog;
            ResetAgent();
        }

        public SceneAnchorId Decide(
            TinyFarmScheduleWindow window,
            SceneAnchorId? currentAnchor,
            int energy,
            SceneAnchorId expectedAnchor)
        {
            lock (_gate)
            {
                string currentAnchorValue = currentAnchor?.Value ?? string.Empty;
                bool decisionInvalidated = _lastExpectedAnchor != expectedAnchor;
                if (decisionInvalidated && _lastExpectedAnchor is not null)
                {
                    ResetAgent();
                }
                _agent.Bb.Set(ActiveWindow, window);
                _agent.Bb.Set(CurrentAnchor, currentAnchorValue);
                _energy.Value = energy;
                if (decisionInvalidated)
                {
                    _agent.Bb.Set(SelectedAnchor, string.Empty);
                    _lastExpectedAnchor = expectedAnchor;
                }
                for (int tick = 0; tick < 8; tick++)
                {
                    _agent.Tick(_world);
                    string selected = _agent.Bb.GetOrDefault(SelectedAnchor, string.Empty);
                    if (selected == expectedAnchor.Value)
                    {
                        return new SceneAnchorId(selected);
                    }
                }

                throw new InvalidOperationException(
                    $"Dominatus did not produce expected anchor '{expectedAnchor}' for actor '{_actor}' "
                    + $"in window '{window.Id}' from current anchor '{currentAnchorValue}' at Energy {energy}; "
                    + $"last selected '{_agent.Bb.GetOrDefault(SelectedAnchor, string.Empty)}'.");
            }
        }

        private void ResetAgent()
        {
            _world = new AiWorld();
            _agent = new AiAgent(Definition.CreateBrain());
            _agent.Bb.Set(ScheduleCatalog, _catalog);
            _agent.Bb.Set(Energy, _energy);
            _world.Add(_agent);
        }
    }

    private sealed class EnergyObservation
    {
        public int Value { get; set; } = TinyFarmEnergy.MaximumUnits;
    }
}
