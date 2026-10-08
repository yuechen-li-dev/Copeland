using Aurelian.Ariadne.VnDemo;
using Aurelian.Composition;
using Aurelian.GameHost;
using Ariadne.OptFlow.Presentation;
using Deliverance.Core;
using Deliverance.Core.Modules;
using Deliverance.Core.Storage;
using InputMan.Aurelian;
using InputMan.Core;
using Machina.Presentation;
using TinyFarm.InputMan;
using TinyFarm.Presentation;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmDialogueM7B2Tests
{
    [Fact]
    public void InteractWithMara_StartsAriadneAndProjectsLineChoiceConditionalAndConsequence()
    {
        (TinyFarmSimulationHost host, TinyFarmDialogueCoordinator dialogue) = CreateConversation(hasWildMint: true);

        Assert.Equal(TinyFarmMaraDialogue.DialogueId, dialogue.Presentation!.DialogueId);
        Assert.Equal("mara.greeting", dialogue.Presentation.OperationId);
        Assert.Equal("Mara", dialogue.Presentation.SpeakerId);
        Assert.True(dialogue.Presentation.CanAdvance);

        dialogue.Apply(TinyFarmDialogueAction.Advance);
        Assert.Equal("mara.shared-weather", dialogue.Presentation!.OperationId);
        dialogue.Apply(TinyFarmDialogueAction.Advance);
        Assert.Equal("mara.mint-notice", dialogue.Presentation!.OperationId);
        dialogue.Apply(TinyFarmDialogueAction.Advance);

        Assert.True(dialogue.Presentation!.IsAwaitingChoice);
        Assert.Equal(["give-mint", "keep-mint"], dialogue.Presentation.Choices.Select(choice => choice.Id));
        Assert.Equal([0, 1], dialogue.Presentation.Choices.Select(choice => choice.DeclarationIndex));
        dialogue.Apply(TinyFarmDialogueAction.Confirm);

        Assert.Equal("mara.mint-thanks", dialogue.Presentation!.OperationId);
        Assert.Equal(IntentResultStatus.Accepted, dialogue.LastConsequenceResult!.Status);
        Assert.Equal(1, dialogue.ConsequenceEmissionCount);
        Assert.Equal(TinyFarmIds.Mara, host.Session.State.Item(TinyFarmIds.WildMint).Owner);
        dialogue.Apply(TinyFarmDialogueAction.Advance);
        Assert.False(dialogue.IsActive);
    }

    [Fact]
    public void MissingMint_TakesExplicitConditionalBranchWithoutQuestState()
    {
        (_, TinyFarmDialogueCoordinator dialogue) = CreateConversation(hasWildMint: false);

        dialogue.Apply(TinyFarmDialogueAction.Advance);
        dialogue.Apply(TinyFarmDialogueAction.Advance);

        Assert.Equal("mara.no-mint", dialogue.Presentation!.OperationId);
        dialogue.Apply(TinyFarmDialogueAction.Advance);
        Assert.Equal(["ask-town", "goodbye"], dialogue.Presentation!.Choices.Select(choice => choice.Id));
    }

    [Fact]
    public void RejectedTypedConsequence_IsExplicitAndDoesNotShowSuccessLine()
    {
        (TinyFarmSimulationHost host, TinyFarmDialogueCoordinator dialogue) = CreateConversation(hasWildMint: true);
        AdvanceToChoice(dialogue);
        RemoveMintOwnership(host.Session.State);

        dialogue.Apply(TinyFarmDialogueAction.Confirm);

        Assert.Equal(IntentResultStatus.Rejected, dialogue.LastConsequenceResult!.Status);
        Assert.Equal(IntentReason.ItemNotOwned, dialogue.LastConsequenceResult.Reason);
        Assert.Equal("mara.mint-rejected", dialogue.Presentation!.OperationId);
        Assert.DoesNotContain("mara.mint-thanks", dialogue.Trace);
    }

    [Fact]
    public void DialogueInputMapConsumesConfirmAndSuppressesGameplayMovementAndToolActions()
    {
        using var adapter = new AurelianInputAdapter(new InputManEngine(GameControls.CreateProfile()));
        adapter.SetContexts(GameControls.Dialogue);
        adapter.RecordButton(Controls.Key(KeyboardKey.E), true);
        adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        adapter.RecordButton(Controls.Key(KeyboardKey.Q), true);
        adapter.BeginFrame(new AurelianHostFrame(1, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16)));
        var controller = new TinyFarmInputController();

        Assert.Equal(TinyFarmDialogueAction.Confirm, controller.MapDialogue(adapter.CurrentFrame));
        Assert.Empty(controller.Map(adapter.CurrentFrame));
    }

    [Fact]
    public void MachinaDialogueOverlay_IsOpaqueAndStableFramesReusePreparedProjection()
    {
        (_, TinyFarmDialogueCoordinator dialogue) = CreateConversation(hasWildMint: true);
        TinyFarmPresentationSnapshot snapshot = PresentationSnapshot(dialogue.Presentation!);
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);

        layer.Receive(new LayerMessage<TinyFarmPresentationSnapshot>(
            TinyFarmMachinaUiLayer.ApplicationId,
            TinyFarmMachinaUiLayer.Id,
            snapshot));
        for (int frame = 0; frame < 120; frame++)
        {
            layer.Receive(new LayerMessage<TinyFarmPresentationSnapshot>(
                TinyFarmMachinaUiLayer.ApplicationId,
                TinyFarmMachinaUiLayer.Id,
                snapshot));
        }

        Assert.Equal(LayerInputPolicy.Opaque, layer.Describe().InputPolicy);
        Assert.Contains(layer.Prepared.PresentationFrame.Operations, operation =>
            operation is FillRectangleOperation fill
            && fill.SourceId == "tiny-farm.dialogue.panel");
        Assert.Equal(1, layer.CacheMetrics.TopologyBuildCount);
        Assert.Equal(0, layer.CacheMetrics.DynamicUpdateCount);
    }

    [Fact]
    public async Task DeliveranceSaveAtPendingLine_RestoresWorldAndProjectionWithoutRedispatch()
    {
        (TinyFarmSimulationHost host, TinyFarmDialogueCoordinator dialogue) = CreateConversation(hasWildMint: true);
        dialogue.Apply(TinyFarmDialogueAction.Advance);
        string worldHash = TinyFarmSemanticHash.Compute(host.Session.State);
        var store = new MemoryStore();
        var writer = new TinyFarmDeliverancePersistence(host, LoadDefinitions(), store, dialogue: dialogue);
        await writer.Deliverance.SaveAsync("line", writer.CaptureSave("line"));

        (TinyFarmSimulationHost restoredHost, TinyFarmDialogueCoordinator restoredDialogue) = CreateHostOnly(hasWildMint: false);
        var reader = new TinyFarmDeliverancePersistence(restoredHost, LoadDefinitions(), store, dialogue: restoredDialogue);
        LoadedSaveCandidate candidate = await reader.Deliverance.LoadAsync(
            "line",
            reader.GetLoadDefinitions("line"),
            reader.GetLoadCompatibility("line"));
        reader.CommitLoadedCandidate("line", candidate);

        Assert.Equal(worldHash, TinyFarmSemanticHash.Compute(restoredHost.Session.State));
        Assert.Equal("mara.shared-weather", restoredDialogue.Presentation!.OperationId);
        Assert.Equal(0, restoredDialogue.DialogueDispatchCount);
    }

    [Fact]
    public async Task DeliveranceSaveAtPendingChoice_RestoresOrderSelectionAndDoesNotReplayEffect()
    {
        (TinyFarmSimulationHost host, TinyFarmDialogueCoordinator dialogue) = CreateConversation(hasWildMint: true);
        AdvanceToChoice(dialogue);
        dialogue.Apply(TinyFarmDialogueAction.ChoiceDown);
        string worldHash = TinyFarmSemanticHash.Compute(host.Session.State);
        var store = new MemoryStore();
        var writer = new TinyFarmDeliverancePersistence(host, LoadDefinitions(), store, dialogue: dialogue);
        await writer.Deliverance.SaveAsync("choice", writer.CaptureSave("choice"));

        (TinyFarmSimulationHost restoredHost, TinyFarmDialogueCoordinator restoredDialogue) = CreateHostOnly(hasWildMint: false);
        var reader = new TinyFarmDeliverancePersistence(restoredHost, LoadDefinitions(), store, dialogue: restoredDialogue);
        LoadedSaveCandidate candidate = await reader.Deliverance.LoadAsync(
            "choice",
            reader.GetLoadDefinitions("choice"),
            reader.GetLoadCompatibility("choice"));
        reader.CommitLoadedCandidate("choice", candidate);

        Assert.Equal(worldHash, TinyFarmSemanticHash.Compute(restoredHost.Session.State));
        Assert.Equal("mara.mint-choice", restoredDialogue.Presentation!.OperationId);
        Assert.Equal(1, restoredDialogue.Presentation.SelectedChoiceIndex);
        Assert.Equal(["give-mint", "keep-mint"], restoredDialogue.Presentation.Choices.Select(choice => choice.Id));
        Assert.Equal(0, restoredDialogue.ConsequenceEmissionCount);
        restoredDialogue.Apply(TinyFarmDialogueAction.ChoiceUp);
        restoredDialogue.Apply(TinyFarmDialogueAction.Confirm);
        Assert.Equal(1, restoredDialogue.ConsequenceEmissionCount);
    }

    [Fact]
    public async Task SaveAfterCompletedEffect_DoesNotReplayEffectAndFocusReturnsAfterCompletion()
    {
        (TinyFarmSimulationHost host, TinyFarmDialogueCoordinator dialogue) = CreateConversation(hasWildMint: true);
        AdvanceToChoice(dialogue);
        dialogue.Apply(TinyFarmDialogueAction.Confirm);
        Assert.Equal("mara.mint-thanks", dialogue.Presentation!.OperationId);
        Assert.Equal(1, dialogue.ConsequenceEmissionCount);
        string worldHash = TinyFarmSemanticHash.Compute(host.Session.State);
        var store = new MemoryStore();
        var writer = new TinyFarmDeliverancePersistence(host, LoadDefinitions(), store, dialogue: dialogue);
        await writer.Deliverance.SaveAsync("post-effect", writer.CaptureSave("post-effect"));

        (TinyFarmSimulationHost restoredHost, TinyFarmDialogueCoordinator restored) = CreateHostOnly(hasWildMint: false);
        var reader = new TinyFarmDeliverancePersistence(restoredHost, LoadDefinitions(), store, dialogue: restored);
        LoadedSaveCandidate candidate = await reader.Deliverance.LoadAsync(
            "post-effect",
            reader.GetLoadDefinitions("post-effect"),
            reader.GetLoadCompatibility("post-effect"));
        reader.CommitLoadedCandidate("post-effect", candidate);

        Assert.Equal(worldHash, TinyFarmSemanticHash.Compute(restoredHost.Session.State));
        Assert.Equal("mara.mint-thanks", restored.Presentation!.OperationId);
        Assert.Equal(0, restored.ConsequenceEmissionCount);
        restored.Apply(TinyFarmDialogueAction.Advance);
        Assert.False(restored.IsActive);

        var layer = new TinyFarmMachinaUiLayer(new RecordingSink(), new LayerSurfaceDescriptor(1280, 720));
        layer.Receive(new LayerMessage<TinyFarmPresentationSnapshot>(
            TinyFarmMachinaUiLayer.ApplicationId,
            TinyFarmMachinaUiLayer.Id,
            PresentationSnapshot(dialogue: null)));
        Assert.Equal(LayerInputPolicy.HitTest, layer.Describe().InputPolicy);
    }

    [Fact]
    public void SemanticDialogueInputReplay_ProducesSameTraceConsequenceAndFinalWorldHash()
    {
        (TinyFarmSimulationHost firstHost, TinyFarmDialogueCoordinator first) = CreateConversation(hasWildMint: true);
        TinyFarmDialogueAction[] actions =
        [
            TinyFarmDialogueAction.Advance,
            TinyFarmDialogueAction.Advance,
            TinyFarmDialogueAction.Advance,
            TinyFarmDialogueAction.Confirm,
            TinyFarmDialogueAction.Advance,
        ];
        foreach (TinyFarmDialogueAction action in actions)
        {
            first.Apply(action);
        }

        (TinyFarmSimulationHost replayHost, TinyFarmDialogueCoordinator replay) = CreateConversation(hasWildMint: true);
        foreach (TinyFarmDialogueInputRecord record in first.InputTape)
        {
            replay.Apply(record.Action);
        }

        Assert.Equal(first.Trace, replay.Trace);
        Assert.Equal(first.LastConsequenceResult!.Status, replay.LastConsequenceResult!.Status);
        Assert.Equal(TinyFarmSemanticHash.Compute(firstHost.Session.State), TinyFarmSemanticHash.Compute(replayHost.Session.State));
    }

    [Fact]
    public void VnAndTinyFarmConsumeExactSameNeutralSnapshotTypeWithoutSkinFields()
    {
        using var vn = new VnSession();
        (_, TinyFarmDialogueCoordinator tinyFarm) = CreateConversation(hasWildMint: true);

        Assert.IsType<DialoguePresentationSnapshot>(vn.Presentation);
        Assert.IsType<DialoguePresentationSnapshot>(tinyFarm.Presentation);
        string[] fields = typeof(DialoguePresentationSnapshot).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("BackgroundKey", fields);
        Assert.DoesNotContain("PortraitKey", fields);
        Assert.DoesNotContain("AutoEnabled", fields);
        Assert.DoesNotContain("SaveEnabled", fields);

        string[] sharedDependencies = typeof(DialoguePresentationSnapshot)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(sharedDependencies, name => name.StartsWith("Machina", StringComparison.Ordinal));
        Assert.DoesNotContain(sharedDependencies, name => name.StartsWith("Aurelian", StringComparison.Ordinal));
    }

    [Fact]
    public void StableProjectionRead_HasNoSteadyStateAllocation()
    {
        (_, TinyFarmDialogueCoordinator dialogue) = CreateConversation(hasWildMint: true);
        _ = dialogue.Presentation;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10_000; index++)
        {
            _ = dialogue.Presentation;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static (TinyFarmSimulationHost Host, TinyFarmDialogueCoordinator Dialogue) CreateConversation(bool hasWildMint)
    {
        (TinyFarmSimulationHost host, TinyFarmDialogueCoordinator dialogue) = CreateHostOnly(hasWildMint);
        TinyFarmStepResult interaction = host.ExecuteIntent(new InteractIntent());
        Assert.True(dialogue.TryBeginFrom(interaction));
        return (host, dialogue);
    }

    private static (TinyFarmSimulationHost Host, TinyFarmDialogueCoordinator Dialogue) CreateHostOnly(bool hasWildMint)
    {
        TinyFarmDefinitions definitions = LoadDefinitions();
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        ActorSceneState mara = state.ActorScene(TinyFarmIds.Mara);
        int playerSceneIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[playerSceneIndex] = new ActorSceneState(
            TinyFarmIds.Player,
            mara.Scene,
            mara.WorldPosition,
            mara.Facing);
        int playerIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[playerIndex] = state.MutableActors[playerIndex] with
        {
            Location = TinyFarmIds.TownSquare,
            Inventory = hasWildMint ? [TinyFarmIds.WildMint] : []
        };
        int mintIndex = state.MutableItems.FindIndex(item => item.Id == TinyFarmIds.WildMint);
        state.MutableItems[mintIndex] = state.MutableItems[mintIndex] with
        {
            Owner = hasWildMint ? TinyFarmIds.Player : null,
            GroundLocation = hasWildMint ? null : TinyFarmIds.Riverside,
            GroundScene = null,
            GroundPosition = null
        };
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(state, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        return (host, new TinyFarmDialogueCoordinator(host));
    }

    private static void AdvanceToChoice(TinyFarmDialogueCoordinator dialogue)
    {
        dialogue.Apply(TinyFarmDialogueAction.Advance);
        dialogue.Apply(TinyFarmDialogueAction.Advance);
        dialogue.Apply(TinyFarmDialogueAction.Advance);
        Assert.Equal("mara.mint-choice", dialogue.Presentation!.OperationId);
    }

    private static void RemoveMintOwnership(TinyFarmState state)
    {
        int playerIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[playerIndex] = state.MutableActors[playerIndex] with { Inventory = [] };
        int mintIndex = state.MutableItems.FindIndex(item => item.Id == TinyFarmIds.WildMint);
        state.MutableItems[mintIndex] = state.MutableItems[mintIndex] with
        {
            Owner = null,
            GroundLocation = TinyFarmIds.Riverside
        };
    }

    private static TinyFarmPresentationSnapshot PresentationSnapshot(DialoguePresentationSnapshot? dialogue)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(state, definitions);
        return new TinyFarmPresentationSnapshot(
            TinyFarmPlayerUiProjector.Project(state, definitions),
            frame.Day,
            frame.Time,
            frame.CurrentLocationName,
            TinyFarmSimulationMode.Playing,
            false,
            "Talking",
            frame.InteractionHints,
            [],
            dialogue);
    }

    private static TinyFarmDefinitions LoadDefinitions() => TinyFarmDefinitionLoader.Load();

    private sealed class RecordingSink : ILayerApplicationMessageSink
    {
        public void Publish<TPayload>(LayerMessage<TPayload> message)
        {
        }
    }

    private sealed class MemoryStore : ISaveStore
    {
        private readonly Dictionary<string, byte[]> slots = new(StringComparer.Ordinal);

        public Task<bool> ExistsAsync(string slotId, CancellationToken ct = default)
            => Task.FromResult(slots.ContainsKey(slotId));

        public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(slots.Keys.Order().ToArray());

        public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default)
            => Task.FromResult<SlotInfo?>(slots.TryGetValue(slotId, out byte[]? bytes)
                ? new SlotInfo(slotId, null, bytes.Length)
                : null);

        public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SlotInfo>>(slots.Select(item =>
                new SlotInfo(item.Key, null, item.Value.Length)).ToArray());

        public Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default)
            => Task.FromResult<ReadOnlyMemory<byte>>(slots[slotId]);

        public Task WriteSlotAsync(
            string slotId,
            ReadOnlyMemory<byte> bytes,
            int keepBackups,
            CancellationToken ct = default)
        {
            slots[slotId] = bytes.ToArray();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string slotId, CancellationToken ct = default)
        {
            slots.Remove(slotId);
            return Task.CompletedTask;
        }
    }
}
