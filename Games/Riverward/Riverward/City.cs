using Aurelian.Strategy;

namespace Riverward;

internal enum Block
{
    Homes,
    Workshop,
    Market,
    Park,
}

internal enum Tool
{
    Inspect,
    Streets,
    Homes,
    Workshop,
    Market,
    Park,
    Clear,
}

internal readonly record struct CityTarget(HexCell Cell, HexEdge Edge, bool Street = false);

internal sealed record Placement(
    string Message, bool Allowed = false, int Cost = 0, CityReport? Forecast = null,
    HexCell[]? Improved = null, HexCell[]? Worsened = null);

internal readonly record struct BlockReport(bool Access, int Appeal, int Capacity, int Customers, int Jobs, int Residents, int Parks, int Noise);

internal sealed record CityReport(Dictionary<HexCell, BlockReport> Blocks, int Residents, int Housing, int Jobs, int Employed, int Income, int Upkeep)
{
    internal int Balance => Income - Upkeep;
}

internal sealed class City
{
    internal const int Goal = 24;
    internal const int RequiredMonths = 3;
    internal const int MonthTicks = 360;

    private Dictionary<HexCell, Block> blocks = new()
    {
        [CityMap.FirstHomes] = Block.Homes,
        [CityMap.SecondHomes] = Block.Homes,
        [CityMap.FirstWorkshop] = Block.Workshop,
    };

    private HashSet<HexEdge> streets = [.. CityMap.InitialStreets];
    private Dictionary<HexCell, int> households = new() { [CityMap.FirstHomes] = 5, [CityMap.SecondHomes] = 5 };

    internal City()
    {
        Report = Evaluate(blocks, streets, households);
    }

    internal IReadOnlyDictionary<HexCell, Block> Blocks => blocks;
    internal IReadOnlySet<HexEdge> Streets => streets;
    internal CityReport Report { get; private set; }
    internal long Cash { get; private set; } = 520;
    internal int Month { get; private set; } = 1;
    internal int MonthProgress { get; private set; }
    internal int Streak { get; private set; }
    internal bool Won { get; private set; }
    internal int Revision { get; private set; }

    internal static Block? BlockFor(Tool tool) => tool switch
    {
        Tool.Homes => Block.Homes,
        Tool.Workshop => Block.Workshop,
        Tool.Market => Block.Market,
        Tool.Park => Block.Park,
        _ => null,
    };

    internal static int Cost(Tool tool) => tool switch
    {
        Tool.Homes => 90,
        Tool.Workshop => 125,
        Tool.Market => 85,
        Tool.Park => 65,
        Tool.Streets => 12,
        Tool.Clear => 10,
        _ => 0,
    };

    internal Placement Preview(Tool tool, CityTarget target) => Change(tool, target, commit: false);
    internal Placement Build(Tool tool, CityTarget target) => Change(tool, target, commit: true);

    private Placement Change(Tool tool, CityTarget target, bool commit)
    {
        HexCell cell = target.Cell;
        HexEdge edge = target.Edge;
        bool changeStreet = tool == Tool.Streets || (tool == Tool.Clear && target.Street);
        if (tool == Tool.Inspect)
        {
            return new(CityMap.IsWater(cell) ? "River" : "Select a block or choose a building tool.");
        }
        if (changeStreet)
        {
            if (!CityMap.CanLayStreet(edge))
            {
                return new("Streets need a land edge.");
            }
            if (tool == Tool.Streets && streets.Contains(edge))
            {
                return new("This edge already has a street.");
            }
            if (tool == Tool.Clear && !streets.Contains(edge))
            {
                return new("This edge has no street.");
            }
        }
        else
        {
            if (cell == CityMap.TownHall)
            {
                return new("Town Hall anchors the street network.");
            }
            if (!CityMap.CanBuild(cell))
            {
                return new("Choose a land block.");
            }
            if (tool == Tool.Clear && !blocks.ContainsKey(cell))
            {
                return new("This block is empty.");
            }
            if (tool != Tool.Clear && blocks.ContainsKey(cell))
            {
                return new("This block is occupied. Clear it first.");
            }
        }

        int cost = Cost(tool);
        if (Cash < cost)
        {
            return new($"Needs ${cost}. Treasury has ${Cash}.");
        }

        var nextBlocks = new Dictionary<HexCell, Block>(blocks);
        var nextStreets = new HashSet<HexEdge>(streets);
        var nextHouseholds = new Dictionary<HexCell, int>(households);
        if (changeStreet && tool == Tool.Clear)
        {
            nextStreets.Remove(edge);
        }
        else if (changeStreet)
        {
            nextStreets.Add(edge);
        }
        else if (tool == Tool.Clear)
        {
            nextBlocks.Remove(cell);
            nextHouseholds.Remove(cell);
        }
        else if (BlockFor(tool) is { } block)
        {
            nextBlocks.Add(cell, block);
        }

        CityReport forecast = Evaluate(nextBlocks, nextStreets, nextHouseholds);
        var improved = new List<HexCell>();
        var worsened = new List<HexCell>();
        foreach ((HexCell key, BlockReport before) in Report.Blocks)
        {
            if (!forecast.Blocks.TryGetValue(key, out BlockReport after))
            {
                continue;
            }
            if (after.Capacity < before.Capacity || after.Customers < before.Customers || after.Jobs < before.Jobs
                || after.Appeal < before.Appeal || (before.Access && !after.Access))
            {
                worsened.Add(key);
            }
            else if (before != after)
            {
                improved.Add(key);
            }
        }

        string message = tool switch
        {
            Tool.Clear when !changeStreet && households.GetValueOrDefault(cell) > 0 =>
                $"Clearing displaces {households[cell]} households.",
            Tool.Clear => changeStreet ? "Remove street segment" : "Clear block",
            Tool.Streets => "Build street segment",
            _ when !forecast.Blocks[cell].Access && tool != Tool.Park => $"{tool} needs a street to Town Hall.",
            _ => $"Build {tool.ToString().ToLowerInvariant()}",
        };
        if (commit)
        {
            blocks = nextBlocks;
            streets = nextStreets;
            households = nextHouseholds;
            Cash -= cost;
            Report = forecast;
            Revision++;
        }
        return new(message, true, cost, forecast, [.. improved], [.. worsened]);
    }

    internal void Advance(int ticks)
    {
        MonthProgress += ticks;
        if (MonthProgress < MonthTicks)
        {
            return;
        }
        MonthProgress -= MonthTicks;
        Month++;
        foreach ((HexCell cell, BlockReport status) in Report.Blocks)
        {
            if (blocks[cell] == Block.Homes && status.Access)
            {
                households[cell] = Math.Min(status.Capacity, status.Residents);
            }
        }
        int vacancies = Report.Jobs - Report.Blocks.Where(pair => pair.Value.Access).Sum(pair => households.GetValueOrDefault(pair.Key));
        foreach ((HexCell cell, BlockReport status) in Report.Blocks)
        {
            if (blocks[cell] == Block.Homes && status.Access && vacancies > 0)
            {
                int arrivals = Math.Min(vacancies, Math.Min(3, status.Capacity - households[cell]));
                households[cell] += arrivals;
                vacancies -= arrivals;
            }
        }
        Report = Evaluate(blocks, streets, households);
        Cash += Report.Balance;
        if (Report.Employed >= Goal && Report.Balance > 0)
        {
            Streak = Math.Min(Streak + 1, RequiredMonths);
            Won |= Streak == RequiredMonths;
        }
        else
        {
            Streak = 0;
        }
        Revision++;
    }

    private static CityReport Evaluate(Dictionary<HexCell, Block> blocks, HashSet<HexEdge> streets, Dictionary<HexCell, int> households)
    {
        HashSet<HexCell> connected = ConnectedFrontage(streets);
        var statuses = new Dictionary<HexCell, BlockReport>();
        int employedCandidates = 0;
        int upkeep = streets.Count;
        int sales = 0;
        foreach ((HexCell cell, Block block) in blocks.OrderBy(pair => pair.Key.R).ThenBy(pair => pair.Key.Q))
        {
            bool access = connected.Contains(cell);
            int parks = 0;
            int noise = 0;
            int customers = 0;
            for (int side = 0; side < HexCell.SideCount; side++)
            {
                HexCell neighbor = cell.Neighbor(side);
                if (!blocks.TryGetValue(neighbor, out Block nearby))
                {
                    continue;
                }
                switch (nearby)
                {
                    case Block.Park:
                        parks++;
                        break;
                    case Block.Workshop:
                        noise++;
                        break;
                    case Block.Homes when access && connected.Contains(neighbor):
                        customers += households.GetValueOrDefault(neighbor);
                        break;
                }
            }
            int residents = households.GetValueOrDefault(cell);
            int appeal = 2 * (parks - noise);
            int capacity = 0;
            int jobs = 0;
            int served = 0;
            switch (block)
            {
                case Block.Homes when access:
                    capacity = appeal >= 2 ? 16 : 8;
                    employedCandidates += residents;
                    break;
                case Block.Workshop:
                    jobs = access ? 14 : 0;
                    upkeep += 13;
                    break;
                case Block.Market:
                    served = Math.Min(12, customers);
                    sales += served * 5;
                    upkeep += 17;
                    break;
                case Block.Park:
                    upkeep += 8;
                    break;
            }
            statuses.Add(cell, new(access, block == Block.Homes ? appeal : 0, capacity, served, jobs, residents,
                block == Block.Homes ? parks : 0, block == Block.Homes ? noise : 0));
        }
        int jobsTotal = statuses.Values.Sum(detail => detail.Jobs);
        int employed = Math.Min(employedCandidates, jobsTotal);
        return new(statuses, households.Values.Sum(), statuses.Values.Sum(detail => detail.Capacity),
            jobsTotal, employed, employed * 4 + sales, upkeep);
    }

    private static HashSet<HexCell> ConnectedFrontage(HashSet<HexEdge> streets)
    {
        var junctions = new Dictionary<HexCorner, List<HexEdge>>();
        foreach (HexEdge edge in streets.Append(CityMap.Bridge))
        {
            AddEdge(edge.A, edge);
            AddEdge(edge.B, edge);
        }
        var reachable = new HashSet<HexCell>();
        var visited = new HashSet<HexCorner> { CityMap.InitialStreets[2].A };
        var queue = new Queue<HexCorner>(visited);
        while (queue.TryDequeue(out HexCorner corner))
        {
            if (!junctions.TryGetValue(corner, out List<HexEdge>? branches))
            {
                continue;
            }
            foreach (HexEdge edge in branches)
            {
                if (edge != CityMap.Bridge)
                {
                    reachable.UnionWith(CityMap.Frontage(edge));
                }
                HexCorner other = edge.A == corner ? edge.B : edge.A;
                if (visited.Add(other))
                {
                    queue.Enqueue(other);
                }
            }
        }
        return reachable;

        void AddEdge(HexCorner corner, HexEdge edge)
        {
            if (!junctions.TryGetValue(corner, out List<HexEdge>? edges))
            {
                edges = [];
                junctions.Add(corner, edges);
            }
            edges.Add(edge);
        }
    }

    internal CitySnapshot Capture() => new(Cash, Month, MonthProgress, Streak, Won,
        [.. Report.Blocks.Keys.Select(cell => new SavedBlock(cell, blocks[cell], households.GetValueOrDefault(cell)))],
        [.. streets.OrderBy(edge => edge.A.X).ThenBy(edge => edge.A.Y).ThenBy(edge => edge.B.X).ThenBy(edge => edge.B.Y)]);

    internal static void Validate(CitySnapshot snapshot)
    {
        if (snapshot.Blocks is null || snapshot.Streets is null || snapshot.Month < 1 || snapshot.MonthProgress is < 0 or >= MonthTicks
            || snapshot.Streak is < 0 or > RequiredMonths || (snapshot.Streak == RequiredMonths && !snapshot.Won)
            || snapshot.Blocks.Any(block => !CityMap.CanBuild(block.Cell) || !Enum.IsDefined(block.Kind)
                || block.Residents is < 0 or > 16 || (block.Kind != Block.Homes && block.Residents != 0))
            || snapshot.Blocks.Select(block => block.Cell).Distinct().Count() != snapshot.Blocks.Length
            || snapshot.Streets.Any(edge => !CityMap.CanLayStreet(edge)) || snapshot.Streets.Distinct().Count() != snapshot.Streets.Length)
        {
            throw new InvalidDataException("Invalid Riverward town snapshot.");
        }
    }

    internal void Restore(CitySnapshot snapshot)
    {
        blocks = snapshot.Blocks.ToDictionary(block => block.Cell, block => block.Kind);
        households = snapshot.Blocks.Where(block => block.Kind == Block.Homes).ToDictionary(block => block.Cell, block => block.Residents);
        streets = [.. snapshot.Streets];
        Cash = snapshot.Cash;
        Month = snapshot.Month;
        MonthProgress = snapshot.MonthProgress;
        Streak = snapshot.Streak;
        Won = snapshot.Won;
        Report = Evaluate(blocks, streets, households);
        Revision++;
    }
}
