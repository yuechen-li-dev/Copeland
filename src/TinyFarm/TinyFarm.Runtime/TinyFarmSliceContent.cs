namespace TinyFarm.Core;

/// <summary>The small, authored opening campaign. Existing content retains its identity and proof paths.</summary>
public static class TinyFarmSliceContent
{
    public static TinyFarmDefinitions Load()
    {
        TinyFarmDefinitions source = TinyFarmDefinitionLoader.LoadM21();
        var broth = new ProductId("turnip-broth");
        return new TinyFarmDefinitions(
            "tinyfarm-sleeping-spring-gate-a-v1;" + source.Identity,
            source.Items.Append(new ItemDefinition(broth, "Turnip Broth", 0, 4)),
            source.Crops.Select(crop => crop with { GrowthDays = 1 }),
            CreateScenes(source.Scenes),
            source.SceneContent,
            CreateSchedules(source.Schedules),
            source.ScheduleContent,
            source.ForageNodes,
            [new CookingRecipeDefinition(new CookingRecipeId("turnip-broth"), CookingStationKind.Cooking,
                [new CookingRecipeInput(TinyFarmIds.Turnip, 1)], broth, 1)],
            source.Trees,
            source.Enemies.Select(enemy => enemy with { MaxHealth = 4 }));
    }

    private static TinyFarmScheduleCatalog CreateSchedules(TinyFarmScheduleCatalog source)
    {
        var windows = source.Windows.Where(window => window.StartMinute >= 1320).ToList();
        windows.Add(new TinyFarmScheduleWindow(TinyFarmIds.Mara, TinyFarmScheduleDay.EveryDay, 0, 1320,
            new SceneAnchorId("opening.mara-garden"), 100, "Opening garden neighbour"));
        windows.Add(new TinyFarmScheduleWindow(TinyFarmIds.Elias, TinyFarmScheduleDay.EveryDay, 0, 1320,
            TinyFarmAnchorIds.EliasRiversideBench, 100, "Beyond the opening route"));
        windows.Add(new TinyFarmScheduleWindow(TinyFarmIds.Sela, TinyFarmScheduleDay.EveryDay, 0, 1320,
            TinyFarmAnchorIds.StoreCounter, 100, "Beyond the opening route"));
        return new TinyFarmScheduleCatalog(windows);
    }

    private static TinyFarmSceneCatalog CreateScenes(TinyFarmSceneCatalog source)
    {
        return new TinyFarmSceneCatalog(source.All.Select(scene =>
        {
            var objects = scene.Objects.ToList();
            var layout = scene.Layout.ToList();
            if (scene.Id == TinyFarmSceneIds.Residence)
            {
                objects.Add(new SceneObjectDefinition(new SceneObjectId("player-bed"), SceneObjectKind.Bed,
                    "Your bed", true, "player"));
                layout.Add(new SceneLayoutRow(new SceneObjectId("player-bed"), 2, 5, 2, 1, 0));
            }
            if (scene.Id == TinyFarmSceneIds.Farm)
            {
                int fenceIndex = layout.FindIndex(row => row.ObjectId == new SceneObjectId("fence"));
                layout[fenceIndex] = layout[fenceIndex] with { X = 7, Y = 3, Width = 6, Height = 1 };
                int houseIndex = layout.FindIndex(row => row.ObjectId == new SceneObjectId("farmhouse"));
                // The painted front door is centered on the existing threshold at x=4.5.
                // Its wall footprint is authored separately from the overhanging roof pixels.
                layout[houseIndex] = layout[houseIndex] with { X = 3, Width = 3 };
            }
            if (scene.Id == TinyFarmSceneIds.Overworld)
            {
                objects.Add(new SceneObjectDefinition(new SceneObjectId("opening-river-north"), SceneObjectKind.Landmark, "River", true));
                objects.Add(new SceneObjectDefinition(new SceneObjectId("opening-river-south"), SceneObjectKind.Landmark, "River", true));
                layout.Add(new SceneLayoutRow(new SceneObjectId("opening-river-north"), 14, 0, 2, 7, 0));
                layout.Add(new SceneLayoutRow(new SceneObjectId("opening-river-south"), 14, 9, 2, 5, 0));
            }
            var anchors = scene.Anchors.ToList();
            if (scene.Id == TinyFarmSceneIds.Farm)
            {
                anchors.Add(new SceneAnchorDefinition(new SceneAnchorId("opening.mara-garden"), scene.Id,
                    ScenePosition.FromGrid(new GridPosition(6, 5)), SceneAnchorKind.Social, TinyFarmIds.Farmhouse));
            }
            string name = scene.Id == TinyFarmSceneIds.Overworld ? "Riverwood Approach" : scene.Name;
            return new SceneDefinition(scene.Id, name, scene.Width, scene.Height,
                objects, layout, anchors, scene.Routes);
        }));
    }

    public static TinyFarmState Start(TinyFarmDefinitions definitions)
    {
        TinyFarmState source = TinyFarmSupperStart.Create(definitions);
        FarmPlotId firstPlot = source.FarmPlots[0].Id;
        return new TinyFarmState(
            TinyFarmState.SliceSaveVersion,
            480,
            source.Actors.Select(PlaceNeighbour).ToArray(),
            source.Items.Select(item => item.Id == TinyFarmIds.WildMint
                ? item with
                {
                    GroundScene = TinyFarmSceneIds.Riverside,
                    GroundLocation = TinyFarmIds.Riverside,
                    GroundPosition = ScenePosition.FromGrid(new GridPosition(5, 5))
                }
                : item).ToArray(),
            [],
            source.Favor,
            definitions.Identity,
            [new InventoryStack(TinyFarmIds.Player, TinyFarmIds.TurnipSeed, 6)],
            source.ShopStock,
            source.FarmPlots.Select(plot => plot.Id == firstPlot
                ? plot with { Crop = new CropId("turnip"), PlantedDay = 1, GrowthStage = 1 }
                : plot).ToArray(),
            source.ActorScenes.Select(actor => PlaceNeighbourInScene(actor, definitions)).ToArray(),
            source.ActorEnergy,
            1,
            source.ForageNodes,
            source.Trees,
            definitions.Enemies.Select(enemy => new EnemyState(enemy.Id, enemy.MaxHealth)).ToArray(),
            TinyFarmSliceState.Start(definitions.Enemy(TinyFarmIds.DungeonSlime).SpawnPosition));
    }

    private static ActorState PlaceNeighbour(ActorState actor)
    {
        if (actor.Id == TinyFarmIds.Mara)
        {
            return actor with { Location = TinyFarmIds.Farmhouse };
        }
        if (actor.Id == TinyFarmIds.Elias)
        {
            return actor with { Location = TinyFarmIds.Riverside };
        }
        if (actor.Id == TinyFarmIds.Sela)
        {
            return actor with { Location = TinyFarmIds.GeneralStore };
        }
        return actor;
    }

    private static ActorSceneState PlaceNeighbourInScene(ActorSceneState actor, TinyFarmDefinitions definitions)
    {
        if (actor.Actor == TinyFarmIds.Mara)
        {
            return actor with
            {
                Scene = TinyFarmSceneIds.Farm,
                WorldPosition = ScenePosition.FromGrid(new GridPosition(6, 5)),
                Facing = ActorFacing.Down
            };
        }
        if (actor.Actor == TinyFarmIds.Elias)
        {
            return actor with
            {
                Scene = TinyFarmSceneIds.Riverside,
                WorldPosition = definitions.Scenes.GetAnchor(TinyFarmAnchorIds.EliasRiversideBench).Position
            };
        }
        if (actor.Actor == TinyFarmIds.Sela)
        {
            return actor with
            {
                Scene = TinyFarmSceneIds.GeneralStore,
                WorldPosition = definitions.Scenes.GetAnchor(TinyFarmAnchorIds.StoreCounter).Position
            };
        }
        return actor;
    }
}
