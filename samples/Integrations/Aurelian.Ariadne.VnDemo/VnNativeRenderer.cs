using System.Security.Cryptography;
using Aurelian.Composition;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using SkiaSharp;

namespace Aurelian.Ariadne.VnDemo;

public sealed class VnNativeRenderer : IDisposable
{
    public const int Width = 1280;
    public const int Height = 720;
    private readonly VnSession session;
    private readonly VnMachinaLayer machinaLayer;
    private readonly AurelianVulkanPlant plant;
    private readonly NativeLayerCompositor compositor;

    public VnNativeRenderer(string repositoryRoot, VnSession session, VnMachinaLayer machinaLayer)
    {
        this.session = session;
        this.machinaLayer = machinaLayer;
        CompiledGraphicsProgram program = CompileShader(repositoryRoot, "samples/Aurelian/ForwardTexturedM3.v.ts");
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.Ariadne.VnDemo"));
        if (!init.Success || init.Plant is null)
        {
            throw new InvalidOperationException(string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        }
        plant = init.Plant;
        compositor = new NativeLayerCompositor(plant, Width, Height, clearColor: NativeFrameClearColor.Transparent);
        var backgroundLayer = new VnImageSemanticLayer(new LayerId("world-background"), 0);
        var portraitLayer = new VnImageSemanticLayer(new LayerId("portrait"), 50);
        compositor.Add(backgroundLayer, new TextureLayerPresenter(backgroundLayer.Describe().Id, plant, program, Background));
        compositor.Add(portraitLayer, new TextureLayerPresenter(portraitLayer.Describe().Id, plant, program, Portrait));
        compositor.Add(machinaLayer, new TextureLayerPresenter(VnMachinaLayer.Id, plant, program, Overlay));
        compositor.Attach();
    }

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

    public void Dispose()
    {
        compositor.Dispose();
        plant.Dispose();
    }

    private SpriteAtlasResource Background()
    {
        return LoadImage("classroom-sunset.png", "classroom-sunset", opaqueBackground: true);
    }

    private SpriteAtlasResource Portrait()
    {
        string? portrait = session.Presentation.PortraitKey;
        string? expression = session.Presentation.ExpressionKey;
        if (portrait is null)
        {
            return Transparent("portrait-empty");
        }
        string file = portrait switch
        {
            "mika" => "mika-concerned.png",
            "rei" when expression == "soft" => "rei-soft-cutout.png",
            _ => "rei-angry.png",
        };
        return LoadPortrait(file, $"{portrait}-{expression}");
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
        float scale = 0.60f;
        float drawWidth = source.Width * scale;
        float drawHeight = source.Height * scale;
        float centerX = portraitCenter(id);
        canvas.DrawBitmap(source, new SKRect(centerX - drawWidth / 2, Height - drawHeight + 110, centerX + drawWidth / 2, Height + 110));
        canvas.Flush();
        return Resource(id, target.Bytes);

        static float portraitCenter(string assetId) => assetId.StartsWith("mika", StringComparison.Ordinal) ? 560 : 610;
    }

    private static SpriteAtlasResource Transparent(string id)
    {
        return Resource(id, new byte[Width * Height * 4]);
    }

    private static SpriteAtlasResource Resource(string id, byte[] rgba)
    {
        string hash = Convert.ToHexString(SHA256.HashData(rgba));
        return new SpriteAtlasResource(new SpriteAssetId(id), hash, Width, Height, rgba, SpriteSampling.Linear);
    }

    private static CompiledGraphicsProgram CompileShader(string repositoryRoot, string sourceName)
    {
        string path = Path.Combine(repositoryRoot, sourceName.Replace('/', Path.DirectorySeparatorChar));
        string source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
        if (!module.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));
        }
        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
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
        DisposeResources();
        Create(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        if (renderer is null || resources is null) throw new InvalidOperationException("Texture presenter is not attached.");
        SpriteAtlasResource current = resource();
        Native2DTextureHandle texture = resources.Resolve(current);
        context.Present(renderer, pass => pass.SubmitQuad(new NativeQuadSubmission(
            new Native2DRect(0, 0, context.TargetWidth, context.TargetHeight),
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
