namespace Aurelian.Strategy;

public readonly record struct RevealSource(int X, int Y, int Radius);
public enum CellVisibility : byte
{
    Unknown,
    Explored,
    Visible
}

/// <summary>Bounded integer disk reveal, without occlusion. The caller owns faction and cadence.</summary>
public sealed class VisibilityGrid
{
    private readonly bool[] explored;
    private readonly bool[] visible;

    public VisibilityGrid(int width, int height)
    {
        if (width is < 1 or > 1024 || height is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        Width = width;
        Height = height;
        explored = new bool[width * height];
        visible = new bool[explored.Length];
    }

    public int Width { get; }
    public int Height { get; }
    public int ExploredCount { get; private set; }

    public CellVisibility this[int x, int y]
    {
        get
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }
            int index = y * Width + x;
            if (visible[index])
            {
                return CellVisibility.Visible;
            }
            return explored[index] ? CellVisibility.Explored : CellVisibility.Unknown;
        }
    }

    public void Recompute(IReadOnlyList<RevealSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (RevealSource source in sources)
        {
            if (source.Radius < 0 || source.Radius > 1024
                || source.X < 0 || source.X >= Width || source.Y < 0 || source.Y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(sources));
            }
        }
        Array.Clear(visible);
        foreach (RevealSource source in sources)
        {
            for (int y = Math.Max(0, source.Y - source.Radius); y <= Math.Min(Height - 1, source.Y + source.Radius); y++)
            {
                for (int x = Math.Max(0, source.X - source.Radius); x <= Math.Min(Width - 1, source.X + source.Radius); x++)
                {
                    int dx = x - source.X;
                    int dy = y - source.Y;
                    if (dx * dx + dy * dy > source.Radius * source.Radius)
                    {
                        continue;
                    }
                    int index = y * Width + x;
                    visible[index] = true;
                    if (!explored[index])
                    {
                        explored[index] = true;
                        ExploredCount++;
                    }
                }
            }
        }
    }

    public bool[] CaptureExploration() => (bool[])explored.Clone();

    public void RestoreExploration(IReadOnlyList<bool> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Count != explored.Length)
        {
            throw new ArgumentException("Exploration dimensions differ.", nameof(saved));
        }
        ExploredCount = 0;
        Array.Clear(visible);
        for (int index = 0; index < explored.Length; index++)
        {
            explored[index] = saved[index];
            if (saved[index])
            {
                ExploredCount++;
            }
        }
    }
}
