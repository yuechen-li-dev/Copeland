using Aurelian.GameWorld2D;
using Deliverance.Core.Storage;
using TinyFarm.InputMan;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmOpeningSliceTests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmSliceContent.Load();

    [Fact]
    public void DiagonalSpeedIsNormalizedThroughTheExistingSweep()
    {
        TinyFarmState initial = At(TinyFarmSceneIds.DungeonEntrance, 3, 7, ActorFacing.Right);
        var cardinal = new TinyFarmSession(initial, definitions);
        var diagonal = new TinyFarmSession(initial, definitions);
        ScenePosition start = initial.ActorScene(TinyFarmIds.Player).WorldPosition;
        cardinal.Step(new SpatialMoveIntent(1, 0, 128), false);
        diagonal.Step(new SpatialMoveIntent(1, 1, 128), false);
        double straight = Math.Sqrt(start.SquaredDistance(cardinal.State.ActorScene(TinyFarmIds.Player).WorldPosition));
        double slanted = Math.Sqrt(start.SquaredDistance(diagonal.State.ActorScene(TinyFarmIds.Player).WorldPosition));
        Assert.InRange(Math.Abs(straight - slanted), 0, 1);
    }

    [Fact]
    public void DodgeCannotCrossTheBurrowWallAndHasABoundedCooldown()
    {
        var session = new TinyFarmSession(At(TinyFarmSceneIds.DungeonEntrance, 1, 2, ActorFacing.Left), definitions);
        Assert.Equal(IntentResultStatus.Accepted, session.Step(new DodgeIntent(-1, 0), false).Results.Single().Status);
        Tick(session, 20);
        ScenePosition player = session.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        Assert.False(TinyFarmScenes.IsBlocked(definitions.Scenes.Get(TinyFarmSceneIds.DungeonEntrance), player));
        Assert.True(player.XUnits >= 1024);
        Assert.Equal(0, session.State.Slice!.DodgeTicks);
        Assert.Equal(IntentResultStatus.NoOp, session.Step(new DodgeIntent(1, 0), false).Results.Single().Status);
        Tick(session, 16);
        Assert.Equal(IntentResultStatus.Accepted, session.Step(new DodgeIntent(1, 0), false).Results.Single().Status);
    }

    [Fact]
    public void SwingHitsOnceAndFreeSwordDoesNotNeedAnEnemySelection()
    {
        var session = new TinyFarmSession(At(TinyFarmSceneIds.DungeonEntrance, 7, 5, ActorFacing.Right), definitions);
        session.Step(new SwordIntent(), false);
        Tick(session, 18);
        Assert.Equal(2, session.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth);
        session.Step(new SwordIntent(), false);
        Tick(session, 18);
        Assert.Equal(EnemyLifecycle.Defeated, session.State.Enemy(TinyFarmIds.DungeonSlime).Lifecycle);
        Assert.Equal(1, session.State.Slice!.Defeats);
        int health = session.State.Slice.Health;
        Tick(session, 120);
        Assert.Equal(health, session.State.Slice!.Health);
    }

    [Fact]
    public void SlimeCommitsToItsTelegraphAndDodgingPreventsContactDamage()
    {
        TinyFarmState initial = At(TinyFarmSceneIds.DungeonEntrance, 8, 5, ActorFacing.Down);
        initial.Slice = initial.Slice! with
        {
            SlimePhase = SlimePhase.Windup,
            SlimeTicks = 2,
            SlimeDirection = new ScenePosition(1024, 0)
        };
        var session = new TinyFarmSession(initial, definitions);
        session.Step(new DodgeIntent(0, 1), false);
        Tick(session, 10);
        Assert.Equal(12, session.State.Slice!.Health);
        Assert.Equal(initial.Slice.SlimePosition.YUnits, session.State.Slice.SlimePosition.YUnits);
        Assert.True(session.State.Slice.SlimePosition.XUnits > initial.Slice.SlimePosition.XUnits);
    }

    [Fact]
    public void ContactDamageHasInvulnerabilityAndBrothIsConsumedOnlyForHealing()
    {
        TinyFarmState initial = At(TinyFarmSceneIds.DungeonEntrance, 8, 5, ActorFacing.Down);
        initial.Slice = initial.Slice! with { SlimePhase = SlimePhase.Lunge, SlimeTicks = 18 };
        initial.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, new ProductId("turnip-broth"), 1));
        var session = new TinyFarmSession(initial, definitions);
        session.Step(new EatIntent(), false);
        Assert.Equal(1, session.State.ProductCount(TinyFarmIds.Player, new ProductId("turnip-broth")));
        Tick(session, 15);
        Assert.Equal(10, session.State.Slice!.Health);
        session.Step(new EatIntent(), false);
        Assert.Equal(12, session.State.Slice!.Health);
        Assert.Equal(0, session.State.ProductCount(TinyFarmIds.Player, new ProductId("turnip-broth")));
    }

    [Fact]
    public void SleepNeedsYourBedAndOnlyWateredOwnCropAdvances()
    {
        TinyFarmState initial = At(TinyFarmSceneIds.Residence, 3, 6, ActorFacing.Up);
        FarmPlotState plot = initial.MutableFarmPlots[1];
        initial.MutableFarmPlots[1] = plot with
        {
            Crop = TinyFarmIds.TurnipCrop,
            PlantedDay = initial.Day,
            GrowthStage = 0,
            WateredToday = true,
            PlantedByPlayer = true
        };
        var session = new TinyFarmSession(initial, definitions);
        Assert.Equal(IntentResultStatus.Accepted, session.Step(new SleepIntent(), false).Results.Single().Status);
        Assert.Equal(2, session.State.Day);
        Assert.Equal(360, session.State.Minute % 1440);
        Assert.Equal(1, session.State.FarmPlots[1].GrowthStage);
        Assert.False(session.State.FarmPlots[1].WateredToday);
        var far = new TinyFarmSession(At(TinyFarmSceneIds.Residence, 8, 6, ActorFacing.Up), definitions);
        string before = TinyFarmSemanticHash.Compute(far.State);
        Assert.Equal(IntentResultStatus.Rejected, far.Step(new SleepIntent(), false).Results.Single().Status);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(far.State));
    }

    [Fact]
    public void MidDodgeSaveRestoresExactCombatAndFutureTicks()
    {
        var original = new TinyFarmSession(At(TinyFarmSceneIds.DungeonEntrance, 3, 7, ActorFacing.Right), definitions);
        original.Step(new DodgeIntent(1, -1), false);
        Tick(original, 5);
        byte[] bytes = TinyFarmChunkedSaveCodec.Write(original, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(bytes, definitions);
        Assert.Equal(TinyFarmSemanticHash.Compute(original.State), TinyFarmSemanticHash.Compute(loaded.State));
        Tick(original, 20);
        Tick(loaded, 20);
        Assert.Equal(TinyFarmSemanticHash.Compute(original.State), TinyFarmSemanticHash.Compute(loaded.State));
        Assert.Equal(original.NextSequence, loaded.NextSequence);
    }

    [Fact]
    public void MalformedCombatStateIsRejectedAndOwnCropHistoryParticipatesInHash()
    {
        TinyFarmState initial = TinyFarmSliceContent.Start(definitions);
        string first = TinyFarmSemanticHash.Compute(initial);
        initial.MutableFarmPlots[0] = initial.MutableFarmPlots[0] with { PlantedByPlayer = true };
        Assert.NotEqual(first, TinyFarmSemanticHash.Compute(initial));
        initial.Slice = initial.Slice! with { Health = 100 };
        Assert.Throws<InvalidDataException>(() => TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(initial, definitions), definitions));
    }

    [Fact]
    public void GridImporterUsesPixelPivotsAndUniformScaleAcrossSquashPoses()
    {
        byte[] pixels = new byte[8 * 4 * 4];
        for (int x = 1; x < 3; x++)
        {
            for (int y = 1; y < 4; y++)
            {
                pixels[(y * 8 + x) * 4 + 3] = 255;
            }
            pixels[(3 * 8 + x + 4) * 4 + 3] = 255;
        }
        var atlas = new SpriteAtlasResource(new SpriteAssetId("test"), "fixture", 8, 4, pixels, SpriteSampling.Linear);
        IReadOnlyList<SpriteFrameMetadata> poses = GridSpriteSheet.Import(atlas, 2, 1, 48);
        Assert.Equal(2, poses.Count);
        Assert.Equal(1, poses[0].PivotX);
        Assert.Equal(3, poses[0].PivotY);
        Assert.Equal(16, poses[0].Scale);
        Assert.Equal(poses[0].Scale, poses[1].Scale);
        Assert.Equal(1, poses[1].Height);
        Assert.Throws<InvalidDataException>(() => GridSpriteSheet.Import(atlas with { Rgba8 = new byte[pixels.Length] }, 2, 1, 48));
    }

    [Fact]
    public void StandingInsideADoorwayDoesNotRequireFacingBackAtItsCenter()
    {
        var session = new TinyFarmSession(At(TinyFarmSceneIds.Farm, 17, 6, ActorFacing.Right), definitions);
        session.Step(new SpatialMoveIntent(1, 0, 51), false);
        IntentResult result = session.Step(new InteractIntent(), false).Results.Single();
        Assert.Equal(IntentResultStatus.Accepted, result.Status);
        Assert.Equal(TinyFarmSceneIds.Overworld, session.State.CurrentScene);
    }

    [Fact]
    public void OneBufferedSwingIsPersistedAndDodgeCancelsIt()
    {
        var session = new TinyFarmSession(At(TinyFarmSceneIds.DungeonEntrance, 3, 7, ActorFacing.Right), definitions);
        session.Step(new SwordIntent(), false);
        Tick(session, 5);
        session.Step(new SwordIntent(), false);
        session.Step(new SwordIntent(), false);
        Assert.True(session.State.Slice!.SwordBuffered);
        Tick(session, 13);
        Assert.Equal(18, session.State.Slice!.SwordTicks);
        Assert.False(session.State.Slice.SwordBuffered);
        session.Step(new SwordIntent(), false);
        session.Step(new DodgeIntent(1, 0), false);
        Assert.False(session.State.Slice!.SwordBuffered);
        Assert.Equal(0, session.State.Slice.SwordTicks);
    }

    [Fact]
    public void RiverBlocksDodgeButBridgeRemainsWalkable()
    {
        var wet = new TinyFarmSession(At(TinyFarmSceneIds.Overworld, 13, 5, ActorFacing.Right), definitions);
        wet.Step(new DodgeIntent(1, 0), false);
        Tick(wet, 15);
        Assert.True(wet.State.ActorScene(TinyFarmIds.Player).WorldPosition.XUnits < 14 * 1024);
        var dry = new TinyFarmSession(At(TinyFarmSceneIds.Overworld, 13, 8, ActorFacing.Right), definitions);
        dry.Step(new DodgeIntent(1, 0), false);
        Tick(dry, 15);
        Assert.True(dry.State.ActorScene(TinyFarmIds.Player).WorldPosition.XUnits > 14 * 1024);
    }

    [Fact]
    public void GroundBrushHasDeterministicUnionCoverageAndTransparentOutside()
    {
        GroundBrushStroke stroke = new([new(1, 1), new(3, 1)], .3);
        SpriteAtlasResource once = GroundBrushRasterizer.Realize(new SpriteAssetId("path"), 4, 2, 16, [stroke], 0xB9A67BC8);
        SpriteAtlasResource twice = GroundBrushRasterizer.Realize(new SpriteAssetId("path"), 4, 2, 16, [stroke, stroke], 0xB9A67BC8);
        Assert.Equal(once.ContentHash, twice.ContentHash);
        Assert.Equal(SpriteSampling.Linear, once.Sampling);
        Assert.Equal(0, once.Rgba8[3]);
        Assert.Equal(200, once.Rgba8[(16 * 64 + 32) * 4 + 3]);
    }

    [Fact]
    public void OpeningIntentsRoundTripThroughGeneratedReplayMetadata()
    {
        TinyFarmState initial = At(TinyFarmSceneIds.DungeonEntrance, 3, 7, ActorFacing.Right);
        var resolver = new TinyFarmResolver(definitions);
        GameIntent[] intents = [new SwordIntent(), new SliceTickIntent(), new DodgeIntent(1, -1), new SliceTickIntent()];
        var records = new List<TinyFarmReplayRecord>();
        TinyFarmState state = initial;
        for (int index = 0; index < intents.Length; index++)
        {
            var intent = new IntentEnvelope(TinyFarmIds.Player, intents[index], state.Minute, index, IntentSourceKind.Human);
            state = resolver.Resolve(state, [intent]).State;
            records.Add(new TinyFarmReplayRecord(index, intent, TinyFarmSemanticHash.Compute(state)));
        }
        TinyFarmReplayEnvelope envelope = TinyFarmSemanticReplay.Create(initial, definitions.Identity, "opening-test", records);
        TinyFarmReplayEnvelope restored = TinyFarmSemanticReplay.Deserialize(TinyFarmSemanticReplay.Serialize(envelope));
        TinyFarmReplayResult result = TinyFarmSemanticReplay.Replay(restored, definitions, "opening-test");
        Assert.Equal(TinyFarmSemanticHash.Compute(state), result.FinalHash);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(240)]
    public void LateOutdoorPlayReturnsHomeOnceAndDungeonHostPausesTheCalendar(int minutes)
    {
        TinyFarmState initial = At(TinyFarmSceneIds.Farm, 8, 7, ActorFacing.Down);
        initial.Minute = 1319;
        initial.Slice = initial.Slice! with { Health = 6 };
        var session = new TinyFarmSession(initial, definitions);
        IntentResult result = session.Step(new WaitIntent(minutes), false).Results.Single();
        Assert.Contains(result.Events, item => item.Kind == GameEventKind.PlayerReturnedForRest);
        Assert.Equal(TinyFarmSceneIds.Residence, session.State.CurrentScene);
        Assert.Equal(2, session.State.Day);
        Assert.Equal(360, session.State.Minute % 1440);
        Assert.Equal(12, session.State.Slice!.Health);
        Assert.Single(result.Events, item => item.Kind == GameEventKind.DayStarted);

        TinyFarmState dungeon = At(TinyFarmSceneIds.DungeonEntrance, 3, 7, ActorFacing.Right);
        var host = new TinyFarmSimulationHost(new TinyFarmSession(dungeon, definitions), definitions,
            rates: new TinyFarmSimulationRates(NormalRealSecondsPerGameMinute: 1));
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
        host.AdvanceHostTime(TimeSpan.FromSeconds(1));
        Assert.Equal(dungeon.Minute, host.Session.State.Minute);
        Assert.True(host.Session.State.Slice!.Tick > 0);
    }

    [Fact]
    public void OpeningMaraConversationDoesNotStartTheClosedPrototypeLetterQuest()
    {
        var store = new FileSaveStore(Path.Combine(AppContext.BaseDirectory, "save-tests", Guid.NewGuid().ToString("N")));
        var game = new TinyFarmGame(store, slice: true);
        TinyFarmState initial = At(TinyFarmSceneIds.Farm, 6, 6, ActorFacing.Up);
        game.Host.ReplaceSession(new TinyFarmSession(initial, game.Definitions));
        game.Start();
        Assert.Equal(IntentResultStatus.Accepted, game.Execute(new InteractIntent()).Results.First().Status);
        Assert.True(game.Dialogue.IsActive);
        Assert.Contains("broth", game.Dialogue.Presentation!.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(initial.Favor, game.State.Favor);
        Assert.Equal(initial.Item(TinyFarmIds.Letter).Owner, game.State.Item(TinyFarmIds.Letter).Owner);
        Assert.DoesNotContain(WorldFact.MaraNeedsDelivery, game.State.Facts);
    }

    [Fact]
    public void SleepAutosaveFollowsAnEarlierBackgroundSave()
    {
        var store = new FileSaveStore(Path.Combine(AppContext.BaseDirectory, "save-tests", Guid.NewGuid().ToString("N")));
        var game = new TinyFarmGame(store, slice: true);
        TinyFarmState initial = At(TinyFarmSceneIds.Residence, 3, 6, ActorFacing.Up);
        game.Host.ReplaceSession(new TinyFarmSession(initial, game.Definitions));
        game.Start();
        Assert.True(game.BeginSave());
        Assert.Equal(IntentResultStatus.Accepted, game.Execute(new SleepIntent()).Results.First().Status);
        Assert.True(game.HasSave);
        string morningHash = TinyFarmSemanticHash.Compute(game.State);
        var restored = new TinyFarmGame(store, slice: true);
        Assert.True(restored.Load());
        Assert.Equal(2, restored.State.Day);
        Assert.Equal(morningHash, TinyFarmSemanticHash.Compute(restored.State));
    }

    [Fact]
    public void OpeningLoopRequiresActuallyReturningHomeAfterCombat()
    {
        TinyFarmState initial = At(TinyFarmSceneIds.Overworld, 3, 7, ActorFacing.Left);
        initial.Slice = initial.Slice! with { OwnHarvest = true, Slept = true, CookedBroth = true, Defeats = 1 };
        var session = new TinyFarmSession(initial, definitions);
        Assert.False(session.State.Slice!.LoopComplete);
        session.Step(new InteractIntent(), false);
        Assert.Equal(TinyFarmSceneIds.Farm, session.State.CurrentScene);
        Assert.True(session.State.Slice!.LoopComplete);
    }

    [Fact]
    public void DefeatRescuesThePlayerAndResetsTheEncounterWithoutLosingInventory()
    {
        TinyFarmState initial = At(TinyFarmSceneIds.DungeonEntrance, 8, 5, ActorFacing.Down);
        initial.Slice = initial.Slice! with { Health = 2, SlimePhase = SlimePhase.Lunge, SlimeTicks = 18 };
        int seeds = initial.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed);
        var session = new TinyFarmSession(initial, definitions);
        IntentResult result = session.Step(new SliceTickIntent(), false).Results.Single();
        Assert.Contains(result.Events, item => item.Kind == GameEventKind.PlayerRescued);
        Assert.Equal(6, session.State.Slice!.Health);
        Assert.Equal(90, session.State.Slice.HurtTicks);
        Assert.Equal(4, session.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth);
        Assert.Equal(seeds, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed));
    }

    private TinyFarmState At(SceneId scene, int x, int y, ActorFacing facing)
    {
        TinyFarmState state = TinyFarmSliceContent.Start(definitions);
        int index = state.MutableActorScenes.FindIndex(actor => actor.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[index] = state.MutableActorScenes[index] with
        {
            Scene = scene,
            WorldPosition = ScenePosition.FromGrid(new GridPosition(x, y)),
            Facing = facing
        };
        int actorIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with { Location = TinyFarmScenes.LocationForScene(scene) };
        return state;
    }

    private static void Tick(TinyFarmSession session, int count)
    {
        for (int tick = 0; tick < count; tick++)
        {
            session.Step(new SliceTickIntent(), false);
        }
    }
}
