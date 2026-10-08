using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmEngineTests
{
    private readonly TinyFarmResolver resolver = new();

    [Fact]
    public void Move_AcceptsAdjacentLocation_WithoutMutatingInput()
    {
        TinyFarmState original = TinyFarmContent.CreateInitialState();

        ResolutionBatchResult result = resolver.Resolve(
            original,
            [Envelope(TinyFarmIds.Player, new MoveIntent(TinyFarmIds.Riverside))]);

        Assert.Equal(IntentResultStatus.Accepted, result.Results.Single().Status);
        Assert.Equal(TinyFarmIds.Riverside, result.State.Actor(TinyFarmIds.Player).Location);
        Assert.Equal(TinyFarmIds.TownSquare, original.Actor(TinyFarmIds.Player).Location);
    }

    [Fact]
    public void Move_RejectsNonAdjacentLocation()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        MoveActor(state, TinyFarmIds.Player, TinyFarmIds.Farmhouse);

        IntentResult result = resolver.Resolve(
            state,
            [Envelope(TinyFarmIds.Player, new MoveIntent(TinyFarmIds.GeneralStore))])
            .Results.Single();

        Assert.Equal(IntentResultStatus.Rejected, result.Status);
        Assert.Equal(IntentReason.NotAdjacent, result.Reason);
    }

    [Fact]
    public void Take_AcceptsPresentItem_AndRejectsSecondAttempt()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        MoveActor(state, TinyFarmIds.Player, TinyFarmIds.Riverside);
        IntentEnvelope first = Envelope(TinyFarmIds.Player, new TakeIntent(TinyFarmIds.WildMint), sequence: 0);
        IntentEnvelope second = Envelope(TinyFarmIds.Mara, new TakeIntent(TinyFarmIds.WildMint), sequence: 1);
        MoveActor(state, TinyFarmIds.Mara, TinyFarmIds.Riverside);

        ResolutionBatchResult result = resolver.Resolve(state, [first, second]);

        Assert.Equal(IntentResultStatus.Accepted, result.Results[0].Status);
        Assert.Equal(IntentReason.ItemAbsent, result.Results[1].Reason);
        Assert.Contains(TinyFarmIds.WildMint, result.State.Actor(TinyFarmIds.Player).Inventory);
    }

    [Fact]
    public void TalkAndGive_AdvanceFavorThroughSemanticState()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        ResolutionBatchResult accepted = resolver.Resolve(
            state,
            [Envelope(TinyFarmIds.Player, new TalkIntent(TinyFarmIds.Mara))]);
        TinyFarmState atRiverside = accepted.State;
        MoveActor(atRiverside, TinyFarmIds.Player, TinyFarmIds.Riverside);
        MoveActor(atRiverside, TinyFarmIds.Elias, TinyFarmIds.Riverside);

        ResolutionBatchResult delivered = resolver.Resolve(
            atRiverside,
            [Envelope(TinyFarmIds.Player, new GiveIntent(TinyFarmIds.Letter, TinyFarmIds.Elias))]);

        Assert.Equal(FavorStage.LetterDelivered, delivered.State.Favor);
        Assert.Contains(WorldFact.EliasHasLetter, delivered.State.Facts);
        Assert.Contains(
            delivered.Results.Single().Events,
            gameEvent => gameEvent.Dialogue == DialogueTopic.EliasReceivesLetter);
    }

    [Fact]
    public void Give_RejectsItemActorDoesNotOwn()
    {
        IntentResult result = resolver.Resolve(
            TinyFarmContent.CreateInitialState(),
            [Envelope(TinyFarmIds.Player, new GiveIntent(TinyFarmIds.Apple, TinyFarmIds.Mara))])
            .Results.Single();

        Assert.Equal(IntentResultStatus.Rejected, result.Status);
        Assert.Equal(IntentReason.ItemNotOwned, result.Reason);
    }

    [Fact]
    public void BuyAndSell_TransferInventoryAndMoneyThroughResolver()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        state.Minute = 9 * 60;
        MoveActor(state, TinyFarmIds.Player, TinyFarmIds.GeneralStore);

        TinyFarmState bought = resolver.Resolve(
            state,
            [Envelope(TinyFarmIds.Player, new BuyIntent(TinyFarmIds.Apple))])
            .State;
        TinyFarmState sold = resolver.Resolve(
            bought,
            [Envelope(TinyFarmIds.Player, new SellIntent(TinyFarmIds.Apple))])
            .State;

        Assert.Equal(10, sold.Actor(TinyFarmIds.Player).Money);
        Assert.Equal(TinyFarmIds.Sela, sold.Item(TinyFarmIds.Apple).Owner);
    }

    [Fact]
    public void Buy_RejectsInsufficientFunds()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        state.Minute = 9 * 60;
        ActorState player = state.Actor(TinyFarmIds.Player);
        ReplaceActor(state, player with { Location = TinyFarmIds.GeneralStore, Money = 1 });

        IntentResult result = resolver.Resolve(
            state,
            [Envelope(TinyFarmIds.Player, new BuyIntent(TinyFarmIds.FishingRod))])
            .Results.Single();

        Assert.Equal(IntentReason.InsufficientFunds, result.Reason);
    }

    [Fact]
    public void Wait_UsesGameTime_AndRejectsOutOfRangeDuration()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        IntentEnvelope accepted = Envelope(TinyFarmIds.Player, new WaitIntent(60), sequence: 0);
        IntentEnvelope rejected = Envelope(TinyFarmIds.Player, new WaitIntent(241), sequence: 1);

        ResolutionBatchResult result = resolver.Resolve(state, [accepted, rejected]);

        Assert.Equal(9 * 60, result.State.Minute);
        Assert.Equal(IntentResultStatus.Accepted, result.Results[0].Status);
        Assert.Equal(IntentReason.InvalidWait, result.Results[1].Reason);
    }

    [Fact]
    public void HumanAndDominatusEnvelopes_UseSameResolverAndOrdering()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        MoveActor(state, TinyFarmIds.Player, TinyFarmIds.Riverside);
        MoveActor(state, TinyFarmIds.Mara, TinyFarmIds.Riverside);
        var human = new IntentEnvelope(
            TinyFarmIds.Player,
            new TakeIntent(TinyFarmIds.WildMint),
            state.Minute,
            0,
            IntentSourceKind.Human);
        var npc = new IntentEnvelope(
            TinyFarmIds.Mara,
            new TakeIntent(TinyFarmIds.WildMint),
            state.Minute,
            0,
            IntentSourceKind.Dominatus);

        ResolutionBatchResult result = resolver.Resolve(state, [human, npc]);

        Assert.Equal(TinyFarmIds.Mara, result.Results[0].Envelope.Actor);
        Assert.Equal(IntentResultStatus.Accepted, result.Results[0].Status);
        Assert.Equal(IntentResultStatus.Rejected, result.Results[1].Status);
    }

    [Fact]
    public void NpcWaitStep_ProducesAutonomousMovesFromBoundedObservations()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateInitialState());

        TinyFarmStepResult result = session.Step(new WaitIntent(240));

        int autonomousMoves = result.Results
            .Where(item => item.Envelope.Source == IntentSourceKind.Dominatus)
            .SelectMany(item => item.Events)
            .Count(gameEvent => gameEvent.Kind == GameEventKind.ActorMoved);
        Assert.Equal(2, autonomousMoves);
    }

    [Fact]
    public void DominatusFlow_IsGeneratedFromThreeAuthoredStates()
    {
        var inspection = TinyFarmNpcFlow.Definition.Inspect();

        Assert.Equal("tiny-farm.npc-schedule", inspection.Id);
        Assert.Equal(3, inspection.States.Count);
        Assert.Equal("Choose", inspection.Root.Value);
    }

    [Fact]
    public void AriadneFlow_ProjectsSemanticTopic_WithoutChangingWorldState()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        string before = TinyFarmSemanticHash.Compute(state);
        var semanticEvent = new GameEvent(
            GameEventKind.Conversation,
            TinyFarmIds.Player,
            TinyFarmIds.Mara,
            Dialogue: DialogueTopic.RequestLetterDelivery);

        IReadOnlyList<NarrativeLine> lines = TinyFarmNarrative.Project([semanticEvent]);

        Assert.Single(lines);
        Assert.Equal("Mara", lines[0].Speaker);
        Assert.Contains("letter", lines[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(state));
        Assert.Single(TinyFarmNarrative.Definition.Inspect().States);
    }

    [Fact]
    public void SaveReload_ContinuesToSameSemanticHashAndResultSequence()
    {
        CanonicalRun uninterrupted = TinyFarmCanonicalScenario.Run(reloadAt: null);
        CanonicalRun reloaded = TinyFarmCanonicalScenario.Run(reloadAt: 8);

        Assert.Equal(uninterrupted.FinalHash, reloaded.FinalHash);
        Assert.Equal(uninterrupted.ResultSequence, reloaded.ResultSequence);
    }

    [Fact]
    public void SemanticHash_IsIndependentOfCollectionOrder()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        TinyFarmState reordered = state.DeepCopy();
        reordered.MutableActors.Reverse();
        reordered.MutableItems.Reverse();
        reordered.MutableFacts.Reverse();

        Assert.Equal(
            TinyFarmSemanticHash.Compute(state),
            TinyFarmSemanticHash.Compute(reordered));
    }

    [Fact]
    public void CanonicalDay_ProvesAllM1DeterminismChecks()
    {
        TinyFarmM1Proof proof = TinyFarmCanonicalScenario.Prove();

        Assert.Equal("A", proof.Outcome);
        Assert.True(proof.RepeatedRunMatches);
        Assert.True(proof.SaveReloadMatches);
        Assert.True(proof.ResultSequenceMatches);
        Assert.Equal("mara", proof.ConflictWinner);
        Assert.Equal(1, proof.ConflictRejected);
        Assert.True(proof.AriadneLines > 0);
    }

    private static IntentEnvelope Envelope(ActorId actor, GameIntent intent, long sequence = 0)
    {
        return new IntentEnvelope(actor, intent, 8 * 60, sequence, IntentSourceKind.Human);
    }

    private static void MoveActor(TinyFarmState state, ActorId actorId, LocationId location)
    {
        ActorState actor = state.Actor(actorId);
        ReplaceActor(state, actor with { Location = location });
    }

    private static void ReplaceActor(TinyFarmState state, ActorState actor)
    {
        int index = state.MutableActors.FindIndex(candidate => candidate.Id == actor.Id);
        state.MutableActors[index] = actor;
    }
}
