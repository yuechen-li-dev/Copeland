namespace TinyFarm.Core;

/// <summary>
/// "Supper with Mara", derived from resolver-owned world state. It began as a stand-alone prototype on the M21 world
/// and is now a chapter of the Sleeping Spring: once Old Burrow is cleared and the player is home again, Mara asks for
/// a proper supper (riverside mushrooms cooked at the stove, wild mint, a seed planted for tomorrow, and one fewer
/// slime), which opens the river road.
/// </summary>
public static class TinyFarmSupper
{
    public static bool IsComplete(TinyFarmState state) => state.Facts.Contains(WorldFact.SupperCompleted);

    public static bool IsRequested(TinyFarmState state) => state.Facts.Contains(WorldFact.SupperRequested);

    public static bool HasMint(TinyFarmState state) =>
        state.Items.FirstOrDefault(item => item.Id == TinyFarmIds.WildMint)?.Owner == TinyFarmIds.Player;

    public static bool HasCookedMushrooms(TinyFarmState state) =>
        state.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods) > 0;

    public static bool SeedPlanted(TinyFarmState state) => state.Facts.Contains(WorldFact.SupperSeedPlanted);

    public static bool BurrowCleared(TinyFarmState state) =>
        state.Enemies.Any(enemy => enemy.Id == TinyFarmIds.DungeonSlime && enemy.Lifecycle == EnemyLifecycle.Defeated);

    public static bool IsReady(TinyFarmState state)
    {
        return IsRequested(state)
            && HasMint(state)
            && HasCookedMushrooms(state)
            && SeedPlanted(state)
            && BurrowCleared(state);
    }

    /// <summary>The Sleeping Spring content that carries the chapter: a craftable mushroom recipe for the stove.</summary>
    public static bool IsAuthored(TinyFarmDefinitions? definitions)
    {
        return definitions is not null
            && definitions.CookingRecipes.Any(recipe => recipe.Id == TinyFarmIds.SauteedHenOfTheWoodsRecipe && recipe.Crafting is not null);
    }

    /// <summary>Mara asks once the burrow is cleared and the player has come home from it.</summary>
    public static bool CanRequest(TinyFarmState state, TinyFarmDefinitions? definitions)
    {
        return state.Slice is { Defeats: > 0, ReturnedHome: true }
            && !IsRequested(state)
            && IsAuthored(definitions);
    }
}

/// <summary>Which roads the Sleeping Spring has opened for the player. Everything else is unfinished in this slice.</summary>
public static class TinyFarmOpeningRoutes
{
    public static bool IsOpen(TinyFarmState state, SceneId target)
    {
        if (state.Slice is null)
        {
            return true;
        }
        if (target == TinyFarmSceneIds.Farm || target == TinyFarmSceneIds.Residence
            || target == TinyFarmSceneIds.Overworld || target == TinyFarmSceneIds.DungeonEntrance)
        {
            return true;
        }
        // Mara's supper request sends the player to the river.
        return target == TinyFarmSceneIds.Riverside && TinyFarmSupper.IsRequested(state);
    }
}
