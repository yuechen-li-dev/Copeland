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
    bool Rebindable = true);

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
        if (!Enum.IsDefined(Look))
        {
            throw new ArgumentException("Unknown look mode.");
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
            ValidateControl(action.Id, action.Primary);
            if (action.Secondary is not null)
            {
                ValidateControl(action.Id, action.Secondary);
            }
        }
    }

    private static void ValidateControl(string id, GameControl control)
    {
        if (control is null || !control.IsValid)
        {
            throw new ArgumentException($"Game action '{id}' uses an unsupported control.");
        }
        if (control == PauseControl)
        {
            throw new ArgumentException($"Game action '{id}' cannot use Escape; it is reserved for the pause menu.");
        }
    }
}

/// <summary>A persisted rebinding of one action's primary control.</summary>
public sealed record GameBindingOverride(string Action, GameControl Control);

/// <summary>Read-only input view handed to game code for one frame or one fixed simulation tick.</summary>
public sealed class GameInputState
{
    private readonly GameInputLayout layout;
    private readonly IReadOnlySet<string> down;
    private readonly IReadOnlySet<string> pressed;
    private readonly IReadOnlySet<string> released;

    internal GameInputState(GameInputLayout layout, IReadOnlySet<string> down, IReadOnlySet<string> pressed,
        IReadOnlySet<string> released, Vector2 look)
    {
        this.layout = layout;
        this.down = down;
        this.pressed = pressed;
        this.released = released;
        Look = look;
    }

    internal static GameInputState Empty(GameInputLayout layout) =>
        new(layout, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), Vector2.Zero);

    /// <summary>Mouse-look delta in radians (X = yaw, Y = pitch, positive up). Zero unless the layout uses mouse look.</summary>
    public Vector2 Look { get; }

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
    private static readonly ActionMapId GameplayMap = new("Aurelian.Game");
    private static readonly ActionId PauseAction = new("Aurelian.Shell.Pause");
    private static readonly AxisId LookX = new("Aurelian.Shell.LookX");
    private static readonly AxisId LookY = new("Aurelian.Shell.LookY");
    private readonly InputManEngine engine;
    private readonly Dictionary<string, GameControl> primary = new(StringComparer.Ordinal);
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
            if (other.Id != action && primary[other.Id] == control)
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
        Adapter.SetContexts(GameplayMap);
        capture = engine.StartRebind(new RebindRequest
        {
            Map = GameplayMap,
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
    internal GameInputFrame Frame(TimeSpan elapsed, bool gameplay)
    {
        total += elapsed;
        if (capture is null)
        {
            Adapter.SetContexts(gameplay ? GameplayMap : GameMenuBindings.Map);
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
        var state = new GameInputState(Layout, down, pressed, released, look);
        return new GameInputFrame(state, default, frame.WasPressed(PauseAction));
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
        Adapter.OnFocusChanged(false);
        Adapter.OnFocusChanged(true);
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
        var used = new HashSet<GameControl>();
        foreach (GameInputAction action in Layout.Actions)
        {
            if (!used.Add(candidate[action.Id]))
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
        var bindings = new List<Binding>
        {
            Bind.Action(GameInputLayout.PauseControl.ToControlKey(), PauseAction),
        };
        foreach (GameInputAction action in Layout.Actions)
        {
            var id = new ActionId(ActionName(action.Id));
            bindings.Add(Bind.Action(primary[action.Id].ToControlKey(), id, ButtonEdge.Down, name: BindingName(action.Id)));
            if (action.Secondary is not null && action.Secondary != primary[action.Id])
            {
                bindings.Add(Bind.Action(action.Secondary.ToControlKey(), id, ButtonEdge.Down, name: BindingName(action.Id) + ".secondary"));
            }
        }
        if (Layout.Look == GameLookMode.CapturedMouse)
        {
            float pitch = Settings.InvertY ? Settings.MouseSensitivity : -Settings.MouseSensitivity;
            bindings.Add(Bind.DeltaAxis(Controls.Mouse(MouseAxis.DeltaX), LookX, Settings.MouseSensitivity));
            bindings.Add(Bind.DeltaAxis(Controls.Mouse(MouseAxis.DeltaY), LookY, pitch));
        }
        return Input.Profile([Input.Map(GameplayMap, 0, bindings), Input.Map(GameMenuBindings.Map, 100, GameMenuBindings.CreateBindings())]);
    }

    private static string ActionName(string action) => "Game." + action;
    private static string BindingName(string action) => "Game." + action + ".primary";
}

internal readonly record struct GameInputFrame(GameInputState Gameplay, GameMenuInput Menu, bool Pause);
