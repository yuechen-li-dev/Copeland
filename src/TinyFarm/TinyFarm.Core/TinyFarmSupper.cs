namespace TinyFarm.Core;

/// <summary>The one authored supper objective, derived from resolver-owned world state.</summary>
public static class TinyFarmSupper
{
    public static bool IsComplete(TinyFarmState state) => state.Facts.Contains(WorldFact.SupperCompleted);

    public static bool IsReady(TinyFarmState state)
    {
        return state.Facts.Contains(WorldFact.SupperRequested)
            && state.Item(TinyFarmIds.WildMint).Owner == TinyFarmIds.Player
            && state.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods) > 0
            && state.Facts.Contains(WorldFact.SupperSeedPlanted)
            && state.Enemies.Any(enemy => enemy.Id == TinyFarmIds.DungeonSlime && enemy.Lifecycle == EnemyLifecycle.Defeated);
    }
}
