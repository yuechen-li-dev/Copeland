using Aurelian.Spatial2D;

namespace TinyFarm.Core;

public static class TinyFarmSemanticSpatialScene
{
    public const string SceneId = "tinyfarm.riverside-farmhouse.m24";
    public const string StyleId = "tinyfarm.painterly-meadow.v1";
    public const string FarmhousePresentationAsset = "m25/farmhouse-three-quarter.png";
    public static readonly ArtRecipe FarmhousePresentationArt = new(
        "farmhouse-m25",
        "same farmhouse, approved north-up three-quarter presentation",
        StyleId,
        1408,
        1117,
        "nominal 45 degree elevation, north-up, zero yaw, parallel projection",
        "Paint over farmhouse-three-quarter-guide.svg; M24 house is material reference only; preserve roof, shallow south facade and centred entry.",
        SeedOrRevision: null,
        Source: "OpenAI built-in image generation",
        ApprovedArtifactSha256: "735e2ef7f093edc7cdbf93b86237cc69905baf6431e46a23dd9c74340648a6f5",
        SelectionOrEditNotes: "M25 user-approved projection convention; generated paint-over followed by generated RGBA background extraction.",
        LicenseMetadata: null);

    public static readonly FixedWorldCamera DefaultCamera = new(
        "tinyfarm.fixed-oblique.v1",
        FixedCameraProjectionKind.Oblique,
        PixelsPerMeter: 48,
        ElevationPixelsPerMeter: 18,
        ShearX: 0.08);

    public static readonly FixedWorldCamera AlternateCamera = new(
        "tinyfarm.fixed-top-down-proof.v1",
        FixedCameraProjectionKind.TopDown,
        PixelsPerMeter: 48,
        ElevationPixelsPerMeter: 14,
        ShearX: 0);

    public static SemanticWorldScene Create()
    {
        WorldPresentationRecipe[] recipes =
        [
            new("meadow-slab", SurfacePresentationKind.Slab, "m24/meadow-slab.png", StyleId),
            new("river-bank-overlay", SurfacePresentationKind.Overlay, "native-bank-paint", StyleId),
            new("dirt-path-overlay", SurfacePresentationKind.Overlay, "native-path-ribbon", StyleId),
            new("flower-overlay", SurfacePresentationKind.Overlay, "native-flower-patch", StyleId),
            new("water-live", SurfacePresentationKind.Overlay, "tinyfarm-field2d-live", StyleId, false),
            new("tree-image", SurfacePresentationKind.Sprite, "m24/tree.png", StyleId),
            new("farmhouse-image", SurfacePresentationKind.Sprite, FarmhousePresentationAsset, StyleId),
            new("well-profile", SurfacePresentationKind.Profile, "m11/well", StyleId),
            new("fence-profile", SurfacePresentationKind.Profile, "m11/fence", StyleId),
            new("crop-live", SurfacePresentationKind.Profile, "tinyfarm-live-crops", StyleId, false),
        ];

        ArtRecipe[] artRecipes =
        [
            FarmhousePresentationArt,
            ApprovedArt(
                "meadow-slab",
                "coherent painterly meadow ground slab",
                1254,
                1254,
                "storybook gouache meadow, top-down, no path/buildings/objects; warm upper-left light; no grid repetition",
                "c0222629a63f12067bbe4c78ce7cb864fa263702d875e6cbd4a7b84c6ce987ee"),
            ApprovedArt(
                "farmhouse",
                "separately composed farmhouse realization",
                1374,
                1145,
                "cream plaster and terracotta farmhouse, orthographic three-quarter top-down, transparent background",
                "f2334a458c82f5d39c69d8a9220fe7171595f0daa89d30dc5d9dee83fa9f8529"),
            ApprovedArt(
                "tree",
                "separately composed tree realization",
                1218,
                1292,
                "mature deciduous painterly tree, broad canopy and visible trunk, transparent background",
                "abf86042eb4ec4497cf7a300488cf7cf5a5c15de73aeddbc2dad2847675c4d6f"),
            ApprovedArt(
                "farmhouse-replacement",
                "alternate approved farmhouse realization",
                1536,
                1024,
                "same semantic farmhouse scale/view/door placement, alternate roof and detailing, transparent background",
                "06be80c44f0962e073487ba3e18dc537d0bd9ad263e6a89e9a268916fa4aaec8"),
        ];

        WorldSurface meadow = new(
            "surface.meadow",
            Rectangle(0, 0, 9.5, 10),
            Elevation: 0,
            SemanticMaterial: "Meadow",
            Traversal: WorldTraversalPolicy.Walkable,
            PresentationRecipeId: "meadow-slab");
        WorldSurface water = new(
            "surface.river",
            Rectangle(9.5, 0, 6.5, 10),
            Elevation: -0.18,
            SemanticMaterial: "WaterSurface",
            Traversal: WorldTraversalPolicy.Restricted,
            PresentationRecipeId: "water-live");
        WorldSurface bridge = new(
            "surface.bridge-deck",
            Rectangle(9.2, 4.25, 2.4, 1.5),
            Elevation: 0.35,
            SemanticMaterial: "TimberDeck",
            Traversal: WorldTraversalPolicy.Bridge,
            PresentationRecipeId: "fence-profile");
        WorldSurface cropPlot = new(
            "surface.crop-plot",
            Rectangle(2.2, 6.4, 3.4, 2.2),
            Elevation: 0.02,
            SemanticMaterial: "CultivatedSoil",
            Traversal: WorldTraversalPolicy.Walkable,
            PresentationRecipeId: "crop-live");

        WorldPath path = new(
            "path.farmhouse-well-south-gate",
            [
                new SpatialPoint3D(3.5, 4.2, 0.03),
                new SpatialPoint3D(5.1, 4.9, 0.03),
                new SpatialPoint3D(6.1, 5.7, 0.03),
                new SpatialPoint3D(7.6, 6.0, 0.03),
                new SpatialPoint3D(8.6, 7.6, 0.03),
                new SpatialPoint3D(8.2, 9.7, 0.03),
            ],
            Width: 1.15,
            SemanticMaterial: "PackedDirt",
            Traversal: WorldTraversalPolicy.Walkable,
            PresentationRecipeId: "dirt-path-overlay");

        WorldPatch[] patches =
        [
            new("patch.flower-meadow", Rectangle(5.4, 2.2, 2.2, 1.35), 0.04, "FlowerPatch", "flower-overlay"),
            new("patch.riverbank-mud", Rectangle(8.7, 6.2, 1.1, 2.0), 0.04, "MudPatch", "river-bank-overlay"),
        ];

        WorldObject[] objects =
        [
            new(
                "farmhouse",
                new SpatialPoint3D(3.2, 2.15, 0),
                new WorldBoxFootprint(3.6, 2.3),
                new WorldBoxFootprint(4.6, 3.3),
                Height: 3.4,
                InteractionId: "farmhouse.door",
                PresentationRecipeId: "farmhouse-image",
                InteractionFootprint: new WorldBoxFootprint(0.9, 0.55)),
            Tree("tree.west", 1.3, 5.25, 2.8),
            Tree("tree.path", 7.0, 3.1, 3.1),
            Tree("tree.bank", 8.35, 1.25, 2.9),
            Tree("tree.south", 6.4, 8.75, 2.65),
            new(
                "well",
                new SpatialPoint3D(6.25, 5.15, 0),
                new WorldCircleFootprint(0.48),
                new WorldCircleFootprint(0.68),
                Height: 1.15,
                InteractionId: "well.draw-water",
                PresentationRecipeId: "well-profile",
                InteractionFootprint: new WorldCircleFootprint(0.85)),
            new(
                "fence.north",
                new SpatialPoint3D(0, 0, 0),
                new WorldSegmentFootprint(new SpatialPoint2D(0.5, 0.65), new SpatialPoint2D(5.7, 0.65), 0.18),
                new WorldSegmentFootprint(new SpatialPoint2D(0.5, 0.65), new SpatialPoint2D(5.7, 0.65), 0.28),
                Height: 0.9,
                InteractionId: null,
                PresentationRecipeId: "fence-profile"),
            new(
                "riverside-exit",
                new SpatialPoint3D(1.5, 5.5, 0),
                CollisionFootprint: null,
                OcclusionFootprint: null,
                Height: 0,
                InteractionId: "route.riverside-overworld",
                PresentationRecipeId: "fence-profile",
                InteractionFootprint: new WorldBoxFootprint(1, 1)),
        ];

        return new SemanticWorldScene(
            SceneId,
            Width: 16,
            Depth: 10,
            [meadow, water, bridge, cropPlot],
            [path],
            patches,
            objects,
            recipes,
            artRecipes);
    }

    public static CompiledSemanticWorld Compile()
    {
        return SemanticWorldCompiler.Compile(Create(), navigationCellSize: 0.5);
    }

    public static SpatialWorld2D BuildCollisionWorld()
    {
        SpatialCollider2D[] colliders = Compile().Collision
            .Select(item => item with
            {
                Shape = ScaleShape(item.Shape, ScenePosition.UnitsPerTile)
            })
            .ToArray();
        return new SpatialWorld2D(colliders);
    }

    private static WorldObject Tree(string id, double x, double y, double canopyRadius)
    {
        return new WorldObject(
            id,
            new SpatialPoint3D(x, y, 0),
            new WorldCircleFootprint(0.34),
            new WorldCircleFootprint(canopyRadius / 2),
            Height: 3.2,
            InteractionId: "tree.chop:" + id,
            PresentationRecipeId: "tree-image",
            InteractionFootprint: new WorldCircleFootprint(0.85));
    }

    private static ArtRecipe ApprovedArt(
        string id,
        string semanticSubject,
        int width,
        int height,
        string promptSpec,
        string approvedHash)
    {
        return new ArtRecipe(
            id,
            semanticSubject,
            StyleId,
            width,
            height,
            "fixed orthographic three-quarter top-down",
            promptSpec,
            SeedOrRevision: null,
            Source: "OpenAI built-in image generation",
            ApprovedArtifactSha256: approvedHash,
            SelectionOrEditNotes: "Selected for M24; no post-generation pixel edits.",
            LicenseMetadata: null);
    }

    private static WorldPolygon Rectangle(double x, double y, double width, double height)
    {
        return new WorldPolygon(
        [
            new SpatialPoint2D(x, y),
            new SpatialPoint2D(x + width, y),
            new SpatialPoint2D(x + width, y + height),
            new SpatialPoint2D(x, y + height),
        ]);
    }

    private static SpatialShape2D ScaleShape(SpatialShape2D shape, double scale)
    {
        return shape switch
        {
            Circle2 circle => new Circle2(
                new SpatialPoint2D(circle.Center.X * scale, circle.Center.Y * scale),
                circle.Radius * scale),
            Aabb2 box => new Aabb2(
                new SpatialPoint2D(box.Center.X * scale, box.Center.Y * scale),
                new SpatialVector2D(box.HalfExtents.X * scale, box.HalfExtents.Y * scale)),
            _ => throw new NotSupportedException($"Unsupported compiled shape '{shape.GetType().Name}'.")
        };
    }
}
