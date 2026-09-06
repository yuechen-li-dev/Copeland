using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Aurelian.GameWorld2D;
using Dominatus.SpriteForge;

namespace TinyFarm.Native;

internal sealed class TinyFarmSpriteAtlas
{
    public const int Columns = 4;
    public const int Rows = 4;
    public const int CellSize = 312;
    public const int Width = Columns * CellSize;
    public const int Height = Rows * CellSize;

    private TinyFarmSpriteAtlas(
        SpriteForgeAtlas metadata,
        SpriteAtlasResource resource,
        SpriteAlphaCleanupFacts alphaCleanup)
    {
        Metadata = metadata;
        Resource = resource;
        AlphaCleanup = alphaCleanup;
    }

    public SpriteForgeAtlas Metadata { get; }

    public SpriteAtlasResource Resource { get; }

    public SpriteAlphaCleanupFacts AlphaCleanup { get; }

    public static TinyFarmSpriteAtlas Load(string path)
    {
        byte[] rgba = ReadStraightRgba(path, out int width, out int height, out SpriteAlphaCleanupFacts alphaCleanup);
        if (width != Width || height != Height)
        {
            throw new InvalidDataException($"TinyFarm M11 atlas must be {Width}x{Height}, but was {width}x{height}.");
        }

        string hash = Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant();
        var resource = new SpriteAtlasResource(
            new SpriteAssetId("tinyfarm-m11-atlas"),
            hash,
            Width,
            Height,
            rgba,
            SpriteSampling.Nearest);
        return new TinyFarmSpriteAtlas(CreateMetadata(path), resource, alphaCleanup);
    }

    private static SpriteForgeAtlas CreateMetadata(string path)
    {
        var grid = new SpriteForgeGrid
        {
            Id = "m11-cells",
            Columns = Columns,
            Rows = Rows,
            CellWidth = CellSize,
            CellHeight = CellSize,
            DefaultPivot = SpriteForgePivots.BottomCenter,
        };
        var sprites = new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal)
        {
            ["grass-a"] = Cell("grass-a", "tile", 0, 0, SpriteForgePivots.Center, 48f / CellSize),
            ["grass-b"] = Cell("grass-b", "tile", 0, 1, SpriteForgePivots.Center, 48f / CellSize),
            ["grass-c"] = Cell("grass-c", "tile", 0, 2, SpriteForgePivots.Center, 48f / CellSize),
            ["grass-d"] = Cell("grass-d", "tile", 0, 3, SpriteForgePivots.Center, 48f / CellSize),
            ["wall"] = Cell("wall", "terrain", 1, 0, SpriteForgePivots.BottomCenter, 50f / CellSize),
            ["fence"] = Cell("fence", "prop", 1, 1, SpriteForgePivots.BottomCenter, 54f / CellSize),
            ["tree"] = Cell("tree", "prop", 1, 2, SpriteForgePivots.BottomCenter, 92f / CellSize),
            ["market"] = Cell("market", "prop", 1, 3, SpriteForgePivots.BottomCenter, 92f / CellSize),
            ["mint"] = Cell("mint", "prop", 3, 0, SpriteForgePivots.BottomCenter, 44f / CellSize),
            ["well"] = Cell("well", "prop", 3, 1, SpriteForgePivots.BottomCenter, 76f / CellSize),
            ["hearth"] = Cell("hearth", "prop", 3, 2, SpriteForgePivots.BottomCenter, 76f / CellSize),
            ["lantern"] = Cell("lantern", "prop", 3, 3, SpriteForgePivots.BottomCenter, 58f / CellSize),
            ["farmer"] = new SpriteForgeSprite
            {
                Id = "farmer",
                Kind = "actor",
                Scale = 70f / CellSize,
                Pivot = SpriteForgePivots.BottomCenter,
                Animations = new Dictionary<string, SpriteForgeAnimation>(StringComparer.Ordinal)
                {
                    ["walk-down"] = new SpriteForgeAnimation
                    {
                        Id = "walk-down",
                        Grid = grid.Id,
                        Row = 2,
                        Frames =
                        [
                            new SpriteForgeFrameRef { Col = 0 },
                            new SpriteForgeFrameRef { Col = 1 },
                            new SpriteForgeFrameRef { Col = 2 },
                            new SpriteForgeFrameRef { Col = 3 },
                        ],
                        Fps = 6,
                        Loop = true,
                    },
                },
            },
        };
        return new SpriteForgeAtlas
        {
            SourcePath = path,
            Image = Path.GetFileName(path),
            ResolvedImagePath = Path.GetFullPath(path),
            Width = Width,
            Height = Height,
            Grids = new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal) { [grid.Id] = grid },
            Sprites = sprites,
        };
    }

    private static SpriteForgeSprite Cell(
        string id,
        string kind,
        int row,
        int column,
        string pivot,
        float scale)
    {
        return new SpriteForgeSprite
        {
            Id = id,
            Kind = kind,
            Grid = "m11-cells",
            Row = row,
            Col = column,
            Pivot = pivot,
            Scale = scale,
        };
    }

    private static byte[] ReadStraightRgba(
        string path,
        out int width,
        out int height,
        out SpriteAlphaCleanupFacts alphaCleanup)
    {
        using var source = new Bitmap(path);
        if (source.Width < Width || source.Height < Height)
        {
            throw new InvalidDataException($"TinyFarm M11 source atlas must be at least {Width}x{Height}.");
        }
        width = Width;
        height = Height;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            int sourceX = (source.Width - width) / 2;
            int sourceY = (source.Height - height) / 2;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                new Rectangle(sourceX, sourceY, width, height),
                GraphicsUnit.Pixel);
        }

        Rectangle bounds = new(0, 0, width, height);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] rgba = new byte[checked(width * height * 4)];
            byte[] row = new byte[checked(width * 4)];
            int sourceTransparent = 0;
            int sourcePartial = 0;
            int sourceOpaque = 0;
            int outputTransparent = 0;
            int outputOpaque = 0;
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < width; x++)
                {
                    int sourceOffset = x * 4;
                    int targetOffset = (y * width + x) * 4;
                    byte sourceAlpha = row[sourceOffset + 3];
                    if (sourceAlpha == 0)
                    {
                        sourceTransparent++;
                    }
                    else if (sourceAlpha == 255)
                    {
                        sourceOpaque++;
                    }
                    else
                    {
                        sourcePartial++;
                    }

                    byte outputAlpha = sourceAlpha < 128 ? (byte)0 : (byte)255;
                    if (outputAlpha == 0)
                    {
                        rgba[targetOffset] = 0;
                        rgba[targetOffset + 1] = 0;
                        rgba[targetOffset + 2] = 0;
                        outputTransparent++;
                    }
                    else
                    {
                        rgba[targetOffset] = row[sourceOffset + 2];
                        rgba[targetOffset + 1] = row[sourceOffset + 1];
                        rgba[targetOffset + 2] = row[sourceOffset];
                        outputOpaque++;
                    }
                    rgba[targetOffset + 3] = outputAlpha;
                }
            }
            alphaCleanup = new SpriteAlphaCleanupFacts(
                Threshold: 128,
                SourceTransparentPixels: sourceTransparent,
                SourcePartialPixels: sourcePartial,
                SourceOpaquePixels: sourceOpaque,
                OutputTransparentPixels: outputTransparent,
                OutputOpaquePixels: outputOpaque);
            return rgba;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

internal sealed record SpriteAlphaCleanupFacts(
    int Threshold,
    int SourceTransparentPixels,
    int SourcePartialPixels,
    int SourceOpaquePixels,
    int OutputTransparentPixels,
    int OutputOpaquePixels);

internal static class TinyFarmAuthoredTileMap
{
    private static readonly string[] FarmRows =
    [
        "ABCDABCDABCDABCDABCDAB",
        "BCDABCDABCDABCDABCDABC",
        "CDABCDABCDABCDABCDABCD",
        "DABCDABCDABCDABCDABCDA",
        "ABCDABCDABCDABCDABCDAB",
        "BCDABCDABCDABCDABCDABC",
        "CDABCDABCDABCDABCDABCD",
        "DABCDABCDABCDABCDABCDA",
        "ABCDABCDABCDABCDABCDAB",
        "BCDABCDABCDABCDABCDABC",
        "CDABCDABCDABCDABCDABCD",
        "DABCDABCDABCDABCDABCDA",
        "ABCDABCDABCDABCDABCDAB",
        "BCDABCDABCDABCDABCDABC",
    ];

    public static string TileAt(int x, int y, bool indoor, bool cave)
    {
        if (cave)
        {
            return "wall";
        }
        if (indoor)
        {
            return (x + y) % 2 == 0 ? "grass-b" : "grass-d";
        }
        char cell = y < FarmRows.Length && x < FarmRows[y].Length
            ? FarmRows[y][x]
            : FarmRows[Math.Abs(y) % FarmRows.Length][Math.Abs(x) % FarmRows[0].Length];
        return cell switch
        {
            'A' => "grass-a",
            'B' => "grass-b",
            'C' => "grass-c",
            _ => "grass-d",
        };
    }
}
