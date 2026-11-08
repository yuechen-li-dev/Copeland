using Deliverance.Core.Storage;
using Machina.Core.Actions;
using Machina.Runtime.Input;
using TinyFarm.InputMan;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmTypedUiTests
{
    [Fact]
    public void DispatchUsesMessageContentInsteadOfItsDiagnosticAlias()
    {
        using var game = Game();
        game.Start();
        game.OpenInventory(false);
        UiAction<TinyFarmMenuMessage> renamed = UiAction.Typed<TinyFarmMenuMessage>(new UiActionId("quit"),
            new TinyFarmMenuMessage.Command(TinyFarmMenuCommand.Equipment));
        game.DispatchMenu(renamed);
        Assert.True(game.Menus.EquipmentOnly);
        Assert.False(game.ShouldQuit);
        Assert.False(game.TryDispatchMenu(UiAction.Named("bag")));
        Assert.True(game.Menus.EquipmentOnly);
        Assert.False(game.TryDispatchMenu(UiAction.Typed(new UiActionId("bag"), "foreign message")));
        Assert.True(game.Menus.EquipmentOnly);
        game.DispatchMenu(TinyFarmUiActions.Bag);
        Assert.False(game.Menus.EquipmentOnly);
    }

    [Fact]
    public void ItemAndProductWithTheSameTextHaveDifferentIdentitiesAndActions()
    {
        var item = new TinyFarmInventoryKey.Item(new ItemId("same:part/a.b%2E"));
        var product = new TinyFarmInventoryKey.Product(new ProductId("same:part/a.b%2E"));
        Assert.NotEqual<TinyFarmInventoryKey>(item, product);
        Assert.NotEqual(TinyFarmUiIds.InventoryRow(item), TinyFarmUiIds.InventoryRow(product));
        Assert.NotEqual(TinyFarmUiActions.SelectRow(item), TinyFarmUiActions.SelectRow(product));
        Assert.Equal(new TinyFarmMenuMessage.SelectRow(item), TinyFarmUiActions.SelectRow(item).Message);
        Assert.Equal(new TinyFarmMenuMessage.SelectRow(product), TinyFarmUiActions.SelectRow(product).Message);
    }

    [Fact]
    public void RealPreparedRowClickCarriesTheTypedInventoryKey()
    {
        using var game = Game();
        game.Start();
        game.OpenInventory(false);
        var ui = new TinyFarmNativeUi(game);
        var frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        var key = new TinyFarmInventoryKey.Item(TinyFarmIds.Sword);
        PointerPoint point = ui.ActionCenter(frame, TinyFarmUiActions.SelectRow(key));
        ui.Pointer(frame, point, true);
        ui.Pointer(frame, point, false);
        Assert.Equal(key, game.Menus.SelectedKey);
        Assert.True(ui.TryActionCenter(frame, "row:item:sword", out var aliasPoint));
        Assert.Equal(point, aliasPoint);
        Assert.False(ui.TryActionCenter(frame, "row:item:missing", out _));
    }

    [Fact]
    public void ReorderedInventoryRetainsItsSelectedIdentityAndCanUseIt()
    {
        using var game = Game();
        game.Start();
        game.OpenInventory(false);
        var key = new TinyFarmInventoryKey.Item(TinyFarmIds.Sword);
        game.DispatchMenu(TinyFarmUiActions.SelectRow(key));
        game.DispatchMenu(TinyFarmUiActions.Sorts[InventorySort.Value]);
        game.Menus.Rows(game.State, game.Definitions);
        Assert.Equal(key, game.Menus.SelectedKey);
        game.DispatchMenu(TinyFarmUiActions.Primary);
        Assert.False(TinyFarmEquipmentRules.IsEquipped(game.State, TinyFarmIds.Sword));
    }

    [Fact]
    public void PreparedRecipeAndIngredientButtonsDriveTheRealCraftingResolver()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState state = TinyFarmCraftingContent.Start(definitions);
        state.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.Turnip, 10));
        int actorIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        ActorState player = state.MutableActors[actorIndex];
        state.MutableActors[actorIndex] = player with
        {
            Location = TinyFarmIds.Farmhouse,
            Rpg = player.Rpg! with
            {
                Crafting = new TinyFarmCraftingProgress([new(TinyFarmCraftingContent.Recipe, "Typed UI test")]),
            },
        };
        int sceneIndex = state.MutableActorScenes.FindIndex(actor => actor.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[sceneIndex] = state.MutableActorScenes[sceneIndex] with
        {
            Scene = TinyFarmSceneIds.Residence,
            WorldPosition = new ScenePosition(6656, 5632),
            Facing = ActorFacing.Up,
        };
        using var game = new TinyFarmGame(new FileSaveStore(Path.Combine(Path.GetTempPath(),
            "tinyfarm-typed-craft", Guid.NewGuid().ToString("N"))),
            authored: new TinyFarmAuthoredWorld(definitions, state));
        game.Start();
        game.Execute(new InteractIntent());
        Assert.Equal(TinyFarmScreen.Crafting, game.Screen);
        var ui = new TinyFarmNativeUi(game);

        void Click(UiAction action)
        {
            TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
            PointerPoint point = ui.ActionCenter(frame, action);
            ui.Pointer(frame, point, true);
            ui.Pointer(frame, point, false);
        }

        Click(TinyFarmUiActions.Recipes[TinyFarmCraftingContent.Recipe]);
        Assert.Equal(TinyFarmCraftingContent.Recipe, game.SelectedCraftRecipe);
        Assert.Equal(2, game.CraftInputs.Count);
        Click(TinyFarmUiActions.RemoveIngredients[TinyFarmIds.Turnip]);
        Assert.DoesNotContain(game.CraftInputs, input => input.Product == TinyFarmIds.Turnip);
        Click(TinyFarmUiActions.AddIngredients[TinyFarmIds.Turnip]);
        Click(TinyFarmUiActions.Craft);
        Assert.Equal(1, game.State.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.Soup));
    }

    private static TinyFarmGame Game()
    {
        return new TinyFarmGame(new FileSaveStore(Path.Combine(Path.GetTempPath(),
            "tinyfarm-typed-ui", Guid.NewGuid().ToString("N"))));
    }
}
