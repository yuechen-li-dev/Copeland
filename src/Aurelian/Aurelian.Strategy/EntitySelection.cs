namespace Aurelian.Strategy;

public enum SelectionChange
{
    Replace,
    Add,
    Subtract,
    Toggle
}

/// <summary>Stable identities only. Applications supply eligibility, type and spatial queries.</summary>
public sealed class EntitySelection<TId> where TId : notnull, IComparable<TId>
{
    private readonly SortedSet<TId> selected = [];
    private readonly Dictionary<int, TId[]> groups = [];

    public IReadOnlyList<TId> Snapshot() => selected.ToArray();

    public bool Contains(TId id) => selected.Contains(id);

    public void Apply(IEnumerable<TId> candidates, SelectionChange change, Func<TId, bool> eligible)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(eligible);
        if (!Enum.IsDefined(change))
        {
            throw new ArgumentOutOfRangeException(nameof(change));
        }
        TId[] accepted = candidates.Distinct().Where(eligible).Order().ToArray();
        if (change == SelectionChange.Replace)
        {
            selected.Clear();
        }
        foreach (TId id in accepted)
        {
            if (change == SelectionChange.Subtract || change == SelectionChange.Toggle && selected.Contains(id))
            {
                selected.Remove(id);
            }
            else
            {
                selected.Add(id);
            }
        }
    }

    public void Prune(Func<TId, bool> eligible)
    {
        selected.RemoveWhere(id => !eligible(id));
        foreach (int group in groups.Keys.ToArray())
        {
            groups[group] = groups[group].Where(eligible).ToArray();
        }
    }

    public void StoreGroup(int group)
    {
        ValidateGroup(group);
        groups[group] = selected.ToArray();
    }

    public void RecallGroup(int group, SelectionChange change, Func<TId, bool> eligible)
    {
        ValidateGroup(group);
        Apply(groups.GetValueOrDefault(group, []), change, eligible);
    }

    private static void ValidateGroup(int group)
    {
        if (group is < 1 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }
    }
}
