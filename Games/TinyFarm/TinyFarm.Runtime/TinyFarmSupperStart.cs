namespace TinyFarm.Core;

public static class TinyFarmSupperStart
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM21ControlStates.Create(definitions);
        return new TinyFarmState(
            baseline.Version,
            690,
            baseline.Actors.Select(actor => actor with
            {
                Location = actor.IsPlayer ? TinyFarmIds.Farmhouse : actor.Location,
                Inventory = actor.Inventory.ToList()
            }).ToArray(),
            baseline.Items.ToArray(),
            [WorldFact.SupperRequested],
            FavorStage.NotStarted,
            definitions.Identity,
            baseline.InventoryStacks.ToArray(),
            baseline.ShopStock.ToArray(),
            baseline.FarmPlots.Select(plot => plot with
            {
                Crop = null,
                PlantedDay = null,
                GrowthStage = 0,
                WateredToday = false
            }).ToArray(),
            baseline.ActorScenes.Select(actor => actor.Actor == TinyFarmIds.Player
                ? actor with
                {
                    Scene = TinyFarmSceneIds.Farm,
                    WorldPosition = ScenePosition.FromGrid(new GridPosition(6, 7)),
                    Facing = ActorFacing.Up
                }
                : actor).ToArray(),
            baseline.ActorEnergy.ToArray(),
            1,
            baseline.ForageNodes.ToArray(),
            baseline.Trees.ToArray(),
            baseline.Enemies.ToArray());
    }
}
