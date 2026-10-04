using Deliverance.Core.Storage;
using InputMan.Core;
using Machina.Runtime.Input;
using TinyFarm.InputMan;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmContainerTests
{
    private static readonly ActorId Chest = TinyFarmShippingContent.Chest;

    private static TinyFarmState Ready(TinyFarmDefinitions definitions, int minute = 480)
    {
        TinyFarmState state = TinyFarmShippingContent.Start(definitions);
        state.Minute = minute;
        int index = state.ActorSceneIndex(TinyFarmIds.Player);
        state.MutableActorScenes[index] = state.ActorScene(TinyFarmIds.Player) with
        {
            WorldPosition = new ScenePosition(7680, 6100), Facing = ActorFacing.Up
        };
        state.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.Turnip, 10));
        return state;
    }

    private static ResolutionBatchResult Run(TinyFarmState state, TinyFarmDefinitions definitions, GameIntent intent)
    {
        return new TinyFarmResolver(definitions).Resolve(state,
            [new IntentEnvelope(TinyFarmIds.Player, intent, state.Minute, 0, IntentSourceKind.Human)]);
    }

    private static TransferContainerIntent Move(bool deposit, int count = 1)
    {
        return new TransferContainerIntent(Chest, deposit, count, Product: TinyFarmIds.Turnip);
    }

    [Fact]
    public void AuthoredChestIsAnIdleObjectWithSemanticFootprintAndClosedPose()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions);
        ActorState chest = state.Actor(Chest);
        Assert.Equal(TinyFarmAgentKind.Object, chest.Agent!.Kind);
        Assert.Equal(TinyFarmAgentControl.Idle, chest.Agent.Control);
        Assert.Equal(TinyFarmObjectPose.Closed, chest.Agent.ObjectPose);
        Assert.Null(chest.Rpg);
        Assert.Empty(chest.Inventory);
        Assert.True(TinyFarmScenes.IsBlocked(definitions.Scenes.Get(TinyFarmSceneIds.Farm), new GridPosition(7, 4)));
        Assert.Equal(InteractionTargetKind.Container,
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes)!.Kind);
        _ = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(state, definitions), definitions), definitions);
    }

    [Fact]
    public void InteractionOpensAndCloseChangesPoseWithoutChangingContentsOrPosition()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions);
        ActorSceneState position = state.ActorScene(Chest);
        ResolutionBatchResult opened = Run(state, definitions, new InteractIntent());
        Assert.Equal(GameEventKind.ContainerOpened, Assert.Single(opened.Results[0].Events).Kind);
        Assert.Equal(TinyFarmObjectPose.Open, opened.State.Actor(Chest).Agent!.ObjectPose);
        state = Run(opened.State, definitions, new CloseContainerIntent(Chest)).State;
        Assert.Equal(TinyFarmObjectPose.Closed, state.Actor(Chest).Agent!.ObjectPose);
        Assert.Null(state.Actor(Chest).Agent!.Container!.OpenedBy);
        Assert.Equal(position, state.ActorScene(Chest));
        Assert.Equal(10, state.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip));
    }

    [Fact]
    public void StackTransferConservesInventoryAndRejectsInvalidOrStaleRequestsAtomically()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Run(Ready(definitions), definitions, new OpenContainerIntent(Chest)).State;
        state = Run(state, definitions, Move(true, 7)).State;
        Assert.Equal(3, state.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip));
        Assert.Equal(7, state.ProductCount(Chest, TinyFarmIds.Turnip));
        state = Run(state, definitions, Move(false, 2)).State;
        Assert.Equal(5, state.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip));
        Assert.Equal(5, state.ProductCount(Chest, TinyFarmIds.Turnip));
        foreach (TransferContainerIntent invalid in new[] { Move(true, 0), Move(false, -1), Move(false, 6),
            new TransferContainerIntent(Chest, true, Item: TinyFarmIds.Axe, Product: TinyFarmIds.Turnip) })
        {
            ResolutionBatchResult result = Run(state, definitions, invalid);
            Assert.Equal(IntentResultStatus.Rejected, result.Results[0].Status);
            Assert.Equal(TinyFarmSemanticHash.Compute(state), TinyFarmSemanticHash.Compute(result.State));
        }
    }

    [Fact]
    public void ClosedDistantAndWrongSceneTransfersCannotMoveItems()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions);
        Assert.Equal(IntentReason.ContainerClosed, Run(state, definitions, Move(true)).Results[0].Reason);
        state = Run(state, definitions, new OpenContainerIntent(Chest)).State;
        int index = state.ActorSceneIndex(TinyFarmIds.Player);
        state.MutableActorScenes[index] = state.ActorScene(TinyFarmIds.Player) with { WorldPosition = new ScenePosition(15000, 10000) };
        Assert.Equal(IntentReason.NotAdjacent, Run(state, definitions, Move(true)).Results[0].Reason);
        state.MutableActorScenes[index] = state.ActorScene(TinyFarmIds.Player) with { Scene = TinyFarmSceneIds.Residence };
        Assert.Equal(IntentReason.NotAdjacent, Run(state, definitions, Move(true)).Results[0].Reason);
        // Closing must remain possible after an external scene transition.
        Assert.Equal(IntentResultStatus.Accepted, Run(state, definitions, new CloseContainerIntent(Chest)).Results[0].Status);
    }

    [Fact]
    public void IdentityItemsTransferOwnershipAndEquippedGearAndKeysStayProtected()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions);
        state = Run(state, definitions, new OpenContainerIntent(Chest)).State;
        Assert.Equal(IntentReason.EquippedItemProtected,
            Run(state, definitions, new TransferContainerIntent(Chest, true, Item: TinyFarmIds.Axe)).Results[0].Reason);
        state = Run(state, definitions, new SetEquipmentIntent(EquipmentSlot.Tool, null)).State;
        state = Run(state, definitions, new TransferContainerIntent(Chest, true, Item: TinyFarmIds.Axe)).State;
        Assert.Equal(Chest, state.Item(TinyFarmIds.Axe).Owner);
        Assert.DoesNotContain(TinyFarmIds.Axe, state.Actor(TinyFarmIds.Player).Inventory);
        state = Run(state, definitions, new TransferContainerIntent(Chest, false, Item: TinyFarmIds.Axe)).State;
        Assert.Equal(TinyFarmIds.Player, state.Item(TinyFarmIds.Axe).Owner);
        Assert.Contains(TinyFarmIds.Axe, state.Actor(TinyFarmIds.Player).Inventory);
        var key = new ItemId("test-key");
        state.MutableItems.Add(new ItemState(key, "Door key", 100, null, TinyFarmIds.Player, IsKeyItem: true));
        state.MutableActors[state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player)] = state.Actor(TinyFarmIds.Player) with
        {
            Inventory = state.Actor(TinyFarmIds.Player).Inventory.Append(key).ToList()
        };
        Assert.Equal(IntentReason.KeyItemProtected,
            Run(state, definitions, new TransferContainerIntent(Chest, true, Item: key)).Results[0].Reason);
        _ = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(state, definitions), definitions), definitions);
        Assert.True(TinyFarmContainers.IsKey(state.Item(TinyFarmIds.Letter)));
        Assert.True(TinyFarmContainers.IsKey(state.Item(TinyFarmCraftingContent.RecipeCard)));
    }

    [Fact]
    public void ShippingAtNinePaysOnceAndLateDepositsWaitUntilNextDay()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Run(Ready(definitions, 538), definitions, new OpenContainerIntent(Chest)).State;
        int money = state.Actor(TinyFarmIds.Player).Money;
        int price = definitions.Item(TinyFarmIds.Turnip).SellPrice;
        state = Run(state, definitions, Move(true, 3)).State;
        state = Run(state, definitions, new WaitIntent(1)).State;
        Assert.Equal(money, state.Actor(TinyFarmIds.Player).Money);
        ResolutionBatchResult collection = Run(state, definitions, new WaitIntent(1));
        state = collection.State;
        Assert.Equal(GameEventKind.ShipmentCollected, collection.Results[0].Events.Last().Kind);
        Assert.Equal(money + price * 3, state.Actor(TinyFarmIds.Player).Money);
        Assert.Equal(0, state.ProductCount(Chest, TinyFarmIds.Turnip));
        Assert.Equal(1, state.Actor(Chest).Agent!.Container!.LastCollectionDay);
        state = Run(state, definitions, Move(true, 2)).State;
        state = Run(state, definitions, new WaitIntent(1)).State;
        Assert.Equal(2, state.ProductCount(Chest, TinyFarmIds.Turnip));
        // Save at 09:01, restore, and advance: there must be no second day-one payout.
        var restored = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(state, definitions), definitions), definitions);
        Assert.Equal(TinyFarmSemanticHash.Compute(state), TinyFarmSemanticHash.Compute(restored.State));
        state = Run(restored.State, definitions, new WaitIntent(60)).State;
        Assert.Equal(money + price * 3, state.Actor(TinyFarmIds.Player).Money);
        state.Minute = 1440 + 539;
        state = Run(state, definitions, new WaitIntent(1)).State;
        Assert.Equal(money + price * 5, state.Actor(TinyFarmIds.Player).Money);
        Assert.Equal(2, state.Actor(Chest).Agent!.Container!.LastCollectionDay);
    }

    [Fact]
    public void EmptyPickupAlsoStampsTheDayAndNoValueItemsAreRetained()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions, 539);
        state.MutableInventoryStacks.Add(new InventoryStack(Chest, TinyFarmCraftingContent.Water, 1));
        int money = state.Actor(TinyFarmIds.Player).Money;
        state = Run(state, definitions, new WaitIntent(1)).State;
        Assert.Equal(1, state.Actor(Chest).Agent!.Container!.LastCollectionDay);
        Assert.Equal(0, state.Actor(Chest).Agent!.Container!.LastCollectionCoins);
        Assert.Equal(1, state.ProductCount(Chest, TinyFarmCraftingContent.Water));
        Assert.Equal(money, state.Actor(TinyFarmIds.Player).Money);
        state = Run(state, definitions, new OpenContainerIntent(Chest)).State;
        state = Run(state, definitions, Move(true)).State;
        state = Run(state, definitions, new WaitIntent(1)).State;
        Assert.Equal(1, state.ProductCount(Chest, TinyFarmIds.Turnip));
    }

    [Fact]
    public void SleepAcrossNineCollectsSkippedPickupAndNextMorningDoesNotDoublePay()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions);
        state.MutableInventoryStacks.Add(new InventoryStack(Chest, TinyFarmIds.Turnip, 2));
        int money = state.Actor(TinyFarmIds.Player).Money;
        int index = state.ActorSceneIndex(TinyFarmIds.Player);
        state.MutableActorScenes[index] = state.ActorScene(TinyFarmIds.Player) with
        {
            Scene = TinyFarmSceneIds.Residence, WorldPosition = new ScenePosition(3072, 6600), Facing = ActorFacing.Up
        };
        ResolutionBatchResult sleep = Run(state, definitions, new SleepIntent());
        Assert.Equal(IntentResultStatus.Accepted, sleep.Results[0].Status);
        state = sleep.State;
        Assert.Equal(2, state.Day);
        Assert.Equal(360, state.Minute % 1440);
        Assert.Equal(1, state.Actor(Chest).Agent!.Container!.LastCollectionDay);
        Assert.Equal(money + 2 * definitions.Item(TinyFarmIds.Turnip).SellPrice, state.Actor(TinyFarmIds.Player).Money);
        state = Run(state, definitions, new WaitIntent(180)).State;
        Assert.Equal(2, state.Actor(Chest).Agent!.Container!.LastCollectionDay);
        Assert.Equal(0, state.Actor(Chest).Agent!.Container!.LastCollectionCoins);
    }

    [Fact]
    public void CurrencyOverflowLeavesShipmentIntactInsteadOfLosingItems()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions, 539);
        int index = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[index] = state.Actor(TinyFarmIds.Player) with { Money = int.MaxValue };
        state.MutableInventoryStacks.Add(new InventoryStack(Chest, TinyFarmIds.Turnip, 2));
        state = Run(state, definitions, new WaitIntent(1)).State;
        Assert.Equal(int.MaxValue, state.Actor(TinyFarmIds.Player).Money);
        Assert.Equal(2, state.ProductCount(Chest, TinyFarmIds.Turnip));
    }

    [Fact]
    public void GeneratedReplayAndSaveCarryTransferPoseAndDailySale()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState initial = Ready(definitions, 539);
        TinyFarmState state = initial;
        GameIntent[] intents = [new OpenContainerIntent(Chest), Move(true, 3), Move(false),
            new CloseContainerIntent(Chest), new WaitIntent(1), new OpenContainerIntent(Chest), Move(true),
            new CloseContainerIntent(Chest), new WaitIntent(1)];
        var records = new List<TinyFarmReplayRecord>();
        for (int index = 0; index < intents.Length; index++)
        {
            var envelope = new IntentEnvelope(TinyFarmIds.Player, intents[index], state.Minute, index, IntentSourceKind.Human);
            state = new TinyFarmResolver(definitions).Resolve(state, [envelope]).State;
            records.Add(new TinyFarmReplayRecord(index, envelope, TinyFarmSemanticHash.Compute(state)));
        }
        TinyFarmReplayEnvelope replay = TinyFarmSemanticReplay.Create(initial, definitions.Identity, "shipping-a3", records);
        TinyFarmReplayResult result = TinyFarmSemanticReplay.Replay(
            TinyFarmSemanticReplay.Deserialize(TinyFarmSemanticReplay.Serialize(replay)), definitions, "shipping-a3");
        Assert.Equal(TinyFarmSemanticHash.Compute(state), result.FinalHash);
        var restored = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(state, definitions), definitions), definitions);
        Assert.Equal(result.FinalHash, TinyFarmSemanticHash.Compute(restored.State));
    }

    [Fact]
    public void ActualMachinaRowsTransferBothWaysAndSaveRestoresOpenWindow()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        var authored = new TinyFarmAuthoredWorld(definitions, Ready(definitions));
        var store = new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-containers", Guid.NewGuid().ToString("N")));
        var game = new TinyFarmGame(store, slice: true, authored: authored);
        game.Start();
        game.Execute(new InteractIntent());
        Assert.Equal(TinyFarmScreen.Container, game.Screen);
        Assert.DoesNotContain(GameControls.Gameplay, game.Contexts);
        var ui = new TinyFarmNativeUi(game);
        void Click(int x, int y)
        {
            TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
            ui.Pointer(frame, new PointerPoint(x, y), true);
            ui.Pointer(frame, new PointerPoint(x, y), false);
        }
        // Axe, sword, turnip, seed sorted by name: turnip is the third row.
        Click(180, 372);
        Assert.Equal(1, game.State.ProductCount(Chest, TinyFarmIds.Turnip));
        Click(770, 303);
        Assert.Equal(0, game.State.ProductCount(Chest, TinyFarmIds.Turnip));
        Click(320, 180);
        Click(180, 372);
        Assert.Equal(10, game.State.ProductCount(Chest, TinyFarmIds.Turnip));
        Click(770, 303);
        Assert.Equal(10, game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip));
        Assert.Equal(0, game.State.ProductCount(Chest, TinyFarmIds.Turnip));
        Assert.True(game.Save(), game.Status);
        string saved = TinyFarmSemanticHash.Compute(game.State);
        game.BackFromMenu();
        Assert.Equal(TinyFarmObjectPose.Closed, game.State.Actor(Chest).Agent!.ObjectPose);
        Assert.True(game.Load(), game.Status);
        Assert.Equal(TinyFarmScreen.Container, game.Screen);
        Assert.Equal(saved, TinyFarmSemanticHash.Compute(game.State));
    }

    [Fact]
    public void ShippingCampaignRejectsDirectShopSales()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions);
        Assert.Equal(IntentReason.NotForSale, Run(state, definitions, new SellProductIntent(TinyFarmIds.Turnip)).Results[0].Reason);
        Assert.Equal(IntentReason.NotForSale, Run(state, definitions, new SellIntent(TinyFarmIds.Axe)).Results[0].Reason);
    }

    [Fact]
    public void DestinationStackLimitRejectsTransferWithoutChangingEitherInventory()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions);
        state.MutableInventoryStacks.Add(new InventoryStack(Chest, TinyFarmIds.Turnip, 99));
        state = Run(state, definitions, new OpenContainerIntent(Chest)).State;
        ResolutionBatchResult rejected = Run(state, definitions, Move(true));
        Assert.Equal(IntentReason.InventoryFull, rejected.Results[0].Reason);
        Assert.Equal(TinyFarmSemanticHash.Compute(state), TinyFarmSemanticHash.Compute(rejected.State));
    }

    [Fact]
    public void OrdinaryStorageContainerNeverAutomaticallySells()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions, 539);
        int index = state.MutableActors.FindIndex(actor => actor.Id == Chest);
        ActorState chest = state.Actor(Chest);
        state.MutableActors[index] = chest with { Agent = chest.Agent! with { Container = new TinyFarmContainerState() } };
        state = Run(state, definitions, new OpenContainerIntent(Chest)).State;
        state = Run(state, definitions, Move(true, 2)).State;
        state = Run(state, definitions, new WaitIntent(1)).State;
        Assert.Equal(2, state.ProductCount(Chest, TinyFarmIds.Turnip));
        Assert.Equal(0, state.Actor(Chest).Agent!.Container!.LastCollectionDay);
    }

    [Fact]
    public void RealHostClockCollectsAtNineAndContainerUiStopsClockAndGameplay()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions, 539);
        state.MutableInventoryStacks.Add(new InventoryStack(Chest, TinyFarmIds.Turnip, 2));
        var store = new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-shipping-clock", Guid.NewGuid().ToString("N")));
        var game = new TinyFarmGame(store, slice: true, authored: new TinyFarmAuthoredWorld(definitions, state));
        var input = new InputManEngine(GameControls.CreateProfile(true));
        game.Start();
        input.SetMaps(game.Contexts);
        input.Tick(new InputSnapshot(new Dictionary<ControlKey, bool>(), new Dictionary<ControlKey, float>()), 1, 0);
        int money = game.State.Actor(TinyFarmIds.Player).Money;
        game.Advance(TimeSpan.FromSeconds(1), input.CurrentFrame, true);
        Assert.Equal(540, game.State.Minute);
        Assert.Equal(money + 2 * definitions.Item(TinyFarmIds.Turnip).SellPrice, game.State.Actor(TinyFarmIds.Player).Money);
        Assert.Contains("Shipping collected", game.Status);
        game.Execute(new InteractIntent());
        input.SetMaps(game.Contexts);
        input.Tick(new InputSnapshot(new Dictionary<ControlKey, bool>
        {
            [Controls.Key(KeyboardKey.W)] = true, [Controls.Key(KeyboardKey.J)] = true
        }, new Dictionary<ControlKey, float>()), 1, 1);
        string hash = TinyFarmSemanticHash.Compute(game.State);
        game.Handle(input.CurrentFrame);
        game.Advance(TimeSpan.FromSeconds(5), input.CurrentFrame, true);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));
    }

    [Fact]
    public void SellingIdentityItemRemovesItAndAllLiveProjectionsRemainValid()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        TinyFarmState state = Ready(definitions, 539);
        var item = new ItemId("sale-prop");
        state.MutableItems.Add(new ItemState(item, "Copper pot", 20, null, TinyFarmIds.Player));
        int index = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[index] = state.Actor(TinyFarmIds.Player) with
        {
            Inventory = state.Actor(TinyFarmIds.Player).Inventory.Append(item).ToList()
        };
        state = Run(state, definitions, new OpenContainerIntent(Chest)).State;
        state = Run(state, definitions, new TransferContainerIntent(Chest, true, Item: item)).State;
        int money = state.Actor(TinyFarmIds.Player).Money;
        state = Run(state, definitions, new WaitIntent(1)).State;
        Assert.Equal(money + 10, state.Actor(TinyFarmIds.Player).Money);
        Assert.DoesNotContain(state.Items, candidate => candidate.Id == item);
        Assert.DoesNotContain(item, state.Actor(Chest).Inventory);
        _ = TinyFarmFrameProjector.Project(state, definitions);
        _ = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(state, definitions), definitions), definitions);
    }

    [Fact]
    public void SaveRejectsInvalidDailyGateAndOpenerPoseMismatch()
    {
        TinyFarmDefinitions definitions = TinyFarmShippingContent.Load();
        foreach (TinyFarmContainerState invalid in new[]
        {
            new TinyFarmContainerState(true, TinyFarmIds.Player, LastCollectionDay: 2),
            new TinyFarmContainerState(true, TinyFarmIds.Player, OpenedBy: TinyFarmIds.Player),
            new TinyFarmContainerState(true, new ActorId("missing-owner"))
        })
        {
            TinyFarmState state = Ready(definitions);
            int index = state.MutableActors.FindIndex(actor => actor.Id == Chest);
            ActorState chest = state.Actor(Chest);
            state.MutableActors[index] = chest with { Agent = chest.Agent! with { Container = invalid } };
            Assert.Throws<InvalidDataException>(() => TinyFarmChunkedSaveCodec.Read(
                TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(state, definitions), definitions), definitions));
        }
    }
}
