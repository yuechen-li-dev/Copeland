using TinyFarm.Core;
using Machina.Runtime.State;

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
public sealed class TinyFarmMenus(Func<UiStateScope> scope)
{
    public const int PageSize = 10;
    private TinyFarmInventoryViewState Inventory => scope().GetOrCreate("inventory.rows", () => new TinyFarmInventoryViewState());
    private TinyFarmStatsViewState Stats => scope().GetOrCreate("agent-properties", () => new TinyFarmStatsViewState());
    private TinyFarmSearchViewState SearchState => scope().GetOrCreate("menu.search", () => new TinyFarmSearchViewState());
    private TinyFarmPauseViewState Pause => scope().GetOrCreate("pause.menu", () => new TinyFarmPauseViewState());

    public InventoryCategory Category
    {
        get => Inventory.Category;
        private set => Inventory.Category = value;
    }

    public InventorySort Sort
    {
        get => Inventory.Sort;
        private set => Inventory.Sort = value;
    }

    public bool Descending
    {
        get => Inventory.Descending;
        private set => Inventory.Descending = value;
    }

    public bool EquipmentOnly
    {
        get => Inventory.EquipmentOnly;
        private set => Inventory.EquipmentOnly = value;
    }

    public string Search
    {
        get => SearchState.Text;
        private set => SearchState.Text = value;
    }

    public bool SearchFocused
    {
        get => SearchFocus.Value;
        set => SearchFocus.Value = value;
    }

    private UiState<bool> SearchFocus => scope().GetOrCreate("menu.search.focus",
        () => new UiState<bool>(false), UiStateLifetime.Activation);

    public string? SelectedKey
    {
        get => Inventory.SelectedKey;
        private set => Inventory.SelectedKey = value;
    }

    public int Offset
    {
        get => Inventory.Offset;
        private set => Inventory.Offset = value;
    }

    public int PauseSelection
    {
        get => Pause.Selection;
        private set => Pause.Selection = value;
    }

    public ActorId StatsAgent
    {
        get => Stats.Agent;
        private set => Stats.Agent = value;
    }

    public string StatsGroup
    {
        get => Stats.Group;
        private set => Stats.Group = value;
    }

    public int StatsOffset
    {
        get => Stats.Offset;
        private set => Stats.Offset = value;
    }

    public string? Confirmation
    {
        get => Pause.Confirmation;
        set => Pause.Confirmation = value;
    }

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
