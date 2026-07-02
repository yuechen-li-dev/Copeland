using System.Collections;
using System.Runtime.CompilerServices;

namespace Aurelian.Core.Presentation.Screens;

[CollectionBuilder(typeof(ScreenLayerOrder), nameof(Create))]
public sealed class ScreenLayerOrder : IReadOnlyList<ScreenLayerSlot>
{
    private readonly ScreenLayerSlot[] compositionSlots;
    private readonly ScreenLayerSlot[] declaredSlots;
    private readonly Dictionary<ScreenLayerKey, LayerPlacement> placements;

    public ScreenLayerOrder(ReadOnlySpan<ScreenLayerSlot> slots)
    {
        declaredSlots = slots.ToArray();
        placements = BuildPlacements(declaredSlots, out compositionSlots);
    }

    public int Count => declaredSlots.Length;

    public ScreenLayerSlot this[int index] => declaredSlots[index];

    public IReadOnlyList<ScreenLayerSlot> DeclaredSlots => declaredSlots;

    public IReadOnlyList<ScreenLayerSlot> CompositionSlots => compositionSlots;

    public static ScreenLayerOrder Create(ReadOnlySpan<ScreenLayerSlot> slots)
    {
        return new ScreenLayerOrder(slots);
    }

    public static ScreenLayerOrder From(ReadOnlySpan<ScreenLayerSlot> slots)
    {
        return new ScreenLayerOrder(slots);
    }

    public bool ContainsLayer(ScreenLayerKey key)
    {
        return placements.ContainsKey(key);
    }

    public ScreenLayerSlot GetSlot(ScreenLayerKey key)
    {
        return GetPlacement(key).Slot;
    }

    public int GetCompositionIndex(ScreenLayerKey key)
    {
        return GetPlacement(key).CompositionIndex;
    }

    public int Compare(ScreenLayerKey left, ScreenLayerKey right)
    {
        return GetCompositionIndex(left).CompareTo(GetCompositionIndex(right));
    }

    public IEnumerator<ScreenLayerSlot> GetEnumerator()
    {
        return ((IEnumerable<ScreenLayerSlot>)declaredSlots).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return declaredSlots.GetEnumerator();
    }

    private LayerPlacement GetPlacement(ScreenLayerKey key)
    {
        if (!placements.TryGetValue(key, out LayerPlacement placement))
        {
            throw new KeyNotFoundException($"Screen layer order does not declare layer '{key.Value}'.");
        }

        return placement;
    }

    private static Dictionary<ScreenLayerKey, LayerPlacement> BuildPlacements(
        IReadOnlyList<ScreenLayerSlot> slots,
        out ScreenLayerSlot[] compositionSlots)
    {
        var declaredPlacements = new List<DeclaredLayerPlacement>(slots.Count);
        var duplicateCheck = new Dictionary<ScreenLayerKey, int>();

        for (int index = 0; index < slots.Count; index++)
        {
            ScreenLayerSlot slot = slots[index];
            if (!duplicateCheck.TryAdd(slot.Key, index))
            {
                throw new ArgumentException(
                    $"Screen layer order contains duplicate layer key '{slot.Key.Value}'.",
                    nameof(slots));
            }

            declaredPlacements.Add(new DeclaredLayerPlacement(slot, index));
        }

        DeclaredLayerPlacement[] sortedPlacements = declaredPlacements
            .OrderBy(static placement => placement.Slot.Order)
            .ThenBy(static placement => placement.Slot.Key.Value, StringComparer.Ordinal)
            .ThenBy(static placement => placement.DeclarationIndex)
            .ToArray();

        compositionSlots = sortedPlacements.Select(static placement => placement.Slot).ToArray();

        var result = new Dictionary<ScreenLayerKey, LayerPlacement>(slots.Count);
        for (int compositionIndex = 0; compositionIndex < sortedPlacements.Length; compositionIndex++)
        {
            DeclaredLayerPlacement placement = sortedPlacements[compositionIndex];
            result.Add(
                placement.Slot.Key,
                new LayerPlacement(placement.Slot, placement.DeclarationIndex, compositionIndex));
        }

        return result;
    }

    private readonly record struct DeclaredLayerPlacement(ScreenLayerSlot Slot, int DeclarationIndex);

    private readonly record struct LayerPlacement(
        ScreenLayerSlot Slot,
        int DeclarationIndex,
        int CompositionIndex);
}
