using System.Text.Json;
using Deliverance.Core.Storage;
using InputMan.Core;
using Machina.Runtime.Input;
using TinyFarm.InputMan;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmInventoryMenuTests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmSliceContent.Load();

    [Fact]
    public void MenuFoodUsesTheExistingHealingRuleAndReconcilesTheConsumedRow()
    {
        var game = new TinyFarmGame(NewStore(), slice: true);
        var broth = new ProductId("turnip-broth");
        game.State.Slice = game.State.Slice! with { Health = 8 };
        game.State.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, broth, 1));
        game.Start();
        game.OpenInventory(false);
        game.Menus.SetCategory(InventoryCategory.Food);
        Assert.Equal(broth, Assert.Single(game.Menus.Rows(game.State, game.Definitions)).Product);
        game.DispatchMenu("primary");
        Assert.Equal(12, game.State.Slice!.Health);
        Assert.Equal(0, game.State.ProductCount(TinyFarmIds.Player, broth));
        Assert.Empty(game.Menus.Rows(game.State, game.Definitions));
        Assert.Null(game.Menus.SelectedKey);
        Assert.Equal(TinyFarmScreen.Inventory, game.Screen);
    }

    [Fact]
    public void UnequippedAxeCannotChopAndLegacyWorldDoesNotAcquireSliceState()
    {
        TinyFarmDefinitions legacy = TinyFarmDefinitionLoader.LoadM21();
        var session = new TinyFarmSession(TinyFarmSupperStart.Create(legacy), legacy);
        session.Step(new SetEquipmentIntent(EquipmentSlot.Tool, null), false);
        Assert.Equal(IntentReason.MissingAxe,
            session.Step(new ChopIntent(legacy.Trees[0].Id), false).Results.Single().Reason);
        TinyFarmSession restored = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(session, legacy), legacy);
        Assert.Null(restored.State.Slice);
        Assert.False(TinyFarmEquipmentRules.IsEquipped(restored.State, TinyFarmIds.Axe));
    }

    [Fact]
    public void HistoricalPartialWorldCannotBePromotedToACompleteEquipmentSave()
    {
        TinyFarmState state = TinyFarmSliceContent.Start(definitions);
        state.Version = TinyFarmState.ItemActionSaveVersion;
        state.Slice = null;
        string before = TinyFarmSemanticHash.Compute(state);
        var session = new TinyFarmSession(state, definitions);
        Assert.Equal(IntentResultStatus.Rejected,
            session.Step(new SetEquipmentIntent(EquipmentSlot.Weapon, null), false).Results.Single().Status);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(session.State));
    }

    [Fact]
    public void EquipmentIsOwnedSlotCheckedAndChangesAttackAvailability()
    {
        var session = new TinyFarmSession(TinyFarmSliceContent.Start(definitions), definitions);
        string before = TinyFarmSemanticHash.Compute(session.State);
        Assert.Equal(IntentReason.ItemNotOwned,
            session.Step(new SetEquipmentIntent(EquipmentSlot.Weapon, TinyFarmIds.WildMint), false).Results.Single().Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(session.State));
        Assert.Equal(IntentReason.WrongTool,
            session.Step(new SetEquipmentIntent(EquipmentSlot.Weapon, TinyFarmIds.Axe), false).Results.Single().Reason);
        Assert.Equal(IntentResultStatus.Accepted,
            session.Step(new SetEquipmentIntent(EquipmentSlot.Weapon, null), false).Results.Single().Status);
        Assert.Equal(TinyFarmState.EquipmentSaveVersion, session.State.Version);
        Assert.NotEqual(before, TinyFarmSemanticHash.Compute(session.State));
        Assert.Equal(IntentResultStatus.NoOp, session.Step(new SwordIntent(), false).Results.Single().Status);
        session.Step(new SetEquipmentIntent(EquipmentSlot.Weapon, TinyFarmIds.Sword), false);
        Assert.Equal(IntentResultStatus.Accepted, session.Step(new SwordIntent(), false).Results.Single().Status);
        Assert.Equal(IntentResultStatus.Rejected,
            session.Step(new SetEquipmentIntent(EquipmentSlot.Weapon, null), false).Results.Single().Status);
    }

    [Fact]
    public void BothOldAndExplicitLoadoutsRoundTripWithGeneratedMetadata()
    {
        var old = new TinyFarmSession(TinyFarmSliceContent.Start(definitions), definitions);
        TinyFarmSession restoredOld = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(old, definitions), definitions);
        Assert.Null(restoredOld.State.Equipment);
        Assert.True(TinyFarmEquipmentRules.IsEquipped(restoredOld.State, TinyFarmIds.Sword));
        old.Step(new SetEquipmentIntent(EquipmentSlot.Weapon, null), false);
        TinyFarmSession restored = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(old, definitions), definitions);
        Assert.False(TinyFarmEquipmentRules.IsEquipped(restored.State, TinyFarmIds.Sword));
        Assert.Equal(TinyFarmSemanticHash.Compute(old.State), TinyFarmSemanticHash.Compute(restored.State));
    }

    [Fact]
    public void EquipmentReplayUsesTheSameOwnershipReducerAndGeneratedIntentGraph()
    {
        TinyFarmState initial = TinyFarmSliceContent.Start(definitions);
        TinyFarmState state = initial;
        var records = new List<TinyFarmReplayRecord>();
        GameIntent[] intents =
        [
            new SetEquipmentIntent(EquipmentSlot.Weapon, null), new SwordIntent(),
            new SetEquipmentIntent(EquipmentSlot.Weapon, TinyFarmIds.Sword), new SwordIntent(), new SliceTickIntent()
        ];
        var resolver = new TinyFarmResolver(definitions);
        for (int index = 0; index < intents.Length; index++)
        {
            var intent = new IntentEnvelope(TinyFarmIds.Player, intents[index], state.Minute, index, IntentSourceKind.Human);
            state = resolver.Resolve(state, [intent]).State;
            records.Add(new TinyFarmReplayRecord(index, intent, TinyFarmSemanticHash.Compute(state)));
        }
        TinyFarmReplayEnvelope replay = TinyFarmSemanticReplay.Create(initial, definitions.Identity, "inventory-test", records);
        TinyFarmReplayResult result = TinyFarmSemanticReplay.Replay(
            TinyFarmSemanticReplay.Deserialize(TinyFarmSemanticReplay.Serialize(replay)), definitions, "inventory-test");
        Assert.Equal(TinyFarmSemanticHash.Compute(state), result.FinalHash);
    }

    [Fact]
    public void CorruptEquipmentDoesNotPassSaveValidation()
    {
        TinyFarmState state = TinyFarmSliceContent.Start(definitions);
        state.Version = TinyFarmState.EquipmentSaveVersion;
        state.Equipment = new TinyFarmEquipment(TinyFarmIds.WildMint, TinyFarmIds.Axe);
        Assert.Throws<InvalidDataException>(() => TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(state, definitions), definitions));
    }

    [Fact]
    public void FilterSortSelectionAndEmptyResultsAreTransient()
    {
        var game = new TinyFarmGame(NewStore(), slice: true);
        game.Start();
        game.OpenInventory(false);
        string hash = TinyFarmSemanticHash.Compute(game.State);
        game.Menus.SetSearch("AXE");
        Assert.Equal(TinyFarmIds.Axe, Assert.Single(game.Menus.Rows(game.State, game.Definitions)).Item);
        game.Menus.SetSearch("");
        var rows = game.Menus.Rows(game.State, game.Definitions);
        game.Menus.Select("item:sword", rows);
        game.Menus.SetSort(InventorySort.Value);
        rows = game.Menus.Rows(game.State, game.Definitions);
        Assert.Equal(rows.OrderBy(row => row.Value).Select(row => row.Key), rows.Select(row => row.Key));
        Assert.Equal("item:sword", game.Menus.SelectedKey);
        game.Menus.SetCategory(InventoryCategory.Food);
        Assert.Empty(game.Menus.Rows(game.State, game.Definitions));
        Assert.Null(game.Menus.SelectedKey);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));
    }

    [Fact]
    public void ModalFreezesWorldAndKeyboardNavigatesInsteadOfAttacking()
    {
        var game = new TinyFarmGame(NewStore(), slice: true);
        game.Start();
        game.OpenInventory(false);
        var engine = new InputManEngine(GameControls.CreateProfile(slice: true));
        engine.SetMaps(game.Contexts);
        engine.Tick(InputSnapshot.Empty, 0, 0);
        string hash = TinyFarmSemanticHash.Compute(game.State);
        string field = game.Host.Session.Field.SemanticHash;
        game.Advance(TimeSpan.FromSeconds(10), engine.CurrentFrame, true);
        Press(game, engine, KeyboardKey.ArrowDown);
        Press(game, engine, KeyboardKey.J);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));
        Assert.Equal(field, game.Host.Session.Field.SemanticHash);
        Assert.Equal(TinyFarmScreen.Inventory, game.Screen);
        game.Menus.SearchFocused = true;
        game.EnterMenuText("fnqi");
        Press(game, engine, KeyboardKey.Q);
        Press(game, engine, KeyboardKey.F);
        Press(game, engine, KeyboardKey.I);
        Press(game, engine, KeyboardKey.E);
        Assert.False(game.ShouldQuit);
        Assert.False(game.SaveInProgress);
        Assert.True(game.Menus.SearchFocused);
        Assert.Equal("fnqi", game.Menus.Search);
    }

    [Fact]
    public void RealMachinaPreparedMenuHitTestsAndRejectsReleaseElsewhere()
    {
        var game = new TinyFarmGame(NewStore(), slice: true);
        game.Start();
        game.OpenInventory(false);
        var ui = new TinyFarmNativeUi(game);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        PointerPoint equipment = ui.ActionCenter(frame, "equipment");
        ui.Pointer(frame, equipment, true);
        ui.Pointer(frame, new PointerPoint(1, 1), false);
        Assert.False(game.Menus.EquipmentOnly);
        ui.Pointer(frame, equipment, true);
        ui.Pointer(frame, equipment, false);
        Assert.True(game.Menus.EquipmentOnly);
        PointerPoint sword = ui.ActionCenter(frame, "row:item:sword");
        ui.Pointer(frame, sword, true);
        ui.Pointer(frame, sword, false);
        PointerPoint use = ui.ActionCenter(frame, "primary");
        ui.Pointer(frame, use, true);
        ui.Pointer(frame, use, false);
        Assert.False(TinyFarmEquipmentRules.IsEquipped(game.State, TinyFarmIds.Sword));
    }

    [Fact]
    public void MenuReturnsToItsParentAndMissingLoadIsDisabled()
    {
        var game = new TinyFarmGame(NewStore(), slice: true);
        game.Start();
        var engine = new InputManEngine(GameControls.CreateProfile(slice: true));
        Press(game, engine, KeyboardKey.Escape);
        Assert.Equal(TinyFarmScreen.Paused, game.Screen);
        game.DispatchMenu("load");
        Assert.Null(game.Menus.Confirmation);
        game.DispatchMenu("inventory");
        game.BackFromMenu();
        Assert.Equal(TinyFarmScreen.Paused, game.Screen);
        game.DispatchMenu("quit");
        Assert.False(game.ShouldQuit);
        game.BackFromMenu();
        Assert.Null(game.Menus.Confirmation);
        game.DispatchMenu("resume");
        game.OpenInventory(false);
        game.BackFromMenu();
        Assert.Equal(TinyFarmScreen.Playing, game.Screen);
    }

    private static void Press(TinyFarmGame game, InputManEngine engine, KeyboardKey key)
    {
        engine.SetMaps(game.Contexts);
        engine.Tick(new InputSnapshot(new Dictionary<ControlKey, bool> { [Controls.Key(key)] = true }, new Dictionary<ControlKey, float>()), .016f, 0);
        game.Handle(engine.CurrentFrame);
        engine.Tick(InputSnapshot.Empty, .016f, .016f);
        engine.SetMaps(game.Contexts);
    }

    private static FileSaveStore NewStore()
    {
        return new FileSaveStore(Path.Combine(AppContext.BaseDirectory, "menu-save-tests", Guid.NewGuid().ToString("N")));
    }
}
