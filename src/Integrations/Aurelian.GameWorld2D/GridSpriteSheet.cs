namespace Aurelian.GameWorld2D;

/// <summary>Cold import of a regular RGBA pose sheet. Cells are alpha-trimmed with consistent visible scale.</summary>
public static class GridSpriteSheet
{
    public static IReadOnlyList<SpriteFrameMetadata> Import(SpriteAtlasResource atlas, int columns, int rows, double heightAt48PixelsPerMetre)
    {
        if (!double.IsFinite(heightAt48PixelsPerMetre) || heightAt48PixelsPerMetre <= 0
            || columns < 1 || rows < 1 || atlas.Width < columns || atlas.Height < rows
            || atlas.Rgba8.Length != checked(atlas.Width * atlas.Height * 4))
        {
            throw new ArgumentException("Pose sheet must contain a nonempty RGBA grid.");
        }
        var frames = new List<SpriteFrameMetadata>();
        int maximumHeight = 1;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                // Normalized grid edges round once to source pixels; generated images need not have
                // dimensions divisible by the grid count. Adjacent cells share the same boundary.
                int cellX = column * (int)atlas.Width / columns;
                int cellY = row * (int)atlas.Height / rows;
                int cellWidth = (column + 1) * (int)atlas.Width / columns - cellX;
                int cellHeight = (row + 1) * (int)atlas.Height / rows - cellY;
                int left = cellWidth;
                int right = -1;
                int top = cellHeight;
                int bottom = -1;
                for (int y = 0; y < cellHeight; y++)
                {
                    for (int x = 0; x < cellWidth; x++)
                    {
                        int index = ((cellY + y) * (int)atlas.Width + cellX + x) * 4 + 3;
                        if (atlas.Rgba8[index] > 16)
                        {
                            left = Math.Min(left, x);
                            right = Math.Max(right, x);
                            top = Math.Min(top, y);
                            bottom = Math.Max(bottom, y);
                        }
                    }
                }
                if (right < left)
                {
                    throw new InvalidDataException($"Empty pose cell {column},{row}.");
                }
                int width = right - left + 1;
                int height = bottom - top + 1;
                maximumHeight = Math.Max(maximumHeight, height);
                int atlasX = cellX + left;
                int atlasY = cellY + top;
                frames.Add(new SpriteFrameMetadata(frames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    atlasX, atlasY, width, height, width / 2.0, height, 0, 0, 1,
                    new UvRect(atlasX / (double)atlas.Width, atlasY / (double)atlas.Height,
                        (atlasX + width) / (double)atlas.Width, (atlasY + height) / (double)atlas.Height)));
            }
        }
        return frames.Select(frame => frame with { Scale = heightAt48PixelsPerMetre / maximumHeight }).ToArray();
    }
}
