using System.Security.Cryptography;
using Aurelian.Composition;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeComposition;
using Aurelian.Machina.Graphics;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using SkiaSharp;
using Machina.Presentation;

namespace Aurelian.Ariadne.VnDemo;

public sealed class VnNativeRenderer : IDisposable
{
    public const int Width = 1280;
    public const int Height = 720;
    private readonly RenApp app;
    private readonly VnMachinaLayer machinaLayer;
    private readonly AurelianVulkanPlant plant;
    private readonly NativeLayerCompositor compositor;
    private readonly SpriteAtlasResource backgroundResource;
    private readonly SpriteAtlasResource portraitResource;
    private readonly SpriteAtlasResource transparentPortraitResource;
    private readonly SpriteAtlasResource uiAtlasResource;
    private readonly SpriteAtlasResource seamFixtureResource;
    private readonly TextureLayerPresenter backgroundPresenter;
    private readonly TextureLayerPresenter portraitPresenter;
    private readonly VnUiLayerPresenter uiPresenter;
    private int framebufferWidth;
    private int framebufferHeight;

    public VnNativeRenderer(
        string repositoryRoot,
        RenApp app,
        VnMachinaLayer machinaLayer,
        int framebufferWidth = Width,
        int framebufferHeight = Height,
        Action<string>? startupProgress = null)
    {
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framebufferWidth));
        }
        this.app = app;
        this.machinaLayer = machinaLayer;
        this.framebufferWidth = framebufferWidth;
        this.framebufferHeight = framebufferHeight;
        backgroundResource = LoadImage(app.Presentation.BackgroundAsset, "sunkill-bunker", opaqueBackground: true);
        portraitResource = LoadPortrait("sunkill-oppenheimer.png", "oppenheimer");
        transparentPortraitResource = Transparent("portrait-empty");
        uiAtlasResource = LoadRawImage(
            machinaLayer.Skin.Atlas.ResolvedImagePath,
            VnUiSkin.AtlasAssetId,
            machinaLayer.Skin.Atlas.Width,
            machinaLayer.Skin.Atlas.Height);
        seamFixtureResource = CreateSeamFixture();
        startupProgress?.Invoke("assets");
        CompiledGraphicsProgram program = Task.Run(() => CompileShader(
                repositoryRoot,
                "samples/Aurelian/ForwardTexturedM3.v.ts",
                startupProgress))
            .GetAwaiter()
            .GetResult();
        startupProgress?.Invoke("shader");
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "SUNKILL"));
        if (!init.Success || init.Plant is null)
        {
            throw new InvalidOperationException(string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        }
        plant = init.Plant;
        startupProgress?.Invoke("vulkan-plant");
        compositor = new NativeLayerCompositor(
            plant,
            framebufferWidth,
            framebufferHeight,
            clearColor: new NativeFrameClearColor(0.005f, 0.004f, 0.003f, 1));
        var backgroundLayer = new VnImageSemanticLayer(new LayerId("world-background"), 0);
        var portraitLayer = new VnImageSemanticLayer(new LayerId("portrait"), 50);
        backgroundPresenter = new TextureLayerPresenter(backgroundLayer.Describe().Id, plant, program, Background);
        portraitPresenter = new TextureLayerPresenter(portraitLayer.Describe().Id, plant, program, Portrait);
        uiPresenter = new VnUiLayerPresenter(
            plant,
            program,
            machinaLayer,
            uiAtlasResource,
            seamFixtureResource,
            Overlay);
        compositor.Add(backgroundLayer, backgroundPresenter);
        compositor.Add(portraitLayer, portraitPresenter);
        compositor.Add(machinaLayer, uiPresenter);
        startupProgress?.Invoke("composition");
        compositor.Attach();
        startupProgress?.Invoke("attached");
    }

    public int FramebufferWidth => framebufferWidth;

    public int FramebufferHeight => framebufferHeight;

    public MachinaViewportTransform ViewportTransform => MachinaViewportTransform.Create(
        Width,
        Height,
        framebufferWidth,
        framebufferHeight);

    public int TextureUploadCount => backgroundPresenter.TextureUploads
        + portraitPresenter.TextureUploads
        + uiPresenter.TextureUploads;

    public NativeLayerFrameResult Render(ulong frameId)
    {
        NativeLayerFrameResult result = compositor.RunFrame(frameId, TimeSpan.FromMilliseconds(16));
        if (!result.NativeLayerOrder.SequenceEqual(
            [new LayerId("world-background"), new LayerId("portrait"), VnMachinaLayer.Id]))
        {
            throw new InvalidOperationException("Native VN composition order diverged from semantic layer order.");
        }
        return result;
    }

    public LayerInputRoutingResult Route(LayerInputEvent input) => compositor.RouteInput(input);

    public LayerPoint ToLogicalPointer(double physicalX, double physicalY)
    {
        (double x, double y) = ViewportTransform.ToLogical(physicalX, physicalY);
        return new LayerPoint(x, y);
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (width == framebufferWidth && height == framebufferHeight)
        {
            return;
        }

        compositor.Resize(width, height);
        framebufferWidth = width;
        framebufferHeight = height;
    }

    public void Dispose()
    {
        compositor.Dispose();
        plant.Dispose();
    }

    private SpriteAtlasResource Background()
    {
        return backgroundResource;
    }

    private SpriteAtlasResource Portrait()
    {
        string? portrait = app.Presentation.PortraitAsset;
        if (portrait is null)
        {
            return transparentPortraitResource;
        }

        return portraitResource;
    }

    private SpriteAtlasResource Overlay()
    {
        byte[] rgba = machinaLayer.Rgba8;
        return Resource("machina-overlay", rgba);
    }

    private static SpriteAtlasResource LoadImage(string file, string id, bool opaqueBackground)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", file);
        using SKBitmap source = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode '{path}'.");
        using var target = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(target);
        canvas.Clear(opaqueBackground ? SKColors.Black : SKColors.Transparent);
        float scale = Math.Max((float)Width / source.Width, (float)Height / source.Height);
        float drawWidth = source.Width * scale;
        float drawHeight = source.Height * scale;
        canvas.DrawBitmap(source, new SKRect((Width - drawWidth) / 2, (Height - drawHeight) / 2, (Width + drawWidth) / 2, (Height + drawHeight) / 2));
        canvas.Flush();
        return Resource(id, target.Bytes);
    }

    private static SpriteAtlasResource LoadPortrait(string file, string id)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", file);
        using SKBitmap source = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode '{path}'.");
        using var target = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        float scale = 0.54f;
        float drawWidth = source.Width * scale;
        float drawHeight = source.Height * scale;
        float centerX = 760;
        canvas.DrawBitmap(source, new SKRect(centerX - drawWidth / 2, Height - drawHeight + 110, centerX + drawWidth / 2, Height + 110));
        canvas.Flush();
        return Resource(id, target.Bytes);
    }

    private static SpriteAtlasResource LoadRawImage(string path, string id, int width, int height)
    {
        using SKBitmap source = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Could not decode '{path}'.");
        if (source.Width != width || source.Height != height)
        {
            throw new InvalidDataException(
                $"SpriteForge atlas declares {width}x{height}, but '{path}' is {source.Width}x{source.Height}.");
        }

        using var target = new SKBitmap(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return Resource(id, target.Bytes, width, height);
    }

    private static SpriteAtlasResource Transparent(string id)
    {
        return Resource(id, new byte[Width * Height * 4]);
    }

    private static SpriteAtlasResource CreateSeamFixture()
    {
        const int size = 16;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int offset = ((y * size) + x) * 4;
                bool boundary = x <= 3 || x >= 12 || y <= 3 || y >= 12;
                pixels[offset] = boundary ? (byte)255 : (byte)18;
                pixels[offset + 1] = boundary ? (byte)112 : (byte)24;
                pixels[offset + 2] = boundary ? (byte)12 : (byte)28;
                pixels[offset + 3] = 255;
            }
        }

        return Resource("sunkill.seam.fixture", pixels, size, size);
    }

    private static SpriteAtlasResource Resource(string id, byte[] rgba)
    {
        return Resource(id, rgba, Width, Height);
    }

    private static SpriteAtlasResource Resource(string id, byte[] rgba, int width, int height)
    {
        string hash = Convert.ToHexString(SHA256.HashData(rgba));
        return new SpriteAtlasResource(
            new SpriteAssetId(id),
            hash,
            (uint)width,
            (uint)height,
            rgba,
            SpriteSampling.Linear);
    }

    private static CompiledGraphicsProgram CompileShader(
        string repositoryRoot,
        string sourceName,
        Action<string>? startupProgress)
    {
        string path = Path.Combine(repositoryRoot, sourceName.Replace('/', Path.DirectorySeparatorChar));
        string source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        startupProgress?.Invoke("shader-source");
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
        startupProgress?.Invoke("shader-bound");
        if (!module.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));
        }
        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
        startupProgress?.Invoke("shader-backend");
        if (!backend.Vertex.SpirvValidated || !backend.Pixel.SpirvValidated)
        {
            throw new InvalidOperationException(backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
        }
        return CompiledGraphicsProgramExporter.Export(module, backend);
    }
}

public sealed class TextureLayerPresenter : INativeLayerPresenter
{
    private readonly AurelianVulkanPlant plant;
    private readonly CompiledGraphicsProgram program;
    private readonly Func<SpriteAtlasResource> resource;
    private VulkanOrderedQuadRenderer? renderer;
    private NativeSpriteResourceScope? resources;

    public TextureLayerPresenter(
        LayerId layer,
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        Func<SpriteAtlasResource> resource)
    {
        Layer = layer;
        this.plant = plant;
        this.program = program;
        this.resource = resource;
    }

    public LayerId Layer { get; }
    public int TextureUploads => resources?.TextureUploads ?? 0;

    public void Attach(VulkanNativeFrameTarget target) => Create(target);

    public void Resize(VulkanNativeFrameTarget target)
    {
        if (renderer is null || resources is null)
        {
            throw new InvalidOperationException("Texture presenter is not attached.");
        }

        renderer.Retarget(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        if (renderer is null || resources is null) throw new InvalidOperationException("Texture presenter is not attached.");
        SpriteAtlasResource current = resource();
        Native2DTextureHandle texture = resources.Resolve(current);
        MachinaViewportTransform viewport = MachinaViewportTransform.Create(
            VnNativeRenderer.Width,
            VnNativeRenderer.Height,
            (int)context.TargetWidth,
            (int)context.TargetHeight);
        global::Machina.Layout.Geometry.Rect physical = viewport.PhysicalViewport;
        context.Present(renderer, pass => pass.SubmitQuad(new NativeQuadSubmission(
            new Native2DRect(
                (float)physical.X,
                (float)physical.Y,
                (float)physical.Width,
                (float)physical.Height),
            Native2DUvRect.Full,
            texture,
            Native2DTint.White)));
    }

    public void Detach() => DisposeResources();

    private void Create(VulkanNativeFrameTarget target)
    {
        renderer = new VulkanOrderedQuadRenderer(plant, program, target, Native2DPipelineOptions.SpriteLinear);
        resources = new NativeSpriteResourceScope(renderer, SpriteSampling.Linear);
    }

    private void DisposeResources()
    {
        resources?.Dispose();
        renderer?.Dispose();
        resources = null;
        renderer = null;
    }
}

public sealed class VnUiLayerPresenter : INativeLayerPresenter
{
    private readonly AurelianVulkanPlant plant;
    private readonly CompiledGraphicsProgram program;
    private readonly VnMachinaLayer layer;
    private readonly SpriteAtlasResource atlas;
    private readonly SpriteAtlasResource seamFixture;
    private readonly Func<SpriteAtlasResource> overlay;
    private VulkanOrderedQuadRenderer? renderer;
    private NativeSpriteResourceScope? resources;

    public VnUiLayerPresenter(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        VnMachinaLayer layer,
        SpriteAtlasResource atlas,
        SpriteAtlasResource seamFixture,
        Func<SpriteAtlasResource> overlay)
    {
        this.plant = plant;
        this.program = program;
        this.layer = layer;
        this.atlas = atlas;
        this.seamFixture = seamFixture;
        this.overlay = overlay;
    }

    public LayerId Layer => VnMachinaLayer.Id;

    public int TextureUploads => resources?.TextureUploads ?? 0;

    public void Attach(VulkanNativeFrameTarget target)
    {
        Create(target);
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        if (renderer is null || resources is null)
        {
            throw new InvalidOperationException("SUNKILL UI presenter is not attached.");
        }

        renderer.Retarget(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        if (renderer is null || resources is null)
        {
            throw new InvalidOperationException("SUNKILL UI presenter is not attached.");
        }

        Native2DTextureHandle atlasTexture = resources.Resolve(atlas);
        Native2DTextureHandle seamTexture = resources.Resolve(seamFixture);
        SpriteAtlasResource overlayResource = overlay();
        Native2DTextureHandle overlayTexture = resources.Resolve(overlayResource);
        MachinaViewportTransform viewport = MachinaViewportTransform.Create(
            VnNativeRenderer.Width,
            VnNativeRenderer.Height,
            (int)context.TargetWidth,
            (int)context.TargetHeight);

        context.Present(renderer, pass =>
        {
            if (layer.ProofNineSlices is not null)
            {
                foreach (MachinaNineSlicePrimitive primitive in layer.NineSlices)
                {
                    bool usesSeamFixture = primitive.Texture.Value == "sunkill.seam.fixture";
                    IReadOnlyList<NativeQuadSubmission> quads = AurelianNineSliceAdapter.Lower(
                        primitive,
                        usesSeamFixture ? seamTexture : atlasTexture,
                        usesSeamFixture ? (int)seamFixture.Width : (int)atlas.Width,
                        usesSeamFixture ? (int)seamFixture.Height : (int)atlas.Height,
                        viewport);
                    foreach (NativeQuadSubmission quad in quads)
                    {
                        pass.SubmitQuad(quad);
                    }
                }
            }
            else
            {
                foreach (MachinaProgrammablePanelPrimitive primitive in layer.Panels)
                {
                    bool usesSeamFixture = primitive.Texture.Value == "sunkill.seam.fixture";
                    AurelianProgrammablePanelLoweringResult panel = AurelianProgrammablePanelAdapter.Lower(
                        primitive,
                        usesSeamFixture ? seamTexture : atlasTexture,
                        usesSeamFixture ? (int)seamFixture.Width : (int)atlas.Width,
                        usesSeamFixture ? (int)seamFixture.Height : (int)atlas.Height,
                        viewport);
                    foreach (NativeQuadSubmission quad in panel.Quads)
                    {
                        pass.SubmitQuad(quad);
                    }
                }
            }

            global::Machina.Layout.Geometry.Rect physical = viewport.PhysicalViewport;
            pass.SubmitQuad(new NativeQuadSubmission(
                new Native2DRect(
                    (float)physical.X,
                    (float)physical.Y,
                    (float)physical.Width,
                    (float)physical.Height),
                Native2DUvRect.Full,
                overlayTexture,
                Native2DTint.White));
        });
    }

    public void Detach()
    {
        DisposeResources();
    }

    private void Create(VulkanNativeFrameTarget target)
    {
        renderer = new VulkanOrderedQuadRenderer(
            plant,
            program,
            target,
            Native2DPipelineOptions.SpriteLinear);
        resources = new NativeSpriteResourceScope(renderer, SpriteSampling.Linear);
    }

    private void DisposeResources()
    {
        resources?.Dispose();
        renderer?.Dispose();
        resources = null;
        renderer = null;
    }
}
