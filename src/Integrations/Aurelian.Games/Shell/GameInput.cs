using System.Numerics;
using System.Text.RegularExpressions;
using Aurelian.GameHost;
using Aurelian.GameMenus;
using InputMan.Aurelian;
using InputMan.Core;
using InputMan.Core.Rebind;

namespace Aurelian.Games;

/// <summary>A physical control a game action can be bound to. Persisted as data in shell preferences.</summary>
public sealed record GameControl(DeviceKind Device, int Code)
{
    public static GameControl Key(KeyboardKey key) => new(DeviceKind.Keyboard, (int)key);
    public static GameControl Mouse(MouseButton button) => new(DeviceKind.Mouse, (int)button);
    public static GameControl Gamepad(GamepadButton button) => new(DeviceKind.Gamepad, (int)button);

    public ControlKey ToControlKey() => new(Device, 0, Code);

    public static GameControl From(ControlKey key) => new(key.Device, key.Code);

    public bool IsValid => Device switch
    {
        DeviceKind.Keyboard => Enum.IsDefined((KeyboardKey)Code) && (KeyboardKey)Code != KeyboardKey.Unknown,
        DeviceKind.Mouse => Enum.IsDefined((MouseButton)Code),
        DeviceKind.Gamepad => Enum.IsDefined((GamepadButton)Code),
        _ => false,
    };

    public string DisplayName => Device switch
    {
        DeviceKind.Keyboard => KeyName((KeyboardKey)Code),
        DeviceKind.Mouse => (MouseButton)Code switch
        {
            MouseButton.Primary => "Left mouse",
            MouseButton.Secondary => "Right mouse",
            MouseButton.Middle => "Middle mouse",
            MouseButton other => "Mouse " + other,
        },
        DeviceKind.Gamepad => "Pad " + (GamepadButton)Code,
        _ => "?",
    };

    private static string KeyName(KeyboardKey key) => key switch
    {
        KeyboardKey.LeftShift => "Left Shift",
        KeyboardKey.RightShift => "Right Shift",
        KeyboardKey.LeftControl => "Left Ctrl",
        KeyboardKey.RightControl => "Right Ctrl",
        KeyboardKey.LeftAlt => "Left Alt",
        KeyboardKey.RightAlt => "Right Alt",
        KeyboardKey.ArrowUp => "Up",
        KeyboardKey.ArrowDown => "Down",
        KeyboardKey.ArrowLeft => "Left",
        KeyboardKey.ArrowRight => "Right",
        _ => key.ToString(),
    };
}

/// <summary>
/// One named game action. The primary control is rebindable from the shell's controls menu;
/// the optional secondary control is a fixed alternative (for example arrow keys beside WASD).
/// </summary>
public sealed record GameInputAction(string Id, string Label, GameControl Primary, GameControl? Secondary = null,
    bool Rebindable = true)
{
    /// <summary>The input context the action belongs to (see <see cref="GameInputLayout.Contexts"/>).</summary>
    public string Context { get; init; } = GameInputContext.Play;

    /// <summary>A fixed gamepad button for the action (the first connected pad), beside the keyboard and mouse controls.</summary>
    public GameControl? Gamepad { get; init; }
}

/// <summary>
/// A set of actions that is active together, such as free play, a dialogue box or an inventory screen. The game picks
/// the active contexts every frame (<see cref="IGame.InputContexts"/>). Higher priority contexts see a key first.
/// A modal context consumes the keys it binds, so a lower context never sees them (Space advances dialogue instead
/// of dodging), and it may bind Escape: while it is active, Escape goes to the game (closing its screen) instead of
/// opening the pause menu.
/// </summary>
public sealed record GameInputContext(string Id, int Priority = 0, bool Modal = false)
{
    /// <summary>The default context for free play.</summary>
    public const string Play = "play";
}

public enum GameLookMode
{
    /// <summary>No mouse look. The cursor stays free while playing.</summary>
    None,

    /// <summary>Relative mouse look with a captured cursor while playing; sensitivity and inversion come from shell settings.</summary>
    CapturedMouse,
}

/// <summary>Everything a game declares about its input. The shell owns devices, menus, pause, rebinding and persistence.</summary>
public sealed record GameInputLayout(IReadOnlyList<GameInputAction> Actions, GameLookMode Look = GameLookMode.None)
{
    /// <summary>Declared contexts. Games with a single context can leave the default.</summary>
    public IReadOnlyList<GameInputContext> Contexts { get; init; } = [new GameInputContext(GameInputContext.Play)];

    /// <summary>
    /// The game reads the free cursor while playing: position, buttons and wheel through
    /// <see cref="GameInputState.Pointer"/>, and typed text through <see cref="GameInputState.Typing"/>. Mouse buttons can
    /// also be bound to actions like any other control. Not available together with captured mouse look.
    /// </summary>
    public bool Pointer { get; init; }

    public GameInputContext Context(string id)
    {
        foreach (GameInputContext context in Contexts)
        {
            if (context.Id == id)
            {
                return context;
            }
        }
        throw new KeyNotFoundException($"The game input layout has no context '{id}'.");
    }

    private static readonly Regex IdPattern = new("^[a-z][a-z0-9]*([.-][a-z0-9]+)*$", RegexOptions.CultureInvariant);

    /// <summary>Escape always opens the pause menu and cannot be bound to a game action.</summary>
    public static GameControl PauseControl { get; } = GameControl.Key(KeyboardKey.Escape);

    public GameInputAction Action(string id)
    {
        foreach (GameInputAction action in Actions)
        {
            if (action.Id == id)
            {
                return action;
            }
        }
        throw new KeyNotFoundException($"The game input layout has no action '{id}'.");
    }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Actions);
        ArgumentNullException.ThrowIfNull(Contexts);
        if (!Enum.IsDefined(Look))
        {
            throw new ArgumentException("Unknown look mode.");
        }
        if (Pointer && Look == GameLookMode.CapturedMouse)
        {
            throw new ArgumentException("A layout cannot read the free pointer and capture the mouse for look at the same time.");
        }
        var contextIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameInputContext context in Contexts)
        {
            if (context is null || context.Id is null || !IdPattern.IsMatch(context.Id) || !contextIds.Add(context.Id))
            {
                throw new ArgumentException("Input context ids must be unique lowercase names such as 'play' or 'dialogue'.");
            }
            if (context.Priority is < 0 or > 50)
            {
                throw new ArgumentException($"Input context '{context.Id}' priority must be between 0 and 50.");
            }
        }
        var gamepadButtons = new HashSet<(string Context, GameControl Control)>();
        foreach (GameInputAction action in Actions)
        {
            if (action?.Gamepad is not null && !gamepadButtons.Add((action.Context, action.Gamepad)))
            {
                throw new ArgumentException($"{action.Gamepad.DisplayName} is bound to more than one action in '{action.Context}'.");
            }
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameInputAction action in Actions)
        {
            if (action is null || action.Id is null || !IdPattern.IsMatch(action.Id) || !ids.Add(action.Id))
            {
                throw new ArgumentException("Game action ids must be unique lowercase names such as 'jump' or 'move.forward'.");
            }
            if (string.IsNullOrWhiteSpace(action.Label))
            {
                throw new ArgumentException($"Game action '{action.Id}' needs a menu label.");
            }
            if (action.Context is null || !contextIds.Contains(action.Context))
            {
                throw new ArgumentException($"Game action '{action.Id}' names unknown input context '{action.Context}'.");
            }
            bool modal = Context(action.Context).Modal;
            ValidateControl(action.Id, action.Primary, modal);
            if (action.Secondary is not null)
            {
                ValidateControl(action.Id, action.Secondary, modal);
            }
            if (action.Gamepad is not null && (action.Gamepad.Device != DeviceKind.Gamepad || !action.Gamepad.IsValid))
            {
                throw new ArgumentException($"Game action '{action.Id}' gamepad control must be a gamepad button.");
            }
        }
    }

    private static void ValidateControl(string id, GameControl control, bool modal)
    {
        if (control is null || !control.IsValid)
        {
            throw new ArgumentException($"Game action '{id}' uses an unsupported control.");
        }
        if (control == PauseControl && !modal)
        {
            throw new ArgumentException($"Game action '{id}' cannot use Escape; outside modal contexts it opens the pause menu.");
        }
    }
}

/// <summary>A persisted rebinding of one action's primary control.</summary>
public sealed record GameBindingOverride(string Action, GameControl Control);

public enum GamePointerButton
{
    Primary,
    Secondary,
    Middle,
}

/// <summary>Text-editing keys delivered with typed text (InputMan has no binding for them).</summary>
public enum GameTextEdit
{
    Backspace,
    Delete,
    Tab,
}

/// <summary>One piece of keyboard typing, in the order it happened: either printable text or an editing key.</summary>
public readonly record struct GameTyping(string Text, GameTextEdit? Edit = null);

/// <summary>
/// The free cursor for one frame or tick, read through InputMan. <see cref="Position"/> is in surface pixels, the same
/// space as <see cref="GameViewRequest"/>, with the origin at the top left; <see cref="Surface"/> is that surface's
/// size, so a game that only runs headless still knows what the coordinates mean.
/// </summary>
public sealed class GamePointer
{
    private readonly IReadOnlySet<GamePointerButton> down;
    private readonly IReadOnlySet<GamePointerButton> pressed;
    private readonly IReadOnlySet<GamePointerButton> released;

    internal GamePointer(bool present, Vector2 position, Vector2 surface, Vector2 wheel,
        IReadOnlySet<GamePointerButton> down, IReadOnlySet<GamePointerButton> pressed, IReadOnlySet<GamePointerButton> released)
    {
        Present = present;
        Position = position;
        Surface = surface;
        Wheel = wheel;
        this.down = down;
        this.pressed = pressed;
        this.released = released;
    }

    internal static GamePointer None { get; } = new(false, Vector2.Zero, Vector2.Zero, Vector2.Zero,
        new HashSet<GamePointerButton>(), new HashSet<GamePointerButton>(), new HashSet<GamePointerButton>());

    /// <summary>False until the cursor has been seen over the window (and whenever the layout does not read it).</summary>
    public bool Present { get; }

    public Vector2 Position { get; }

    public Vector2 Surface { get; }

    /// <summary>Wheel movement since the previous frame or tick, in notches (Y positive away from the user).</summary>
    public Vector2 Wheel { get; }

    public bool IsDown(GamePointerButton button) => down.Contains(button);
    public bool WasPressed(GamePointerButton button) => pressed.Contains(button);
    public bool WasReleased(GamePointerButton button) => released.Contains(button);
}

/// <summary>Read-only input view handed to game code for one frame or one fixed simulation tick.</summary>
public sealed class GameInputState
{
    private readonly GameInputLayout layout;
    private readonly IReadOnlySet<string> down;
    private readonly IReadOnlySet<string> pressed;
    private readonly IReadOnlySet<string> released;

    internal GameInputState(GameInputLayout layout, IReadOnlySet<string> down, IReadOnlySet<string> pressed,
        IReadOnlySet<string> released, Vector2 look, GamePointer? pointer = null, IReadOnlyList<GameTyping>? typing = null,
        Vector2 leftStick = default, Vector2 rightStick = default)
    {
        this.layout = layout;
        this.down = down;
        this.pressed = pressed;
        this.released = released;
        Look = look;
        Pointer = pointer ?? GamePointer.None;
        Typing = typing ?? [];
        LeftStick = leftStick;
        RightStick = rightStick;
    }

    internal static GameInputState Empty(GameInputLayout layout) =>
        new(layout, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), Vector2.Zero);

    /// <summary>Mouse-look delta in radians (X = yaw, Y = pitch, positive up). Zero unless the layout uses mouse look.</summary>
    public Vector2 Look { get; }

    /// <summary>The first gamepad's left stick, X right and Y up, each in [-1, 1], with a radial dead zone applied.</summary>
    public Vector2 LeftStick { get; }

    /// <summary>The first gamepad's right stick, X right and Y up.</summary>
    public Vector2 RightStick { get; }

    /// <summary>The free cursor. Never null; not <see cref="GamePointer.Present"/> unless the layout reads the pointer.</summary>
    public GamePointer Pointer { get; }

    /// <summary>
    /// Text typed and editing keys pressed since the previous frame or tick, in order (layouts that read the pointer only).
    /// Keys still arrive as actions too; a game reading text switches to a context that binds none of them.
    /// </summary>
    public IReadOnlyList<GameTyping> Typing { get; }

    public bool IsDown(string action) => down.Contains(Known(action));
    public bool WasPressed(string action) => pressed.Contains(Known(action));
    public bool WasReleased(string action) => released.Contains(Known(action));

    /// <summary>-1, 0 or +1 from two opposing actions.</summary>
    public float Axis(string negative, string positive)
    {
        float value = 0;
        if (IsDown(negative))
        {
            value -= 1;
        }
        if (IsDown(positive))
        {
            value += 1;
        }
        return value;
    }

    /// <summary>A normalized 2D direction from four actions (X = right, Y = forward).</summary>
    public Vector2 Direction(string left, string right, string backward, string forward)
    {
        var value = new Vector2(Axis(left, right), Axis(backward, forward));
        if (value.LengthSquared() > 1)
        {
            value = Vector2.Normalize(value);
        }
        return value;
    }

    private string Known(string action)
    {
        _ = layout.Action(action);
        return action;
    }
}

public enum GameRebindStatus
{
    Idle,
    Waiting,
    Bound,
    Canceled,
    Rejected,
}

/// <summary>
/// Shell-owned input: one InputMan engine with a gameplay map built from the game's layout,
/// the shared menu map, the reserved pause action and data-level rebinding.
/// </summary>
public sealed class GameInputSystem : IDisposable
{
    private static readonly ActionMapId PauseMap = new("Aurelian.Shell.Pause");
    private static readonly ActionId PauseAction = new("Aurelian.Shell.Pause");
    private static readonly AxisId LookX = new("Aurelian.Shell.LookX");
    private static readonly AxisId LookY = new("Aurelian.Shell.LookY");
    private static readonly ActionMapId PointerMap = new("Aurelian.Game.Pointer");
    private static readonly ActionMapId SticksMap = new("Aurelian.Game.Sticks");
    private static readonly (GamepadAxis Axis, AxisId Id)[] StickAxes =
    [
        (GamepadAxis.LeftX, new AxisId("Aurelian.Stick.LeftX")),
        (GamepadAxis.LeftY, new AxisId("Aurelian.Stick.LeftY")),
        (GamepadAxis.RightX, new AxisId("Aurelian.Stick.RightX")),
        (GamepadAxis.RightY, new AxisId("Aurelian.Stick.RightY")),
    ];
    private const float StickDeadZone = .2f;
    private static readonly AxisId PointerX = new("Aurelian.Pointer.X");
    private static readonly AxisId PointerY = new("Aurelian.Pointer.Y");
    private static readonly AxisId WheelX = new("Aurelian.Pointer.WheelX");
    private static readonly AxisId WheelY = new("Aurelian.Pointer.WheelY");
    private static readonly (GamePointerButton Button, MouseButton Mouse, ActionId Action)[] PointerButtons =
    [
        (GamePointerButton.Primary, MouseButton.Primary, new ActionId("Aurelian.Pointer.Primary")),
        (GamePointerButton.Secondary, MouseButton.Secondary, new ActionId("Aurelian.Pointer.Secondary")),
        (GamePointerButton.Middle, MouseButton.Middle, new ActionId("Aurelian.Pointer.Middle")),
    ];
    private readonly InputManEngine engine;
    private readonly Dictionary<string, GameControl> primary = new(StringComparer.Ordinal);
    private readonly List<GameTyping> typing = [];
    private Vector2 pointerWindow = new(960, 600);
    private Vector2 pointerSurface = new(960, 600);
    private TimeSpan total;
    private ulong sequence;
    private IRebindSession? capture;
    private string? captureAction;
    private RebindResult? completedCapture;

    public GameInputSystem(GameInputLayout layout, GameControlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.Validate();
        Layout = layout;
        Settings = settings;
        ApplyOverrides(settings.Bindings);
        engine = new InputManEngine(BuildProfile());
        Adapter = new AurelianInputAdapter(engine);
        Adapter.SetContexts(GameMenuBindings.Map);
    }

    public GameInputLayout Layout { get; }
    public GameControlSettings Settings { get; private set; }
    public AurelianInputAdapter Adapter { get; }
    public GameRebindStatus RebindStatus { get; private set; }
    public string? RebindAction => captureAction;
    public string? RebindMessage { get; private set; }
    public bool Capturing => capture is not null || completedCapture is not null;

    public GameControl Binding(string action) => primary[Layout.Action(action).Id];

    /// <summary>The window size the device reports cursor positions in, and the pixel size of the surface they map to.</summary>
    public Vector2 PointerWindow => pointerWindow;
    public Vector2 PointerSurface => pointerSurface;

    /// <summary>
    /// Tell the input system how cursor coordinates map to surface pixels. Hosts call this on every resize; HiDPI windows
    /// report the cursor in window units while the framebuffer is larger.
    /// </summary>
    public void SetPointerSurface(Vector2 window, Vector2 surface)
    {
        if (window.X <= 0 || window.Y <= 0 || surface.X <= 0 || surface.Y <= 0)
        {
            return;
        }
        pointerWindow = window;
        pointerSurface = surface;
    }

    /// <summary>Text typed into the window, delivered to the game with the next gameplay frame (pointer layouts only).</summary>
    public void EnterText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string printable = new(text.Where(character => !char.IsControl(character)).ToArray());
        if (printable.Length > 0)
        {
            typing.Add(new GameTyping(printable));
        }
    }

    /// <summary>An editing key, delivered in order with typed text.</summary>
    public void EditText(GameTextEdit edit)
    {
        if (!Enum.IsDefined(edit))
        {
            throw new ArgumentOutOfRangeException(nameof(edit));
        }
        typing.Add(new GameTyping("", edit));
    }

    /// <summary>The persisted overrides: only actions whose primary control differs from the layout default.</summary>
    public IReadOnlyList<GameBindingOverride> Overrides()
    {
        var result = new List<GameBindingOverride>();
        foreach (GameInputAction action in Layout.Actions)
        {
            GameControl current = primary[action.Id];
            if (current != action.Primary)
            {
                result.Add(new GameBindingOverride(action.Id, current));
            }
        }
        return result;
    }

    /// <summary>Apply sensitivity, inversion and bindings atomically. Invalid settings leave the live profile unchanged.</summary>
    public void Apply(GameControlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var previous = new Dictionary<string, GameControl>(primary, StringComparer.Ordinal);
        GameControlSettings previousSettings = Settings;
        try
        {
            ApplyOverrides(settings.Bindings);
            Settings = settings;
            Reload();
        }
        catch
        {
            primary.Clear();
            foreach (KeyValuePair<string, GameControl> pair in previous)
            {
                primary[pair.Key] = pair.Value;
            }
            Settings = previousSettings;
            throw;
        }
    }

    /// <summary>Bind an action's primary control. A conflicting rebindable action receives this action's old control.</summary>
    public void Rebind(string action, GameControl control)
    {
        GameInputAction declared = Layout.Action(action);
        if (!declared.Rebindable)
        {
            throw new InvalidOperationException($"Game action '{action}' is not rebindable.");
        }
        if (control is null || !control.IsValid || control == GameInputLayout.PauseControl)
        {
            throw new ArgumentException("That control cannot be bound to a game action.", nameof(control));
        }
        GameControl old = primary[action];
        foreach (GameInputAction other in Layout.Actions)
        {
            // The same key may mean different things in different contexts; conflicts are per context.
            if (other.Id != action && other.Context == declared.Context && primary[other.Id] == control)
            {
                if (!other.Rebindable)
                {
                    throw new ArgumentException($"{control.DisplayName} is reserved by '{other.Label}'.", nameof(control));
                }
                primary[other.Id] = old;
            }
        }
        primary[action] = control;
        Settings = Settings with { Bindings = Overrides() };
        Reload();
    }

    public void ResetBindings()
    {
        ApplyOverrides([]);
        Settings = Settings with { Bindings = [] };
        Reload();
    }

    /// <summary>Wait for the next physical button and bind it to <paramref name="action"/>. Escape cancels.</summary>
    public void BeginRebind(string action)
    {
        GameInputAction declared = Layout.Action(action);
        if (!declared.Rebindable)
        {
            throw new InvalidOperationException($"Game action '{action}' is not rebindable.");
        }
        CancelRebind();
        var candidates = new List<ControlKey>();
        foreach (KeyboardKey key in Enum.GetValues<KeyboardKey>())
        {
            if (key != KeyboardKey.Unknown)
            {
                candidates.Add(Controls.Key(key));
            }
        }
        candidates.Add(Controls.Mouse(MouseButton.Primary));
        candidates.Add(Controls.Mouse(MouseButton.Secondary));
        candidates.Add(Controls.Mouse(MouseButton.Middle));
        // Escape stays a candidate so that pressing it ends capture; completion treats it as cancel.
        // Capture runs against the gameplay map, so the menu keys that started it are seeded as already held.
        Adapter.SetContexts(MapFor(declared.Context));
        capture = engine.StartRebind(new RebindRequest
        {
            Map = MapFor(declared.Context),
            BindingNameOrSlot = BindingName(action),
            CandidateButtons = candidates,
            DisallowConflictsInSameMap = false,
            ConflictPolicy = RebindConflictPolicy.Allow,
            Timeout = TimeSpan.FromSeconds(10),
        });
        captureAction = action;
        RebindStatus = GameRebindStatus.Waiting;
        RebindMessage = null;
        capture.OnCompleted += OnCaptureCompleted;
    }

    public void CancelRebind()
    {
        if (capture is null)
        {
            return;
        }
        completedCapture = null;
        IRebindSession session = capture;
        capture = null;
        session.OnCompleted -= OnCaptureCompleted;
        session.Cancel();
        captureAction = null;
        RebindStatus = GameRebindStatus.Canceled;
        Reload();
    }

    /// <summary>Advance InputMan for one host frame and read the logical result.</summary>
    internal GameInputFrame Frame(TimeSpan elapsed, bool gameplay, IReadOnlyCollection<string>? contexts = null)
    {
        total += elapsed;
        if (capture is null)
        {
            if (gameplay)
            {
                var maps = new List<ActionMapId> { PauseMap };
                foreach (string context in contexts ?? [GameInputContext.Play])
                {
                    maps.Add(MapFor(Layout.Context(context).Id));
                }
                if (Layout.Pointer)
                {
                    maps.Add(PointerMap);
                }
                maps.Add(SticksMap);
                Adapter.SetContexts([.. maps]);
            }
            else
            {
                Adapter.SetContexts(GameMenuBindings.Map);
            }
        }
        Adapter.BeginFrame(new AurelianHostFrame(++sequence, elapsed, total));
        InputFrame frame = Adapter.CurrentFrame;
        if (completedCapture is { } completed)
        {
            // Completion is raised inside InputMan's tick; profile replacement waits until the tick has finished.
            completedCapture = null;
            FinishCapture(completed);
            return new GameInputFrame(GameInputState.Empty(Layout), default, Pause: false);
        }
        // Typing is only for gameplay; anything typed over a shell menu is dropped.
        GameTyping[] typed = [.. typing];
        typing.Clear();
        if (capture is not null || !gameplay)
        {
            GameMenuInput menu = capture is null ? GameMenuBindings.Read(frame) : default;
            return new GameInputFrame(GameInputState.Empty(Layout), menu, Pause: false);
        }
        var down = new HashSet<string>(StringComparer.Ordinal);
        var pressed = new HashSet<string>(StringComparer.Ordinal);
        var released = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameInputAction action in Layout.Actions)
        {
            var id = new ActionId(ActionName(action.Id));
            if (frame.IsDown(id))
            {
                down.Add(action.Id);
            }
            if (frame.WasPressed(id))
            {
                pressed.Add(action.Id);
            }
            if (frame.WasReleased(id))
            {
                released.Add(action.Id);
            }
        }
        Vector2 look = Layout.Look == GameLookMode.CapturedMouse
            ? new Vector2(frame.GetAxis(LookX), frame.GetAxis(LookY))
            : Vector2.Zero;
        GamePointer? pointer = Layout.Pointer ? ReadPointer(frame) : null;
        Vector2 leftStick = Stick(frame, StickAxes[0].Id, StickAxes[1].Id);
        Vector2 rightStick = Stick(frame, StickAxes[2].Id, StickAxes[3].Id);
        var state = new GameInputState(Layout, down, pressed, released, look, pointer, Layout.Pointer ? typed : null,
            leftStick, rightStick);
        return new GameInputFrame(state, default, frame.WasPressed(PauseAction));
    }

    /// <summary>Pads report Y down; games get Y up. Inside the dead zone the stick is centred, and outside it the range is rescaled.</summary>
    private static Vector2 Stick(InputFrame frame, AxisId x, AxisId y)
    {
        var raw = new Vector2(frame.GetAxis(x), -frame.GetAxis(y));
        float length = raw.Length();
        if (length <= StickDeadZone)
        {
            return Vector2.Zero;
        }
        float scaled = Math.Min(1, (length - StickDeadZone) / (1 - StickDeadZone));
        return raw / length * scaled;
    }

    private GamePointer ReadPointer(InputFrame frame)
    {
        var down = new HashSet<GamePointerButton>();
        var pressed = new HashSet<GamePointerButton>();
        var released = new HashSet<GamePointerButton>();
        foreach ((GamePointerButton button, MouseButton _, ActionId action) in PointerButtons)
        {
            if (frame.IsDown(action))
            {
                down.Add(button);
            }
            if (frame.WasPressed(action))
            {
                pressed.Add(button);
            }
            if (frame.WasReleased(action))
            {
                released.Add(button);
            }
        }
        // InputMan reports window units; the game sees surface pixels.
        var scale = new Vector2(pointerSurface.X / pointerWindow.X, pointerSurface.Y / pointerWindow.Y);
        var position = new Vector2(frame.GetAxis(PointerX), frame.GetAxis(PointerY)) * scale;
        var wheel = new Vector2(frame.GetAxis(WheelX), frame.GetAxis(WheelY));
        return new GamePointer(Adapter.HasPointerPosition, position, pointerSurface, wheel, down, pressed, released);
    }

    public void OnFocusChanged(bool focused)
    {
        Adapter.OnFocusChanged(focused);
        if (!focused)
        {
            CancelRebind();
        }
    }

    /// <summary>Release held state so a screen change cannot leak a press into the next screen.</summary>
    public void ResetHeldState()
    {
        // The cursor has not moved; only held buttons and keys are released.
        bool hadPointer = Adapter.TryGetPointerPosition(out Vector2 cursor);
        Adapter.OnFocusChanged(false);
        Adapter.OnFocusChanged(true);
        if (hadPointer)
        {
            Adapter.RecordPointerPosition(cursor);
        }
        typing.Clear();
    }

    public void Dispose()
    {
        CancelRebind();
        Adapter.Dispose();
    }

    private void OnCaptureCompleted(RebindResult result)
    {
        completedCapture = result;
    }

    private void FinishCapture(RebindResult result)
    {
        string? action = captureAction;
        capture = null;
        captureAction = null;
        if (result.Succeeded && result.BoundControl is { } escape && GameControl.From(escape) == GameInputLayout.PauseControl)
        {
            RebindStatus = GameRebindStatus.Canceled;
            RebindMessage = "Binding unchanged.";
        }
        else if (result.Succeeded && result.BoundControl is { } bound && action is not null)
        {
            try
            {
                Rebind(action, GameControl.From(bound));
                RebindStatus = GameRebindStatus.Bound;
                RebindMessage = $"{Layout.Action(action).Label}: {GameControl.From(bound).DisplayName}";
                return;
            }
            catch (ArgumentException exception)
            {
                RebindStatus = GameRebindStatus.Rejected;
                RebindMessage = exception.Message;
            }
        }
        else
        {
            RebindStatus = GameRebindStatus.Canceled;
            RebindMessage = result.Error;
        }
        // The session may have edited the live profile; data remains the source of truth.
        Reload();
    }

    private void ApplyOverrides(IReadOnlyList<GameBindingOverride>? overrides)
    {
        var candidate = new Dictionary<string, GameControl>(StringComparer.Ordinal);
        foreach (GameInputAction action in Layout.Actions)
        {
            candidate[action.Id] = action.Primary;
        }
        foreach (GameBindingOverride item in overrides ?? [])
        {
            GameInputAction action = Layout.Actions.FirstOrDefault(declared => declared.Id == item?.Action)
                ?? throw new ArgumentException($"Saved binding names unknown action '{item?.Action}'.");
            if (!action.Rebindable || item.Control is null || !item.Control.IsValid || item.Control == GameInputLayout.PauseControl)
            {
                throw new ArgumentException($"Saved binding for '{item.Action}' is not allowed.");
            }
            candidate[action.Id] = item.Control;
        }
        var used = new HashSet<(string Context, GameControl Control)>();
        foreach (GameInputAction action in Layout.Actions)
        {
            if (!used.Add((action.Context, candidate[action.Id])))
            {
                throw new ArgumentException($"{candidate[action.Id].DisplayName} is bound to more than one action.");
            }
        }
        primary.Clear();
        foreach (KeyValuePair<string, GameControl> pair in candidate)
        {
            primary[pair.Key] = pair.Value;
        }
    }

    private void Reload()
    {
        // The adapter (and therefore every native subscription) is kept; only the profile changes.
        Adapter.OnFocusChanged(false);
        engine.ImportProfile(BuildProfile());
        Adapter.OnFocusChanged(true);
    }

    private InputProfile BuildProfile()
    {
        var maps = new List<ActionMapDefinition>
        {
            // Escape opens the pause menu unless an active modal context consumed it first.
            Input.Map(PauseMap, 5,
            [
                Bind.Action(GameInputLayout.PauseControl.ToControlKey(), PauseAction),
                Bind.Action(Controls.Gamepad(GamepadButton.Start), PauseAction, name: "Aurelian.Shell.Pause.Gamepad"),
            ], canConsume: false),
            Input.Map(GameMenuBindings.Map, 100, GameMenuBindings.CreateBindings()),
        };
        foreach (GameInputContext context in Layout.Contexts)
        {
            var bindings = new List<Binding>();
            ConsumeMode consume = context.Modal ? ConsumeMode.ControlOnly : ConsumeMode.None;
            foreach (GameInputAction action in Layout.Actions.Where(item => item.Context == context.Id))
            {
                var id = new ActionId(ActionName(action.Id));
                bindings.Add(Bind.Action(primary[action.Id].ToControlKey(), id, ButtonEdge.Down, consume: consume,
                    name: BindingName(action.Id)));
                if (action.Gamepad is not null)
                {
                    bindings.Add(Bind.Action(action.Gamepad.ToControlKey(), id, ButtonEdge.Down, consume: consume,
                        name: BindingName(action.Id) + ".gamepad"));
                }
                if (action.Secondary is not null && action.Secondary != primary[action.Id])
                {
                    bindings.Add(Bind.Action(action.Secondary.ToControlKey(), id, ButtonEdge.Down, consume: consume,
                        name: BindingName(action.Id) + ".secondary"));
                }
            }
            if (context.Id == GameInputContext.Play && Layout.Look == GameLookMode.CapturedMouse)
            {
                float pitch = Settings.InvertY ? Settings.MouseSensitivity : -Settings.MouseSensitivity;
                bindings.Add(Bind.DeltaAxis(Controls.Mouse(MouseAxis.DeltaX), LookX, Settings.MouseSensitivity));
                bindings.Add(Bind.DeltaAxis(Controls.Mouse(MouseAxis.DeltaY), LookY, pitch));
            }
            // Modal contexts sit above the pause map so they can claim Escape.
            int priority = context.Modal ? 10 + context.Priority : context.Priority;
            maps.Add(Input.Map(MapFor(context.Id), priority, bindings, canConsume: context.Modal));
        }
        var sticks = new List<Binding>();
        foreach ((GamepadAxis axis, AxisId id) in StickAxes)
        {
            sticks.Add(Bind.Axis(Controls.Gamepad(axis), id, name: id.Name));
        }
        maps.Add(Input.Map(SticksMap, 2, sticks, canConsume: false));
        if (Layout.Pointer)
        {
            // Never consumes: a mouse button bound to an action still reaches the pointer, unless a modal context took it.
            var pointer = new List<Binding>
            {
                // InputMan clamps plain axes to [-1, 1] (sticks); delta-axis triggers pass any magnitude through.
                Bind.DeltaAxis(Controls.Mouse(MouseAxis.PositionX), PointerX, name: "Aurelian.Pointer.X"),
                Bind.DeltaAxis(Controls.Mouse(MouseAxis.PositionY), PointerY, name: "Aurelian.Pointer.Y"),
                Bind.DeltaAxis(Controls.Mouse(MouseAxis.WheelX), WheelX, name: "Aurelian.Pointer.WheelX"),
                Bind.DeltaAxis(Controls.Mouse(MouseAxis.WheelY), WheelY, name: "Aurelian.Pointer.WheelY"),
            };
            foreach ((GamePointerButton _, MouseButton mouse, ActionId action) in PointerButtons)
            {
                pointer.Add(Bind.Action(Controls.Mouse(mouse), action, ButtonEdge.Down, name: action.Name));
            }
            maps.Add(Input.Map(PointerMap, 3, pointer, canConsume: false));
        }
        return Input.Profile([.. maps]);
    }

    private static ActionMapId MapFor(string context) => new("Aurelian.Game." + context);

    private static string ActionName(string action) => "Game." + action;
    private static string BindingName(string action) => "Game." + action + ".primary";
}

internal readonly record struct GameInputFrame(GameInputState Gameplay, GameMenuInput Menu, bool Pause);
