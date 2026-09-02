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
            && state.Version != TinyFarmState.ContinuousSceneSaveVersion)
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
        }

        foreach (ItemState item in state.Items)
        {
            bool hasOwner = item.Owner is not null;
            bool isGrounded = item.GroundLocation is not null;
            if (hasOwner == isGrounded)
            {
                throw new InvalidDataException($"Item '{item.Id}' must have exactly one container.");
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
    }

    private static string RuntimeVersionFor(int gameVersion)
    {
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
