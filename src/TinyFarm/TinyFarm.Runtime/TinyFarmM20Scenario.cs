using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM20Evidence(
    object Proof,
    object Woodcutting,
    object Hotbar,
    object Parity,
    object Manifest);

public static class TinyFarmM20ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM19ControlStates.Create(definitions, hasIngredient: false);
        ActorState[] actors = baseline.Actors.Select(actor => actor.Id == TinyFarmIds.Player
            ? actor with
            {
                Location = TinyFarmIds.Farmhouse,
                Inventory = actor.Inventory.Append(TinyFarmIds.Axe).Distinct().ToList()
            }
            : actor with { Inventory = actor.Inventory.ToList() }).ToArray();
        ActorSceneState[] placements = baseline.ActorScenes.Select(placement =>
            placement.Actor == TinyFarmIds.Player
                ? placement with
                {
                    Scene = TinyFarmSceneIds.Farm,
                    WorldPosition = ScenePosition.FromGrid(new GridPosition(10, 5)),
                    Facing = ActorFacing.Right
                }
                : placement).ToArray();
        ItemState[] items = baseline.Items
            .Where(item => item.Id != TinyFarmIds.Axe)
            .Append(new ItemState(
                TinyFarmIds.Axe,
                "Axe",
                0,
                GroundLocation: null,
                Owner: TinyFarmIds.Player))
            .ToArray();
        return new TinyFarmState(
            TinyFarmState.WoodcuttingSaveVersion,
            baseline.Minute,
            actors,
            items,
            baseline.Facts.ToList(),
            baseline.Favor,
            definitions.Identity,
            baseline.InventoryStacks
                .Where(stack => stack.Product != TinyFarmIds.Wood)
                .ToList(),
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            selectedHotbarSlot: 1,
            baseline.ForageNodes.ToList(),
            definitions.Trees.Select(tree => new TreeState(tree.Id, TreeAvailability.Standing)).ToArray());
    }
}

public static class TinyFarmM20Scenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM20Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        RunResult human = Run(definitions, IntentSourceKind.Human, useSelected: true);
        RunResult repeat = Run(definitions, IntentSourceKind.Human, useSelected: true);
        RunResult replay = Run(definitions, IntentSourceKind.Replay, useSelected: true);
        RunResult direct = Run(definitions, IntentSourceKind.Human, useSelected: false);
        string composedGameplayHash = ProveComposedGameplay(definitions);
        bool useSelectedParity = human.FinalHash == direct.FinalHash
            && human.Chop.Status == direct.Chop.Status
            && human.Chop.Reason == direct.Chop.Reason
            && human.Chop.Events.SequenceEqual(direct.Chop.Events);
        bool replayExact = human.FinalHash == replay.FinalHash
            && human.ResultsHash == replay.ResultsHash
            && human.EventsHash == replay.EventsHash;
        bool repeatExact = human.FinalHash == repeat.FinalHash
            && human.ResultsHash == repeat.ResultsHash
            && human.EventsHash == repeat.EventsHash;
        bool success = human.WrongTool.Reason == IntentReason.WrongTool
            && human.Chop.Status == IntentResultStatus.Accepted
            && human.Second.Reason == IntentReason.AlreadyDepleted
            && human.Final.Tree(TinyFarmIds.FarmTree).Availability == TreeAvailability.Depleted
            && human.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.Wood) == 1
            && human.Final.Actor(TinyFarmIds.Player).Inventory.Contains(TinyFarmIds.Axe)
            && human.Final.SelectedHotbarSlot == 3
            && human.Chop.Events.Single().Kind == GameEventKind.TreeChopped
            && human.FinalHash == human.LoadedHash
            && useSelectedParity
            && replayExact
            && repeatExact;
        object hashes = new
        {
            state = human.FinalHash,
            results = human.ResultsHash,
            events = human.EventsHash,
            tree = Hash(human.Final.Trees),
            wood = Hash(human.Final.InventoryStacks.Where(stack => stack.Product == TinyFarmIds.Wood)),
            hotbar = human.HotbarHash,
            useSelectedParity = Hash(new { human = human.FinalHash, direct = direct.FinalHash }),
            chopParity = Hash(new { human.Chop.Status, human.Chop.Reason, human.Chop.Events }),
            projection = human.ProjectionHash,
            dto = human.DtoHash,
            replay = replay.FinalHash,
            definitions = Hash(new { definitions.Identity, definitions.Items, definitions.Trees })
        };
        return new TinyFarmM20Evidence(
            new
            {
                milestone = "TINY-FARM-M20",
                outcome = success ? "A" : "B",
                saveLoadExact = human.FinalHash == human.LoadedHash,
                replayExact,
                repeatExact,
                useSelectedParity,
                hashes
            },
            new
            {
                tool = TinyFarmIds.Axe.Value,
                tree = TinyFarmIds.FarmTree.Value,
                product = TinyFarmIds.Wood.Value,
                yieldCount = 1,
                wrongTool = human.WrongTool.Reason,
                firstChop = human.Chop.Status,
                secondChop = human.Second.Reason,
                finalTree = human.Final.Tree(TinyFarmIds.FarmTree).Availability,
                woodCount = human.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.Wood),
                eventValue = human.Chop.Events.Single()
            },
            new
            {
                slots = TinyFarmPlayerUiProjector.Project(human.Final, definitions).Hotbar,
                selectedSlot = human.Final.SelectedHotbarSlot,
                axeOwned = human.Final.Actor(TinyFarmIds.Player).Inventory.Contains(TinyFarmIds.Axe),
                axeConsumed = false
            },
            new
            {
                useSelectedParity,
                replayExact,
                repeatExact,
                composedGameplayHash,
                human = human.FinalHash,
                direct = direct.FinalHash,
                replay = replay.FinalHash
            },
            new
            {
                milestone = "TINY-FARM-M20",
                kind = "axe-gated-woodcutting",
                tool = "axe",
                resource = "wood",
                toolUsesIdentityItem = true,
                hotbarSupportsItemBinding = true,
                useSelectedLowersToChop = true,
                chopUsesResolver = true,
                treeDepletionAuthoritative = true,
                axeDurabilityAdded = false,
                toolStatsAdded = false,
                skillSystemAdded = false,
                genericToolFrameworkAdded = false,
                genericResourceFrameworkAdded = false,
                combatAdded = false
            });
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        TinyFarmM20Evidence evidence = Prove();
        Write(Path.Combine(directory, "proof.json"), evidence.Proof);
        Write(Path.Combine(directory, "woodcutting.json"), evidence.Woodcutting);
        Write(Path.Combine(directory, "hotbar.json"), evidence.Hotbar);
        Write(Path.Combine(directory, "parity.json"), evidence.Parity);
        Write(Path.Combine(directory, "manifest.json"), evidence.Manifest);
    }

    public static string WriteJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static RunResult Run(
        TinyFarmDefinitions definitions,
        IntentSourceKind source,
        bool useSelected)
    {
        TinyFarmState initial = TinyFarmM20ControlStates.Create(definitions);
        var resolver = new TinyFarmResolver(definitions);
        ResolutionBatchResult wrong = resolver.Resolve(initial,
        [
            Envelope(initial, new UseSelectedIntent(), 0, source)
        ]);
        ResolutionBatchResult selected = resolver.Resolve(wrong.State,
        [
            Envelope(wrong.State, new SelectHotbarSlotIntent(new HotbarSlotId(3)), 1, source)
        ]);
        ResolutionBatchResult chopped = resolver.Resolve(selected.State,
        [
            Envelope(
                selected.State,
                useSelected ? new UseSelectedIntent() : new ChopIntent(TinyFarmIds.FarmTree),
                2,
                source)
        ]);
        ResolutionBatchResult second = resolver.Resolve(chopped.State,
        [
            Envelope(chopped.State, new ChopIntent(TinyFarmIds.FarmTree), 3, source)
        ]);
        TinyFarmState final = second.State;
        var session = new TinyFarmSession(final, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, definitions),
            definitions);
        var host = new TinyFarmSimulationHost(session, definitions, TinyFarmSimulationMode.Playing);
        IntentResult[] results =
        [
            wrong.Results.Single(),
            selected.Results.Single(),
            chopped.Results.Single(),
            second.Results.Single()
        ];
        return new RunResult(
            results[0],
            results[2],
            results[3],
            final,
            TinyFarmSemanticHash.Compute(final),
            TinyFarmSemanticHash.Compute(loaded.State),
            Hash(results.Select(result => new { result.Status, result.Reason })),
            Hash(results.SelectMany(result => result.Events)),
            TinyFarmFrameProjector.ComputeHash(TinyFarmFrameProjector.Project(final, definitions)),
            TinyFarmSimulationSnapshotProjector.ComputeTsonHash(host.Snapshot()),
            Hash(TinyFarmPlayerUiProjector.Project(final, definitions).Hotbar));
    }

    private static string ProveComposedGameplay(TinyFarmDefinitions definitions)
    {
        var session = new TinyFarmSession(TinyFarmM20ControlStates.Create(definitions), definitions);
        session.Step(new SelectHotbarSlotIntent(new HotbarSlotId(3)), evaluateNpcDecisions: false);
        IntentResult chop = session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();
        TinyFarmState riverside = session.State.DeepCopy();
        PlacePlayer(
            riverside,
            TinyFarmIds.Riverside,
            TinyFarmSceneIds.Riverside,
            new ScenePosition(5632, 6656),
            ActorFacing.Right);
        var forageSession = new TinyFarmSession(riverside, definitions);
        IntentResult gather = forageSession.Step(
            new GatherIntent(TinyFarmIds.RiversideHenOfTheWoods),
            evaluateNpcDecisions: false).Results.Single();
        TinyFarmState residence = forageSession.State.DeepCopy();
        PlacePlayer(
            residence,
            TinyFarmIds.Farmhouse,
            TinyFarmSceneIds.Residence,
            ScenePosition.FromGrid(new GridPosition(5, 4)),
            ActorFacing.Right);
        var cookingSession = new TinyFarmSession(residence, definitions);
        IntentResult cook = cookingSession.Step(
            new CookIntent(TinyFarmIds.HearthHouseKitchen, TinyFarmIds.SauteedHenOfTheWoodsRecipe),
            evaluateNpcDecisions: false).Results.Single();
        if (chop.Status != IntentResultStatus.Accepted
            || gather.Status != IntentResultStatus.Accepted
            || cook.Status != IntentResultStatus.Accepted)
        {
            throw new InvalidOperationException("M20 composed chop, forage, and cook story did not resolve every semantic verb.");
        }
        return Hash(new
        {
            chopEvents = chop.Events,
            gatherEvents = gather.Events,
            cookEvents = cook.Events,
            cookingSession.State.InventoryStacks,
            cookingSession.State.Trees,
            cookingSession.State.ForageNodes
        });
    }

    private static void PlacePlayer(
        TinyFarmState state,
        LocationId location,
        SceneId scene,
        ScenePosition position,
        ActorFacing facing)
    {
        int actorIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with { Location = location };
        int placementIndex = state.MutableActorScenes.FindIndex(placement => placement.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placementIndex] = state.MutableActorScenes[placementIndex] with
        {
            Scene = scene,
            WorldPosition = position,
            Facing = facing
        };
    }

    private static IntentEnvelope Envelope(
        TinyFarmState state,
        GameIntent intent,
        long sequence,
        IntentSourceKind source)
    {
        return new IntentEnvelope(TinyFarmIds.Player, intent, state.Minute, sequence, source);
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
        IntentResult WrongTool,
        IntentResult Chop,
        IntentResult Second,
        TinyFarmState Final,
        string FinalHash,
        string LoadedHash,
        string ResultsHash,
        string EventsHash,
        string ProjectionHash,
        string DtoHash,
        string HotbarHash);
}
