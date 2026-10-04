using System.Security.Cryptography;

namespace Aurelian.GameWorld2D;

public sealed record GroundBrushStroke(IReadOnlyList<WorldPoint2> Points, double Radius);

/// <summary>Cold realization of soft painted path strokes. Coverage uses union rather than layered disk overlap.</summary>
public static class GroundBrushRasterizer
{
    public static SpriteAtlasResource Realize(SpriteAssetId id, double worldWidth, double worldHeight,
        int pixelsPerMetre, IReadOnlyList<GroundBrushStroke> strokes, uint rgba, uint seed = 1)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || pixelsPerMetre < 1
            || strokes.Any(stroke => stroke.Radius <= 0 || stroke.Points.Count < 2))
        {
            throw new ArgumentException("A ground brush needs positive bounds, density and nonempty paths.");
        }
        int width = checked((int)Math.Ceiling(worldWidth * pixelsPerMetre));
        int height = checked((int)Math.Ceiling(worldHeight * pixelsPerMetre));
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double worldX = (x + .5) / pixelsPerMetre;
                double worldY = (y + .5) / pixelsPerMetre;
                double coverage = 0;
                foreach (GroundBrushStroke stroke in strokes)
                {
                    for (int segment = 1; segment < stroke.Points.Count; segment++)
                    {
                        WorldPoint2 start = stroke.Points[segment - 1];
                        WorldPoint2 end = stroke.Points[segment];
                        double dx = end.X - start.X;
                        double dy = end.Y - start.Y;
                        double lengthSquared = dx * dx + dy * dy;
                        double fraction = lengthSquared == 0 ? 0 : Math.Clamp(
                            ((worldX - start.X) * dx + (worldY - start.Y) * dy) / lengthSquared, 0, 1);
                        double offsetX = worldX - start.X - dx * fraction;
                        double offsetY = worldY - start.Y - dy * fraction;
                        double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                        double edgeNoise = SmoothNoise(x / 22.0, y / 22.0, seed) * .06;
                        double amount = Math.Clamp((stroke.Radius + .13 + edgeNoise - distance) / .26, 0, 1);
                        coverage = Math.Max(coverage, amount * amount * (3 - 2 * amount));
                    }
                }
                if (coverage <= 0)
                {
                    continue;
                }
                double variation = 1 + SmoothNoise(x / 12.0, y / 12.0, seed + 7) * .06;
                int offset = (y * width + x) * 4;
                pixels[offset] = (byte)Math.Clamp((rgba >> 24) * variation, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp(((rgba >> 16) & 255) * variation, 0, 255);
                pixels[offset + 2] = (byte)Math.Clamp(((rgba >> 8) & 255) * variation, 0, 255);
                pixels[offset + 3] = (byte)((rgba & 255) * coverage);
            }
        }
        string hash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        return new SpriteAtlasResource(id, hash, (uint)width, (uint)height, pixels, SpriteSampling.Linear);
    }

    private static double SmoothNoise(double x, double y, uint seed)
    {
        int left = (int)Math.Floor(x);
        int top = (int)Math.Floor(y);
        double tx = x - left;
        double ty = y - top;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double north = Noise(left, top, seed) * (1 - tx) + Noise(left + 1, top, seed) * tx;
        double south = Noise(left, top + 1, seed) * (1 - tx) + Noise(left + 1, top + 1, seed) * tx;
        return north * (1 - ty) + south * ty;
    }

    private static double Noise(int x, int y, uint seed)
    {
        uint bits = unchecked((uint)x * 374761393 + (uint)y * 668265263 + seed * 1442695041);
        bits = unchecked((bits ^ (bits >> 13)) * 1274126177);
        bits ^= bits >> 16;
        return (bits & 65535) / 65535.0 * 2 - 1;
    }
}
