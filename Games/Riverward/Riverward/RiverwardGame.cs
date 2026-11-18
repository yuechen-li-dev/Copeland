using System.Globalization;
using System.Numerics;
using Aurelian.Games;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime;
using Aurelian.Strategy;
using InputMan.Core;

namespace Riverward;

internal sealed class RiverwardGame : IGame
{
    private static readonly KeyboardKey[] ToolKeys =
    [
        KeyboardKey.Number0, KeyboardKey.Number1, KeyboardKey.Number2, KeyboardKey.Number3,
        KeyboardKey.Number4, KeyboardKey.Number5, KeyboardKey.Number6,
        KeyboardKey.Number7, KeyboardKey.Number8, KeyboardKey.Number9,
    ];

    private City city = new();
    private CityView view = new();
    private Tool tool;
    private CityTarget? hover;
    private HexCell? selected;
    private Placement? preview;
    private int previewRevision = -1;
    private bool paused = true;
    private bool fast;
    private string notice = "";

    public RiverwardGame()
    {
        Saves = new GameSaveSchema<CitySnapshot>(RiverwardJson.Default.CitySnapshot, "riverward-river-town",
            () => city.Capture(), City.Validate, Restore)
            {
                Version = 2,
                Migrations = [new GameSaveMigration(1, state =>
                {
                    state["Streak"] = 0;
                    state["Won"] = false;
                    return state;
                })],
            };
    }

    public GameManifest Manifest { get; } = new("riverward", "Riverward", new GameInputLayout(
    [
        new("pan.north", "Pan up", GameControl.Key(KeyboardKey.W), GameControl.Key(KeyboardKey.ArrowUp)),
        new("pan.south", "Pan down", GameControl.Key(KeyboardKey.S), GameControl.Key(KeyboardKey.ArrowDown)),
        new("pan.west", "Pan left", GameControl.Key(KeyboardKey.A), GameControl.Key(KeyboardKey.ArrowLeft)),
        new("pan.east", "Pan right", GameControl.Key(KeyboardKey.D), GameControl.Key(KeyboardKey.ArrowRight)),
        new("orbit.left", "Rotate left", GameControl.Key(KeyboardKey.Q)),
        new("orbit.right", "Rotate right", GameControl.Key(KeyboardKey.E)),
        new("time.pause", "Pause city time", GameControl.Key(KeyboardKey.Space)),
        new("time.fast", "Change city speed", GameControl.Key(KeyboardKey.F)),
        .. ToolKeys.Select((key, index) => new GameInputAction($"tool.{index}", City.Label((Tool)index), GameControl.Key(key))),
    ]) { Pointer = true })
    {
        Tagline = "Build a town on both banks.",
        ControlsHint = "WASD pan   Q/E rotate   Wheel zoom   Space pause time",
        TickRate = 30,
        Graphics = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None,
            DirectDiffuseModel = DirectDiffuseModel3D.Lambert,
            SunDirection = Vector3.Normalize(new Vector3(-.55f, .84f, .32f)),
            SunIntensity = 2.45f,
            ShadowRadius = 43,
            ShadowDistance = 135,
            BloomIntensity = 0,
        },
    };

    public GameSaveSchema Saves { get; }

    public void NewGame()
    {
        city = new();
        ResetPresentation();
    }

    private void Restore(CitySnapshot snapshot)
    {
        city.Restore(snapshot);
        ResetPresentation();
    }

    private void ResetPresentation()
    {
        view = new();
        tool = Tool.Inspect;
        hover = null;
        selected = null;
        preview = null;
        previewRevision = -1;
        paused = true;
        fast = false;
        notice = "";
    }

    public void FixedUpdate(GameTick tick)
    {
        GameInputState input = tick.Input;
        GamePointer pointer = input.Pointer;
        bool click = pointer.WasPressed(GamePointerButton.Primary);
        if (input.WasPressed("time.pause"))
        {
            paused = !paused;
        }
        if (input.WasPressed("time.fast"))
        {
            fast = !fast;
        }
        if (!paused)
        {
            city.Advance(fast ? 3 : 1);
        }

        view.Move(input.Axis("pan.south", "pan.north"), input.Axis("pan.west", "pan.east"),
            input.Axis("orbit.left", "orbit.right"), pointer.Wheel.Y, tick.Seconds);
        for (int index = 0; index < ToolKeys.Length; index++)
        {
            if (input.WasPressed($"tool.{index}"))
            {
                SelectTool((Tool)index);
            }
        }
        if (pointer.WasPressed(GamePointerButton.Secondary))
        {
            SelectTool(Tool.Inspect);
        }

        CityTarget? target = null;
        if (pointer.Present && pointer.Surface.X > 0 && pointer.Surface.Y > 0)
        {
            double scale = GameHud.SurfaceScale(pointer.Surface);
            Vector2 logical = pointer.Position / (float)scale;
            var (width, height) = GameHud.SurfaceSize(pointer.Surface);
            var toolbar = CityHud.Toolbar(width, height);
            if (toolbar.Contains(logical))
            {
                if (click)
                {
                    SelectTool((Tool)((int)(logical.X - toolbar.Left) / CityHud.ButtonWidth));
                }
            }
            else if (!CityHud.BlocksMap(logical, width, height))
            {
                target = view.Pick(pointer.Position, pointer.Surface, tool);
            }
        }
        RefreshPreview(target);
        if (!click || hover is not { } cell)
        {
            return;
        }
        if (tool == Tool.Inspect)
        {
            selected = cell.Cell;
            return;
        }
        Placement result = city.Build(tool, cell);
        notice = result.Allowed ? $"Built  /  -${result.Cost}" : result.Message;
        if (result.Allowed)
        {
            selected = cell.Cell;
            RefreshPreview(cell);
        }
    }

    private void SelectTool(Tool next)
    {
        tool = next;
        notice = "";
        previewRevision = -1;
    }

    private void RefreshPreview(CityTarget? target)
    {
        if (target == hover && previewRevision == city.Revision)
        {
            return;
        }
        hover = target;
        preview = tool != Tool.Inspect && hover is { } picked ? city.Preview(tool, picked) : null;
        previewRevision = city.Revision;
    }

    public GameView View(GameViewRequest request) => view.Render(request, city, tool, hover, preview);

    public void BuildHud(GameHud hud) => CityHud.Draw(hud, city, tool, hover, selected, preview, paused, fast, notice);

    public (double X, double Y)? ActionPoint(string action, GameViewRequest surface)
    {
        Vector2 size = new(surface.Width, surface.Height);
        if (action.StartsWith("tool-", StringComparison.Ordinal)
            && Enum.TryParse(action.AsSpan(5), ignoreCase: true, out Tool button)
            && Enum.IsDefined(button))
        {
            var (width, height) = GameHud.SurfaceSize(size);
            var toolbar = CityHud.Toolbar(width, height);
            double scale = GameHud.SurfaceScale(size);
            return ((toolbar.Left + ((int)button + .5) * CityHud.ButtonWidth) * scale / size.X,
                (toolbar.Top + toolbar.Height * .5) * scale / size.Y);
        }
        string[] parts = action.Split(':');
        if (parts.Length is < 3 or > 4 || parts[0] != "hex"
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int q)
            || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out int r))
        {
            return null;
        }
        HexCell cell = new(q, r);
        if (!CityMap.Contains(cell))
        {
            return null;
        }
        Vector2 ground = cell.Center(CityMap.Radius);
        if (parts.Length == 4)
        {
            if (!int.TryParse(parts[3], CultureInfo.InvariantCulture, out int side) || side is < 0 or >= HexCell.SideCount)
            {
                return null;
            }
            HexEdge edge = cell.Edge(side);
            Vector2 midpoint = (edge.A.Position(CityMap.Radius) + edge.B.Position(CityMap.Radius)) * .5f;
            ground = Vector2.Lerp(midpoint, ground, .025f);
        }
        if (!Camera3D.TryProject(new(ground.X, 0, ground.Y), view.Camera(surface.Aspect).Matrix, size, out Vector2 pixel))
        {
            return null;
        }
        return (pixel.X / size.X, pixel.Y / size.Y);
    }

    public void Observe(GameFacts facts)
    {
        facts.Set("city.cash", city.Cash);
        facts.Set("city.month", city.Month);
        facts.Set("city.streets", city.Streets.Count);
        facts.Set("city.blocks", city.Blocks.Count);
        facts.Set("city.households", city.Report.Residents);
        facts.Set("city.housing", city.Report.Housing);
        facts.Set("city.jobs", city.Report.Jobs);
        facts.Set("city.employed", city.Report.Employed);
        facts.Set("city.balance", city.Report.Balance);
        facts.Set("city.happiness", city.Report.Happiness);
        facts.Set("city.ferry.stops", city.Report.FerryStops.Length);
        facts.Set("city.ferry.trips", city.Report.FerryTrips);
        facts.Set("city.streak", city.Streak);
        facts.Set("city.won", city.Won);
        facts.Set("city.paused", paused);
        facts.Set("tool", tool.ToString());
        facts.Set("hover", hover is { } target ? CellName(target.Cell) : "");
        facts.Set("preview.allowed", preview?.Allowed == true);
        facts.Set("preview.housing", preview?.Forecast?.Housing ?? city.Report.Housing);
        facts.Set("preview.happiness", preview?.Forecast?.Happiness ?? city.Report.Happiness);
        facts.Set("preview.ferry.stops", preview?.Forecast?.FerryStops.Length ?? city.Report.FerryStops.Length);
        facts.Set("preview.ferry.trips", preview?.Forecast?.FerryTrips ?? city.Report.FerryTrips);
        facts.Set("preview.improved", preview?.Improved?.Length ?? 0);
        facts.Set("preview.worsened", preview?.Worsened?.Length ?? 0);
        foreach ((HexCell cell, Block block) in city.Blocks)
        {
            string key = $"hex.{cell.Q}.{cell.R}";
            BlockReport status = city.Report.Blocks[cell];
            facts.Set(key, block.ToString());
            facts.Set($"{key}.access", status.Access);
            facts.Set($"{key}.residents", status.Residents);
            facts.Set($"{key}.appeal", status.Appeal);
            facts.Set($"{key}.capacity", status.Capacity);
            facts.Set($"{key}.customers", status.Customers);
            facts.Set($"{key}.happiness", status.Happiness);
            facts.Set($"{key}.ferry", status.Ferry);
            facts.Set($"{key}.trips", status.Trips);
            facts.Set($"{key}.net", status.Net);
        }
    }

    private static string CellName(HexCell cell) => $"{cell.Q},{cell.R}";

    public void Dispose() { }
}
