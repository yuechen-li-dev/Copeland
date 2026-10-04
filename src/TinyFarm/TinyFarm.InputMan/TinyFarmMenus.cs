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
    public string? Confirmation { get; set; }

    public string CacheKey => $"{Category}|{Sort}|{Descending}|{EquipmentOnly}|{Search}|{SearchFocused}|{SelectedKey}|{Offset}|{PauseSelection}|{Confirmation}";

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
        PauseSelection = Math.Clamp(PauseSelection + delta, 0, 4);
    }

    private IOrderedEnumerable<TinyFarmInventoryRow> Order<T>(IEnumerable<TinyFarmInventoryRow> rows,
        Func<TinyFarmInventoryRow, T> key)
    {
        return Descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
    }
}
