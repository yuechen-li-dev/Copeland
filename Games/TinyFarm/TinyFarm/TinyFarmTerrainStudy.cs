using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Vulkan.Native2D;
using TinyFarm.Core;
using TinyFarm.Runtime;

namespace TinyFarm.Native;

public enum TinyFarmTerrainMode
{
    Legacy,
    Alpha,
    Detail,
    Multiscale,
}

/// <summary>Bounded static-material experiment. The river boundary is projected from the session's liquid mask.</summary>
internal sealed class TinyFarmTerrainStudy
{
    private const int PixelsPerUnit = 48;
    public const int Padding = 1;
    private readonly TinyFarmTerrainMode mode;
    private readonly MaterialImage meadow;
    private readonly MaterialImage dirt;
    private readonly Dictionary<SceneId, SpriteAtlasResource> cache = new();
    private byte[]? cachedLiquidMask;
    public double BuildMilliseconds { get; private set; }
    public long CachedBytes { get; private set; }

    public TinyFarmTerrainStudy(TinyFarmTerrainMode mode, SpriteAtlasResource meadow, SpriteAtlasResource dirt)
    {
        this.mode = mode;
        this.meadow = new(meadow);
        this.dirt = new(dirt, maximumDimension: 192);
    }

    public static bool Supports(SceneId? scene)
    {
        return scene == TinyFarmSceneIds.Farm || scene == TinyFarmSceneIds.Riverside;
    }

    public SpriteAtlasResource Get(TinyFarmFrame frame, TinyFarmFieldRuntime field)
    {
        if (!Supports(frame.ActiveScene) || mode == TinyFarmTerrainMode.Legacy)
        {
            throw new InvalidOperationException("The terrain experiment covers Farm and Riverside only.");
        }
        if (cachedLiquidMask is not null && !cachedLiquidMask.SequenceEqual(field.Fluid.LiquidMask.Cells))
        {
            throw new InvalidOperationException("The terrain study requires the authored static liquid topology; rebuild the presentation for a changed topology.");
        }
        SceneId scene = frame.ActiveScene ?? throw new InvalidOperationException("A terrain cache requires a scene.");
        if (cache.TryGetValue(scene, out SpriteAtlasResource? resource))
        {
            return resource;
        }
        long started = Stopwatch.GetTimestamp();
        int width = checked((frame.SceneWidth + Padding * 2) * PixelsPerUnit);
        int height = checked((frame.SceneHeight + Padding * 2) * PixelsPerUnit);
        var ground = new Vector3[width * height];
        var soil = new Vector3[ground.Length];
        float[] coverage = new float[ground.Length];
        bool riverside = frame.ActiveScene == TinyFarmSceneIds.Riverside;
        var shore = riverside ? new Shoreline(field) : null;
        if (riverside)
        {
            cachedLiquidMask = field.Fluid.LiquidMask.Cells.ToArray();
        }
        // The sprite renderer decodes both the source texture and its sRGB tint.
        Vector3 meadowTint = new(NativeSrgbTransfer.Decode(.83f), NativeSrgbTransfer.Decode(.94f), NativeSrgbTransfer.Decode(.87f));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                double worldX = (x + .5) / PixelsPerUnit - Padding;
                double worldY = (y + .5) / PixelsPerUnit - Padding;
                ground[index] = meadow.Sample((worldX + 4) / (frame.SceneWidth + 8),
                    (worldY + 4) / (frame.SceneHeight + 8), repeat: false) * meadowTint;
                Vector3 earth = dirt.Sample(worldX / 3.2, worldY / 3.2, repeat: true);
                double detail = Noise(worldX * 5, worldY * 5) * .065 + Noise(worldX * 15, worldY * 15) * .025;
                if (!riverside)
                {
                    double distance = GroundBrushRasterizer.SignedDistance(new(worldX, worldY), TinyFarmSliceArt.FarmPathStrokes);
                    double widthOfEdge = mode == TinyFarmTerrainMode.Alpha ? .30 : .065;
                    double shaped = distance;
                    if (mode != TinyFarmTerrainMode.Alpha)
                    {
                        shaped += detail;
                    }
                    coverage[index] = 1 - Smooth(-widthOfEdge, widthOfEdge, shaped);
                    soil[index] = earth;
                }
                else
                {
                    double distance = shore!.SignedDistance(worldX, worldY);
                    // Negative is wet. Wet ground becomes a muted riverbed beneath the live water pass.
                    double dryDistance = Math.Max(distance, 0);
                    double shaped = dryDistance + (mode == TinyFarmTerrainMode.Alpha ? 0 : detail);
                    double widthOfEdge = mode == TinyFarmTerrainMode.Alpha ? .34 : .09;
                    coverage[index] = 1 - Smooth(.55 - widthOfEdge, .55 + widthOfEdge, shaped);
                    float damp = 1 - Smooth(0, .38, dryDistance);
                    Vector3 dampTint = Vector3.Lerp(Vector3.One, new(.48f, .53f, .48f), damp);
                    float bankShadow = 1 - .18f * (1 - Smooth(0, .085, Math.Abs(distance)));
                    soil[index] = earth * dampTint * bankShadow;
                }
            }
        }
        GroundTextureBlend method = mode == TinyFarmTerrainMode.Multiscale ? GroundTextureBlend.Multiscale : GroundTextureBlend.Linear;
        byte[] pixels = GroundTextureBlender.Blend(width, height, ground, soil, coverage, method);
        string hash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        resource = new(new SpriteAssetId($"terrain-study-{mode}-{scene.Value}"), hash,
            (uint)width, (uint)height, pixels, SpriteSampling.Linear);
        cache.Add(scene, resource);
        CachedBytes += pixels.Length;
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        BuildMilliseconds += elapsed;
        Console.WriteLine($"TINYFARM_TERRAIN_COMPOSED mode={mode} scene={scene.Value} ms={elapsed:F2} bytes={pixels.Length} sha256={hash}");
        return resource;
    }

    private static float Smooth(double minimum, double maximum, double value)
    {
        double normalized = Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
        return (float)(normalized * normalized * (3 - 2 * normalized));
    }

    private static double Noise(double x, double y)
    {
        static double Hash(int column, int row)
        {
            uint value = unchecked((uint)column * 374761393 + (uint)row * 668265263 + 1442695041);
            value = unchecked((value ^ (value >> 13)) * 1274126177);
            return ((value ^ (value >> 16)) & 65535) / 65535.0 * 2 - 1;
        }
        int left = (int)Math.Floor(x);
        int top = (int)Math.Floor(y);
        float horizontal = Smooth(0, 1, x - left);
        float vertical = Smooth(0, 1, y - top);
        double north = Hash(left, top) * (1 - horizontal) + Hash(left + 1, top) * horizontal;
        double south = Hash(left, top + 1) * (1 - horizontal) + Hash(left + 1, top + 1) * horizontal;
        return north * (1 - vertical) + south * vertical;
    }

    private sealed class MaterialImage
    {
        private readonly int width;
        private readonly int height;
        private readonly Vector3[] linear;

        public MaterialImage(SpriteAtlasResource resource, int maximumDimension = int.MaxValue)
        {
            width = (int)resource.Width;
            height = (int)resource.Height;
            linear = new Vector3[width * height];
            for (int index = 0; index < linear.Length; index++)
            {
                linear[index] = new(GroundTextureBlender.Decode(resource.Rgba8[index * 4]),
                    GroundTextureBlender.Decode(resource.Rgba8[index * 4 + 1]), GroundTextureBlender.Decode(resource.Rgba8[index * 4 + 2]));
            }
            // Pre-filter the repeated material to the experiment's world texel density in linear light.
            while (width > maximumDimension || height > maximumDimension)
            {
                int nextWidth = Math.Max(1, width / 2);
                int nextHeight = Math.Max(1, height / 2);
                var next = new Vector3[nextWidth * nextHeight];
                for (int y = 0; y < nextHeight; y++)
                {
                    for (int x = 0; x < nextWidth; x++)
                    {
                        int left = x * 2;
                        int top = y * 2;
                        int right = Math.Min(left + 1, width - 1);
                        int bottom = Math.Min(top + 1, height - 1);
                        next[y * nextWidth + x] = (linear[top * width + left] + linear[top * width + right]
                            + linear[bottom * width + left] + linear[bottom * width + right]) / 4;
                    }
                }
                width = nextWidth;
                height = nextHeight;
                linear = next;
            }
        }

        public Vector3 Sample(double u, double v, bool repeat)
        {
            if (repeat)
            {
                // Mirrored repeat guarantees matching edge colors even when a generated tile is imperfectly seamless.
                u -= Math.Floor(u / 2) * 2;
                v -= Math.Floor(v / 2) * 2;
                if (u > 1)
                {
                    u = 2 - u;
                }
                if (v > 1)
                {
                    v = 2 - v;
                }
            }
            double x = Math.Clamp(u * width - .5, 0, width - 1);
            double y = Math.Clamp(v * height - .5, 0, height - 1);
            int left = (int)x;
            int top = (int)y;
            int right = Math.Min(left + 1, width - 1);
            int bottom = Math.Min(top + 1, height - 1);
            Vector3 north = Vector3.Lerp(linear[top * width + left], linear[top * width + right], (float)(x - left));
            Vector3 south = Vector3.Lerp(linear[bottom * width + left], linear[bottom * width + right], (float)(x - left));
            return Vector3.Lerp(north, south, (float)(y - top));
        }
    }

    private sealed class Shoreline
    {
        private readonly List<(Vector2 Start, Vector2 End)> edges = [];
        private readonly TinyFarmFieldDefinition definition;
        private readonly byte[] wet;
        private readonly float cell;
        private readonly Vector2 origin;

        public Shoreline(TinyFarmFieldRuntime field)
        {
            definition = field.Definition;
            wet = field.Fluid.LiquidMask.Cells.ToArray();
            cell = definition.CellSize / (float)ScenePosition.UnitsPerTile;
            origin = new(definition.Origin.XUnits / (float)ScenePosition.UnitsPerTile,
                definition.Origin.YUnits / (float)ScenePosition.UnitsPerTile);
            bool IsWet(int x, int y) => x >= 0 && y >= 0 && x < definition.Width && y < definition.Height && wet[y * definition.Width + x] != 0;
            for (int y = 0; y < definition.Height; y++)
            {
                for (int x = 0; x < definition.Width; x++)
                {
                    if (!IsWet(x, y))
                    {
                        continue;
                    }
                    Vector2 at = origin + new Vector2(x * cell, y * cell);
                    if (!IsWet(x - 1, y))
                    {
                        edges.Add((at, at + new Vector2(0, cell)));
                    }
                    if (!IsWet(x + 1, y))
                    {
                        edges.Add((at + new Vector2(cell, 0), at + new Vector2(cell, cell)));
                    }
                    if (!IsWet(x, y - 1))
                    {
                        edges.Add((at, at + new Vector2(cell, 0)));
                    }
                    if (!IsWet(x, y + 1))
                    {
                        edges.Add((at + new Vector2(0, cell), at + new Vector2(cell, cell)));
                    }
                }
            }
            if (edges.Count == 0)
            {
                throw new InvalidDataException("The terrain study requires a liquid mask with a boundary.");
            }
        }

        public double SignedDistance(double x, double y)
        {
            Vector2 point = new((float)x, (float)y);
            float nearest = float.PositiveInfinity;
            foreach (var edge in edges)
            {
                Vector2 direction = edge.End - edge.Start;
                float amount = Math.Clamp(Vector2.Dot(point - edge.Start, direction) / direction.LengthSquared(), 0, 1);
                nearest = Math.Min(nearest, Vector2.DistanceSquared(point, edge.Start + direction * amount));
            }
            int column = (int)Math.Floor((x - origin.X) / cell);
            int row = (int)Math.Floor((y - origin.Y) / cell);
            bool inside = column >= 0 && row >= 0 && column < definition.Width && row < definition.Height && wet[row * definition.Width + column] != 0;
            return Math.Sqrt(nearest) * (inside ? -1 : 1);
        }
    }
}
