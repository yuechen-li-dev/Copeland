using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Audio;
using Aurelian.GameMenus;
using Aurelian.GameSaves;
using Aurelian.Simulation;
using Deliverance.Core.Storage;

namespace Aurelian.Games;

/// <summary>Native display services the session drives from the display menu. Headless sessions have none.</summary>
public interface IGameDisplay
{
    /// <summary>Resolutions the current monitor can show, smallest first.</summary>
    IReadOnlyList<GameResolution> Resolutions { get; }

    /// <summary>Apply window mode, resolution, vsync and quality. Implementations must not throw for supported values.</summary>
    void Apply(GameDisplaySettings settings);
}

/// <summary>
/// The shell's world-save envelope: the game's typed state, the cadence phase, and input presses
/// latched for a simulation tick that has not run yet. Player preferences are never included.
/// </summary>
public sealed record GameShellSave(JsonElement State, IReadOnlyList<CadenceAccumulatorFact> Cadence, long Tick, double Time,
    IReadOnlyList<string>? PendingPressed = null, IReadOnlyList<string>? PendingReleased = null, float PendingLookX = 0,
    float PendingLookY = 0);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GameShellSettings))]
[JsonSerializable(typeof(GameShellSave))]
[JsonSerializable(typeof(SortedDictionary<string, string>))]
public partial class GameShellJsonContext : JsonSerializerContext;

/// <summary>
/// The headless shell: screens and menus, settings and their persistence, input and rebinding,
/// audio buses, fixed-step cadence, pause and world saves around one <see cref="IGame"/>.
/// <see cref="GameApp"/> adds a window, Vulkan presentation and native input on top of exactly this object.
/// </summary>
public sealed class GameSession : IDisposable
{
    public const string SettingsSlot = "preferences";
    public const string QuickSlot = "slot-1";
    private const int BindingsPerPage = 6;
    private readonly IGame game;
    private readonly AudioResourceScope audioResources = new();
    private readonly GameMenuNavigation navigation = new();
    private readonly GameSaveSlots<GameShellSettings> settingsStore;
    private readonly GameSaveSlots<GameShellSave>? worldStore;
    private readonly Stack<string> history = new();
    private readonly HashSet<string> pendingPressed = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingReleased = new(StringComparer.Ordinal);
    private Vector2 pendingLook;
    private CadenceScheduler scheduler;
    private GameDisplaySettings pendingDisplay;
    private IGameDisplay? display;
    private bool disposed;
    private bool hasWorld;
    private bool slotExists;
    private int bindingPage;

    public GameSession(IGame game, string? saveDirectory = null, IAudioOutputBackend? audioBackend = null, GameAssets? assets = null)
    {
        this.game = game ?? throw new ArgumentNullException(nameof(game));
        Manifest = game.Manifest ?? throw new ArgumentException("The game has no manifest.", nameof(game));
        Manifest.Validate();
        string savesRoot = saveDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Manifest.Id, "saves");
        SaveDirectory = Path.GetFullPath(savesRoot);
        settingsStore = new GameSaveSlots<GameShellSettings>(Manifest.Id, "aurelian.shell-settings.v1",
            new FileSaveStore(Path.Combine(SaveDirectory, "config")), GameShellJsonContext.Default.GameShellSettings,
            settings => settings.Validate());
        settings = GameShellSettings.Default with { Display = Manifest.DefaultDisplay };
        Input = new GameInputSystem(Manifest.Input, settings.Controls);
        Audio = new AurelianAudioRuntime(audioResources, audioBackend ?? new NullAudioOutputBackend());
        audioResources.Add(GameTones.Create("aurelian.ui.move", 660, 0.04f, 0.08f));
        audioResources.Add(GameTones.Create("aurelian.ui.select", 880, 0.07f, 0.1f));
        Context = new GameContext(Manifest, Audio, audioResources, assets ?? new GameAssets());
        scheduler = NewScheduler();
        pendingDisplay = Settings.Display;
        GameSaveSchema? saves = game.Saves;
        if (saves is not null)
        {
            worldStore = new GameSaveSlots<GameShellSave>(Manifest.Id, saves.Identity, new FileSaveStore(SaveDirectory),
                GameShellJsonContext.Default.GameShellSave, ValidateSave);
            Persist(async () => slotExists = await worldStore.ExistsAsync(QuickSlot));
        }
        // Loaded last so that a rejected preferences file remains the visible persistence error.
        LoadSettings();
        game.Start(Context);
        ApplyAudioSettings();
        UpdateAudioPause();
    }

    public GameManifest Manifest { get; }
    public IGame Game => game;
    public GameContext Context { get; }
    public GameInputSystem Input { get; }
    public AurelianAudioRuntime Audio { get; }
    private GameShellSettings settings;

    /// <summary>Current preferences. Controls always reflect the live input system, including direct rebinds.</summary>
    public GameShellSettings Settings
    {
        get => settings with { Controls = Input.Settings };
        private set => settings = value;
    }
    public string SaveDirectory { get; }
    public string Screen { get; private set; } = "title";
    public bool Playing => Screen == "playing";
    public bool Quit { get; private set; }
    public long Tick { get; private set; }
    public double Time { get; private set; }
    public int SelectedIndex => navigation.SelectedIndex;
    public string? PersistenceError { get; private set; }
    public string? Notice { get; private set; }
    public bool SupportsSaves => worldStore is not null;
    public GameDisplaySettings PendingDisplay => pendingDisplay;

    /// <summary>Attach native display services. Later display-menu changes are applied through it.</summary>
    public void AttachDisplay(IGameDisplay host, bool applyNow = true)
    {
        display = host ?? throw new ArgumentNullException(nameof(host));
        if (applyNow)
        {
            display.Apply(Settings.Display);
        }
    }

    public IReadOnlyList<GameResolution> Resolutions => display?.Resolutions ?? GameResolution.Common;

    public GameMenuPage? Menu => Screen switch
    {
        "title" => TitlePage(),
        "pause" => PausePage(),
        "settings" => SettingsPage(),
        "display" => DisplayPage(),
        "audio" => AudioPage(),
        "controls" => ControlsPage(),
        "bindings" => BindingsPage(),
        "rebind" => RebindPage(),
        _ => null,
    };

    /// <summary>Advance one presented frame: menus or gameplay, fixed ticks and audio.</summary>
    public void Advance(TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }
        GameInputFrame frame = Input.Frame(elapsed, Playing);
        if (Screen == "rebind")
        {
            if (!Input.Capturing)
            {
                Notice = Input.RebindMessage;
                if (Input.RebindStatus == GameRebindStatus.Bound)
                {
                    SaveSettings();
                }
                ChangeScreen("bindings", remember: false);
            }
        }
        else if (Playing)
        {
            if (frame.Pause)
            {
                ChangeScreen("pause");
            }
            else
            {
                Play(elapsed, frame.Gameplay);
            }
        }
        else if (frame.Menu.Back)
        {
            Back();
        }
        else if (Menu is { } menu && navigation.Navigate(menu.Entries, frame.Menu) is { } command)
        {
            Execute(command);
        }
        else if (frame.Menu.Up || frame.Menu.Down)
        {
            UiSound("aurelian.ui.move");
        }
        Audio.Update(elapsed);
    }

    /// <summary>Activate a menu entry by id, as a click would. Adjustable entries step forward.</summary>
    public void Activate(string action)
    {
        if (Menu is not { } menu)
        {
            return;
        }
        GameMenuEntry? entry = menu.Entries.FirstOrDefault(item => item.Id == action && !item.Disabled);
        if (entry is null)
        {
            return;
        }
        navigation.Select(menu.Entries, action);
        Execute(new GameMenuCommand(action, entry.Adjustable ? GameMenuCommandKind.Increase : GameMenuCommandKind.Activate));
    }

    /// <summary>Step an adjustable menu entry, as left/right would.</summary>
    public void Adjust(string action, int direction)
    {
        if (Menu is not { } menu || direction == 0)
        {
            return;
        }
        GameMenuEntry? entry = menu.Entries.FirstOrDefault(item => item.Id == action && !item.Disabled && item.Adjustable);
        if (entry is null)
        {
            return;
        }
        navigation.Select(menu.Entries, action);
        Execute(new GameMenuCommand(action, direction < 0 ? GameMenuCommandKind.Decrease : GameMenuCommandKind.Increase));
    }

    /// <summary>Pause gameplay (for example on focus loss) and release held input.</summary>
    public void Pause()
    {
        if (Playing)
        {
            ChangeScreen("pause");
        }
    }

    public void OnFocusChanged(bool focused)
    {
        Input.OnFocusChanged(focused);
        Audio.SetFocused(focused);
        if (!focused)
        {
            Pause();
            if (Screen == "rebind")
            {
                ChangeScreen("bindings", remember: false);
            }
        }
    }

    public void NewGame()
    {
        game.NewGame();
        scheduler = NewScheduler();
        Tick = 0;
        Time = 0;
        hasWorld = true;
        ClearLatchedInput();
        ChangeScreen("playing", remember: false);
        history.Clear();
    }

    public void ReturnToTitle()
    {
        Quit = false;
        history.Clear();
        ChangeScreen("title", remember: false);
    }

    public GameShellSave CaptureSave()
    {
        GameSaveSchema schema = game.Saves ?? throw new NotSupportedException("This game does not define a save schema.");
        return new GameShellSave(schema.Capture(), scheduler.InspectAccumulators(), Tick, Time,
            pendingPressed.Order(StringComparer.Ordinal).ToArray(), pendingReleased.Order(StringComparer.Ordinal).ToArray(),
            pendingLook.X, pendingLook.Y);
    }

    public void RestoreSave(GameShellSave save)
    {
        GameSaveSchema schema = game.Saves ?? throw new NotSupportedException("This game does not define a save schema.");
        ArgumentNullException.ThrowIfNull(save);
        object decoded = schema.Decode(save.State);
        CadenceScheduler candidate = NewScheduler();
        candidate.RestoreAccumulators(save.Cadence ?? throw new InvalidDataException("The save has no cadence phase."));
        if (save.Tick < 0 || !double.IsFinite(save.Time) || save.Time < 0
            || !float.IsFinite(save.PendingLookX) || !float.IsFinite(save.PendingLookY))
        {
            throw new InvalidDataException("The save has an invalid clock or latched input.");
        }
        string[] pressed = ValidateActions(save.PendingPressed);
        string[] released = ValidateActions(save.PendingReleased);
        // Everything has been validated; only now replace live state.
        schema.Restore(decoded);
        scheduler = candidate;
        Tick = save.Tick;
        Time = save.Time;
        hasWorld = true;
        ClearLatchedInput();
        pendingPressed.UnionWith(pressed);
        pendingReleased.UnionWith(released);
        pendingLook = new Vector2(save.PendingLookX, save.PendingLookY);
    }

    private string[] ValidateActions(IReadOnlyList<string>? actions)
    {
        if (actions is null)
        {
            return [];
        }
        foreach (string action in actions)
        {
            if (action is null || !Manifest.Input.Actions.Any(declared => declared.Id == action))
            {
                throw new InvalidDataException($"The save latches unknown input action '{action}'.");
            }
        }
        return actions.ToArray();
    }

    public async Task SaveAsync(string slot, CancellationToken cancellation = default)
    {
        GameSaveSlots<GameShellSave> store = worldStore ?? throw new NotSupportedException("This game does not define a save schema.");
        await store.SaveAsync(slot, CaptureSave(), cancellation);
        if (slot == QuickSlot)
        {
            slotExists = true;
        }
    }

    public async Task LoadAsync(string slot, CancellationToken cancellation = default)
    {
        GameSaveSlots<GameShellSave> store = worldStore ?? throw new NotSupportedException("This game does not define a save schema.");
        GameShellSave save = await store.LoadAsync(slot, cancellation);
        RestoreSave(save);
    }

    public GameSaveSlots<GameShellSave> Saves => worldStore ?? throw new NotSupportedException("This game does not define a save schema.");

    /// <summary>Replace all player preferences atomically, apply them and (by default) persist them.</summary>
    public void ApplySettings(GameShellSettings settings, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Input.Apply(settings.Controls);
        bool displayChanged = settings.Display != Settings.Display;
        Settings = settings with { Controls = Input.Settings };
        pendingDisplay = Settings.Display;
        ApplyAudioSettings();
        if (displayChanged)
        {
            display?.Apply(Settings.Display);
        }
        if (persist)
        {
            SaveSettings();
        }
    }

    public void SaveSettings()
    {
        Persist(() => settingsStore.SaveAsync(SettingsSlot, Settings));
    }

    public GameFacts Observe()
    {
        var facts = new GameFacts();
        game.Observe(facts);
        facts.Set("screen", Screen);
        facts.Set("tick", Tick);
        facts.Set("time", Time);
        facts.Set("display.resolution", Settings.Display.Resolution.ToString());
        facts.Set("display.mode", Settings.Display.Mode.ToString());
        facts.Set("display.vsync", Settings.Display.VSync);
        facts.Set("display.quality", Settings.Display.Quality.ToString());
        facts.Set("audio.master", Settings.Audio.Master);
        facts.Set("audio.music", Settings.Audio.Music);
        facts.Set("audio.effects", Settings.Audio.Effects);
        // Same units as the controls menu: radians per thousand mouse counts.
        facts.Set("controls.sensitivity", SensitivityText(Settings.Controls.MouseSensitivity));
        facts.Set("controls.invert-y", Settings.Controls.InvertY);
        facts.Set("rebind", Input.RebindStatus.ToString());
        foreach (GameInputAction action in Manifest.Input.Actions)
        {
            facts.Set("binding." + action.Id, Input.Binding(action.Id).DisplayName);
        }
        if (Menu is { } menu && menu.Entries.Count > 0)
        {
            facts.Set("menu.selected", menu.Entries[Math.Clamp(SelectedIndex, 0, menu.Entries.Count - 1)].Id);
        }
        if (PersistenceError is not null)
        {
            facts.Set("persistence.error", PersistenceError);
        }
        return facts;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        game.Dispose();
        Input.Dispose();
        Audio.Dispose();
        audioResources.Dispose();
    }

    private void Play(TimeSpan elapsed, GameInputState frameInput)
    {
        pendingPressed.UnionWith(Manifest.Input.Actions.Where(action => frameInput.WasPressed(action.Id)).Select(action => action.Id));
        pendingReleased.UnionWith(Manifest.Input.Actions.Where(action => frameInput.WasReleased(action.Id)).Select(action => action.Id));
        pendingLook += frameInput.Look;
        game.Update(new GameUpdate(elapsed, frameInput));
        if (!Playing)
        {
            return;
        }
        CadenceAdvanceResult advance = scheduler.Advance(elapsed, SimulationExecutionRate.Normal);
        float seconds = 1f / Manifest.TickRate;
        foreach (DueWorkFact _ in advance.DueWork)
        {
            var down = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameInputAction action in Manifest.Input.Actions)
            {
                if (frameInput.IsDown(action.Id))
                {
                    down.Add(action.Id);
                }
            }
            var tickInput = new GameInputState(Manifest.Input, down, new HashSet<string>(pendingPressed, StringComparer.Ordinal),
                new HashSet<string>(pendingReleased, StringComparer.Ordinal), pendingLook);
            ClearLatchedInput();
            Tick++;
            Time += seconds;
            game.FixedUpdate(new GameTick(Tick, seconds, Time, tickInput));
            if (!Playing)
            {
                break;
            }
        }
    }

    private void Execute(GameMenuCommand command)
    {
        Notice = null;
        UiSound("aurelian.ui.select");
        int step = command.Kind == GameMenuCommandKind.Decrease ? -1 : 1;
        string id = command.Id;
        if (id.StartsWith("bind.", StringComparison.Ordinal))
        {
            Input.BeginRebind(id["bind.".Length..]);
            ChangeScreen("rebind", remember: false);
            return;
        }
        switch (id)
        {
            case "start":
                NewGame();
                break;
            case "resume":
                ChangeScreen("playing", remember: false);
                history.Clear();
                break;
            case "save":
                if (Persist(() => SaveAsync(QuickSlot)))
                {
                    Notice = "Saved.";
                }
                break;
            case "load":
                if (Persist(() => LoadAsync(QuickSlot)))
                {
                    history.Clear();
                    ChangeScreen("playing", remember: false);
                }
                break;
            case "settings":
                ChangeScreen("settings");
                break;
            case "display":
                pendingDisplay = Settings.Display;
                ChangeScreen("display");
                break;
            case "audio":
                ChangeScreen("audio");
                break;
            case "controls":
                ChangeScreen("controls");
                break;
            case "bindings":
                bindingPage = 0;
                ChangeScreen("bindings");
                break;
            case "bindings.next":
                bindingPage = (bindingPage + 1) % BindingPageCount();
                navigation.Reset();
                break;
            case "bindings.reset":
                Input.ResetBindings();
                Settings = Settings with { Controls = Input.Settings };
                SaveSettings();
                Notice = "Default bindings restored.";
                break;
            case "title":
                ReturnToTitle();
                break;
            case "quit":
                Quit = true;
                break;
            case "back":
                Back();
                break;
            case "display.resolution":
                pendingDisplay = StepResolution(pendingDisplay, step);
                break;
            case "display.mode":
                pendingDisplay = pendingDisplay with { Mode = Cycle(pendingDisplay.Mode, step) };
                break;
            case "display.vsync":
                pendingDisplay = pendingDisplay with { VSync = !pendingDisplay.VSync };
                break;
            case "display.quality":
                pendingDisplay = pendingDisplay with { Quality = Cycle(pendingDisplay.Quality, step) };
                break;
            case "display.apply":
                ApplySettings(Settings with { Display = pendingDisplay });
                Notice = "Display settings applied.";
                break;
            case "audio.master":
                ApplySettings(Settings with { Audio = Settings.Audio with { Master = StepVolume(Settings.Audio.Master, step) } });
                break;
            case "audio.music":
                ApplySettings(Settings with { Audio = Settings.Audio with { Music = StepVolume(Settings.Audio.Music, step) } });
                break;
            case "audio.effects":
                ApplySettings(Settings with { Audio = Settings.Audio with { Effects = StepVolume(Settings.Audio.Effects, step) } });
                break;
            case "controls.sensitivity":
                ApplySettings(Settings with
                {
                    Controls = Input.Settings with { MouseSensitivity = StepSensitivity(Input.Settings.MouseSensitivity, step) },
                });
                break;
            case "controls.invert-y":
                ApplySettings(Settings with { Controls = Input.Settings with { InvertY = !Input.Settings.InvertY } });
                break;
        }
    }

    private void Back()
    {
        if (Screen == "title")
        {
            Quit = true;
            return;
        }
        if (Screen == "pause")
        {
            ChangeScreen("playing", remember: false);
            history.Clear();
            return;
        }
        if (Screen == "display")
        {
            pendingDisplay = Settings.Display;
        }
        string previous = history.Count > 0 ? history.Pop() : hasWorld ? "pause" : "title";
        ChangeScreen(previous, remember: false);
    }

    private void ChangeScreen(string next, bool remember = true)
    {
        if (remember && next != Screen)
        {
            history.Push(Screen);
        }
        Screen = next;
        navigation.Reset();
        if (next != "rebind" && Input.Capturing)
        {
            Input.CancelRebind();
        }
        if (next != "rebind")
        {
            Input.ResetHeldState();
        }
        ClearLatchedInput();
        UpdateAudioPause();
    }

    private void ClearLatchedInput()
    {
        pendingPressed.Clear();
        pendingReleased.Clear();
        pendingLook = Vector2.Zero;
    }

    private GameMenuPage TitlePage()
    {
        var entries = new List<GameMenuEntry> { new("start", "New game") };
        if (SupportsSaves)
        {
            entries.Add(new("load", "Load game", Disabled: !slotExists));
        }
        entries.Add(new("settings", "Settings"));
        entries.Add(new("quit", "Quit"));
        string subtitle = PersistenceError is not null ? "SAVED DATA COULD NOT BE USED: " + PersistenceError : Notice ?? Manifest.Tagline;
        return new GameMenuPage(Manifest.Id + ".title", Manifest.Title, subtitle, entries);
    }

    private GameMenuPage PausePage()
    {
        var entries = new List<GameMenuEntry> { new("resume", "Resume") };
        if (SupportsSaves)
        {
            entries.Add(new("save", "Save game"));
            entries.Add(new("load", "Load game", Disabled: !slotExists));
        }
        entries.Add(new("settings", "Settings"));
        entries.Add(new("title", "Quit to title"));
        string subtitle = PersistenceError is not null ? "Save / load failed. Your current game is still here." : Notice ?? Manifest.ControlsHint;
        return new GameMenuPage(Manifest.Id + ".pause", "PAUSED", subtitle, entries);
    }

    private GameMenuPage SettingsPage()
    {
        return new GameMenuPage("shell.settings", "SETTINGS", Notice ?? Manifest.ControlsHint,
        [
            new("display", "Display"),
            new("audio", "Audio"),
            new("controls", "Controls"),
            new("back", "Back"),
        ]);
    }

    private GameMenuPage DisplayPage()
    {
        bool changed = pendingDisplay != Settings.Display;
        string subtitle = Notice ?? (changed ? "Changes are pending until you apply them." : "Current: "
            + Settings.Display.Resolution + " " + Settings.Display.Mode);
        return new GameMenuPage("shell.display", "DISPLAY", subtitle,
        [
            new("display.resolution", "Resolution", Value: pendingDisplay.Resolution.ToString(), Adjustable: true,
                Disabled: pendingDisplay.Mode == GameWindowMode.Borderless),
            new("display.mode", "Window", Value: ModeName(pendingDisplay.Mode), Adjustable: true),
            new("display.vsync", "VSync", Value: pendingDisplay.VSync ? "On" : "Off", Adjustable: true),
            new("display.quality", "Graphics", Value: pendingDisplay.Quality.ToString(), Adjustable: true),
            new("display.apply", "Apply changes", Disabled: !changed),
            new("back", "Back"),
        ], Footer: "UP / DOWN choose   LEFT / RIGHT change   ENTER confirm   ESC back");
    }

    private GameMenuPage AudioPage()
    {
        return new GameMenuPage("shell.audio", "AUDIO", Notice ?? "Changes apply and save immediately.",
        [
            new("audio.master", "Master", Value: Percent(Settings.Audio.Master), Adjustable: true),
            new("audio.music", "Music", Value: Percent(Settings.Audio.Music), Adjustable: true),
            new("audio.effects", "Effects", Value: Percent(Settings.Audio.Effects), Adjustable: true),
            new("back", "Back"),
        ], Footer: "UP / DOWN choose   LEFT / RIGHT change   ESC back");
    }

    private GameMenuPage ControlsPage()
    {
        var entries = new List<GameMenuEntry>();
        if (Manifest.Input.Look == GameLookMode.CapturedMouse)
        {
            entries.Add(new("controls.sensitivity", "Mouse sensitivity",
                Value: SensitivityText(Input.Settings.MouseSensitivity), Adjustable: true));
            entries.Add(new("controls.invert-y", "Invert mouse Y", Value: Input.Settings.InvertY ? "On" : "Off", Adjustable: true));
        }
        entries.Add(new("bindings", "Key bindings", Disabled: !Manifest.Input.Actions.Any(action => action.Rebindable)));
        entries.Add(new("back", "Back"));
        return new GameMenuPage("shell.controls", "CONTROLS", Notice ?? Manifest.ControlsHint, entries,
            Footer: "UP / DOWN choose   LEFT / RIGHT change   ENTER confirm   ESC back");
    }

    private GameMenuPage BindingsPage()
    {
        GameInputAction[] actions = RebindableActions();
        int pages = BindingPageCount();
        bindingPage = Math.Clamp(bindingPage, 0, pages - 1);
        var entries = new List<GameMenuEntry>();
        foreach (GameInputAction action in actions.Skip(bindingPage * BindingsPerPage).Take(BindingsPerPage))
        {
            entries.Add(new("bind." + action.Id, action.Label, Value: Input.Binding(action.Id).DisplayName));
        }
        if (pages > 1)
        {
            entries.Add(new("bindings.next", $"More ({bindingPage + 1}/{pages})"));
        }
        entries.Add(new("bindings.reset", "Reset to defaults"));
        entries.Add(new("back", "Back"));
        return new GameMenuPage("shell.bindings." + bindingPage.ToString(CultureInfo.InvariantCulture), "KEY BINDINGS",
            Notice ?? "Select an action, then press its new key. Escape cancels.", entries);
    }

    private GameMenuPage RebindPage()
    {
        string label = Input.RebindAction is { } action ? Manifest.Input.Action(action).Label : "";
        return new GameMenuPage("shell.rebind", "PRESS A KEY", $"Binding {label.ToUpperInvariant()}.", [],
            Footer: "ESC cancels   (10 second timeout)");
    }

    private GameInputAction[] RebindableActions() => Manifest.Input.Actions.Where(action => action.Rebindable).ToArray();

    private int BindingPageCount() => Math.Max(1, (RebindableActions().Length + BindingsPerPage - 1) / BindingsPerPage);

    private GameDisplaySettings StepResolution(GameDisplaySettings current, int step)
    {
        IReadOnlyList<GameResolution> options = Resolutions;
        if (options.Count == 0)
        {
            return current;
        }
        int index = -1;
        for (int candidate = 0; candidate < options.Count; candidate++)
        {
            if (options[candidate] == current.Resolution)
            {
                index = candidate;
            }
        }
        if (index < 0)
        {
            // Snap an unlisted resolution to the nearest list entry in the requested direction.
            index = step > 0 ? -1 : options.Count;
            for (int candidate = 0; candidate < options.Count; candidate++)
            {
                long area = (long)options[candidate].Width * options[candidate].Height;
                long currentArea = (long)current.Width * current.Height;
                if (area < currentArea)
                {
                    index = step > 0 ? candidate : candidate + 1;
                }
            }
        }
        int next = ((index + step) % options.Count + options.Count) % options.Count;
        return current with { Width = options[next].Width, Height = options[next].Height };
    }

    private static T Cycle<T>(T value, int step) where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>();
        int index = Array.IndexOf(values, value);
        return values[((index + step) % values.Length + values.Length) % values.Length];
    }

    private static float StepVolume(float value, int step) => MathF.Round(Math.Clamp(value + step * 0.1f, 0, 1), 2);

    private static float StepSensitivity(float value, int step)
    {
        float next = step > 0 ? value * 1.25f : value / 1.25f;
        return Math.Clamp(MathF.Round(next, 6), GameControlSettings.MinimumSensitivity, GameControlSettings.MaximumSensitivity);
    }

    private static string SensitivityText(float value) => (value * 1000).ToString("0.0#", CultureInfo.InvariantCulture);

    private static string Percent(float value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string ModeName(GameWindowMode mode) => mode switch
    {
        GameWindowMode.Borderless => "Borderless",
        GameWindowMode.Fullscreen => "Fullscreen",
        _ => "Windowed",
    };

    private void LoadSettings()
    {
        bool exists = false;
        GameShellSettings? loaded = null;
        if (!Persist(async () => exists = await settingsStore.ExistsAsync(SettingsSlot)) || !exists)
        {
            return;
        }
        if (!Persist(async () => loaded = await settingsStore.LoadAsync(SettingsSlot)) || loaded is null)
        {
            return;
        }
        try
        {
            Input.Apply(loaded.Controls);
            Settings = loaded with { Controls = Input.Settings };
            pendingDisplay = Settings.Display;
        }
        catch (ArgumentException exception)
        {
            // Bindings saved for an older input layout are rejected as a whole; everything else is kept.
            PersistenceError = exception.Message;
            Settings = loaded with { Controls = Input.Settings };
            pendingDisplay = Settings.Display;
        }
    }

    private void ApplyAudioSettings()
    {
        Audio.SetBusVolume(AudioBusId.Master, Settings.Audio.Master);
        Audio.SetBusVolume(AudioBusId.Music, Settings.Audio.Music);
        Audio.SetBusVolume(AudioBusId.Sfx, Settings.Audio.Effects);
        Audio.SetBusVolume(AudioBusId.Ambient, Settings.Audio.Effects);
        Audio.SetBusVolume(AudioBusId.UI, Settings.Audio.Effects);
    }

    private void UpdateAudioPause()
    {
        // Gameplay sounds freeze with the world; music and menu sounds keep playing.
        Audio.SetBusPaused(AudioBusId.Sfx, !Playing);
        Audio.SetBusPaused(AudioBusId.Ambient, !Playing);
    }

    private void UiSound(string asset)
    {
        Context.PlaySound(asset, bus: AudioBusId.UI);
    }

    private void ValidateSave(GameShellSave save)
    {
        if (save is null || save.Cadence is null)
        {
            throw new InvalidDataException("The save is incomplete.");
        }
        _ = game.Saves!.Decode(save.State);
        NewScheduler().RestoreAccumulators(save.Cadence);
    }

    private bool Persist(Func<Task> operation)
    {
        try
        {
            operation().GetAwaiter().GetResult();
            PersistenceError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or Deliverance.Core.DeliveranceException or JsonException
            or ArgumentException or InvalidDataException or UnauthorizedAccessException)
        {
            PersistenceError = exception.Message;
            Console.Error.WriteLine("Game persistence failed: " + exception.Message);
            return false;
        }
    }

    private CadenceScheduler NewScheduler() => new([new(new("gameplay"), RationalRate.PerSecond(Manifest.TickRate), 0)],
        TimeSpan.FromSeconds(0.1));
}
