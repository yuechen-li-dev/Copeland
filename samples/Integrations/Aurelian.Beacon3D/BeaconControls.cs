using InputMan.Aurelian;
using InputMan.Core;

namespace Aurelian.Beacon3D;

public readonly record struct BeaconCommands(BeaconInput Movement, bool Restart, bool Quit);

/// <summary>InputMan owns bindings, held axes, action edges, and focus reset.</summary>
public sealed class BeaconControls : IDisposable
{
    private static readonly ActionMapId Map = new("Beacon3D");
    private static readonly AxisId Forward = new("Forward");
    private static readonly AxisId Strafe = new("Strafe");
    private static readonly AxisId Turn = new("Turn");
    private static readonly AxisId Look = new("Look");
    private static readonly ActionId Jump = new("Jump");
    private static readonly ActionId Restart = new("Restart");
    private static readonly ActionId Quit = new("Quit");
    private TimeSpan total;
    private ulong frameId;

    public BeaconControls()
    {
        Binding[] bindings =
        [
            Bind.ButtonAxis(Controls.Key(KeyboardKey.W), Forward, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.S), Forward, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.D), Strafe, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.A), Strafe, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowRight), Turn, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowLeft), Turn, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowUp), Look, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowDown), Look, -1),
            Bind.Action(Controls.Key(KeyboardKey.Space), Jump),
            Bind.Action(Controls.Key(KeyboardKey.R), Restart),
            Bind.Action(Controls.Key(KeyboardKey.Escape), Quit),
        ];
        Adapter = new AurelianInputAdapter(new InputManEngine(Input.Profile([Input.Map(Map, 0, bindings)])));
        Adapter.SetContexts(Map);
    }

    public AurelianInputAdapter Adapter { get; }

    public BeaconCommands Tick(float seconds)
    {
        TimeSpan elapsed = TimeSpan.FromSeconds(seconds);
        total += elapsed;
        Adapter.BeginFrame(new(++frameId, elapsed, total));
        InputFrame frame = Adapter.CurrentFrame;
        return new BeaconCommands(
            new BeaconInput(frame.GetAxis(Forward), frame.GetAxis(Strafe), frame.GetAxis(Turn),
                frame.GetAxis(Look), frame.WasPressed(Jump)),
            frame.WasPressed(Restart), frame.WasPressed(Quit));
    }

    public void Dispose()
    {
        Adapter.Dispose();
    }
}
