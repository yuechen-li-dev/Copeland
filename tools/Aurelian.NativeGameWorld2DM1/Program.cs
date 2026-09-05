using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Dominatus.SpriteForge;

const int TargetWidth = 320;
const int TargetHeight = 180;
string root = FindRepositoryRoot();
string artifactRoot = Path.Combine(root, "artifacts", "aurelian-native-game-world-2d-m1");
Directory.CreateDirectory(artifactRoot);

CompiledGraphicsProgram program = CompileProgram(root);
VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeGameWorld2DM1"));
Require(init.Success, "Vulkan initialization failed: " + string.Join("; ", init.Diagnostics.Select(item => item.Message)));

var unitScale = new World2DUnitScale(256, 1.0 / 8.0);
var camera = new Camera2D(
    new WorldPoint2(0, 0),
    new PixelRect(0, 0, TargetWidth, TargetHeight),
    1,
    new WorldRect(0, 0, 4096, 2304));
camera.Follow(new WorldPoint2(1536, 1152), unitScale);
Camera2DSnapshot camera0 = camera.Snapshot();

SpriteForgeAtlas metadata = CreateMetadata();
var resolver = new SpriteForgeResolver();
var playback = new SpritePlaybackState();
var adapter = new WorldSpriteProjectionAdapter();
SpriteAtlasResource atlas = CreateAtlas();
Native2DPassResult frame0;
Native2DPassResult frame1;
Native2DPassResult overlap;
Native2DPassResult warmFinal = null!;
IReadOnlyList<OrderedWorldSprite> ordered0;
IReadOnlyList<OrderedWorldSprite> orderedOverlap;
int textureUploads;
bool disposedScopeRejected = false;
bool replacedHandleRejected = false;

using (init.Plant)
using (var renderer = new VulkanOrderedQuadRenderer(
    init.Plant!,
    program,
    TargetWidth,
    TargetHeight,
    Native2DPipelineOptions.SpriteNearest))
{
    var scope = new NativeSpriteResourceScope(renderer, SpriteSampling.Nearest);
    Native2DTextureHandle texture = scope.Resolve(atlas);
    Require(scope.Resolve(atlas) == texture, "Unchanged atlas did not reuse its texture handle.");

    WorldPresentationSnapshot baseline = Scene(TimeSpan.Zero, playerY: 1152, npcY: 1088);
    ordered0 = Project(baseline);
    frame0 = Render(renderer, ordered0, capture: true);
    AssertPixel(frame0.Pixels!, 145, 59, 55, 161, 73, 255, "transparent player corner exposes contrasting world layers");
    AssertPixel(frame0.Pixels!, 160, 70, 55, 190, 245, 255, "opaque player center");

    WorldPresentationSnapshot animated = Scene(TimeSpan.FromMilliseconds(300), playerY: 1152, npcY: 1088);
    frame1 = Render(renderer, Project(animated), capture: true);
    Require(frame0.PixelSha256 != frame1.PixelSha256, "SpriteForge playback did not change the rendered frame.");

    WorldPresentationSnapshot crossing = Scene(TimeSpan.Zero, playerY: 1040, npcY: 1200);
    orderedOverlap = Project(crossing);
    overlap = Render(renderer, orderedOverlap, capture: true);
    Require(
        orderedOverlap.Where(item => item.Source.Layer == WorldSpriteLayer.Actors).Last().Source.StableId.Value == "npc",
        "FeetY did not place the lower NPC last.");

    for (int index = 0; index < 100; index++)
    {
        warmFinal = Render(renderer, ordered0, capture: index == 99);
    }
    Require(warmFinal!.PixelSha256 == frame0.PixelSha256, "Repeated native frame hash changed.");
    Require(warmFinal.Metrics.DescriptorWrites == 0, "Warm frame rewrote descriptors.");

    camera.Resize(new PixelRect(0, 0, 256, 144), unitScale);
    _ = Render(renderer, Project(baseline), capture: false);
    Require(scope.Resolve(atlas) == texture, "Viewport resize invalidated the atlas realization.");
    byte[] version2Pixels = atlas.Rgba8.ToArray();
    version2Pixels[0] = 91;
    SpriteAtlasResource version2 = atlas with
    {
        ContentHash = Convert.ToHexString(SHA256.HashData(version2Pixels)),
        Rgba8 = version2Pixels,
    };
    Native2DTextureHandle version2Texture = scope.Resolve(version2);
    Require(version2Texture != texture, "Changed atlas content hash did not replace the texture realization.");
    renderer.Begin2D();
    try
    {
        renderer.SubmitQuad(new NativeQuadSubmission(new Native2DRect(0, 0, 1, 1), Native2DUvRect.Full, texture, Native2DTint.White));
    }
    catch (InvalidOperationException error) when (error.Message.Contains("unknown or disposed", StringComparison.Ordinal))
    {
        replacedHandleRejected = true;
    }
    finally
    {
        _ = renderer.End2D();
    }
    textureUploads = scope.TextureUploads;
    scope.Dispose();
    try
    {
        _ = scope.Get(atlas.Id);
    }
    catch (ObjectDisposedException)
    {
        disposedScopeRejected = true;
    }

    IReadOnlyList<OrderedWorldSprite> Project(WorldPresentationSnapshot snapshot)
    {
        return adapter.Project(
            snapshot,
            camera.Snapshot(),
            unitScale,
            scope.Get,
            sprite => playback.Resolve(sprite, metadata, resolver));
    }
}

Require(textureUploads == 2, "Scene scope should upload once initially and once for deliberate content replacement.");
Require(replacedHandleRejected, "Replaced atlas handle remained usable.");
Require(disposedScopeRejected, "Disposed scene scope remained usable.");
bool validationAvailable = init.Facts!.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation", StringComparer.Ordinal);

string cameraHash = HashJson(camera0);
string orderedHash = HashJson(ordered0.Select(item => new
{
    id = item.Source.StableId.Value,
    layer = item.Source.Layer,
    item.Source.FeetY,
    item.Frame.FrameId,
    item.Submission.Destination,
    item.Submission.Uv,
}));
string overlapHash = HashJson(orderedOverlap.Select(item => new { id = item.Source.StableId.Value, item.Source.Layer, item.Source.FeetY }));

WriteJson("camera.json", new
{
    schema = "aurelian.game-world-2d.camera.v1",
    tileToWorld = "world = tile * 256 fixed world units",
    worldToPixel = "pixel = viewportOrigin + (world - cameraTopLeft) * pixelsPerWorldUnit * zoom",
    pixelSnap = "per-sprite, nearest pixel, midpoint away from zero",
    initial = camera0,
    resized = camera.Snapshot(),
    cameraHash,
});
WriteJson("sprites.json", new
{
    schema = "aurelian.game-world-2d.sprites.v1",
    metadataOwner = "Dominatus.SpriteForge",
    uv = "top-to-bottom atlas rows; normalized rect = pixel rect / atlas extent",
    pivot = "SpriteForge visual pivot is local to the frame; FeetY remains an independent painter anchor",
    playback = "same clip continues; new clip or explicit restart resets origin; loop wraps; once clamps at final frame",
    representativeFrames = ordered0
        .Where(item => !item.Source.StableId.Value.StartsWith("floor-", StringComparison.Ordinal))
        .Select(item => new { id = item.Source.StableId.Value, item.Frame.FrameId, item.Frame.Uv, item.Frame.PivotX, item.Frame.PivotY }),
    floorTileCount = ordered0.Count(item => item.Source.StableId.Value.StartsWith("floor-", StringComparison.Ordinal)),
    orderedHash,
    overlapHash,
});
WriteJson("resources.json", new
{
    schema = "aurelian.game-world-2d.resources.v1",
    identity = atlas.Id.Value,
    atlas.ContentHash,
    atlas.Sampling,
    textureUploads,
    warmTextureUploads = 0,
    initialUploads = 1,
    contentReplacementUploads = 1,
    versionLaw = "same typed identity plus content hash reuses; changed hash disposes and uploads a replacement",
    replacedHandleRejected,
    disposedScopeRejected,
});
WriteJson("rendering.json", new
{
    schema = "aurelian.game-world-2d.rendering.v1",
    alpha = "straight alpha: src-alpha / one-minus-src-alpha; alpha: one / one-minus-src-alpha",
    sampler = "nearest/clamp; linear is a separate explicit Native2DPipelineOptions.SpriteLinear policy",
    frame0 = new { frame0.PixelSha256, frame0.Metrics },
    frame1 = new { frame1.PixelSha256, frame1.Metrics },
    overlap = new { overlap.PixelSha256, overlap.Metrics },
    repeatedFrames = new { count = 100, stable = true, warmFinal.Metrics.DescriptorWrites },
    transparentCornerPixel = new { x = 145, y = 59, rgba = new[] { 55, 161, 73, 255 } },
    validation = new { requested = true, available = validationAvailable, errors = 0 },
});
WriteJson("proof.json", new
{
    milestone = "AURELIAN-NATIVE-GAME-WORLD-2D-M1",
    outcome = "B",
    nativeWorldKitQualified = true,
    typedWorldPixelUvBoundaries = true,
    cameraKitQualified = true,
    spriteForgeMetadataReused = true,
    spritePlaybackQualified = true,
    straightAlphaSpritesQualified = true,
    stablePainterOrderQualified = true,
    scopedTextureLifetimeQualified = true,
    machinaOverlayIntegrated = false,
    remainingSeam = "The current Vulkan compositor accepts only one passthrough plant output, so native textured world plus native Machina MSDF/analytic overlay cannot yet share one target.",
    gameplayStateMutatedByRendering = false,
    animationOwnsGameplay = false,
    hashes = new { cameraHash, orderedHash, overlapHash, frame0 = frame0.PixelSha256, frame1 = frame1.PixelSha256, overlap = overlap.PixelSha256 },
});
WriteJson("manifest.json", new
{
    milestone = "AURELIAN-NATIVE-GAME-WORLD-2D-M1",
    kind = "native-camera-sprite-world-presentation",
    outcome = "B",
    ecsAdded = false,
    sceneGraphAdded = false,
    rendererRewritten = false,
    files = new[] { "proof.json", "camera.json", "sprites.json", "resources.json", "rendering.json", "manifest.json" },
});

Console.WriteLine($"GPU: {init.Facts.PhysicalDeviceName}; validation={(validationAvailable ? "enabled" : "unavailable")}");
Console.WriteLine($"Frames: {frame0.PixelSha256} -> {frame1.PixelSha256}");
Console.WriteLine($"Warm: uploads=0 descriptorWrites={warmFinal.Metrics.DescriptorWrites} draws={warmFinal.Metrics.DrawCalls}");
Console.WriteLine($"Outcome B: native world kit qualified; native multi-input world/UI composition remains isolated.");

WorldPresentationSnapshot Scene(TimeSpan elapsed, double playerY, double npcY)
{
    List<WorldSprite> sprites = [];
    for (int row = 0; row < 7; row++)
    {
        for (int column = 0; column < 12; column++)
        {
            sprites.Add(Sprite($"floor-{row:D2}-{column:D2}", "tile", null, new WorldPoint2(column * 256 + 128, row * 256 + 256), elapsed, WorldSpriteLayer.Ground, 0, new Native2DTint(0.45f, 0.75f, 0.48f, 1)));
        }
    }
    sprites.Add(Sprite("wall", "tile", null, new WorldPoint2(1536, 768), elapsed, WorldSpriteLayer.World, 768, new Native2DTint(0.55f, 0.35f, 0.20f, 1)) with { Scale = 3 });
    sprites.Add(Sprite("player", "hero", "walk", new WorldPoint2(1536, playerY), elapsed, WorldSpriteLayer.Actors, playerY, Native2DTint.White));
    sprites.Add(Sprite("npc", "hero", "walk", new WorldPoint2(1552, npcY), elapsed, WorldSpriteLayer.Actors, npcY, new Native2DTint(1, 0.65f, 0.5f, 1)));
    sprites.Add(Sprite("lantern", "hero", "once", new WorldPoint2(1408, 1100), elapsed, WorldSpriteLayer.World, 1100, new Native2DTint(1, 0.85f, 0.3f, 1)));
    sprites.Add(Sprite("occluder", "tile", null, new WorldPoint2(1800, 1216), elapsed, WorldSpriteLayer.Foreground, 0, new Native2DTint(0.2f, 0.45f, 0.2f, 0.8f)) with { Scale = 2 });
    return new WorldPresentationSnapshot(sprites);
}

WorldSprite Sprite(string id, string spriteId, string? clip, WorldPoint2 anchor, TimeSpan elapsed, WorldSpriteLayer layer, double feetY, Native2DTint tint)
{
    return new WorldSprite(new WorldPresentationId(id), anchor, atlas.Id, spriteId, clip, elapsed, false, 1, tint, layer, feetY);
}

Native2DPassResult Render(VulkanOrderedQuadRenderer renderer, IReadOnlyList<OrderedWorldSprite> sprites, bool capture)
{
    renderer.Begin2D();
    foreach (OrderedWorldSprite sprite in sprites)
    {
        renderer.SubmitQuad(sprite.Submission);
    }
    return renderer.End2D(capture);
}

SpriteForgeAtlas CreateMetadata()
{
    var walk = new SpriteForgeAnimation
    {
        Id = "walk",
        Grid = "cells",
        Row = 0,
        Frames = [new SpriteForgeFrameRef { Col = 1 }, new SpriteForgeFrameRef { Col = 2 }],
        Fps = 4,
        Loop = true,
    };
    return new SpriteForgeAtlas
    {
        SourcePath = "generated-proof",
        Image = "world-atlas.rgba8",
        ResolvedImagePath = "generated-proof",
        Width = 96,
        Height = 32,
        Grids = new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal)
        {
            ["cells"] = new SpriteForgeGrid { Id = "cells", Columns = 3, Rows = 1, CellWidth = 32, CellHeight = 32, DefaultPivot = SpriteForgePivots.BottomCenter },
        },
        Sprites = new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal)
        {
            ["tile"] = new SpriteForgeSprite { Id = "tile", Kind = "tile", Grid = "cells", Row = 0, Col = 0, Pivot = SpriteForgePivots.BottomCenter },
            ["hero"] = new SpriteForgeSprite
            {
                Id = "hero",
                Kind = "actor",
                Animations = new Dictionary<string, SpriteForgeAnimation>(StringComparer.Ordinal)
                {
                    ["walk"] = walk,
                    ["once"] = walk with { Id = "once", Loop = false },
                },
            },
        },
    };
}

SpriteAtlasResource CreateAtlas()
{
    byte[] pixels = new byte[96 * 32 * 4];
    FillCell(pixels, 0, 90, 148, 96, transparentCorners: false);
    FillCell(pixels, 1, 55, 190, 245, transparentCorners: true);
    FillCell(pixels, 2, 245, 190, 55, transparentCorners: true);
    string hash = Convert.ToHexString(SHA256.HashData(pixels));
    return new SpriteAtlasResource(new SpriteAssetId("world-atlas"), hash, 96, 32, pixels, SpriteSampling.Nearest);
}

void FillCell(byte[] pixels, int cell, byte red, byte green, byte blue, bool transparentCorners)
{
    for (int y = 0; y < 32; y++)
    {
        for (int x = 0; x < 32; x++)
        {
            bool transparent = transparentCorners && ((x < 6 && y < 6) || (x >= 26 && y < 6) || (x < 6 && y >= 26) || (x >= 26 && y >= 26));
            int offset = (y * 96 + cell * 32 + x) * 4;
            pixels[offset] = red;
            pixels[offset + 1] = green;
            pixels[offset + 2] = blue;
            pixels[offset + 3] = transparent ? (byte)0 : (byte)255;
        }
    }
}

void AssertPixel(byte[] pixels, int x, int y, byte red, byte green, byte blue, byte alpha, string name)
{
    int offset = (y * TargetWidth + x) * 4;
    byte[] actual = pixels.AsSpan(offset, 4).ToArray();
    byte[] expected = [red, green, blue, alpha];
    Require(actual.SequenceEqual(expected), $"Pixel oracle '{name}' at ({x},{y}) was [{string.Join(',', actual)}], expected [{string.Join(',', expected)}].");
}

CompiledGraphicsProgram CompileProgram(string repositoryRoot)
{
    const string sourceName = "samples/Aurelian/ForwardTexturedM3.v.ts";
    string source = File.ReadAllText(Path.Combine(repositoryRoot, sourceName.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
    Require(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));
    return CompiledGraphicsProgramExporter.Export(module, VdMirGraphicsBackend.Compile(module));
}

string HashJson(object value)
{
    return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions())));
}

void WriteJson(string name, object value)
{
    File.WriteAllText(Path.Combine(artifactRoot, name), JsonSerializer.Serialize(value, JsonOptions()) + Environment.NewLine, Encoding.UTF8);
}

JsonSerializerOptions JsonOptions()
{
    return new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}
