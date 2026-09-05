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
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed class SupperRenderer : IAurelianHostCompositor
{
    private readonly AurelianVulkanPlant plant;
    private readonly NativeLayerCompositor compositor;
    private readonly Action<byte[]> display;
    private readonly TinyFarmSupperGame game;
    private readonly SupperUi ui;
    private readonly SupperPresenter world;
    private TinyFarmFrame frame;

    public SupperRenderer(string root, TinyFarmSupperGame game, Action<byte[]> display)
    {
        this.game = game;
        this.display = display;
        ui = new SupperUi(game);
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "TinyFarm - A Little Mint of Kindness"));
        if (!init.Success || init.Plant is null)
        {
            throw new InvalidOperationException(string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        }
        plant = init.Plant;
        Device = init.Facts!.PhysicalDeviceName;
        compositor = new NativeLayerCompositor(plant, 1280, 720);
        CompiledGraphicsProgram analytic = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts");
        CompiledGraphicsProgram shockwave = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/SoftShockwave.v.ts");
        CompiledGraphicsProgram texture = Compile(root, "samples/Aurelian/ForwardTexturedM3.v.ts");
        world = new SupperPresenter(new LayerId("farm-world"), plant, analytic, shockwave, game, () => frame);
        compositor.Add(new SupperLayer(world.Layer, 0), world);
        string portraitPath = Path.Combine(AppContext.BaseDirectory, "Assets", "mara-dialogue.png");
        if (File.Exists(portraitPath))
        {
            var portrait = new SupperPortrait(plant, texture, game, portraitPath);
            compositor.Add(new SupperLayer(portrait.Layer, 50), portrait);
        }
        var overlay = new SupperOverlay(new LayerId("machina-hud"), plant, texture, () => ui.Resource(frame));
        compositor.Add(new SupperLayer(overlay.Layer, 100), overlay);
        compositor.Attach();
    }

    public string Device { get; }
    public NativeLayerFrameResult? Last { get; private set; }
    public int UiRebuilds => ui.Rebuilds;
    public int ShaderQuads => world.ShaderQuads;
    public bool PresentToWindow { get; set; } = true;

    public void Resize(HostSurfaceSize size) { }

    public void Present(AurelianHostFrame hostFrame)
    {
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        Last = compositor.RunFrame(hostFrame.Sequence, hostFrame.Elapsed, captureReadback: true);
        if (PresentToWindow)
        {
            display(Last.NativeFrame.Pixels!);
        }
    }

    public void Dispose()
    {
        compositor.Dispose();
        plant.Dispose();
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
    TinyFarmSupperGame game,
    Func<TinyFarmFrame> getFrame) : INativeLayerPresenter
{
    private VulkanOrderedQuadRenderer shapes = null!;
    private VulkanOrderedQuadRenderer waves = null!;
    private float scale;
    private float left;
    private float top;
    public LayerId Layer => layer;
    public int ShaderQuads { get; private set; }

    public void Attach(VulkanNativeFrameTarget target)
    {
        shapes = new VulkanOrderedQuadRenderer(plant, analytic, target, Native2DPipelineOptions.AnalyticShape2D);
        waves = new VulkanOrderedQuadRenderer(plant, shockwave, target, Native2DPipelineOptions.SoftShockwave);
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        Detach();
        Attach(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        TinyFarmFrame frame = getFrame();
        scale = Math.Min(870f / frame.SceneWidth, 416f / frame.SceneHeight);
        left = 475 - frame.SceneWidth * scale / 2;
        top = 315 - frame.SceneHeight * scale / 2;
        bool cave = frame.ActiveScene == TinyFarmSceneIds.DungeonEntrance;
        bool house = frame.ActiveScene == TinyFarmSceneIds.Residence || frame.ActiveScene == TinyFarmSceneIds.GeneralStore;
        var camera = new EffectCameraTransform(Vector2.Zero, new Vector2(left, top), scale / 1024, 1);
        context.Present(shapes, pass =>
        {
            Rect(pass, 0, 0, 1280, 720, 0xAEBF92FF, 0);
            Rect(pass, 22, 108, 904, 459, 0x203E36FF, 16);
            for (int y = 0; y < frame.SceneHeight; y++)
            {
                for (int x = 0; x < frame.SceneWidth; x++)
                {
                    uint color = cave ? 0x354C49FF : house ? 0x9B7857FF : 0x7FA576FF;
                    if ((x * 7 + y * 13) % 7 < 2)
                    {
                        color = cave ? 0x39534DFF : house ? 0xA5825CFF : 0x86AD7AFF;
                    }
                    Tile(pass, x, y, 1.01f, 1.01f, color, 0);
                    if (!cave && !house && (x * 31 + y * 17) % 13 == 0)
                    {
                        Tile(pass, x + .18f, y + .27f, .08f, .13f, 0xC6CE92FF, 2);
                        Tile(pass, x + .33f, y + .42f, .08f, .1f, 0xD4D39EFF, 2);
                    }
                }
            }
            if (!cave && !house)
            {
                // The broad walking lane visually ties each small place together.
                Tile(pass, 0, frame.SceneHeight / 2f, frame.SceneWidth, .8f, 0xB5B184FF, 4);
            }
            foreach (TinyFarmSceneObjectView item in frame.SceneObjects ?? [])
            {
                DrawObject(pass, item, cave);
            }
            foreach (TinyFarmPlotView plot in frame.Plots)
            {
                if (plot.Crop is not null)
                {
                    float x = plot.Position.X / 1024f;
                    float y = plot.Position.Y / 1024f;
                    Tile(pass, x - .08f, y - .35f, .16f, .5f, 0xD5E798FF, 3);
                    Tile(pass, x - .32f, y - .24f, .3f, .18f, 0x8DCB70FF, 4);
                    Tile(pass, x + .02f, y - .34f, .3f, .18f, 0x8DCB70FF, 4);
                }
            }
            foreach (TinyFarmItemView item in frame.GroundItems)
            {
                float x = item.Position.X / 1024f;
                float y = item.Position.Y / 1024f;
                Tile(pass, x - .3f, y - .28f, .6f, .55f, 0xD7E4A7FF, 7);
                Tile(pass, x - .08f, y - .28f, .16f, .55f, 0x3B865BFF, 3);
                Tile(pass, x - .27f, y - .18f, .55f, .17f, 0x56A36BFF, 4);
            }
            foreach (TinyFarmActorView actor in frame.Actors.OrderBy(actor => actor.Position.Y))
            {
                DrawActor(pass, actor);
            }
            foreach (TinyFarmEnemyView enemy in frame.Enemies ?? [])
            {
                float x = enemy.Position.X / 1024f;
                float y = enemy.Position.Y / 1024f;
                if (enemy.Lifecycle == EnemyLifecycle.Defeated)
                {
                    Tile(pass, x - .42f, y - .12f, .84f, .22f, 0x78B99DFF, 6);
                    continue;
                }
                Tile(pass, x - .48f, y - .35f, .96f, .72f, 0x29474170, 10);
                Tile(pass, x - .46f, y - .75f, .92f, .92f, 0x81C985FF, 15);
                Tile(pass, x - .26f, y - .52f, .13f, .2f, 0x143B35FF, 2);
                Tile(pass, x + .17f, y - .52f, .13f, .2f, 0x143B35FF, 2);
                Tile(pass, x - .05f, y - .28f, .17f, .07f, 0x143B35FF, 2);
            }
            foreach (NativeAnalyticShapeSubmission particle in EffectNativeProjection.Particles(game.Effects.BuildParticleDrawData(), camera))
            {
                pass.SubmitAnalyticShape(particle);
            }
        });
        var shockwaves = EffectNativeProjection.Shockwaves(game.Effects.BuildQuadDrawData(), camera);
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
        shapes?.Dispose();
        waves?.Dispose();
    }
}

internal sealed class SupperOverlay(LayerId layer, AurelianVulkanPlant plant, CompiledGraphicsProgram program,
    Func<SpriteAtlasResource> resource) : INativeLayerPresenter
{
    private VulkanOrderedQuadRenderer renderer = null!;
    private NativeSpriteResourceScope resources = null!;
    public LayerId Layer => layer;
    public void Attach(VulkanNativeFrameTarget target)
    {
        renderer = new VulkanOrderedQuadRenderer(plant, program, target, Native2DPipelineOptions.SpriteLinear);
        resources = new NativeSpriteResourceScope(renderer, SpriteSampling.Linear);
    }
    public void Resize(VulkanNativeFrameTarget target)
    {
        Detach();
        Attach(target);
    }
    public void Present(NativeLayerFrameContext context)
    {
        Native2DTextureHandle texture = resources.Resolve(resource());
        context.Present(renderer, pass => pass.SubmitQuad(new NativeQuadSubmission(new Native2DRect(0, 0, 1280, 720),
            Native2DUvRect.Full, texture, Native2DTint.White)));
    }
    public void Detach()
    {
        resources?.Dispose();
        renderer?.Dispose();
    }
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
