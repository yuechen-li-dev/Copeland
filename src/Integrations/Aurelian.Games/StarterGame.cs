using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Audio;
using Aurelian.Combat;
using Aurelian.GameMenus;
using Aurelian.GameSaves;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Runtime;
using Aurelian.Simulation;
using Aurelian.Spatial3D;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;
using Deliverance.Core.Storage;

namespace Aurelian.Games;

/// <summary>A small customizable geometric starter. Domain snapshots are explicit; the host does no discovery.</summary>
public sealed class StarterGame : IDisposable
{
    private readonly StarterOptions options;
    private readonly StarterObject[] objects;
    private readonly SpatialWorld3D spatialWorld;
    private readonly CharacterMotor3D motor = new();
    private IRayQueryWorld3D rayQueries = null!;
    private readonly ScenePlan scenePlan;
    private SceneInstance scene = null!;
    private readonly GameMenuNavigation navigation = new();
    private readonly AudioResourceScope audioResources = new();
    private CadenceScheduler scheduler = NewScheduler();
    private IReadOnlyList<SceneAgent<int>> agents = [];
    private SceneAgent<Vector3> player = null!;
    private Vector3 position
    {
        get => player.State;
        set => player.State = value;
    }
    private float yaw;
    private float pitch;
    private float verticalVelocity;
    private double time;
    private string returnScreen = "title";
    private readonly ReloadableGun gun;
    private readonly GameSaveSlots<GameSettings> settings;
    private bool disposed;
    private bool pendingJump;
    private bool pendingReload;
    private bool pendingFire;
    private ulong audioEventSequence;

    public StarterGame(string id, GameDefinition definition, StarterOptions? options = null,
        string? saveDirectory = null, IAudioOutputBackend? audioBackend = null, SceneGroup? sceneDocument = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Game id must use ASCII letters, digits or hyphens.", nameof(id));
        }
        Id = id;
        Definition = definition;
        this.options = options ?? new();
        if (string.IsNullOrWhiteSpace(this.options.Title) || !float.IsFinite(this.options.MovementSpeed) || this.options.MovementSpeed <= 0)
        {
            throw new ArgumentException("Starter title and positive movement speed are required.");
        }
        if (sceneDocument is not null && this.options.Objects is not null)
        {
            throw new ArgumentException("Supply either a scene document or legacy starter objects.");
        }
        StarterObject[] authoredObjects = (this.options.Objects ?? [new("target-left", new(-3, 1, -4), new(0.8f, 1, 0.8f)),
            new("target-center", new(0, 1, -6), new(0.8f, 1, 0.8f)),
            new("target-right", new(3, 1, -4), new(0.8f, 1, 0.8f))]).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        scenePlan = SceneCompiler.Compile(sceneDocument ?? StarterScenes.TrainingRange(authoredObjects));
        objects = ReadTargets(scenePlan);
        var targetColliders = new List<Collider3D>();
        foreach (PlacedSceneAgent declaration in scenePlan.Agents)
        {
            if (declaration.Node is SceneAgentNode<int> { Definition: StarterTargetDefinition target })
            {
                targetColliders.Add(new(declaration.Placement.Id, CollisionMesh3D.Box(target.HalfSize),
                    declaration.Placement.WorldTransform, SemanticOwnerId: declaration.Placement.Id));
            }
        }
        spatialWorld = SceneSpatial3D.Build(scenePlan, targetColliders);
        rayQueries = spatialWorld;
        ValidateObjects();
        gun = new(this.options.Gun);
        var normalized = this.options with { Gun = gun.Configuration, Objects = objects };
        string identity = definition.Identity + scenePlan.ContentIdentity
            + JsonSerializer.Serialize(normalized, StarterJsonContext.Default.StarterOptions);
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        Controls = new();
        Audio = new AurelianAudioRuntime(audioResources, audioBackend ?? new NullAudioOutputBackend());
        float[] samples = new float[2400];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = MathF.Sin(index * MathF.Tau * 440 / 48000) * 0.2f * (1 - (float)index / samples.Length);
        }
        audioResources.Add(new(new("shot"), "starter-shot-v1", 48000, 1, samples.Length, samples));
        string savesRoot = saveDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), id, "saves");
        Saves = new GameSaveSlots<StarterSnapshot>(id, Identity, new FileSaveStore(savesRoot),
            StarterJsonContext.Default.StarterSnapshot, ValidateSnapshot);
        settings = new GameSaveSlots<GameSettings>(id, "aurelian.settings.v1", new FileSaveStore(Path.Combine(savesRoot, "config")),
            StarterJsonContext.Default.GameSettings, ValidateSettings);
        ResetWorld();
        if (settings.ExistsAsync("preferences").GetAwaiter().GetResult())
        {
            Persist(LoadSettingsAsync);
        }
    }

    public string Id { get; }
    public GameDefinition Definition { get; }
    public string Identity { get; }
    public SpatialWorld3D SpatialWorld => spatialWorld;
    public CharacterMove3D? LastMovement { get; private set; }
    /// <summary>Borrow a backend for this immutable world; its caller owns disposal before the Vulkan plant.</summary>
    public void UseRayQueries(IRayQueryWorld3D backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        rayQueries = backend;
    }
    public GameControls Controls { get; private set; }
    public AurelianAudioRuntime Audio { get; }
    public GameSaveSlots<StarterSnapshot> Saves { get; }
    public string Screen { get; private set; } = "title";
    public bool Playing => Screen == "playing";
    public bool Quit { get; private set; }
    public int SelectedIndex => navigation.SelectedIndex;
    public CameraView View { get; private set; }
    public float Volume { get; private set; } = 1;
    public string? PersistenceError { get; private set; }
    public IReadOnlyList<GameAgent<int>> Agents => agents.Select(agent => agent.Agent).ToArray();
    public GameAgent<Vector3> Player => player.Agent;
    public SceneInstance Scene => scene;
    public event Action<StarterGame>? ShotFired;

    public GameMenuPage? Menu => Screen switch
    {
        "title" => new("starter.title", options.Title, PersistenceError is null ? "A TRAINING RANGE FOR YOUR NEXT GAME" : "SAVED DATA COULD NOT BE LOADED",
            [new("start", "Start"), new("load", "Load slot 1"), new("settings", "Settings / controls"), new("quit", "Quit")]),
        "pause" => new("starter.pause", "PAUSED", PersistenceError is null ? "Your progress waits while this menu is open." : "Save / load failed. Your current game is still here.",
            [new("resume", "Resume"), new("save", "Save slot 1"), new("load", "Load slot 1"), new("settings", "Settings / controls")]),
        "settings" => new("starter.settings", "SETTINGS", $"WASD MOVE / MOUSE LOOK / LMB FIRE / R RELOAD / V VIEW",
            [new("sensitivity", $"Mouse sensitivity: {Controls.Keys.MouseSensitivity:F4}"),
             new("volume", $"Volume: {Volume:F1}"), new("title", "Main menu"), new("back", "Back")]),
        _ => null,
    };

    public void Advance(TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }
        GameCommands commands = Controls.Tick((float)elapsed.TotalSeconds, !Playing);
        if (Playing && commands.Pause)
        {
            ChangeScreen("pause");
        }
        else if (!Playing)
        {
            if (commands.Menu.Back)
            {
                if (Screen == "title")
                {
                    Quit = true;
                }
                else
                {
                    ChangeScreen(Screen == "settings" ? returnScreen : "playing");
                }
            }
            else if (Menu is { } menu && navigation.Update(menu.Entries, commands.Menu) is { } action)
            {
                Activate(action);
            }
        }
        if (Playing)
        {
            pendingJump |= commands.Jump;
            pendingReload |= commands.Reload;
            pendingFire |= commands.Fire;
            yaw = MathF.IEEERemainder(yaw + commands.MouseYaw, MathF.Tau);
            pitch = Math.Clamp(pitch + commands.MousePitch, -1.3f, 1.3f);
            if (commands.SwitchView && Definition.Has(GameConcept.FirstPersonCamera) && Definition.Has(GameConcept.ThirdPersonCamera))
            {
                View = View == CameraView.FirstPerson ? CameraView.ThirdPerson : CameraView.FirstPerson;
            }
            CadenceAdvanceResult advance = scheduler.Advance(elapsed, SimulationExecutionRate.Normal);
            foreach (DueWorkFact tick in advance.DueWork)
            {
                Step(commands with { Jump = pendingJump, Reload = pendingReload, Fire = pendingFire || commands.Fire }, 1f / 60);
                pendingJump = pendingReload = pendingFire = false;
        LastMovement = null;
            }
        }
        Audio.Update(elapsed);
    }

    public void Activate(string action)
    {
        if (Menu is not { } menu || !menu.Entries.Any(entry => entry.Id == action && !entry.Disabled))
        {
            return;
        }
        switch (action)
        {
            case "start":
                ResetWorld();
                ChangeScreen("playing");
                break;
            case "resume":
                ChangeScreen("playing");
                break;
            case "settings":
                returnScreen = Screen;
                ChangeScreen("settings");
                break;
            case "back":
                ChangeScreen(returnScreen);
                break;
            case "title":
                ChangeScreen("title");
                break;
            case "quit":
                Quit = true;
                break;
            case "sensitivity":
                Rebind(Controls.Keys with { MouseSensitivity = Controls.Keys.MouseSensitivity < 0.005f ? 0.005f : 0.0025f });
                Persist(SaveSettingsAsync);
                break;
            case "volume":
                SetVolume(Volume > 0 ? 0 : 1);
                Persist(SaveSettingsAsync);
                break;
            case "save":
                Persist(() => SaveAsync("slot-1"));
                break;
            case "load":
                Persist(LoadDefaultSlotAsync);
                break;
        }
    }

    public void Pause()
    {
        if (Playing)
        {
            ChangeScreen("pause");
        }
        Controls.Adapter.OnFocusChanged(false);
    }

    public void Rebind(GameKeyBindings keys)
    {
        Controls.Rebind(keys);
    }

    public void SetVolume(float volume)
    {
        if (!float.IsFinite(volume) || volume < 0 || volume > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }
        Volume = volume;
        Audio.SetBusVolume(AudioBusId.Master, volume);
    }

    public StarterSnapshot Capture() => new(Point3.From(position), yaw, pitch, verticalVelocity, time, View,
        gun.State, agents.Select(agent => agent.State).ToArray(), Controls.Keys, Volume,
        scheduler.InspectAccumulators(), pendingJump, pendingReload, pendingFire);

    public Task SaveAsync(string slot, CancellationToken cancellation = default) => Saves.SaveAsync(slot, Capture(), cancellation);

    public Task SaveSettingsAsync() => settings.SaveAsync("preferences", new(Controls.Keys, Volume));

    public async Task LoadSettingsAsync()
    {
        GameSettings candidate = await settings.LoadAsync("preferences");
        Rebind(candidate.Keys);
        SetVolume(candidate.Volume);
    }

    private async Task LoadDefaultSlotAsync()
    {
        await LoadAsync("slot-1");
        ChangeScreen("playing");
    }

    public async Task LoadAsync(string slot, CancellationToken cancellation = default)
    {
        StarterSnapshot snapshot = await Saves.LoadAsync(slot, cancellation);
        Restore(snapshot);
    }

    public void Restore(StarterSnapshot snapshot)
    {
        ValidateSnapshot(snapshot);
        // The whole candidate has already passed validation before any live state is replaced.
        Rebind(snapshot.Keys);
        position = snapshot.Position.ToVector();
        yaw = snapshot.Yaw;
        pitch = snapshot.Pitch;
        verticalVelocity = snapshot.VerticalVelocity;
        time = snapshot.Time;
        View = snapshot.View;
        gun.Restore(snapshot.Gun);
        for (int index = 0; index < agents.Count; index++)
        {
            agents[index].State = snapshot.ObjectHealth[index];
        }
        SetVolume(snapshot.Volume);
        scheduler = NewScheduler();
        scheduler.RestoreAccumulators(snapshot.Cadence);
        pendingJump = snapshot.PendingJump;
        pendingReload = snapshot.PendingReload;
        pendingFire = snapshot.PendingFire;
        LastMovement = null;
        PersistenceError = null;
    }

    public void ReturnToTitle()
    {
        Quit = false;
        returnScreen = "title";
        ChangeScreen("title");
    }

    public StarterObservation Observe() => new(Screen, Point3.From(position), yaw, pitch, View,
        gun.State.Ammo, gun.State.ReloadRemaining > 0, gun.State.Shots,
        objects.Select((item, index) => item.Health - agents[index].State).Sum(), time, Identity, PersistenceError);

    public Matrix4x4 Camera(float aspect) => Camera3D.Matrix(Camera3D.Eye(position, yaw, pitch, View), Camera3D.Direction(yaw, pitch), aspect);

    public Native3DVertex[] BuildScene()
    {
        SceneFrame frame = scene.Project(agent => agent.Id != "player" || View == CameraView.ThirdPerson);
        if (Definition.Has(GameConcept.ReloadableGuns))
        {
            Vector3 aim = Camera3D.Direction(yaw, pitch);
            Vector3 right = Vector3.Normalize(Vector3.Cross(aim, Vector3.UnitY));
            Vector3 muzzle = position + Vector3.UnitY * 1.6f + aim * 0.6f + right * 0.22f - Vector3.UnitY * 0.22f;
            frame = frame with
            {
                Boxes = frame.Boxes.Add(new("view.gun", Matrix4x4.CreateTranslation(muzzle),
                    new(0.08f, 0.08f, 0.18f), new(0.3f, 0.6f, 0.7f, 1), SceneCollision.None)),
            };
        }
        return SceneGeometry3D.Build(frame);
    }

    private void Step(GameCommands commands, float seconds)
    {
        time += seconds;
        yaw = MathF.IEEERemainder(yaw + commands.Turn * seconds * 1.8f, MathF.Tau);
        pitch = Math.Clamp(pitch + commands.Look * seconds * 1.2f, -1.3f, 1.3f);
        bool control = Definition.Has(View == CameraView.FirstPerson ? GameConcept.FirstPersonControl : GameConcept.ThirdPersonControl);
        Vector3 movement = Vector3.Zero;
        if (control)
        {
            movement = new Vector3(MathF.Sin(yaw), 0, -MathF.Cos(yaw)) * commands.Forward +
                new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)) * commands.Strafe;
            if (movement.LengthSquared() > 1)
            {
                movement = Vector3.Normalize(movement);
            }
        }
        LastMovement = motor.Step(spatialWorld, new(position, verticalVelocity), movement * options.MovementSpeed,
            control && commands.Jump, seconds);
        position = LastMovement.State.Feet;
        verticalVelocity = LastMovement.State.VerticalVelocity;
        if (Definition.Has(GameConcept.ReloadableGuns) && gun.Step(commands.Fire, commands.Reload, seconds))
        {
            Vector3 origin = position + Vector3.UnitY * 1.6f;
            Vector3 direction = Camera3D.Direction(yaw, pitch);
            SpatialHit3D? contact = rayQueries.Raycast(new(origin, direction, 80));
            int hit = contact is null ? -1 : Array.FindIndex(objects, item => item.Id == contact.Value.ColliderId);
            if (hit >= 0 && agents[hit].State > 0)
            {
                agents[hit].State--;
            }
            // Presentation event identity stays unique when a save restores an earlier game tick.
            Audio.Play(new(new($"shot-{++audioEventSequence}"), new("shot"), AudioBusId.Sfx));
            ShotFired?.Invoke(this);
        }
    }

    private bool CanStand(Vector3 next) => spatialWorld.Overlap(Capsule3D.AtFeet(next)).IsEmpty;

    private void ResetWorld()
    {
        SceneInstance candidate = scenePlan.Mount();
        scene?.Dispose();
        scene = candidate;
        player = scene.Agent<Vector3>("player");
        agents = objects.Select(item => scene.Agent<int>(item.Id)).ToArray();
        yaw = pitch = verticalVelocity = 0;
        time = 0;
        View = Definition.Has(GameConcept.FirstPersonCamera) ? CameraView.FirstPerson : CameraView.ThirdPerson;
        gun.Restore(new(gun.Configuration.Capacity, 0, 0, 0));
        scheduler = NewScheduler();
        pendingJump = pendingReload = pendingFire = false;
        LastMovement = null;
    }

    private void ValidateObjects()
    {
        if (objects.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != objects.Length ||
            objects.Any(item => string.IsNullOrWhiteSpace(item.Id) || !item.Position.IsFinite || !item.HalfSize.IsFinite ||
                item.HalfSize.X <= 0 || item.HalfSize.Y <= 0 || item.HalfSize.Z <= 0 || item.Health <= 0))
        {
            throw new ArgumentException("Starter objects need unique ids, finite geometry, positive sizes and health.");
        }
        Vector3 spawn = scenePlan.Agents.Single(agent => agent.Placement.Id == "player").Placement.Position;
        if (!CanStand(spawn))
        {
            throw new ArgumentException("Starter player spawn must be clear of collision.");
        }
    }

    private static StarterObject[] ReadTargets(ScenePlan plan)
    {
        if (plan.Agents.Count(agent => agent.Placement.Id == "player"
            && agent.Node is SceneAgentNode<Vector3> { Definition: StarterPlayerDefinition }) != 1)
        {
            throw new InvalidDataException("Starter scenes need one root 'player' using StarterPlayerDefinition.");
        }
        var result = new List<StarterObject>();
        foreach (PlacedSceneAgent declaration in plan.Agents)
        {
            if (declaration.Placement.Id == "player")
            {
                continue;
            }
            if (declaration.Node is not SceneAgentNode<int> { Definition: StarterTargetDefinition target })
            {
                throw new InvalidDataException("Starter snapshots support StarterPlayerDefinition and StarterTargetDefinition; custom games own other state schemas.");
            }
            result.Add(CollisionBox(declaration.Placement.Id, declaration.Placement.WorldTransform, target.HalfSize, target.Health));
        }
        return result.ToArray();
    }

    private static StarterObject CollisionBox(string id, Matrix4x4 transform, Vector3 halfSize, int health)
    {
        Vector3 extent = new(
            MathF.Abs(transform.M11) * halfSize.X + MathF.Abs(transform.M21) * halfSize.Y + MathF.Abs(transform.M31) * halfSize.Z,
            MathF.Abs(transform.M12) * halfSize.X + MathF.Abs(transform.M22) * halfSize.Y + MathF.Abs(transform.M32) * halfSize.Z,
            MathF.Abs(transform.M13) * halfSize.X + MathF.Abs(transform.M23) * halfSize.Y + MathF.Abs(transform.M33) * halfSize.Z);
        return new(id, Point3.From(transform.Translation),
            Point3.From(extent), health);
    }

    private void ValidateSnapshot(StarterSnapshot snapshot)
    {
        if (snapshot.Position is null || !snapshot.Position.IsFinite || !CanStand(snapshot.Position.ToVector()) ||
            !float.IsFinite(snapshot.Yaw) || !float.IsFinite(snapshot.Pitch) || MathF.Abs(snapshot.Pitch) > 1.3f ||
            !float.IsFinite(snapshot.VerticalVelocity) ||
            !double.IsFinite(snapshot.Time) || snapshot.Time < 0 || !Enum.IsDefined(snapshot.View) ||
            !Definition.Has(snapshot.View == CameraView.FirstPerson ? GameConcept.FirstPersonCamera : GameConcept.ThirdPersonCamera) ||
            snapshot.ObjectHealth is null || snapshot.ObjectHealth.Count != objects.Length ||
            snapshot.ObjectHealth.Where((health, index) => health < 0 || health > objects[index].Health).Any() ||
            snapshot.Gun is null || snapshot.Keys is null || !float.IsFinite(snapshot.Volume) || snapshot.Volume < 0 || snapshot.Volume > 1)
        {
            throw new InvalidDataException("Invalid starter state; the live game has not been changed.");
        }
        gun.Validate(snapshot.Gun);
        GameControls.Validate(snapshot.Keys);
        NewScheduler().RestoreAccumulators(snapshot.Cadence);
    }

    private static void ValidateSettings(GameSettings candidate)
    {
        if (candidate.Keys is null || !float.IsFinite(candidate.Volume) || candidate.Volume < 0 || candidate.Volume > 1)
        {
            throw new InvalidDataException("Invalid game preferences.");
        }
        GameControls.Validate(candidate.Keys);
    }

    private void ChangeScreen(string next)
    {
        Screen = next;
        navigation.Reset();
        pendingJump = pendingReload = pendingFire = false;
        LastMovement = null;
        Controls.Adapter.OnFocusChanged(false);
        Controls.Adapter.OnFocusChanged(true);
    }

    private void Persist(Func<Task> operation)
    {
        try
        {
            operation().GetAwaiter().GetResult();
            PersistenceError = null;
        }
        catch (Exception exception) when (exception is IOException or Deliverance.Core.DeliveranceException or JsonException or ArgumentException)
        {
            PersistenceError = exception.Message;
            Console.Error.WriteLine("Game persistence failed: " + exception.Message);
        }
    }

    private static CadenceScheduler NewScheduler() => new([new(new("gameplay"), RationalRate.PerSecond(60), 0)], TimeSpan.FromSeconds(0.1));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        scene.Dispose();
        Controls.Dispose();
        Audio.Dispose();
        audioResources.Dispose();
    }
}

