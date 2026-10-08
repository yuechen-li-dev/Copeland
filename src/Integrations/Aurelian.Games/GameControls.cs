using InputMan.Aurelian;
using InputMan.Core;
using Aurelian.GameMenus;

namespace Aurelian.Games;

public sealed record GameKeyBindings(
    KeyboardKey Forward = KeyboardKey.W, KeyboardKey Backward = KeyboardKey.S,
    KeyboardKey Left = KeyboardKey.A, KeyboardKey Right = KeyboardKey.D,
    KeyboardKey Jump = KeyboardKey.Space, KeyboardKey Reload = KeyboardKey.R,
    KeyboardKey SwitchView = KeyboardKey.V, float MouseSensitivity = 0.0025f);

public readonly record struct GameCommands(float Forward, float Strafe, float Turn, float Look,
    bool Jump, float MouseYaw, float MousePitch, bool Fire, bool Reload, bool SwitchView,
    bool Pause, GameMenuInput Menu);

/// <summary>Shared 3D bindings. Silk only supplies physical events to InputMan.</summary>
public sealed class GameControls : IDisposable
{
    private static readonly ActionMapId Map = new("Aurelian.Gameplay3D");
    private static readonly AxisId Forward = new("Forward");
    private static readonly AxisId Strafe = new("Strafe");
    private static readonly AxisId Turn = new("Turn");
    private static readonly AxisId Look = new("Look");
    private static readonly AxisId MouseYaw = new("MouseYaw");
    private static readonly AxisId MousePitch = new("MousePitch");
    private static readonly ActionId Jump = new("Jump");
    private static readonly ActionId Reload = new("Reload");
    private static readonly ActionId SwitchView = new("SwitchView");
    private static readonly ActionId Pause = new("Pause");
    private static readonly ActionId Fire = new("Fire");
    private readonly InputManEngine engine;
    private TimeSpan total;
    private ulong sequence;

    public GameControls(GameKeyBindings? keys = null)
    {
        Keys = keys ?? new();
        Validate(Keys);
        engine = new InputManEngine(BuildProfile(Keys));
        Adapter = new AurelianInputAdapter(engine);
        Adapter.SetContexts(Map);
    }

    public void Rebind(GameKeyBindings keys)
    {
        Validate(keys);
        Adapter.OnFocusChanged(false);
        engine.ImportProfile(BuildProfile(keys));
        Adapter.OnFocusChanged(true);
        Keys = keys;
    }

    private static InputProfile BuildProfile(GameKeyBindings keys)
    {
        Binding[] bindings =
        [
            Bind.DeltaAxis(Controls.Mouse(MouseAxis.DeltaX), MouseYaw, keys.MouseSensitivity),
            Bind.DeltaAxis(Controls.Mouse(MouseAxis.DeltaY), MousePitch, -keys.MouseSensitivity),
            Bind.Action(Controls.Mouse(MouseButton.Primary), Fire, ButtonEdge.Down),
            Bind.ButtonAxis(Controls.Key(keys.Forward), Forward, 1),
            Bind.ButtonAxis(Controls.Key(keys.Backward), Forward, -1),
            Bind.ButtonAxis(Controls.Key(keys.Right), Strafe, 1),
            Bind.ButtonAxis(Controls.Key(keys.Left), Strafe, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowRight), Turn, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowLeft), Turn, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowUp), Look, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowDown), Look, -1),
            Bind.Action(Controls.Key(keys.Jump), Jump),
            Bind.Action(Controls.Key(keys.Reload), Reload),
            Bind.Action(Controls.Key(keys.SwitchView), SwitchView),
            Bind.Action(Controls.Key(KeyboardKey.Escape), Pause),
        ];
        return Input.Profile(
            [Input.Map(Map, 0, bindings), Input.Map(GameMenuBindings.Map, 100, GameMenuBindings.CreateBindings())]);
    }

    public GameKeyBindings Keys { get; private set; }
    public AurelianInputAdapter Adapter { get; }

    public static void Validate(GameKeyBindings keys)
    {
        KeyboardKey[] values = [keys.Forward, keys.Backward, keys.Left, keys.Right, keys.Jump, keys.Reload, keys.SwitchView];
        if (values.Any(key => !Enum.IsDefined(key) || key == KeyboardKey.Unknown) || values.Distinct().Count() != values.Length ||
            values.Any(key => key is KeyboardKey.Escape or KeyboardKey.ArrowUp or KeyboardKey.ArrowDown or KeyboardKey.ArrowLeft or KeyboardKey.ArrowRight) ||
            !float.IsFinite(keys.MouseSensitivity) || keys.MouseSensitivity is <= 0 or > 1)
        {
            throw new ArgumentException("Gameplay bindings must use distinct keys; Escape and arrows are reserved. Sensitivity must be finite and positive.");
        }
    }

    public GameCommands Tick(float seconds, bool menuActive = false)
    {
        TimeSpan elapsed = TimeSpan.FromSeconds(seconds);
        total += elapsed;
        Adapter.SetContexts(menuActive ? GameMenuBindings.Map : Map);
        Adapter.BeginFrame(new(++sequence, elapsed, total));
        InputFrame frame = Adapter.CurrentFrame;
        return new(frame.GetAxis(Forward), frame.GetAxis(Strafe), frame.GetAxis(Turn), frame.GetAxis(Look),
            frame.WasPressed(Jump), frame.GetAxis(MouseYaw), frame.GetAxis(MousePitch), frame.IsDown(Fire),
            frame.WasPressed(Reload), frame.WasPressed(SwitchView), frame.WasPressed(Pause), GameMenuBindings.Read(frame));
    }

    public void Dispose() => Adapter.Dispose();
}

