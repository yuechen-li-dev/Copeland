namespace Machina.Layout.Geometry;

/// <summary>A bounded interval on the layout axis. Use X for rows or Y for columns.</summary>
public readonly record struct LinearLane(double Start, double Length);

public readonly record struct LinearItem(double Length, double ExtraGapBefore = 0);

public readonly record struct LinearPlacement(int Lane, double Start, double Length);

/// <summary>Places an ordered prefix in bounded lanes. The first unplaced item is at the returned count.</summary>
public static class LinearFit
{
    /// <summary>
    /// Wraps to the next lane when an item cannot fit. Gaps apply only between items in the same lane.
    /// The final lane can reserve space for a trailing control such as an overflow button.
    /// </summary>
    public static IReadOnlyList<LinearPlacement> Place(IReadOnlyList<LinearItem> items, IReadOnlyList<LinearLane> lanes,
        double gap = 0, double trailingReserve = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentOutOfRangeException.ThrowIfZero(lanes.Count);
        ValidateLength(gap, nameof(gap));
        ValidateLength(trailingReserve, nameof(trailingReserve));
        foreach (var lane in lanes)
        {
            ValidateLength(lane.Length, nameof(lanes));
            if (!double.IsFinite(lane.Start) || !double.IsFinite(lane.Start + lane.Length))
                throw new ArgumentOutOfRangeException(nameof(lanes), "Lane positions must be finite.");
        }
        foreach (var item in items)
        {
            ValidateLength(item.Length, nameof(items));
            ValidateLength(item.ExtraGapBefore, nameof(items));
        }

        var placed = new List<LinearPlacement>(items.Count);
        int laneIndex = 0;
        double used = 0;
        bool occupied = false;
        foreach (var item in items)
        {
            while (laneIndex < lanes.Count)
            {
                var lane = lanes[laneIndex];
                double start = occupied ? used + gap + item.ExtraGapBefore : 0;
                double end = start + item.Length;
                double available = lane.Length - (laneIndex == lanes.Count - 1 ? trailingReserve : 0);
                if (end <= available)
                {
                    placed.Add(new(laneIndex, lane.Start + start, item.Length));
                    used = end;
                    occupied = true;
                    break;
                }

                laneIndex++;
                used = 0;
                occupied = false;
            }
            if (laneIndex == lanes.Count)
                break;
        }
        return placed;
    }

    private static void ValidateLength(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, "Lengths and gaps must be finite and nonnegative.");
    }
}
