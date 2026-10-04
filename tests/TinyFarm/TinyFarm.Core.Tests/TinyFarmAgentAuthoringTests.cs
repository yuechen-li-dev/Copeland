using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmAgentAuthoringTests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmSliceContent.Load();

    [Fact]
    public void SharedTemplateCreatesIndependentAgentsWithNamespacedOwnedEquipment()
    {
        var conditions = new List<string> { "rested" };
        var template = TinyFarmAgentTemplate.Character("neighbour") with
        {
            Conditions = conditions,
            Items =
            [
                new TinyFarmAgentItemSeed("tool", "Hoe", 4, EquipmentSlot.Tool, Equip: true),
                new TinyFarmAgentItemSeed("spare", "Spare hoe", 4, EquipmentSlot.Tool)
            ]
        };
        TinyFarmState initial = TinyFarmSliceContent.Start(definitions);
        string before = TinyFarmSemanticHash.Compute(initial);
        TinyFarmState result = TinyFarmAgentAuthoring.Compile(initial, definitions,
        [
            template.Spawn("ivy", "Ivy", TinyFarmSceneIds.Farm, new GridPosition(8, 7)),
            template.Spawn("leo", "Leo", TinyFarmSceneIds.Farm, new GridPosition(9, 7))
        ]);
        conditions.Add("later-template-edit");
        Assert.Equal(before, TinyFarmSemanticHash.Compute(initial));
        Assert.Contains(new ItemId("ivy.item.tool"), result.Actor(new ActorId("ivy")).Inventory);
        Assert.Contains(new ItemId("leo.item.tool"), result.Actor(new ActorId("leo")).Inventory);
        Assert.Equal(new ActorId("ivy"), result.Item(new ItemId("ivy.item.tool")).Owner);
        Assert.Equal(["rested"], result.Actor(new ActorId("ivy")).Agent!.Conditions);
        IReadOnlyList<TinyFarmInventoryRow> inventory = TinyFarmInventory.Project(result, definitions, new ActorId("ivy"));
        Assert.True(inventory.Single(row => row.Item == new ItemId("ivy.item.tool")).Equipped);
        Assert.False(inventory.Single(row => row.Item == new ItemId("ivy.item.spare")).Equipped);
        Assert.All(inventory, row => Assert.Equal(InventoryCategory.Tools, row.Category));
        TinyFarmState copy = result.DeepCopy();
        copy.Actor(new ActorId("ivy")).Inventory.Clear();
        Assert.Equal(2, result.Actor(new ActorId("ivy")).Inventory.Count);
    }

    [Fact]
    public void CharacterAndObjectUseSameWorldControllerAndGeneratedSavePath()
    {
        TinyFarmAuthoredWorld world = TinyFarmAgentExamples.Create();
        var session = new TinyFarmSession(world.State, world.Definitions);
        session.Step(new WaitIntent(1));
        Assert.Equal(2, session.PassiveDominatusAgentCount);
        session.Step(new WaitIntent(1));
        session.Step(new WaitIntent(1));
        Assert.Equal(2, session.PassiveDominatusAgentCount);
        TinyFarmSession restored = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(session, world.Definitions), world.Definitions);
        Assert.Equal(TinyFarmSemanticHash.Compute(session.State), TinyFarmSemanticHash.Compute(restored.State));
        Assert.Equal(TinyFarmObjectPose.Closed, restored.State.Actor(new ActorId("garden-cache")).Agent!.ObjectPose);
        Assert.Equal(3, restored.State.ProductCount(new ActorId("garden-cache"), TinyFarmIds.TurnipSeed));
        Assert.Equal(TinyFarmAgentKind.Object, restored.State.Actor(new ActorId("garden-cache")).Agent!.Kind);
        Assert.Equal(new TinyFarmAgentHealth(12, 12), restored.State.Actor(new ActorId("ivy")).Agent!.Health);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(restored.State, world.Definitions);
        Assert.Equal(TinyFarmAgentSprite.ObjectMarker, frame.Actors.Single(actor => actor.Id.Value == "garden-cache").Appearance!.OverworldSprite);
        Assert.Null(frame.Actors.Single(actor => actor.Id.Value == "ivy").Regime);
        Assert.Equal(TinyFarmEnergy.InitialUnits, session.State.EnergyFor(new ActorId("garden-cache")).Energy);
        var talk = new IntentEnvelope(TinyFarmIds.Player, new TalkIntent(new ActorId("garden-cache")),
            restored.State.Minute, 100, IntentSourceKind.Human);
        Assert.Equal(IntentReason.WrongTargetKind,
            new TinyFarmResolver(world.Definitions).Resolve(restored.State, [talk]).Results.Single().Reason);
    }

    [Fact]
    public void AuthoringIsOrderIndependentAndLegacyPlacementDoesNotChangeOldStateHash()
    {
        TinyFarmState source = TinyFarmSliceContent.Start(definitions);
        TinyFarmState placed = TinyFarmAgentAuthoring.Compile(source, definitions, TinyFarmSliceContent.OpeningCast(definitions));
        Assert.Equal(TinyFarmSemanticHash.Compute(source), TinyFarmSemanticHash.Compute(placed));
        Assert.All(placed.Actors, actor => Assert.Null(actor.Agent));
        var template = TinyFarmAgentTemplate.Object("cache");
        TinyFarmAgentSpawn[] spawns =
        [
            template.Spawn("b-cache", "B", TinyFarmSceneIds.Farm, new GridPosition(7, 7)),
            template.Spawn("a-cache", "A", TinyFarmSceneIds.Farm, new GridPosition(8, 7))
        ];
        TinyFarmAuthoredWorld forward = TinyFarmAgentAuthoring.Build(source, definitions, spawns);
        TinyFarmAuthoredWorld reverse = TinyFarmAgentAuthoring.Build(source, definitions, spawns.Reverse());
        Assert.Equal(forward.Definitions.Identity, reverse.Definitions.Identity);
        Assert.Equal(TinyFarmSemanticHash.Compute(forward.State), TinyFarmSemanticHash.Compute(reverse.State));
    }

    [Fact]
    public void InvalidDeclarationsFailAtomicallyBeforeWorldMutation()
    {
        TinyFarmState source = TinyFarmSliceContent.Start(definitions);
        string before = TinyFarmSemanticHash.Compute(source);
        var template = TinyFarmAgentTemplate.Character("neighbour");
        TinyFarmAgentSpawn good = template.Spawn("ivy", "Ivy", TinyFarmSceneIds.Farm, new GridPosition(8, 7));
        Assert.Throws<InvalidDataException>(() => TinyFarmAgentAuthoring.Compile(source, definitions, [good, good]));
        Assert.Throws<InvalidDataException>(() => TinyFarmAgentAuthoring.Compile(source, definitions,
            [good, good with { Id = new ActorId("bad"), Position = new ScenePosition(-100, -100) }]));
        Assert.Throws<InvalidDataException>(() => TinyFarmAgentAuthoring.Compile(source, definitions,
            [good with { Template = template with { Products = [new(new ProductId("missing"), 1)] } }]));
        Assert.Throws<InvalidDataException>(() => TinyFarmAgentAuthoring.Compile(source, definitions,
            [good with { Template = template with { Control = TinyFarmAgentControl.Human } }]));
        Assert.Throws<InvalidDataException>(() => TinyFarmAgentAuthoring.Compile(source, definitions,
            [good with { Template = template with { Health = new TinyFarmAgentHealth(5, 3) } }]));
        Assert.Equal(before, TinyFarmSemanticHash.Compute(source));
    }

    [Fact]
    public void ScheduleRoutineCompilesForNewIdentityAndMissingCoverageFailsAtAuthoring()
    {
        var template = TinyFarmAgentTemplate.Character("scheduled-neighbour") with
        {
            Control = TinyFarmAgentControl.Schedule,
            Routine = [new(0, 1440, TinyFarmAnchorIds.FarmHome, "Home")]
        };
        TinyFarmAgentSpawn spawn = template.Spawn("ivy", "Ivy", TinyFarmSceneIds.Farm, new GridPosition(8, 7));
        TinyFarmState source = TinyFarmSliceContent.Start(definitions);
        TinyFarmAuthoredWorld world = TinyFarmAgentAuthoring.Build(source, definitions, [spawn]);
        var session = new TinyFarmSession(world.State, world.Definitions);
        ScenePosition before = session.State.ActorScene(spawn.Id).WorldPosition;
        Assert.Contains(session.Step(new WaitIntent(1)).Results, result => result.Envelope.Actor == spawn.Id
            && result.Envelope.Source == IntentSourceKind.Dominatus && result.Status == IntentResultStatus.Accepted
            && result.Envelope.Intent is SpatialMoveIntent);
        Assert.NotEqual(before, session.State.ActorScene(spawn.Id).WorldPosition);
        Assert.Throws<InvalidDataException>(() => TinyFarmAgentAuthoring.Build(source, definitions,
            [spawn with { Routine = [new(0, 60, TinyFarmAnchorIds.FarmHome, "Incomplete")] }]));
    }

    [Fact]
    public void ExistingTransferReducerClearsEquipmentForAnyAuthoredAgent()
    {
        var template = TinyFarmAgentTemplate.Character("neighbour") with
        {
            Items = [new TinyFarmAgentItemSeed("tool", "Hoe", Slot: EquipmentSlot.Tool, Equip: true)]
        };
        ActorId ivy = new("ivy");
        ItemId hoe = new("ivy.item.tool");
        TinyFarmState state = TinyFarmAgentAuthoring.Compile(TinyFarmSliceContent.Start(definitions), definitions,
            [template.Spawn("ivy", "Ivy", TinyFarmSceneIds.Farm, new GridPosition(6, 8))]);
        var envelope = new IntentEnvelope(ivy, new GiveIntent(hoe, TinyFarmIds.Player), state.Minute, 0,
            IntentSourceKind.Dominatus);
        ResolutionBatchResult result = new TinyFarmResolver(definitions).Resolve(state, [envelope]);
        Assert.Equal(IntentResultStatus.Accepted, Assert.Single(result.Results).Status);
        Assert.Null(result.State.Actor(ivy).Agent!.Equipment.Tool);
        Assert.Equal(TinyFarmIds.Player, result.State.Item(hoe).Owner);
        Assert.Contains(hoe, result.State.Actor(TinyFarmIds.Player).Inventory);
        TinyFarmInventoryRow row = TinyFarmInventory.Project(result.State, definitions)
            .Single(candidate => candidate.Item == hoe);
        Assert.Equal(InventoryCategory.Tools, row.Category);
        Assert.Equal(EquipmentSlot.Tool, row.Slot);
        _ = TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(result.State, definitions), definitions);
        string expectedHash = TinyFarmSemanticHash.Compute(result.State);
        TinyFarmReplayEnvelope replay = TinyFarmSemanticReplay.Create(state, definitions.Identity, "agent-transfer",
            [new TinyFarmReplayRecord(0, envelope, expectedHash)]);
        TinyFarmReplayResult replayed = TinyFarmSemanticReplay.Replay(
            TinyFarmSemanticReplay.Deserialize(TinyFarmSemanticReplay.Serialize(replay)), definitions, "agent-transfer");
        Assert.Equal(expectedHash, replayed.FinalHash);
        var session = new TinyFarmSession(replayed.State, definitions);
        session.Step(new SetEquipmentIntent(EquipmentSlot.Tool, hoe), false);
        Assert.True(TinyFarmEquipmentRules.IsEquipped(session.State, hoe));
        TinyFarmSession restored = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(session, definitions), definitions);
        Assert.True(TinyFarmEquipmentRules.IsEquipped(restored.State, hoe));
    }

    [Fact]
    public void AuthoredCheckpointReplaysAndAppearanceDoesNotChangeSemanticState()
    {
        TinyFarmAuthoredWorld world = TinyFarmAgentExamples.Create();
        TinyFarmState state = world.State.DeepCopy();
        int index = state.MutableActors.FindIndex(actor => actor.Id.Value == "ivy");
        ActorState actor = state.MutableActors[index];
        state.MutableActors[index] = actor with { Agent = actor.Agent! with
        {
            Appearance = actor.Agent!.Appearance with { ScalePercent = 150 }
        } };
        Assert.Equal(TinyFarmSemanticHash.Compute(world.State), TinyFarmSemanticHash.Compute(state));
        var intent = new IntentEnvelope(TinyFarmIds.Player, new LookIntent(), state.Minute, 0, IntentSourceKind.Human);
        state = new TinyFarmResolver(world.Definitions).Resolve(state, [intent]).State;
        TinyFarmReplayEnvelope replay = TinyFarmSemanticReplay.Create(world.State, world.Definitions.Identity, "agents",
            [new TinyFarmReplayRecord(0, intent, TinyFarmSemanticHash.Compute(state))]);
        TinyFarmReplayResult result = TinyFarmSemanticReplay.Replay(
            TinyFarmSemanticReplay.Deserialize(TinyFarmSemanticReplay.Serialize(replay)), world.Definitions, "agents");
        Assert.Equal(TinyFarmSemanticHash.Compute(state), result.FinalHash);
        var session = new TinyFarmSession(state, world.Definitions);
        session.Step(new SetEquipmentIntent(EquipmentSlot.Tool, null), false);
        Assert.Equal(TinyFarmState.AgentAuthoringSaveVersion, session.State.Version);
        _ = TinyFarmChunkedSaveCodec.Write(session, world.Definitions);
    }
}
