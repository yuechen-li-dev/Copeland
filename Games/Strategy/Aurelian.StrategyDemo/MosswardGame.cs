using Aurelian.Games;
using Aurelian.Spatial2D;
using InputMan.Core;

namespace Aurelian.StrategyDemo;

/// <summary>The strategy simulation and native 2D view, hosted by the common shell.</summary>
public sealed class MosswardGame : IGame
{
    private StrategySession world = new();
    private StrategyView view = new();
    private StrategyInput input;
    private NativeGameGraphics? graphics;
    private StrategyNativeRenderer? renderer;

    public MosswardGame()
    {
        view.Center(StrategySession.Home);
        input = new StrategyInput(world, view, shellInput: true);
    }

    public GameManifest Manifest { get; } = new("mossward", "MOSSWARD", new GameInputLayout(
    [
        new("select", "Select / drag units", GameControl.Mouse(MouseButton.Primary)),
        new("command", "Order units", GameControl.Mouse(MouseButton.Secondary)),
        new("additive", "Add to selection", GameControl.Key(KeyboardKey.LeftShift)),
        new("store", "Store control group", GameControl.Key(KeyboardKey.LeftControl)),
        new("build", "Place lodge", GameControl.Key(KeyboardKey.B)),
        new("recruit", "Recruit worker", GameControl.Key(KeyboardKey.N)),
        new("stop", "Stop units", GameControl.Key(KeyboardKey.S)),
        new("idle", "Select idle worker", GameControl.Key(KeyboardKey.I)),
        new("center", "Center selection", GameControl.Key(KeyboardKey.Space)),
        new("cancel", "Cancel placement / selection", GameControl.Key(KeyboardKey.C)),
        new("ranger", "Recruit ranger", GameControl.Key(KeyboardKey.F)),
        new("group1", "Control group 1", GameControl.Key(KeyboardKey.Number1)),
        new("group2", "Control group 2", GameControl.Key(KeyboardKey.Number2)),
        new("group3", "Control group 3", GameControl.Key(KeyboardKey.Number3)),
        new("pan.left", "Pan left", GameControl.Key(KeyboardKey.ArrowLeft)),
        new("pan.right", "Pan right", GameControl.Key(KeyboardKey.ArrowRight)),
        new("pan.up", "Pan up", GameControl.Key(KeyboardKey.ArrowUp)),
        new("pan.down", "Pan down", GameControl.Key(KeyboardKey.ArrowDown)),
        new("inspector", "Toggle inspector", GameControl.Key(KeyboardKey.F10)),
    ]) { Pointer = true })
    {
        Tagline = "GATHER CRYSTAL. BUILD A LODGE. GROW YOUR SETTLEMENT.",
        ControlsHint = "CLICK SELECT   RIGHT CLICK ORDER   B BUILD   N RECRUIT   C CANCEL   ESC PAUSE",
        DefaultDisplay = new(1280, 800),
    };

    public StrategySession World => world;

    public void NewGame()
    {
        input.Dispose();
        world = new StrategySession();
        view.Camera.SnapTo(new(-640, 0), new(1, 1));
        view.Camera.SetZoom(1, new(1, 1));
        view.Center(StrategySession.Home);
        view.DragStart = null;
        view.Placing = false;
        input = new StrategyInput(world, view, shellInput: true);
    }

    public void FixedUpdate(GameTick tick)
    {
        var pointer = tick.Input.Pointer;
        if (pointer.Present)
        {
            view.ScreenPointer = (pointer.Position.X * 1280 / pointer.Surface.X,
                pointer.Position.Y * 800 / pointer.Surface.Y);
        }
        if (tick.Input.WasPressed("inspector") && renderer is not null)
        {
            renderer.ToggleInspectorRequested = true;
        }
        input.Update(tick.Input, TimeSpan.FromSeconds(tick.Seconds), TimeSpan.FromSeconds(tick.Time));
        // Keep selection and camera input at the shell's 60 Hz; the existing rules advance at 10 Hz.
        if (tick.Index % 6 == 0)
        {
            world.Dispatch(new AdvanceIntent());
        }
    }

    public (double X, double Y)? ActionPoint(string action, GameViewRequest surface)
    {
        var unit = world.Units.FirstOrDefault(candidate => action == "unit." + candidate.Id);
        var resource = world.Resources.FirstOrDefault(candidate => action == "resource." + candidate.Id);
        SpatialPoint2D? position = unit?.Position ?? resource?.Position;
        if (position is not { } worldPoint)
        {
            return null;
        }
        var point = view.Screen(worldPoint);
        return (point.X / 1280.0, point.Y / 800.0);
    }

    public void OnFrame(GameFrame frame)
    {
        if (!frame.Playing)
        {
            view.DragStart = null;
        }
    }

    public void Observe(GameFacts facts)
    {
        facts.Set("hash", world.Hash());
        facts.Set("tick", world.Tick);
        facts.Set("selected", world.Selection.Snapshot().Count);
        facts.Set("units", world.Units.Count);
        facts.Set("placing", view.Placing);
        facts.Set("complete", world.ObjectiveComplete);
    }

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        this.graphics = graphics;
        graphics.Clear = new(.125f, .239f, .212f, 1);
        renderer = new StrategyNativeRenderer(graphics, () => world, view);
        graphics.Layers.Add(renderer);
    }

    public void DetachGraphics()
    {
        if (renderer is not null)
        {
            graphics?.Layers.Remove(renderer.Layer);
        }
        renderer = null;
        graphics = null;
    }

    public void Dispose() => input.Dispose();
}
