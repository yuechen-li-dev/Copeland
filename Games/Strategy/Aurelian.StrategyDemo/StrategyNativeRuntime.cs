using System.Diagnostics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Machina;
using Aurelian.Machina.Graphics;
using Aurelian.Profile.Graphics;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Aurelian.Spatial2D;
using Aurelian.Strategy;
using Copeland.Profile;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Profiles;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SkiaSharp;
using InputKey = InputMan.Core.KeyboardKey;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Aurelian.StrategyDemo;

internal static class StrategyNativeRuntime
{
    public static int Run(bool smoke)
    {
        try
        {
            string root = ProofArtifacts.FindRoot();
            using StrategyNativeWindow window = StrategyNativeWindow.Create(smoke);
            var session = new StrategySession();
            var view = new StrategyView();
            view.Center(StrategySession.Home);
            using var input = new StrategyInput(session, view);
            window.Connect(input, view);
            var host = new StrategyHost(session);
            using var renderer = new StrategyNativeRenderer(root, window, session, view, validation: smoke);
            Stopwatch clock = Stopwatch.StartNew();
            TimeSpan previous = clock.Elapsed;
            int frames = 0;
            while (!window.ShouldClose)
            {
                TimeSpan now = clock.Elapsed;
                TimeSpan elapsed = now - previous;
                previous = now;
                window.PumpEvents();
                input.Update(elapsed, now);
                host.Advance(elapsed, view.Paused);
                if (smoke && frames == 3)
                {
                    StrategyNativeRenderer.ToggleInspectorRequested = true;
                }
                bool capture = smoke && frames is 2 or 3;
                VulkanNativeFrameResult frame = renderer.Render((ulong)frames, capture);
                renderer.Present((ulong)frames);
                frames++;
                if (smoke && frames == 3)
                {
                    SaveSmoke(frame, renderer, frames);
                }
                else if (smoke && frames == 4)
                {
                    SaveNativeImage(frame, "native-vulkan-inspector-overlay.png");
                    ProofArtifacts.Write("native-vulkan-inspector-overlay.json", new
                    {
                        visible = true,
                        toggle = "F10",
                        authority = "development-only Machina overlay; no gameplay state mutation",
                        frame.PixelSha256,
                    });
                    Console.WriteLine("MOSSWARD_NATIVE_VULKAN_READY frames=4 inspector=verified");
                    break;
                }
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void SaveSmoke(VulkanNativeFrameResult frame, StrategyNativeRenderer renderer, int frames)
    {
        SaveNativeImage(frame, "native-vulkan-launch-frame.png");
        SaveM19AcceptanceImage(frame);
        ProofArtifacts.Write("native-vulkan-launch.json", new
        {
            windowOpened = true,
            framesRendered = frames,
            renderer = "Aurelian Vulkan Profile + native Machina + semantic fog",
            renderer.Device,
            renderer.ProfileUploads,
            renderer.FontUploads,
            inspectorToggle = "F10",
            width = StrategyHudProfile.Width,
            height = StrategyHudProfile.Height,
            frame.PixelSha256,
        });
        ProofArtifacts.WriteM19("strategy-native-acceptance.json", new
        {
            image = "strategy-native-after.png",
            source = "live Aurelian.StrategyDemo Vulkan presenter smoke capture",
            perspective = "Mossward isometric",
            semanticState = "AURELIAN-RTS-PEARL-MINING-M18 default session",
            hud = "StrategyHudProfile.Build",
            fonts = new[]
            {
                "CrimsonText-Regular.ttf / 22,30",
                "SpaceMono-Regular.ttf / 12",
            },
            profileFieldQuality = ProfileNativeCompiler.DefaultQualitySize,
            profileMinimumShortAxis = ProfileNativeCompiler.DefaultMinimumShortAxis,
            profilePixelRange = ProfileNativeCompiler.DefaultPixelRange,
            textMinimumFieldDimension = StrategyNativeUiFont.MinimumFieldDimension,
            textPixelRange = StrategyNativeUiFont.FieldPixelRange,
            width = StrategyHudProfile.Width,
            height = StrategyHudProfile.Height,
            frame.PixelSha256,
        });
    }

    private static void SaveM19AcceptanceImage(VulkanNativeFrameResult frame)
    {
        byte[] pixels = frame.Pixels ?? throw new InvalidOperationException("Native launch smoke did not capture pixels.");
        using var bitmap = new SKBitmap(new SKImageInfo(
            StrategyHudProfile.Width,
            StrategyHudProfile.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        ProofArtifacts.SaveM19(bitmap, "strategy-native-after.png");
    }

    private static void SaveNativeImage(VulkanNativeFrameResult frame, string name)
    {
        byte[] pixels = frame.Pixels ?? throw new InvalidOperationException("Native launch smoke did not capture pixels.");
        using var bitmap = new SKBitmap(new SKImageInfo(StrategyHudProfile.Width, StrategyHudProfile.Height,
            SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        ProofArtifacts.Save(bitmap, name);
    }
}

internal sealed class StrategyNativeWindow : IDisposable
{
    private readonly IWindow window;
    private readonly IInputContext inputContext;
    private StrategyInput? input;
    private StrategyView? view;
    private bool disposed;

    private StrategyNativeWindow(IWindow window, IInputContext inputContext, IReadOnlyList<string> extensions)
    {
        this.window = window;
        this.inputContext = inputContext;
        RequiredVulkanInstanceExtensions = extensions;
    }

    public IWindow NativeWindow => window;
    public IReadOnlyList<string> RequiredVulkanInstanceExtensions { get; }
    public bool ShouldClose => window.IsClosing;

    public static StrategyNativeWindow Create(bool smoke)
    {
        WindowOptions options = WindowOptions.DefaultVulkan;
        options.IsVisible = !smoke;
        options.Size = new Vector2D<int>(StrategyHudProfile.Width, StrategyHudProfile.Height);
        options.Title = "Mossward — Aurelian Vulkan";
        options.VSync = true;
        options.WindowBorder = WindowBorder.Fixed;
        IWindow window = Silk.NET.Windowing.Window.Create(options);
        window.Initialize();
        return new StrategyNativeWindow(window, window.CreateInput(), ReadRequiredVulkanExtensions(window));
    }

    public void Connect(StrategyInput strategyInput, StrategyView strategyView)
    {
        input = strategyInput;
        view = strategyView;
        foreach (IKeyboard keyboard in inputContext.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }
        foreach (IMouse mouse in inputContext.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnScroll;
        }
        window.FocusChanged += OnFocusChanged;
    }

    public void PumpEvents() => window.DoEvents();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        window.FocusChanged -= OnFocusChanged;
        foreach (IKeyboard keyboard in inputContext.Keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
        }
        foreach (IMouse mouse in inputContext.Mice)
        {
            mouse.MouseMove -= OnMouseMove;
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.Scroll -= OnScroll;
        }
        inputContext.Dispose();
        window.Dispose();
    }

    private void OnKeyDown(IKeyboard keyboard, SilkKey key, int scanCode)
    {
        if (key == SilkKey.F10)
        {
            StrategyNativeRenderer.ToggleInspectorRequested = true;
        }
        if (TryMapKey(key, out InputKey mapped))
        {
            input!.RecordButton(InputMan.Core.Controls.Key(mapped), true);
        }
    }

    private void OnKeyUp(IKeyboard keyboard, SilkKey key, int scanCode)
    {
        if (TryMapKey(key, out InputKey mapped))
        {
            input!.RecordButton(InputMan.Core.Controls.Key(mapped), false);
        }
    }

    private void OnMouseMove(IMouse mouse, System.Numerics.Vector2 position)
    {
        view!.ScreenPointer = (position.X, position.Y);
    }

    private void OnMouseDown(IMouse mouse, SilkMouseButton button)
    {
        RecordMouse(button, true);
    }

    private void OnMouseUp(IMouse mouse, SilkMouseButton button)
    {
        RecordMouse(button, false);
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        input!.RecordWheel(wheel.Y);
    }

    private void RecordMouse(SilkMouseButton button, bool down)
    {
        InputMan.Core.MouseButton? mapped = button switch
        {
            SilkMouseButton.Left => InputMan.Core.MouseButton.Primary,
            SilkMouseButton.Right => InputMan.Core.MouseButton.Secondary,
            _ => null,
        };
        if (mapped.HasValue)
        {
            input!.RecordButton(InputMan.Core.Controls.Mouse(mapped.Value), down);
        }
    }

    private void OnFocusChanged(bool focused)
    {
        input?.Focus(focused);
    }

    private static bool TryMapKey(SilkKey key, out InputKey mapped)
    {
        mapped = key switch
        {
            SilkKey.B => InputKey.B,
            SilkKey.F => InputKey.F,
            SilkKey.I => InputKey.I,
            SilkKey.N => InputKey.N,
            SilkKey.S => InputKey.S,
            SilkKey.Number1 => InputKey.Number1,
            SilkKey.Number2 => InputKey.Number2,
            SilkKey.Number3 => InputKey.Number3,
            SilkKey.Space => InputKey.Space,
            SilkKey.Escape => InputKey.Escape,
            SilkKey.Left => InputKey.ArrowLeft,
            SilkKey.Right => InputKey.ArrowRight,
            SilkKey.Up => InputKey.ArrowUp,
            SilkKey.Down => InputKey.ArrowDown,
            SilkKey.ShiftLeft or SilkKey.ShiftRight => InputKey.LeftShift,
            SilkKey.ControlLeft or SilkKey.ControlRight => InputKey.LeftControl,
            _ => InputKey.Unknown,
        };
        return mapped != InputKey.Unknown;
    }

    private static unsafe IReadOnlyList<string> ReadRequiredVulkanExtensions(IWindow window)
    {
        IVkSurface surface = window.VkSurface
            ?? throw new InvalidOperationException("Silk.NET did not expose a Vulkan surface source.");
        uint count = 0;
        byte** extensions = surface.GetRequiredExtensions(out count);
        var names = new List<string>((int)count);
        for (int index = 0; index < count; index++)
        {
            string? name = SilkMarshal.PtrToString((nint)extensions[index], NativeStringEncoding.UTF8);
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }
        return names;
    }
}

internal sealed class StrategyNativeRenderer : IDisposable
{
    private const int Width = StrategyHudProfile.Width;
    private const int Height = StrategyHudProfile.Height;
    private readonly StrategySession session;
    private readonly StrategyView view;
    private readonly AurelianVulkanPlant plant;
    private readonly AurelianVulkanSurface surface;
    private readonly AurelianVulkanSwapchain swapchain;
    private readonly VulkanNativeFrameTarget target;
    private readonly VulkanNativeSwapchainPresenter presenter;
    private readonly VulkanOrderedQuadRenderer shapes;
    private readonly VulkanOrderedQuadRenderer profiles;
    private readonly VulkanOrderedQuadRenderer text;
    private readonly VulkanOrderedQuadRenderer fog;
    private readonly ProfileNativeRealizationCache profileCache;
    private readonly AurelianMsdfAtlasCache fontCache;
    private readonly StrategyNativeUiFont font;
    private readonly IReadOnlyDictionary<string, ProfileNativeCompositionResource> assets;
    private readonly ProfileNativeCompositionResource landTile;
    private readonly ProfileNativeCompositionResource pathTile;
    private readonly ProfileNativeCompositionResource waterTile;
    private Native2DTextureHandle? fogTexture;
    private readonly byte[] lastVisibility = new byte[StrategySession.MapSize * StrategySession.MapSize];
    private bool hasFogProjection;
    private double lastFogCameraX;
    private double lastFogCameraY;
    private double lastFogZoom;
    private string? hudKey;
    private NativeAnalyticShapeSubmission[] hudShapes = [];
    private NativeMsdfQuadSubmission[] hudText = [];
    private bool inspectorVisible;

    public static bool ToggleInspectorRequested { get; set; }

    public StrategyNativeRenderer(
        string root,
        StrategyNativeWindow window,
        StrategySession session,
        StrategyView view,
        bool validation)
    {
        this.session = session;
        this.view = view;
        string assetDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        font = StrategyNativeUiFont.Create(assetDirectory);
        assets = CompileAssets(assetDirectory);
        landTile = CompileTile("land", "#667f50");
        pathTile = CompileTile("path", "#959463");
        waterTile = CompileTile("water", "#43706e");
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(
                EnableValidation: validation,
                ApplicationName: "Mossward",
                EnablePresentation: true,
                RequiredPresentationInstanceExtensions: window.RequiredVulkanInstanceExtensions));
        if (!init.Success || init.Plant is null)
        {
            throw new InvalidOperationException(string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        }
        plant = init.Plant;
        Device = init.Facts!.PhysicalDeviceName;
        VulkanSwapchainCreateResult created = VulkanSwapchainFactory.Create(
            plant,
            window.NativeWindow,
            new VulkanSwapchainCreateOptions(Width, Height, VSync: true, "Mossward", Visible: !validation));
        if (!created.Success || created.Surface is null || created.Swapchain is null)
        {
            throw new InvalidOperationException(string.Join("; ", created.Diagnostics.Select(item => item.Message)));
        }
        surface = created.Surface;
        swapchain = created.Swapchain;
        target = new VulkanNativeFrameTarget(plant, Width, Height, ParseSwapchainFormat(swapchain.Facts.SelectedFormat));
        shapes = new VulkanOrderedQuadRenderer(
            plant,
            Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts"),
            target,
            Native2DPipelineOptions.AnalyticShape2D);
        profiles = new VulkanOrderedQuadRenderer(
            plant,
            Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/ProfileMsdf.v.ts"),
            target,
            Native2DPipelineOptions.ProfileMsdf);
        text = new VulkanOrderedQuadRenderer(
            plant,
            Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts"),
            target,
            Native2DPipelineOptions.MsdfText);
        fog = new VulkanOrderedQuadRenderer(
            plant,
            Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/SemanticFog.v.ts"),
            target,
            Native2DPipelineOptions.SemanticFog);
        profileCache = new ProfileNativeRealizationCache(profiles, 32);
        foreach (ProfileNativeCompositionResource asset in assets.Values.Append(landTile).Append(pathTile).Append(waterTile))
        {
            profileCache.Warm(asset);
        }
        fontCache = new AurelianMsdfAtlasCache(text);
        foreach (AurelianMsdfAtlasResource resource in font.Resources)
        {
            fontCache.Resolve(resource);
        }
        presenter = new VulkanNativeSwapchainPresenter(plant, target, swapchain);
    }

    public string Device { get; }
    public int ProfileUploads => profileCache.UploadCount;
    public int FontUploads => fontCache.UploadCount;

    public VulkanNativeFrameResult Render(ulong frameId, bool capture)
    {
        if (ToggleInspectorRequested)
        {
            ToggleInspectorRequested = false;
            inspectorVisible = !inspectorVisible;
            hudKey = null;
        }
        PrepareFog();
        PrepareHud();
        using VulkanNativeFrameSession frame = target.BeginFrame(new NativeFrameClearColor(0.125f, 0.239f, 0.212f, 1));
        frame.Present(profiles, DrawTerrain);
        frame.Present(fog, pass => pass.SubmitSemanticFog(new NativeSemanticFogSubmission(
            new Native2DRect(0, 78, Width, 532),
            Native2DUvRect.Full,
            fogTexture!.Value,
            new Native2DTint(22 / 255f, 42 / 255f, 36 / 255f, 1),
            .92f,
            .48f,
            .72f,
            .015f,
            (frameId % 240) / 240f)));
        frame.Present(profiles, DrawWorldObjects);
        frame.Present(shapes, pass =>
        {
            foreach (NativeAnalyticShapeSubmission submission in hudShapes)
            {
                pass.SubmitAnalyticShape(submission);
            }
            DrawMinimap(pass);
        });
        frame.Present(text, pass =>
        {
            foreach (NativeMsdfQuadSubmission submission in hudText)
            {
                pass.SubmitMsdfQuad(submission);
            }
        });
        frame.Present(profiles, pass =>
        {
            string portrait = session.Selection.Snapshot().Count == 0
                ? "hq"
                : StrategyRenderer.UnitAsset(session.Units.First(unit => session.Selection.Contains(unit.Id)).Kind);
            profileCache.Submit(assets[portrait], new ProfileNativeInstance(76, 740, .65f));
        });
        return frame.EndFrame(capture);
    }

    public void Present(ulong frameId)
    {
        presenter.Present(frameId);
    }

    public void Dispose()
    {
        if (fogTexture.HasValue)
        {
            fog.DisposeTexture(fogTexture.Value);
        }
        fontCache.Dispose();
        profileCache.Dispose();
        fog.Dispose();
        text.Dispose();
        profiles.Dispose();
        shapes.Dispose();
        presenter.Dispose();
        target.Dispose();
        swapchain.Dispose();
        surface.Dispose();
        plant.Dispose();
    }

    private void DrawTerrain(VulkanOrderedQuadRenderer pass)
    {
        for (int y = 0; y < StrategySession.MapSize; y++)
        {
            for (int x = 0; x < StrategySession.MapSize; x++)
            {
                ProfileNativeCompositionResource tile = x >= 19 && y != 12
                    ? waterTile
                    : x == 11 && y is >= 9 and <= 18 ? pathTile : landTile;
                var center = view.Screen(new SpatialPoint2D(x + .5, y + .5));
                profileCache.Submit(tile, new ProfileNativeInstance(center.X, center.Y, (float)view.Camera.Zoom));
            }
        }
    }

    private void DrawWorldObjects(VulkanOrderedQuadRenderer pass)
    {
        var objects = new List<(double Depth, int Id, string Asset, SpatialPoint2D Position, float Scale)>();
        for (int y = 1; y < 23; y++)
        {
            for (int x = 1; x < 23; x++)
            {
                if ((x * 13 + y * 7) % 11 == 0 && !(x is >= 7 and <= 16 && y is >= 8 and <= 16)
                    && session.Fog[x, y] != CellVisibility.Unknown)
                {
                    objects.Add((x + y, x + y * 24, "tree", new SpatialPoint2D(x + .3, y + .4), .8f));
                }
            }
        }
        foreach (ResourceNode node in session.Resources.Where(resource => resource.Amount > 0 && session.IsVisible(resource.Position)))
        {
            objects.Add((node.Position.X + node.Position.Y, node.Id,
                node.Kind == ResourceKind.Wood ? "tree" : "crystal", node.Position, 1));
        }
        foreach (StrategyBuilding building in session.Buildings)
        {
            objects.Add((building.Position.X + building.Position.Y, building.Id,
                building.Id == 200 ? "hq" : "production", building.Position, 1));
        }
        foreach (StrategyUnit unit in session.Units)
        {
            objects.Add((unit.Position.X + unit.Position.Y, unit.Id, StrategyRenderer.UnitAsset(unit.Kind), unit.Position, .9f));
        }
        if (session.EnemyHealth > 0 && session.IsVisible(StrategySession.EnemyPosition))
        {
            objects.Add((StrategySession.EnemyPosition.X + StrategySession.EnemyPosition.Y, 300, "marker",
                StrategySession.EnemyPosition, 1.1f));
        }
        foreach (var item in objects.OrderBy(item => item.Depth).ThenBy(item => item.Id))
        {
            var point = view.Screen(item.Position);
            profileCache.Submit(assets[item.Asset], new ProfileNativeInstance(
                point.X,
                point.Y,
                (float)view.Camera.Zoom * item.Scale));
        }
        if (view.Placing)
        {
            var point = view.Screen(view.Pointer);
            profileCache.Submit(assets["production"], new ProfileNativeInstance(point.X, point.Y, (float)view.Camera.Zoom));
        }
    }

    private void PrepareFog()
    {
        bool changed = !hasFogProjection
            || lastFogCameraX != view.Camera.Position.X
            || lastFogCameraY != view.Camera.Position.Y
            || lastFogZoom != view.Camera.Zoom;
        for (int y = 0; y < StrategySession.MapSize; y++)
        {
            for (int x = 0; x < StrategySession.MapSize; x++)
            {
                int index = y * StrategySession.MapSize + x;
                byte value = (byte)session.Fog[x, y];
                if (lastVisibility[index] != value)
                {
                    lastVisibility[index] = value;
                    changed = true;
                }
            }
        }
        if (!changed)
        {
            return;
        }
        const int width = 320;
        const int height = 133;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float screenX = (x + .5f) * Width / width;
                float screenY = 78 + ((y + .5f) * 532 / height);
                SpatialPoint2D world = view.World(screenX, screenY);
                int cellX = (int)Math.Floor(world.X);
                int cellY = (int)Math.Floor(world.Y);
                CellVisibility visibility = cellX is >= 0 and < StrategySession.MapSize
                    && cellY is >= 0 and < StrategySession.MapSize
                        ? session.Fog[cellX, cellY]
                        : CellVisibility.Unknown;
                byte value = visibility switch
                {
                    CellVisibility.Visible => 255,
                    CellVisibility.Explored => 128,
                    _ => 0,
                };
                int offset = ((y * width) + x) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }
        Native2DTextureHandle replacement = fog.CreateTexture(width, height, pixels);
        if (fogTexture.HasValue)
        {
            fog.DisposeTexture(fogTexture.Value);
        }
        fogTexture = replacement;
        lastFogCameraX = view.Camera.Position.X;
        lastFogCameraY = view.Camera.Position.Y;
        lastFogZoom = view.Camera.Zoom;
        hasFogProjection = true;
    }

    private void PrepareHud()
    {
        StrategyHudSnapshot snapshot = StrategyRenderer.ProjectHud(session, view);
        string key = System.Text.Json.JsonSerializer.Serialize(new { snapshot, inspectorVisible });
        if (key == hudKey)
        {
            return;
        }
        var shapesResult = new List<NativeAnalyticShapeSubmission>();
        var textResult = new List<NativeMsdfQuadSubmission>();
        AppendUi(StrategyHudProfile.Build(snapshot, StrategyHudStyle.Woodland), shapesResult, textResult);
        if (inspectorVisible)
        {
            AppendUi(BuildInspector(), shapesResult, textResult);
        }
        hudShapes = shapesResult.ToArray();
        hudText = textResult.ToArray();
        hudKey = key;
    }

    private UiNode BuildInspector()
    {
        var skin = StrategyHudStyle.Woodland;
        UiNode card = UI.Rect(id: "inspector-card", style: new UiStyle(Background: ColorToken.Hex(0x101E1CF4)));
        UiNode title = UI.Text("NATIVE INSPECTOR / F10", id: "inspector-title", color: ColorToken.Hex(skin.Accent), size: TextSize.Sm);
        UiNode line1 = UI.Text($"PROFILE CACHE  {profileCache.Count} resources / {profileCache.UploadCount} uploads",
            id: "inspector-profile", color: ColorToken.Hex(skin.Text), size: TextSize.Sm);
        UiNode line2 = UI.Text($"FOG FIELD      {session.Fog.ExploredCount} explored cells",
            id: "inspector-fog", color: ColorToken.Hex(skin.Text), size: TextSize.Sm);
        UiNode line3 = UI.Text("AUTHORITY      app facts -> projection -> Vulkan",
            id: "inspector-authority", color: ColorToken.Hex(skin.Muted), size: TextSize.Sm);
        return UI.Surface(
            id: "strategy-inspector",
            width: Width,
            height: Height,
            children:
            [
                UI.Anchor(card, id: "inspector-card-slot", left: 930, top: 92, width: 326, height: 122),
                UI.Anchor(title, id: "inspector-title-slot", left: 948, top: 106, width: 285, height: 20),
                UI.Anchor(line1, id: "inspector-profile-slot", left: 948, top: 137, width: 285, height: 20),
                UI.Anchor(line2, id: "inspector-fog-slot", left: 948, top: 161, width: 285, height: 20),
                UI.Anchor(line3, id: "inspector-authority-slot", left: 948, top: 185, width: 285, height: 20),
            ]);
    }

    private void AppendUi(
        UiNode root,
        List<NativeAnalyticShapeSubmission> shapeOutput,
        List<NativeMsdfQuadSubmission> textOutput)
    {
        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(
            root,
            Width,
            Height,
            new UiLoweringOptions(font));
        foreach (MachinaPresentationOperation operation in prepared.PresentationFrame.Operations)
        {
            MachinaAnalyticShapePrimitive? shape = operation switch
            {
                MachinaAnalyticShapePrimitive analytic => analytic,
                FillRectangleOperation fill => new MachinaAnalyticShapePrimitive(
                    fill.SourceId,
                    MachinaAnalyticShapeKind.RoundedRect,
                    fill.Rect,
                    fill.Color),
                StrokeRectangleOperation stroke => new MachinaAnalyticShapePrimitive(
                    stroke.SourceId,
                    MachinaAnalyticShapeKind.RoundedRect,
                    stroke.Rect,
                    ColorToken.Hex(0x00000000),
                    borderColor: stroke.Color,
                    borderWidth: stroke.Thickness),
                _ => null,
            };
            if (shape is not null)
            {
                NativeAnalyticShapeSubmission? submission = AurelianAnalyticShapePresentationAdapter.Adapt(shape);
                if (submission.HasValue)
                {
                    shapeOutput.Add(submission.Value);
                }
                continue;
            }
            if (operation is PositionedTextOperation sourceText)
            {
                PositionedTextOperation qualified = font.Qualify(sourceText);
                AurelianMsdfTextPresentationAdapter.AdaptInto(
                    qualified,
                    font.ResourceFor(qualified),
                    fontCache,
                    textOutput);
            }
        }
    }

    private void DrawMinimap(VulkanOrderedQuadRenderer pass)
    {
        const float left = 1000;
        const float top = 659;
        const float minimapWidth = 232;
        const float minimapHeight = 100;
        const float width = 232f / StrategySession.MapSize;
        const float height = 100f / StrategySession.MapSize;
        for (int y = 0; y < StrategySession.MapSize; y++)
        {
            for (int x = 0; x < StrategySession.MapSize; x++)
            {
                uint color = session.Fog[x, y] switch
                {
                    CellVisibility.Unknown => 0x152A24FF,
                    CellVisibility.Explored => 0x3D5842FF,
                    _ => 0x7A9663FF,
                };
                Rect(pass, left + x * width, top + y * height, width + .3f, height + .3f, color);
            }
        }

        foreach (StrategyUnit unit in session.Units)
        {
            Circle(
                pass,
                left + ((float)unit.Position.X / StrategySession.MapSize * minimapWidth),
                top + ((float)unit.Position.Y / StrategySession.MapSize * minimapHeight),
                2.3f,
                0xF5E1A3FF);
        }
        foreach (ResourceNode node in session.Resources.Where(node => node.Amount > 0 && session.IsVisible(node.Position)))
        {
            Circle(
                pass,
                left + ((float)node.Position.X / StrategySession.MapSize * minimapWidth),
                top + ((float)node.Position.Y / StrategySession.MapSize * minimapHeight),
                2,
                0xA5E5D5FF);
        }

        (float X, float Y)[] viewport = [(0, 78), (Width, 78), (Width, 610), (0, 610)];
        (float X, float Y)[] minimapViewport = viewport
            .Select(point => view.World(point.X, point.Y))
            .Select(world => (
                left + ((float)world.X / StrategySession.MapSize * minimapWidth),
                top + ((float)world.Y / StrategySession.MapSize * minimapHeight)))
            .ToArray();
        for (int index = 0; index < minimapViewport.Length; index++)
        {
            DrawClippedMinimapLine(
                pass,
                minimapViewport[index],
                minimapViewport[(index + 1) % minimapViewport.Length],
                left,
                top,
                left + minimapWidth,
                top + minimapHeight);
        }
    }

    private static void DrawClippedMinimapLine(
        VulkanOrderedQuadRenderer pass,
        (float X, float Y) start,
        (float X, float Y) end,
        float left,
        float top,
        float right,
        float bottom)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y))));
        for (int index = 0; index <= steps; index++)
        {
            float amount = index / (float)steps;
            float x = start.X + ((end.X - start.X) * amount);
            float y = start.Y + ((end.Y - start.Y) * amount);
            if (x >= left && x < right && y >= top && y < bottom)
            {
                Rect(pass, x, y, 1, 1, 0xF1E6B8FF);
            }
        }
    }

    private static IReadOnlyDictionary<string, ProfileNativeCompositionResource> CompileAssets(string directory)
    {
        string toolkit = File.ReadAllText(Path.Combine(directory, "StrategyArt.ts"));
        var result = new Dictionary<string, ProfileNativeCompositionResource>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(directory, "*.profile.tsx").Order(StringComparer.Ordinal))
        {
            ProfileCompositionCompilationResult composition = ProfileTsxCompiler.CompileComposition(
                toolkit + "\n" + File.ReadAllText(path),
                path);
            if (!composition.Success)
            {
                throw new InvalidOperationException(string.Join("; ", composition.Diagnostics.Select(item => item.Message)));
            }
            ProfileNativeCompileResult native = ProfileNativeCompiler.Compile(composition.Composition!, path);
            if (!native.Success)
            {
                throw new InvalidOperationException(string.Join("; ", native.Diagnostics.Select(item => item.Message)));
            }
            result.Add(Path.GetFileName(path).Replace(".profile.tsx", "", StringComparison.Ordinal), native.Resource!);
        }
        if (result.Count != 9)
        {
            throw new InvalidOperationException("Mossward requires the complete nine-asset Profile pack.");
        }
        return result;
    }

    private static ProfileNativeCompositionResource CompileTile(string id, string fill)
    {
        string source = $$"""
            export default (Layers([
                Layer("Tile", [
                    Profile({ name: "{{id}}", shape: Polygon({ points: [[-24.0,0.0],[0.0,12.0],[24.0,0.0],[0.0,-12.0]] }), operations: [], yieldState: "Base", style: { fill: "{{fill}}" } })
                ])
            ]));
            """;
        ProfileCompositionCompilationResult composition = ProfileTsxCompiler.CompileComposition(source, $"native://{id}.profile.tsx");
        if (!composition.Success)
        {
            throw new InvalidOperationException(string.Join("; ", composition.Diagnostics.Select(item => item.Message)));
        }
        ProfileNativeCompileResult native = ProfileNativeCompiler.Compile(composition.Composition!, $"native://{id}.profile.tsx");
        if (!native.Success)
        {
            throw new InvalidOperationException(string.Join("; ", native.Diagnostics.Select(item => item.Message)));
        }
        return native.Resource!;
    }

    private static CompiledGraphicsProgram Compile(string root, string relativePath)
    {
        string source = File.ReadAllText(Path.Combine(root, relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
            new GpuCompilationRequest([new GpuSourceFile(relativePath, source)]));
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

    private static VulkanTextureFormat ParseSwapchainFormat(string format)
    {
        return format switch
        {
            "R8G8B8A8Unorm" => VulkanTextureFormat.Rgba8Unorm,
            "B8G8R8A8Unorm" => VulkanTextureFormat.Bgra8Unorm,
            "R8G8B8A8Srgb" => VulkanTextureFormat.Rgba8Srgb,
            "B8G8R8A8Srgb" => VulkanTextureFormat.Bgra8Srgb,
            _ => throw new NotSupportedException($"Mossward does not support swapchain format {format}."),
        };
    }

    private static void Rect(VulkanOrderedQuadRenderer pass, float x, float y, float width, float height, uint color)
    {
        Native2DTint tint = new(
            (color >> 24) / 255f,
            ((color >> 16) & 255) / 255f,
            ((color >> 8) & 255) / 255f,
            (color & 255) / 255f);
        pass.SubmitAnalyticShape(new NativeAnalyticShapeSubmission(
            new Native2DRect(x, y, width, height),
            new Native2DSize(width, height),
            Native2DUvRect.Full,
            NativeAnalyticShapeKind.RoundedRect,
            tint,
            0,
            tint,
            0));
    }

    private static void Circle(VulkanOrderedQuadRenderer pass, float centerX, float centerY, float radius, uint color)
    {
        Native2DTint tint = new(
            (color >> 24) / 255f,
            ((color >> 16) & 255) / 255f,
            ((color >> 8) & 255) / 255f,
            (color & 255) / 255f);
        float diameter = radius * 2;
        pass.SubmitAnalyticShape(new NativeAnalyticShapeSubmission(
            new Native2DRect(centerX - radius, centerY - radius, diameter, diameter),
            new Native2DSize(diameter, diameter),
            Native2DUvRect.Full,
            NativeAnalyticShapeKind.Circle,
            tint,
            radius,
            tint,
            0));
    }
}
