namespace TinyFarm.Core;

/// <summary>A2 composes the opening campaign without changing historical proof content.</summary>
public static class TinyFarmCraftingContent
{
    public static readonly ProductId Water = new("water");
    public static readonly ProductId Salt = new("rock-salt");
    public static readonly ProductId Soup = new("turnip-broth");
    public static readonly ProductId SaltedSoup = new("salted-turnip-soup");
    public static readonly CookingRecipeId Recipe = new("turnip-broth");
    public static readonly ItemId RecipeCard = new("turnip-soup-recipe-card");
    public static readonly ForageNodeId SaltOutcrop = new("farm-rock-salt");

    public static TinyFarmDefinitions Load()
    {
        TinyFarmDefinitions source = TinyFarmSliceContent.Load();
        var scenes = new TinyFarmSceneCatalog(source.Scenes.All.Select(scene =>
        {
            if (scene.Id != TinyFarmSceneIds.Farm)
            {
                return scene;
            }
            var id = new SceneObjectId(SaltOutcrop.Value);
            return new SceneDefinition(scene.Id, scene.Name, scene.Width, scene.Height,
                scene.Objects.Append(new SceneObjectDefinition(id, SceneObjectKind.Forage, "Rock salt outcrop", false, SaltOutcrop.Value)).ToArray(),
                scene.Layout.Append(new SceneLayoutRow(id, 13, 8, 1, 1, 0)).ToArray(), scene.Anchors, scene.Routes);
        }));
        return new TinyFarmDefinitions("tinyfarm-crafting-a2-v1;" + source.Identity,
            source.Items.Select(item => item.Id == Soup
                ? item with { Name = "Turnip Soup", Food = new TinyFarmFoodEffect(4, 12) } : item)
                .Concat([new ItemDefinition(Water, "Water", 0, 0),
                    new ItemDefinition(Salt, "Rock Salt", 0, 1),
                    new ItemDefinition(SaltedSoup, "Salted Turnip Soup", 0, 7, new TinyFarmFoodEffect(4, 20))]),
            source.Crops, scenes, source.SceneContent, source.Schedules, source.ScheduleContent,
            source.ForageNodes.Append(new ForageNodeDefinition(SaltOutcrop, TinyFarmSceneIds.Farm,
                ScenePosition.FromGrid(new GridPosition(13, 8)), Salt, 2)),
            [new CookingRecipeDefinition(Recipe, CookingStationKind.Cooking,
                [new CookingRecipeInput(TinyFarmIds.Turnip, 1), new CookingRecipeInput(Water, 1)], Soup, 1,
                new TinyFarmCraftingRules(0, 4, 20, [Water],
                    [new TinyFarmRecipeModifier(Salt, SaltedSoup, 3, "Salt enriches the soup: +8 SP recovery.")]))],
            source.Trees, source.Enemies);
    }

    public static TinyFarmState Start(TinyFarmDefinitions definitions)
    {
        TinyFarmState source = TinyFarmSliceContent.Start(definitions);
        return new TinyFarmState(TinyFarmState.CraftingSaveVersion, source.Minute,
            source.Actors.Select(actor => actor with
            {
                Rpg = actor.Rpg is null ? null : actor.Rpg with
                {
                    Crafting = new TinyFarmCraftingProgress([]), ActiveConditions = []
                }
            }).ToArray(),
            source.Items.Append(new ItemState(RecipeCard, "Turnip Soup Recipe", 0, TinyFarmIds.Farmhouse, null,
                TinyFarmSceneIds.Residence, ScenePosition.FromGrid(new GridPosition(4, 5)), TeachesRecipe: Recipe)).ToArray(),
            source.Facts, source.Favor, definitions.Identity, source.InventoryStacks,
            source.ShopStock.Where(stock => definitions.Crops.Any(crop => crop.SeedItemId == stock.Product)).ToArray(),
            source.FarmPlots, source.ActorScenes, source.ActorEnergy, source.SelectedHotbarSlot,
            source.ForageNodes, source.Trees, source.Enemies, source.Slice, source.Equipment);
    }
}
