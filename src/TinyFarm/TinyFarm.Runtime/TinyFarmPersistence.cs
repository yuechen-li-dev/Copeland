using System.Text.Json;
using System.Text.Json.Serialization;
using Dominatus.Core.Persistence;

namespace TinyFarm.Core;

public sealed record TinyFarmRuntimeSave(
    long NextSequence,
    List<GameEvent> RecentEvents);

public sealed record TinyFarmAgentSave(
    string Runtime,
    string DecisionMemory);

public sealed record TinyFarmNarrativeSave(
    string Runtime,
    string AuthorityBoundary);

public sealed record TinyFarmSave(
    string RuntimeVersion,
    TinyFarmState Game,
    TinyFarmRuntimeSave Runtime,
    TinyFarmAgentSave Agents,
    TinyFarmNarrativeSave Narrative);

public static class TinyFarmSaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write(TinyFarmSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        Validate(save);
        return JsonSerializer.Serialize(save, Options);
    }

    public static TinyFarmSession Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        TinyFarmSave save = JsonSerializer.Deserialize<TinyFarmSave>(json, Options)
            ?? throw new InvalidDataException("The TinyFarm save document was empty.");
        Validate(save);
        return new TinyFarmSession(
            save.Game,
            TinyFarmDefinitionLoader.Load(),
            save.Runtime.NextSequence,
            save.Runtime.RecentEvents);
    }

    private static void Validate(TinyFarmSave save)
    {
        if (save.RuntimeVersion != "tiny-farm-m1@1")
        {
            throw new InvalidDataException($"Unsupported runtime version '{save.RuntimeVersion}'.");
        }

        if (save.Game.Version != TinyFarmState.M1SaveVersion)
        {
            throw new InvalidDataException($"Unsupported game save version {save.Game.Version}.");
        }

        string[] actorIds = save.Game.Actors.Select(actor => actor.Id.Value).ToArray();
        if (actorIds.Length != actorIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidDataException("Actor identities must be unique.");
        }

        string[] itemIds = save.Game.Items.Select(item => item.Id.Value).ToArray();
        if (itemIds.Length != itemIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidDataException("Item identities must be unique.");
        }

        foreach (ItemState item in save.Game.Items)
        {
            bool hasOwner = item.Owner is not null;
            bool isGrounded = item.GroundLocation is not null;
            if (hasOwner == isGrounded)
            {
                throw new InvalidDataException($"Item '{item.Id}' must have exactly one container.");
            }

            if (item.Owner is ActorId owner && !save.Game.Actor(owner).Inventory.Contains(item.Id))
            {
                throw new InvalidDataException($"Item '{item.Id}' disagrees with owner '{owner}'.");
            }
        }
    }
}

public static class TinyFarmChunkedSaveCodec
{
    public const string RuntimeVersion = "tiny-farm-m2@2";
    public const string SceneRuntimeVersion = "tiny-farm-m4@3";
    public const string ContinuousSceneRuntimeVersion = "tiny-farm-m5@4";
    public const string EnergyRuntimeVersion = "tiny-farm-m12@5";
    public const string PlayerUiRuntimeVersion = "tiny-farm-m16@6";
    public const string ItemActionRuntimeVersion = "tiny-farm-m17@7";
    public const string ForageRuntimeVersion = "tiny-farm-m18@8";
    public const string WoodcuttingRuntimeVersion = "tiny-farm-m20@9";
    public const string DungeonCombatRuntimeVersion = "tiny-farm-m21@10";
    public static readonly ChunkId WorldChunk = new("tinyfarm.world");
    public static readonly ChunkId RuntimeChunk = new("tinyfarm.runtime");
    public static readonly ChunkId AgentChunk = new("tinyfarm.agents");
    public static readonly ChunkId NarrativeChunk = new("tinyfarm.narrative");

    private static readonly JsonSerializerOptions ChunkOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] Write(TinyFarmSession session, TinyFarmDefinitions definitions)
    {
        ValidateWorld(session.State, definitions);
        var context = new SaveWriteContext();
        string runtimeVersion = RuntimeVersionFor(session.State.Version);
        var world = new WorldChunkModel(runtimeVersion, definitions.Identity, session.State);
        var runtime = new TinyFarmRuntimeSave(session.NextSequence, session.RecentEvents.ToList());
        var agents = new TinyFarmAgentSave(
            "dominatus-1.0.0",
            "observation-pure schedule; no duplicated world truth");
        var narrative = new TinyFarmNarrativeSave(
            "ariadne-1.0.0",
            "semantic topics in world events; prose derived");
        context.AddUtf8Json(WorldChunk, JsonSerializer.Serialize(world, ChunkOptions));
        context.AddUtf8Json(RuntimeChunk, JsonSerializer.Serialize(runtime, ChunkOptions));
        context.AddUtf8Json(AgentChunk, JsonSerializer.Serialize(agents, ChunkOptions));
        context.AddUtf8Json(NarrativeChunk, JsonSerializer.Serialize(narrative, ChunkOptions));
        return WriteContainer(context.Chunks);
    }

    public static TinyFarmSession Read(byte[] bytes, TinyFarmDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        IReadOnlyList<SaveChunk> chunks = ReadContainer(bytes);
        var context = new SaveReadContext(chunks);
        WorldChunkModel world = ReadRequired<WorldChunkModel>(context, WorldChunk);
        TinyFarmRuntimeSave runtime = ReadRequired<TinyFarmRuntimeSave>(context, RuntimeChunk);
        _ = ReadRequired<TinyFarmAgentSave>(context, AgentChunk);
        _ = ReadRequired<TinyFarmNarrativeSave>(context, NarrativeChunk);
        string expectedRuntimeVersion = RuntimeVersionFor(world.Game.Version);
        if (world.RuntimeVersion != expectedRuntimeVersion)
        {
            throw new InvalidDataException(
                $"Unsupported TinyFarm runtime version '{world.RuntimeVersion}'. Expected '{expectedRuntimeVersion}'.");
        }

        if (world.DefinitionSetId != definitions.Identity)
        {
            throw new InvalidDataException(
                $"TinyFarm definition set mismatch. Save uses '{world.DefinitionSetId}', runtime uses '{definitions.Identity}'.");
        }

        ValidateWorld(world.Game, definitions);
        return new TinyFarmSession(world.Game, definitions, runtime.NextSequence, runtime.RecentEvents);
    }

    private static T ReadRequired<T>(SaveReadContext context, ChunkId id)
    {
        if (!context.TryGetUtf8Json(id, out string json))
        {
            throw new InvalidDataException($"Required TinyFarm save chunk '{id}' is missing.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, ChunkOptions)
                ?? throw new InvalidDataException($"TinyFarm save chunk '{id}' was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"TinyFarm save chunk '{id}' contains malformed JSON.", exception);
        }
    }

    private static void ValidateWorld(TinyFarmState state, TinyFarmDefinitions definitions)
    {
        if (state.Version != TinyFarmState.SaveVersion
            && state.Version != TinyFarmState.SceneSaveVersion
            && state.Version != TinyFarmState.ContinuousSceneSaveVersion
            && state.Version != TinyFarmState.EnergySaveVersion
            && state.Version != TinyFarmState.PlayerUiSaveVersion
            && state.Version != TinyFarmState.ItemActionSaveVersion
            && state.Version != TinyFarmState.ForageSaveVersion
            && state.Version != TinyFarmState.WoodcuttingSaveVersion
            && state.Version != TinyFarmState.DungeonCombatSaveVersion)
        {
            throw new InvalidDataException($"Unsupported TinyFarm game save version {state.Version}.");
        }

        if (state.DefinitionSetId != definitions.Identity)
        {
            throw new InvalidDataException("TinyFarm world definition provenance is incompatible with the loaded content.");
        }

        if (state.Actors.Select(actor => actor.Id).Distinct().Count() != state.Actors.Count
            || state.Items.Select(item => item.Id).Distinct().Count() != state.Items.Count
            || state.FarmPlots.Select(plot => plot.Id).Distinct().Count() != state.FarmPlots.Count
            || state.ForageNodes.Select(node => node.Id).Distinct().Count() != state.ForageNodes.Count
            || state.Trees.Select(tree => tree.Id).Distinct().Count() != state.Trees.Count
            || state.Enemies.Select(enemy => enemy.Id).Distinct().Count() != state.Enemies.Count
            || state.ShopStock.Select(stock => stock.Product).Distinct().Count() != state.ShopStock.Count
            || state.InventoryStacks.Select(stack => (stack.Actor, stack.Product)).Distinct().Count() != state.InventoryStacks.Count)
        {
            throw new InvalidDataException("TinyFarm save contains duplicate semantic identities.");
        }

        foreach (ActorState actor in state.Actors)
        {
            if (!TinyFarmContent.Locations.Any(location => location.Id == actor.Location))
            {
                throw new InvalidDataException($"Actor '{actor.Id}' references unknown location '{actor.Location}'.");
            }
            if (actor.Inventory.Distinct().Count() != actor.Inventory.Count
                || actor.Inventory.Any(itemId => state.Items.SingleOrDefault(item => item.Id == itemId)?.Owner != actor.Id))
            {
                throw new InvalidDataException($"Actor '{actor.Id}' inventory disagrees with identity-item ownership.");
            }
        }

        foreach (ItemState item in state.Items)
        {
            bool hasOwner = item.Owner is not null;
            bool isGrounded = item.GroundLocation is not null;
            if (hasOwner == isGrounded)
            {
                throw new InvalidDataException($"Item '{item.Id}' must have exactly one container.");
            }
            if (item.Owner is ActorId owner
                && state.Actors.SingleOrDefault(actor => actor.Id == owner)?.Inventory.Contains(item.Id) != true)
            {
                throw new InvalidDataException($"Item '{item.Id}' disagrees with owner '{owner}'.");
            }
            if (state.Version >= TinyFarmState.ItemActionSaveVersion)
            {
                bool hasScene = item.GroundScene is not null;
                bool hasPosition = item.GroundPosition is not null;
                if (isGrounded != hasScene || isGrounded != hasPosition)
                {
                    throw new InvalidDataException(
                        $"Item '{item.Id}' scene placement must agree with its ground container.");
                }
                if (hasScene)
                {
                    SceneDefinition scene;
                    try
                    {
                        scene = definitions.Scenes.Get(item.GroundScene!.Value);
                    }
                    catch (KeyNotFoundException exception)
                    {
                        throw new InvalidDataException(
                            $"Item '{item.Id}' references unknown scene '{item.GroundScene}'.",
                            exception);
                    }
                    if (!TinyFarmScenes.IsInBounds(scene, item.GroundPosition!.Value))
                    {
                        throw new InvalidDataException($"Item '{item.Id}' has invalid scene placement.");
                    }
                }
            }
        }

        foreach (InventoryStack stack in state.InventoryStacks)
        {
            if (!state.Actors.Any(actor => actor.Id == stack.Actor) || !definitions.Items.Any(item => item.Id == stack.Product) || stack.Count <= 0)
            {
                throw new InvalidDataException($"Invalid inventory stack '{stack.Actor}/{stack.Product}'.");
            }
        }

        foreach (FarmPlotState plot in state.FarmPlots)
        {
            if (plot.Crop is CropId crop && !definitions.Crops.Any(item => item.Id == crop))
            {
                throw new InvalidDataException($"Farm plot '{plot.Id}' references unknown crop '{crop}'.");
            }
            if (!TinyFarmContent.Locations.Any(location => location.Id == plot.Location) || plot.GrowthStage < 0)
            {
                throw new InvalidDataException($"Farm plot '{plot.Id}' has invalid runtime state.");
            }
        }

        foreach (ShopStock stock in state.ShopStock)
        {
            if (!definitions.Items.Any(item => item.Id == stock.Product) || stock.Count < 0 || stock.DailyRestockCount < 0)
            {
                throw new InvalidDataException($"Invalid shop stock '{stock.Product}'.");
            }
        }

        if (state.Version >= TinyFarmState.ForageSaveVersion)
        {
            ForageNodeId[] savedNodeIds = state.ForageNodes
                .Select(node => node.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            ForageNodeId[] definitionNodeIds = definitions.ForageNodes
                .Select(node => node.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            if (!savedNodeIds.SequenceEqual(definitionNodeIds))
            {
                throw new InvalidDataException("TinyFarm forage state must match the authored forage definitions exactly.");
            }
        }

        if (state.Version >= TinyFarmState.WoodcuttingSaveVersion)
        {
            TreeId[] savedTreeIds = state.Trees
                .Select(tree => tree.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            TreeId[] definitionTreeIds = definitions.Trees
                .Select(tree => tree.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            if (!savedTreeIds.SequenceEqual(definitionTreeIds))
            {
                throw new InvalidDataException("TinyFarm tree state must match the authored tree definitions exactly.");
            }
        }

        if (state.Version >= TinyFarmState.DungeonCombatSaveVersion)
        {
            EnemyId[] savedEnemyIds = state.Enemies
                .Select(enemy => enemy.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            EnemyId[] definitionEnemyIds = definitions.Enemies
                .Select(enemy => enemy.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            if (!savedEnemyIds.SequenceEqual(definitionEnemyIds))
            {
                throw new InvalidDataException("TinyFarm enemy state must match authored enemy definitions exactly.");
            }
            foreach (EnemyState enemy in state.Enemies)
            {
                EnemyDefinition definition = definitions.Enemy(enemy.Id);
                if (enemy.CurrentHealth < 0 || enemy.CurrentHealth > definition.MaxHealth)
                {
                    throw new InvalidDataException($"Enemy '{enemy.Id}' has invalid health.");
                }
            }
        }


        if (state.Version >= TinyFarmState.SceneSaveVersion)
        {
            if (state.ActorScenes.Select(item => item.Actor).Distinct().Count() != state.Actors.Count
                || state.ActorScenes.Count != state.Actors.Count)
            {
                throw new InvalidDataException("TinyFarm scene state requires one placement per actor.");
            }

            foreach (ActorSceneState placement in state.ActorScenes)
            {
                SceneDefinition scene;
                try
                {
                    scene = definitions.Scenes.Get(placement.Scene);
                }
                catch (KeyNotFoundException exception)
                {
                    throw new InvalidDataException(
                        $"Actor '{placement.Actor}' references unknown scene '{placement.Scene}'.",
                        exception);
                }

                bool validPosition = state.Version >= TinyFarmState.ContinuousSceneSaveVersion
                    ? TinyFarmScenes.IsInBounds(scene, placement.WorldPosition)
                        && !TinyFarmScenes.IsBlocked(scene, placement.WorldPosition)
                    : TinyFarmScenes.IsInBounds(scene, placement.Position)
                        && !TinyFarmScenes.IsBlocked(scene, placement.Position);
                ActorState? actor = state.Actors.SingleOrDefault(candidate => candidate.Id == placement.Actor);
                bool sceneAgreesWithLocation = state.Version < TinyFarmState.ContinuousSceneSaveVersion
                    || actor is not null
                    && TinyFarmScenes.SceneAgreesWithLocation(placement.Scene, actor.Location);
                if (actor is null || !validPosition || !sceneAgreesWithLocation)
                {
                    throw new InvalidDataException($"Actor '{placement.Actor}' has invalid scene placement.");
                }
            }
        }

        if (state.Version >= TinyFarmState.EnergySaveVersion)
        {
            ActorId[] npcActors = state.Actors.Where(actor => !actor.IsPlayer).Select(actor => actor.Id).ToArray();
            if (state.ActorEnergy.Count != npcActors.Length
                || state.ActorEnergy.Select(item => item.Actor).Distinct().Count() != npcActors.Length
                || state.ActorEnergy.Any(item => !npcActors.Contains(item.Actor)
                    || item.Energy is < TinyFarmEnergy.MinimumUnits or > TinyFarmEnergy.MaximumUnits))
            {
                throw new InvalidDataException("TinyFarm Energy state requires one finite, bounded row per NPC.");
            }
        }

        if (state.Version >= TinyFarmState.PlayerUiSaveVersion
            && state.SelectedHotbarSlot is < 1 or > HotbarSlotId.Count)
        {
            throw new InvalidDataException("TinyFarm player UI state requires a selected hotbar slot from 1 through 8.");
        }
    }

    private static string RuntimeVersionFor(int gameVersion)
    {
        if (gameVersion >= TinyFarmState.DungeonCombatSaveVersion)
        {
            return DungeonCombatRuntimeVersion;
        }
        if (gameVersion >= TinyFarmState.WoodcuttingSaveVersion)
        {
            return WoodcuttingRuntimeVersion;
        }
        if (gameVersion >= TinyFarmState.ForageSaveVersion)
        {
            return ForageRuntimeVersion;
        }
        if (gameVersion >= TinyFarmState.ItemActionSaveVersion)
        {
            return ItemActionRuntimeVersion;
        }
        if (gameVersion >= TinyFarmState.PlayerUiSaveVersion)
        {
            return PlayerUiRuntimeVersion;
        }
        if (gameVersion >= TinyFarmState.EnergySaveVersion)
        {
            return EnergyRuntimeVersion;
        }
        if (gameVersion >= TinyFarmState.ContinuousSceneSaveVersion)
        {
            return ContinuousSceneRuntimeVersion;
        }
        return gameVersion >= TinyFarmState.SceneSaveVersion ? SceneRuntimeVersion : RuntimeVersion;
    }

    private static byte[] WriteContainer(IReadOnlyList<SaveChunk> chunks)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tinyfarm-{Guid.NewGuid():N}.save");
        try
        {
            SaveFile.Write(path, chunks);
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IReadOnlyList<SaveChunk> ReadContainer(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tinyfarm-{Guid.NewGuid():N}.save");
        try
        {
            File.WriteAllBytes(path, bytes);
            return SaveFile.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed record WorldChunkModel(string RuntimeVersion, string DefinitionSetId, TinyFarmState Game);
}
