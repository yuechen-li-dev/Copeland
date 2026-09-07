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
using Aurelian.Profile.Graphics;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Spatial2D;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Profiles;
using Copeland.Profile;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using TinyFarm.Core;
using TinyFarm.InputMan;
using Silk.NET.Windowing;

namespace TinyFarm.Native;

internal sealed class TinyFarmNativeRenderer : IAurelianHostCompositor
{
    private readonly AurelianVulkanPlant plant;
    private AurelianVulkanSurface surface;
    private AurelianVulkanSwapchain swapchain;
    private readonly NativeLayerCompositor compositor;
    private VulkanNativeSwapchainPresenter swapchainPresenter;
    private readonly TinyFarmNativeWindow window;
    private readonly bool proof;
    private readonly bool vSync;
    private readonly string captureDirectory;
    public TinyFarmPresentationLayout Layout { get; private set; }
    public uint? WorldBackdropOverride { get => world.BackdropOverride; set => world.BackdropOverride = value; }
    public double TreeScale { get => world.TreeScale; set => world.TreeScale = value; }
    public double FarmhouseScale { get => world.FarmhouseScale; set => world.FarmhouseScale = value; }
    private readonly TinyFarmGame game;
    private readonly TinyFarmNativeUi ui;
    private readonly TinyFarmWorldPresenter world;
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

    public TinyFarmNativeRenderer(
        string root,
        TinyFarmGame game,
        TinyFarmNativeWindow window,
        bool proof,
        bool vSync = true, bool legacy = false)
    {
        this.game = game;
        this.window = window;
        this.proof = proof;
        this.vSync = vSync;
        Layout = new TinyFarmPresentationLayout(window.SurfaceSize.Width, window.SurfaceSize.Height, legacy);
        captureDirectory = Path.Combine(root, "artifacts", "tinyfarm-captures");
        ui = new TinyFarmNativeUi(game);
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
            new VulkanSwapchainCreateOptions((uint)Layout.Width, (uint)Layout.Height, VSync: vSync, "TinyFarm - A Little Mint of Kindness", Visible: !proof));
        if (!swapchainResult.Success || swapchainResult.Surface is null || swapchainResult.Swapchain is null)
        {
            throw new InvalidOperationException(string.Join("; ", swapchainResult.Diagnostics.Select(item => item.Message)));
        }
        surface = swapchainResult.Surface;
        swapchain = swapchainResult.Swapchain;
        VulkanTextureFormat targetFormat = ParseSwapchainFormat(swapchain.Facts.SelectedFormat);
        Layout = Layout with { Width = (int)swapchain.Facts.Width, Height = (int)swapchain.Facts.Height };
        compositor = new NativeLayerCompositor(plant, Layout.Width, Layout.Height, format: targetFormat);
        CompiledGraphicsProgram analytic = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts");
        CompiledGraphicsProgram shockwave = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/SoftShockwave.v.ts");
        CompiledGraphicsProgram msdf = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts");
        CompiledGraphicsProgram profileMsdf = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/ProfileMsdf.v.ts");
        CompiledGraphicsProgram texture = Compile(root, "samples/Aurelian/ForwardTexturedM3.v.ts");
        CompiledGraphicsProgram field = Compile(root, legacy
            ? "src/Aurelian/Aurelian.Shaders/Assets/ReactiveFluid2D.v.ts"
            : "src/TinyFarm/TinyFarm.Native/Assets/M25/PainterlyWater.v.ts");
        string spriteAtlasPath = Path.Combine(AppContext.BaseDirectory, "Assets", "M11", "tinyfarm-sprite-atlas-source.png");
        TinyFarmSpriteAtlas spriteAtlas = TinyFarmSpriteAtlas.Load(spriteAtlasPath);
        string m24AssetDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "M24");
        TinyFarmM24Assets m24Assets = TinyFarmM24Assets.Load(m24AssetDirectory, legacy);
        string profilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "M19", "mossward-tree.profile.tsx");
        ProfileCompositionCompilationResult profileCompilation = ProfileTsxCompiler.CompileComposition(
            File.ReadAllText(profilePath),
            profilePath);
        if (!profileCompilation.Success)
        {
            throw new InvalidOperationException(string.Join("; ", profileCompilation.Diagnostics.Select(static item => item.Message)));
        }
        ProfileNativeCompileResult profileNative = ProfileNativeCompiler.Compile(profileCompilation.Composition!, profilePath);
        if (!profileNative.Success)
        {
            throw new InvalidOperationException(string.Join("; ", profileNative.Diagnostics.Select(static item => item.Message)));
        }
        world = new TinyFarmWorldPresenter(
            new LayerId("farm-world"),
            plant,
            analytic,
            shockwave,
            texture,
            field,
            spriteAtlas,
            m24Assets,
            game,
            () => frame,
            () => Layout);
        if (legacy)
        {
            world.TreeScale = 1;
            world.FarmhouseScale = 1;
        }
        compositor.Add(new TinyFarmNativeLayer(world.Layer, 0), world);
        string portraitPath = Path.Combine(AppContext.BaseDirectory, "Assets", "mara-dialogue.png");
        if (File.Exists(portraitPath))
        {
            var portrait = new TinyFarmNativePortrait(plant, texture, game, portraitPath);
            compositor.Add(new TinyFarmNativeLayer(portrait.Layer, 50), portrait);
        }
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "SpaceMono-Regular.ttf");
        TinyFarmNativeUiFont font = TinyFarmNativeUiFont.Create(fontPath);
        Overlay = new TinyFarmNativeOverlay(
            new LayerId("machina-hud"),
            plant,
            analytic,
            msdf,
            profileMsdf,
            font,
            profileNative.Resource!,
            () => ui.Resources(frame),
            game,
            () => Layout,
            () => world.LastCamera);
        compositor.Add(new TinyFarmNativeLayer(Overlay.Layer, 100), Overlay);
        compositor.Attach();
        compositor.RunFrame(0, TimeSpan.Zero);
        swapchainPresenter = new VulkanNativeSwapchainPresenter(plant, compositor.Target, swapchain);
    }

    public object TextureFacts => world.TextureFacts;
    public string Device { get; }
    public NativeLayerFrameResult? Last { get; private set; }
    public int UiRebuilds => ui.Rebuilds;
    public int ShaderQuads => world.ShaderQuads;
    private TinyFarmNativeOverlay Overlay { get; }
    public long WorldAllocatedBytes => world.AllocatedBytes;
    public long SnapshotAllocatedBytes => world.SnapshotAllocatedBytes;
    public long SpriteProjectionAllocatedBytes => world.SpriteProjectionAllocatedBytes;
    public long NativeSubmissionAllocatedBytes => world.NativeSubmissionAllocatedBytes;
    public long OverlayAllocatedBytes => Overlay.AllocatedBytes;
    public int DynamicUiTextureUploads => 0;
    public int NativeUiPrimitiveCount => Overlay.NativePrimitiveCount;
    public int FallbackRasterPrimitiveCount => Overlay.FallbackRasterPrimitiveCount;
    public long FallbackRasterBytes => 0;
    public int FallbackRasterUploads => 0;
    public int FontAtlasUploads => Overlay.FontAtlasUploads;
    public int TextGeometryCacheEntries => Overlay.TextGeometryCacheEntries;
    public int TextGeometryCacheCapacity => Overlay.TextGeometryCacheCapacity;
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
    public int FieldTextureUploads => world.FieldTextureUploads;
    public long FieldUploadBytes => world.FieldUploadBytes;
    public double FieldProjectionMilliseconds => world.FieldProjectionMilliseconds;
    public string SpriteAtlasHash => world.SpriteAtlasHash;
    public SpriteAlphaCleanupFacts SpriteAlphaCleanup => world.SpriteAlphaCleanup;
    public IReadOnlyDictionary<string, string> M24AssetHashes => world.M24AssetHashes;

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

    public void Resize(HostSurfaceSize size)
    {
        if (size.Width <= 0 || size.Height <= 0 || size.Width == Layout.Width && size.Height == Layout.Height)
        {
            return;
        }
        // All native submissions finish synchronously. Retarget retained resources only after presentation stops.
        swapchainPresenter.Dispose();
        swapchain.Dispose();
        surface.Dispose();
        VulkanSwapchainCreateResult replacement = VulkanSwapchainFactory.Create(plant, window.NativeWindow,
            new VulkanSwapchainCreateOptions((uint)size.Width, (uint)size.Height, VSync: vSync, Visible: !proof));
        if (!replacement.Success || replacement.Surface is null || replacement.Swapchain is null)
        {
            throw new InvalidOperationException(string.Join("; ", replacement.Diagnostics.Select(item => item.Message)));
        }
        surface = replacement.Surface;
        swapchain = replacement.Swapchain;
        Layout = Layout with { Width = (int)swapchain.Facts.Width, Height = (int)swapchain.Facts.Height };
        compositor.Resize(Layout.Width, Layout.Height);
        swapchainPresenter = new VulkanNativeSwapchainPresenter(plant, compositor.Target, swapchain);
    }

    public void Present(AurelianHostFrame hostFrame)
    {
        if (window.SurfaceSize.Width <= 0 || window.SurfaceSize.Height <= 0)
        {
            return;
        }
        UpdateInspection();
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        long timeStart = Stopwatch.GetTimestamp();
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        long projectionEnd = Stopwatch.GetTimestamp();
        long projectionAllocationEnd = GC.GetAllocatedBytesForCurrentThread();
        bool cleanCapture = game.Presentation.CleanCaptureRequested;
        game.Presentation.CleanCaptureRequested = false;
        bool hudBefore = game.Presentation.HudVisible;
        bool inspectorBefore = game.Presentation.InspectorVisible;
        if (cleanCapture)
        {
            game.Presentation.HudVisible = false;
            game.Presentation.InspectorVisible = false;
        }
        bool capture = captureNextFrame || cleanCapture;
        captureNextFrame = false;
        try
        {
            Last = compositor.RunFrame(hostFrame.Sequence, hostFrame.Elapsed, captureReadback: capture);
        }
        finally
        {
            game.Presentation.HudVisible = hudBefore;
            game.Presentation.InspectorVisible = inspectorBefore;
        }
        if (cleanCapture)
        {
            Directory.CreateDirectory(captureDirectory);
            string path = Path.Combine(captureDirectory, $"tinyfarm-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png");
            PngWriter.Write(path, Layout.Width, Layout.Height, Last.NativeFrame.Pixels!);
        }
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

    private void UpdateInspection()
    {
        var current = game.LiveInspection.Presentation;
        if (current is not null && current.Width == Layout.Width && current.Height == Layout.Height
            && current.HudVisible == game.Presentation.HudVisible
            && current.InspectorVisible == game.Presentation.InspectorVisible
            && current.TreeScale == TreeScale && current.FarmhouseScale == FarmhouseScale)
        {
            return;
        }
        double treePixels = 178 * TreeScale * Layout.WorldScale / 48;
        double housePixels = (Layout.Legacy ? 238 : TinyFarmPainterlyPolicy.FarmhouseHeightAt48PixelsPerMetre) * FarmhouseScale * Layout.WorldScale / 48;
        game.LiveInspection.Presentation = new TinyFarm.Oblivion.TinyFarmPresentationInspection(
            Layout.Width, Layout.Height, Layout.WorldScale, game.Presentation.HudVisible,
            game.Presentation.InspectorVisible, TreeScale, FarmhouseScale,
            world.SamplerDescription,
            $"{world.TreeSourceHeight}px tree / {treePixels:0.0}px = {world.TreeSourceHeight / treePixels:0.00} source/display axis; "
                + $"{world.FarmhouseSourceHeight}px house / {housePixels:0.0}px = {world.FarmhouseSourceHeight / housePixels:0.00}",
            $"artifacts/tinyfarm-high-fidelity-presentation-m25/world-only-{Layout.Height}p-after.png",
            $"artifacts/tinyfarm-high-fidelity-presentation-m25/ui-on-{Layout.Height}p-after.png");
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

internal sealed class TinyFarmWorldPresenter(
    LayerId layer,
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram analytic,
    CompiledGraphicsProgram shockwave,
    CompiledGraphicsProgram textured,
    CompiledGraphicsProgram fieldProgram,
    TinyFarmSpriteAtlas spriteAtlas,
    TinyFarmM24Assets m24Assets,
    TinyFarmGame game,
    Func<TinyFarmFrame> getFrame,
    Func<TinyFarmPresentationLayout> getLayout) : INativeLayerPresenter
{
    private VulkanOrderedQuadRenderer shapes = null!;
    private VulkanOrderedQuadRenderer waves = null!;
    private VulkanOrderedQuadRenderer sprites = null!;
    private VulkanOrderedQuadRenderer painterly = null!;
    private NativeSpriteResourceScope painterlyResources = null!;
    private readonly HashSet<Native2DTextureHandle> linearTextures = [];
    public uint? BackdropOverride { get; set; }
    public double TreeScale { get; set; } = TinyFarmPainterlyPolicy.TreeVisualScale;
    public double FarmhouseScale { get; set; } = TinyFarmPainterlyPolicy.FarmhouseVisualScale;
    private VulkanOrderedQuadRenderer field = null!;
    private Native2DTextureHandle fieldTexture;
    private long projectedFieldGeneration = -1;
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
    private readonly List<WorldSprite> worldSpriteScratch = new(512);
    private readonly List<PreparedWorldSprite> orderedSpriteScratch = new(512);
    private readonly List<WorldSprite> playerSpriteScratch = new(1);
    private readonly List<PreparedWorldSprite> playerProjectionScratch = new(1);
    private readonly List<WorldSprite> staticTileSprites = new(512);
    private readonly SemanticWorldScene m24Scene = TinyFarmSemanticSpatialScene.Create();
    private int staticTileWidth = -1;
    private int staticTileHeight = -1;
    private bool staticTileCave;
    private bool staticTileHouse;
    private long fieldProjectionTicks;
    public double FieldProjectionMilliseconds => Stopwatch.GetElapsedTime(0, fieldProjectionTicks).TotalMilliseconds;
    private long snapshotAllocatedBytes;
    private long spriteProjectionAllocatedBytes;
    private long nativeSubmissionAllocatedBytes;
    public LayerId Layer => layer;
    public int ShaderQuads { get; private set; }
    public long AllocatedBytes { get; private set; }
    public int LastSpriteCount { get; private set; }
    public Camera2DSnapshot? LastCamera { get; private set; }
    public string SamplerDescription => $"Meadow {m24Assets.Meadow.Sampling}; house {m24Assets.Farmhouse.Sampling}; tree {m24Assets.Tree.Sampling}; pixel atlas {spriteAtlas.Resource.Sampling}; text MSDF";
    public object TextureFacts => new
    {
        painterly = m24Assets.Resources.Select(resource => new
        {
            id = resource.Id.Value, resource.Width, resource.Height, resource.ContentHash,
            sampling = resource.Sampling.ToString(), rgbaBytes = resource.Rgba8.Length,
        }).ToArray(),
        pixelAtlas = new { spriteAtlas.Resource.Width, spriteAtlas.Resource.Height,
            sampling = spriteAtlas.Resource.Sampling.ToString(), rgbaBytes = spriteAtlas.Resource.Rgba8.Length },
        field = new { width = game.Host.Session.Field.Definition.Width + FieldPadding * 2,
            height = game.Host.Session.Field.Definition.Height + FieldPadding * 2, sampling = "Linear UNORM" },
        SpriteTextureUploads, FieldTextureUploads, FieldUploadBytes,
    };
    public uint TreeSourceHeight => m24Assets.Tree.Height;
    public uint FarmhouseSourceHeight => m24Assets.Farmhouse.Height;
    public int SpriteTextureUploads => (spriteResources?.TextureUploads ?? 0) + (painterlyResources?.TextureUploads ?? 0);
    public string SpriteAtlasHash => spriteAtlas.Resource.ContentHash;
    public SpriteAlphaCleanupFacts SpriteAlphaCleanup => spriteAtlas.AlphaCleanup;
    public IReadOnlyDictionary<string, string> M24AssetHashes { get; } = new Dictionary<string, string>
    {
        ["meadow-slab.png"] = m24Assets.Meadow.ContentHash,
        [m24Assets.Farmhouse.Id.Value] = m24Assets.Farmhouse.ContentHash,
        ["tree.png"] = m24Assets.Tree.ContentHash,
    };
    public int FieldTextureUploads { get; private set; }
    public long FieldUploadBytes { get; private set; }
    public long SnapshotAllocatedBytes => snapshotAllocatedBytes;
    public long SpriteProjectionAllocatedBytes => spriteProjectionAllocatedBytes;
    public long NativeSubmissionAllocatedBytes => nativeSubmissionAllocatedBytes;

    public void ResetPerformanceMetrics()
    {
        AllocatedBytes = 0;
        fieldProjectionTicks = 0;
        snapshotAllocatedBytes = 0;
        spriteProjectionAllocatedBytes = 0;
        nativeSubmissionAllocatedBytes = 0;
    }

    public void Attach(VulkanNativeFrameTarget target)
    {
        shapes = new VulkanOrderedQuadRenderer(plant, analytic, target, Native2DPipelineOptions.AnalyticShape2D);
        waves = new VulkanOrderedQuadRenderer(plant, shockwave, target, Native2DPipelineOptions.SoftShockwave);
        sprites = new VulkanOrderedQuadRenderer(plant, textured, target, Native2DPipelineOptions.SpriteNearest);
        field = new VulkanOrderedQuadRenderer(
            plant,
            fieldProgram,
            target,
            new Native2DPipelineOptions(
                Native2DPipelineKind.Textured,
                EnableStraightAlphaBlend: true,
                EnableLinearFiltering: true,
                InputsAreSrgb: false));
        painterly = new VulkanOrderedQuadRenderer(plant, textured, target, Native2DPipelineOptions.SpriteLinear);
        painterlyResources = new NativeSpriteResourceScope(painterly, SpriteSampling.Linear);
        spriteResources = new NativeSpriteResourceScope(sprites, SpriteSampling.Nearest);
        spriteResources.Resolve(spriteAtlas.Resource);
        foreach (SpriteAtlasResource resource in m24Assets.Resources)
        {
            Native2DTextureHandle texture = ResourceScope(resource).Resolve(resource);
            if (resource.Sampling == SpriteSampling.Linear)
            {
                linearTextures.Add(texture);
            }
        }
        byte[] pixels = ProjectFieldPixels(game.Host.Session.Field);
        TinyFarmFieldDefinition definition = game.Host.Session.Field.Definition;
        fieldTexture = field.CreateTexture((uint)(definition.Width + FieldPadding * 2), (uint)(definition.Height + FieldPadding * 2), pixels);
        projectedFieldGeneration = game.Host.Session.Field.ProjectionGeneration;
        FieldTextureUploads++;
        FieldUploadBytes += pixels.Length;
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        shapes.Retarget(target);
        waves.Retarget(target);
        sprites.Retarget(target);
        painterly.Retarget(target);
        field.Retarget(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        TinyFarmFrame frame = getFrame();
        bool cave = frame.ActiveScene == TinyFarmSceneIds.DungeonEntrance;
        bool house = frame.ActiveScene == TinyFarmSceneIds.Residence || frame.ActiveScene == TinyFarmSceneIds.GeneralStore;
        context.Present(shapes, pass =>
        {
            Rect(pass, 0, 0, context.TargetWidth, context.TargetHeight, 0x426B3FFF, 0);
        });

        Camera2D camera = CreateCamera(frame);
        scale = getLayout().WorldScale;
        left = (float)camera.Viewport.X - (float)camera.Position.X * scale;
        top = (float)camera.Viewport.Y - (float)camera.Position.Y * scale;
        long snapshotAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        WorldPresentationSnapshot snapshot = BuildSpriteSnapshot(frame, context.FrameId, cave, house);
        snapshotAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - snapshotAllocationStart;
        long spriteProjectionAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        spriteProjection.ProjectPreparedInto(
            snapshot,
            camera.Snapshot(),
            worldScale,
            ResolveTexture,
            ResolveFrame,
            orderedSpriteScratch);
        spriteProjectionAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - spriteProjectionAllocationStart;
        long nativeSubmissionAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<PreparedWorldSprite> ordered = orderedSpriteScratch;
        LastCamera = camera.Snapshot();
        LastSpriteCount = ordered.Count;
        bool showField = frame.ActiveScene == game.Host.Session.Field.Definition.Scene;
        if (showField)
        {
            PresentM24Ground(context);
            PresentOrdered(context, ordered, ground: true);
            PresentField(context, frame);
            PresentM24Bridge(context);
            PresentOrdered(context, ordered, ground: false);
        }
        else
        {
            PresentOrdered(context, ordered, ground: null);
        }

        if (!getLayout().Legacy)
        {
            PresentOccludedPlayer(context, snapshot, camera.Snapshot());
        }
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
        nativeSubmissionAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - nativeSubmissionAllocationStart;
        AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private void PresentOccludedPlayer(NativeLayerFrameContext context, WorldPresentationSnapshot snapshot, Camera2DSnapshot camera)
    {
        playerSpriteScratch.Clear();
        foreach (WorldSprite sprite in snapshot.Sprites)
        {
            if (sprite.StableId.Value == "actor-" + TinyFarmIds.Player.Value)
            {
                playerSpriteScratch.Add(sprite);
                break;
            }
        }
        if (playerSpriteScratch.Count == 0)
        {
            return;
        }
        spriteProjection.ProjectPreparedInto(new WorldPresentationSnapshot(playerSpriteScratch), camera,
            worldScale, ResolveTexture, ResolveFrame, playerProjectionScratch);
        NativeQuadSubmission player = playerProjectionScratch[0].Submission;
        Native2DRect bounds = player.Destination;
        foreach (PreparedWorldSprite sprite in orderedSpriteScratch)
        {
            Native2DRect cover = sprite.Submission.Destination;
            if (linearTextures.Contains(sprite.Submission.Texture)
                && cover.Y + cover.Height > bounds.Y + bounds.Height
                && cover.X < bounds.X + bounds.Width && cover.X + cover.Width > bounds.X
                && cover.Y < bounds.Y + bounds.Height)
            {
                // A restrained translucent silhouette makes the player locatable through foreground art.
                context.Present(sprites, pass => pass.SubmitQuad(player with
                {
                    Tint = new Native2DTint(1, 1, 0.85f, 0.65f),
                }));
                break;
            }
        }
    }

    private NativeSpriteResourceScope ResourceScope(SpriteAtlasResource resource)
    {
        return resource.Sampling == SpriteSampling.Linear ? painterlyResources : spriteResources;
    }

    private Native2DTextureHandle ResolveTexture(SpriteAssetId id)
    {
        if (id == spriteAtlas.Resource.Id)
        {
            return spriteResources.Get(id);
        }
        SpriteAtlasResource resource = m24Assets.Resources.Single(item => item.Id == id);
        return ResourceScope(resource).Get(id);
    }

    private VulkanOrderedQuadRenderer RendererFor(Native2DTextureHandle texture)
    {
        return linearTextures.Contains(texture) ? painterly : sprites;
    }

    private void PresentOrdered(NativeLayerFrameContext context, IReadOnlyList<PreparedWorldSprite> ordered, bool? ground)
    {
        // Contiguous sampler runs preserve mixed-resource feet-Y order exactly.
        int index = 0;
        while (index < ordered.Count)
        {
            PreparedWorldSprite first = ordered[index];
            bool isGround = first.Layer == WorldSpriteLayer.Ground;
            if (ground.HasValue && ground.Value != isGround)
            {
                index++;
                continue;
            }
            VulkanOrderedQuadRenderer renderer = RendererFor(first.Submission.Texture);
            context.Present(renderer, pass =>
            {
                while (index < ordered.Count)
                {
                    PreparedWorldSprite sprite = ordered[index];
                    if (RendererFor(sprite.Submission.Texture) != renderer
                        || ground.HasValue && ground.Value != (sprite.Layer == WorldSpriteLayer.Ground))
                    {
                        break;
                    }
                    pass.SubmitQuad(sprite.Submission);
                    index++;
                }
            });
        }
    }

    private Camera2D CreateCamera(TinyFarmFrame frame)
    {
        var camera = new Camera2D(
            new WorldPoint2(0, 0),
            getLayout().WorldViewport,
            getLayout().WorldScale / 48.0,
            new WorldRect(0, 0, Math.Max(frame.SceneWidth, 1), Math.Max(frame.SceneHeight, 1)));
        TinyFarmActorView? player = frame.Actors.FirstOrDefault(actor => actor.IsPlayer);
        if (player is not null)
        {
            camera.Follow(
                new WorldPoint2(player.Position.X / 1024.0, player.Position.Y / 1024.0),
                worldScale);
        }
        if (!getLayout().Legacy && frame.ActiveScene == TinyFarmSceneIds.Riverside)
        {
            // Presentation bounds include canopy height; semantic ground remains [0,16] x [0,10].
            camera.SetBounds(new WorldRect(0, -3, 16, 13), worldScale);
            camera.SnapTo(new WorldPoint2(0, -3), worldScale);
        }
        return camera;
    }

    private int FieldPadding => getLayout().Legacy ? 0 : 1;

    private byte[] ProjectFieldPixels(TinyFarmFieldRuntime runtime)
    {
        long started = Stopwatch.GetTimestamp();
        byte[] source = runtime.ProjectRgba8();
        byte[] pixels = FieldPadding == 0
            ? source
            : TinyFarmBankMask.Pad(source, runtime.Definition.Width, runtime.Definition.Height);
        fieldProjectionTicks += Stopwatch.GetTimestamp() - started;
        return pixels;
    }

    private void PresentField(NativeLayerFrameContext context, TinyFarmFrame frame)
    {
        TinyFarmFieldRuntime runtime = game.Host.Session.Field;
        if (frame.ActiveScene != runtime.Definition.Scene)
        {
            return;
        }
        if (projectedFieldGeneration != runtime.ProjectionGeneration)
        {
            byte[] pixels = ProjectFieldPixels(runtime);
            field.UpdateTexture(
                fieldTexture,
                (uint)(runtime.Definition.Width + FieldPadding * 2),
                (uint)(runtime.Definition.Height + FieldPadding * 2),
                pixels);
            projectedFieldGeneration = runtime.ProjectionGeneration;
            FieldTextureUploads++;
            FieldUploadBytes += pixels.Length;
        }
        float x = left + ((float)runtime.Definition.Origin.XUnits / ScenePosition.UnitsPerTile * scale);
        float y = top + ((float)runtime.Definition.Origin.YUnits / ScenePosition.UnitsPerTile * scale);
        float width = runtime.Definition.Width * runtime.Definition.CellSize
            / (float)ScenePosition.UnitsPerTile * scale;
        float height = runtime.Definition.Height * runtime.Definition.CellSize
            / (float)ScenePosition.UnitsPerTile * scale;
        float padding = FieldPadding * runtime.Definition.CellSize / (float)ScenePosition.UnitsPerTile * scale;
        x -= padding;
        y -= padding;
        width += padding * 2;
        height += padding * 2;
        context.Present(field, pass => pass.SubmitQuad(new NativeQuadSubmission(
            new Native2DRect(x, y, width, height),
            Native2DUvRect.Full,
            fieldTexture,
            Native2DTint.White)));
    }

    private WorldPresentationSnapshot BuildSpriteSnapshot(
        TinyFarmFrame frame,
        ulong frameId,
        bool cave,
        bool house)
    {
        TimeSpan elapsed = TimeSpan.FromSeconds(frameId / 60.0);
        worldSpriteScratch.Clear();
        bool semanticRiverside = frame.ActiveScene == TinyFarmSceneIds.Riverside;
        if (semanticRiverside)
        {
            AddM24WorldObjects(elapsed);
        }
        else
        {
            EnsureStaticTileSprites(frame.SceneWidth, frame.SceneHeight, cave, house);
            worldSpriteScratch.AddRange(staticTileSprites);
        }

        foreach (TinyFarmSceneObjectView item in frame.SceneObjects ?? [])
        {
            if (item.Id.Value == "river" || semanticRiverside && item.Id.Value == "reeds")
            {
                continue;
            }
            string spriteId = item.Depleted && item.Kind == SceneObjectKind.Tree
                ? "grass-d"
                : SpriteFor(item);
            worldSpriteScratch.Add(Sprite(
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
            worldSpriteScratch.Add(Sprite("crop-" + plot.Id.Value, "mint", new WorldPoint2(x, y), elapsed, WorldSpriteLayer.World, y, Native2DTint.White));
        }

        foreach (TinyFarmItemView item in frame.GroundItems)
        {
            double x = item.Position.X / 1024.0;
            double y = item.Position.Y / 1024.0;
            worldSpriteScratch.Add(Sprite("ground-item-" + item.Id.Value, "mint", new WorldPoint2(x, y), elapsed, WorldSpriteLayer.World, y, Native2DTint.White));
        }

        foreach (TinyFarmActorView actor in frame.Actors)
        {
            double x = actor.Position.X / 1024.0;
            double y = actor.Position.Y / 1024.0;
            Native2DTint tint = actor.IsPlayer ? Native2DTint.White : new Native2DTint(1, 0.82f, 0.72f, 1);
            worldSpriteScratch.Add(Sprite("actor-" + actor.Id.Value, "farmer", new WorldPoint2(x, y), elapsed, WorldSpriteLayer.Actors, y, tint, "walk-down"));
        }

        foreach (TinyFarmEnemyView enemy in frame.Enemies ?? [])
        {
            if (enemy.Lifecycle != EnemyLifecycle.Alive)
            {
                continue;
            }
            double x = enemy.Position.X / 1024.0;
            double y = enemy.Position.Y / 1024.0;
            worldSpriteScratch.Add(Sprite("enemy-" + enemy.Id.Value, "mint", new WorldPoint2(x, y), elapsed, WorldSpriteLayer.Actors, y, new Native2DTint(0.65f, 1, 0.72f, 1)));
        }
        return new WorldPresentationSnapshot(worldSpriteScratch);
    }

    private void AddM24WorldObjects(TimeSpan elapsed)
    {
        worldSpriteScratch.Add(M24Sprite(
            "m24-farmhouse",
            m24Assets.Farmhouse.Id,
            new WorldPoint2(3.2, 3.3),
            elapsed,
            feetY: 3.3,
            spriteScale: FarmhouseScale));
        AddTree("m24-tree-west", 1.3, 5.25, elapsed, 1.0);
        AddTree("m24-tree-path", 7.0, 3.1, elapsed, 1.08);
        AddTree("m24-tree-bank", 8.35, 1.25, elapsed, 1.02);
        AddTree("m24-tree-south", 6.4, 8.75, elapsed, 0.94);
        worldSpriteScratch.Add(Sprite(
            "m24-well",
            "well",
            new WorldPoint2(6.25, 5.65),
            elapsed,
            WorldSpriteLayer.Actors,
            5.65,
            Native2DTint.White));
        for (int index = 0; index < 5; index++)
        {
            double x = 1.0 + index;
            worldSpriteScratch.Add(Sprite(
                "m24-fence-" + index,
                "fence",
                new WorldPoint2(x, 0.85),
                elapsed,
                WorldSpriteLayer.Actors,
                0.85,
                Native2DTint.White));
        }
    }

    private void AddTree(string stableId, double x, double y, TimeSpan elapsed, double spriteScale)
    {
        worldSpriteScratch.Add(M24Sprite(
            stableId,
            m24Assets.Tree.Id,
            new WorldPoint2(x, y),
            elapsed,
            y,
            spriteScale * TreeScale));
    }

    private WorldSprite M24Sprite(
        string stableId,
        SpriteAssetId assetId,
        WorldPoint2 anchor,
        TimeSpan elapsed,
        double feetY,
        double spriteScale = 1)
    {
        return new WorldSprite(
            new WorldPresentationId(stableId),
            anchor,
            assetId,
            "full",
            ClipId: null,
            elapsed,
            Restart: false,
            Scale: spriteScale,
            Native2DTint.White,
            WorldSpriteLayer.Actors,
            feetY);
    }

    private SpriteFrameMetadata ResolveFrame(WorldSprite sprite)
    {
        return sprite.AssetId == spriteAtlas.Resource.Id
            ? playback.Resolve(sprite, spriteAtlas.Metadata, spriteResolver)
            : m24Assets.Frame(sprite.AssetId);
    }

    private void PresentM24Ground(NativeLayerFrameContext context)
    {
        if (BackdropOverride is { } color)
        {
            context.Present(shapes, pass => Rect(pass, 0, 0, context.TargetWidth, context.TargetHeight, color, 0));
            return;
        }
        context.Present(RendererFor(ResolveTexture(m24Assets.Meadow.Id)), pass => pass.SubmitQuad(new NativeQuadSubmission(
            getLayout().Legacy
                ? new Native2DRect(left, top, 16 * scale, 10 * scale)
                : new Native2DRect(0, 0, context.TargetWidth, context.TargetHeight),
            Native2DUvRect.Full,
            ResolveTexture(m24Assets.Meadow.Id),
            Native2DTint.White)));
        context.Present(shapes, pass =>
        {
            if (getLayout().Legacy)
            {
                Tile(pass, 9.12f, 0, 0.76f, 10, 0xA98A61FF, 12);
            }
            else
            {
                // Translucent soil feather under the semantic wet-alpha field.
                for (int band = 8; band >= 1; band--)
                {
                    float width = 0.10f + band * 0.075f;
                    Tile(pass, 9.5f - width, 0, width * 2, 10, 0xA98A6120, 0);
                }
            }
            DrawPath(pass);
            Tile(pass, 2.2f, 6.4f, 3.4f, 2.2f, 0x795A3DE6, 12);
            for (int row = 0; row < 4; row++)
            {
                Tile(pass, 2.45f, 6.7f + row * 0.42f, 2.9f, 0.08f, 0xB68A59FF, 3);
            }
            for (int index = 0; index < 15; index++)
            {
                float x = 5.55f + ((index * 37) % 20) / 10f;
                float y = 2.35f + ((index * 19) % 11) / 10f;
                Tile(pass, x, y, 0.11f, 0.11f, index % 3 == 0 ? 0xF3D38BFF : 0xF4E7C2FF, 6);
            }
        });
    }

    private void DrawPath(VulkanOrderedQuadRenderer pass)
    {
        SpatialPoint3D[] points = m24Scene.Paths.Single().Centerline.ToArray();
        for (int segment = 0; segment < points.Length - 1; segment++)
        {
            for (int step = 0; step <= 10; step++)
            {
                float amount = step / 10f;
                float x = (float)(points[segment].X + ((points[segment + 1].X - points[segment].X) * amount));
                float y = (float)(points[segment].Y + ((points[segment + 1].Y - points[segment].Y) * amount));
                float irregular = ((segment * 11 + step * 7) % 5 - 2) * 0.025f;
                Tile(pass, x - 0.58f - irregular, y - 0.58f, 1.16f + irregular * 2, 1.16f, 0xB88D58F2, 28);
            }
        }
    }

    private void PresentM24Bridge(NativeLayerFrameContext context)
    {
        context.Present(shapes, pass =>
        {
            if (getLayout().Legacy)
            {
                Tile(pass, 9.22f, 0, 0.20f, 10, 0x78904CFF, 7);
                Tile(pass, 9.40f, 0, 0.24f, 10, 0xA98A61E8, 8);
            }
            Tile(pass, 9.2f, 4.25f, 2.4f, 1.5f, 0x8A623FFF, 5);
            for (int plank = 0; plank < 6; plank++)
            {
                Tile(pass, 9.28f + plank * 0.38f, 4.34f, 0.28f, 1.32f, 0xC99A61FF, 3);
            }
        });
    }

    private void EnsureStaticTileSprites(int width, int height, bool cave, bool house)
    {
        if (staticTileWidth == width
            && staticTileHeight == height
            && staticTileCave == cave
            && staticTileHouse == house)
        {
            return;
        }

        staticTileSprites.Clear();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                staticTileSprites.Add(Sprite(
                    $"tile-{x:D2}-{y:D2}",
                    TinyFarmAuthoredTileMap.TileAt(x, y, house, cave),
                    new WorldPoint2(x + 0.5, y + 0.5),
                    TimeSpan.Zero,
                    WorldSpriteLayer.Ground,
                    y,
                    Native2DTint.White));
            }
        }

        staticTileWidth = width;
        staticTileHeight = height;
        staticTileCave = cave;
        staticTileHouse = house;
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
                uint color = item.Id.Value == "river" ? 0x649BFFF : cave ? 0x243934FF : 0xA18A64FF;
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
        painterlyResources?.Dispose();
        painterly?.Dispose();
        spriteResources?.Dispose();
        field?.Dispose();
        sprites?.Dispose();
        shapes?.Dispose();
        waves?.Dispose();
    }
}

internal sealed class TinyFarmNativeOverlay(
    LayerId layer,
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram analyticProgram,
    CompiledGraphicsProgram msdfProgram,
    CompiledGraphicsProgram profileMsdfProgram,
    TinyFarmNativeUiFont font,
    ProfileNativeCompositionResource profileTree,
    Func<TinyFarmUiResources> presentation,
    TinyFarmGame game,
    Func<TinyFarmPresentationLayout> getLayout,
    Func<Camera2DSnapshot?> getCamera) : INativeLayerPresenter
{
    private VulkanOrderedQuadRenderer shapes = null!;
    private VulkanOrderedQuadRenderer text = null!;
    private VulkanOrderedQuadRenderer profiles = null!;
    private AurelianMsdfAtlasCache atlasCache = null!;
    private ProfileNativeRealizationCache profileCache = null!;
    private string? inspectorKey;
    private TinyFarmNativeUiSegments inspectorSegments = TinyFarmNativeUiSegments.Empty;
    private MachinaPresentationFrame? baseSource;
    private MachinaPresentationFrame? clockSource;
    private MachinaPresentationFrame? promptSource;
    private TinyFarmNativeUiSegments baseSegments = TinyFarmNativeUiSegments.Empty;
    private TinyFarmNativeUiSegments clockSegments = TinyFarmNativeUiSegments.Empty;
    private TinyFarmNativeUiSegments promptSegments = TinyFarmNativeUiSegments.Empty;
    public LayerId Layer => layer;
    public long AllocatedBytes { get; private set; }
    public int NativePrimitiveCount =>
        baseSegments.NativePrimitiveCount + clockSegments.NativePrimitiveCount + promptSegments.NativePrimitiveCount;
    public int FallbackRasterPrimitiveCount =>
        baseSegments.FallbackCount + clockSegments.FallbackCount + promptSegments.FallbackCount;
    public int FontAtlasUploads => atlasCache?.UploadCount ?? 0;
    public int TextGeometryCacheEntries => font.CachedTextRunCount;
    public int TextGeometryCacheCapacity => font.CachedTextRunCapacity;
    public int GeometryRebuilds { get; private set; }

    public void ResetPerformanceMetrics()
    {
        AllocatedBytes = 0;
    }

    public void Attach(VulkanNativeFrameTarget target)
    {
        shapes = new VulkanOrderedQuadRenderer(plant, analyticProgram, target, Native2DPipelineOptions.AnalyticShape2D);
        text = new VulkanOrderedQuadRenderer(plant, msdfProgram, target, Native2DPipelineOptions.MsdfText);
        profiles = new VulkanOrderedQuadRenderer(plant, profileMsdfProgram, target, Native2DPipelineOptions.ProfileMsdf);
        atlasCache = new AurelianMsdfAtlasCache(text);
        profileCache = new ProfileNativeRealizationCache(profiles, capacity: 4);
        foreach (AurelianMsdfAtlasResource resource in font.Resources)
        {
            atlasCache.Resolve(resource);
        }
        if (getLayout().Legacy)
        {
            profileCache.Warm(profileTree);
        }
        WarmCurrentPresentation();
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        shapes.Retarget(target);
        text.Retarget(target);
        profiles.Retarget(target);
        baseSource = null;
        clockSource = null;
        promptSource = null;
    }

    public void Present(NativeLayerFrameContext context)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        TinyFarmUiResources current = presentation();
        UpdateRealization(current);

        if (game.Presentation.HudVisible)
        {
            PresentSegment(context, baseSegments.Base, includeProfile: getLayout().Legacy);
            PresentSegment(context, clockSegments.Base);
        }
        PresentSegment(context, baseSegments.Overlay);
        if (getLayout().Legacy)
        {
            PresentSegment(context, promptSegments.Base, offsetX: 125, offsetY: 528);
        }
        else if (getCamera() is { } camera)
        {
            ScenePosition player = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
            TinyFarmPresentationLayout layout = getLayout();
            float x = ((float)camera.Viewport.X + (player.XUnits / 1024f - (float)camera.Position.X) * layout.WorldScale - layout.UiLeft) / layout.UiScale;
            float y = ((float)camera.Viewport.Y + (player.YUnits / 1024f - (float)camera.Position.Y) * layout.WorldScale - layout.UiTop) / layout.UiScale;
            PresentSegment(context, promptSegments.Base, offsetX: Math.Clamp(x + 16, 8, 800), offsetY: Math.Clamp(y + 12, 8, 675));
        }
        if (game.Presentation.InspectorVisible)
        {
            PresentInspector(context);
        }
        AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private void PresentInspector(NativeLayerFrameContext context)
    {
        TinyFarmPresentationLayout layout = getLayout();
        string key = $"{layout.Width}x{layout.Height} | world {layout.WorldScale:0.0} px/m | HUD {game.Presentation.HudVisible}";
        if (key != inspectorKey)
        {
            inspectorKey = key;
            var nodes = new List<Machina.Core.Nodes.UiNode>();
            TinyFarmNativeUi.Panel(nodes, "inspector", 0, 0, 760, 184, 0x102D25EE);
            TinyFarmNativeUi.Text(nodes, "inspect-title", "OBLIVION / PRESENTATION / F10", 14, 10, 720, Machina.Core.Styling.TextSize.Md);
            TinyFarmNativeUi.Text(nodes, "inspect-viewport", key, 14, 38, 720, Machina.Core.Styling.TextSize.Md);
            TinyFarmNativeUi.Text(nodes, "inspect-sampling", game.LiveInspection.Presentation?.Sampling ?? "No sampler facts", 14, 66, 730, Machina.Core.Styling.TextSize.Md);
            TinyFarmNativeUi.Text(nodes, "inspect-authority", "World projection independent of HUD / semantic state untouched", 14, 94, 730, Machina.Core.Styling.TextSize.Md);
            string[] detail = (game.LiveInspection.Presentation?.Detail ?? "No native detail facts").Split("; ");
            for (int line = 0; line < detail.Length; line++)
            {
                TinyFarmNativeUi.Text(nodes, "inspect-detail-" + line, detail[line], 14, 122 + line * 28, 730, Machina.Core.Styling.TextSize.Md);
            }
            var node = Machina.Core.Authoring.UI.Surface(id: "presentation-inspector", width: 760, height: 184, children: nodes);
            var prepared = new Machina.Pipeline.MachinaPresentationPipeline().Prepare(node, 760, 184).PresentationFrame;
            inspectorSegments = Realize(prepared, 0, 0, splitOverlay: false);
        }
        PresentSegment(context, inspectorSegments.Base, offsetX: 16, offsetY: 16);
    }

    private void WarmCurrentPresentation()
    {
        UpdateRealization(presentation());
    }

    private void UpdateRealization(TinyFarmUiResources current)
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
                ? TinyFarmNativeUiSegments.Empty
                : Realize(current.Prompt, 0, 0, splitOverlay: false);
        }
    }

    private TinyFarmNativeUiSegments Realize(
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

        return new TinyFarmNativeUiSegments(
            new TinyFarmNativeUiSegment(baseShapes.ToArray(), baseText.ToArray()),
            new TinyFarmNativeUiSegment(overlayShapes.ToArray(), overlayText.ToArray()),
            fallbackCount);
    }

    private void PresentSegment(
        NativeLayerFrameContext context,
        TinyFarmNativeUiSegment segment,
        bool includeProfile = false, float offsetX = 0, float offsetY = 0)
    {
        if (segment.Shapes.Length > 0)
        {
            context.Present(shapes, pass =>
            {
                foreach (NativeAnalyticShapeSubmission submission in segment.Shapes)
                {
                    TinyFarmPresentationLayout layout = getLayout();
                    pass.SubmitAnalyticShape(submission with
                    {
                        Destination = layout.UiRect(submission.Destination with { X = submission.Destination.X + offsetX, Y = submission.Destination.Y + offsetY }),
                        ShapeSize = new Native2DSize(submission.ShapeSize.Width * layout.UiScale, submission.ShapeSize.Height * layout.UiScale),
                        Radius = submission.Radius * layout.UiScale,
                        BorderWidth = submission.BorderWidth * layout.UiScale,
                    });
                }
            });
        }
        if (segment.Text.Length > 0)
        {
            context.Present(text, pass =>
            {
                foreach (NativeMsdfQuadSubmission submission in segment.Text)
                {
                    TinyFarmPresentationLayout layout = getLayout();
                    pass.SubmitMsdfQuad(submission with
                    {
                        Destination = layout.UiRect(submission.Destination with { X = submission.Destination.X + offsetX, Y = submission.Destination.Y + offsetY }),
                        Msdf = submission.Msdf with { FieldScale = submission.Msdf.FieldScale * layout.UiScale },
                    });
                }
            });
        }
        if (includeProfile)
        {
            context.Present(profiles, pass =>
            {
                profileCache.Submit(profileTree, new ProfileNativeInstance(550, 430, 0.68f));
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
        profileCache?.Dispose();
        atlasCache?.Dispose();
        profiles?.Dispose();
        text?.Dispose();
        shapes?.Dispose();
    }
}

internal sealed record TinyFarmNativeUiSegments(
    TinyFarmNativeUiSegment Base,
    TinyFarmNativeUiSegment Overlay,
    int FallbackCount)
{
    public static TinyFarmNativeUiSegments Empty { get; } = new(
        TinyFarmNativeUiSegment.Empty,
        TinyFarmNativeUiSegment.Empty,
        0);

    public int NativePrimitiveCount => Base.NativePrimitiveCount + Overlay.NativePrimitiveCount;
}

internal sealed record TinyFarmNativeUiSegment(
    NativeAnalyticShapeSubmission[] Shapes,
    NativeMsdfQuadSubmission[] Text)
{
    public static TinyFarmNativeUiSegment Empty { get; } = new([], []);

    public int NativePrimitiveCount => Shapes.Length + Text.Length;
}

internal sealed class TinyFarmNativeLayer(LayerId id, int order) : IAurelianLayer
{
    private LayerViewport viewport = new(0, 0, 1920, 1080);
    public LayerDescriptor Describe() => new(id, order, true, viewport, LayerPresentationMode.DirectHostPass, LayerInputPolicy.None);
    public void Attach(LayerSurfaceDescriptor surface)
    {
        Resize(surface);
    }
    public void Resize(LayerSurfaceDescriptor surface)
    {
        viewport = new LayerViewport(0, 0, surface.Width, surface.Height);
    }
    public void Update(LayerUpdateContext context) { }
    public LayerPresentationDto Present(LayerPresentationContext context) => new(id, Describe().Viewport, true, context.Surface.Kind, id.Value);
    public LayerInputResult HandleInput(LayerInputEvent input) => LayerInputResult.Unconsumed;
    public void Detach() { }
}
