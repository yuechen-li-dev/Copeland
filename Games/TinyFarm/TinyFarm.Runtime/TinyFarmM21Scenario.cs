using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM21Evidence(
    object Proof,
    object Dungeon,
    object Combat,
    object Parity,
    object Manifest);

public static class TinyFarmM21ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM20ControlStates.Create(definitions);
        ActorState[] actors = baseline.Actors.Select(actor => actor.Id == TinyFarmIds.Player
            ? actor with
            {
                Location = TinyFarmIds.TownSquare,
                Inventory = actor.Inventory.Append(TinyFarmIds.Sword).Distinct().ToList()
            }
            : actor with { Inventory = actor.Inventory.ToList() }).ToArray();
        ActorSceneState[] placements = baseline.ActorScenes.Select(placement =>
            placement.Actor == TinyFarmIds.Player
                ? placement with
                {
                    Scene = TinyFarmSceneIds.DungeonEntrance,
                    WorldPosition = ScenePosition.FromGrid(new GridPosition(7, 5)),
                    Facing = ActorFacing.Right
                }
                : placement).ToArray();
        ItemState[] items = baseline.Items
            .Where(item => item.Id != TinyFarmIds.Sword)
            .Append(new ItemState(
                TinyFarmIds.Sword,
                "Sword",
                0,
                GroundLocation: null,
                Owner: TinyFarmIds.Player))
            .ToArray();
        return new TinyFarmState(
            TinyFarmState.DungeonCombatSaveVersion,
            baseline.Minute,
            actors,
            items,
            baseline.Facts.ToList(),
            baseline.Favor,
            definitions.Identity,
            baseline.InventoryStacks.ToList(),
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            selectedHotbarSlot: 3,
            baseline.ForageNodes.ToList(),
            baseline.Trees.ToList(),
            definitions.Enemies.Select(enemy => new EnemyState(enemy.Id, enemy.MaxHealth)).ToArray());
    }
}

public static class TinyFarmM21Scenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM21Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        RunResult human = Run(definitions, IntentSourceKind.Human, useSelected: true);
        RunResult repeat = Run(definitions, IntentSourceKind.Human, useSelected: true);
        RunResult replay = Run(definitions, IntentSourceKind.Replay, useSelected: true);
        RunResult direct = Run(definitions, IntentSourceKind.Human, useSelected: false);
        object route = ProveRoute(definitions);
        string peacefulIsolationHash = ProvePeacefulIsolation(definitions);
        string composedGameplayHash = ProveComposedGameplay(definitions);
        bool useSelectedParity = human.FinalHash == direct.FinalHash
            && human.Attack.Status == direct.Attack.Status
            && human.Attack.Reason == direct.Attack.Reason
            && human.Attack.Events.SequenceEqual(direct.Attack.Events);
        bool replayExact = human.FinalHash == replay.FinalHash
            && human.ResultsHash == replay.ResultsHash
            && human.EventsHash == replay.EventsHash;
        bool repeatExact = human.FinalHash == repeat.FinalHash
            && human.ResultsHash == repeat.ResultsHash
            && human.EventsHash == repeat.EventsHash;
        bool success = human.WrongWeapon.Reason == IntentReason.WrongWeapon
            && human.Attack.Status == IntentResultStatus.Accepted
            && human.Second.Reason == IntentReason.AlreadyDefeated
            && human.Final.Enemy(TinyFarmIds.DungeonSlime).Lifecycle == EnemyLifecycle.Defeated
            && human.Final.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth == 0
            && human.Final.Actor(TinyFarmIds.Player).Inventory.Contains(TinyFarmIds.Sword)
            && human.Final.SelectedHotbarSlot == 4
            && human.Attack.Events.Single().Kind == GameEventKind.EnemyDefeated
            && human.FinalHash == human.LoadedHash
            && useSelectedParity
            && replayExact
            && repeatExact;
        object hashes = new
        {
            state = human.FinalHash,
            results = human.ResultsHash,
            events = human.EventsHash,
            enemy = Hash(human.Final.Enemies),
            combat = Hash(new { human.Attack.Status, human.Attack.Reason, human.Attack.Events }),
            hotbar = human.HotbarHash,
            attackParity = Hash(new { human = human.FinalHash, direct = direct.FinalHash }),
            sceneDefinitions = Hash(definitions.Scenes.All),
            routes = Hash(route),
            projection = human.ProjectionHash,
            dto = human.DtoHash,
            replay = replay.FinalHash,
            peacefulIsolation = peacefulIsolationHash
        };
        return new TinyFarmM21Evidence(
            new
            {
                milestone = "TINY-FARM-M21",
                outcome = success ? "A" : "B",
                saveLoadExact = human.FinalHash == human.LoadedHash,
                replayExact,
                repeatExact,
                useSelectedParity,
                hashes
            },
            new
            {
                scene = TinyFarmSceneIds.DungeonEntrance.Value,
                displayName = definitions.Scenes.Get(TinyFarmSceneIds.DungeonEntrance).Name,
                dimensions = "16x12",
                enemyCount = definitions.Enemies.Count,
                route
            },
            new
            {
                weapon = TinyFarmIds.Sword.Value,
                target = TinyFarmIds.DungeonSlime.Value,
                wrongWeapon = human.WrongWeapon.Reason,
                firstAttack = human.Attack.Status,
                secondAttack = human.Second.Reason,
                remainingHealth = human.Final.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth,
                lifecycle = human.Final.Enemy(TinyFarmIds.DungeonSlime).Lifecycle,
                eventValue = human.Attack.Events.Single()
            },
            new
            {
                useSelectedParity,
                replayExact,
                repeatExact,
                peacefulIsolationHash,
                composedGameplayHash,
                human = human.FinalHash,
                direct = direct.FinalHash,
                replay = replay.FinalHash
            },
            new
            {
                milestone = "TINY-FARM-M21",
                kind = "first-dungeon-sword-slime-combat",
                dungeonSceneAdded = true,
                enemyCount = 1,
                weapon = "sword",
                enemy = "slime",
                attackUsesResolver = true,
                useSelectedLowersToAttack = true,
                enemyHealthAdded = true,
                enemyDefeatAdded = true,
                friendlyAttackAllowed = false,
                playerHealthAdded = false,
                enemyAiAdded = false,
                enemyAttackAdded = false,
                cooldownAdded = false,
                lootAdded = false,
                xpAdded = false,
                genericCombatFrameworkAdded = false
            });
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        TinyFarmM21Evidence evidence = Prove();
        Write(Path.Combine(directory, "proof.json"), evidence.Proof);
        Write(Path.Combine(directory, "dungeon.json"), evidence.Dungeon);
        Write(Path.Combine(directory, "combat.json"), evidence.Combat);
        Write(Path.Combine(directory, "parity.json"), evidence.Parity);
        Write(Path.Combine(directory, "manifest.json"), evidence.Manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static RunResult Run(
        TinyFarmDefinitions definitions,
        IntentSourceKind source,
        bool useSelected)
    {
        TinyFarmState initial = TinyFarmM21ControlStates.Create(definitions);
        var resolver = new TinyFarmResolver(definitions);
        ResolutionBatchResult wrong = resolver.Resolve(initial,
        [
            Envelope(initial, new UseSelectedIntent(), 0, source)
        ]);
        ResolutionBatchResult selected = resolver.Resolve(wrong.State,
        [
            Envelope(wrong.State, new SelectHotbarSlotIntent(new HotbarSlotId(4)), 1, source)
        ]);
        ResolutionBatchResult attacked = resolver.Resolve(selected.State,
        [
            Envelope(
                selected.State,
                useSelected ? new UseSelectedIntent() : new AttackIntent(TinyFarmIds.DungeonSlime),
                2,
                source)
        ]);
        ResolutionBatchResult second = resolver.Resolve(attacked.State,
        [
            Envelope(attacked.State, new AttackIntent(TinyFarmIds.DungeonSlime), 3, source)
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
            attacked.Results.Single(),
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

    private static object ProveRoute(TinyFarmDefinitions definitions)
    {
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        PlacePlayer(state, TinyFarmSceneIds.Overworld, new GridPosition(18, 2), ActorFacing.Right);
        var session = new TinyFarmSession(state, definitions);
        IntentResult enter = session.Step(
            new InteractIntent(new SceneObjectId("dungeon-entrance")),
            evaluateNpcDecisions: false).Results.Single();
        SceneId enteredScene = session.State.CurrentScene!.Value;
        int playerPlacement = session.State.MutableActorScenes.FindIndex(placement =>
            placement.Actor == TinyFarmIds.Player);
        session.State.MutableActorScenes[playerPlacement] = session.State.MutableActorScenes[playerPlacement] with
        {
            Facing = ActorFacing.Left
        };
        IntentResult leave = session.Step(
            new InteractIntent(new SceneObjectId("dungeon-exit")),
            evaluateNpcDecisions: false).Results.Single();
        if (enter.Status != IntentResultStatus.Accepted
            || enteredScene != TinyFarmSceneIds.DungeonEntrance
            || leave.Status != IntentResultStatus.Accepted
            || session.State.CurrentScene != TinyFarmSceneIds.Overworld)
        {
            throw new InvalidOperationException(
                $"M21 authored dungeon route proof failed: enter={enter.Status}/{enter.Reason}, entered={enteredScene}, leave={leave.Status}/{leave.Reason}, final={session.State.CurrentScene}.");
        }
        return new
        {
            enter = enter.Events,
            enteredScene,
            leave = leave.Events,
            finalScene = session.State.CurrentScene
        };
    }

    private static string ProvePeacefulIsolation(TinyFarmDefinitions definitions)
    {
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        PlacePlayer(state, TinyFarmSceneIds.Farm, new GridPosition(6, 6), ActorFacing.Down);
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(state, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        TinyFarmHostAdvanceResult result = host.AdvanceHostTime(TimeSpan.FromSeconds(10));
        if (result.Results.SelectMany(item => item.Events).Any(item => item.Kind == GameEventKind.EnemyDefeated))
        {
            throw new InvalidOperationException("Peaceful interval emitted a combat event.");
        }
        return Hash(new
        {
            host.Session.State.Minute,
            host.Session.State.ActorScenes,
            host.Session.State.ActorEnergy,
            enemies = host.Session.State.Enemies,
            combatEvents = 0
        });
    }

    private static string ProveComposedGameplay(TinyFarmDefinitions definitions)
    {
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        PlacePlayer(state, TinyFarmSceneIds.Farm, new GridPosition(10, 5), ActorFacing.Right);
        var session = new TinyFarmSession(state, definitions);
        session.Step(new SelectHotbarSlotIntent(new HotbarSlotId(3)), evaluateNpcDecisions: false);
        IntentResult chop = session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();

        PlacePlayer(session.State, TinyFarmSceneIds.Riverside, new GridPosition(5, 6), ActorFacing.Right);
        IntentResult gather = session.Step(
            new GatherIntent(TinyFarmIds.RiversideHenOfTheWoods),
            evaluateNpcDecisions: false).Results.Single();

        PlacePlayer(session.State, TinyFarmSceneIds.Residence, new GridPosition(5, 4), ActorFacing.Right);
        IntentResult cook = session.Step(
            new CookIntent(TinyFarmIds.HearthHouseKitchen, TinyFarmIds.SauteedHenOfTheWoodsRecipe),
            evaluateNpcDecisions: false).Results.Single();

        PlacePlayer(session.State, TinyFarmSceneIds.Overworld, new GridPosition(18, 2), ActorFacing.Right);
        IntentResult enter = session.Step(
            new InteractIntent(new SceneObjectId("dungeon-entrance")),
            evaluateNpcDecisions: false).Results.Single();
        PlacePlayer(session.State, TinyFarmSceneIds.DungeonEntrance, new GridPosition(7, 5), ActorFacing.Right);
        session.Step(new SelectHotbarSlotIntent(new HotbarSlotId(4)), evaluateNpcDecisions: false);
        IntentResult attack = session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();
        PlacePlayer(session.State, TinyFarmSceneIds.DungeonEntrance, new GridPosition(2, 6), ActorFacing.Left);
        IntentResult leave = session.Step(
            new InteractIntent(new SceneObjectId("dungeon-exit")),
            evaluateNpcDecisions: false).Results.Single();

        IntentResult[] verbs = [chop, gather, cook, enter, attack, leave];
        if (verbs.Any(result => result.Status != IntentResultStatus.Accepted))
        {
            throw new InvalidOperationException("M21 composed life-sim and dungeon story failed.");
        }
        return Hash(new
        {
            results = verbs.Select(result => new { result.Status, result.Reason }),
            events = verbs.SelectMany(result => result.Events),
            session.State.InventoryStacks,
            session.State.Trees,
            session.State.ForageNodes,
            session.State.Enemies,
            session.State.CurrentScene
        });
    }

    private static void PlacePlayer(
        TinyFarmState state,
        SceneId scene,
        GridPosition position,
        ActorFacing facing)
    {
        int actorIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with
        {
            Location = TinyFarmScenes.LocationForScene(scene)
        };
        int placementIndex = state.MutableActorScenes.FindIndex(placement => placement.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placementIndex] = state.MutableActorScenes[placementIndex] with
        {
            Scene = scene,
            WorldPosition = ScenePosition.FromGrid(position),
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
        IntentResult WrongWeapon,
        IntentResult Attack,
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
