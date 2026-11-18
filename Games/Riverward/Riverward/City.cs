using Aurelian.Strategy;

namespace Riverward;

internal enum Block
{
    Homes,
    Workshop,
    Market,
    Park,
    Dock,
    FishMarket,
    Boardwalk,
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
    Dock,
    FishMarket,
    Boardwalk,
}

internal readonly record struct CityTarget(HexCell Cell, HexEdge Edge, bool Street = false);

internal sealed record Placement(
    string Message, bool Allowed = false, int Cost = 0, CityReport? Forecast = null,
    HexCell[]? Improved = null, HexCell[]? Worsened = null);

internal readonly record struct BlockReport(bool Access, int Appeal, int Capacity, int Customers, int Jobs, int Residents,
    int Parks, int Noise, int Happiness, int Boardwalks, int Shops, bool Ferry, int Trips, int Net);

internal sealed record CityReport(Dictionary<HexCell, BlockReport> Blocks, int Residents, int Housing, int Jobs, int Employed,
    int Income, int Upkeep, int Happiness, FerryStop[] FerryStops, int FerryTrips)
{
    internal int Balance => Income - Upkeep;
}

internal sealed class City
{
    internal const int Goal = 24;
    internal const int HappinessGoal = 72;
    internal const int SurplusGoal = 30;
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
        Tool.Dock => Block.Dock,
        Tool.FishMarket => Block.FishMarket,
        Tool.Boardwalk => Block.Boardwalk,
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
        Tool.Dock => 60,
        Tool.FishMarket => 95,
        Tool.Boardwalk => 90,
        _ => 0,
    };

    internal static string Label(Tool tool) => tool == Tool.FishMarket ? "Fish market" : tool.ToString();

    private static bool RequiresShore(Block kind) => kind is Block.Dock or Block.FishMarket or Block.Boardwalk;

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
            if (BlockFor(tool) is { } kind && RequiresShore(kind) && CityMap.WaterAtShore(cell) is null)
            {
                return new("Choose a block on the riverbank.");
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
                || after.Appeal < before.Appeal || after.Happiness < before.Happiness || after.Net < before.Net
                || (before.Access && !after.Access) || (before.Ferry && !after.Ferry))
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
            Tool.Dock when !forecast.Blocks[cell].Ferry => DockHint(cell, nextStreets, forecast.Blocks[cell].Access),
            _ when !forecast.Blocks[cell].Access && tool is not (Tool.Park or Tool.Boardwalk) => $"{Label(tool)} needs access.",
            _ => $"Build {Label(tool).ToLowerInvariant()}",
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

    private static string DockHint(HexCell cell, HashSet<HexEdge> streets, bool rooted)
    {
        bool paved = Enumerable.Range(0, HexCell.SideCount).Any(side => streets.Contains(cell.Edge(side)));
        if (!paved)
        {
            return "Lay a street along this dock.";
        }
        return rooted ? "Build a second dock with a street." : "Connect a dock to Town Hall.";
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
        if (Report.Employed >= Goal && Report.Happiness >= HappinessGoal && Report.Balance >= SurplusGoal)
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
        CityAccess access = CityAccess.Evaluate(blocks, streets);
        CityCommerce commerce = CityCommerce.Evaluate(access, blocks, households);
        var docks = access.Stops.Select(stop => stop.Land).ToHashSet();
        var statuses = new Dictionary<HexCell, BlockReport>();
        int employedCandidates = households.Where(pair => access.Connected.Contains(pair.Key)).Sum(pair => pair.Value);
        int upkeep = streets.Count;
        int sales = 0;
        foreach ((HexCell cell, Block block) in blocks.OrderBy(pair => pair.Key.R).ThenBy(pair => pair.Key.Q))
        {
            bool street = access.Connected.Contains(cell);
            int parks = 0;
            int boardwalks = 0;
            int noise = 0;
            int customers = 0;
            int shops = 0;
            bool nearbyFerry = false;
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
                    case Block.Boardwalk:
                        boardwalks++;
                        break;
                    case Block.Dock when docks.Contains(neighbor):
                        nearbyFerry = true;
                        break;
                    case Block.Workshop:
                        noise++;
                        break;
                    case Block.Homes when access.Connected.Contains(neighbor):
                        customers += households.GetValueOrDefault(neighbor);
                        break;
                    case Block.Market when access.Connected.Contains(neighbor):
                    case Block.FishMarket when access.Connected.Contains(neighbor) || AdjacentToDock(neighbor, docks):
                        shops++;
                        break;
                }
            }
            int residents = households.GetValueOrDefault(cell);
            int appeal = 2 * (parks + boardwalks - noise);
            bool servedByFerry = block == Block.Dock ? docks.Contains(cell) : nearbyFerry;
            bool connected = street || (block is Block.FishMarket or Block.Boardwalk && nearbyFerry);
            int capacity = block == Block.Homes && connected ? (appeal >= 2 ? 16 : 8) : 0;
            int trips = block == Block.Dock ? commerce.Departures.GetValueOrDefault(cell) : commerce.Visitors.GetValueOrDefault(cell);
            int served = 0;
            int revenue = 0;
            int expenses = 0;
            int jobs = block == Block.Workshop && connected ? 14 : 0;
            switch (block)
            {
                case Block.Workshop:
                    expenses = 13;
                    break;
                case Block.Market:
                    served = connected ? Math.Min(12, customers) : 0;
                    revenue = served * 5;
                    expenses = 17;
                    break;
                case Block.Park:
                    expenses = 8;
                    break;
                case Block.Dock:
                    revenue = trips;
                    expenses = servedByFerry ? 10 : 6;
                    break;
                case Block.FishMarket:
                    served = connected ? Math.Min(18, customers + trips) : 0;
                    revenue = served * (nearbyFerry ? 7 : 5);
                    expenses = 18;
                    break;
                case Block.Boardwalk:
                    revenue = 2 * trips;
                    expenses = 12;
                    break;
            }
            sales += revenue;
            upkeep += expenses;
            statuses.Add(cell, block == Block.Homes
                ? new(connected, appeal, capacity, served, jobs, residents, parks, noise, 0, boardwalks, shops, nearbyFerry, 0, 0)
                : new(connected, 0, 0, served, jobs, 0, 0, 0, 0, 0, 0, servedByFerry, trips, revenue - expenses));
        }
        int jobsTotal = statuses.Values.Sum(detail => detail.Jobs);
        int employed = Math.Min(employedCandidates, jobsTotal);
        int employment = employedCandidates > 0 ? employed * 16 / employedCandidates : jobsTotal > 0 ? 16 : 0;
        int weightedHappiness = 0;
        foreach (HexCell cell in blocks.Keys.Where(cell => blocks[cell] == Block.Homes))
        {
            BlockReport home = statuses[cell];
            int happiness = Math.Clamp((home.Access ? 52 + employment : 24) + 8 * Math.Min(home.Parks, 2)
                + 18 * Math.Min(home.Boardwalks, 1) + 6 * Math.Min(home.Shops, 1)
                + (home.Ferry ? 5 : 0) - 10 * home.Noise, 0, 100);
            statuses[cell] = home with { Happiness = happiness };
            weightedHappiness += happiness * home.Residents;
        }
        int population = households.Values.Sum();
        return new(statuses, population, statuses.Values.Sum(detail => detail.Capacity),
            jobsTotal, employed, employed * 4 + sales, upkeep, population > 0 ? weightedHappiness / population : 0,
            access.Stops, commerce.Departures.Values.Sum());
    }

    private static bool AdjacentToDock(HexCell cell, HashSet<HexCell> docks)
    {
        for (int side = 0; side < HexCell.SideCount; side++)
        {
            if (docks.Contains(cell.Neighbor(side)))
            {
                return true;
            }
        }
        return false;
    }

    internal CitySnapshot Capture() => new(Cash, Month, MonthProgress, Streak, Won,
        [.. Report.Blocks.Keys.Select(cell => new SavedBlock(cell, blocks[cell], households.GetValueOrDefault(cell)))],
        [.. streets.OrderBy(edge => edge.A.X).ThenBy(edge => edge.A.Y).ThenBy(edge => edge.B.X).ThenBy(edge => edge.B.Y)]);

    internal static void Validate(CitySnapshot snapshot)
    {
        if (snapshot.Blocks is null || snapshot.Streets is null || snapshot.Month < 1 || snapshot.MonthProgress is < 0 or >= MonthTicks
            || snapshot.Streak is < 0 or > RequiredMonths || (snapshot.Streak == RequiredMonths && !snapshot.Won)
            || snapshot.Blocks.Any(block => !CityMap.CanBuild(block.Cell) || !Enum.IsDefined(block.Kind)
                || (RequiresShore(block.Kind) && CityMap.WaterAtShore(block.Cell) is null)
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
