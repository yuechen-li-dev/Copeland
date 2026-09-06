using System.Diagnostics;
using System.Numerics;
using Aurelian.Composition;
using Aurelian.Effects2D;
using Aurelian.Effects2D.Graphics;
using Aurelian.GameHost;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Machina;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using TinyFarm.Core;
using TinyFarm.InputMan;
using Silk.NET.Windowing;

namespace TinyFarm.Native;

internal sealed class SupperRenderer : IAurelianHostCompositor
{
    private readonly AurelianVulkanPlant plant;
    private readonly AurelianVulkanSurface surface;
    private readonly AurelianVulkanSwapchain swapchain;
    private readonly NativeLayerCompositor compositor;
    private readonly VulkanNativeSwapchainPresenter swapchainPresenter;
    private readonly TinyFarmSupperGame game;
    private readonly SupperUi ui;
    private readonly SupperPresenter world;
    private TinyFarmFrame frame;
    private bool captureNextFrame;
    private long projectionAllocatedBytes;
    private long compositionAllocatedBytes;
    private long swapchainAllocatedBytes;
    private long projectionTicks;
    private long compositionTicks;
    private long swapchainTicks;
    private int measuredFrames;
    private long nativePassAllocatedBytes;
    private int descriptorWrites;
    private int bufferUploads;
    private int drawCalls;

    public SupperRenderer(string root, TinyFarmSupperGame game, SupperWindow window, bool proof)
    {
        this.game = game;
        ui = new SupperUi(game);
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(
                EnableValidation: proof,
                ApplicationName: "TinyFarm - A Little Mint of Kindness",
                EnablePresentation: true,
                RequiredPresentationInstanceExtensions: window.RequiredVulkanInstanceExtensions));
        if (!init.Success || init.Plant is null)
        {
            throw new InvalidOperationException(string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        }
        plant = init.Plant;
        Device = init.Facts!.PhysicalDeviceName;
        VulkanSwapchainCreateResult swapchainResult = VulkanSwapchainFactory.Create(
            plant,
            window.NativeWindow,
            new VulkanSwapchainCreateOptions(1280, 720, VSync: true, "TinyFarm - A Little Mint of Kindness", Visible: !proof));
        if (!swapchainResult.Success || swapchainResult.Surface is null || swapchainResult.Swapchain is null)
        {
            throw new InvalidOperationException(string.Join("; ", swapchainResult.Diagnostics.Select(item => item.Message)));
        }
        surface = swapchainResult.Surface;
        swapchain = swapchainResult.Swapchain;
        VulkanTextureFormat targetFormat = ParseSwapchainFormat(swapchain.Facts.SelectedFormat);
        compositor = new NativeLayerCompositor(plant, 1280, 720, format: targetFormat);
        CompiledGraphicsProgram analytic = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts");
        CompiledGraphicsProgram shockwave = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/SoftShockwave.v.ts");
        CompiledGraphicsProgram msdf = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts");
        CompiledGraphicsProgram texture = Compile(root, "samples/Aurelian/ForwardTexturedM3.v.ts");
        string spriteAtlasPath = Path.Combine(AppContext.BaseDirectory, "Assets", "M11", "tinyfarm-sprite-atlas-source.png");
        TinyFarmSpriteAtlas spriteAtlas = TinyFarmSpriteAtlas.Load(spriteAtlasPath);
        world = new SupperPresenter(
            new LayerId("farm-world"),
            plant,
            analytic,
            shockwave,
            texture,
            spriteAtlas,
            game,
            () => frame);
        compositor.Add(new SupperLayer(world.Layer, 0), world);
        string portraitPath = Path.Combine(AppContext.BaseDirectory, "Assets", "mara-dialogue.png");
        if (File.Exists(portraitPath))
        {
            var portrait = new SupperPortrait(plant, texture, game, portraitPath);
            compositor.Add(new SupperLayer(portrait.Layer, 50), portrait);
        }
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "SpaceMono-Regular.ttf");
        SupperNativeUiFont font = SupperNativeUiFont.Create(fontPath);
        Overlay = new SupperOverlay(
            new LayerId("machina-hud"),
            plant,
            analytic,
            msdf,
            font,
            () => ui.Resources(frame));
        compositor.Add(new SupperLayer(Overlay.Layer, 100), Overlay);
        compositor.Attach();
        compositor.RunFrame(0, TimeSpan.Zero);
        swapchainPresenter = new VulkanNativeSwapchainPresenter(plant, compositor.Target, swapchain);
    }

    public string Device { get; }
    public NativeLayerFrameResult? Last { get; private set; }
    public int UiRebuilds => ui.Rebuilds;
    public int ShaderQuads => world.ShaderQuads;
    private SupperOverlay Overlay { get; }
    public long WorldAllocatedBytes => world.AllocatedBytes;
    public long OverlayAllocatedBytes => Overlay.AllocatedBytes;
    public int DynamicUiTextureUploads => 0;
    public int NativeUiPrimitiveCount => Overlay.NativePrimitiveCount;
    public int FallbackRasterPrimitiveCount => Overlay.FallbackRasterPrimitiveCount;
    public long FallbackRasterBytes => 0;
    public int FallbackRasterUploads => 0;
    public int FontAtlasUploads => Overlay.FontAtlasUploads;
    public int TextGeometryCacheEntries => Overlay.TextGeometryCacheEntries;
    public int NativeUiGeometryRebuilds => Overlay.GeometryRebuilds;
    public string PresentMode => swapchainPresenter.PresentMode;
    public uint SwapchainImageCount => swapchainPresenter.SwapchainImageCount;
    public int ReadbackCount { get; private set; }
    public int MeasuredFrames => measuredFrames;
    public long ProjectionAllocatedBytes => projectionAllocatedBytes;
    public long CompositionAllocatedBytes => compositionAllocatedBytes;
    public long SwapchainAllocatedBytes => swapchainAllocatedBytes;
    public TimeSpan ProjectionTime => Stopwatch.GetElapsedTime(0, projectionTicks);
    public TimeSpan CompositionTime => Stopwatch.GetElapsedTime(0, compositionTicks);
    public TimeSpan SwapchainTime => Stopwatch.GetElapsedTime(0, swapchainTicks);
    public long NativePassAllocatedBytes => nativePassAllocatedBytes;
    public int DescriptorWrites => descriptorWrites;
    public int BufferUploads => bufferUploads;
    public int DrawCalls => drawCalls;
    public int WorldSpriteCount => world.LastSpriteCount;
    public Camera2DSnapshot? WorldCamera => world.LastCamera;
    public int SpriteTextureUploads => world.SpriteTextureUploads;
    public string SpriteAtlasHash => world.SpriteAtlasHash;
    public SpriteAlphaCleanupFacts SpriteAlphaCleanup => world.SpriteAlphaCleanup;

    public void CaptureNextFrame()
    {
        captureNextFrame = true;
    }

    public void ResetPerformanceMetrics()
    {
        projectionAllocatedBytes = 0;
        compositionAllocatedBytes = 0;
        swapchainAllocatedBytes = 0;
        projectionTicks = 0;
        compositionTicks = 0;
        swapchainTicks = 0;
        measuredFrames = 0;
        nativePassAllocatedBytes = 0;
        descriptorWrites = 0;
        bufferUploads = 0;
        drawCalls = 0;
        world.ResetPerformanceMetrics();
        Overlay.ResetPerformanceMetrics();
    }

    public void Resize(HostSurfaceSize size) { }

    public void Present(AurelianHostFrame hostFrame)
    {
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        long timeStart = Stopwatch.GetTimestamp();
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        long projectionEnd = Stopwatch.GetTimestamp();
        long projectionAllocationEnd = GC.GetAllocatedBytesForCurrentThread();
        bool capture = captureNextFrame;
        captureNextFrame = false;
        Last = compositor.RunFrame(hostFrame.Sequence, hostFrame.Elapsed, captureReadback: capture);
        foreach (Native2DPassResult pass in Last.NativeFrame.Passes)
        {
            nativePassAllocatedBytes += pass.Metrics.CpuAllocatedBytes;
            descriptorWrites += pass.Metrics.DescriptorWrites;
            bufferUploads += pass.Metrics.BufferUploads;
            drawCalls += pass.Metrics.DrawCalls;
        }
        long compositionEnd = Stopwatch.GetTimestamp();
        long compositionAllocationEnd = GC.GetAllocatedBytesForCurrentThread();
        if (capture)
        {
            ReadbackCount++;
        }
        swapchainPresenter.Present(hostFrame.Sequence);
        long swapchainEnd = Stopwatch.GetTimestamp();
        long swapchainAllocationEnd = GC.GetAllocatedBytesForCurrentThread();
        projectionAllocatedBytes += projectionAllocationEnd - allocationStart;
        compositionAllocatedBytes += compositionAllocationEnd - projectionAllocationEnd;
        swapchainAllocatedBytes += swapchainAllocationEnd - compositionAllocationEnd;
        projectionTicks += projectionEnd - timeStart;
        compositionTicks += compositionEnd - projectionEnd;
        swapchainTicks += swapchainEnd - compositionEnd;
        measuredFrames++;
    }

    public void Dispose()
    {
        swapchainPresenter.Dispose();
        compositor.Dispose();
        swapchain.Dispose();
        surface.Dispose();
        plant.Dispose();
    }

    private static VulkanTextureFormat ParseSwapchainFormat(string format)
    {
        return format switch
        {
            "R8G8B8A8Unorm" => VulkanTextureFormat.Rgba8Unorm,
            "B8G8R8A8Unorm" => VulkanTextureFormat.Bgra8Unorm,
            "R8G8B8A8Srgb" => VulkanTextureFormat.Rgba8Srgb,
            "B8G8R8A8Srgb" => VulkanTextureFormat.Bgra8Srgb,
            _ => throw new NotSupportedException($"TinyFarm does not support swapchain format {format}."),
        };
    }

    private static CompiledGraphicsProgram Compile(string root, string file)
    {
        string source = File.ReadAllText(Path.Combine(root, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(file, source)]));
        if (!module.Success)
        {
            throw new InvalidOperationException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        }
        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
        if (!backend.Vertex.SpirvValidated || !backend.Pixel.SpirvValidated)
        {
            throw new InvalidOperationException(backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
        }
        return CompiledGraphicsProgramExporter.Export(module, backend);
    }
}

internal sealed class SupperPresenter(
    LayerId layer,
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram analytic,
    CompiledGraphicsProgram shockwave,
    CompiledGraphicsProgram textured,
    TinyFarmSpriteAtlas spriteAtlas,
    TinyFarmSupperGame game,
    Func<TinyFarmFrame> getFrame) : INativeLayerPresenter
{
    private VulkanOrderedQuadRenderer shapes = null!;
    private VulkanOrderedQuadRenderer waves = null!;
    private VulkanOrderedQuadRenderer sprites = null!;
    private NativeSpriteResourceScope spriteResources = null!;
    private readonly SpritePlaybackState playback = new();
    private readonly WorldSpriteProjectionAdapter spriteProjection = new();
    private readonly Dominatus.SpriteForge.SpriteForgeResolver spriteResolver = new();
    private readonly World2DUnitScale worldScale = new(1, 48);
    private float scale;
    private float left;
    private float top;
    private readonly List<ParticleSnapshot> particleSnapshots = new(256);
    private readonly List<EffectQuadSnapshot> quadSnapshots = new(32);
    public LayerId Layer => layer;
    public int ShaderQuads { get; private set; }
    public long AllocatedBytes { get; private set; }
    public int LastSpriteCount { get; private set; }
    public Camera2DSnapshot? LastCamera { get; private set; }
    public int SpriteTextureUploads => spriteResources?.TextureUploads ?? 0;
    public string SpriteAtlasHash => spriteAtlas.Resource.ContentHash;
    public SpriteAlphaCleanupFacts SpriteAlphaCleanup => spriteAtlas.AlphaCleanup;

    public void ResetPerformanceMetrics()
    {
        AllocatedBytes = 0;
    }

    public void Attach(VulkanNativeFrameTarget target)
    {
        shapes = new VulkanOrderedQuadRenderer(plant, analytic, target, Native2DPipelineOptions.AnalyticShape2D);
        waves = new VulkanOrderedQuadRenderer(plant, shockwave, target, Native2DPipelineOptions.SoftShockwave);
        sprites = new VulkanOrderedQuadRenderer(plant, textured, target, Native2DPipelineOptions.SpriteNearest);
        spriteResources = new NativeSpriteResourceScope(sprites, SpriteSampling.Nearest);
        spriteResources.Resolve(spriteAtlas.Resource);
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        Detach();
        Attach(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        TinyFarmFrame frame = getFrame();
        bool cave = frame.ActiveScene == TinyFarmSceneIds.DungeonEntrance;
        bool house = frame.ActiveScene == TinyFarmSceneIds.Residence || frame.ActiveScene == TinyFarmSceneIds.GeneralStore;
        context.Present(shapes, pass =>
        {
            Rect(pass, 0, 0, 1280, 720, 0x426B3FFF, 0);
        });

        Camera2D camera = CreateCamera(frame);
        WorldPresentationSnapshot snapshot = BuildSpriteSnapshot(frame, context.FrameId, cave, house);
        IReadOnlyList<OrderedWorldSprite> ordered = spriteProjection.Project(
            snapshot,
            camera.Snapshot(),
            worldScale,
            spriteResources.Get,
            sprite => playback.Resolve(sprite, spriteAtlas.Metadata, spriteResolver));
        LastCamera = camera.Snapshot();
        LastSpriteCount = ordered.Count;
        context.Present(sprites, pass =>
        {
            foreach (OrderedWorldSprite sprite in ordered)
            {
                pass.SubmitQuad(sprite.Submission);
            }
        });

        scale = 48;
        left = 22 - (float)camera.Position.X * scale;
        top = 24 - (float)camera.Position.Y * scale;
        var effectCamera = new EffectCameraTransform(Vector2.Zero, new Vector2(left, top), scale / 1024, 1);
        game.Effects.CopyParticleDrawData(particleSnapshots);
        if (particleSnapshots.Count > 0)
        {
            context.Present(shapes, pass =>
            {
                foreach (ParticleSnapshot particle in particleSnapshots)
                {
                    pass.SubmitAnalyticShape(EffectNativeProjection.Particle(particle, effectCamera));
                }
            });
        }
        game.Effects.CopyQuadDrawData(quadSnapshots);
        IReadOnlyList<NativeSoftShockwaveSubmission> shockwaves = EffectNativeProjection.Shockwaves(quadSnapshots, effectCamera);
        ShaderQuads = shockwaves.Count;
        if (ShaderQuads > 0)
        {
            context.Present(waves, pass =>
            {
                foreach (NativeSoftShockwaveSubmission wave in shockwaves)
                {
                    pass.SubmitSoftShockwave(wave);
                }
            });
        }
        AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private Camera2D CreateCamera(TinyFarmFrame frame)
    {
        var camera = new Camera2D(
            new WorldPoint2(0, 0),
            new PixelRect(22, 24, 904, 648),
            1,
            new WorldRect(0, 0, Math.Max(frame.SceneWidth, 1), Math.Max(frame.SceneHeight, 1)));
        TinyFarmActorView? player = frame.Actors.FirstOrDefault(actor => actor.IsPlayer);
        if (player is not null)
        {
            camera.Follow(
                new WorldPoint2(player.Position.X / 1024.0, player.Position.Y / 1024.0),
                worldScale);
        }
        return camera;
    }

    private WorldPresentationSnapshot BuildSpriteSnapshot(
        TinyFarmFrame frame,
        ulong frameId,
        bool cave,
        bool house)
    {
        TimeSpan elapsed = TimeSpan.FromSeconds(frameId / 60.0);
        var worldSprites = new List<WorldSprite>(frame.SceneWidth * frame.SceneHeight + 32);
        for (int y = 0; y < frame.SceneHeight; y++)
        {
            for (int x = 0; x < frame.SceneWidth; x++)
            {
                worldSprites.Add(Sprite(
                    $"tile-{x:D2}-{y:D2}",
                    TinyFarmAuthoredTileMap.TileAt(x, y, house, cave),
                    new WorldPoint2(x + 0.5, y + 0.5),
                    elapsed,
                    WorldSpriteLayer.Ground,
                    y,
                    Native2DTint.White));
            }
        }

        foreach (TinyFarmSceneObjectView item in frame.SceneObjects ?? [])
        {
            string spriteId = item.Depleted && item.Kind == SceneObjectKind.Tree
                ? "grass-d"
                : SpriteFor(item);
            worldSprites.Add(Sprite(
                "object-" + item.Id.Value,
                spriteId,
                new WorldPoint2(item.Position.X + item.Width / 2.0, item.Position.Y + item.Height),
                elapsed,
                WorldSpriteLayer.World,
                item.Position.Y + item.Height,
                Native2DTint.White));
        }

        foreach (TinyFarmPlotView plot in frame.Plots.Where(plot => plot.Crop is not null))
        {
            double x = plot.Position.X / 1024.0;
            double y = plot.Position.Y / 1024.0;
            worldSprites.Add(Sprite("crop-" + plot.Id.Value, "mint", new WorldPoint2(x, y), elapsed, WorldSpriteLayer.World, y, Native2DTint.White));
        }

        foreach (TinyFarmItemView item in frame.GroundItems)
        {
            double x = item.Position.X / 1024.0;
            double y = item.Position.Y / 1024.0;
            worldSprites.Add(Sprite("ground-item-" + item.Id.Value, "mint", new WorldPoint2(x, y), elapsed, WorldSpriteLayer.World, y, Native2DTint.White));
        }

        foreach (TinyFarmActorView actor in frame.Actors)
        {
            double x = actor.Position.X / 1024.0;
            double y = actor.Position.Y / 1024.0;
            Native2DTint tint = actor.IsPlayer ? Native2DTint.White : new Native2DTint(1, 0.82f, 0.72f, 1);
            worldSprites.Add(Sprite("actor-" + actor.Id.Value, "farmer", new WorldPoint2(x, y), elapsed, WorldSpriteLayer.Actors, y, tint, "walk-down"));
        }

        foreach (TinyFarmEnemyView enemy in frame.Enemies ?? [])
        {
            if (enemy.Lifecycle != EnemyLifecycle.Alive)
            {
                continue;
            }
            double x = enemy.Position.X / 1024.0;
            double y = enemy.Position.Y / 1024.0;
            worldSprites.Add(Sprite("enemy-" + enemy.Id.Value, "mint", new WorldPoint2(x, y), elapsed, WorldSpriteLayer.Actors, y, new Native2DTint(0.65f, 1, 0.72f, 1)));
        }
        return new WorldPresentationSnapshot(worldSprites);
    }

    private WorldSprite Sprite(
        string stableId,
        string spriteId,
        WorldPoint2 anchor,
        TimeSpan elapsed,
        WorldSpriteLayer layer,
        double feetY,
        Native2DTint tint,
        string? clipId = null)
    {
        return new WorldSprite(
            new WorldPresentationId(stableId),
            anchor,
            spriteAtlas.Resource.Id,
            spriteId,
            clipId,
            elapsed,
            Restart: false,
            Scale: 1,
            tint,
            layer,
            feetY);
    }

    private static string SpriteFor(TinyFarmSceneObjectView item)
    {
        return item.Kind switch
        {
            SceneObjectKind.Tree => "tree",
            SceneObjectKind.Forage => "mint",
            SceneObjectKind.CookingStation => "hearth",
            SceneObjectKind.Portal => "lantern",
            SceneObjectKind.Plot => "mint",
            SceneObjectKind.Bed => "hearth",
            _ when item.Id.Value == "well" => "well",
            _ when item.Id.Value == "market-stall" => "market",
            _ when item.Id.Value == "fence" => "fence",
            _ => "wall",
        };
    }

    private void DrawObject(VulkanOrderedQuadRenderer pass, TinyFarmSceneObjectView item, bool cave)
    {
        float x = item.Position.X;
        float y = item.Position.Y;
        switch (item.Kind)
        {
            case SceneObjectKind.Portal:
                Tile(pass, x - .1f, y - .1f, item.Width + .2f, item.Height + .2f, 0xD8CCA0FF, 7);
                Tile(pass, x + .12f, y + .1f, .76f, .76f, 0x385A43FF, 6);
                Tile(pass, x + .42f, y + .2f, .16f, .5f, 0xF2D795FF, 2);
                Tile(pass, x + .3f, y + .47f, .4f, .12f, 0xF2D795FF, 2);
                break;
            case SceneObjectKind.Plot:
                Tile(pass, x, y, 1, 1, 0x72563FFF, 4);
                for (int i = 0; i < 3; i++)
                {
                    Tile(pass, x + .12f, y + .15f + i * .25f, .76f, .08f, 0xA58559FF, 2);
                }
                break;
            case SceneObjectKind.Tree:
                Tile(pass, x + .35f, y + .3f, .3f, .65f, 0x71573FFF, 2);
                if (!item.Depleted)
                {
                    Tile(pass, x - .32f, y - .55f, 1.65f, 1.3f, 0x355C45FF, 18);
                    Tile(pass, x - .16f, y - .7f, 1.3f, .9f, 0x4D7950FF, 14);
                }
                break;
            case SceneObjectKind.Forage:
                Tile(pass, x + .2f, y + .3f, .16f, .4f, 0xE2D5A5FF, 2);
                Tile(pass, x + .05f, y + .1f, .6f, .32f, 0xD3A27AFF, 6);
                Tile(pass, x + .55f, y + .45f, .13f, .3f, 0xE2D5A5FF, 2);
                Tile(pass, x + .42f, y + .33f, .46f, .25f, 0xDBB485FF, 5);
                break;
            case SceneObjectKind.CookingStation:
                Tile(pass, x, y, 1, 1, 0x4D4940FF, 5);
                Tile(pass, x + .14f, y + .45f, .7f, .35f, 0xE6AE62FF, 5);
                Tile(pass, x + .18f, y + .1f, .64f, .24f, 0x242F2BFF, 7);
                break;
            case SceneObjectKind.Enemy:
                break;
            default:
                uint color = item.Id.Value == "river" ? 0x649F9FFF : cave ? 0x243934FF : 0xA18A64FF;
                if (item.Id.Value == "fence")
                {
                    Tile(pass, x + .18f, y, .12f, item.Height, 0xD4BC88FF, 2);
                    Tile(pass, x + .68f, y, .12f, item.Height, 0xD4BC88FF, 2);
                    for (int i = 0; i <= item.Height; i++)
                    {
                        Tile(pass, x + .07f, y + i - .08f, .84f, .17f, 0xE1CCA0FF, 2);
                    }
                }
                else if (item.Id.Value == "well")
                {
                    Tile(pass, x, y, 2, 2, 0x778577FF, 30);
                    Tile(pass, x + .25f, y + .25f, 1.5f, 1.5f, 0x385B59FF, 24);
                    Tile(pass, x + .85f, y - .2f, .2f, 2.4f, 0xC0AA76FF, 3);
                    Tile(pass, x + .3f, y + .4f, .5f, .5f, 0x9CBFB0FF, 8);
                }
                else if (item.Id.Value == "market-stall")
                {
                    Tile(pass, x, y + .7f, item.Width, 1.3f, 0x8E7150FF, 3);
                    for (int i = 0; i < item.Width * 2; i++)
                    {
                        Tile(pass, x + i * .5f, y, .5f, .8f,
                            i % 2 == 0 ? 0xD6C48CFF : 0xAA6F52FF, 3);
                    }
                    Tile(pass, x + .25f, y + 1, .6f, .35f, 0x98B671FF, 4);
                    Tile(pass, x + 1.15f, y + 1, .6f, .35f, 0xD4A46CFF, 4);
                }
                else if (item.Kind == SceneObjectKind.Bed)
                {
                    Tile(pass, x, y, item.Width, item.Height, 0x694F3CFF, 4);
                    Tile(pass, x + .12f, y + .12f, .45f, item.Height - .24f, 0xE4D8ACFF, 4);
                    Tile(pass, x + .65f, y + .1f, item.Width - .8f, item.Height - .2f, 0x79947FFF, 3);
                }
                else if (item.Id.Value == "farmhouse")
                {
                    Tile(pass, x, y, item.Width, item.Height, 0xD3BF8DFF, 4);
                    for (int i = 0; i < 6; i++)
                    {
                        Tile(pass, x - .12f + i * .15f, y - i * .13f, item.Width + .24f - i * .3f, .25f, 0x9B6351FF, 2);
                    }
                    Tile(pass, x + .7f, y + 1.2f, .7f, .7f, 0xEFD894FF, 3);
                    Tile(pass, x + 2.4f, y + 1.2f, .7f, .7f, 0xEFD894FF, 3);
                }
                else
                {
                    Tile(pass, x, y, item.Width, item.Height, color, 5);
                    if (item.Id.Value == "river")
                    {
                        for (int i = 0; i < item.Height; i++)
                        {
                            Tile(pass, x + .4f + i % 3, y + i + .3f, 1.4f, .07f, 0xAED2B6FF, 2);
                        }
                    }
                }
                break;
        }
    }

    private void DrawActor(VulkanOrderedQuadRenderer pass, TinyFarmActorView actor)
    {
        float x = actor.Position.X / 1024f;
        float y = actor.Position.Y / 1024f;
        Tile(pass, x - .35f, y - .1f, .7f, .24f, 0x243D3470, 8);
        Tile(pass, x - .23f, y - .6f, .46f, .55f, actor.IsPlayer ? 0x426B80FF : 0xB76D54FF, 5);
        Tile(pass, x - .23f, y - 1.03f, .46f, .48f, 0xEDD0A0FF, 6);
        Tile(pass, x - .32f, y - 1.12f, .64f, .22f, actor.IsPlayer ? 0xD9B46FFF : 0x63513DFF, 5);
        float eye = actor.Facing == ActorFacing.Left ? -.16f : .09f;
        Tile(pass, x + eye, y - .87f, .08f, .1f, 0x243D34FF, 1);
        Tile(pass, x - .2f, y - .08f, .15f, .15f, 0x384039FF, 2);
        Tile(pass, x + .07f, y - .08f, .15f, .15f, 0x384039FF, 2);
        if (actor.IsPlayer && game.State.SelectedHotbarSlot == 4)
        {
            Tile(pass, x + .3f, y - .76f, .1f, .63f, 0xDDE9D8FF, 1);
            Tile(pass, x + .2f, y - .27f, .3f, .1f, 0xDBB971FF, 1);
        }
    }

    private void Tile(VulkanOrderedQuadRenderer pass, float x, float y, float width, float height, uint color, float radius)
    {
        Rect(pass, left + x * scale, top + y * scale, width * scale, height * scale, color, radius);
    }

    private static void Rect(VulkanOrderedQuadRenderer pass, float x, float y, float width, float height, uint color, float radius)
    {
        var tint = new Native2DTint((color >> 24) / 255f, ((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f);
        pass.SubmitAnalyticShape(new NativeAnalyticShapeSubmission(new Native2DRect(x, y, width, height),
            new Native2DSize(width, height), Native2DUvRect.Full, NativeAnalyticShapeKind.RoundedRect,
            tint, Math.Min(radius, Math.Min(width, height) / 2), tint, 0));
    }

    public void Detach()
    {
        spriteResources?.Dispose();
        sprites?.Dispose();
        shapes?.Dispose();
        waves?.Dispose();
    }
}

internal sealed class SupperOverlay(
    LayerId layer,
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram analyticProgram,
    CompiledGraphicsProgram msdfProgram,
    SupperNativeUiFont font,
    Func<SupperUiResources> presentation) : INativeLayerPresenter
{
    private VulkanOrderedQuadRenderer shapes = null!;
    private VulkanOrderedQuadRenderer text = null!;
    private AurelianMsdfAtlasCache atlasCache = null!;
    private MachinaPresentationFrame? baseSource;
    private MachinaPresentationFrame? clockSource;
    private MachinaPresentationFrame? promptSource;
    private SupperNativeUiSegments baseSegments = SupperNativeUiSegments.Empty;
    private SupperNativeUiSegments clockSegments = SupperNativeUiSegments.Empty;
    private SupperNativeUiSegments promptSegments = SupperNativeUiSegments.Empty;
    public LayerId Layer => layer;
    public long AllocatedBytes { get; private set; }
    public int NativePrimitiveCount =>
        baseSegments.NativePrimitiveCount + clockSegments.NativePrimitiveCount + promptSegments.NativePrimitiveCount;
    public int FallbackRasterPrimitiveCount =>
        baseSegments.FallbackCount + clockSegments.FallbackCount + promptSegments.FallbackCount;
    public int FontAtlasUploads => atlasCache?.UploadCount ?? 0;
    public int TextGeometryCacheEntries => font.CachedTextRunCount;
    public int GeometryRebuilds { get; private set; }

    public void ResetPerformanceMetrics()
    {
        AllocatedBytes = 0;
    }

    public void Attach(VulkanNativeFrameTarget target)
    {
        shapes = new VulkanOrderedQuadRenderer(plant, analyticProgram, target, Native2DPipelineOptions.AnalyticShape2D);
        text = new VulkanOrderedQuadRenderer(plant, msdfProgram, target, Native2DPipelineOptions.MsdfText);
        atlasCache = new AurelianMsdfAtlasCache(text);
        foreach (AurelianMsdfAtlasResource resource in font.Resources)
        {
            atlasCache.Resolve(resource);
        }
        WarmCurrentPresentation();
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        Detach();
        Attach(target);
        baseSource = null;
        clockSource = null;
        promptSource = null;
    }

    public void Present(NativeLayerFrameContext context)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        SupperUiResources current = presentation();
        UpdateRealization(current);

        PresentSegment(context, baseSegments.Base);
        PresentSegment(context, baseSegments.Overlay);
        PresentSegment(context, clockSegments.Base);
        PresentSegment(context, promptSegments.Base);
        AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private void WarmCurrentPresentation()
    {
        UpdateRealization(presentation());
    }

    private void UpdateRealization(SupperUiResources current)
    {
        if (!ReferenceEquals(baseSource, current.Base))
        {
            baseSource = current.Base;
            baseSegments = Realize(current.Base, 0, 0, splitOverlay: true);
        }
        if (!ReferenceEquals(clockSource, current.Clock))
        {
            clockSource = current.Clock;
            clockSegments = Realize(current.Clock, 835, 44, splitOverlay: false);
        }
        if (!ReferenceEquals(promptSource, current.Prompt))
        {
            promptSource = current.Prompt;
            promptSegments = current.Prompt is null
                ? SupperNativeUiSegments.Empty
                : Realize(current.Prompt, 125, 528, splitOverlay: false);
        }
    }

    private SupperNativeUiSegments Realize(
        MachinaPresentationFrame frame,
        float offsetX,
        float offsetY,
        bool splitOverlay)
    {
        GeometryRebuilds++;
        var baseShapes = new List<NativeAnalyticShapeSubmission>();
        var baseText = new List<NativeMsdfQuadSubmission>();
        var overlayShapes = new List<NativeAnalyticShapeSubmission>();
        var overlayText = new List<NativeMsdfQuadSubmission>();
        var clips = new Stack<Rect>();
        int fallbackCount = 0;

        foreach (MachinaPresentationOperation operation in frame.Operations)
        {
            if (operation is PushRectangularClipOperation push)
            {
                Rect translated = Offset(push.Rect, offsetX, offsetY);
                clips.Push(clips.Count == 0 ? translated : Intersect(clips.Peek(), translated));
                continue;
            }
            if (operation is PopClipOperation)
            {
                clips.Pop();
                continue;
            }

            bool overlay = splitOverlay && IsOverlay(operation);
            List<NativeAnalyticShapeSubmission> shapeTarget = overlay ? overlayShapes : baseShapes;
            List<NativeMsdfQuadSubmission> textTarget = overlay ? overlayText : baseText;
            Rect? clip = clips.Count == 0 ? null : clips.Peek();

            MachinaAnalyticShapePrimitive? shape = operation switch
            {
                MachinaAnalyticShapePrimitive analytic => Offset(analytic, offsetX, offsetY),
                FillRectangleOperation fill => new MachinaAnalyticShapePrimitive(
                    fill.SourceId,
                    MachinaAnalyticShapeKind.RoundedRect,
                    Offset(fill.Rect, offsetX, offsetY),
                    fill.Color),
                StrokeRectangleOperation stroke => new MachinaAnalyticShapePrimitive(
                    stroke.SourceId,
                    MachinaAnalyticShapeKind.RoundedRect,
                    Offset(stroke.Rect, offsetX, offsetY),
                    ColorToken.Hex(0x00000000),
                    borderColor: stroke.Color,
                    borderWidth: stroke.Thickness),
                _ => null,
            };
            if (shape is not null)
            {
                NativeAnalyticShapeSubmission? submission = AurelianAnalyticShapePresentationAdapter.Adapt(shape, clip);
                if (submission.HasValue)
                {
                    shapeTarget.Add(submission.Value);
                }
                continue;
            }

            if (operation is PositionedTextOperation sourceText)
            {
                PositionedTextOperation positioned = Offset(font.Qualify(sourceText), offsetX, offsetY);
                AurelianMsdfAtlasResource atlas = font.ResourceFor(positioned);
                AurelianMsdfTextPresentationAdapter.AdaptInto(positioned, atlas, atlasCache, textTarget, clip);
                continue;
            }

            fallbackCount++;
        }

        return new SupperNativeUiSegments(
            new SupperNativeUiSegment(baseShapes.ToArray(), baseText.ToArray()),
            new SupperNativeUiSegment(overlayShapes.ToArray(), overlayText.ToArray()),
            fallbackCount);
    }

    private void PresentSegment(NativeLayerFrameContext context, SupperNativeUiSegment segment)
    {
        if (segment.Shapes.Length > 0)
        {
            context.Present(shapes, pass =>
            {
                foreach (NativeAnalyticShapeSubmission submission in segment.Shapes)
                {
                    pass.SubmitAnalyticShape(submission);
                }
            });
        }
        if (segment.Text.Length > 0)
        {
            context.Present(text, pass =>
            {
                foreach (NativeMsdfQuadSubmission submission in segment.Text)
                {
                    pass.SubmitMsdfQuad(submission);
                }
            });
        }
    }

    private static bool IsOverlay(MachinaPresentationOperation operation)
    {
        string? sourceId = operation switch
        {
            MachinaAnalyticShapePrimitive shape => shape.SourceId,
            FillRectangleOperation fill => fill.SourceId,
            StrokeRectangleOperation stroke => stroke.SourceId,
            PositionedTextOperation positionedText => positionedText.SourceId,
            _ => null,
        };
        return sourceId is not null
            && (sourceId.StartsWith("dialogue", StringComparison.Ordinal)
                || sourceId.StartsWith("speaker", StringComparison.Ordinal)
                || sourceId.StartsWith("choice-", StringComparison.Ordinal)
                || sourceId.StartsWith("modal", StringComparison.Ordinal));
    }

    private static MachinaAnalyticShapePrimitive Offset(
        MachinaAnalyticShapePrimitive source,
        float offsetX,
        float offsetY)
    {
        return new MachinaAnalyticShapePrimitive(
            source.SourceId,
            source.Kind,
            Offset(source.DestinationRect, offsetX, offsetY),
            source.FillColor,
            source.Radius,
            source.BorderColor,
            source.BorderWidth);
    }

    private static PositionedTextOperation Offset(PositionedTextOperation source, float offsetX, float offsetY)
    {
        return new PositionedTextOperation(
            source.SourceId,
            Offset(source.Rect, offsetX, offsetY),
            source.Text,
            source.Style,
            source.Color,
            source.Primitive);
    }

    private static Rect Offset(Rect source, float offsetX, float offsetY)
    {
        return new Rect(source.X + offsetX, source.Y + offsetY, source.Width, source.Height);
    }

    private static Rect Intersect(Rect left, Rect right)
    {
        double x = Math.Max(left.X, right.X);
        double y = Math.Max(left.Y, right.Y);
        double rightEdge = Math.Min(left.X + left.Width, right.X + right.Width);
        double bottomEdge = Math.Min(left.Y + left.Height, right.Y + right.Height);
        return new Rect(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottomEdge - y));
    }

    public void Detach()
    {
        atlasCache?.Dispose();
        text?.Dispose();
        shapes?.Dispose();
    }
}

internal sealed record SupperNativeUiSegments(
    SupperNativeUiSegment Base,
    SupperNativeUiSegment Overlay,
    int FallbackCount)
{
    public static SupperNativeUiSegments Empty { get; } = new(
        SupperNativeUiSegment.Empty,
        SupperNativeUiSegment.Empty,
        0);

    public int NativePrimitiveCount => Base.NativePrimitiveCount + Overlay.NativePrimitiveCount;
}

internal sealed record SupperNativeUiSegment(
    NativeAnalyticShapeSubmission[] Shapes,
    NativeMsdfQuadSubmission[] Text)
{
    public static SupperNativeUiSegment Empty { get; } = new([], []);

    public int NativePrimitiveCount => Shapes.Length + Text.Length;
}

internal sealed class SupperLayer(LayerId id, int order) : IAurelianLayer
{
    public LayerDescriptor Describe() => new(id, order, true, new LayerViewport(0, 0, 1280, 720), LayerPresentationMode.DirectHostPass, LayerInputPolicy.None);
    public void Attach(LayerSurfaceDescriptor surface) { }
    public void Resize(LayerSurfaceDescriptor surface) { }
    public void Update(LayerUpdateContext context) { }
    public LayerPresentationDto Present(LayerPresentationContext context) => new(id, Describe().Viewport, true, context.Surface.Kind, id.Value);
    public LayerInputResult HandleInput(LayerInputEvent input) => LayerInputResult.Unconsumed;
    public void Detach() { }
}
