using Aurelian.Games;
using Aurelian.Composition;
using Aurelian.NativeComposition;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Machina;
using Aurelian.Machina.Graphics;
using Aurelian.Profile.Graphics;
using Aurelian.Spatial2D;
using Aurelian.Strategy;
using Copeland.Profile;
using Copeland.TS.Profiles;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation;

namespace Aurelian.StrategyDemo;

internal sealed class StrategyNativeRenderer : INativeLayerPresenter
{
    private const int Width = StrategyHudProfile.Width;
    private const int Height = StrategyHudProfile.Height;
    private readonly Func<StrategySession> getSession;
    private StrategySession session => getSession();
    private readonly StrategyView view;
    private readonly AurelianVulkanPlant plant;
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

    public bool ToggleInspectorRequested { get; set; }

    public StrategyNativeRenderer(
        NativeGameGraphics graphics,
        Func<StrategySession> getSession,
        StrategyView view)
    {
        this.getSession = getSession;
        this.view = view;
        string assetDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        font = StrategyNativeUiFont.Create(assetDirectory);
        assets = CompileAssets(assetDirectory);
        landTile = CompileTile("land", "#667f50");
        pathTile = CompileTile("path", "#959463");
        waterTile = CompileTile("water", "#43706e");
        plant = graphics.Plant;
        var target = graphics.Target;
        Device = plant.Facts.PhysicalDeviceName;
        var shaderAssets = new GameAssets();
        shapes = new VulkanOrderedQuadRenderer(
            plant,
            shaderAssets.Shader("AnalyticShape2D.v.ts"),
            target,
            Native2DPipelineOptions.AnalyticShape2D);
        profiles = new VulkanOrderedQuadRenderer(
            plant,
            shaderAssets.Shader("ProfileMsdf.v.ts"),
            target,
            Native2DPipelineOptions.ProfileMsdf);
        text = new VulkanOrderedQuadRenderer(
            plant,
            shaderAssets.Shader("MsdfText.v.ts"),
            target,
            Native2DPipelineOptions.MsdfText);
        fog = new VulkanOrderedQuadRenderer(
            plant,
            shaderAssets.Shader("SemanticFog.v.ts"),
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
        foreach (var renderer in new[] { shapes, profiles, text, fog })
        {
            renderer.SetLogicalSize(Width, Height);
        }
    }

    public string Device { get; }
    public int ProfileUploads => profileCache.UploadCount;
    public int FontUploads => fontCache.UploadCount;

    public LayerId Layer { get; } = new("mossward.world");

    public void Attach(VulkanNativeFrameTarget target) => Resize(target);

    public void Resize(VulkanNativeFrameTarget target)
    {
        foreach (var renderer in new[] { shapes, profiles, text, fog })
        {
            renderer.Retarget(target);
        }
    }

    public void Present(NativeLayerFrameContext frame)
    {
        if (ToggleInspectorRequested)
        {
            ToggleInspectorRequested = false;
            inspectorVisible = !inspectorVisible;
            hudKey = null;
        }
        PrepareFog();
        PrepareHud();
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
            (frame.FrameId % 240) / 240f)));
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
    }

    public void Detach()
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
