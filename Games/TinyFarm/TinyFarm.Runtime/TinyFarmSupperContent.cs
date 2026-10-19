namespace TinyFarm.Core;

/// <summary>
/// A4 brings the supper prototype into the Sleeping Spring as a chapter. The prototype's pieces are reused as they
/// were: the riverside hen-of-the-woods node and wild mint, the sautéed mushroom dish, Mara's supper lines and the
/// supper facts. What changes is how they are reached: the dish is cooked at the A2 stove, and the river road opens
/// when Mara asks (see <see cref="TinyFarmSupper"/>).
/// </summary>
public static class TinyFarmSupperContent
{
    public static TinyFarmDefinitions Load()
    {
        TinyFarmDefinitions source = TinyFarmShippingContent.Load();
        var sauteed = new CookingRecipeDefinition(TinyFarmIds.SauteedHenOfTheWoodsRecipe, CookingStationKind.Cooking,
            [new CookingRecipeInput(TinyFarmIds.HenOfTheWoods, 1)], TinyFarmIds.SauteedHenOfTheWoods, 1,
            new TinyFarmCraftingRules(0, 4, 20, [], []));
        IEnumerable<ItemDefinition> items = source.Items.Select(item => item.Id == TinyFarmIds.SauteedHenOfTheWoods
            ? item with { Name = "Sauteed Hen-of-the-Woods", Food = new TinyFarmFoodEffect(3, 10) }
            : item);
        return new TinyFarmDefinitions("tinyfarm-supper-a4-v1;" + source.Identity, items,
            source.Crops, source.Scenes, source.SceneContent, source.Schedules, source.ScheduleContent,
            source.ForageNodes, source.CookingRecipes.Append(sauteed), source.Trees, source.Enemies);
    }

    /// <summary>The same morning as A3; the chapter is offered later, by Mara.</summary>
    public static TinyFarmState Start(TinyFarmDefinitions definitions)
    {
        TinyFarmState source = TinyFarmShippingContent.Start(definitions);
        return new TinyFarmState(source.Version, source.Minute, source.Actors, source.Items, source.Facts, source.Favor,
            definitions.Identity, source.InventoryStacks, source.ShopStock, source.FarmPlots, source.ActorScenes,
            source.ActorEnergy, source.SelectedHotbarSlot, source.ForageNodes, source.Trees, source.Enemies,
            source.Slice, source.Equipment);
    }
}
