using Aurelian.Spatial2D;
using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmSemanticSpatialM24Tests
{
    [Fact]
    public void SemanticSceneCompilesSiblingNavigationCollisionOcclusionAndInteractionProjections()
    {
        SemanticWorldScene scene = TinyFarmSemanticSpatialScene.Create();
        CompiledSemanticWorld compiled = TinyFarmSemanticSpatialScene.Compile();

        Assert.Equal("tinyfarm.riverside-farmhouse.m24", scene.Id);
        Assert.Equal(4, scene.Surfaces.Count);
        Assert.Single(scene.Paths);
        Assert.Equal(2, scene.Patches.Count);
        Assert.Equal(8, scene.Objects.Count);
        Assert.Equal(4, scene.ArtRecipes.Count);
        Assert.All(scene.ArtRecipes, item => Assert.Equal(64, item.ApprovedArtifactSha256.Length));
        Assert.Equal(7, compiled.Collision.Count);
        Assert.Equal(7, compiled.Interactions.Count);
        Assert.Equal(7, compiled.Occlusion.Count);
        Assert.Equal(32 * 20, compiled.Navigation.Count);
        Assert.Contains(compiled.Navigation, item => item.SurfaceId == "surface.river" && !item.Walkable);
        Assert.Contains(compiled.Navigation, item => item.SurfaceId == "surface.bridge-deck" && item.Walkable);
        Assert.Contains(compiled.Navigation, item => item.SurfaceId == "path.farmhouse-well-south-gate");
    }

    [Fact]
    public void TreeCanopyAndFarmhouseRoofDoNotBecomeCollisionByVisualIdentity()
    {
        SemanticWorldScene scene = TinyFarmSemanticSpatialScene.Create();
        WorldObject tree = scene.Objects.Single(item => item.Id == "tree.path");
        WorldObject farmhouse = scene.Objects.Single(item => item.Id == "farmhouse");

        var trunk = Assert.IsType<WorldCircleFootprint>(tree.CollisionFootprint);
        var canopy = Assert.IsType<WorldCircleFootprint>(tree.OcclusionFootprint);
        var walls = Assert.IsType<WorldBoxFootprint>(farmhouse.CollisionFootprint);
        var roof = Assert.IsType<WorldBoxFootprint>(farmhouse.OcclusionFootprint);

        Assert.Equal(0.34, trunk.Radius);
        Assert.True(canopy.Radius > trunk.Radius * 4);
        Assert.True(roof.Width > walls.Width);
        Assert.True(roof.Depth > walls.Depth);

        SpatialWorld2D collision = TinyFarmSemanticSpatialScene.BuildCollisionWorld();
        double units = ScenePosition.UnitsPerTile;
        SpatialHit2D? canopyOnly = collision.Sweep(
            new Circle2(new SpatialPoint2D(8.15 * units, 3.1 * units), 0),
            new SpatialVector2D(0.1 * units, 0));
        SpatialHit2D? trunkHit = collision.Sweep(
            new Circle2(new SpatialPoint2D(6.5 * units, 3.1 * units), 0),
            new SpatialVector2D(0.3 * units, 0));

        Assert.Null(canopyOnly);
        Assert.NotNull(trunkHit);
        Assert.Equal("tree.path", trunkHit.Value.SemanticOwnerId);
    }

    [Fact]
    public void FreshEditsChangeOnlyTheirOwnedProjection()
    {
        SemanticWorldScene original = TinyFarmSemanticSpatialScene.Create();
        CompiledSemanticWorld originalCompiled = SemanticWorldCompiler.Compile(original, 0.5);
        WorldPath path = Assert.Single(original.Paths);
        WorldPath curved = path with
        {
            Centerline = path.Centerline
                .Select((point, index) => index == 2 ? point with { X = point.X + 0.75 } : point)
                .ToArray()
        };
        SemanticWorldScene pathEdited = original with { Paths = [curved] };
        CompiledSemanticWorld pathCompiled = SemanticWorldCompiler.Compile(pathEdited, 0.5);
        Assert.NotEqual(
            originalCompiled.Navigation.Select(item => item.SurfaceId),
            pathCompiled.Navigation.Select(item => item.SurfaceId));
        Assert.Equal(originalCompiled.Collision, pathCompiled.Collision);

        WorldObject tree = original.Objects.Single(item => item.Id == "tree.path");
        var collision = Assert.IsType<WorldCircleFootprint>(tree.CollisionFootprint);
        var canopy = Assert.IsType<WorldCircleFootprint>(tree.OcclusionFootprint);
        WorldObject movedTree = tree with
        {
            Position = tree.Position with { X = tree.Position.X + 2 },
            OcclusionFootprint = canopy with { Radius = canopy.Radius + 0.5 }
        };
        Assert.Equal(collision, movedTree.CollisionFootprint);
        Assert.NotEqual(tree.Position, movedTree.Position);
        Assert.NotEqual(tree.OcclusionFootprint, movedTree.OcclusionFootprint);

        WorldPresentationRecipe farmhouseRecipe = original.PresentationRecipes
            .Single(item => item.Id == "farmhouse-image");
        SemanticWorldScene replacement = original with
        {
            PresentationRecipes = original.PresentationRecipes
                .Select(item => item.Id == farmhouseRecipe.Id
                    ? item with { AssetId = "m24/farmhouse-replacement.png" }
                    : item)
                .ToArray()
        };
        Assert.Equal(
            originalCompiled.Collision,
            SemanticWorldCompiler.Compile(replacement, 0.5).Collision);
        Assert.Equal(original.Objects, replacement.Objects);
    }

    [Fact]
    public void FixedCameraReprojectsWithoutChangingSemanticOrCompiledWorld()
    {
        SemanticWorldScene scene = TinyFarmSemanticSpatialScene.Create();
        CompiledSemanticWorld before = TinyFarmSemanticSpatialScene.Compile();
        var point = new SpatialPoint3D(7, 3.1, 3.2);

        SpatialPoint2D oblique = TinyFarmSemanticSpatialScene.DefaultCamera.Project(point);
        SpatialPoint2D topDown = TinyFarmSemanticSpatialScene.AlternateCamera.Project(point);

        Assert.NotEqual(oblique, topDown);
        Assert.Same(scene.Surfaces[0], scene.Surfaces[0]);
        CompiledSemanticWorld after = TinyFarmSemanticSpatialScene.Compile();
        Assert.Equal(before.Collision, after.Collision);
        Assert.Equal(
            before.Navigation.Select(NavigationKey),
            after.Navigation.Select(NavigationKey));
    }

    private static string NavigationKey(CompiledNavigationCell item)
    {
        return $"{item.X},{item.Y}:{item.Walkable}:{item.SurfaceId}:{string.Join(',', item.BlockingObjectIds)}";
    }

    [Fact]
    public void RiversideNavigationUsesSemanticCompiledCells()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        SceneDefinition scene = definitions.Scenes.Get(TinyFarmSceneIds.Riverside);
        var planner = new DotRecastNavigationPlanner();

        NavigationPath path = planner.FindPath(
            scene,
            ScenePosition.FromGrid(new GridPosition(2, 5)),
            ScenePosition.FromGrid(new GridPosition(8, 8)));
        NavigationPath water = planner.FindPath(
            scene,
            ScenePosition.FromGrid(new GridPosition(2, 5)),
            ScenePosition.FromGrid(new GridPosition(13, 8)));

        Assert.True(path.Succeeded, path.FailureDetail);
        Assert.Equal(NavigationFailure.GoalBlocked, water.Failure);
    }
}
