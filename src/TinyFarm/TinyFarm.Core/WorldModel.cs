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

public sealed record LocationDefinition(
    LocationId Id,
    string Name,
    string Description,
    IReadOnlyList<LocationId> Exits);

public sealed record ActorState(
    ActorId Id,
    string Name,
    LocationId Location,
    int Money,
    List<ItemId> Inventory,
    bool IsPlayer);

public sealed record ItemState(
    ItemId Id,
    string Name,
    int Price,
    LocationId? GroundLocation,
    ActorId? Owner);

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
    MaraThankedPlayer
}

public sealed class TinyFarmState
{
    private readonly List<ActorState> actors;
    private readonly List<ItemState> items;
    private readonly List<WorldFact> facts;

    public const int SaveVersion = 1;

    [JsonConstructor]
    public TinyFarmState(
        int version,
        int minute,
        IReadOnlyList<ActorState> actors,
        IReadOnlyList<ItemState> items,
        IReadOnlyList<WorldFact> facts,
        FavorStage favor)
    {
        Version = version;
        Minute = minute;
        this.actors = actors.ToList();
        this.items = items.ToList();
        this.facts = facts.ToList();
        Favor = favor;
    }

    public int Version { get; }

    public int Minute { get; internal set; }

    public IReadOnlyList<ActorState> Actors => actors;

    public IReadOnlyList<ItemState> Items => items;

    public IReadOnlyList<WorldFact> Facts => facts;

    public FavorStage Favor { get; internal set; }

    internal List<ActorState> MutableActors => actors;

    internal List<ItemState> MutableItems => items;

    internal List<WorldFact> MutableFacts => facts;

    public ActorState Actor(ActorId id)
    {
        return Actors.Single(actor => actor.Id == id);
    }

    public ItemState Item(ItemId id)
    {
        return Items.Single(item => item.Id == id);
    }

    public TinyFarmState DeepCopy()
    {
        return new TinyFarmState(
            Version,
            Minute,
            Actors
                .Select(actor => actor with { Inventory = actor.Inventory.ToList() })
                .ToList(),
            Items.ToList(),
            Facts.ToList(),
            Favor);
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
}

public static class TinyFarmContent
{
    public static IReadOnlyList<LocationDefinition> Locations { get; } =
    [
        new(TinyFarmIds.Farmhouse, "Farmhouse", "A small house at the edge of town.", [TinyFarmIds.TownSquare]),
        new(TinyFarmIds.TownSquare, "Town Square", "Morning light falls across the old well.", [TinyFarmIds.Farmhouse, TinyFarmIds.GeneralStore, TinyFarmIds.Riverside]),
        new(TinyFarmIds.GeneralStore, "General Store", "Shelves of practical things surround Sela's counter.", [TinyFarmIds.TownSquare]),
        new(TinyFarmIds.Riverside, "Riverside", "The river folds silver around the reeds.", [TinyFarmIds.TownSquare])
    ];

    public static LocationDefinition Location(LocationId id)
    {
        return Locations.Single(location => location.Id == id);
    }

    public static TinyFarmState CreateInitialState()
    {
        return new TinyFarmState(
            TinyFarmState.SaveVersion,
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
}
