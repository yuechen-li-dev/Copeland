namespace TinyFarm.Core;

public sealed record TinyFarmStepResult(
    TinyFarmState State,
    IReadOnlyList<IntentResult> Results,
    IReadOnlyList<NarrativeLine> Narrative);

public sealed class TinyFarmSession
{
    private readonly TinyFarmResolver resolver;
    private long nextSequence;
    private IReadOnlyList<GameEvent> recentEvents;

    public TinyFarmSession(TinyFarmState state)
        : this(state, 0, [])
    {
    }

    internal TinyFarmSession(
        TinyFarmState state,
        long nextSequence,
        IReadOnlyList<GameEvent> recentEvents)
    {
        State = state.DeepCopy();
        this.nextSequence = nextSequence;
        this.recentEvents = recentEvents.ToArray();
        resolver = new TinyFarmResolver();
    }

    public TinyFarmState State { get; private set; }

    public long NextSequence => nextSequence;

    public IReadOnlyList<GameEvent> RecentEvents => recentEvents;

    public TinyFarmStepResult Step(GameIntent humanIntent)
    {
        ArgumentNullException.ThrowIfNull(humanIntent);

        int observationMinute = State.Minute;
        if (humanIntent is WaitIntent wait && wait.Minutes > 0 && wait.Minutes <= 240)
        {
            observationMinute += wait.Minutes;
        }

        var envelopes = new List<IntentEnvelope>
        {
            new(
                TinyFarmIds.Player,
                humanIntent,
                State.Minute,
                nextSequence++,
                IntentSourceKind.Human)
        };

        IReadOnlyList<IntentEnvelope> npcIntents = TinyFarmNpcController.ObserveDecideAndSubmit(
            State,
            recentEvents,
            nextSequence,
            observationMinute);
        envelopes.AddRange(npcIntents);
        nextSequence += npcIntents.Count;

        ResolutionBatchResult batch = resolver.Resolve(State, envelopes);
        State = batch.State;
        recentEvents = batch.Results.SelectMany(result => result.Events).ToArray();
        IReadOnlyList<NarrativeLine> narrative = TinyFarmNarrative.Project(recentEvents);
        return new TinyFarmStepResult(State.DeepCopy(), batch.Results, narrative);
    }

    public TinyFarmSave CaptureSave()
    {
        return new TinyFarmSave(
            "tiny-farm-m1@1",
            State.DeepCopy(),
            new TinyFarmRuntimeSave(nextSequence, recentEvents.ToList()),
            new TinyFarmAgentSave("dominatus-1.0.0", "schedule decisions are observation-pure"),
            new TinyFarmNarrativeSave("ariadne-1.0.0", "surface prose is derived from semantic dialogue topics"));
    }
}
