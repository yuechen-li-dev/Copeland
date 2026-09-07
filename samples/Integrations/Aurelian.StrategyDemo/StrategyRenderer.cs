using System.Runtime.InteropServices;
using Aurelian.GameWorld2D;
using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Aurelian.Spatial2D;
using Aurelian.Strategy;
using Machina.Pipeline;
using SkiaSharp;

namespace Aurelian.StrategyDemo;

public sealed class StrategyView
{
    private static readonly World2DUnitScale Scale = new(1, 1);
    public IsometricProjection Projection { get; } = new(48, 24);
    public Camera2D Camera { get; } = new(new(-640, 0), new(0, 78, 1280, 532), 1, new(-1500, -500, 3000, 2000));
    public SpatialPoint2D Pointer { get; set; } = new(14, 15);
    public bool Placing { get; set; }
    public bool Paused { get; set; }
    public (float X, float Y)? DragStart { get; set; }
    public (float X, float Y) ScreenPointer { get; set; }

    public SKPoint Screen(SpatialPoint2D point)
    {
        WorldPoint2 projected = Projection.Project(new(point.X, point.Y));
        return new((float)((projected.X - Camera.Position.X) * Camera.Zoom),
            (float)(78 + (projected.Y - Camera.Position.Y) * Camera.Zoom));
    }

    public SpatialPoint2D World(float x, float y)
    {
        WorldPoint2 point = Projection.Unproject(new(x / Camera.Zoom + Camera.Position.X, (y - 78) / Camera.Zoom + Camera.Position.Y));
        return new(point.X, point.Y);
    }

    public void Center(SpatialPoint2D point) => Camera.Follow(Projection.Project(new(point.X, point.Y)), Scale);

    public void Pan(double x, double y) => Camera.SnapTo(new(Camera.Position.X + x, Camera.Position.Y + y), Scale);

    public void Zoom(double amount) => Camera.SetZoom(Math.Clamp(Camera.Zoom + amount, .65, 1.7), Scale);

    public static bool OnMinimap(float x, float y) => x is >= 1000 and <= 1232 && y is >= 659 and <= 759;

    public void MinimapJump(float x, float y) => Center(new(Math.Clamp((x - 1000) / 232 * StrategySession.MapSize, 0, 23.99),
        Math.Clamp((y - 659) / 100 * StrategySession.MapSize, 0, 23.99)));
}

public sealed class StrategyRenderer : IDisposable
{
    public const int Width = StrategyHudProfile.Width;
    public const int Height = StrategyHudProfile.Height;
    private readonly StrategyAssets assets;
    private readonly HudTextRenderer text;
    private readonly SKPaint paint = new() { IsAntialias = true };
    private SKBitmap? hudBitmap;
    private string? hudKey;
    public StrategyHudStyle Style { get; set; } = StrategyHudStyle.Woodland;
    public StrategyHudSnapshot? HudOverride { get; set; }
    public StrategyAssets Assets => assets;

    public StrategyRenderer(string assetDirectory)
    {
        assets = new(assetDirectory);
        text = new(assetDirectory);
    }

    public SKBitmap Render(StrategySession session, StrategyView view)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#203d36"));
        canvas.Save();
        canvas.ClipRect(new(0, 78, Width, 610));
        for (int y = 0; y < StrategySession.MapSize; y++)
        {
            for (int x = 0; x < StrategySession.MapSize; x++)
            {
                DrawTile(canvas, session, view, x, y);
            }
        }
        var objects = new List<(double Depth, int Id, Action Draw)>();
        for (int y = 1; y < 23; y++)
        {
            for (int x = 1; x < 23; x++)
            {
                if ((x * 13 + y * 7) % 11 != 0 || x is >= 7 and <= 16 && y is >= 8 and <= 16)
                {
                    continue;
                }
                var point = new SpatialPoint2D(x + .3, y + .4);
                if (session.Fog[x, y] == CellVisibility.Unknown)
                {
                    continue;
                }
                SKPoint screen = view.Screen(point);
                objects.Add((x + y, x + y * 24, () => assets.Draw(canvas, "tree", screen.X, screen.Y, (float)view.Camera.Zoom * .8f)));
            }
        }
        foreach (ResourceNode node in session.Resources.Where(r => r.Amount > 0))
        {
            if (session.Fog[(int)node.Position.X, (int)node.Position.Y] != CellVisibility.Unknown)
            {
                Add(node.Position, node.Id, node.Kind == ResourceKind.Wood ? "tree" : "crystal", 1);
            }
        }
        foreach (StrategyBuilding building in session.Buildings)
        {
            Add(building.Position, building.Id, building.Id == 200 ? "hq" : "production", 1);
        }
        foreach (StrategyUnit unit in session.Units)
        {
            SKPoint point = view.Screen(unit.Position);
            if (session.Selection.Contains(unit.Id))
            {
                paint.Color = SKColor.Parse("#dfd991");
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.5f;
                canvas.DrawOval(point.X, point.Y, 16 * (float)view.Camera.Zoom, 7 * (float)view.Camera.Zoom, paint);
                paint.Style = SKPaintStyle.Fill;
            }
            Add(unit.Position, unit.Id, UnitAsset(unit.Kind), .9f);
        }
        if (session.EnemyHealth > 0 && session.IsVisible(StrategySession.EnemyPosition))
        {
            Add(StrategySession.EnemyPosition, 300, "marker", 1.1f);
        }
        foreach (var item in objects.OrderBy(item => item.Depth).ThenBy(item => item.Id))
        {
            item.Draw();
        }
        foreach (StrategyBuilding building in session.Buildings.Where(building => building.Progress < 100))
        {
            SKPoint point = view.Screen(building.Position);
            float width = 48 * (float)view.Camera.Zoom;
            paint.Color = SKColor.Parse("#19342d");
            canvas.DrawRect(point.X - width / 2, point.Y + 8, width, 5, paint);
            paint.Color = SKColor.Parse("#d8c589");
            canvas.DrawRect(point.X - width / 2, point.Y + 8, width * building.Progress / 100, 5, paint);
        }
        if (view.Placing)
        {
            SKPoint point = view.Screen(view.Pointer);
            bool valid = session.PlacementFailure(view.Pointer) is null;
            paint.Color = valid ? new SKColor(170, 220, 142, 160) : new SKColor(239, 115, 86, 180);
            canvas.DrawOval(point.X, point.Y, 60, 28, paint);
            assets.Draw(canvas, "production", point.X, point.Y, (float)view.Camera.Zoom);
        }
        if (view.DragStart is { } start)
        {
            paint.Color = new SKColor(223, 218, 158, 180);
            paint.Style = SKPaintStyle.Stroke;
            canvas.DrawRect(SKRect.Create(Math.Min(start.X, view.ScreenPointer.X), Math.Min(start.Y, view.ScreenPointer.Y),
                Math.Abs(start.X - view.ScreenPointer.X), Math.Abs(start.Y - view.ScreenPointer.Y)), paint);
            paint.Style = SKPaintStyle.Fill;
        }
        canvas.Restore();
        DrawHud(canvas, session, view);
        DrawMinimap(canvas, session, view);
        assets.Draw(canvas, session.Selection.Snapshot().Count == 0 ? "hq" : UnitAsset(session.Units.First(u => session.Selection.Contains(u.Id)).Kind), 76, 740, .65f);
        canvas.Flush();
        return bitmap;

        void Add(SpatialPoint2D position, int id, string asset, float scale)
        {
            SKPoint screen = view.Screen(position);
            objects.Add((position.X + position.Y, id, () => assets.Draw(canvas, asset, screen.X, screen.Y, (float)view.Camera.Zoom * scale)));
        }
    }

    public static string UnitAsset(UnitKind kind) => kind switch
    {
        UnitKind.Worker => "worker",
        UnitKind.Ranger => "ranger",
        _ => "heavy",
    };

    public static StrategyHudSnapshot ProjectHud(StrategySession session, StrategyView view)
    {
        int selected = session.Selection.Snapshot().Count;
        string detail = selected == 1 ? session.Units.Single(u => session.Selection.Contains(u.Id)).Phase : "Ready for orders";
        return new("MOSSWARD", "AN OUTPOST AT THE WATER'S EDGE",
            [new("CRYSTAL", session.Stock.ToString()), new("PEOPLE / CAPACITY", $"{session.Units.Count + session.Production.Count} / 12"),
                new("TIMBER", session.WoodStock.ToString()), new("LAND DISCOVERED", $"{session.Fog.ExploredCount * 100 / 576}%")],
            session.ObjectiveComplete ? "A home takes root" : "A place to begin",
            [$"Gather crystal   {session.Gathered} / 20", $"Raise a lodge   {session.Buildings.Count(b => b.Id != 200 && b.Progress == 100)} / 1", "Explore beyond the river"],
            selected == 0 ? "Settlement" : $"{selected} selected", detail,
            [new("B", "Raise lodge", "40 crystal", session.Stock >= 40),
                new("N / F", "Recruit", session.Production.Count == 0 ? "Worker / ranger" : $"Queued: {session.Production.Count}", session.Stock >= 25),
                new("S", "Stop", "Hold this ground", selected > 0), new("I", "Idle worker", "Find & select", true)],
            view.Placing ? session.PlacementFailure(view.Pointer) ?? "Clear ground. Click to commit the lodge." : session.Notice);
    }

    private void DrawHud(SKCanvas canvas, StrategySession session, StrategyView view)
    {
        StrategyHudSnapshot snapshot = HudOverride ?? ProjectHud(session, view);
        string key = System.Text.Json.JsonSerializer.Serialize(new { snapshot, Style });
        if (hudKey != key)
        {
            var prepared = new MachinaPresentationPipeline().Prepare(StrategyHudProfile.Build(snapshot, Style), Width, Height,
                new global::Machina.Core.Lowering.UiLoweringOptions(text));
            var panels = new global::Machina.Presentation.MachinaPresentationFrame(prepared.PresentationFrame.Viewport,
                prepared.PresentationFrame.Operations.Where(operation => operation is not global::Machina.Presentation.PositionedTextOperation));
            RasterFrame frame = new AurelianCpuRasterRenderer().Render(MachinaPresentationTranslator.Translate(panels));
            var pixels = frame.Surface.CopyPixels();
            var bytes = new byte[pixels.Length * 4];
            for (int index = 0; index < pixels.Length; index++)
            {
                bytes[index * 4] = pixels[index].R;
                bytes[index * 4 + 1] = pixels[index].G;
                bytes[index * 4 + 2] = pixels[index].B;
                bytes[index * 4 + 3] = pixels[index].A;
            }
            hudBitmap?.Dispose();
            hudBitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            Marshal.Copy(bytes, 0, hudBitmap.GetPixels(), bytes.Length);
            using var hudCanvas = new SKCanvas(hudBitmap);
            text.Draw(hudCanvas, prepared.PresentationFrame.Operations.OfType<global::Machina.Presentation.PositionedTextOperation>());
            hudKey = key;
        }
        canvas.DrawBitmap(hudBitmap!, 0, 0);
    }

    private void DrawTile(SKCanvas canvas, StrategySession session, StrategyView view, int x, int y)
    {
        CellVisibility visibility = session.Fog[x, y];
        bool water = x >= 19 && y != 12;
        int variant = (x * 17 + y * 23) % 13;
        SKColor color = water ? new SKColor(67, (byte)(110 + variant), 110)
            : new SKColor((byte)(96 + variant), (byte)(123 + variant), (byte)(76 + variant));
        if (x == 11 && y is >= 9 and <= 18)
        {
            color = new(149, 148, 99);
        }
        if (visibility == CellVisibility.Unknown)
        {
            color = new(34, 61, 48);
        }
        else if (visibility == CellVisibility.Explored)
        {
            color = new((byte)(color.Red * .55), (byte)(color.Green * .6), (byte)(color.Blue * .6));
        }
        paint.Color = color;
        using var path = new SKPath();
        path.MoveTo(view.Screen(new(x, y)));
        path.LineTo(view.Screen(new(x + 1.01, y)));
        path.LineTo(view.Screen(new(x + 1.01, y + 1.01)));
        path.LineTo(view.Screen(new(x, y + 1.01)));
        path.Close();
        canvas.DrawPath(path, paint);
        if (visibility == CellVisibility.Visible && variant % 3 == 0)
        {
            SKPoint point = view.Screen(new(x + .4, y + .7));
            paint.Color = new SKColor(174, 191, 129, 100);
            canvas.DrawOval(point.X, point.Y, 2, 1, paint);
        }
    }

    public void DrawMinimap(SKCanvas canvas, StrategySession session, StrategyView view)
    {
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                paint.Color = session.Fog[x, y] switch
                {
                    CellVisibility.Unknown => new SKColor(21, 42, 36),
                    CellVisibility.Explored => new SKColor(61, 88, 66),
                    _ => new SKColor(122, 150, 99),
                };
                canvas.DrawRect(1000 + x * 232f / 24, 659 + y * 100f / 24, 232f / 24 + .3f, 100f / 24 + .3f, paint);
            }
        }
        paint.Color = new(245, 225, 163);
        foreach (StrategyUnit unit in session.Units)
        {
            canvas.DrawCircle(1000 + (float)unit.Position.X / 24 * 232, 659 + (float)unit.Position.Y / 24 * 100, 2.3f, paint);
        }
        paint.Color = new(165, 229, 213);
        foreach (ResourceNode node in session.Resources.Where(node => node.Amount > 0 && session.IsVisible(node.Position)))
        {
            canvas.DrawCircle(1000 + (float)node.Position.X / 24 * 232, 659 + (float)node.Position.Y / 24 * 100, 2, paint);
        }
        using var viewport = new SKPath();
        foreach ((float x, float y) in new[] { (0f, 78f), (1280f, 78f), (1280f, 610f), (0f, 610f) })
        {
            SpatialPoint2D world = view.World(x, y);
            SKPoint point = new(1000 + (float)world.X / 24 * 232, 659 + (float)world.Y / 24 * 100);
            if (viewport.IsEmpty)
            {
                viewport.MoveTo(point);
            }
            else
            {
                viewport.LineTo(point);
            }
        }
        viewport.Close();
        canvas.Save();
        canvas.ClipRect(new(1000, 659, 1232, 759));
        paint.Color = new(241, 230, 184);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        canvas.DrawPath(viewport, paint);
        paint.Style = SKPaintStyle.Fill;
        canvas.Restore();
    }

    public void Dispose()
    {
        assets.Dispose();
        text.Dispose();
        paint.Dispose();
        hudBitmap?.Dispose();
    }
}
