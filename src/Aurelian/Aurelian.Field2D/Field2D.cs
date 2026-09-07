namespace Aurelian.Field2D;

public sealed class Field2D<T>
{
    private readonly T[] cells;

    public Field2D(int width, int height, double cellSize, T initialValue = default!)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Field dimensions and cell size must be positive.");
        }
        Width = width;
        Height = height;
        CellSize = cellSize;
        cells = new T[checked(width * height)];
        if (!EqualityComparer<T>.Default.Equals(initialValue, default!))
        {
            Array.Fill(cells, initialValue);
        }
    }

    public int Width { get; }
    public int Height { get; }
    public double CellSize { get; }
    public int Count => cells.Length;
    public Span<T> Cells => cells;
    public ReadOnlySpan<T> ReadOnlyCells => cells;

    public ref T this[int x, int y] => ref cells[Index(x, y)];

    public int Index(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        return (y * Width) + x;
    }

    public (int X, int Y) CellAt(double worldX, double worldY)
    {
        if (!double.IsFinite(worldX) || !double.IsFinite(worldY))
        {
            throw new ArgumentOutOfRangeException(nameof(worldX));
        }
        return (
            Math.Clamp((int)Math.Floor(worldX / CellSize), 0, Width - 1),
            Math.Clamp((int)Math.Floor(worldY / CellSize), 0, Height - 1));
    }

    public T SampleNearest(double worldX, double worldY)
    {
        (int x, int y) = CellAt(worldX, worldY);
        return this[x, y];
    }

    public T[] Snapshot()
    {
        return cells.ToArray();
    }
}
