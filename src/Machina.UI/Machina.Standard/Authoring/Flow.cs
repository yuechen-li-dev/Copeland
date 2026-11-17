using System.Collections.Immutable;

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
        var slots = ImmutableArray.CreateBuilder<FlowSlot>(widths.Count);
        double x = 0, used = 0;
        int row = 0, dropped = 0;
        for (int index = 0; index < widths.Count; index++)
        {
            double width = widths[index];
            if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(widths), $"Width {index} must be finite and non-negative.");
            double lead = x == 0 ? 0 : gap + (groupBreaks?.Contains(index) == true ? groupGap : 0);
            if (x + lead + width > available + 1e-9)
            {
                if (overflow == FlowOverflow.Throw)
                    throw new InvalidOperationException($"Flow item {index} ({width:0.##}) does not fit in {available:0.##}.");
                if (overflow == FlowOverflow.Drop || x == 0)
                {
                    // A dropped item ends the row for Drop; a single over-wide item can never be placed either way.
                    dropped = widths.Count - index;
                    break;
                }
                row++; x = 0; lead = 0;
            }
            x += lead;
            slots.Add(new FlowSlot(index, row, x, row * (rowHeight + rowGap), width, rowHeight));
            x += width;
            used = Math.Max(used, x);
        }
        int rows = slots.Count == 0 ? 0 : row + 1;
        return new FlowLayout(slots.ToImmutable(), dropped, used, rows == 0 ? 0 : rows * rowHeight + (rows - 1) * rowGap, rows);
    }
}
