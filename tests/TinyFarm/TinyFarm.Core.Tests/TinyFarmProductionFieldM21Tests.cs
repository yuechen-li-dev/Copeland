using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Field2D;
using Deliverance.Core;
using Deliverance.Core.Codecs;
using Deliverance.Core.Modules;
using Deliverance.Core.Storage;
using TinyFarm.InputMan;
using TinyFarm.Oblivion;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmProductionFieldM21Tests
{
    [Fact]
    public void DefaultSessionOwnsAuthoredRiversideField()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var first = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);
        var second = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);

        Assert.Equal("TinyFarmSession", first.Field.Definition.SemanticOwner);
        Assert.Equal(TinyFarmSceneIds.Riverside, first.Field.Definition.Scene);
        Assert.Equal(14 * 22, first.Field.Fluid.HeightField.Count);
        Assert.Equal(12 * 20, first.Field.Fluid.LiquidMask.ReadOnlyCells.ToArray().Count(value => value != 0));
        Assert.NotSame(first.Field, second.Field);
        Assert.Equal(first.Field.SemanticHash, second.Field.SemanticHash);
    }

    [Fact]
    public void ExistingHostCadenceIsFramePartitionDeterministicForField()
    {
        TinyFarmSimulationHost first = CreateHost(TinyFarmSimulationMode.Playing);
        TinyFarmSimulationHost second = CreateHost(TinyFarmSimulationMode.Playing);
        first.Session.Field.Energize(WaterPosition());
        second.Session.Field.Energize(WaterPosition());

        for (int index = 0; index < 10; index++)
        {
            first.AdvanceHostTime(TimeSpan.FromMilliseconds(100));
        }
        for (int index = 0; index < 20; index++)
        {
            second.AdvanceHostTime(TimeSpan.FromMilliseconds(50));
        }

        Assert.Equal(60, first.FieldStepsAdvanced);
        Assert.Equal(first.FieldStepsAdvanced, second.FieldStepsAdvanced);
        Assert.Equal(first.Session.Field.SemanticHash, second.Session.Field.SemanticHash);
    }

    [Fact]
    public void WalkingThroughWaterCreatesSemanticDisturbance()
    {
        TinyFarmSimulationHost host = CreateHost(TinyFarmSimulationMode.Playing, placeInWater: true);
        string before = host.Session.Field.SemanticHash;
        host.SetPlayerMovement(1, 0);

        host.AdvanceHostTime(TimeSpan.FromMilliseconds(20));

        Assert.NotEqual(before, host.Session.Field.SemanticHash);
        TinyFarmFieldEvent disturbance = Assert.Single(
            host.Session.Field.RecentEvents,
            item => item.Kind == "movement-disturbance");
        Assert.Equal(FieldDisturbanceShape.Point, disturbance.Shape);
        Assert.True(disturbance.AffectedCells > 0);
    }

    [Fact]
    public void CombatMovesRouteToExistingFieldShapesWithoutRendererPolicy()
    {
        var cases = new[]
        {
            (TinyFarmCombatMoves.SwordSwing, FieldDisturbanceShape.Arc),
            (TinyFarmCombatMoves.SpearThrust, FieldDisturbanceShape.Line),
            (TinyFarmCombatMoves.HammerSmash, FieldDisturbanceShape.Ring),
            (TinyFarmCombatMoves.SweepingHoe, FieldDisturbanceShape.Arc)
        };
        foreach ((Aurelian.Combat.CombatMoveDefinition move, FieldDisturbanceShape expected) in cases)
        {
            TinyFarmFieldRuntime field = TinyFarmFieldRuntime.CreateDefault();
            int motionCells = field.ApplyCombatMove(
                move,
                TinyFarmSceneIds.Riverside,
                WaterPosition(),
                ActorFacing.Right,
                contact: false);
            int contactCells = field.ApplyCombatMove(
                move,
                TinyFarmSceneIds.Riverside,
                WaterPosition(),
                ActorFacing.Right,
                contact: true);

            Assert.True(motionCells > 0);
            Assert.True(contactCells > 0);
            Assert.All(field.RecentEvents, item => Assert.Equal(expected, item.Shape));
            Assert.True(field.Fluid.HeightField.ReadOnlyCells.ToArray().Max() > 0);
        }
    }

    [Fact]
    public void HostedCombatPhasesAdvanceOverRealCadenceInsteadOfSpinLoop()
    {
        TinyFarmDefinitions definitions = Definitions();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        EnemyDefinition enemy = definitions.Enemy(TinyFarmIds.DungeonSlime);
        PlacePlayer(
            state,
            enemy.Scene,
            new ScenePosition(
                enemy.SpawnPosition.XUnits - (ScenePosition.UnitsPerTile / 2),
                enemy.SpawnPosition.YUnits));
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(state, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        int initialHealth = host.Session.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth;

        TinyFarmStepResult accepted = host.ExecuteIntent(new AttackIntent(TinyFarmIds.DungeonSlime));

        Assert.Equal(IntentResultStatus.Accepted, accepted.Results.Single().Status);
        Assert.True(host.Session.HasActiveCombat);
        Assert.Equal(initialHealth, host.Session.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth);
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(50));
        Assert.Equal(initialHealth, host.Session.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth);
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(20));
        Assert.Equal(initialHealth - 1, host.Session.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth);
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(300));
        Assert.False(host.Session.HasActiveCombat);
        Assert.Equal("Complete", host.Session.CombatInspection.Phase);
    }

    [Fact]
    public void SelectedSwordSwingInWaterUsesHostedCombatAndDisturbsTheField()
    {
        TinyFarmSimulationHost host = CreateHost(TinyFarmSimulationMode.Playing, placeInWater: true);
        TinyFarmStepResult selection = host.ExecuteIntent(
            new SelectHotbarSlotIntent(new HotbarSlotId(4)));
        string before = host.Session.Field.SemanticHash;

        TinyFarmStepResult swing = host.ExecuteIntent(new UseSelectedIntent());

        Assert.Equal(IntentResultStatus.Accepted, selection.Results.Single().Status);
        Assert.Equal(IntentResultStatus.Accepted, swing.Results.Single().Status);
        Assert.True(host.Session.HasActiveCombat);
        Assert.NotEqual(before, host.Session.Field.SemanticHash);
        TinyFarmFieldEvent disturbance = Assert.Single(
            host.Session.Field.RecentEvents,
            item => item.Kind == "combat-motion");
        Assert.Equal(FieldDisturbanceShape.Arc, disturbance.Shape);

        host.AdvanceHostTime(TimeSpan.FromMilliseconds(400));

        Assert.False(host.Session.HasActiveCombat);
        Assert.DoesNotContain(host.Session.Field.RecentEvents, item => item.Kind == "combat-contact");
    }

    [Fact]
    public void ActiveHostedCombatIsAnExplicitSaveBoundary()
    {
        var game = new TinyFarmGame(new MemorySaveStore());
        game.Start();
        PlacePlayer(game.State, TinyFarmSceneIds.Riverside, WaterPosition());
        game.Host.ExecuteIntent(new SelectHotbarSlotIntent(new HotbarSlotId(4)));
        TinyFarmStepResult swing = game.Host.ExecuteIntent(new UseSelectedIntent());

        Assert.Equal(IntentResultStatus.Accepted, swing.Results.Single().Status);
        Assert.True(game.Host.Session.HasActiveCombat);
        Assert.False(game.Save());
        Assert.False(game.BeginSave());
        Assert.Equal("Finish the swing before saving.", game.Status);
    }

    [Fact]
    public void ChargedWaterPenaltyIsSemanticAndDebounced()
    {
        TinyFarmSimulationHost host = CreateHost(TinyFarmSimulationMode.Playing, placeInWater: true);
        Assert.True(host.Session.Field.Energize(WaterPosition()) > 0);

        host.AdvanceHostTime(TimeSpan.FromMilliseconds(20));
        long firstContact = host.Session.Field.ChargedWaterContactCount;
        ScenePosition before = host.Session.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        host.SetPlayerMovement(1, 0);
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(100));

        Assert.Equal(1, firstContact);
        Assert.Equal(firstContact, host.Session.Field.ChargedWaterContactCount);
        Assert.Equal(before, host.Session.State.ActorScene(TinyFarmIds.Player).WorldPosition);
        Assert.True(host.Session.Field.PlayerMovementPenaltyRemaining > 0);
    }

    [Fact]
    public async Task DeliveranceRestoresExactFieldAndChargedWaterState()
    {
        TinyFarmSimulationHost host = CreateHost(TinyFarmSimulationMode.Playing, placeInWater: true);
        host.Session.Field.ApplyCombatMove(
            TinyFarmCombatMoves.HammerSmash,
            TinyFarmSceneIds.Riverside,
            WaterPosition(),
            ActorFacing.Right,
            contact: true);
        host.Session.Field.Energize(WaterPosition());
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(20));
        string expectedFieldHash = host.Session.Field.SemanticHash;
        string expectedWorldHash = TinyFarmSemanticHash.Compute(host.Session.State);
        var store = new MemorySaveStore();
        var persistence = new TinyFarmDeliverancePersistence(host, Definitions(), store);
        await persistence.Deliverance.SaveAsync("field", persistence.CaptureSave("field"));

        host.Session.Field.AdvanceTick(TinyFarmSceneIds.Farm, ScenePosition.FromGrid(new GridPosition(1, 1)));
        host.CommitLoadedSession(new TinyFarmSession(TinyFarmM21ControlStates.Create(Definitions()), Definitions()));
        LoadedSaveCandidate candidate = await persistence.Deliverance.LoadAsync(
            "field",
            persistence.GetLoadDefinitions("field"),
            persistence.GetLoadCompatibility("field"));
        persistence.CommitLoadedCandidate("field", candidate);

        Assert.Equal(expectedFieldHash, host.Session.Field.SemanticHash);
        Assert.Equal(expectedWorldHash, TinyFarmSemanticHash.Compute(host.Session.State));
        Assert.Equal(1, host.Session.Field.ChargedWaterContactCount);
    }

    [Fact]
    public async Task SchemaV2SaveRegeneratesDeterministicDefaultField()
    {
        TinyFarmDefinitions definitions = Definitions();
        TinyFarmSimulationHost host = CreateHost(TinyFarmSimulationMode.Paused);
        var store = new MemorySaveStore();
        var persistence = new TinyFarmDeliverancePersistence(host, definitions, store);
        TinyFarmSemanticSaveSnapshot current = persistence.CaptureSnapshot();
        var legacyOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        byte[] legacyBytes = JsonSerializer.SerializeToUtf8Bytes(
            current with { Field = null },
            legacyOptions);
        var request = new SaveRequest(
            new SaveApplicationMetadata(
                TinyFarmDeliverancePersistence.ApplicationId,
                TinyFarmDeliverancePersistence.ApplicationVersion,
                DefinitionHash: definitions.Identity,
                CadenceConfigHash: host.CadenceConfigurationIdentity,
                ApplicationSaveVersion: 1),
            [new SaveModulePayload(
                TinyFarmDeliverancePersistence.ModuleId,
                2,
                ModuleCriticality.Required,
                0,
                new GzipCodec().Id,
                legacyBytes)]);
        await persistence.Deliverance.SaveAsync("v2", request);

        LoadedSaveCandidate candidate = await persistence.Deliverance.LoadAsync(
            "v2",
            persistence.GetLoadDefinitions("v2"),
            persistence.GetLoadCompatibility("v2"));
        persistence.CommitLoadedCandidate("v2", candidate);

        Assert.Equal(3, candidate.GetModule(TinyFarmDeliverancePersistence.ModuleId).SchemaVersion);
        Assert.Equal(TinyFarmFieldRuntime.CreateDefault().SemanticHash, host.Session.Field.SemanticHash);
    }

    [Fact]
    public void ReplayIncludesMovementFieldHash()
    {
        TinyFarmDefinitions definitions = Definitions();
        TinyFarmState initial = TinyFarmM21ControlStates.Create(definitions);
        PlacePlayer(initial, TinyFarmSceneIds.Riverside, WaterPosition());
        IntentEnvelope movement = new(
            TinyFarmIds.Player,
            new SpatialMoveIntent(1, 0, 128),
            initial.Minute,
            1,
            IntentSourceKind.Human);
        TinyFarmState final = new TinyFarmResolver(definitions).Resolve(initial, [movement]).State;
        TinyFarmFieldRuntime expectedField = TinyFarmFieldRuntime.CreateDefault();
        expectedField.ApplyMovement(
            TinyFarmSceneIds.Riverside,
            initial.ActorScene(TinyFarmIds.Player).WorldPosition,
            final.ActorScene(TinyFarmIds.Player).WorldPosition);
        var record = new TinyFarmReplayRecord(
            0,
            movement,
            TinyFarmSemanticHash.Compute(final),
            expectedField.SemanticHash);
        TinyFarmReplayEnvelope envelope = TinyFarmSemanticReplay.Create(
            initial,
            definitions.Identity,
            "field-cadence",
            [record]);

        TinyFarmReplayResult replay = TinyFarmSemanticReplay.Replay(envelope, definitions, "field-cadence");

        Assert.Equal(expectedField.SemanticHash, replay.FieldHash);
    }

    [Fact]
    public void ReplayIncludesEnvironmentalSwordMotionFieldHash()
    {
        TinyFarmDefinitions definitions = Definitions();
        TinyFarmState initial = TinyFarmM21ControlStates.Create(definitions);
        PlacePlayer(initial, TinyFarmSceneIds.Riverside, WaterPosition());
        IntentEnvelope select = new(
            TinyFarmIds.Player,
            new SelectHotbarSlotIntent(new HotbarSlotId(4)),
            initial.Minute,
            1,
            IntentSourceKind.Human);
        initial = new TinyFarmResolver(definitions).Resolve(initial, [select]).State;
        IntentEnvelope swing = new(
            TinyFarmIds.Player,
            new UseSelectedIntent(),
            initial.Minute,
            2,
            IntentSourceKind.Human);
        TinyFarmFieldRuntime expectedField = TinyFarmFieldRuntime.CreateDefault();
        expectedField.ApplyCombatMove(
            TinyFarmCombatMoves.SwordSwing,
            TinyFarmSceneIds.Riverside,
            WaterPosition(),
            initial.ActorScene(TinyFarmIds.Player).Facing,
            contact: false);
        var record = new TinyFarmReplayRecord(
            0,
            swing,
            TinyFarmSemanticHash.Compute(initial),
            expectedField.SemanticHash);
        TinyFarmReplayEnvelope envelope = TinyFarmSemanticReplay.Create(
            initial,
            definitions.Identity,
            "field-cadence",
            [record]);

        TinyFarmReplayResult replay = TinyFarmSemanticReplay.Replay(
            envelope,
            definitions,
            "field-cadence");

        Assert.Equal(expectedField.SemanticHash, replay.FieldHash);
        Assert.Equal(TinyFarmSemanticHash.Compute(initial), replay.FinalHash);
    }

    [Fact]
    public void OblivionSurfacesReadActualRunningFieldAndCombatState()
    {
        TinyFarmSimulationHost host = CreateHost(TinyFarmSimulationMode.Paused, placeInWater: true);
        var surfaces = new TinyFarmOblivionLiveSurfaces(host);
        string before = surfaces.Capture().Pages.Single().Cards[0].Body.RawText;
        host.Session.Field.Energize(WaterPosition());
        string after = surfaces.Capture().Pages.Single().Cards[0].Body.RawText;

        Assert.Equal(
            ["tinyfarm.live.field", "tinyfarm.live.combat", "tinyfarm.spatial.m24", "tinyfarm.presentation.m25"],
            surfaces.RegisteredSurfaceIds);
        Assert.NotEqual(before, after);
        Assert.Contains("wet-cells: 240", after, StringComparison.Ordinal);
        Assert.Contains("snapshot-version: 1", after, StringComparison.Ordinal);
        Assert.Contains("TinyFarm.Player.Combat.ActiveAction",
            surfaces.Capture().Pages.Single().Cards[1].Body.RawText,
            StringComparison.Ordinal);
    }

    private static TinyFarmSimulationHost CreateHost(
        TinyFarmSimulationMode mode,
        bool placeInWater = false)
    {
        TinyFarmDefinitions definitions = Definitions();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        if (placeInWater)
        {
            PlacePlayer(state, TinyFarmSceneIds.Riverside, WaterPosition());
        }
        return new TinyFarmSimulationHost(new TinyFarmSession(state, definitions), definitions, mode);
    }

    private static TinyFarmDefinitions Definitions() => TinyFarmDefinitionLoader.LoadM21();

    private static ScenePosition WaterPosition()
    {
        return ScenePosition.FromGrid(new GridPosition(10, 5));
    }

    private static void PlacePlayer(TinyFarmState state, SceneId scene, ScenePosition position)
    {
        int placementIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        ActorSceneState current = state.MutableActorScenes[placementIndex];
        state.MutableActorScenes[placementIndex] = current with
        {
            Scene = scene,
            WorldPosition = position
        };
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == TinyFarmIds.Player);
        ActorState player = state.MutableActors[actorIndex];
        state.MutableActors[actorIndex] = player with { Location = TinyFarmScenes.LocationForScene(scene) };
    }

    private sealed class MemorySaveStore : ISaveStore
    {
        private readonly Dictionary<string, byte[]> slots = new(StringComparer.Ordinal);

        public Task<bool> ExistsAsync(string slotId, CancellationToken ct = default) =>
            Task.FromResult(slots.ContainsKey(slotId));

        public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(slots.Keys.ToArray());

        public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default) =>
            Task.FromResult<SlotInfo?>(slots.TryGetValue(slotId, out byte[]? value)
                ? new SlotInfo(slotId, null, value.Length)
                : null);

        public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SlotInfo>>(slots
                .Select(item => new SlotInfo(item.Key, null, item.Value.Length))
                .ToArray());

        public Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default) =>
            Task.FromResult<ReadOnlyMemory<byte>>(slots[slotId]);

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
