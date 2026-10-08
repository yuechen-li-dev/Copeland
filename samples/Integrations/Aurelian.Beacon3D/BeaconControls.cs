using InputMan.Aurelian;
using InputMan.Core;
using Aurelian.GameMenus;

namespace Aurelian.Beacon3D;

public readonly record struct BeaconCommands(BeaconInput Movement, bool Pause, GameMenuInput Menu = default);

/// <summary>InputMan owns bindings, held axes, action edges, and focus reset.</summary>
public sealed class BeaconControls : IDisposable
{
    private static readonly ActionMapId Map = new("Beacon3D");
    private static readonly AxisId Forward = new("Forward");
    private static readonly AxisId Strafe = new("Strafe");
    private static readonly AxisId Turn = new("Turn");
    private static readonly AxisId Look = new("Look");
    private static readonly ActionId Jump = new("Jump");
    private static readonly ActionId Reload = new("Reload");
    private static readonly ActionId Pause = new("Pause");
    private static readonly ActionId Fire = new("Fire");
    private static readonly AxisId MouseYaw = new("MouseYaw");
    private static readonly AxisId MousePitch = new("MousePitch");
    public const float MouseSensitivity = 0.0025f;
    private TimeSpan total;
    private ulong frameId;

    public BeaconControls()
    {
        Binding[] bindings =
        [
            Bind.DeltaAxis(Controls.Mouse(MouseAxis.DeltaX), MouseYaw, MouseSensitivity),
            Bind.DeltaAxis(Controls.Mouse(MouseAxis.DeltaY), MousePitch, -MouseSensitivity),
            Bind.Action(Controls.Mouse(MouseButton.Primary), Fire, ButtonEdge.Down),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.W), Forward, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.S), Forward, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.D), Strafe, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.A), Strafe, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowRight), Turn, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowLeft), Turn, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowUp), Look, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowDown), Look, -1),
            Bind.Action(Controls.Key(KeyboardKey.Space), Jump),
            Bind.Action(Controls.Key(KeyboardKey.R), Reload),
            Bind.Action(Controls.Key(KeyboardKey.Escape), Pause),
        ];
        Adapter = new AurelianInputAdapter(new InputManEngine(Input.Profile(
            [Input.Map(Map, 0, bindings), Input.Map(GameMenuBindings.Map, 100, GameMenuBindings.CreateBindings())])));
        Adapter.SetContexts(Map);
    }

    public AurelianInputAdapter Adapter { get; }

    public BeaconCommands Tick(float seconds, bool menuActive = false)
    {
        TimeSpan elapsed = TimeSpan.FromSeconds(seconds);
        total += elapsed;
        Adapter.SetContexts(menuActive ? GameMenuBindings.Map : Map);
        Adapter.BeginFrame(new(++frameId, elapsed, total));
        InputFrame frame = Adapter.CurrentFrame;
        return new BeaconCommands(
            new BeaconInput(frame.GetAxis(Forward), frame.GetAxis(Strafe), frame.GetAxis(Turn),
                frame.GetAxis(Look), frame.WasPressed(Jump), frame.GetAxis(MouseYaw), frame.GetAxis(MousePitch),
                frame.IsDown(Fire), frame.WasPressed(Reload)),
            frame.WasPressed(Pause), GameMenuBindings.Read(frame));
    }

    public void Dispose()
    {
        Adapter.Dispose();
    }
}
