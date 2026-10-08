using TinyFarm.Core;

namespace TinyFarm.InputMan;

public enum InventorySort
{
    Name,
    Category,
    Quantity,
    Value,
    Equipped
}

/// <summary>Transient CRUD view state. Never serialized, hashed or replayed.</summary>
public sealed class TinyFarmMenus
{
    public const int PageSize = 10;
    public InventoryCategory Category { get; private set; }
    public InventorySort Sort { get; private set; }
    public bool Descending { get; private set; }
    public bool EquipmentOnly { get; private set; }
    public string Search { get; private set; } = "";
    public bool SearchFocused { get; set; }
    public string? SelectedKey { get; private set; }
    public int Offset { get; private set; }
    public int PauseSelection { get; private set; }
    public bool InventoryFromPause { get; set; }
    public bool StatsFromPause { get; set; }
    public ActorId StatsAgent { get; private set; } = TinyFarmIds.Player;
    public string StatsGroup { get; private set; } = "Stats";
    public int StatsOffset { get; private set; }
    public string? Confirmation { get; set; }

    public string CacheKey => $"{Category}|{Sort}|{Descending}|{EquipmentOnly}|{Search}|{SearchFocused}|{SelectedKey}|{Offset}|{PauseSelection}|{Confirmation}|{StatsAgent}|{StatsGroup}|{StatsOffset}";

    public IReadOnlyList<TinyFarmAgentProperty> PropertyRows(TinyFarmState state, TinyFarmDefinitions definitions)
    {
        if (!state.Actors.Any(actor => actor.Id == StatsAgent))
        {
            StatsAgent = TinyFarmIds.Player;
        }
        TinyFarmAgentProperty[] rows = TinyFarmAgentProperties.Project(state, definitions, StatsAgent)
            .Where(row => StatsGroup == "All" || row.Group == StatsGroup)
            .Where(row => row.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || row.Group.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || row.Value.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || row.Source.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        StatsOffset = Math.Clamp(StatsOffset, 0, Math.Max(0, rows.Length - PageSize));
        return rows;
    }

    public void SetStatsGroup(string group)
    {
        StatsGroup = group;
        StatsOffset = 0;
    }

    public void ChangeStatsAgent(int delta, TinyFarmState state)
    {
        ActorState[] agents = state.Actors.OrderByDescending(actor => actor.IsPlayer)
            .ThenBy(actor => actor.Id.Value, StringComparer.Ordinal).ToArray();
        int index = Array.FindIndex(agents, actor => actor.Id == StatsAgent);
        index = (index + delta + agents.Length) % agents.Length;
        StatsAgent = agents[index].Id;
        StatsOffset = 0;
    }

    public void ScrollStats(int delta, int count)
    {
        StatsOffset = Math.Clamp(StatsOffset + delta, 0, Math.Max(0, count - PageSize));
    }

    public IReadOnlyList<TinyFarmInventoryRow> Rows(TinyFarmState state, TinyFarmDefinitions definitions)
    {
        IEnumerable<TinyFarmInventoryRow> rows = TinyFarmInventory.Project(state, definitions)
            .Where(row => Category == InventoryCategory.All || row.Category == Category)
            .Where(row => !EquipmentOnly || row.Slot is not null)
            .Where(row => row.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || row.Category.ToString().Contains(Search, StringComparison.OrdinalIgnoreCase));
        IOrderedEnumerable<TinyFarmInventoryRow> ordered = Sort switch
        {
            InventorySort.Quantity => Order(rows, row => row.Quantity),
            InventorySort.Value => Order(rows, row => row.Value),
            InventorySort.Equipped => Order(rows, row => row.Equipped),
            InventorySort.Category => Order(rows, row => row.Category),
            _ => Descending
                ? rows.OrderByDescending(row => row.Name, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        };
        TinyFarmInventoryRow[] result = ordered.ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
        if (!result.Any(row => row.Key == SelectedKey))
        {
            SelectedKey = result.FirstOrDefault()?.Key;
            Offset = 0;
        }
        Offset = Math.Clamp(Offset, 0, Math.Max(0, result.Length - PageSize));
        return result;
    }

    public void SetCategory(InventoryCategory category)
    {
        Category = category;
        Offset = 0;
    }

    public void SetSearch(string text)
    {
        Search = text.Length > 48 ? text[..48] : text;
        Offset = 0;
        StatsOffset = 0;
    }

    public void SetEquipmentOnly(bool value)
    {
        EquipmentOnly = value;
        Category = InventoryCategory.All;
        Offset = 0;
    }

    public void SetSort(InventorySort sort)
    {
        Descending = Sort == sort && !Descending;
        Sort = sort;
        Offset = 0;
    }

    public void Select(string key, IReadOnlyList<TinyFarmInventoryRow> rows)
    {
        if (rows.Any(row => row.Key == key))
        {
            SelectedKey = key;
        }
    }

    public void MoveSelection(int delta, IReadOnlyList<TinyFarmInventoryRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }
        int index = rows.ToList().FindIndex(row => row.Key == SelectedKey);
        index = Math.Clamp(index + delta, 0, rows.Count - 1);
        SelectedKey = rows[index].Key;
        if (index < Offset)
        {
            Offset = index;
        }
        else if (index >= Offset + PageSize)
        {
            Offset = index - PageSize + 1;
        }
    }

    public void Scroll(int delta, int count)
    {
        Offset = Math.Clamp(Offset + delta, 0, Math.Max(0, count - PageSize));
    }

    public void MovePause(int delta)
    {
        PauseSelection = Math.Clamp(PauseSelection + delta, 0, TinyFarmGame.PauseActions.Length - 1);
    }

    private IOrderedEnumerable<TinyFarmInventoryRow> Order<T>(IEnumerable<TinyFarmInventoryRow> rows,
        Func<TinyFarmInventoryRow, T> key)
    {
        return Descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
    }
}
