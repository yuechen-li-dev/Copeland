using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public readonly record struct ActorId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct LocationId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ItemId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ProductId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct CropId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct FarmPlotId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ForageNodeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct CookingRecipeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct TreeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct EnemyId(string Value)
{
    public override string ToString() => Value;
}

public enum EnemyKind
{
    Slime
}

public enum EnemyLifecycle
{
    Alive,
    Defeated
}

public sealed record EnemyDefinition(
    EnemyId Id,
    EnemyKind Kind,
    SceneId Scene,
    ScenePosition SpawnPosition,
    int MaxHealth);

public sealed record EnemyState(EnemyId Id, int CurrentHealth)
{
    [JsonIgnore]
    public EnemyLifecycle Lifecycle => CurrentHealth == 0
        ? EnemyLifecycle.Defeated
        : EnemyLifecycle.Alive;
}

public enum CookingStationKind
{
    Cooking
}

public sealed record CookingRecipeInput(ProductId Product, int Count);

public sealed record CookingRecipeDefinition(
    CookingRecipeId Id,
    CookingStationKind StationKind,
    IReadOnlyList<CookingRecipeInput> Inputs,
    ProductId OutputProduct,
    int OutputCount);

public sealed record LocationDefinition(LocationId Id, string Name, string Description, IReadOnlyList<LocationId> Exits);
public sealed record ActorState(ActorId Id, string Name, LocationId Location, int Money, List<ItemId> Inventory, bool IsPlayer);
public sealed record ItemState(
    ItemId Id,
    string Name,
    int Price,
    LocationId? GroundLocation,
    ActorId? Owner,
    SceneId? GroundScene = null,
    ScenePosition? GroundPosition = null);
public sealed record ItemDefinition(ProductId Id, string Name, int BuyPrice, int SellPrice);
public sealed record CropDefinition(CropId Id, ProductId SeedItemId, ProductId HarvestItemId, int GrowthDays, int WaterRequirement, int Yield);
public sealed record InventoryStack(ActorId Actor, ProductId Product, int Count);
public sealed record ShopStock(ProductId Product, int Count, int DailyRestockCount);
public sealed record FarmPlotState(FarmPlotId Id, LocationId Location, CropId? Crop, int? PlantedDay, int GrowthStage, bool WateredToday);
public sealed record ForageNodeDefinition(
    ForageNodeId Id,
    SceneId Scene,
    ScenePosition Position,
    ProductId Product,
    int YieldCount);
public enum ForageNodeAvailability
{
    Available,
    Depleted
}
public sealed record ForageNodeState(ForageNodeId Id, ForageNodeAvailability Availability);
public sealed record TreeDefinition(
    TreeId Id,
    SceneId Scene,
    ScenePosition Position,
    ProductId YieldProduct,
    int YieldCount);
public enum TreeAvailability
{
    Standing,
    Depleted
}
public sealed record TreeState(TreeId Id, TreeAvailability Availability);
public sealed record ActorEnergyState(ActorId Actor, int Energy, bool IsResting);
public sealed record SceneContentSource(string Path, string Sha256, long ByteLength);
public sealed record SceneContentProvenance(
    string Format,
    string AggregateSha256,
    IReadOnlyList<SceneContentSource> Sources,
    double ReadMilliseconds,
    double ParseMilliseconds,
    double MaterializeAndValidateMilliseconds);

public sealed class TinyFarmDefinitions
{
    public TinyFarmDefinitions(
        string identity,
        IEnumerable<ItemDefinition> items,
        IEnumerable<CropDefinition> crops,
        TinyFarmSceneCatalog scenes,
        SceneContentProvenance sceneContent,
        TinyFarmScheduleCatalog schedules,
        ScheduleContentProvenance scheduleContent,
        IEnumerable<ForageNodeDefinition>? forageNodes = null,
        IEnumerable<CookingRecipeDefinition>? cookingRecipes = null,
        IEnumerable<TreeDefinition>? trees = null,
        IEnumerable<EnemyDefinition>? enemies = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(sceneContent);
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(scheduleContent);
        Identity = identity;
        Items = items.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        Crops = crops.OrderBy(crop => crop.Id.Value, StringComparer.Ordinal).ToArray();
        Scenes = scenes;
        SceneContent = sceneContent;
        Schedules = schedules;
        ScheduleContent = scheduleContent;
        ForageNodes = (forageNodes ?? []).OrderBy(node => node.Id.Value, StringComparer.Ordinal).ToArray();
        CookingRecipes = (cookingRecipes ?? [])
            .Select(recipe => recipe with
            {
                Inputs = recipe.Inputs
                    .OrderBy(input => input.Product.Value, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(recipe => recipe.Id.Value, StringComparer.Ordinal)
            .ToArray();
        Trees = (trees ?? []).OrderBy(tree => tree.Id.Value, StringComparer.Ordinal).ToArray();
        Enemies = (enemies ?? []).OrderBy(enemy => enemy.Id.Value, StringComparer.Ordinal).ToArray();
        if (Items.Select(item => item.Id).Distinct().Count() != Items.Count || Crops.Select(crop => crop.Id).Distinct().Count() != Crops.Count)
        {
            throw new InvalidDataException("TinyFarm definition identities must be unique.");
        }
        foreach (CropDefinition crop in Crops)
        {
            if (!Items.Any(item => item.Id == crop.SeedItemId) || !Items.Any(item => item.Id == crop.HarvestItemId)
                || crop.GrowthDays <= 0 || crop.WaterRequirement != 1 || crop.Yield <= 0)
            {
                throw new InvalidDataException($"Crop definition '{crop.Id}' is invalid for TinyFarm M2 semantics.");
            }
        }
        if (ForageNodes.Select(node => node.Id).Distinct().Count() != ForageNodes.Count)
        {
            throw new InvalidDataException("TinyFarm forage node identities must be unique.");
        }
        foreach (ForageNodeDefinition node in ForageNodes)
        {
            if (!Items.Any(item => item.Id == node.Product)
                || node.YieldCount <= 0
                || !Scenes.All.Any(scene => scene.Id == node.Scene)
                || !TinyFarmScenes.IsInBounds(Scenes.Get(node.Scene), node.Position))
            {
                throw new InvalidDataException($"Forage node definition '{node.Id}' is invalid.");
            }
        }
        if (CookingRecipes.Select(recipe => recipe.Id).Distinct().Count() != CookingRecipes.Count)
        {
            throw new InvalidDataException("TinyFarm cooking recipe identities must be unique.");
        }
        foreach (CookingRecipeDefinition recipe in CookingRecipes)
        {
            if (recipe.Inputs.Count == 0
                || recipe.OutputCount <= 0
                || !Items.Any(item => item.Id == recipe.OutputProduct)
                || recipe.Inputs.Any(input => input.Count <= 0 || !Items.Any(item => item.Id == input.Product))
                || recipe.Inputs.Select(input => input.Product).Distinct().Count() != recipe.Inputs.Count)
            {
                throw new InvalidDataException($"Cooking recipe definition '{recipe.Id}' is invalid.");
            }
        }
        if (Trees.Select(tree => tree.Id).Distinct().Count() != Trees.Count)
        {
            throw new InvalidDataException("TinyFarm tree identities must be unique.");
        }
        foreach (TreeDefinition tree in Trees)
        {
            if (!Items.Any(item => item.Id == tree.YieldProduct)
                || tree.YieldCount <= 0
                || !Scenes.All.Any(scene => scene.Id == tree.Scene)
                || !TinyFarmScenes.IsInBounds(Scenes.Get(tree.Scene), tree.Position))
            {
                throw new InvalidDataException($"Tree definition '{tree.Id}' is invalid.");
            }
        }
        if (Enemies.Select(enemy => enemy.Id).Distinct().Count() != Enemies.Count)
        {
            throw new InvalidDataException("TinyFarm enemy identities must be unique.");
        }
        foreach (EnemyDefinition enemy in Enemies)
        {
            if (enemy.MaxHealth <= 0
                || !Scenes.All.Any(scene => scene.Id == enemy.Scene)
                || !TinyFarmScenes.IsInBounds(Scenes.Get(enemy.Scene), enemy.SpawnPosition)
                || TinyFarmScenes.IsBlocked(Scenes.Get(enemy.Scene), enemy.SpawnPosition))
            {
                throw new InvalidDataException($"Enemy definition '{enemy.Id}' is invalid.");
            }
        }
        if (Scenes.All.Any(scene => scene.Id == TinyFarmSceneIds.Residence))
        {
            ValidatePersonalHomes();
        }
    }

    public string Identity { get; }
    public IReadOnlyList<ItemDefinition> Items { get; }
    public IReadOnlyList<CropDefinition> Crops { get; }
    public TinyFarmSceneCatalog Scenes { get; }
    public SceneContentProvenance SceneContent { get; }
    public TinyFarmScheduleCatalog Schedules { get; }
    public ScheduleContentProvenance ScheduleContent { get; }
    public IReadOnlyList<ForageNodeDefinition> ForageNodes { get; }
    public IReadOnlyList<CookingRecipeDefinition> CookingRecipes { get; }
    public IReadOnlyList<TreeDefinition> Trees { get; }
    public IReadOnlyList<EnemyDefinition> Enemies { get; }
    public ItemDefinition Item(ProductId id) => Items.Single(item => item.Id == id);
    public CropDefinition Crop(CropId id) => Crops.Single(crop => crop.Id == id);
    public ForageNodeDefinition ForageNode(ForageNodeId id) => ForageNodes.Single(node => node.Id == id);
    public CookingRecipeDefinition CookingRecipe(CookingRecipeId id) => CookingRecipes.Single(recipe => recipe.Id == id);
    public TreeDefinition Tree(TreeId id) => Trees.Single(tree => tree.Id == id);
    public EnemyDefinition Enemy(EnemyId id) => Enemies.Single(enemy => enemy.Id == id);

    private void ValidatePersonalHomes()
    {
        ActorId[] npcs = [TinyFarmIds.Elias, TinyFarmIds.Mara, TinyFarmIds.Sela];
        var ownedObjects = new HashSet<SceneObjectId>();
        foreach (ActorId actor in npcs)
        {
            SceneAnchorId bedId = TinyFarmAnchorIds.HomeBedFor(actor);
            SceneAnchorDefinition bed = Scenes.GetAnchor(bedId);
            if (bed.Scene != TinyFarmSceneIds.Residence
                || bed.Kind != SceneAnchorKind.Rest
                || bed.SemanticObject is not SceneObjectId objectId)
            {
                throw new InvalidDataException($"NPC '{actor}' requires one reachable personal Rest anchor in the residence scene.");
            }

            SceneObjectDefinition bedObject = Scenes.Get(bed.Scene).Object(objectId);
            if (bedObject.Kind != SceneObjectKind.Bed
                || bedObject.SemanticReference != actor.Value
                || !ownedObjects.Add(objectId))
            {
                throw new InvalidDataException($"NPC '{actor}' requires one uniquely owned Bed object.");
            }

            if (!Schedules.Windows.Any(window => window.Actor == actor
                && window.Regime == TinyFarmScheduleRegime.Required
                && window.RequiredAnchor == bedId))
            {
                throw new InvalidDataException($"NPC '{actor}' requires a structural bedtime window targeting their own bed.");
            }

            foreach (TinyFarmUtilityCandidate rest in Schedules.Candidates.Where(candidate =>
                         candidate.ConsiderationKind == "energy-rest"
                         && Schedules.Windows.Single(window => window.Id == candidate.WindowId).Actor == actor))
            {
                if (rest.Anchor != bedId)
                {
                    throw new InvalidDataException($"NPC '{actor}' has an Open Rest candidate targeting another NPC's bed.");
                }
            }
        }
    }
}

public enum FavorStage
{
    NotStarted,
    LetterReceived,
    LetterDelivered,
    Complete
}

public enum WorldFact
{
    MaraNeedsDelivery,
    EliasHasLetter,
    MaraThankedPlayer,
    FirstCropHarvested,
    FirstCropSold,
    SupperRequested,
    SupperCompleted,
    SupperSeedPlanted
}

public sealed class TinyFarmState
{
    private readonly List<ActorState> actors;
    private readonly List<ItemState> items;
    private readonly List<WorldFact> facts;
    private readonly List<InventoryStack> inventoryStacks;
    private readonly List<ShopStock> shopStock;
    private readonly List<FarmPlotState> farmPlots;
    private readonly List<ActorSceneState> actorScenes;
    private readonly List<ActorEnergyState> actorEnergy;
    private readonly List<ForageNodeState> forageNodes;
    private readonly List<TreeState> trees;
    private readonly List<EnemyState> enemies;
    private readonly Dictionary<ActorId, int> actorIndex;
    private readonly Dictionary<ActorId, int> actorSceneIndex;
    private readonly Dictionary<ActorId, int> actorEnergyIndex;

    public const int M1SaveVersion = 1;
    public const int SaveVersion = 2;
    public const int SceneSaveVersion = 3;
    public const int ContinuousSceneSaveVersion = 4;
    public const int EnergySaveVersion = 5;
    public const int PlayerUiSaveVersion = 6;
    public const int ItemActionSaveVersion = 7;
    public const int ForageSaveVersion = 8;
    public const int WoodcuttingSaveVersion = 9;
    public const int DungeonCombatSaveVersion = 10;

    [JsonConstructor]
    public TinyFarmState(
        int version,
        int minute,
        IReadOnlyList<ActorState> actors,
        IReadOnlyList<ItemState> items,
        IReadOnlyList<WorldFact> facts,
        FavorStage favor,
        string? definitionSetId = null,
        IReadOnlyList<InventoryStack>? inventoryStacks = null,
        IReadOnlyList<ShopStock>? shopStock = null,
        IReadOnlyList<FarmPlotState>? farmPlots = null,
        IReadOnlyList<ActorSceneState>? actorScenes = null,
        IReadOnlyList<ActorEnergyState>? actorEnergy = null,
        int selectedHotbarSlot = 0,
        IReadOnlyList<ForageNodeState>? forageNodes = null,
        IReadOnlyList<TreeState>? trees = null,
        IReadOnlyList<EnemyState>? enemies = null)
    {
        Version = version;
        Minute = minute;
        this.actors = actors.ToList();
        this.items = items.ToList();
        this.facts = facts.ToList();
        Favor = favor;
        DefinitionSetId = definitionSetId;
        this.inventoryStacks = inventoryStacks?.ToList() ?? [];
        this.shopStock = shopStock?.ToList() ?? [];
        this.farmPlots = farmPlots?.ToList() ?? [];
        this.actorScenes = actorScenes?.ToList() ?? [];
        this.actorEnergy = actorEnergy?.ToList() ?? [];
        SelectedHotbarSlot = selectedHotbarSlot;
        this.forageNodes = forageNodes?.ToList() ?? [];
        this.trees = trees?.ToList() ?? [];
        this.enemies = enemies?.ToList() ?? [];
        actorIndex = BuildActorIndex(this.actors);
        actorSceneIndex = BuildActorSceneIndex(this.actorScenes);
        actorEnergyIndex = BuildActorEnergyIndex(this.actorEnergy);
    }

    public int Version { get; }
    public int Minute { get; internal set; }
    public int Day => Minute / 1440 + 1;
    public IReadOnlyList<ActorState> Actors => actors;
    public IReadOnlyList<ItemState> Items => items;
    public IReadOnlyList<WorldFact> Facts => facts;
    public FavorStage Favor { get; internal set; }
    public string? DefinitionSetId { get; }
    public IReadOnlyList<InventoryStack> InventoryStacks => inventoryStacks;
    public IReadOnlyList<ShopStock> ShopStock => shopStock;
    public IReadOnlyList<FarmPlotState> FarmPlots => farmPlots;
    public IReadOnlyList<ActorSceneState> ActorScenes => actorScenes;
    public IReadOnlyList<ActorEnergyState> ActorEnergy => actorEnergy;
    public int SelectedHotbarSlot { get; internal set; }
    public IReadOnlyList<ForageNodeState> ForageNodes => forageNodes;
    public IReadOnlyList<TreeState> Trees => trees;
    public IReadOnlyList<EnemyState> Enemies => enemies;
    public SceneId? CurrentScene => actorScenes.SingleOrDefault(item => item.Actor == TinyFarmIds.Player)?.Scene;
    internal List<ActorState> MutableActors => actors;
    internal List<ItemState> MutableItems => items;
    internal List<WorldFact> MutableFacts => facts;
    internal List<InventoryStack> MutableInventoryStacks => inventoryStacks;
    internal List<ShopStock> MutableShopStock => shopStock;
    internal List<FarmPlotState> MutableFarmPlots => farmPlots;
    internal List<ActorSceneState> MutableActorScenes => actorScenes;
    internal List<ActorEnergyState> MutableActorEnergy => actorEnergy;
    internal List<ForageNodeState> MutableForageNodes => forageNodes;
    internal List<TreeState> MutableTrees => trees;
    internal List<EnemyState> MutableEnemies => enemies;
    public ActorState Actor(ActorId id) => actors[RequiredIndex(actorIndex, id, "actor")];
    public ItemState Item(ItemId id) => Items.Single(item => item.Id == id);
    public ActorSceneState ActorScene(ActorId id) => actorScenes[RequiredIndex(actorSceneIndex, id, "actor scene")];
    public ActorEnergyState EnergyFor(ActorId id) => actorEnergy[RequiredIndex(actorEnergyIndex, id, "actor energy")];
    public ForageNodeState ForageNode(ForageNodeId id) => forageNodes.Single(node => node.Id == id);
    public TreeState Tree(TreeId id) => trees.Single(tree => tree.Id == id);
    public EnemyState Enemy(EnemyId id) => enemies.Single(enemy => enemy.Id == id);

    internal bool TryGetActorIndex(ActorId id, out int index)
    {
        return actorIndex.TryGetValue(id, out index);
    }

    internal int ActorSceneIndex(ActorId id)
    {
        return RequiredIndex(actorSceneIndex, id, "actor scene");
    }

    internal bool TryGetActorEnergyIndex(ActorId id, out int index)
    {
        return actorEnergyIndex.TryGetValue(id, out index);
    }
    public int ProductCount(ActorId actor, ProductId product)
    {
        return InventoryStacks
            .SingleOrDefault(stack => stack.Actor == actor && stack.Product == product)
            ?.Count ?? 0;
    }

    public TinyFarmState DeepCopy()
    {
        return new TinyFarmState(
            Version,
            Minute,
            Actors.Select(actor => actor with { Inventory = actor.Inventory.ToList() }).ToList(),
            Items.ToList(),
            Facts.ToList(),
            Favor,
            DefinitionSetId,
            InventoryStacks.ToList(),
            ShopStock.ToList(),
            FarmPlots.ToList(),
            ActorScenes.ToList(),
            ActorEnergy.ToList(),
            SelectedHotbarSlot,
            ForageNodes.ToList(),
            Trees.ToList(),
            Enemies.ToList());
    }

    private static Dictionary<ActorId, int> BuildActorIndex(IReadOnlyList<ActorState> values)
    {
        var index = new Dictionary<ActorId, int>(values.Count);
        for (int position = 0; position < values.Count; position++)
        {
            index.Add(values[position].Id, position);
        }
        return index;
    }

    private static Dictionary<ActorId, int> BuildActorSceneIndex(IReadOnlyList<ActorSceneState> values)
    {
        var index = new Dictionary<ActorId, int>(values.Count);
        for (int position = 0; position < values.Count; position++)
        {
            index.Add(values[position].Actor, position);
        }
        return index;
    }

    private static Dictionary<ActorId, int> BuildActorEnergyIndex(IReadOnlyList<ActorEnergyState> values)
    {
        var index = new Dictionary<ActorId, int>(values.Count);
        for (int position = 0; position < values.Count; position++)
        {
            index.Add(values[position].Actor, position);
        }
        return index;
    }

    private static int RequiredIndex(
        IReadOnlyDictionary<ActorId, int> index,
        ActorId id,
        string kind)
    {
        return index.TryGetValue(id, out int position)
            ? position
            : throw new InvalidOperationException($"Unknown {kind} '{id}'.");
    }
}

public static class TinyFarmIds
{
    public static readonly ActorId Player = new("player");
    public static readonly ActorId Mara = new("mara");
    public static readonly ActorId Elias = new("elias");
    public static readonly ActorId Sela = new("sela");
    public static readonly LocationId Farmhouse = new("farmhouse");
    public static readonly LocationId TownSquare = new("town-square");
    public static readonly LocationId GeneralStore = new("general-store");
    public static readonly LocationId Riverside = new("riverside");
    public static readonly ItemId Letter = new("mara-letter");
    public static readonly ItemId Apple = new("store-apple");
    public static readonly ItemId FishingRod = new("fishing-rod");
    public static readonly ItemId WildMint = new("wild-mint");
    public static readonly ItemId Axe = new("axe");
    public static readonly ItemId Sword = new("sword");
    public static readonly ProductId TurnipSeed = new("turnip-seed");
    public static readonly ProductId Turnip = new("turnip");
    public static readonly ProductId HenOfTheWoods = new("hen-of-the-woods");
    public static readonly ProductId SauteedHenOfTheWoods = new("sauteed-hen-of-the-woods");
    public static readonly ProductId Wood = new("wood");
    public static readonly CookingRecipeId SauteedHenOfTheWoodsRecipe = new("sauteed-hen-of-the-woods");
    public static readonly SceneObjectId HearthHouseKitchen = new("hearth-house-kitchen");
    public static readonly CropId TurnipCrop = new("turnip");
    public static readonly FarmPlotId PlotOne = new("plot-1");
    public static readonly FarmPlotId PlotTwo = new("plot-2");
    public static readonly ForageNodeId RiversideHenOfTheWoods = new("riverside-hen-of-the-woods");
    public static readonly TreeId FarmTree = new("farm-tree");
    public static readonly EnemyId DungeonSlime = new("dungeon.slime-1");
}

public static class TinyFarmContent
{
    public static IReadOnlyList<LocationDefinition> Locations { get; } =
    [
        new(TinyFarmIds.Farmhouse, "Farmhouse", "A small house at the edge of town.", [TinyFarmIds.TownSquare]),
        new(
            TinyFarmIds.TownSquare,
            "Town Square",
            "Morning light falls across the old well.",
            [TinyFarmIds.Farmhouse, TinyFarmIds.GeneralStore, TinyFarmIds.Riverside]),
        new(TinyFarmIds.GeneralStore, "General Store", "Shelves of practical things surround Sela's counter.", [TinyFarmIds.TownSquare]),
        new(TinyFarmIds.Riverside, "Riverside", "The river folds silver around the reeds.", [TinyFarmIds.TownSquare])
    ];

    public static LocationDefinition Location(LocationId id) => Locations.Single(location => location.Id == id);

    public static TinyFarmState CreateInitialState()
    {
        return new TinyFarmState(
            TinyFarmState.M1SaveVersion,
            8 * 60,
            [
                new(TinyFarmIds.Player, "You", TinyFarmIds.TownSquare, 12, [], true),
                new(TinyFarmIds.Mara, "Mara", TinyFarmIds.TownSquare, 4, [TinyFarmIds.Letter], false),
                new(TinyFarmIds.Elias, "Elias", TinyFarmIds.Farmhouse, 7, [], false),
                new(TinyFarmIds.Sela, "Sela", TinyFarmIds.GeneralStore, 30, [TinyFarmIds.Apple, TinyFarmIds.FishingRod], false)
            ],
            [
                new(TinyFarmIds.Letter, "sealed letter", 0, null, TinyFarmIds.Mara),
                new(TinyFarmIds.Apple, "red apple", 3, null, TinyFarmIds.Sela),
                new(TinyFarmIds.FishingRod, "fishing rod", 8, null, TinyFarmIds.Sela),
                new(TinyFarmIds.WildMint, "wild mint", 2, TinyFarmIds.Riverside, null)
            ],
            [],
            FavorStage.NotStarted);
    }

    public static TinyFarmState CreateWeekState(TinyFarmDefinitions definitions)
    {
        return new TinyFarmState(
            TinyFarmState.SaveVersion,
            8 * 60,
            [
                new(TinyFarmIds.Player, "You", TinyFarmIds.TownSquare, 12, [], true),
                new(TinyFarmIds.Mara, "Mara", TinyFarmIds.TownSquare, 8, [TinyFarmIds.Letter], false),
                new(TinyFarmIds.Elias, "Elias", TinyFarmIds.Farmhouse, 7, [], false),
                new(TinyFarmIds.Sela, "Sela", TinyFarmIds.GeneralStore, 30, [TinyFarmIds.Apple, TinyFarmIds.FishingRod], false)
            ],
            [
                new(TinyFarmIds.Letter, "sealed letter", 0, null, TinyFarmIds.Mara),
                new(TinyFarmIds.Apple, "red apple", 3, null, TinyFarmIds.Sela),
                new(TinyFarmIds.FishingRod, "fishing rod", 8, null, TinyFarmIds.Sela),
                new(TinyFarmIds.WildMint, "wild mint", 2, TinyFarmIds.Riverside, null)
            ],
            [],
            FavorStage.NotStarted,
            definitions.Identity,
            [],
            [new(TinyFarmIds.TurnipSeed, 3, 3)],
            [
                new(TinyFarmIds.PlotOne, TinyFarmIds.Farmhouse, null, null, 0, false),
                new(TinyFarmIds.PlotTwo, TinyFarmIds.Farmhouse, null, null, 0, false)
            ]);
    }

    public static TinyFarmState CreateSceneState(TinyFarmDefinitions definitions)
    {
        TinyFarmState week = CreateWeekState(definitions);
        return new TinyFarmState(
            TinyFarmState.SceneSaveVersion,
            week.Minute,
            week.Actors.Select(actor => actor with { Inventory = actor.Inventory.ToList() }).ToList(),
            week.Items.ToList(),
            week.Facts.ToList(),
            week.Favor,
            week.DefinitionSetId,
            week.InventoryStacks.ToList(),
            week.ShopStock.ToList(),
            week.FarmPlots.ToList(),
            [
                new(TinyFarmIds.Player, TinyFarmSceneIds.Farm, new GridPosition(6, 6)),
                new(
                    TinyFarmIds.Mara,
                    TinyFarmSceneIds.Town,
                    definitions.Scenes.GetAnchor(TinyFarmAnchorIds.TownSquare).Position),
                new(
                    TinyFarmIds.Elias,
                    TinyFarmSceneIds.Farm,
                    definitions.Scenes.GetAnchor(TinyFarmAnchorIds.FarmHome).Position),
                new(
                    TinyFarmIds.Sela,
                    TinyFarmSceneIds.GeneralStore,
                    definitions.Scenes.GetAnchor(TinyFarmAnchorIds.StoreCounter).Position)
            ]);
    }

    public static TinyFarmState CreateContinuousSceneState(TinyFarmDefinitions definitions)
    {
        TinyFarmState scene = CreateSceneState(definitions);
        Dictionary<ActorId, LocationId> locations = scene.ActorScenes.ToDictionary(
            placement => placement.Actor,
            placement => TinyFarmScenes.LocationForScene(placement.Scene));
        return new TinyFarmState(
            TinyFarmState.ContinuousSceneSaveVersion,
            scene.Minute,
            scene.Actors.Select(actor => actor with
            {
                Location = locations[actor.Id],
                Inventory = actor.Inventory.ToList()
            }).ToList(),
            scene.Items.ToList(),
            scene.Facts.ToList(),
            scene.Favor,
            scene.DefinitionSetId,
            scene.InventoryStacks.ToList(),
            scene.ShopStock.ToList(),
            scene.FarmPlots.ToList(),
            scene.ActorScenes.ToList());
    }

    public static TinyFarmState CreateEnergySceneState(TinyFarmDefinitions definitions)
    {
        TinyFarmState scene = CreateContinuousSceneState(definitions);
        var energyState = new TinyFarmState(
            TinyFarmState.EnergySaveVersion,
            scene.Minute,
            scene.Actors.Select(actor => actor with { Inventory = actor.Inventory.ToList() }).ToList(),
            scene.Items.ToList(),
            scene.Facts.ToList(),
            scene.Favor,
            scene.DefinitionSetId,
            scene.InventoryStacks.ToList(),
            scene.ShopStock.ToList(),
            scene.FarmPlots.ToList(),
            scene.ActorScenes.ToList(),
            [
                new(TinyFarmIds.Elias, TinyFarmEnergy.InitialUnits, false),
                new(TinyFarmIds.Mara, TinyFarmEnergy.InitialUnits, false),
                new(TinyFarmIds.Sela, TinyFarmEnergy.InitialUnits, false)
            ]);
        SceneAnchorDefinition residenceEntry = definitions.Scenes.GetAnchor(new SceneAnchorId("residence.from-farm"));
        int playerPlacement = energyState.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        energyState.MutableActorScenes[playerPlacement] = new ActorSceneState(
            TinyFarmIds.Player,
            residenceEntry.Scene,
            residenceEntry.Position);
        return energyState;
    }
}

public static class TinyFarmEnergy
{
    public const int MinimumUnits = 0;
    public const int MaximumUnits = 10_000;
    public const int InitialUnits = 9_000;
    public const int ActiveDecayUnitsPerMinute = 8;
    public const int RestRecoveryUnitsPerMinute = 40;

    public static int Advance(int energy, bool isResting, int minutes)
    {
        if (energy is < MinimumUnits or > MaximumUnits || minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(energy), "Energy and elapsed minutes must be within their finite bounds.");
        }

        int rate = isResting ? RestRecoveryUnitsPerMinute : -ActiveDecayUnitsPerMinute;
        return Math.Clamp(checked(energy + (rate * minutes)), MinimumUnits, MaximumUnits);
    }

    public static double RestContribution(int energy)
    {
        if (energy is < MinimumUnits or > MaximumUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(energy));
        }

        return (MaximumUnits - energy) / (double)MaximumUnits * 0.8d;
    }
}
