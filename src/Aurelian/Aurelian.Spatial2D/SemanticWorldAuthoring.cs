namespace Aurelian.Spatial2D;

public readonly record struct SpatialPoint3D(double X, double Y, double Z)
{
    public SpatialPoint2D Ground => new(X, Y);

    public void Validate()
    {
        SpatialMath2D.RequireFinite(X, nameof(X));
        SpatialMath2D.RequireFinite(Y, nameof(Y));
        SpatialMath2D.RequireFinite(Z, nameof(Z));
    }
}

public sealed record WorldPolygon(IReadOnlyList<SpatialPoint2D> Points)
{
    public void Validate()
    {
        if (Points.Count < 3)
        {
            throw new ArgumentException("A world polygon requires at least three points.", nameof(Points));
        }

        foreach (SpatialPoint2D point in Points)
        {
            point.Validate();
        }
    }

    public bool Contains(SpatialPoint2D point)
    {
        Validate();
        bool inside = false;
        for (int current = 0, previous = Points.Count - 1; current < Points.Count; previous = current++)
        {
            SpatialPoint2D a = Points[current];
            SpatialPoint2D b = Points[previous];
            bool crosses = (a.Y > point.Y) != (b.Y > point.Y)
                && point.X < ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X;
            if (crosses)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}

public enum WorldTraversalPolicy
{
    Walkable,
    Restricted,
    Bridge
}

public enum SurfacePresentationKind
{
    Slab,
    Overlay,
    Sprite,
    Profile,
    Extrusion
}

public sealed record WorldPresentationRecipe(
    string Id,
    SurfacePresentationKind Kind,
    string AssetId,
    string StyleId,
    bool Cacheable = true);

public sealed record ArtRecipe(
    string Id,
    string SemanticSubject,
    string StyleId,
    int TargetWidth,
    int TargetHeight,
    string IntendedProjection,
    string PromptSpec,
    string? SeedOrRevision,
    string Source,
    string ApprovedArtifactSha256,
    string? SelectionOrEditNotes,
    string? LicenseMetadata);

public sealed record WorldSurface(
    string Id,
    WorldPolygon Boundary,
    double Elevation,
    string SemanticMaterial,
    WorldTraversalPolicy Traversal,
    string PresentationRecipeId);

public sealed record WorldPath(
    string Id,
    IReadOnlyList<SpatialPoint3D> Centerline,
    double Width,
    string SemanticMaterial,
    WorldTraversalPolicy Traversal,
    string PresentationRecipeId);

public sealed record WorldPatch(
    string Id,
    WorldPolygon Boundary,
    double Elevation,
    string SemanticMaterial,
    string PresentationRecipeId);

public abstract record WorldFootprint;

public sealed record WorldCircleFootprint(double Radius) : WorldFootprint;

public sealed record WorldBoxFootprint(double Width, double Depth) : WorldFootprint;

public sealed record WorldSegmentFootprint(
    SpatialPoint2D Start,
    SpatialPoint2D End,
    double Thickness) : WorldFootprint;

public sealed record WorldObject(
    string Id,
    SpatialPoint3D Position,
    WorldFootprint? CollisionFootprint,
    WorldFootprint? OcclusionFootprint,
    double Height,
    string? InteractionId,
    string PresentationRecipeId,
    WorldFootprint? InteractionFootprint = null);

public sealed record SemanticWorldScene(
    string Id,
    double Width,
    double Depth,
    IReadOnlyList<WorldSurface> Surfaces,
    IReadOnlyList<WorldPath> Paths,
    IReadOnlyList<WorldPatch> Patches,
    IReadOnlyList<WorldObject> Objects,
    IReadOnlyList<WorldPresentationRecipe> PresentationRecipes,
    IReadOnlyList<ArtRecipe> ArtRecipes);

public sealed record CompiledNavigationCell(
    int X,
    int Y,
    bool Walkable,
    string? SurfaceId,
    IReadOnlyList<string> BlockingObjectIds);

public sealed record CompiledSemanticWorld(
    IReadOnlyList<SpatialCollider2D> Collision,
    IReadOnlyList<SpatialTrigger2D> Interactions,
    IReadOnlyList<SpatialShape2D> Occlusion,
    IReadOnlyList<CompiledNavigationCell> Navigation);

public static class SemanticWorldCompiler
{
    public static CompiledSemanticWorld Compile(SemanticWorldScene scene, double navigationCellSize)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (navigationCellSize <= 0 || !double.IsFinite(navigationCellSize))
        {
            throw new ArgumentOutOfRangeException(nameof(navigationCellSize));
        }

        SpatialCollider2D[] collision = scene.Objects
            .Where(item => item.CollisionFootprint is not null)
            .Select(ToCollider)
            .OrderBy(item => item.Id)
            .ToArray();
        SpatialTrigger2D[] interactions = scene.Objects
            .Where(item => item.InteractionId is not null)
            .Select(item => new SpatialTrigger2D(
                new SpatialTriggerId("interaction:" + item.InteractionId),
                ToShape(item.Position, item.InteractionFootprint ?? item.CollisionFootprint
                    ?? throw new InvalidOperationException($"Interactive object '{item.Id}' has no interaction shape.")),
                SemanticOwnerId: item.Id))
            .OrderBy(item => item.Id)
            .ToArray();
        SpatialShape2D[] occlusion = scene.Objects
            .Where(item => item.OcclusionFootprint is not null)
            .Select(item => ToShape(item.Position, item.OcclusionFootprint!))
            .ToArray();

        int columns = (int)Math.Ceiling(scene.Width / navigationCellSize);
        int rows = (int)Math.Ceiling(scene.Depth / navigationCellSize);
        var navigation = new List<CompiledNavigationCell>(columns * rows);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var point = new SpatialPoint2D(
                    (x + 0.5) * navigationCellSize,
                    (y + 0.5) * navigationCellSize);
                WorldSurface? surface = scene.Surfaces
                    .Where(item => item.Boundary.Contains(point))
                    .OrderByDescending(item => item.Elevation)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                WorldPath? path = scene.Paths
                    .Where(item => Contains(item, point))
                    .OrderBy(item => item.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                string[] blockers = collision
                    .Where(item => SpatialWorld2D.Overlaps(item.Shape, new Circle2(point, 0)))
                    .Select(item => item.SemanticOwnerId ?? item.Id.Value)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray();
                WorldTraversalPolicy? traversal = path?.Traversal ?? surface?.Traversal;
                bool walkable = traversal is WorldTraversalPolicy.Walkable or WorldTraversalPolicy.Bridge
                    && blockers.Length == 0;
                navigation.Add(new CompiledNavigationCell(x, y, walkable, path?.Id ?? surface?.Id, blockers));
            }
        }

        return new CompiledSemanticWorld(collision, interactions, occlusion, navigation);
    }

    private static bool Contains(WorldPath path, SpatialPoint2D point)
    {
        for (int index = 0; index < path.Centerline.Count - 1; index++)
        {
            SpatialPoint2D start = path.Centerline[index].Ground;
            SpatialPoint2D end = path.Centerline[index + 1].Ground;
            SpatialVector2D segment = end - start;
            double lengthSquared = segment.LengthSquared;
            double amount = lengthSquared <= SpatialMath2D.Epsilon
                ? 0
                : Math.Clamp(SpatialVector2D.Dot(point - start, segment) / lengthSquared, 0, 1);
            SpatialPoint2D nearest = start + (segment * amount);
            if ((point - nearest).LengthSquared <= path.Width * path.Width / 4)
            {
                return true;
            }
        }
        return false;
    }

    private static SpatialCollider2D ToCollider(WorldObject item)
    {
        return new SpatialCollider2D(
            new SpatialColliderId("world-object:" + item.Id),
            ToShape(item.Position, item.CollisionFootprint!),
            SemanticOwnerId: item.Id);
    }

    public static SpatialShape2D ToShape(SpatialPoint3D position, WorldFootprint footprint)
    {
        return footprint switch
        {
            WorldCircleFootprint circle => new Circle2(position.Ground, circle.Radius),
            WorldBoxFootprint box => new Aabb2(
                position.Ground,
                new SpatialVector2D(box.Width / 2, box.Depth / 2)),
            WorldSegmentFootprint segment => SegmentBox(position, segment),
            _ => throw new NotSupportedException($"Unsupported world footprint '{footprint.GetType().Name}'.")
        };
    }

    private static Aabb2 SegmentBox(SpatialPoint3D position, WorldSegmentFootprint segment)
    {
        double minimumX = Math.Min(segment.Start.X, segment.End.X) + position.X;
        double maximumX = Math.Max(segment.Start.X, segment.End.X) + position.X;
        double minimumY = Math.Min(segment.Start.Y, segment.End.Y) + position.Y;
        double maximumY = Math.Max(segment.Start.Y, segment.End.Y) + position.Y;
        return new Aabb2(
            new SpatialPoint2D((minimumX + maximumX) / 2, (minimumY + maximumY) / 2),
            new SpatialVector2D(
                ((maximumX - minimumX) + segment.Thickness) / 2,
                ((maximumY - minimumY) + segment.Thickness) / 2));
    }
}

public enum FixedCameraProjectionKind
{
    TopDown,
    Oblique
}

public sealed record FixedWorldCamera(
    string Id,
    FixedCameraProjectionKind Kind,
    double PixelsPerMeter,
    double ElevationPixelsPerMeter,
    double ShearX)
{
    public SpatialPoint2D Project(SpatialPoint3D point)
    {
        point.Validate();
        return Kind switch
        {
            FixedCameraProjectionKind.TopDown => new SpatialPoint2D(
                point.X * PixelsPerMeter,
                point.Y * PixelsPerMeter - point.Z * ElevationPixelsPerMeter),
            FixedCameraProjectionKind.Oblique => new SpatialPoint2D(
                (point.X + point.Y * ShearX) * PixelsPerMeter,
                point.Y * PixelsPerMeter - point.Z * ElevationPixelsPerMeter),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
    }
}
