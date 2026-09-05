using System.Text.Json;
using System.Text.Json.Serialization;
using Deliverance.Core;
using Deliverance.Core.Codecs;
using Deliverance.Core.Modules;
using Deliverance.Core.Storage;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmSaveReplayM6Tests
{
    [Fact]
    public async Task DeliveranceSave_DestroyAndLoad_RestoresExactMidActionSemanticState()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        var originalHost = new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmM14ControlStates.Create(definitions, "wander"), definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        originalHost.Session.Step(new LookIntent());
        originalHost.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
        Assert.True(originalHost.Session.HasActiveNpcNavigation);
        string expectedHash = TinyFarmSemanticHash.Compute(originalHost.Session.State);
        long expectedSequence = originalHost.Session.NextSequence;
        ScenePosition expectedNpcPosition = originalHost.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        var store = new M6MemoryStore();
        var writer = new TinyFarmDeliverancePersistence(originalHost, definitions, store);
        await writer.Deliverance.SaveAsync("mid-action", writer.CaptureSave("mid-action"));

        var restoredHost = new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmM14ControlStates.Create(definitions, "wander"), definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        var reader = new TinyFarmDeliverancePersistence(restoredHost, definitions, store);
        LoadedSaveCandidate candidate = await reader.Deliverance.LoadAsync(
            "mid-action",
            reader.GetLoadDefinitions("mid-action"),
            reader.GetLoadCompatibility("mid-action"));
        reader.CommitLoadedCandidate("mid-action", candidate);

        Assert.Equal(expectedHash, TinyFarmSemanticHash.Compute(restoredHost.Session.State));
        Assert.Equal(expectedSequence, restoredHost.Session.NextSequence);
        Assert.Equal(expectedNpcPosition, restoredHost.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition);
        Assert.Equal(0, restoredHost.Session.NavigationPlanCount);
        restoredHost.Session.Step(new LookIntent());
        Assert.True(restoredHost.Session.NavigationPlanCount > 0);
    }

    [Fact]
    public async Task SceneInventoryHotbarAndWorldTimeRestore_WhileInputAndCachesDoNot()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        SetPlayerPlacement(state, TinyFarmSceneIds.Farm, new GridPosition(17, 6));
        ResolutionBatchResult transition = new TinyFarmResolver(definitions).Resolve(
            state,
            [new IntentEnvelope(TinyFarmIds.Player, new InteractIntent(), state.Minute, 1, IntentSourceKind.Human)]);
        Assert.Equal(IntentResultStatus.Accepted, transition.Results.Single().Status);
        transition.State.SelectedHotbarSlot = 4;
        transition.State.Minute = 777;
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(transition.State, definitions),
            definitions,
            TinyFarmSimulationMode.Paused);
        host.SetPlayerMovement(1, 0);
        ActorSceneState expectedPlacement = host.Session.State.ActorScene(TinyFarmIds.Player);
        string expectedHash = TinyFarmSemanticHash.Compute(host.Session.State);
        var store = new M6MemoryStore();
        var persistence = new TinyFarmDeliverancePersistence(host, definitions, store);
        await persistence.Deliverance.SaveAsync("transition", persistence.CaptureSave("transition"));

        host.CommitLoadedSession(new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions));
        LoadedSaveCandidate candidate = await persistence.Deliverance.LoadAsync(
            "transition",
            persistence.GetLoadDefinitions("transition"),
            persistence.GetLoadCompatibility("transition"));
        persistence.CommitLoadedCandidate("transition", candidate);

        Assert.Equal(expectedHash, TinyFarmSemanticHash.Compute(host.Session.State));
        Assert.Equal(expectedPlacement, host.Session.State.ActorScene(TinyFarmIds.Player));
        Assert.Equal(4, host.Session.State.SelectedHotbarSlot);
        Assert.Equal(777, host.Session.State.Minute);
        Assert.Equal(0, host.Session.NavigationPlanCount);

        ScenePosition before = host.Session.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(100));
        Assert.Equal(before, host.Session.State.ActorScene(TinyFarmIds.Player).WorldPosition);
    }

    [Fact]
    public void SaveSnapshotType_ExcludesRendererAudioInputNavigationAndSpatialCaches()
    {
        string[] members = typeof(TinyFarmSemanticSaveSnapshot)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            ["ApplicationSaveVersion", "RuntimeVersion", "DefinitionHash", "World", "NextSequence", "RecentEvents", "Dialogue"],
            members);
        Assert.DoesNotContain(members, name => name.Contains("Audio", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, name => name.Contains("Input", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, name => name.Contains("Collider", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, name => name.Contains("Render", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticIntentTape_ReplaysToExactHashAndReportsCompatibilityAndSequenceDivergence()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState initial = TinyFarmM21ControlStates.Create(definitions);
        var resolver = new TinyFarmResolver(definitions);
        IntentEnvelope select = new(TinyFarmIds.Player, new SelectHotbarSlotIntent(new HotbarSlotId(4)), initial.Minute, 1, IntentSourceKind.Human);
        TinyFarmState afterSelect = resolver.Resolve(initial, [select]).State;
        IntentEnvelope attack = new(TinyFarmIds.Player, new AttackIntent(TinyFarmIds.DungeonSlime), afterSelect.Minute, 2, IntentSourceKind.Human);
        TinyFarmState final = resolver.Resolve(afterSelect, [attack]).State;
        var records = new TinyFarmReplayRecord[]
        {
            new(0, select, TinyFarmSemanticHash.Compute(afterSelect)),
            new(1, attack, TinyFarmSemanticHash.Compute(final)),
        };
        const string cadence = "cadence-test";
        TinyFarmReplayEnvelope envelope = TinyFarmSemanticReplay.Create(initial, definitions.Identity, cadence, records);
        byte[] encodedEnvelope = TinyFarmSemanticReplay.Serialize(envelope);
        TinyFarmReplayEnvelope decodedEnvelope = TinyFarmSemanticReplay.Deserialize(encodedEnvelope);
        SelectHotbarSlotIntent decodedSelect = Assert.IsType<SelectHotbarSlotIntent>(decodedEnvelope.Intents[0].Intent.Intent);
        Assert.Equal(new HotbarSlotId(4), decodedSelect.Slot);

        TinyFarmReplayResult replay = TinyFarmSemanticReplay.Replay(decodedEnvelope, definitions, cadence);
        Assert.Equal(TinyFarmSemanticHash.Compute(final), replay.FinalHash);
        Assert.Equal(2, replay.AppliedIntentCount);

        InvalidDataException definition = Assert.Throws<InvalidDataException>(
            () => TinyFarmSemanticReplay.Replay(envelope with { DefinitionHash = "changed" }, definitions, cadence));
        Assert.Contains("definition hash mismatch", definition.Message, StringComparison.OrdinalIgnoreCase);
        InvalidDataException cadenceMismatch = Assert.Throws<InvalidDataException>(
            () => TinyFarmSemanticReplay.Replay(envelope, definitions, "changed"));
        Assert.Contains("cadence hash mismatch", cadenceMismatch.Message, StringComparison.OrdinalIgnoreCase);
        InvalidDataException checkpointMismatch = Assert.Throws<InvalidDataException>(
            () => TinyFarmSemanticReplay.Replay(envelope with { InitialCheckpointHash = "wrong" }, definitions, cadence));
        Assert.Contains("checkpoint hash mismatch", checkpointMismatch.Message, StringComparison.OrdinalIgnoreCase);
        InvalidDataException formatMismatch = Assert.Throws<InvalidDataException>(
            () => TinyFarmSemanticReplay.Replay(envelope with { ReplayFormatVersion = 99 }, definitions, cadence));
        Assert.Contains("replay format", formatMismatch.Message, StringComparison.OrdinalIgnoreCase);
        string unknownIntentJson = System.Text.Encoding.UTF8.GetString(encodedEnvelope)
            .Replace("select-hotbar", "unknown-intent", StringComparison.Ordinal);
        InvalidDataException intentSchemaMismatch = Assert.Throws<InvalidDataException>(
            () => TinyFarmSemanticReplay.Deserialize(System.Text.Encoding.UTF8.GetBytes(unknownIntentJson)));
        Assert.Contains("intent schema mismatch", intentSchemaMismatch.Message, StringComparison.OrdinalIgnoreCase);
        InvalidDataException divergence = Assert.Throws<InvalidDataException>(
            () => TinyFarmSemanticReplay.Replay(
                envelope with { Intents = [records[0] with { ExpectedStateHash = "wrong" }] },
                definitions,
                cadence));
        Assert.Contains("index 0", divergence.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SchemaV1SemanticSnapshot_MigratesToCurrentSchemaAndCommitsExactly()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState expectedState = TinyFarmM21ControlStates.Create(definitions);
        expectedState.Minute = 923;
        var store = new M6MemoryStore();
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(expectedState, definitions),
            definitions,
            TinyFarmSimulationMode.Paused);
        var persistence = new TinyFarmDeliverancePersistence(host, definitions, store);
        TinyFarmSemanticSaveSnapshot current = persistence.CaptureSnapshot();
        var legacy = new TinyFarmSemanticSaveSnapshotV1(
            current.RuntimeVersion,
            current.DefinitionHash,
            current.World,
            current.NextSequence,
            current.RecentEvents);
        var legacyOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };
        byte[] legacyBytes = JsonSerializer.SerializeToUtf8Bytes(legacy, legacyOptions);
        var request = new SaveRequest(
            new SaveApplicationMetadata(
                TinyFarmDeliverancePersistence.ApplicationId,
                TinyFarmDeliverancePersistence.ApplicationVersion,
                DefinitionHash: definitions.Identity,
                CadenceConfigHash: host.CadenceConfigurationIdentity,
                ApplicationSaveVersion: 1),
            [
                new SaveModulePayload(
                    TinyFarmDeliverancePersistence.ModuleId,
                    SchemaVersion: 1,
                    ModuleCriticality.Required,
                    SerializerId: 0,
                    CompressionId: new GzipCodec().Id,
                    legacyBytes),
            ]);
        await persistence.Deliverance.SaveAsync("legacy-v1", request);

        LoadedSaveCandidate candidate = await persistence.Deliverance.LoadAsync(
            "legacy-v1",
            persistence.GetLoadDefinitions("legacy-v1"),
            persistence.GetLoadCompatibility("legacy-v1"));
        persistence.CommitLoadedCandidate("legacy-v1", candidate);

        Assert.Equal(TinyFarmDeliverancePersistence.ModuleSchemaVersion, candidate.GetModule(TinyFarmDeliverancePersistence.ModuleId).SchemaVersion);
        Assert.Equal(923, host.Session.State.Minute);
        Assert.Equal(TinyFarmSemanticHash.Compute(expectedState), TinyFarmSemanticHash.Compute(host.Session.State));
    }

    private static void SetPlayerPlacement(TinyFarmState state, SceneId scene, GridPosition position)
    {
        int placementIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placementIndex] = new ActorSceneState(TinyFarmIds.Player, scene, position);
        ActorState player = state.Actor(TinyFarmIds.Player);
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == TinyFarmIds.Player);
        state.MutableActors[actorIndex] = player with { Location = TinyFarmScenes.LocationForScene(scene) };
    }

    private sealed class M6MemoryStore : ISaveStore
    {
        private readonly Dictionary<string, byte[]> slots = new(StringComparer.Ordinal);

        public Task<bool> ExistsAsync(string slotId, CancellationToken ct = default) => Task.FromResult(slots.ContainsKey(slotId));
        public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(slots.Keys.Order().ToArray());
        public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default) => Task.FromResult<SlotInfo?>(slots.TryGetValue(slotId, out byte[]? bytes) ? new SlotInfo(slotId, null, bytes.Length) : null);
        public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SlotInfo>>(slots.Select(item => new SlotInfo(item.Key, null, item.Value.Length)).ToArray());
        public Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<byte>>(slots[slotId]);

        public Task WriteSlotAsync(string slotId, ReadOnlyMemory<byte> bytes, int keepBackups, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
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
