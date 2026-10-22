using TinyFarm.Core;

namespace TinyFarm.Native;

/// <summary>Fine shoreline coverage from authored geometry, with live coarse fluid channels interpolated beneath it.</summary>
internal sealed class TinyFarmRiverProjection
{
    public const int Density = 20;
    public const double Left = 6.5;
    public const double Top = -2.5;
    public const double WorldWidth = 11;
    public const double WorldHeight = 15;
    public const int Width = (int)(WorldWidth * Density);
    public const int Height = (int)(WorldHeight * Density);
    private readonly byte[] shore = new byte[Width * Height];

    public TinyFarmRiverProjection()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                double worldX = Left + (x + .5) / Density;
                double worldY = Top + (y + .5) / Density;
                double distance = TinyFarmLandscape.RiverDistance(worldX, worldY);
                shore[y * Width + x] = (byte)Math.Clamp((int)Math.Round((.5 - distance * .4) * 255), 0, 255);
            }
        }
    }

    public byte[] Project(TinyFarmFieldRuntime runtime)
    {
        byte[] source = runtime.ProjectRgba8();
        var pixels = new byte[Width * Height * 4];
        var definition = runtime.Definition;
        double cell = definition.CellSize / (double)ScenePosition.UnitsPerTile;
        double originX = definition.Origin.XUnits / (double)ScenePosition.UnitsPerTile;
        double originY = definition.Origin.YUnits / (double)ScenePosition.UnitsPerTile;
        for (int y = 0; y < Height; y++)
        {
            double sourceY = Math.Clamp((Top + (y + .5) / Density - originY) / cell - .5, 0, definition.Height - 1);
            int north = (int)sourceY;
            int south = Math.Min(north + 1, definition.Height - 1);
            double vertical = sourceY - north;
            for (int x = 0; x < Width; x++)
            {
                double sourceX = Math.Clamp((Left + (x + .5) / Density - originX) / cell - .5, 0, definition.Width - 1);
                int west = (int)sourceX;
                int east = Math.Min(west + 1, definition.Width - 1);
                double horizontal = sourceX - west;
                int target = (y * Width + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    if (channel == 2)
                    {
                        pixels[target + channel] = shore[y * Width + x];
                        continue;
                    }
                    double first = source[(north * definition.Width + west) * 4 + channel] * (1 - horizontal)
                        + source[(north * definition.Width + east) * 4 + channel] * horizontal;
                    double second = source[(south * definition.Width + west) * 4 + channel] * (1 - horizontal)
                        + source[(south * definition.Width + east) * 4 + channel] * horizontal;
                    pixels[target + channel] = (byte)Math.Round(first * (1 - vertical) + second * vertical);
                }
            }
        }
        return pixels;
    }
}
