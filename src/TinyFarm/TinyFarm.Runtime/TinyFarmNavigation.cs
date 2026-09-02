using System.Diagnostics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;

namespace TinyFarm.Core;

public enum NavigationFailure
{
    None,
    StartBlocked,
    GoalBlocked,
    NoPath,
    BuildFailed
}

public sealed record NavigationPath(
    SceneId Scene,
    IReadOnlyList<ScenePosition> Waypoints,
    NavigationFailure Failure,
    double BuildMilliseconds,
    double QueryMilliseconds,
    string? FailureDetail = null)
{
    public bool Succeeded => Failure == NavigationFailure.None;
}

public interface INavigationPlanner
{
    NavigationPath FindPath(SceneDefinition scene, ScenePosition start, ScenePosition goal);
}

public sealed class DotRecastNavigationPlanner : INavigationPlanner
{
    private const int MaxPathPolygons = 256;
    private readonly Dictionary<string, CachedSceneNavigation> cache = new(StringComparer.Ordinal);

    public NavigationPath FindPath(SceneDefinition scene, ScenePosition start, ScenePosition goal)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!TinyFarmScenes.IsInBounds(scene, start) || TinyFarmScenes.IsBlocked(scene, start))
        {
            return Failure(scene.Id, NavigationFailure.StartBlocked);
        }
        if (!TinyFarmScenes.IsInBounds(scene, goal) || TinyFarmScenes.IsBlocked(scene, goal))
        {
            return Failure(scene.Id, NavigationFailure.GoalBlocked);
        }

        CachedSceneNavigation navigation;
        try
        {
            navigation = GetOrBuild(scene);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(scene.Id, NavigationFailure.BuildFailed, detail: exception.Message);
        }

        var stopwatch = Stopwatch.StartNew();
        var query = new DtNavMeshQuery(navigation.NavMesh);
        var filter = new DtQueryDefaultFilter();
        RcVec3f startPoint = ToRecast(start);
        RcVec3f goalPoint = ToRecast(goal);
        var extents = new RcVec3f(0.75f, 2f, 0.75f);
        query.FindNearestPoly(startPoint, extents, filter, out long startReference, out RcVec3f nearestStart, out _);
        query.FindNearestPoly(goalPoint, extents, filter, out long goalReference, out RcVec3f nearestGoal, out _);
        if (startReference == 0 || goalReference == 0)
        {
            return Failure(scene.Id, NavigationFailure.NoPath, navigation.BuildMilliseconds, stopwatch.Elapsed.TotalMilliseconds);
        }

        long[] polygonPath = new long[MaxPathPolygons];
        DtStatus status = query.FindPath(
            startReference,
            goalReference,
            nearestStart,
            nearestGoal,
            filter,
            polygonPath,
            out int polygonCount,
            polygonPath.Length);
        if (status.Failed() || polygonCount == 0 || polygonPath[polygonCount - 1] != goalReference)
        {
            return Failure(scene.Id, NavigationFailure.NoPath, navigation.BuildMilliseconds, stopwatch.Elapsed.TotalMilliseconds);
        }

        DtStraightPath[] straightPath = new DtStraightPath[MaxPathPolygons];
        status = query.FindStraightPath(
            nearestStart,
            nearestGoal,
            polygonPath,
            polygonCount,
            straightPath,
            out int waypointCount,
            straightPath.Length,
            0);
        stopwatch.Stop();
        if (status.Failed() || waypointCount == 0)
        {
            return Failure(scene.Id, NavigationFailure.NoPath, navigation.BuildMilliseconds, stopwatch.Elapsed.TotalMilliseconds);
        }

        ScenePosition[] waypoints = straightPath
            .Take(waypointCount)
            .Select(item => FromRecast(item.pos))
            .ToArray();
        waypoints[0] = start;
        waypoints[^1] = goal;
        return new NavigationPath(
            scene.Id,
            waypoints,
            NavigationFailure.None,
            navigation.BuildMilliseconds,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private CachedSceneNavigation GetOrBuild(SceneDefinition scene)
    {
        string key = SceneKey(scene);
        if (cache.TryGetValue(key, out CachedSceneNavigation? cached))
        {
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        (float[] vertices, int[] triangles) = BuildWalkableGeometry(scene);
        var geometry = new RcSampleInputGeomProvider(vertices, triangles);
        var config = new RcConfig(
            RcPartition.WATERSHED,
            0.25f,
            0.1f,
            45f,
            0.5f,
            0.2f,
            0.1f,
            1,
            2,
            12f,
            1.3f,
            6,
            6f,
            1f,
            true,
            true,
            true,
            new RcAreaModification(1, 0x07),
            true);
        var builderConfig = new RcBuilderConfig(config, geometry.GetMeshBoundsMin(), geometry.GetMeshBoundsMax());
        RcBuilderResult result = new RcBuilder().Build(geometry, builderConfig, false);
        RcPolyMesh polygonMesh = result.Mesh;
        if (polygonMesh.npolys == 0)
        {
            throw new InvalidOperationException(
                $"DotRecast produced no polygons for scene '{scene.Id}' from {triangles.Length / 3} triangles.");
        }
        for (int index = 0; index < polygonMesh.npolys; index++)
        {
            polygonMesh.flags[index] = 1;
        }

        var create = new DtNavMeshCreateParams
        {
            verts = polygonMesh.verts,
            vertCount = polygonMesh.nverts,
            polys = polygonMesh.polys,
            polyAreas = polygonMesh.areas,
            polyFlags = polygonMesh.flags,
            polyCount = polygonMesh.npolys,
            nvp = polygonMesh.nvp,
            walkableHeight = 0.5f,
            walkableRadius = 0.2f,
            walkableClimb = 0.1f,
            bmin = polygonMesh.bmin,
            bmax = polygonMesh.bmax,
            cs = 0.25f,
            ch = 0.1f,
            buildBvTree = true
        };
        RcPolyMeshDetail? detailMesh = result.MeshDetail;
        if (detailMesh is not null)
        {
            create.detailMeshes = detailMesh.meshes;
            create.detailVerts = detailMesh.verts;
            create.detailVertsCount = detailMesh.nverts;
            create.detailTris = detailMesh.tris;
            create.detailTriCount = detailMesh.ntris;
        }
        DtMeshData meshData = DtNavMeshBuilder.CreateNavMeshData(create)
            ?? throw new InvalidOperationException(
                $"DotRecast could not build scene '{scene.Id}' ({polygonMesh.nverts} vertices, {polygonMesh.npolys} polygons, nvp {polygonMesh.nvp}).");
        var navMesh = new DtNavMesh();
        if (navMesh.Init(meshData, polygonMesh.nvp, 0).Failed())
        {
            throw new InvalidOperationException($"DotRecast could not initialize scene '{scene.Id}'.");
        }

        stopwatch.Stop();
        cached = new CachedSceneNavigation(navMesh, stopwatch.Elapsed.TotalMilliseconds);
        cache.Add(key, cached);
        return cached;
    }

    private static string SceneKey(SceneDefinition scene)
    {
        string blockers = string.Join(
            ';',
            scene.Layout
                .Where(row => scene.Object(row.ObjectId).BlocksMovement)
                .OrderBy(row => row.ObjectId.Value, StringComparer.Ordinal)
                .Select(row => $"{row.ObjectId.Value}:{row.X},{row.Y},{row.Width},{row.Height}"));
        return $"{scene.Id.Value}|{scene.Width}|{scene.Height}|{blockers}";
    }

    private static (float[] Vertices, int[] Triangles) BuildWalkableGeometry(SceneDefinition scene)
    {
        var vertices = new List<float>();
        var triangles = new List<int>();
        AddQuad(
            vertices,
            triangles,
            (0, 0, 0),
            (scene.Width, 0, 0),
            (scene.Width, 0, scene.Height),
            (0, 0, scene.Height),
            upward: true);
        foreach (SceneLayoutRow row in scene.Layout.Where(item => scene.Object(item.ObjectId).BlocksMovement))
        {
            float left = row.X;
            float right = row.X + row.Width;
            float top = row.Y;
            float bottom = row.Y + row.Height;
            AddQuad(vertices, triangles, (left, 1, top), (right, 1, top), (right, 1, bottom), (left, 1, bottom), true);
            AddQuad(vertices, triangles, (left, 0, top), (right, 0, top), (right, 1, top), (left, 1, top), false);
            AddQuad(vertices, triangles, (right, 0, top), (right, 0, bottom), (right, 1, bottom), (right, 1, top), false);
            AddQuad(vertices, triangles, (right, 0, bottom), (left, 0, bottom), (left, 1, bottom), (right, 1, bottom), false);
            AddQuad(vertices, triangles, (left, 0, bottom), (left, 0, top), (left, 1, top), (left, 1, bottom), false);
        }
        return (vertices.ToArray(), triangles.ToArray());
    }

    private static void AddQuad(
        ICollection<float> vertices,
        ICollection<int> triangles,
        (float X, float Y, float Z) a,
        (float X, float Y, float Z) b,
        (float X, float Y, float Z) c,
        (float X, float Y, float Z) d,
        bool upward)
    {
        int first = vertices.Count / 3;
        foreach ((float x, float y, float z) in new[] { a, b, c, d })
        {
            vertices.Add(x);
            vertices.Add(y);
            vertices.Add(z);
        }
        if (upward)
        {
            foreach (int index in new[] { first, first + 2, first + 1, first, first + 3, first + 2 })
            {
                triangles.Add(index);
            }
        }
        else
        {
            foreach (int index in new[] { first, first + 1, first + 2, first, first + 2, first + 3 })
            {
                triangles.Add(index);
            }
        }
    }

    private static RcVec3f ToRecast(ScenePosition position)
    {
        return new RcVec3f(
            (float)position.XUnits / ScenePosition.UnitsPerTile,
            0,
            (float)position.YUnits / ScenePosition.UnitsPerTile);
    }

    private static ScenePosition FromRecast(RcVec3f position)
    {
        return new ScenePosition(
            (int)MathF.Round(position.X * ScenePosition.UnitsPerTile),
            (int)MathF.Round(position.Z * ScenePosition.UnitsPerTile));
    }

    private static NavigationPath Failure(
        SceneId scene,
        NavigationFailure failure,
        double buildMilliseconds = 0,
        double queryMilliseconds = 0,
        string? detail = null)
    {
        return new NavigationPath(scene, [], failure, buildMilliseconds, queryMilliseconds, detail);
    }

    private sealed record CachedSceneNavigation(DtNavMesh NavMesh, double BuildMilliseconds);
}
