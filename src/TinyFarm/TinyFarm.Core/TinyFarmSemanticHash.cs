using System.Security.Cryptography;
using System.Text;

namespace TinyFarm.Core;

public static class TinyFarmSemanticHash
{
    public static string Compute(TinyFarmState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var canonical = new StringBuilder();
        canonical.Append("v=").Append(state.Version)
            .Append(";minute=").Append(state.Minute)
            .Append(";favor=").Append(state.Favor).AppendLine();

        foreach (ActorState actor in state.Actors.OrderBy(actor => actor.Id.Value, StringComparer.Ordinal))
        {
            canonical.Append("actor|").Append(actor.Id.Value)
                .Append('|').Append(actor.Name)
                .Append('|').Append(actor.Location.Value)
                .Append('|').Append(actor.Money)
                .Append('|').Append(actor.IsPlayer ? '1' : '0')
                .Append('|').AppendJoin(',', actor.Inventory
                    .OrderBy(item => item.Value, StringComparer.Ordinal)
                    .Select(item => item.Value))
                .AppendLine();
        }

        foreach (ItemState item in state.Items.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            canonical.Append("item|").Append(item.Id.Value)
                .Append('|').Append(item.Name)
                .Append('|').Append(item.Price)
                .Append('|').Append(item.Owner?.Value ?? "-")
                .Append('|').Append(item.GroundLocation?.Value ?? "-");
            if (state.Version >= TinyFarmState.ItemActionSaveVersion)
            {
                canonical.Append('|').Append(item.GroundScene?.Value ?? "-")
                    .Append('|').Append(item.GroundPosition?.XUnits.ToString() ?? "-")
                    .Append('|').Append(item.GroundPosition?.YUnits.ToString() ?? "-");
            }
            canonical.AppendLine();
        }

        if (state.Version >= TinyFarmState.SaveVersion)
        {
            canonical.Append("definitions|").Append(state.DefinitionSetId).AppendLine();
            IEnumerable<InventoryStack> orderedStacks = state.InventoryStacks
                .OrderBy(stack => stack.Actor.Value, StringComparer.Ordinal)
                .ThenBy(stack => stack.Product.Value, StringComparer.Ordinal);
            foreach (InventoryStack stack in orderedStacks)
            {
                canonical.Append("stack|").Append(stack.Actor.Value)
                    .Append('|').Append(stack.Product.Value)
                    .Append('|').Append(stack.Count)
                    .AppendLine();
            }

            foreach (ShopStock stock in state.ShopStock.OrderBy(stock => stock.Product.Value, StringComparer.Ordinal))
            {
                canonical.Append("stock|").Append(stock.Product.Value)
                    .Append('|').Append(stock.Count)
                    .Append('|').Append(stock.DailyRestockCount)
                    .AppendLine();
            }

            foreach (FarmPlotState plot in state.FarmPlots.OrderBy(plot => plot.Id.Value, StringComparer.Ordinal))
            {
                canonical.Append("plot|").Append(plot.Id.Value).Append('|').Append(plot.Location.Value).Append('|')
                    .Append(plot.Crop?.Value ?? "-").Append('|').Append(plot.PlantedDay?.ToString() ?? "-").Append('|')
                    .Append(plot.GrowthStage).Append('|').Append(plot.WateredToday ? '1' : '0').AppendLine();
            }
        }

        if (state.Version >= TinyFarmState.SceneSaveVersion)
        {
            foreach (ActorSceneState placement in state.ActorScenes.OrderBy(item => item.Actor.Value, StringComparer.Ordinal))
            {
                canonical.Append("scene-actor|").Append(placement.Actor.Value)
                    .Append('|').Append(placement.Scene.Value);
                if (state.Version >= TinyFarmState.ContinuousSceneSaveVersion)
                {
                    canonical.Append('|').Append(placement.WorldPosition.XUnits)
                        .Append('|').Append(placement.WorldPosition.YUnits)
                        .Append('|').Append(placement.Facing);
                }
                else
                {
                    canonical.Append('|').Append(placement.Position.X)
                        .Append('|').Append(placement.Position.Y);
                }
                canonical.AppendLine();
            }
        }

        if (state.Version >= TinyFarmState.EnergySaveVersion)
        {
            foreach (ActorEnergyState energy in state.ActorEnergy.OrderBy(item => item.Actor.Value, StringComparer.Ordinal))
            {
                canonical.Append("energy|").Append(energy.Actor.Value)
                    .Append('|').Append(energy.Energy)
                    .Append('|').Append(energy.IsResting ? '1' : '0')
                    .AppendLine();
            }
        }

        if (state.Version >= TinyFarmState.PlayerUiSaveVersion)
        {
            canonical.Append("selected-hotbar-slot|")
                .Append(state.SelectedHotbarSlot)
                .AppendLine();
        }

        if (state.Version >= TinyFarmState.ForageSaveVersion)
        {
            foreach (ForageNodeState node in state.ForageNodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal))
            {
                canonical.Append("forage|").Append(node.Id.Value)
                    .Append('|').Append(node.Availability)
                    .AppendLine();
            }
        }

        if (state.Version >= TinyFarmState.WoodcuttingSaveVersion)
        {
            foreach (TreeState tree in state.Trees.OrderBy(tree => tree.Id.Value, StringComparer.Ordinal))
            {
                canonical.Append("tree|").Append(tree.Id.Value)
                    .Append('|').Append(tree.Availability)
                    .AppendLine();
            }
        }
        if (state.Version >= TinyFarmState.DungeonCombatSaveVersion)
        {
            foreach (EnemyState enemy in state.Enemies.OrderBy(enemy => enemy.Id.Value, StringComparer.Ordinal))
            {
                canonical.Append("enemy|").Append(enemy.Id.Value)
                    .Append('|').Append(enemy.CurrentHealth)
                    .AppendLine();
            }
        }

        canonical.Append("facts|").AppendJoin(',', state.Facts.OrderBy(fact => fact));
        byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
