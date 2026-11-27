using System.Collections.Immutable;
using Machina.Layout.Geometry;

namespace Machina.Standard.Authoring;

/// <summary>What a flow does with items that do not fit the available width.</summary>
public enum FlowOverflow
{
    /// <summary>Keep the leading items that fit; report the rest in <see cref="FlowLayout.Dropped"/>.</summary>
    Drop,
    /// <summary>Start a new row.</summary>
    Wrap,
    /// <summary>Throw: the caller promised everything fits.</summary>
    Throw,
}

/// <summary>One placed item: its index in the input, its row, and its position relative to the flow's origin.</summary>
public readonly record struct FlowSlot(int Index, int Row, double X, double Y, double Width, double Height);

/// <summary>The computed positions. Pure data: print it, assert on it, then place nodes at the slots.</summary>
public sealed record FlowLayout(ImmutableArray<FlowSlot> Slots, int Dropped, double Width, double Height, int Rows);

/// <summary>
/// Explicit horizontal flow: a pure function from widths to positions, with overflow as a declared policy rather than a
/// layout exception. It replaces hand-written <c>x += width + gap</c> cursor walks; nothing is measured implicitly.
/// </summary>
public static class Flow
{
    /// <param name="groupBreaks">Indexes that start a new visual group: an extra <paramref name="groupGap"/> precedes them.</param>
    public static FlowLayout Horizontal(double available, IReadOnlyList<double> widths, double gap = 0, FlowOverflow overflow = FlowOverflow.Drop,
        double rowHeight = 0, double rowGap = 0, IReadOnlySet<int>? groupBreaks = null, double groupGap = 0)
    {
        ArgumentNullException.ThrowIfNull(widths);
        if (!double.IsFinite(available) || available < 0) throw new ArgumentOutOfRangeException(nameof(available));
        foreach (double value in new[] { gap, rowHeight, rowGap, groupGap })
            if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(gap), "Gaps and heights must be finite and non-negative.");
        var measured = widths.Select((width, index) =>
            new LinearItem(width, groupBreaks?.Contains(index) == true ? groupGap : 0)).ToArray();
        int laneCount = overflow == FlowOverflow.Wrap ? Math.Max(widths.Count, 1) : 1;
        var lanes = Enumerable.Repeat(new LinearLane(0, available), laneCount).ToArray();
        var placements = LinearFit.Place(measured, lanes, gap);
        int dropped = widths.Count - placements.Count;
        if (dropped > 0 && overflow == FlowOverflow.Throw)
            throw new InvalidOperationException($"Flow item {placements.Count} ({widths[placements.Count]:0.##}) does not fit in {available:0.##}.");

        var slots = placements.Select((item, index) => new FlowSlot(index, item.Lane, item.Start,
            item.Lane * (rowHeight + rowGap), item.Length, rowHeight)).ToImmutableArray();
        int rows = slots.IsEmpty ? 0 : slots[^1].Row + 1;
        double used = slots.IsEmpty ? 0 : slots.Max(slot => slot.X + slot.Width);
        return new FlowLayout(slots, dropped, used, rows == 0 ? 0 : rows * rowHeight + (rows - 1) * rowGap, rows);
    }
}
