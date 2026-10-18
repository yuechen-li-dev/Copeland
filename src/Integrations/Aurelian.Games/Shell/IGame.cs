using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Aurelian.Audio;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Games;

/// <summary>
/// The game-specific half of an Aurelian application. The shell (<see cref="GameApp"/> natively,
/// <see cref="GameSession"/> headlessly) owns the window, resolution, menus, pause, settings,
/// input devices and rebinding, audio output and volume, fixed-step cadence, save slots and playtesting.
/// A game supplies rules, presentation and, optionally, a typed save schema.
/// </summary>
public interface IGame : IDisposable
{
    GameManifest Manifest { get; }

    /// <summary>Called once, before the title screen, with the shell's services.</summary>
    void Start(GameContext context)
    {
    }

    /// <summary>Reset the world for a fresh game. Called by "Start" on the title screen.</summary>
    void NewGame();

    /// <summary>Once per presented frame while playing, with variable elapsed time. Use for immediate camera look.</summary>
    void Update(GameUpdate update)
    {
    }

    /// <summary>
    /// Once per presented frame on every screen, menus included, after the shell has handled input. Use it for things
    /// that continue behind menus, such as choosing music for the current screen. Do not change simulation state here.
    /// </summary>
    void OnFrame(GameFrame frame)
    {
    }

    /// <summary>
    /// Input contexts active this frame, from <see cref="GameInputLayout.Contexts"/>. Games with one context keep the
    /// default. Switch to a modal context while the game's own screen (dialogue, inventory) has focus.
    /// </summary>
    IReadOnlyCollection<string> InputContexts => [GameInputContext.Play];

    /// <summary>Deterministic simulation step at <see cref="GameManifest.TickRate"/> while playing.</summary>
    void FixedUpdate(GameTick tick);

    /// <summary>The 3D view to draw this frame, or null for none. Also called behind menus.</summary>
    GameView? View(GameViewRequest request) => null;

    /// <summary>Overlay drawn while playing. Coordinates are logical UI units; the shell scales them to the window.</summary>
    void BuildHud(GameHud hud)
    {
    }

    /// <summary>Optional typed persistence. Null hides save/load in the menus.</summary>
    GameSaveSchema? Saves => null;

    /// <summary>Flat, stringly facts for playtest assertions and inspection (for example "score" = "3").</summary>
    void Observe(GameFacts facts)
    {
    }

    /// <summary>
    /// Game-specific playtest/debug commands (for example "teleport 2" or "autopilot on"). Return false for unknown
    /// commands; the playtest target then reports them as errors.
    /// </summary>
    bool Command(string command) => false;

    /// <summary>Native only: called after the Vulkan graphics exist (load lighting, environments, GPU presenters).</summary>
    void AttachGraphics(NativeGameGraphics graphics)
    {
    }

    /// <summary>Native only: optional GPU-produced geometry (for example skinned characters) for this frame.</summary>
    NativeGpuGeometry3D? PrepareGpuGeometry(NativeGameGraphics graphics) => null;

    /// <summary>Native only: called before the graphics are disposed. Release anything created in <see cref="AttachGraphics"/>.</summary>
    void DetachGraphics()
    {
    }
}

/// <summary>What the shell needs to know about a game before it can open a window.</summary>
public sealed record GameManifest(string Id, string Title, GameInputLayout Input)
{
    /// <summary>Subtitle on the title screen.</summary>
    public string Tagline { get; init; } = "";

    /// <summary>One line of control hints on the settings and pause pages.</summary>
    public string ControlsHint { get; init; } = "";

    /// <summary>The game's authored look. The player's quality setting derives lower tiers from it.</summary>
    public Graphics3DSettings Graphics { get; init; } = Graphics3DSettings.Default;

    /// <summary>First-run display settings before the player chooses any.</summary>
    public GameDisplaySettings DefaultDisplay { get; init; } = new();

    /// <summary>Fixed simulation rate in ticks per second.</summary>
    public int TickRate { get; init; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Game id must use ASCII letters, digits or hyphens; it names the save directory.");
        }
        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new ArgumentException("Games need a title.");
        }
        ArgumentNullException.ThrowIfNull(Input);
        Input.Validate();
        ArgumentNullException.ThrowIfNull(Graphics);
        Graphics.Validate();
        ArgumentNullException.ThrowIfNull(DefaultDisplay);
        DefaultDisplay.Validate();
        if (TickRate is < 1 or > 1000)
        {
            throw new ArgumentException("Tick rate must be between 1 and 1000 per second.");
        }
    }
}

/// <summary>Shell services available to game code.</summary>
public sealed class GameContext
{
    private ulong soundSequence;

    internal Action<string>? SaveRequested { get; set; }

    internal GameContext(GameManifest manifest, AurelianAudioRuntime audio, AudioResourceScope audioResources, GameAssets assets)
    {
        Manifest = manifest;
        Audio = audio;
        AudioResources = audioResources;
        Assets = assets;
    }

    public GameManifest Manifest { get; }

    /// <summary>The shared audio runtime. Master/Music/Sfx bus volumes follow the player's settings.</summary>
    public AurelianAudioRuntime Audio { get; }

    /// <summary>Register clips here (for example with <see cref="AudioResourceScope.LoadPcmWav"/>).</summary>
    public AudioResourceScope AudioResources { get; }

    /// <summary>Built-in engine shaders and fonts.</summary>
    public GameAssets Assets { get; }

    /// <summary>Play a registered clip with a unique event identity. Gameplay sounds pause with the game.</summary>
    public AudioVoiceId? PlaySound(string asset, float volume = 1, AudioBusId? bus = null, Vector2? position = null)
    {
        var id = new AudioEventId($"{asset}#{++soundSequence}");
        return Audio.Play(new AudioCue(id, new AudioAssetId(asset), bus ?? AudioBusId.Sfx, volume, Position: position));
    }

    /// <summary>
    /// Ask the shell for a checkpoint save, for example when the player sleeps. Call it from <see cref="IGame.FixedUpdate"/>:
    /// the shell captures the state right after the current tick, so the save matches the simulation clock exactly, and
    /// writes it in the background. The slot defaults to the one the menus load. Failures appear like menu save failures.
    /// Games without <see cref="IGame.Saves"/> cannot request saves.
    /// </summary>
    public void RequestSave(string slot = GameSession.QuickSlot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        (SaveRequested ?? throw new NotSupportedException("This game does not define a save schema."))(slot);
    }

    /// <summary>Register a short synthesized tone, handy for prototypes before real sound assets exist.</summary>
    public void AddTone(string asset, float frequency, float seconds, float gain = 0.2f)
    {
        AudioResources.Add(GameTones.Create(asset, frequency, seconds, gain));
    }
}

internal static class GameTones
{
    public static AudioClipResource Create(string asset, float frequency, float seconds, float gain)
    {
        if (!float.IsFinite(frequency) || frequency <= 0 || !float.IsFinite(seconds) || seconds <= 0 || seconds > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), "Tones need a positive frequency and a duration up to ten seconds.");
        }
        const int Rate = 48000;
        float[] samples = new float[(int)(Rate * seconds)];
        for (int index = 0; index < samples.Length; index++)
        {
            float envelope = 1 - (float)index / samples.Length;
            samples[index] = MathF.Sin(index * MathF.Tau * frequency / Rate) * gain * envelope;
        }
        string hash = string.Create(CultureInfo.InvariantCulture, $"tone-{frequency}-{seconds}-{gain}");
        return new AudioClipResource(new AudioAssetId(asset), hash, Rate, 1, samples.Length, samples);
    }
}

/// <summary>One presented frame on any screen ("title", "playing", "pause", settings pages...).</summary>
public readonly record struct GameFrame(TimeSpan Elapsed, string Screen)
{
    public bool Playing => Screen == "playing";
}

/// <summary>Per-frame update while playing.</summary>
public readonly record struct GameUpdate(TimeSpan Elapsed, GameInputState Input)
{
    public float Seconds => (float)Elapsed.TotalSeconds;
}

/// <summary>
/// One fixed simulation tick. Presses and releases that happened in frames without a tick are
/// latched until the next tick, so short taps are never lost; <see cref="GameInputState.IsDown"/> is current.
/// </summary>
public readonly record struct GameTick(long Index, float Seconds, double Time, GameInputState Input);

public readonly record struct GameViewRequest(int Width, int Height)
{
    public float Aspect => (float)Width / Height;
}

/// <summary>A 3D frame: scene, world-to-clip matrix and eye position.</summary>
public sealed record GameView(Native3DScene Scene, Matrix4x4 Camera, Vector3 Eye);

/// <summary>Sorted string facts for playtests. Values are formatted with the invariant culture.</summary>
public sealed class GameFacts
{
    private readonly SortedDictionary<string, string> values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Values => values;

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        values[name] = value ?? "";
    }

    public void Set(string name, int value) => Set(name, value.ToString(CultureInfo.InvariantCulture));
    public void Set(string name, long value) => Set(name, value.ToString(CultureInfo.InvariantCulture));
    public void Set(string name, bool value) => Set(name, value ? "true" : "false");
    public void Set(string name, float value) => Set(name, value.ToString("0.###", CultureInfo.InvariantCulture));
    public void Set(string name, double value) => Set(name, value.ToString("0.###", CultureInfo.InvariantCulture));
    public void Set(string name, Vector3 value) => Set(name, string.Create(CultureInfo.InvariantCulture,
        $"{value.X:0.###},{value.Y:0.###},{value.Z:0.###}"));
}

/// <summary>
/// A game's typed, explicitly validated world snapshot. The shell stores it in Deliverance slots together
/// with the cadence phase. Player preferences are never part of it.
/// </summary>
public abstract class GameSaveSchema
{
    private protected GameSaveSchema(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Identity = identity;
    }

    /// <summary>Content identity: saves made under a different identity are rejected before touching live state.</summary>
    public string Identity { get; }

    /// <summary>
    /// Schema version of the captured state. Raise it when the snapshot's shape changes and add a migration from the
    /// previous version, so existing saves keep loading.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>Steps that rewrite an older snapshot's JSON one version forward. Loading applies them in order.</summary>
    public IReadOnlyList<GameSaveMigration> Migrations { get; init; } = [];

    internal void ValidateVersioning()
    {
        if (Version < 1)
        {
            throw new ArgumentException("Save schema versions start at 1.");
        }
        var seen = new HashSet<int>();
        foreach (GameSaveMigration migration in Migrations ?? throw new ArgumentNullException(nameof(Migrations)))
        {
            if (migration is null || migration.Migrate is null || migration.FromVersion < 1 || migration.FromVersion >= Version
                || !seen.Add(migration.FromVersion))
            {
                throw new ArgumentException("Save migrations must each start at a distinct version below the schema version.");
            }
        }
    }

    internal abstract JsonElement Capture();
    internal abstract object Decode(JsonElement state);
    internal abstract void Restore(object decoded);

    public static GameSaveSchema<T> Create<T>(JsonTypeInfo<T> type, string identity, Func<T> capture, Action<T> validate,
        Action<T> restore) where T : class => new(type, identity, capture, validate, restore);
}

/// <summary>Rewrites a saved game state from <paramref name="FromVersion"/> to the next version.</summary>
public sealed record GameSaveMigration(int FromVersion, Func<System.Text.Json.Nodes.JsonNode, System.Text.Json.Nodes.JsonNode> Migrate);

public sealed class GameSaveSchema<T> : GameSaveSchema where T : class
{
    private readonly JsonTypeInfo<T> type;
    private readonly Func<T> capture;
    private readonly Action<T> validate;
    private readonly Action<T> restore;

    public GameSaveSchema(JsonTypeInfo<T> type, string identity, Func<T> capture, Action<T> validate, Action<T> restore)
        : base(identity)
    {
        this.type = type ?? throw new ArgumentNullException(nameof(type));
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        this.validate = validate ?? throw new ArgumentNullException(nameof(validate));
        this.restore = restore ?? throw new ArgumentNullException(nameof(restore));
    }

    internal override JsonElement Capture()
    {
        T snapshot = capture();
        validate(snapshot);
        return JsonSerializer.SerializeToElement(snapshot, type);
    }

    internal override object Decode(JsonElement state)
    {
        T snapshot = state.Deserialize(type) ?? throw new InvalidDataException("The save contains an empty game state.");
        validate(snapshot);
        return snapshot;
    }

    internal override void Restore(object decoded) => restore((T)decoded);
}
