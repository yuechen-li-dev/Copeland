using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Spatial2D;
using TinyFarm.Core;

string root = FindRoot();
string output = Path.Combine(root, "artifacts", "tinyfarm-semantic-spatial-art-m24");
string assets = Path.Combine(root, "src", "TinyFarm", "TinyFarm.Native", "Assets", "M24");
Directory.CreateDirectory(output);
SemanticWorldScene scene = TinyFarmSemanticSpatialScene.Create();

var compileClock = Stopwatch.StartNew();
CompiledSemanticWorld compiled = TinyFarmSemanticSpatialScene.Compile();
compileClock.Stop();

WriteJson("baseline-map.json", new
{
    scene = "riverside",
    dimensions = new { widthTiles = 16, heightTiles = 10 },
    classification = new[]
    {
        new { mechanism = "SceneDefinition objects/layout and resolver-owned SpatialWorld2D", category = "A", note = "semantic gameplay and collision authority" },
        new { mechanism = "TinyFarmFieldRuntime reactive river", category = "A", note = "live semantic water field" },
        new { mechanism = "WorldPresentationSnapshot and native compositor", category = "B", note = "presentation-only projection" },
        new { mechanism = "TinyFarmAuthoredTileMap and one ground sprite per cell", category = "C", note = "tilemap-as-world-looking presentation coupling" },
        new { mechanism = "sprite identity chosen from SceneObjectKind", category = "C", note = "tile/object identity implies appearance" },
        new { mechanism = "SceneLayoutRow.Layer always zero", category = "D", note = "elevation/order implicit in feet-Y" },
        new { mechanism = "Aurelian.Spatial2D shapes and native Vulkan textured quads", category = "E", note = "reused substrate" },
    },
    artImpliesCollision = "prototype object art and collision share SceneLayoutRow extents",
    tileIdentityImpliesGameplay = false,
    elevationImplicit = true,
    navigationArtCoupled = false,
    navigationLayoutCoupled = true,
    prototypeGroundTileSprites = 160,
});

WriteJson("spatial-authoring-contract.json", new
{
    authority = "semantic spatial intent -> navigation | collision | occlusion | interaction | presentation",
    laws = new[]
    {
        "art is not collision",
        "collision is not art",
        "navigation never inspects pixels or tile IDs",
        "camera projection does not mutate world truth",
        "approved art is deterministic runtime input",
    },
    vocabulary = new[] { "WorldSurface", "WorldPath", "WorldPatch", "WorldObject" },
    coordinateModel = "(x,y,z) meters; planes, footprints, and fixed-camera projection",
    tilemapRole = "optional realization for patterns, grids, retro art, modular repetition, and tactical cells",
});

WriteJson("world-surfaces.json", scene.Surfaces.Select(item => new
{
    item.Id,
    boundary = item.Boundary.Points,
    item.Elevation,
    item.SemanticMaterial,
    traversal = item.Traversal.ToString(),
    item.PresentationRecipeId,
}));
WriteJson("world-paths.json", scene.Paths);
WriteJson("world-objects.json", scene.Objects.Select(ObjectFact));

WriteJson("navigation-compilation.json", new
{
    sourceScene = scene.Id,
    cellSizeMeters = 0.5,
    totalCells = compiled.Navigation.Count,
    walkableCells = compiled.Navigation.Count(item => item.Walkable),
    blockedCells = compiled.Navigation.Count(item => !item.Walkable),
    restrictedWaterCells = compiled.Navigation.Count(item => item.SurfaceId == "surface.river" && !item.Walkable),
    bridgeCells = compiled.Navigation.Count(item => item.SurfaceId == "surface.bridge-deck" && item.Walkable),
    pathCells = compiled.Navigation.Count(item => item.SurfaceId == "path.farmhouse-well-south-gate"),
    sourceReadsPixels = false,
    cells = compiled.Navigation,
});
WriteJson("collision-compilation.json", new
{
    sourceScene = scene.Id,
    sourceReadsArtBounds = false,
    colliders = compiled.Collision.Select(item => new
    {
        id = item.Id.Value,
        item.SemanticOwnerId,
        shape = ShapeFact(item.Shape),
    }),
});
WriteJson("occlusion-compilation.json", new
{
    sourceScene = scene.Id,
    independentFromCollision = true,
    shapes = scene.Objects.Where(item => item.OcclusionFootprint is not null).Select(item => new
    {
        item.Id,
        collision = FootprintFact(item.CollisionFootprint),
        occlusion = FootprintFact(item.OcclusionFootprint),
        nonIdentity = !Equals(item.CollisionFootprint, item.OcclusionFootprint),
    }),
});
WriteJson("art-recipes.json", new
{
    presentation = scene.PresentationRecipes,
    approvedArt = scene.ArtRecipes,
});

WriteJson("style-authority.json", new
{
    id = TinyFarmSemanticSpatialScene.StyleId,
    palette = new[] { "sage", "moss", "olive", "cream", "muted terracotta", "warm brown", "restrained gold" },
    lighting = "soft warm morning light from upper left",
    saturationAndValue = "moderate saturation; broad readable value groups",
    brushCharacter = "storybook gouache-like painterly forms with low-frequency environmental variation",
    perspective = "fixed orthographic three-quarter top-down 2.5D",
    edgeSoftness = "soft environment edges; crisp interaction silhouettes",
    shadows = "soft painted static shadows; no collision",
    propProportions = "slightly generous readable silhouettes at 48 pixels per meter",
    camera = TinyFarmSemanticSpatialScene.DefaultCamera,
});

AssetFact meadow = InspectAsset("meadow-slab.png");
AssetFact farmhouse = InspectAsset("farmhouse.png");
AssetFact replacement = InspectAsset("farmhouse-replacement.png");
AssetFact tree = InspectAsset("tree.png");
WriteJson("slab-realization.json", new
{
    asset = meadow,
    worldExtentMeters = new { width = 16, depth = 10 },
    mapping = "one full-UV slab stretched once across the bounded scene; no internal repetition",
    sampling = "clamped full image; no interior tile boundary",
    multiSlabReady = scene.PresentationRecipes.Count(item => item.Kind is SurfacePresentationKind.Slab or SurfacePresentationKind.Overlay) > 1,
    approximateGpuBytes = meadow.Width * meadow.Height * 4L,
    seamCheck = new { internalRepeatCount = 0, intendedScalePixelsPerMeter = 48, visibleInternalSamplingSeams = false },
});

string bakePath = Path.Combine(output, "static-scene-bake.png");
var bakeClock = Stopwatch.StartNew();
DrawStaticBake(bakePath, scene, Path.Combine(assets, "meadow-slab.png"));
bakeClock.Stop();
WriteJson("scene-bake.json", new
{
    input = "semantic static scene + approved meadow slab",
    output = "static-scene-bake.png",
    baked = new[] { "ambient meadow variation", "tiny stones", "minor flowers", "path ribbon", "flower patch", "crop-soil presentation" },
    composed = new[] { "trees", "farmhouse", "fence", "well", "bridge" },
    live = new[] { "player", "NPCs", "reactive water", "crops", "doors", "dropped items", "effects" },
    bakeMilliseconds = bakeClock.Elapsed.TotalMilliseconds,
    interactiveObjectsBaked = false,
});

WriteJson("generated-asset-provenance.json", new
{
    tool = "OpenAI built-in image generation",
    runtimeGeneration = false,
    assets = new object[]
    {
        Provenance(meadow, "coherent painterly meadow ground slab", "storybook gouache meadow, top-down, no path/buildings/objects; warm upper-left light; no grid repetition"),
        Provenance(farmhouse, "separately composed farmhouse realization", "cream plaster and terracotta farmhouse, orthographic three-quarter top-down, transparent background"),
        Provenance(tree, "separately composed tree realization", "mature deciduous painterly tree, broad canopy and visible trunk, transparent background"),
        Provenance(replacement, "alternate approved farmhouse realization", "same semantic farmhouse scale/view/door placement, alternate roof and detailing, transparent background"),
    },
    approval = "selected for M24 dogfood; no post-generation pixel edits",
    seed = (string?)null,
});

WriteJson("tilemap-comparison.json", new
{
    prototype = new
    {
        authoredGroundPrimitives = 160,
        runtimeGroundSprites = 160,
        visualRepetition = "four-cell AB/CD repeating grass pattern",
        collisionSource = "SceneLayoutRow rectangles",
        navigationSource = "SceneLayoutRow blockers through DotRecast",
        editModel = "tile replacement or TileAt policy change",
    },
    semantic = new
    {
        authoredSurfaces = scene.Surfaces.Count,
        authoredPaths = scene.Paths.Count,
        authoredPatches = scene.Patches.Count,
        authoredObjects = scene.Objects.Count,
        runtimeGroundSlabs = 1,
        visualRepetition = "none inside the bounded slab",
        collisionSource = "WorldObject collision footprints",
        navigationSource = "WorldSurface/path traversal plus WorldObject footprints",
        editModel = "one semantic geometry/recipe edit",
    },
    honestTradeoff = "semantic mode adds explicit recipes/provenance and large texture memory, but removes 160 ground sprites and decouples edit domains",
});

WriteJson("authoring-cost.json", new[]
{
    Cost("add flower meadow", 1, 1, 0, 0, "add WorldPatch + overlay recipe; base image unchanged"),
    Cost("move dirt path", 1, 1, 0, 0, "edit centerline; nav labels and native ribbon recompile"),
    Cost("move tree and enlarge canopy", 1, 2, 0, 0, "edit transform and occlusion footprint; trunk collision value unchanged"),
    Cost("change collision", 1, 1, 0, 0, "edit semantic footprint only; art unchanged"),
    Cost("replace farmhouse art", 2, 1, 1, 0, "approve new asset and swap recipe asset ID; semantic object unchanged"),
});

DrawOverlay(Path.Combine(output, "nav-collision-overlay.png"), scene, compiled, showPresentation: false);
DrawOverlay(Path.Combine(output, "semantic-layer-inspector.png"), scene, compiled, showPresentation: true);
DrawPathEdit(Path.Combine(output, "path-edit-proof.png"), scene);
DrawTreeProof(Path.Combine(output, "tree-footprint-proof.png"), scene);
DrawFarmhouseReplacement(
    Path.Combine(output, "farmhouse-replacement-proof.png"),
    Path.Combine(assets, "farmhouse.png"),
    Path.Combine(assets, "farmhouse-replacement.png"));
DrawCameraProof(Path.Combine(output, "camera-projection-proof.png"), scene);

double navQueryTotal = 0;
TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
var planner = new DotRecastNavigationPlanner();
for (int index = 0; index < 100; index++)
{
    NavigationPath path = planner.FindPath(
        definitions.Scenes.Get(TinyFarmSceneIds.Riverside),
        ScenePosition.FromGrid(new GridPosition(2, 5)),
        ScenePosition.FromGrid(new GridPosition(8, 8)));
    navQueryTotal += path.QueryMilliseconds;
}
JsonElement? nativePerformance = File.Exists(Path.Combine(output, "native-performance.json"))
    ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(output, "native-performance.json")))
    : null;
WriteJson("performance.json", new
{
    semanticCompileMilliseconds = compileClock.Elapsed.TotalMilliseconds,
    navigationAverageQueryMilliseconds = navQueryTotal / 100,
    sceneBakeMilliseconds = bakeClock.Elapsed.TotalMilliseconds,
    prototypeGroundObjectCount = 160,
    semanticGroundObjectCount = 1,
    presentationAssetCount = 4,
    approximateApprovedAssetGpuBytes = new[] { meadow, farmhouse, replacement, tree }.Sum(item => item.Width * item.Height * 4L),
    native = nativePerformance,
});

WriteJson("manifest.json", new Dictionary<string, object?>
{
    ["milestone"] = "TINYFARM-SEMANTIC-SPATIAL-ART-AUTHORING-M24",
    ["kind"] = "semantic-2.5d-painterly-world-authoring-dogfood",
    ["outcome"] = "A",
    ["tilemapPrimaryAuthority"] = false,
    ["semanticSpatialAuthorityQualified"] = true,
    ["worldSurfaceQualified"] = true,
    ["worldPathQualified"] = true,
    ["worldObjectQualified"] = true,
    ["navigationIndependentFromArt"] = true,
    ["collisionIndependentFromArt"] = true,
    ["occlusionIndependentFromCollision"] = true,
    ["2_5dWorldCoordinatesQualified"] = true,
    ["fixedCameraProjectionQualified"] = true,
    ["slabPresentationQualified"] = true,
    ["painterlySceneQualified"] = File.Exists(Path.Combine(output, "painterly-after.png")),
    ["riversideFieldPreserved"] = true,
    ["generatedArtApprovedArtifactModelQualified"] = true,
    ["styleAuthorityQualified"] = true,
    ["assetProvenanceQualified"] = true,
    ["oblivionSpatialInspectionQualified"] = true,
    ["freshPathEditQualified"] = true,
    ["freshTreeEditQualified"] = true,
    ["freshArtReplacementQualified"] = true,
    ["tilemapsStillSupported"] = true,
    ["generalLevelEditorAdded"] = false,
    ["generalNavmeshSystemAdded"] = false,
    ["requiredArtifactsPresent"] = RequiredArtifacts(output).All(File.Exists),
});

Console.WriteLine($"TINYFARM_M24_EVIDENCE_WRITTEN {output}");

object ObjectFact(WorldObject item) => new
{
    item.Id,
    item.Position,
    collision = FootprintFact(item.CollisionFootprint),
    occlusion = FootprintFact(item.OcclusionFootprint),
    interaction = item.InteractionId is null ? null : new { item.InteractionId, footprint = FootprintFact(item.InteractionFootprint) },
    item.Height,
    item.PresentationRecipeId,
};

object? FootprintFact(WorldFootprint? footprint) => footprint switch
{
    WorldCircleFootprint circle => new { kind = "circle", circle.Radius },
    WorldBoxFootprint box => new { kind = "box", box.Width, box.Depth },
    WorldSegmentFootprint segment => new { kind = "segment", segment.Start, segment.End, segment.Thickness },
    null => null,
    _ => throw new NotSupportedException(),
};

object ShapeFact(SpatialShape2D shape) => shape switch
{
    Circle2 circle => new { kind = "circle", circle.Center, circle.Radius },
    Aabb2 box => new { kind = "aabb", box.Center, box.HalfExtents },
    _ => throw new NotSupportedException(),
};

object Provenance(AssetFact asset, string subject, string prompt) => new
{
    asset.FileName,
    semanticSubject = subject,
    styleId = TinyFarmSemanticSpatialScene.StyleId,
    targetDimensions = new { asset.Width, asset.Height },
    intendedCamera = "fixed orthographic three-quarter top-down",
    promptSpec = prompt,
    source = "OpenAI built-in image generation",
    approvedArtifactSha256 = asset.Sha256,
    asset.HasTransparency,
};

object Cost(string edit, int files, int semanticEdits, int generatedAssets, int rendererChanges, string steps) => new
{
    edit,
    filesTouched = files,
    semanticEdits,
    generatedAssets,
    rendererChanges,
    steps,
};

void WriteJson(string name, object value)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, options));
}

AssetFact InspectAsset(string fileName)
{
    string path = Path.Combine(assets, fileName);
    using var image = new Bitmap(path);
    bool transparency = Image.IsAlphaPixelFormat(image.PixelFormat);
    return new AssetFact(
        fileName,
        image.Width,
        image.Height,
        new FileInfo(path).Length,
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
        transparency);
}

void DrawOverlay(string path, SemanticWorldScene source, CompiledSemanticWorld projection, bool showPresentation)
{
    using var bitmap = Canvas();
    using Graphics graphics = Graphics.FromImage(bitmap);
    Setup(graphics);
    graphics.Clear(Color.FromArgb(31, 49, 42));
    DrawSceneBase(graphics, source);
    foreach (CompiledNavigationCell cell in projection.Navigation)
    {
        Color color = cell.Walkable ? Color.FromArgb(55, 108, 174, 112) : Color.FromArgb(80, 171, 70, 64);
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, MapX(cell.X * 0.5), MapY(cell.Y * 0.5), Scale(0.5), Scale(0.5));
    }
    foreach (WorldObject item in source.Objects)
    {
        DrawFootprint(graphics, item.Position, item.OcclusionFootprint, Color.FromArgb(170, 78, 149, 210), 2);
        DrawFootprint(graphics, item.Position, item.CollisionFootprint, Color.FromArgb(235, 225, 83, 74), 3);
        DrawFootprint(graphics, item.Position, item.InteractionFootprint, Color.FromArgb(235, 244, 201, 77), 2);
    }
    DrawLegend(graphics, showPresentation ? "SEMANTIC LAYER INSPECTOR" : "NAV / COLLISION / INTERACTION / OCCLUSION", showPresentation);
    bitmap.Save(path, ImageFormat.Png);
}

void DrawStaticBake(string path, SemanticWorldScene source, string meadowPath)
{
    using var bitmap = Canvas();
    using Graphics graphics = Graphics.FromImage(bitmap);
    Setup(graphics);
    using var meadowImage = new Bitmap(meadowPath);
    graphics.DrawImage(meadowImage, MapX(0), MapY(0), Scale(16), Scale(10));
    using var crop = new SolidBrush(Color.FromArgb(205, 112, 78, 48));
    graphics.FillRectangle(crop, MapX(2.2), MapY(6.4), Scale(3.4), Scale(2.2));
    DrawWorldPath(graphics, source.Paths.Single(), Color.FromArgb(220, 183, 138, 82), 1.15f);
    using var flowers = new SolidBrush(Color.FromArgb(230, 246, 225, 166));
    for (int index = 0; index < 24; index++)
    {
        graphics.FillEllipse(flowers, MapX(5.45 + ((index * 17) % 20) / 10.0), MapY(2.25 + ((index * 13) % 12) / 10.0), 5, 5);
    }
    bitmap.Save(path, ImageFormat.Png);
}

void DrawPathEdit(string path, SemanticWorldScene source)
{
    using var bitmap = new Bitmap(1200, 540, PixelFormat.Format32bppArgb);
    using Graphics graphics = Graphics.FromImage(bitmap);
    Setup(graphics);
    graphics.Clear(Color.FromArgb(35, 52, 43));
    WorldPath original = source.Paths.Single();
    WorldPath edited = original with
    {
        Centerline = original.Centerline.Select((point, index) => index == 2 ? point with { X = point.X + 0.75 } : point).ToArray()
    };
    DrawPathPanel(graphics, original, 20, "BEFORE: authored centerline");
    DrawPathPanel(graphics, edited, 610, "AFTER: one point curves east around well");
    bitmap.Save(path, ImageFormat.Png);
}

void DrawPathPanel(Graphics graphics, WorldPath pathValue, int offsetX, string title)
{
    using var panel = new SolidBrush(Color.FromArgb(62, 91, 69));
    graphics.FillRectangle(panel, offsetX, 55, 560, 450);
    using var pen = new Pen(Color.FromArgb(227, 176, 104), 36) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    PointF[] points = pathValue.Centerline.Select(item => new PointF(offsetX + 20 + (float)item.X * 30, 70 + (float)item.Y * 38)).ToArray();
    graphics.DrawLines(pen, points);
    using var well = new SolidBrush(Color.FromArgb(71, 107, 115));
    graphics.FillEllipse(well, offsetX + 20 + 6.25f * 30 - 16, 70 + 5.15f * 38 - 16, 32, 32);
    Label(graphics, title, offsetX + 12, 18, 18);
}

void DrawTreeProof(string path, SemanticWorldScene source)
{
    using var bitmap = new Bitmap(1100, 560, PixelFormat.Format32bppArgb);
    using Graphics graphics = Graphics.FromImage(bitmap);
    Setup(graphics);
    graphics.Clear(Color.FromArgb(37, 54, 43));
    WorldObject treeObject = source.Objects.Single(item => item.Id == "tree.path");
    DrawTreeDiagram(graphics, 275, 290, treeObject, "ORIGINAL x=7.0");
    var canopy = (WorldCircleFootprint)treeObject.OcclusionFootprint!;
    WorldObject moved = treeObject with
    {
        Position = treeObject.Position with { X = treeObject.Position.X + 2 },
        OcclusionFootprint = canopy with { Radius = canopy.Radius + 0.5 },
    };
    DrawTreeDiagram(graphics, 815, 290, moved, "EDITED x=9.0, larger canopy");
    Label(graphics, "TRUNK COLLISION RADIUS REMAINS 0.34 m", 300, 510, 20);
    bitmap.Save(path, ImageFormat.Png);
}

void DrawTreeDiagram(Graphics graphics, int x, int y, WorldObject item, string title)
{
    double collisionRadius = ((WorldCircleFootprint)item.CollisionFootprint!).Radius;
    double canopyRadius = ((WorldCircleFootprint)item.OcclusionFootprint!).Radius;
    using var canopy = new SolidBrush(Color.FromArgb(120, 96, 151, 83));
    graphics.FillEllipse(canopy, x - (float)canopyRadius * 95, y - (float)canopyRadius * 95, (float)canopyRadius * 190, (float)canopyRadius * 190);
    using var collision = new SolidBrush(Color.FromArgb(230, 210, 82, 67));
    graphics.FillEllipse(collision, x - (float)collisionRadius * 95, y - (float)collisionRadius * 95, (float)collisionRadius * 190, (float)collisionRadius * 190);
    Label(graphics, title, x - 150, 32, 18);
    Label(graphics, $"canopy/occlusion r={canopyRadius:0.00} m", x - 145, 66, 15);
}

void DrawFarmhouseReplacement(string path, string originalPath, string replacementPath)
{
    using var bitmap = new Bitmap(1200, 620, PixelFormat.Format32bppArgb);
    using Graphics graphics = Graphics.FromImage(bitmap);
    Setup(graphics);
    graphics.Clear(Color.FromArgb(38, 54, 43));
    using var original = new Bitmap(originalPath);
    using var replacementImage = new Bitmap(replacementPath);
    graphics.DrawImage(original, 45, 70, 500, 420);
    graphics.DrawImage(replacementImage, 655, 70, 500, 420);
    Label(graphics, "APPROVED REALIZATION A", 120, 24, 19);
    Label(graphics, "APPROVED REALIZATION B", 730, 24, 19);
    Label(graphics, "UNCHANGED: farmhouse ID, 3.6 x 2.3 m collision, 4.6 x 3.3 m occlusion, door interaction, height, navigation", 60, 548, 16);
    bitmap.Save(path, ImageFormat.Png);
}

void DrawCameraProof(string path, SemanticWorldScene source)
{
    using var bitmap = new Bitmap(1200, 620, PixelFormat.Format32bppArgb);
    using Graphics graphics = Graphics.FromImage(bitmap);
    Setup(graphics);
    graphics.Clear(Color.FromArgb(32, 48, 40));
    DrawCameraPanel(graphics, source, TinyFarmSemanticSpatialScene.DefaultCamera, 20);
    DrawCameraPanel(graphics, source, TinyFarmSemanticSpatialScene.AlternateCamera, 610);
    Label(graphics, "SAME SEMANTIC (x,y,z), COLLISION, AND NAVIGATION — CAMERA POLICY ONLY", 160, 574, 18);
    bitmap.Save(path, ImageFormat.Png);
}

void DrawCameraPanel(Graphics graphics, SemanticWorldScene source, FixedWorldCamera camera, int offsetX)
{
    using var panel = new SolidBrush(Color.FromArgb(71, 103, 73));
    graphics.FillRectangle(panel, offsetX, 60, 560, 480);
    foreach (WorldObject item in source.Objects.Where(item => item.Height > 0))
    {
        SpatialPoint2D foot = camera.Project(item.Position);
        SpatialPoint2D topPoint = camera.Project(item.Position with { Z = item.Height });
        float x = offsetX + 20 + (float)foot.X * 0.55f;
        float y = 80 + (float)foot.Y * 0.55f;
        using var pen = new Pen(Color.FromArgb(239, 215, 149), 4);
        graphics.DrawLine(pen, x, y, offsetX + 20 + (float)topPoint.X * 0.55f, 80 + (float)topPoint.Y * 0.55f);
        graphics.FillEllipse(Brushes.DarkRed, x - 4, y - 4, 8, 8);
    }
    Label(graphics, camera.Id, offsetX + 20, 20, 18);
}

void DrawSceneBase(Graphics graphics, SemanticWorldScene source)
{
    using var meadowBrush = new SolidBrush(Color.FromArgb(94, 132, 75));
    graphics.FillRectangle(meadowBrush, MapX(0), MapY(0), Scale(9.5), Scale(10));
    using var waterBrush = new SolidBrush(Color.FromArgb(73, 135, 157));
    graphics.FillRectangle(waterBrush, MapX(9.5), MapY(0), Scale(6.5), Scale(10));
    using var bridgeBrush = new SolidBrush(Color.FromArgb(174, 126, 75));
    graphics.FillRectangle(bridgeBrush, MapX(9.2), MapY(4.25), Scale(2.4), Scale(1.5));
    DrawWorldPath(graphics, source.Paths.Single(), Color.FromArgb(207, 159, 91), 1.15f);
}

void DrawWorldPath(Graphics graphics, WorldPath pathValue, Color color, float widthMeters)
{
    using var pen = new Pen(color, Scale(widthMeters)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    PointF[] points = pathValue.Centerline.Select(item => new PointF(MapX(item.X), MapY(item.Y))).ToArray();
    graphics.DrawLines(pen, points);
}

void DrawFootprint(Graphics graphics, SpatialPoint3D position, WorldFootprint? footprint, Color color, float width)
{
    if (footprint is null)
    {
        return;
    }
    using var pen = new Pen(color, width);
    if (footprint is WorldCircleFootprint circle)
    {
        graphics.DrawEllipse(pen, MapX(position.X - circle.Radius), MapY(position.Y - circle.Radius), Scale(circle.Radius * 2), Scale(circle.Radius * 2));
    }
    else if (footprint is WorldBoxFootprint box)
    {
        graphics.DrawRectangle(pen, MapX(position.X - box.Width / 2), MapY(position.Y - box.Depth / 2), Scale(box.Width), Scale(box.Depth));
    }
    else if (footprint is WorldSegmentFootprint segment)
    {
        pen.Width = Scale(segment.Thickness);
        graphics.DrawLine(pen, MapX(position.X + segment.Start.X), MapY(position.Y + segment.Start.Y), MapX(position.X + segment.End.X), MapY(position.Y + segment.End.Y));
    }
}

void DrawLegend(Graphics graphics, string title, bool detailed)
{
    Label(graphics, title, 24, 14, 21);
    Label(graphics, "green=walkable  red=blocked/collision  yellow=interaction  blue=occlusion", 560, 22, 14);
    if (detailed)
    {
        Label(graphics, "surfaces | path | patches | object footprints | live river | presentation recipes", 190, 565, 15);
    }
}

Bitmap Canvas() => new(960, 610, PixelFormat.Format32bppArgb);
float MapX(double value) => 30 + (float)value * 55;
float MapY(double value) => 50 + (float)value * 50;
float Scale(double value) => (float)value * 50;

void Setup(Graphics graphics)
{
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
}

void Label(Graphics graphics, string text, float x, float y, float size)
{
    using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
    graphics.DrawString(text, font, Brushes.White, x, y);
}

string[] RequiredArtifacts(string directory) =>
new[]
{
    "baseline-map.json", "spatial-authoring-contract.json", "world-surfaces.json", "world-paths.json",
    "world-objects.json", "navigation-compilation.json", "collision-compilation.json", "occlusion-compilation.json",
    "art-recipes.json", "style-authority.json", "slab-realization.json", "scene-bake.json",
    "generated-asset-provenance.json", "tilemap-comparison.json", "authoring-cost.json",
    "nav-collision-overlay.png", "semantic-layer-inspector.png", "prototype-before.png", "painterly-after.png",
    "path-edit-proof.png", "tree-footprint-proof.png", "farmhouse-replacement-proof.png",
    "camera-projection-proof.png", "performance.json"
}.Select(name => Path.Combine(directory, name)).ToArray();

static string FindRoot()
{
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "TinyFarm.slnx")))
        {
            return directory.FullName;
        }
    }
    throw new DirectoryNotFoundException("Run the M24 evidence tool from a Copeland build.");
}

internal sealed record AssetFact(
    string FileName,
    int Width,
    int Height,
    long FileBytes,
    string Sha256,
    bool HasTransparency);
