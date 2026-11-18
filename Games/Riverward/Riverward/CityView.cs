using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Runtime;
using Aurelian.Strategy;

namespace Riverward;

internal sealed partial class CityView
{
    internal static readonly Vector4 Green = new(.20f, .82f, .50f, 1);
    internal static readonly Vector4 Red = new(.93f, .22f, .12f, 1);
    internal static readonly Vector4 Gold = new(.99f, .70f, .25f, 1);
    private static readonly Vector4[] Grass =
    [
        new(.16f, .31f, .15f, 1), new(.18f, .33f, .17f, 1), new(.21f, .35f, .17f, 1),
        new(.16f, .30f, .13f, 1), new(.20f, .33f, .15f, 1),
    ];
    private static readonly HexCell[] Groves =
    [
        new(-4, -3), new(-5, -2), new(-5, 0), new(-4, 2), new(-6, 2), new(-7, 4),
        new(2, -4), new(4, -4), new(4, -3), new(5, -2), new(5, 0), new(3, 2), new(2, 3),
    ];
    private static readonly Vector4 Paving = new(.52f, .43f, .30f, 1);
    private static readonly Vector4 Asphalt = new(.15f, .18f, .18f, 1);
    private static readonly Vector4 Stone = new(.76f, .66f, .51f, 1);
    private static readonly Vector4 Window = new(.10f, .23f, .26f, 1);
    private static readonly Native3DVertex[] Terrain = MakeTerrain();

    private Native3DVertex[] town = [];
    private Native3DScene scene = new([], []);
    private int renderedRevision = -1;
    private (Tool Tool, CityTarget? Target) renderedHover;
    private Vector3 focus;
    private float yaw = -.14f;
    private float distance = 49;

    internal void Move(float forward, float sideways, float orbit, float zoom, float seconds)
    {
        yaw = MathF.IEEERemainder(yaw + orbit * seconds * 1.35f, MathF.Tau);
        Vector3 south = new(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        Vector3 east = new(south.Z, 0, -south.X);
        Vector3 movement = east * sideways - south * forward;
        if (movement.LengthSquared() > 1)
        {
            movement = Vector3.Normalize(movement);
        }
        focus += movement * (distance * .43f * seconds);
        focus = Vector3.Clamp(focus, new(-46, 0, -27), new(46, 0, 27));
        distance = Math.Clamp(distance * MathF.Pow(.89f, zoom), 20, 100);
    }

    internal (Matrix4x4 Matrix, Vector3 Eye) Camera(float aspect)
    {
        Vector3 eye = focus + distance * new Vector3(MathF.Sin(yaw) * .60f, .80f, MathF.Cos(yaw) * .60f);
        return (Camera3D.Matrix(eye, Vector3.Normalize(focus - eye), aspect, 250), eye);
    }

    internal CityTarget? Pick(Vector2 pixel, Vector2 size, Tool tool)
    {
        if (!Camera3D.TryPickPlane(pixel, size, Camera(size.X / size.Y).Matrix, 0, out Vector3 point))
        {
            return null;
        }
        Vector2 ground = new(point.X, point.Z);
        HexCell cell = HexCell.FromPosition(ground, CityMap.Radius);
        if (!CityMap.Contains(cell))
        {
            return null;
        }
        HexEdge edge = cell.Edge(cell.ClosestSide(ground, CityMap.Radius));
        Vector2 a = edge.A.Position(CityMap.Radius);
        Vector2 b = edge.B.Position(CityMap.Radius);
        Vector2 direction = Vector2.Normalize(b - a);
        Vector2 offset = ground - a;
        float edgeDistance = MathF.Abs(direction.X * offset.Y - direction.Y * offset.X);
        return new(cell, edge, tool == Tool.Streets || (tool == Tool.Clear && edgeDistance < .58f));
    }

    internal GameView Render(GameViewRequest request, City city, Tool tool, CityTarget? hover, Placement? preview)
    {
        bool changed = renderedRevision != city.Revision;
        if (changed)
        {
            var vertices = new List<Native3DVertex>();
            var pavedCorners = new HashSet<HexCorner>();
            foreach (HexEdge edge in city.Streets)
            {
                AddStreet(vertices, edge, .15f, .44f, Asphalt);
                pavedCorners.Add(edge.A);
                pavedCorners.Add(edge.B);
            }
            foreach (HexCorner corner in pavedCorners)
            {
                PrimitiveGeometry3D.AddBox(vertices, World(corner.Position(CityMap.Radius), .055f),
                    new(.40f, .05f, .40f), Asphalt);
            }
            foreach (HexCell cell in Groves)
            {
                if (!city.Blocks.ContainsKey(cell))
                {
                    Vector2 origin = cell.Center(CityMap.Radius);
                    Tree(vertices, origin + new Vector2(-1.2f, .20f), 1.20f);
                    Tree(vertices, origin + new Vector2(.10f, -.83f), 1.52f);
                    Tree(vertices, origin + new Vector2(1.18f, .78f), 1.07f);
                }
            }
            DrawBridge(vertices);
            DrawHall(vertices);
            foreach ((HexCell cell, Block block) in city.Blocks)
            {
                DrawBlock(vertices, cell, block, city.Report.Blocks[cell]);
            }
            UpdateFerryRoute(city.Report.FerryStops);
            town = [.. vertices];
            renderedRevision = city.Revision;
        }
        if (changed || renderedHover != (tool, hover))
        {
            var overlay = new List<Native3DVertex>();
            if (hover is { } target)
            {
                Vector4 color = tool == Tool.Inspect ? Gold : preview?.Allowed == true ? Green : Red;
                if (target.Street)
                {
                    AddStreet(overlay, target.Edge, .24f, .28f, color);
                }
                else
                {
                    Outline(overlay, target.Cell, color, .91f, .085f);
                }
                foreach (HexCell cell in preview?.Improved ?? [])
                {
                    Outline(overlay, cell, Green, .78f, .12f);
                }
                foreach (HexCell cell in preview?.Worsened ?? [])
                {
                    Outline(overlay, cell, Red, .78f, .12f);
                }
                if (preview?.Forecast is { } forecast && !forecast.FerryStops.SequenceEqual(city.Report.FerryStops))
                {
                    bool opening = forecast.FerryStops.Length >= city.Report.FerryStops.Length;
                    DrawFerryLine(overlay, FerryPath(opening ? forecast.FerryStops : city.Report.FerryStops), opening ? Green : Red);
                }
                else if (tool == Tool.Inspect && (CityMap.IsWater(target.Cell) || activeStops.Any(stop => stop.Land == target.Cell)))
                {
                    DrawFerryLine(overlay, ferryPath, new(.28f, .78f, .70f, 1));
                }
            }
            scene = new([.. Terrain, .. town, .. overlay], []);
            renderedHover = (tool, hover);
        }
        var (matrix, eye) = Camera(request.Aspect);
        return new(PresentFerry(scene, city), matrix, eye);
    }

    private static Native3DVertex[] MakeTerrain()
    {
        var vertices = new List<Native3DVertex>();
        foreach (HexCell cell in CityMap.Cells)
        {
            int pattern = Math.Abs((cell.Q * 1777) ^ (cell.R * 3299));
            Vector2 center = cell.Center(CityMap.Radius);
            if (CityMap.IsWater(cell))
            {
                HexSurface(vertices, cell, 1, -.27f, new(.07f, .31f, .36f, 1));
                for (int stripe = 0; stripe < 4; stripe++)
                {
                    float x = (pattern % 13 - 6) * .11f + (stripe - 1.5f) * .72f;
                    float z = (stripe % 2 == 0 ? -.5f : .85f) + (pattern % 11) * .1f;
                    Vector2 start = center + new Vector2(x, z - .78f);
                    Strip(vertices, start, start + new Vector2(.52f, 1.45f), -.253f, .027f, new(.17f, .43f, .45f, 1));
                }
                continue;
            }
            HexSurface(vertices, cell, 1, 0, Grass[pattern % Grass.Length]);
            for (int side = 0; side < HexCell.SideCount; side++)
            {
                HexCell neighbor = cell.Neighbor(side);
                HexEdge edge = cell.Edge(side);
                if (CityMap.Contains(neighbor) && !CityMap.IsWater(neighbor))
                {
                    continue;
                }
                Vector2 a = edge.A.Position(CityMap.Radius);
                Vector2 b = edge.B.Position(CityMap.Radius);
                Vector3 normal = Vector3.Normalize(World((a + b) * .5f - center, 0));
                PrimitiveGeometry3D.AddQuad(vertices, World(a, -.72f), World(a, 0), World(b, 0), World(b, -.72f),
                    normal, new(.36f, .31f, .17f, 1));
                if (CityMap.Contains(neighbor))
                {
                    Vector2 insetA = Vector2.Lerp(a, center, .045f);
                    Vector2 insetB = Vector2.Lerp(b, center, .045f);
                    Strip(vertices, insetA, insetB, .012f, .08f, new(.60f, .53f, .31f, 1));
                }
            }
            Outline(vertices, cell, new(.12f, .24f, .12f, 1), .970f, .026f, .020f);
        }
        return [.. vertices];
    }

    private static void DrawBridge(List<Native3DVertex> vertices)
    {
        Vector2 a = CityMap.Bridge.A.Position(CityMap.Radius);
        Vector2 b = CityMap.Bridge.B.Position(CityMap.Radius);
        Vector2 start = Vector2.Lerp(a, b, .13f);
        Vector2 end = Vector2.Lerp(a, b, .87f);
        PrimitiveGeometry3D.AddBox(vertices, World((start + end) * .5f, .35f),
            new((end.X - start.X) * .5f, .09f, .52f), Paving);
        Vector2 width = new(0, .52f);
        PrimitiveGeometry3D.AddQuad(vertices, World(a - width, .13f), World(a + width, .13f),
            World(start + width, .44f), World(start - width, .44f), Vector3.UnitY, Paving);
        PrimitiveGeometry3D.AddQuad(vertices, World(end - width, .44f), World(end + width, .44f),
            World(b + width, .13f), World(b - width, .13f), Vector3.UnitY, Paving);
        foreach (float offset in new[] { -.48f, .48f })
        {
            Vector2 rail = new(0, offset);
            Strip(vertices, start + rail, end + rail, .77f, .06f, Stone);
            for (int post = 0; post <= 4; post++)
            {
                PrimitiveGeometry3D.AddBox(vertices, World(Vector2.Lerp(start, end, post / 4f) + rail, .59f),
                    new(.08f, .19f, .08f), Stone);
            }
        }
    }

    private static void DrawHall(List<Native3DVertex> vertices)
    {
        Vector2 origin = CityMap.TownHall.Center(CityMap.Radius);
        Box(vertices, origin, 0, .10f, 0, 1.65f, .10f, 1.25f, Paving);
        DrawBuilding(vertices, origin, 1.33f, 1.0f, 1.6f, Stone, new(.24f, .36f, .40f, 1));
        Box(vertices, origin, 0, 2.1f, 0, .36f, .47f, .36f, Stone);
        Roof(vertices, origin, .49f, .49f, 2.57f, .43f, new(.24f, .36f, .40f, 1));
    }

    private static void DrawBlock(List<Native3DVertex> vertices, HexCell cell, Block block, BlockReport status)
    {
        Vector2 origin = cell.Center(CityMap.Radius);
        Box(vertices, origin, 0, .05f, 0, 2.03f, .05f, 1.65f, block == Block.Park ? new(.35f, .53f, .23f, 1) : Paving);
        switch (block)
        {
            case Block.Dock:
            case Block.FishMarket:
            case Block.Boardwalk:
                DrawWaterfront(vertices, cell, block, status.Ferry);
                break;
            case Block.Homes when status.Residents > 8:
                DrawBuilding(vertices, origin + new Vector2(-.57f, -.2f), .76f, .93f, 2.85f, Stone, new(.53f, .23f, .13f, 1));
                DrawBuilding(vertices, origin + new Vector2(1.02f, .29f), .62f, .69f, 1.91f, new(.72f, .50f, .33f, 1), new(.57f, .27f, .16f, 1));
                break;
            case Block.Homes:
                DrawBuilding(vertices, origin + new Vector2(-.84f, -.28f), .62f, .68f, 1.01f, Stone, new(.55f, .22f, .13f, 1));
                DrawBuilding(vertices, origin + new Vector2(.73f, .33f), .66f, .70f, 1.15f, new(.76f, .55f, .35f, 1), new(.42f, .19f, .13f, 1));
                Tree(vertices, origin + new Vector2(1.5f, -.85f), .63f);
                break;
            case Block.Workshop:
                DrawBuilding(vertices, origin, 1.53f, .94f, 1.44f, new(.55f, .32f, .24f, 1), new(.30f, .32f, .34f, 1));
                Box(vertices, origin, 1.21f, 1.5f, -.57f, .21f, 1.4f, .21f, new(.41f, .29f, .24f, 1));
                Box(vertices, origin, 1.21f, 2.79f, -.57f, .28f, .12f, .28f, Asphalt);
                break;
            case Block.Market:
                DrawBuilding(vertices, origin + new Vector2(0, -.55f), 1.29f, .58f, .91f, Stone, new(.16f, .37f, .47f, 1));
                for (int stall = 0; stall < 3; stall++)
                {
                    float x = (stall - 1) * 1.06f;
                    Box(vertices, origin, x, .34f, .77f, .42f, .24f, .39f, Stone);
                    Box(vertices, origin, x, .92f, .77f, .46f, .075f, .48f, new(.76f, .47f, .11f, 1));
                }
                break;
            case Block.Park:
                Box(vertices, origin, 0, .11f, 0, 1.90f, .015f, .19f, Stone);
                Tree(vertices, origin + new Vector2(-1.16f, -.68f), .97f);
                Tree(vertices, origin + new Vector2(.11f, -.93f), 1.13f);
                Tree(vertices, origin + new Vector2(1.31f, .63f), .83f);
                Tree(vertices, origin + new Vector2(-.71f, .80f), .72f);
                break;
        }
    }

    private static void DrawBuilding(List<Native3DVertex> vertices, Vector2 origin, float x, float z, float height, Vector4 walls, Vector4 roof)
    {
        Box(vertices, origin, 0, height / 2 + .10f, 0, x, height / 2, z, walls);
        Roof(vertices, origin, x + .10f, z + .09f, height + .10f, .38f, roof);
        for (float y = .54f; y < height; y += .63f)
        {
            Box(vertices, origin, -x * .43f, y, z, .14f, .16f, .021f, Window);
            Box(vertices, origin, x * .43f, y, z, .14f, .16f, .021f, Window);
            Box(vertices, origin, -x, y, 0, .021f, .16f, .15f, Window);
        }
    }

    private static void Roof(List<Native3DVertex> vertices, Vector2 origin, float x, float z, float baseY, float rise, Vector4 color)
    {
        Vector3 ridgeLeft = World(origin + new Vector2(-x, 0), baseY + rise);
        Vector3 ridgeRight = World(origin + new Vector2(x, 0), baseY + rise);
        Vector3 nearLeft = World(origin + new Vector2(-x, z), baseY);
        Vector3 nearRight = World(origin + new Vector2(x, z), baseY);
        Vector3 farLeft = World(origin + new Vector2(-x, -z), baseY);
        Vector3 farRight = World(origin + new Vector2(x, -z), baseY);
        PrimitiveGeometry3D.AddQuad(vertices, ridgeLeft, nearLeft, nearRight, ridgeRight, Vector3.Normalize(new(0, z, rise)), color);
        PrimitiveGeometry3D.AddQuad(vertices, farLeft, ridgeLeft, ridgeRight, farRight, Vector3.Normalize(new(0, z, -rise)), color);
        PrimitiveGeometry3D.AddTriangle(vertices, nearLeft, ridgeLeft, farLeft, -Vector3.UnitX, color);
        PrimitiveGeometry3D.AddTriangle(vertices, farRight, ridgeRight, nearRight, Vector3.UnitX, color);
    }

    private static void Tree(List<Native3DVertex> vertices, Vector2 origin, float scale)
    {
        Box(vertices, origin, 0, .49f * scale, 0, .09f * scale, .45f * scale, .09f * scale, new(.30f, .21f, .13f, 1));
        Box(vertices, origin, 0, .97f * scale, 0, .39f * scale, .35f * scale, .39f * scale, new(.16f, .41f, .20f, 1));
        Box(vertices, origin, .06f * scale, 1.25f * scale, 0, .29f * scale, .23f * scale, .27f * scale, new(.23f, .48f, .24f, 1));
    }

    private static void HexSurface(List<Native3DVertex> vertices, HexCell cell, float scale, float height, Vector4 color)
    {
        Vector2 center = cell.Center(CityMap.Radius);
        for (int side = 0; side < HexCell.SideCount; side++)
        {
            Vector2 a = Vector2.Lerp(center, cell.Corner(side).Position(CityMap.Radius), scale);
            Vector2 b = Vector2.Lerp(center, cell.Corner((side + 1) % HexCell.SideCount).Position(CityMap.Radius), scale);
            PrimitiveGeometry3D.AddTriangle(vertices, World(center, height), World(b, height), World(a, height), Vector3.UnitY, color);
        }
    }

    private static void Outline(List<Native3DVertex> vertices, HexCell cell, Vector4 color, float scale, float width, float height = .17f)
    {
        Vector2 center = cell.Center(CityMap.Radius);
        for (int side = 0; side < HexCell.SideCount; side++)
        {
            HexEdge edge = cell.Edge(side);
            Vector2 a = Vector2.Lerp(center, edge.A.Position(CityMap.Radius), scale);
            Vector2 b = Vector2.Lerp(center, edge.B.Position(CityMap.Radius), scale);
            Strip(vertices, a, b, height, width, color);
        }
    }

    private static void AddStreet(List<Native3DVertex> vertices, HexEdge edge, float height, float width, Vector4 color) =>
        Strip(vertices, edge.A.Position(CityMap.Radius), edge.B.Position(CityMap.Radius), height, width, color);

    private static void Strip(List<Native3DVertex> vertices, Vector2 a, Vector2 b, float height, float width, Vector4 color)
    {
        Vector2 direction = Vector2.Normalize(b - a);
        Vector2 side = new(-direction.Y * width, direction.X * width);
        PrimitiveGeometry3D.AddQuad(vertices, World(a - side, height), World(a + side, height), World(b + side, height),
            World(b - side, height), Vector3.UnitY, color);
    }

    private static void Box(List<Native3DVertex> vertices, Vector2 origin, float x, float y, float z,
        float halfX, float halfY, float halfZ, Vector4 color) =>
        PrimitiveGeometry3D.AddBox(vertices, World(origin + new Vector2(x, z), y), new(halfX, halfY, halfZ), color);

    private static Vector3 World(Vector2 ground, float y) => new(ground.X, y, ground.Y);
}
