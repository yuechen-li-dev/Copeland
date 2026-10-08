namespace TinyFarm.Core;

/// <summary>A3 adds one authored object agent to the established opening campaign.</summary>
public static class TinyFarmShippingContent
{
    public static readonly ActorId Chest = new("shipping-chest");

    public static TinyFarmDefinitions Load()
    {
        TinyFarmDefinitions source = TinyFarmCraftingContent.Load();
        var scenes = new TinyFarmSceneCatalog(source.Scenes.All.Select(scene =>
        {
            if (scene.Id != TinyFarmSceneIds.Farm)
            {
                return scene;
            }
            var footprint = new SceneObjectId("shipping-chest-footprint");
            return new SceneDefinition(scene.Id, scene.Name, scene.Width, scene.Height,
                scene.Objects.Append(new SceneObjectDefinition(footprint, SceneObjectKind.Landmark,
                    "Shipping chest", true, Chest.Value)).ToArray(),
                scene.Layout.Append(new SceneLayoutRow(footprint, 7, 4, 1, 1, 0)).ToArray(),
                scene.Anchors, scene.Routes);
        }));
        return new TinyFarmDefinitions("tinyfarm-shipping-a3-v1;" + source.Identity, source.Items,
            source.Crops, scenes, source.SceneContent, source.Schedules, source.ScheduleContent,
            source.ForageNodes, source.CookingRecipes, source.Trees, source.Enemies);
    }

    public static TinyFarmState Start(TinyFarmDefinitions definitions)
    {
        TinyFarmState source = TinyFarmCraftingContent.Start(definitions);
        var initial = new TinyFarmState(TinyFarmState.ContainerSaveVersion, source.Minute,
            source.Actors, source.Items, source.Facts, source.Favor, definitions.Identity,
            source.InventoryStacks, source.ShopStock, source.FarmPlots, source.ActorScenes,
            source.ActorEnergy, source.SelectedHotbarSlot, source.ForageNodes, source.Trees,
            source.Enemies, source.Slice, source.Equipment);
        TinyFarmAgentTemplate template = TinyFarmAgentTemplate.Object("shipping-chest") with
        {
            Appearance = new TinyFarmAgentAppearance(TinyFarmAgentSprite.Chest, WalkingAnimation: false),
            Container = new TinyFarmContainerState(Shipping: true, Beneficiary: TinyFarmIds.Player)
        };
        return TinyFarmAgentAuthoring.Compile(initial, definitions,
            [new TinyFarmAgentSpawn(Chest, "Shipping Chest", TinyFarmSceneIds.Farm,
                new ScenePosition(7680, 5120), template)]);
    }
}
