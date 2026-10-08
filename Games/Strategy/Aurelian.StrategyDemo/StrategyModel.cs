using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Simulation;
using Aurelian.Spatial2D;
using Aurelian.Strategy;

namespace Aurelian.StrategyDemo;

public enum UnitKind
{
    Worker,
    Soldier,
    Ranger
}
public enum ResourceKind
{
    Crystal,
    Wood
}
public abstract record UnitOrder;
public sealed record IdleOrder : UnitOrder;
public sealed record MoveOrder(SpatialPoint2D Target) : UnitOrder;
public sealed record GatherOrder(int ResourceId) : UnitOrder;
public sealed record ConstructOrder(int BuildingId) : UnitOrder;
public sealed record AttackOrder(int EnemyId) : UnitOrder;
public abstract record StrategyIntent;
public sealed record SelectIntent(int[] Ids, SelectionChange Change = SelectionChange.Replace) : StrategyIntent;
public sealed record ControlGroupIntent(int Group, bool Store) : StrategyIntent;
public sealed record OrderIntent(int[] Ids, UnitOrder Order) : StrategyIntent;
public sealed record BuildIntent(SpatialPoint2D Position) : StrategyIntent;
public sealed record ProduceIntent(UnitKind Kind) : StrategyIntent;
public sealed record AdvanceIntent : StrategyIntent;
public sealed record IntentResult(bool Accepted, string Message);

public sealed class StrategyUnit(int id, UnitKind kind, SpatialPoint2D position)
{
    public int Id { get; } = id;
    public UnitKind Kind { get; } = kind;
    public SpatialPoint2D Position { get; internal set; } = position;
    public UnitOrder Order { get; internal set; } = new IdleOrder();
    public int Carry { get; internal set; }
    public ResourceKind? CarryKind { get; internal set; }
    public int Work { get; internal set; }
    public string Phase { get; internal set; } = "idle";
}

public sealed class ResourceNode(int id, ResourceKind kind, SpatialPoint2D position, int amount)
{
    public int Id { get; } = id;
    public ResourceKind Kind { get; } = kind;
    public SpatialPoint2D Position { get; } = position;
    public int Amount { get; internal set; } = amount;
}

public sealed class StrategyBuilding(int id, SpatialPoint2D position, int progress)
{
    public int Id { get; } = id;
    public SpatialPoint2D Position { get; } = position;
    public int Progress { get; internal set; } = progress;
}

public sealed record ProductionItem(UnitKind Kind, int RemainingTicks);
public sealed record RecordedIntent(long Sequence, StrategyIntent Intent);

/// <summary>The only owner of positions, resources, orders, construction and production.</summary>
public sealed class StrategySession
{
    public const int MapSize = 24;
    public static readonly SpatialPoint2D Home = new(10, 11);
    public static readonly SpatialPoint2D EnemyPosition = new(18, 9);
    private readonly List<StrategyUnit> units = [];
    private readonly List<ResourceNode> resources = [];
    private readonly List<StrategyBuilding> buildings = [];
    private readonly List<ProductionItem> production = [];
    private readonly List<RecordedIntent> tape = [];
    private readonly WorkerBehaviors behaviors;
    private int nextId = 5;

    public StrategySession()
    {
        units.Add(new(1, UnitKind.Worker, new(8, 12)));
        units.Add(new(2, UnitKind.Worker, new(9, 13)));
        units.Add(new(3, UnitKind.Worker, new(12, 12)));
        units.Add(new(4, UnitKind.Soldier, new(11, 14)));
        resources.Add(new(100, ResourceKind.Crystal, new(14, 12), 300));
        resources.Add(new(101, ResourceKind.Crystal, new(6, 7), 300));
        resources.Add(new(102, ResourceKind.Wood, new(13, 15), 300));
        buildings.Add(new(200, Home, 100));
        behaviors = new WorkerBehaviors(this);
        foreach (StrategyUnit unit in units)
        {
            behaviors.Add(unit);
        }
        Reveal();
    }

    public IReadOnlyList<StrategyUnit> Units => units.AsReadOnly();
    public IReadOnlyList<ResourceNode> Resources => resources.AsReadOnly();
    public IReadOnlyList<StrategyBuilding> Buildings => buildings.AsReadOnly();
    public IReadOnlyList<ProductionItem> Production => production.AsReadOnly();
    public IReadOnlyList<RecordedIntent> Tape => tape.Select(entry => entry with { Intent = Detach(entry.Intent) }).ToArray();
    public EntitySelection<int> Selection { get; } = new();
    public VisibilityGrid Fog { get; } = new(MapSize, MapSize);
    public int Stock { get; private set; } = 100;
    public int Gathered { get; private set; }
    public int WoodStock { get; private set; }
    public int WoodGathered { get; private set; }
    public int EnemyHealth { get; private set; } = 40;
    public long Tick { get; private set; }
    public string Notice { get; private set; } = "Select a worker, then right-click a crystal grove.";
    public bool ObjectiveComplete => Gathered >= 20 && buildings.Any(b => b.Id != 200 && b.Progress == 100);

    public IntentResult Dispatch(StrategyIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        intent = Detach(intent);
        IntentResult result = Resolve(intent);
        tape.Add(new(tape.Count, intent));
        if (intent is not AdvanceIntent)
        {
            Notice = result.Message;
        }
        return result;
    }

    private static StrategyIntent Detach(StrategyIntent intent)
    {
        return intent switch
        {
            SelectIntent select => select with { Ids = (int[])select.Ids.Clone() },
            OrderIntent order => order with { Ids = (int[])order.Ids.Clone() },
            _ => intent
        };
    }

    public bool Eligible(int id) => units.Any(u => u.Id == id);

    public int[] Query(SpatialShape2D shape)
    {
        var world = new SpatialWorld2D(units.Select(u => Collider(u.Id, new Circle2(u.Position, .55))));
        return world.Overlap(shape).Select(hit => int.Parse(hit.ColliderId.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    public string? PlacementFailure(SpatialPoint2D point)
    {
        if (!InBounds(point, 2))
        {
            return "Choose ground inside the map.";
        }
        for (int y = (int)Math.Floor(point.Y - 1.1); y <= (int)Math.Floor(point.Y + 1.1); y++)
        {
            for (int x = (int)Math.Floor(point.X - 1.1); x <= (int)Math.Floor(point.X + 1.1); x++)
            {
                if (Fog[x, y] == CellVisibility.Unknown)
                {
                    return "Explore the entire footprint first.";
                }
            }
        }
        if (Stock < 40)
        {
            return "The lodge needs 40 crystal.";
        }
        var site = new Aabb2(point, new(1.1, 1.1));
        var occupied = new SpatialWorld2D(
            units.Select(u => Collider(u.Id, new Circle2(u.Position, .3)))
                .Concat(resources.Where(r => r.Amount > 0).Select(r => Collider(r.Id, new Circle2(r.Position, .7)))));
        if (CollisionWorld().Overlap(site).Count > 0 || occupied.Overlap(site).Count > 0)
        {
            return "The footprint must be clear.";
        }
        return null;
    }

    private IntentResult Resolve(StrategyIntent intent)
    {
        switch (intent)
        {
            case SelectIntent select:
                Selection.Apply(select.Ids, select.Change, Eligible);
                return new(true, $"{Selection.Snapshot().Count} selected");
            case ControlGroupIntent group when group.Group is >= 1 and <= 9:
                if (group.Store)
                {
                    Selection.StoreGroup(group.Group);
                }
                else
                {
                    Selection.RecallGroup(group.Group, SelectionChange.Replace, Eligible);
                }
                return new(true, $"Group {group.Group}");
            case OrderIntent order:
                return AcceptOrder(order);
            case BuildIntent build:
                return Build(build.Position);
            case ProduceIntent produce:
                if (!Enum.IsDefined(produce.Kind) || Stock < 25 || production.Count >= 4 || units.Count + production.Count >= 12)
                {
                    return new(false, "Production needs 25 crystal, free supply and queue space.");
                }
                Stock -= 25;
                production.Add(new(produce.Kind, 50));
                return new(true, "Recruitment queued; cost and supply reserved.");
            case AdvanceIntent:
                Advance();
                return new(true, "Tick resolved.");
            default:
                return new(false, "Unsupported intent.");
        }
    }

    private IntentResult AcceptOrder(OrderIntent intent)
    {
        StrategyUnit[] targets = intent.Ids.Distinct().Order().Select(id => units.Find(u => u.Id == id)).OfType<StrategyUnit>().ToArray();
        if (targets.Length == 0 || targets.Length != intent.Ids.Distinct().Count())
        {
            return new(false, "Order requires live owned units.");
        }
        bool valid = intent.Order switch
        {
            IdleOrder => true,
            MoveOrder move => InBounds(move.Target, .4),
            GatherOrder gather => targets.All(u => u.Kind == UnitKind.Worker)
                && resources.Any(r => r.Id == gather.ResourceId && r.Amount > 0 && IsVisible(r.Position)),
            AttackOrder attack => attack.EnemyId == 300 && EnemyHealth > 0 && IsVisible(EnemyPosition),
            _ => false,
        };
        if (!valid)
        {
            return new(false, "This order is unavailable for the selected units or target.");
        }
        foreach (StrategyUnit unit in targets)
        {
            unit.Order = intent.Order;
            unit.Work = 0;
        }
        return new(true, "Order accepted.");
    }

    private IntentResult Build(SpatialPoint2D point)
    {
        string? failure = PlacementFailure(point);
        StrategyUnit? worker = units.Where(u => u.Kind == UnitKind.Worker && u.Order is IdleOrder)
            .OrderBy(u => (u.Position - point).LengthSquared).ThenBy(u => u.Id).FirstOrDefault();
        if (failure is not null || worker is null)
        {
            return new(false, failure ?? "An idle worker is required.");
        }
        var building = new StrategyBuilding(200 + buildings.Count, point, 0);
        buildings.Add(building);
        Stock -= 40;
        worker.Order = new ConstructOrder(building.Id);
        return new(true, "Lodge site accepted; a worker is on the way.");
    }

    private void Advance()
    {
        Tick++;
        behaviors.Tick();
        if (production.Count > 0)
        {
            ProductionItem current = production[0];
            if (current.RemainingTicks > 1)
            {
                production[0] = current with { RemainingTicks = current.RemainingTicks - 1 };
            }
            else
            {
                SpatialPoint2D spawn = new(10, 13);
                if (CollisionWorld().Overlap(new Circle2(spawn, .3)).Count == 0)
                {
                    var unit = new StrategyUnit(nextId++, current.Kind, spawn);
                    units.Add(unit);
                    behaviors.Add(unit);
                    production.RemoveAt(0);
                }
            }
        }
        if (Tick % 5 == 0)
        {
            Reveal();
        }
    }

    internal void MoveToward(StrategyUnit unit, SpatialPoint2D target, double range)
    {
        SpatialVector2D delta = target - unit.Position;
        double distance = Math.Sqrt(delta.LengthSquared);
        if (distance <= range)
        {
            return;
        }
        SpatialVector2D step = delta * (Math.Min(.16, distance - range) / distance);
        SpatialHit2D? hit = CollisionWorld().Sweep(new Circle2(unit.Position, .25), step);
        if (hit is null)
        {
            unit.Position += step;
        }
        else
        {
            unit.Order = new IdleOrder();
            Notice = "Movement blocked. Choose a waypoint around the building.";
        }
    }

    internal void Harvest(StrategyUnit unit, ResourceNode node)
    {
        if (unit.Carry > 0 && unit.CarryKind != node.Kind)
        {
            return;
        }
        if (++unit.Work < 5)
        {
            return;
        }
        unit.Work = 0;
        int amount = Math.Min(2, Math.Min(node.Amount, 10 - unit.Carry));
        node.Amount -= amount;
        unit.Carry += amount;
        if (amount > 0)
        {
            unit.CarryKind = node.Kind;
        }
    }

    internal void Deposit(StrategyUnit unit)
    {
        switch (unit.CarryKind)
        {
            case ResourceKind.Crystal:
                Stock += unit.Carry;
                Gathered += unit.Carry;
                break;
            case ResourceKind.Wood:
                WoodStock += unit.Carry;
                WoodGathered += unit.Carry;
                break;
        }
        unit.Carry = 0;
        unit.CarryKind = null;
    }

    internal void Construct(StrategyUnit unit, StrategyBuilding building)
    {
        building.Progress = Math.Min(100, building.Progress + 2);
        if (building.Progress == 100)
        {
            unit.Order = new IdleOrder();
        }
    }

    internal void Attack(StrategyUnit unit)
    {
        if (EnemyHealth == 0 || !IsVisible(EnemyPosition))
        {
            unit.Order = new IdleOrder();
            return;
        }
        if (++unit.Work >= 10)
        {
            unit.Work = 0;
            EnemyHealth = Math.Max(0, EnemyHealth - 5);
        }
        if (EnemyHealth == 0)
        {
            unit.Order = new IdleOrder();
        }
    }

    public bool IsVisible(SpatialPoint2D point) => Fog[(int)point.X, (int)point.Y] == CellVisibility.Visible;

    public string Hash()
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Tick, Stock, Gathered, WoodStock, WoodGathered, EnemyHealth,
            Units = units.Select(u => new
            {
                u.Id, u.Kind, u.Position, u.Carry, u.CarryKind, u.Work, u.Phase,
                Order = new
                {
                    Kind = u.Order.GetType().Name,
                    Target = (u.Order as MoveOrder)?.Target,
                    Resource = (u.Order as GatherOrder)?.ResourceId,
                    Building = (u.Order as ConstructOrder)?.BuildingId,
                    Enemy = (u.Order as AttackOrder)?.EnemyId
                }
            }),
            Resources = resources.Select(r => new { r.Id, r.Kind, r.Amount }),
            Buildings = buildings.Select(b => new { b.Id, b.Position, b.Progress }),
            Production = production,
            Exploration = Fog.CaptureExploration(),
            Visibility = Enumerable.Range(0, MapSize * MapSize).Select(index => Fog[index % MapSize, index / MapSize]),
        });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private void Reveal()
    {
        Fog.Recompute(units.Select(u => new RevealSource((int)u.Position.X, (int)u.Position.Y, 5))
            .Concat(buildings.Where(b => b.Progress == 100).Select(b => new RevealSource((int)b.Position.X, (int)b.Position.Y, 5))).ToArray());
    }

    private SpatialWorld2D CollisionWorld() => new(buildings.Select(b => Collider(b.Id, new Aabb2(b.Position, new(.9, .9)))));

    private static SpatialCollider2D Collider(int id, SpatialShape2D shape) => new(new(id.ToString(System.Globalization.CultureInfo.InvariantCulture)), shape, SpatialLayerMask.All, SpatialLayerMask.All);

    private static bool InBounds(SpatialPoint2D point, double margin) => double.IsFinite(point.X) && double.IsFinite(point.Y)
        && point.X >= margin && point.Y >= margin && point.X < MapSize - margin && point.Y < MapSize - margin;
}

public sealed class StrategyHost(StrategySession session)
{
    private readonly CadenceScheduler scheduler = new([new(new("strategy"), RationalRate.PerSecond(10), 0)], TimeSpan.FromSeconds(1));

    public void Advance(TimeSpan delta, bool paused = false)
    {
        CadenceAdvanceResult result = scheduler.Advance(delta, paused ? SimulationExecutionRate.Paused : SimulationExecutionRate.Normal);
        foreach (DueWorkFact fact in result.DueWork)
        {
            session.Dispatch(new AdvanceIntent());
        }
    }
}
