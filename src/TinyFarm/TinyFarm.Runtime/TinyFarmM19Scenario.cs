using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM19Evidence(
    object Proof,
    object Cooking,
    object Recipes,
    object Inventory,
    object Manifest);

public static class TinyFarmM19ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions, bool hasIngredient = true)
    {
        TinyFarmState baseline = TinyFarmM18ControlStates.Create(definitions);
        ActorState[] actors = baseline.Actors.Select(actor => actor.Id == TinyFarmIds.Player
            ? actor with { Location = TinyFarmIds.Farmhouse, Inventory = actor.Inventory.ToList() }
            : actor with { Inventory = actor.Inventory.ToList() }).ToArray();
        ActorSceneState[] placements = baseline.ActorScenes.Select(placement =>
            placement.Actor == TinyFarmIds.Player
                ? placement with
                {
                    Scene = TinyFarmSceneIds.Residence,
                    WorldPosition = ScenePosition.FromGrid(new GridPosition(5, 4)),
                    Facing = ActorFacing.Right
                }
                : placement).ToArray();
        List<InventoryStack> inventory = baseline.InventoryStacks
            .Where(stack => stack.Actor != TinyFarmIds.Player
                || stack.Product != TinyFarmIds.HenOfTheWoods
                && stack.Product != TinyFarmIds.SauteedHenOfTheWoods)
            .ToList();
        if (hasIngredient)
        {
            inventory.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods, 1));
        }
        return new TinyFarmState(
            TinyFarmState.ForageSaveVersion,
            baseline.Minute,
            actors,
            baseline.Items.ToList(),
            baseline.Facts.ToList(),
            baseline.Favor,
            definitions.Identity,
            inventory,
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            baseline.SelectedHotbarSlot,
            baseline.ForageNodes.ToList());
    }
}

public static class TinyFarmM19Scenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM19Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM19();
        RunResult human = Run(definitions, IntentSourceKind.Human);
        RunResult repeat = Run(definitions, IntentSourceKind.Human);
        RunResult replay = Run(definitions, IntentSourceKind.Replay);
        string composedHash = ProveGatherThenCook(definitions);
        bool replayExact = human.StateHash == replay.StateHash
            && human.ResultsHash == replay.ResultsHash
            && human.EventsHash == replay.EventsHash;
        bool repeatExact = human.StateHash == repeat.StateHash
            && human.ResultsHash == repeat.ResultsHash
            && human.EventsHash == repeat.EventsHash;
        bool success = human.First.Status == IntentResultStatus.Accepted
            && human.First.Events.Single().Kind == GameEventKind.RecipeCooked
            && human.Second.Reason == IntentReason.MissingIngredient
            && human.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods) == 0
            && human.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods) == 1
            && human.InitialSelectedSlot == human.Final.SelectedHotbarSlot
            && human.StateHash == human.LoadedHash
            && repeatExact
            && replayExact;
        object hashes = new
        {
            state = human.StateHash,
            results = human.ResultsHash,
            events = human.EventsHash,
            recipes = Hash(definitions.CookingRecipes),
            products = Hash(definitions.Items),
            inventory = Hash(human.Final.InventoryStacks),
            projection = human.ProjectionHash,
            dto = human.DtoHash,
            replay = replay.StateHash,
            m18ToM19ComposedLoop = composedHash
        };
        return new TinyFarmM19Evidence(
            new
            {
                milestone = "TINY-FARM-M19",
                outcome = success ? "A" : "B",
                recipeAuthoredInTson = true,
                cookUsesResolver = true,
                atomicIngredientTransformation = true,
                saveLoadExact = human.StateHash == human.LoadedHash,
                replayExact,
                repeatExact,
                hashes
            },
            new
            {
                station = TinyFarmIds.HearthHouseKitchen.Value,
                scene = TinyFarmSceneIds.Residence.Value,
                lowering = $"{nameof(InteractIntent)} -> {nameof(CookIntent)}",
                priority = "actor, portal, ground item, forage node, plot, cooking station, shop; then squared distance; then ordinal stable ID",
                first = human.First.Status,
                second = human.Second.Reason,
                eventKind = human.First.Events.Single().Kind
            },
            new
            {
                count = definitions.CookingRecipes.Count,
                definitions.CookingRecipes,
                definitionHash = Hash(definitions.CookingRecipes),
                futureMultiRecipeUiSeam = "When a station has more than one available recipe, present explicit recipe selection.",
                futureCraftingSeam = "Station + flat RecipeDefinition + inputs resolves as a station-specific transformation verb."
            },
            new
            {
                rawCount = human.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods),
                cookedCount = human.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods),
                selectedHotbarSlotBefore = human.InitialSelectedSlot,
                selectedHotbarSlotAfter = human.Final.SelectedHotbarSlot,
                human.AfterUi.Inventory
            },
            new
            {
                milestone = "TINY-FARM-M19",
                kind = "hen-of-the-woods-cooking",
                recipeCount = 1,
                recipeAuthoredInTson = true,
                cookUsesResolver = true,
                atomicIngredientTransformation = true,
                inventoryAuthorityMoved = false,
                genericCraftingGraphAdded = false,
                recipeEditorAdded = false,
                qualitySystemAdded = false,
                skillSystemAdded = false,
                foodEffectsAdded = false
            });
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        TinyFarmM19Evidence evidence = Prove();
        Write(Path.Combine(directory, "proof.json"), evidence.Proof);
        Write(Path.Combine(directory, "cooking.json"), evidence.Cooking);
        Write(Path.Combine(directory, "recipes.json"), evidence.Recipes);
        Write(Path.Combine(directory, "inventory.json"), evidence.Inventory);
        Write(Path.Combine(directory, "manifest.json"), evidence.Manifest);
    }

    public static string WriteJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static RunResult Run(TinyFarmDefinitions definitions, IntentSourceKind source)
    {
        TinyFarmState initial = TinyFarmM19ControlStates.Create(definitions);
        int selectedSlot = initial.SelectedHotbarSlot;
        var resolver = new TinyFarmResolver(definitions);
        ResolutionBatchResult firstBatch = resolver.Resolve(initial,
        [
            new IntentEnvelope(TinyFarmIds.Player, new InteractIntent(), initial.Minute, 0, source)
        ]);
        IntentResult first = firstBatch.Results.Single();
        ResolutionBatchResult secondBatch = resolver.Resolve(firstBatch.State,
        [
            new IntentEnvelope(
                TinyFarmIds.Player,
                new CookIntent(TinyFarmIds.HearthHouseKitchen, TinyFarmIds.SauteedHenOfTheWoodsRecipe),
                firstBatch.State.Minute,
                1,
                source)
        ]);
        TinyFarmState final = secondBatch.State;
        IntentResult second = secondBatch.Results.Single();
        TinyFarmPlayerUiView ui = TinyFarmPlayerUiProjector.Project(final, definitions);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(final, definitions);
        var session = new TinyFarmSession(final, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, definitions),
            definitions);
        var host = new TinyFarmSimulationHost(session, definitions, TinyFarmSimulationMode.Playing);
        IntentResult[] results = [first, second];
        GameEvent[] events = results.SelectMany(result => result.Events).ToArray();
        return new RunResult(
            first,
            second,
            final,
            ui,
            selectedSlot,
            TinyFarmSemanticHash.Compute(final),
            TinyFarmSemanticHash.Compute(loaded.State),
            Hash(results.Select(result => new
            {
                intent = result.Envelope.Intent.GetType().Name,
                result.Status,
                result.Reason
            })),
            Hash(events),
            TinyFarmFrameProjector.ComputeHash(frame),
            TinyFarmSimulationSnapshotProjector.ComputeTsonHash(host.Snapshot()));
    }

    private static string ProveGatherThenCook(TinyFarmDefinitions definitions)
    {
        var session = new TinyFarmSession(TinyFarmM18ControlStates.Create(definitions), definitions);
        IntentResult gather = session.Step(new InteractIntent(), evaluateNpcDecisions: false).Results.Single();
        TinyFarmState cookingState = session.State.DeepCopy();
        int actorIndex = cookingState.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        cookingState.MutableActors[actorIndex] = cookingState.MutableActors[actorIndex] with
        {
            Location = TinyFarmIds.Farmhouse
        };
        int placementIndex = cookingState.MutableActorScenes.FindIndex(placement =>
            placement.Actor == TinyFarmIds.Player);
        cookingState.MutableActorScenes[placementIndex] = cookingState.MutableActorScenes[placementIndex] with
        {
            Scene = TinyFarmSceneIds.Residence,
            WorldPosition = ScenePosition.FromGrid(new GridPosition(5, 4)),
            Facing = ActorFacing.Right
        };
        var cookingSession = new TinyFarmSession(cookingState, definitions);
        IntentResult cook = cookingSession.Step(new InteractIntent(), evaluateNpcDecisions: false).Results.Single();
        if (gather.Status != IntentResultStatus.Accepted || cook.Status != IntentResultStatus.Accepted)
        {
            throw new InvalidOperationException("M18 to M19 composed loop did not resolve both semantic actions.");
        }
        return Hash(new
        {
            gather = gather.Events,
            cook = cook.Events,
            cookingSession.State.InventoryStacks
        });
    }

    private static string Hash(object value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static void Write(string path, object value)
    {
        File.WriteAllText(path, WriteJson(value) + Environment.NewLine);
    }

    private sealed record RunResult(
        IntentResult First,
        IntentResult Second,
        TinyFarmState Final,
        TinyFarmPlayerUiView AfterUi,
        int InitialSelectedSlot,
        string StateHash,
        string LoadedHash,
        string ResultsHash,
        string EventsHash,
        string ProjectionHash,
        string DtoHash);
}
