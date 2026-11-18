using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Strategy;

namespace Riverward;

internal sealed partial class CityView
{
    private static readonly ModelMaterial FerryMaterial = new("riverward-ferry") { Metallic = 0, Roughness = .8f };
    private static readonly NativeMesh3D FerryMesh = MakeFerryMesh();
    private static readonly Vector4 Timber = new(.45f, .27f, .13f, 1);
    private static readonly Vector4 Teal = new(.08f, .46f, .48f, 1);
    private static readonly Vector4 Awning = new(.78f, .26f, .12f, 1);

    private FerryStop[] activeStops = [];
    private Vector2[] ferryPath = [];
    private float ferryLength;
    private NativeInstanceSet3D? ferryInstances;

    private void UpdateFerryRoute(FerryStop[] stops)
    {
        if (activeStops.SequenceEqual(stops))
        {
            return;
        }
        activeStops = stops;
        ferryPath = FerryPath(stops);
        ferryLength = 0;
        for (int index = 1; index < ferryPath.Length; index++)
        {
            ferryLength += Vector2.Distance(ferryPath[index - 1], ferryPath[index]);
        }
        ferryInstances = null;
    }

    private Native3DScene PresentFerry(Native3DScene backdrop, City city)
    {
        if (ferryLength <= 0)
        {
            return backdrop;
        }
        float time = (city.Month - 1) + city.MonthProgress / (float)City.MonthTicks;
        float traveled = (time * 22) % (2 * ferryLength);
        bool upstream = traveled > ferryLength;
        float alongRoute = upstream ? 2 * ferryLength - traveled : traveled;
        for (int index = 1; index < ferryPath.Length; index++)
        {
            Vector2 start = ferryPath[index - 1];
            Vector2 end = ferryPath[index];
            float length = Vector2.Distance(start, end);
            if (alongRoute > length && index < ferryPath.Length - 1)
            {
                alongRoute -= length;
                continue;
            }
            Vector2 heading = upstream ? start - end : end - start;
            Vector2 position = Vector2.Lerp(start, end, Math.Clamp(alongRoute / length, 0, 1));
            Matrix4x4 transform = Matrix4x4.CreateRotationY(MathF.Atan2(heading.X, heading.Y))
                * Matrix4x4.CreateTranslation(World(position, -.22f));
            if (ferryInstances is null || ferryInstances[0].Transform != transform)
            {
                ferryInstances = new NativeInstanceSet3D([new("riverward/ferry", FerryMesh, FerryMaterial, transform)], ferryInstances);
            }
            else if (ferryInstances.HasMotion)
            {
                // A 30 Hz town tick often gives consecutive render frames the same boat pose.
                // A resident motion snapshot is single-use, so publish a static pose on the repeat.
                ferryInstances = new NativeInstanceSet3D([new("riverward/ferry", FerryMesh, FerryMaterial, transform)]);
            }
            break;
        }
        return ferryInstances is null ? backdrop : backdrop with { Instances = ferryInstances };
    }

    private static Vector2[] FerryPath(FerryStop[] stops)
    {
        if (stops.Length < 2)
        {
            return [];
        }
        var path = new List<Vector2>();
        for (int index = 0; index < stops.Length; index++)
        {
            FerryStop stop = stops[index];
            Vector2 channel = stop.Water.Center(CityMap.Radius);
            Vector2 bank = Vector2.Lerp(stop.Land.Center(CityMap.Radius), channel, .5f);
            Vector2 berth = Vector2.Lerp(bank, channel, .53f);
            if (index > 0)
            {
                for (int row = stops[index - 1].Water.R + 1; row < stop.Water.R; row++)
                {
                    path.Add(new HexCell(0, row).Center(CityMap.Radius));
                }
                if (path[^1] != channel)
                {
                    path.Add(channel);
                }
            }
            path.Add(berth);
            if (index < stops.Length - 1)
            {
                path.Add(channel);
            }
        }
        return [.. path];
    }

    private static void DrawFerryLine(List<Native3DVertex> vertices, Vector2[] path, Vector4 color)
    {
        for (int index = 1; index < path.Length; index++)
        {
            Vector2 a = path[index - 1];
            Vector2 b = path[index];
            int segments = Math.Max(1, (int)(Vector2.Distance(a, b) * 1.6f));
            for (int dash = 0; dash < segments; dash++)
            {
                Strip(vertices, Vector2.Lerp(a, b, dash / (float)segments),
                    Vector2.Lerp(a, b, (dash + .60f) / segments), -.20f, .055f, color);
            }
        }
    }

    private static void DrawWaterfront(List<Native3DVertex> vertices, HexCell cell, Block block, bool active)
    {
        Vector2 origin = cell.Center(CityMap.Radius);
        Vector2 water = CityMap.WaterAtShore(cell)!.Value.Center(CityMap.Radius);
        float east = cell.Q < 0 ? 1 : -1;
        Vector2 bank = Vector2.Lerp(origin, water, .5f);
        switch (block)
        {
            case Block.Dock:
                Vector4 marker = active ? new(.28f, .82f, .54f, 1) : new(.62f, .47f, .25f, 1);
                Box(vertices, bank, east * .44f, .07f, 0, 1.12f, .08f, .61f, Timber);
                foreach (float side in new[] { -.54f, .54f })
                {
                    Box(vertices, bank, east * 1.44f, .02f, side, .06f, .34f, .06f, Timber);
                    Box(vertices, bank, east * 1.44f, .34f, side, .12f, .10f, .12f, marker);
                }
                Box(vertices, origin, east * .85f, .12f, 0, 1.28f, .12f, .55f, Timber);
                DrawBuilding(vertices, origin + new Vector2(-.45f * east, -.78f), .61f, .46f, .68f, Stone, Teal);
                break;
            case Block.FishMarket:
                DrawBuilding(vertices, origin + new Vector2(-.33f, -.58f), 1.24f, .62f, 1.27f, new(.59f, .75f, .72f, 1), Teal);
                for (int stall = 0; stall < 3; stall++)
                {
                    float x = (stall - 1) * .99f;
                    Box(vertices, origin, x, .42f, .85f, .41f, .28f, .42f, Timber);
                    Box(vertices, origin, x, .93f, .85f, .47f, .07f, .52f, stall == 1 ? Stone : Teal);
                }
                break;
            case Block.Boardwalk:
                Box(vertices, origin, east * 1.15f, .10f, 0, 1.47f, .10f, 1.54f, Timber);
                Box(vertices, bank, -east * .42f, .39f, 0, .08f, .12f, 1.54f, Stone);
                for (int stall = 0; stall < 3; stall++)
                {
                    float z = (stall - 1) * 1.06f;
                    Box(vertices, origin, east * .71f, .45f, z, .24f, .27f, .25f, Stone);
                    Box(vertices, origin, east * .71f, .88f, z, .47f, .09f, .42f, stall == 1 ? Stone : Awning);
                }
                Tree(vertices, origin + new Vector2(-east * 1.36f, -.88f), .86f);
                break;
        }
    }

    private static NativeMesh3D MakeFerryMesh()
    {
        var vertices = new List<Native3DVertex>();
        Box(vertices, Vector2.Zero, 0, .12f, 0, .41f, .12f, .73f, new(.78f, .44f, .07f, 1));
        Box(vertices, Vector2.Zero, 0, .24f, 0, .43f, .035f, .77f, new(.84f, .75f, .53f, 1));
        Box(vertices, Vector2.Zero, 0, .35f, -.06f, .29f, .12f, .31f, new(.13f, .40f, .43f, 1));
        Box(vertices, Vector2.Zero, 0, .47f, -.06f, .36f, .035f, .37f, new(.93f, .88f, .71f, 1));
        for (int offset = 0; offset < vertices.Count; offset += 3)
        {
            Native3DVertex first = vertices[offset];
            Vector3 normal = Vector3.Cross(vertices[offset + 1].Position - first.Position, vertices[offset + 2].Position - first.Position);
            if (Vector3.Dot(normal, first.Normal) < 0)
            {
                (vertices[offset + 1], vertices[offset + 2]) = (vertices[offset + 2], vertices[offset + 1]);
            }
        }
        return new NativeMesh3D("riverward/ferry",
            vertices.Select(vertex =>
            {
                Vector3 axis = MathF.Abs(vertex.Normal.X) < .8f ? Vector3.UnitX : Vector3.UnitZ;
                var tangent = Vector3.Normalize(Vector3.Cross(axis, vertex.Normal));
                return new NativeModel3DVertex(vertex.Position, vertex.Normal, vertex.Color, Vector2.Zero, new(tangent, 1));
            }),
            Enumerable.Range(0, vertices.Count).Select(index => (uint)index));
    }
}
