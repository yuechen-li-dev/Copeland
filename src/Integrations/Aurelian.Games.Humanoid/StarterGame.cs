using System.Numerics;
using System.Collections.Immutable;
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
using Aurelian.Spatial3D;
using Aurelian.Spatial3D.Vulkan;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;
using Aetheris.Humanoid;
using Aurelian.Humanoid;
using Aurelian.Runtime.Dominatus.Inspection;
using InputMan.Aurelian;
using InputMan.Core;

namespace Aurelian.Games;

/// <summary>
/// The composable FPS/TPS training range. Only range rules live here: the window, menus, settings,
/// input devices, audio buses, cadence, pause and save slots belong to the shell (<see cref="Session"/>).
/// </summary>
public sealed class StarterGame : IGame
{
    private const string ShotSound = "starter.shot";
    private readonly StarterOptions options;
    private readonly StarterObject[] objects;
    private readonly SpatialWorld3D spatialWorld;
    private readonly CharacterMotor3D motor = new();
    private IRayQueryWorld3D rayQueries = null!;
    private readonly ScenePlan scenePlan;
    private readonly SceneGroup document;
    private readonly HumanoidPlayerOptions? humanoid;
    private readonly HumanoidLocomotion? locomotion;
    private readonly GameSession session;
    private GameContext context = null!;
    private AurelianAgentRuntime? policies;
    private HumanoidCharacterDefinition? humanoidDefinition;
    private SceneAgent<HumanoidCharacterState>? humanoidPlayer;
    private SceneInstance scene = null!;
    private IReadOnlyList<SceneAgent<int>> agents = [];
    private SceneAgent<Vector3> player = null!;
    private HumanoidGamePresenter? characterPresenter;
    private VulkanSpatialRayQueries3D? gpuRays;
    private Vector3 position
    {
        get => humanoidPlayer?.State.Position ?? player.State;
        set
        {
            if (humanoidPlayer is not null)
            {
                humanoidPlayer.State = humanoidPlayer.State with { Position = value };
            }
            else
            {
                player.State = value;
            }
        }
    }
    private float yaw;
    private float pitch;
    private float verticalVelocity;
    private double time;
    private readonly ReloadableGun gun;
    private readonly GameSaveSchema saveSchema;
    private bool disposed;

    public StarterGame(string id, GameDefinition definition, StarterOptions? options = null,
        string? saveDirectory = null, IAudioOutputBackend? audioBackend = null, SceneGroup? sceneDocument = null,
        HumanoidPlayerOptions? humanoid = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Game id must use ASCII letters, digits or hyphens.", nameof(id));
        }
        Id = id;
        Definition = definition;
        if (definition.Has(GameConcept.HumanoidPresentation) != (humanoid is not null))
        {
            throw new ArgumentException("HumanoidPresentation requires explicit HumanoidPlayerOptions, and options require the concept.");
        }
        humanoid?.Validate();
        this.humanoid = humanoid;
        locomotion = humanoid?.Locomotion is null ? null : new(humanoid.Body, humanoid.Locomotion);
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
        document = sceneDocument ?? StarterScenes.TrainingRange(authoredObjects);
        scenePlan = SceneCompiler.Compile(document);
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
        string identity = definition.Identity + scenePlan.ContentIdentity + humanoid?.Identity
            + JsonSerializer.Serialize(normalized, StarterJsonContext.Default.StarterOptions);
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        Manifest = new GameManifest(id, this.options.Title, StarterInput.Layout)
        {
            Tagline = "A TRAINING RANGE FOR YOUR NEXT GAME",
            ControlsHint = "WASD MOVE / MOUSE LOOK / LMB FIRE / R RELOAD / V VIEW",
            Graphics = this.options.Graphics,
            DefaultDisplay = new GameDisplaySettings(960, 600),
        };
        saveSchema = GameSaveSchema.Create(StarterJsonContext.Default.StarterSnapshot, Identity, Capture, ValidateSnapshot, RestoreValidated);
        ResetWorld();
        session = new GameSession(this, saveDirectory, audioBackend);
        Controls = new StarterControls(session);
    }

    // ---- Shell facade: the same session any IGame gets from GameApp. ----

    /// <summary>The shell running this game: menus, settings, input, audio, cadence and saves.</summary>
    public GameSession Session => session;
    public StarterControls Controls { get; }
    public AurelianAudioRuntime Audio => session.Audio;
    /// <summary>The Deliverance slots holding this range's world saves (with the shell's cadence phase).</summary>
    public GameSaveSlots<GameShellSave> Saves => session.Saves;
    public string Screen => session.Screen;
    public bool Playing => session.Playing;
    public bool Quit => session.Quit;
    public int SelectedIndex => session.SelectedIndex;
    public GameMenuPage? Menu => session.Menu;
    public float Volume => session.Settings.Audio.Master;
    public string? PersistenceError => session.PersistenceError;
    public void Advance(TimeSpan elapsed) => session.Advance(elapsed);
    public void Activate(string action) => session.Activate(action);
    public void Pause() => session.Pause();
    public void ReturnToTitle() => session.ReturnToTitle();
    public Task SaveAsync(string slot, CancellationToken cancellation = default) => session.SaveAsync(slot, cancellation);
    public Task LoadAsync(string slot, CancellationToken cancellation = default) => session.LoadAsync(slot, cancellation);

    /// <summary>Change the FPS bindings and mouse sensitivity through the shell's persisted control settings.</summary>
    public void Rebind(GameKeyBindings keys) => Controls.Apply(keys);

    public void SetVolume(float volume)
    {
        if (!float.IsFinite(volume) || volume < 0 || volume > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }
        session.ApplySettings(session.Settings with { Audio = session.Settings.Audio with { Master = volume } });
    }

    public Task SaveSettingsAsync()
    {
        session.SaveSettings();
        return session.PersistenceError is null ? Task.CompletedTask
            : Task.FromException(new IOException(session.PersistenceError));
    }

    // ---- Range state ----

    public string Id { get; }
    public GameDefinition Definition { get; }
    public string Identity { get; }
    public GameManifest Manifest { get; }
    GameSaveSchema? IGame.Saves => saveSchema;
    public SpatialWorld3D SpatialWorld => spatialWorld;
    public CharacterMove3D? LastMovement { get; private set; }

    /// <summary>Borrow a backend for this immutable world; its caller owns disposal before the Vulkan plant.</summary>
    public void UseRayQueries(IRayQueryWorld3D backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        rayQueries = backend;
    }

    public CameraView View { get; private set; }
    public IReadOnlyList<GameAgent<int>> Agents => agents.Select(agent => agent.Agent).ToArray();
    public GameAgent<Vector3> Player => humanoidPlayer is null ? player.Agent
        : throw new InvalidOperationException("This player uses HumanoidPlayer's typed state.");
    public SceneAgent<HumanoidCharacterState>? HumanoidPlayer => humanoidPlayer;
    public HumanoidPlayerOptions? HumanoidOptions => humanoid;
    public AurelianAgentRuntime? PresentationPolicies => policies;
    public SolvedHumanoidPose? CharacterPose => humanoidPlayer is null ? null : humanoidDefinition!.Pose(humanoidPlayer);
    public SceneInstance Scene => scene;
    public int CharacterDispatches => characterPresenter?.DispatchCount ?? 0;
    public long RayQueryDispatches => gpuRays?.DispatchCount ?? 0;
    public event Action<StarterGame>? ShotFired;

    public StarterSnapshot Capture() => new(Point3.From(position), yaw, pitch, verticalVelocity, time, View,
        gun.State, agents.Select(agent => agent.State).ToArray(),
        humanoidPlayer is null ? null : new(humanoidPlayer.State.Animation, policies!.CapturePolicyCheckpoint()));

    /// <summary>Validate the whole candidate, then replace live range state. Shell state (cadence, settings) is untouched.</summary>
    public void Restore(StarterSnapshot snapshot)
    {
        ValidateSnapshot(snapshot);
        RestoreValidated(snapshot);
    }

    public StarterObservation Observe() => new(session.Screen, Point3.From(position), yaw, pitch, View,
        gun.State.Ammo, gun.State.ReloadRemaining > 0, gun.State.Shots,
        objects.Select((item, index) => item.Health - agents[index].State).Sum(), time, Identity, session.PersistenceError,
        humanoidPlayer?.State.Animation.Motion.ToString(), humanoidPlayer?.State.Animation.ClipSeconds,
        humanoidPlayer?.State.Animation.Locomotion?.Gait);

    public Matrix4x4 Camera(float aspect) => Camera3D.Matrix(Camera3D.Eye(position, yaw, pitch, View), Camera3D.Direction(yaw, pitch), aspect);

    public Vector3 CameraEye => Camera3D.Eye(position, yaw, pitch, View);
    public Native3DVertex[] BuildScene() => SceneGeometry3D.Build(BuildSceneFrame());
    public Native3DScene BuildRenderScene() => SceneGeometry3D.BuildScene(BuildSceneFrame());

    // ---- IGame ----

    void IGame.Start(GameContext context)
    {
        this.context = context;
        context.AddTone(ShotSound, 440, 0.05f);
    }

    void IGame.NewGame() => ResetWorld();

    void IGame.Update(GameUpdate update)
    {
        yaw = MathF.IEEERemainder(yaw + update.Input.Look.X, MathF.Tau);
        pitch = Math.Clamp(pitch + update.Input.Look.Y, -1.3f, 1.3f);
        if (update.Input.WasPressed(StarterInput.View) && Definition.Has(GameConcept.FirstPersonCamera)
            && Definition.Has(GameConcept.ThirdPersonCamera))
        {
            View = View == CameraView.FirstPerson ? CameraView.ThirdPerson : CameraView.FirstPerson;
        }
    }

    void IGame.FixedUpdate(GameTick tick) => Step(tick.Input, tick.Seconds);

    GameView? IGame.View(GameViewRequest request) => new(BuildRenderScene(), Camera(request.Aspect), CameraEye);

    void IGame.BuildHud(GameHud hud)
    {
        StarterObservation state = Observe();
        hud.StatusBar($"{state.View}   AMMO {state.Ammo}   SHOTS {state.Shots}   HITS {state.Hits}   " +
            (state.Reloading ? "RELOADING" : "LMB FIRE / R RELOAD / V VIEW / ESC MENU"));
        hud.Crosshair();
    }

    void IGame.Observe(GameFacts facts)
    {
        StarterObservation state = Observe();
        facts.Set("view", state.View.ToString());
        facts.Set("ammo", state.Ammo);
        facts.Set("reloading", state.Reloading);
        facts.Set("shots", state.Shots);
        facts.Set("hits", state.Hits);
        facts.Set("position", state.Position.ToVector());
        facts.Set("motion", state.Motion ?? "None");
        facts.Set("gait", state.Gait?.ToString() ?? "None");
    }

    void IGame.AttachGraphics(NativeGameGraphics graphics)
    {
        if (options.EnvironmentAsset is not null)
        {
            graphics.Environment = new GameAssets().LoadEnvironment(options.EnvironmentAsset);
        }
        graphics.ReflectionProbe = options.ReflectionProbe;
        graphics.LocalLights = options.LocalLights;
        if (humanoid is not null)
        {
            characterPresenter = new HumanoidGamePresenter(graphics.Plant, humanoid);
        }
        if (graphics.Plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal))
        {
            gpuRays = GameRayQueries.Create(graphics.Plant, spatialWorld);
            UseRayQueries(gpuRays);
        }
        Console.WriteLine($"AURELIAN_STARTER_RAYS backend={(gpuRays is null ? "CPU" : "Vulkan")}");
    }

    NativeGpuGeometry3D? IGame.PrepareGpuGeometry(NativeGameGraphics graphics)
    {
        if (humanoidPlayer is null || characterPresenter is null)
        {
            return null;
        }
        return characterPresenter.Present(CharacterPose!, humanoidPlayer.WorldTransform, View == CameraView.ThirdPerson);
    }

    void IGame.DetachGraphics()
    {
        Console.WriteLine($"AURELIAN_RAY_QUERY_DISPATCHES count={RayQueryDispatches}");
        Console.WriteLine($"AURELIAN_HUMANOID_GPU_DISPATCHES count={CharacterDispatches}");
        rayQueries = spatialWorld;
        gpuRays?.Dispose();
        gpuRays = null;
        characterPresenter?.Dispose();
        characterPresenter = null;
    }

    private SceneFrame BuildSceneFrame()
    {
        SceneFrame frame = scene.Project(agent => agent.Id != "player" || View == CameraView.ThirdPerson);
        if (humanoidPlayer is not null)
        {
            SceneFrame equipment = humanoid!.Attach(CharacterPose!, humanoidPlayer.WorldTransform);
            frame = frame with
            {
                Boxes = frame.Boxes.AddRange(equipment.Boxes),
                Meshes = frame.Meshes.AddRange(equipment.Meshes),
                Models = frame.Models.AddRange(equipment.Models),
            };
        }
        else if (Definition.Has(GameConcept.ReloadableGuns))
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
        return frame;
    }

    private void Step(GameInputState input, float seconds)
    {
        time += seconds;
        yaw = MathF.IEEERemainder(yaw + input.Axis(StarterInput.TurnLeft, StarterInput.TurnRight) * seconds * 1.8f, MathF.Tau);
        pitch = Math.Clamp(pitch + input.Axis(StarterInput.LookDown, StarterInput.LookUp) * seconds * 1.2f, -1.3f, 1.3f);
        bool control = Definition.Has(View == CameraView.FirstPerson ? GameConcept.FirstPersonControl : GameConcept.ThirdPersonControl);
        bool fire = input.WasPressed(StarterInput.Fire) || input.IsDown(StarterInput.Fire);
        Vector3 movement = Vector3.Zero;
        if (control)
        {
            float forward = input.Axis(StarterInput.Backward, StarterInput.Forward);
            float strafe = input.Axis(StarterInput.Left, StarterInput.Right);
            movement = new Vector3(MathF.Sin(yaw), 0, -MathF.Cos(yaw)) * forward +
                new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)) * strafe;
            if (movement.LengthSquared() > 1)
            {
                movement = Vector3.Normalize(movement);
            }
        }
        Vector3 previousPosition = position;
        bool sprint = input.IsDown(StarterInput.Sprint);
        float requestedSpeed = humanoid?.Locomotion is null || sprint ? options.MovementSpeed : options.MovementSpeed * .4f;
        Vector3 velocity = movement * requestedSpeed;
        HumanoidRootMotionRequest? root = null;
        if (humanoid?.Locomotion is { } bank)
        {
            root = bank.Request(humanoidPlayer!.State.Animation.Locomotion!, velocity, seconds);
            velocity = root.Displacement / seconds;
        }
        LastMovement = motor.Step(spatialWorld, new(position, verticalVelocity), velocity,
            control && input.WasPressed(StarterInput.Jump), seconds);
        position = LastMovement.State.Feet;
        verticalVelocity = LastMovement.State.VerticalVelocity;
        if (humanoidPlayer is not null)
        {
            Vector3 acceptedVelocity = (position - previousPosition) / seconds;
            float speed = new Vector2(acceptedVelocity.X, acceptedVelocity.Z).Length();
            humanoidPlayer.State = humanoidPlayer.State with { Heading = -yaw };
            humanoidDefinition!.Observe(humanoidPlayer, new(speed, fire, LastMovement.Grounded));
            policies!.Tick(TimeSpan.FromSeconds(seconds));
            humanoidDefinition.Advance(humanoidPlayer, seconds);
            if (root is not null)
            {
                var animation = humanoidPlayer.State.Animation;
                var aimPose = humanoid!.Body.Solve("aim-layer", humanoid.Animations.Sample(animation)).Pose!;
                var next = locomotion!.Advance(animation.Locomotion!, root, position - previousPosition, LastMovement.Grounded,
                    humanoidPlayer.WorldTransform, rayQueries, fire, aimPose, seconds);
                humanoidPlayer.State = humanoidPlayer.State with { Animation = animation with { Locomotion = next } };
                humanoidDefinition.ObserveLocomotion(humanoidPlayer.Id, next);
            }
        }
        if (Definition.Has(GameConcept.ReloadableGuns) && gun.Step(fire, input.WasPressed(StarterInput.Reload), seconds))
        {
            Vector3 origin = position + Vector3.UnitY * 1.6f;
            Vector3 direction = Camera3D.Direction(yaw, pitch);
            SpatialHit3D? contact = rayQueries.Raycast(new(origin, direction, 80));
            int hit = contact is null ? -1 : Array.FindIndex(objects, item => item.Id == contact.Value.ColliderId);
            if (hit >= 0 && agents[hit].State > 0)
            {
                agents[hit].State--;
            }
            context.PlaySound(ShotSound);
            ShotFired?.Invoke(this);
        }
    }

    private bool CanStand(Vector3 next) => spatialWorld.Overlap(Capsule3D.AtFeet(next)).IsEmpty;

    private void ResetWorld()
    {
        if (humanoid is null)
        {
            SceneInstance candidate = scenePlan.Mount();
            scene?.Dispose();
            scene = candidate;
            player = scene.Agent<Vector3>("player");
            agents = objects.Select(item => scene.Agent<int>(item.Id)).ToArray();
        }
        else
        {
            ReplaceHumanoidWorld(null);
        }
        yaw = humanoidPlayer is null ? 0 : -humanoidPlayer.State.Heading;
        pitch = verticalVelocity = 0;
        time = 0;
        View = Definition.Has(GameConcept.FirstPersonCamera) ? CameraView.FirstPerson : CameraView.ThirdPerson;
        gun.Restore(new(gun.Configuration.Capacity, 0, 0, 0));
        LastMovement = null;
    }

    private void RestoreValidated(StarterSnapshot snapshot)
    {
        if (humanoid is not null)
        {
            ReplaceHumanoidWorld(snapshot.Humanoid);
        }
        position = snapshot.Position.ToVector();
        yaw = snapshot.Yaw;
        if (humanoidPlayer is not null)
        {
            humanoidPlayer.State = humanoidPlayer.State with { Heading = -yaw, Animation = snapshot.Humanoid!.Animation };
        }
        pitch = snapshot.Pitch;
        verticalVelocity = snapshot.VerticalVelocity;
        time = snapshot.Time;
        View = snapshot.View;
        gun.Restore(snapshot.Gun);
        for (int index = 0; index < agents.Count; index++)
        {
            agents[index].State = snapshot.ObjectHealth[index];
        }
        LastMovement = null;
    }

    private void ReplaceHumanoidWorld(StarterHumanoidSnapshot? saved)
    {
        var candidatePolicies = new AurelianAgentRuntime(512);
        var candidateDefinition = new HumanoidCharacterDefinition(humanoid!.Body, candidatePolicies, humanoid.Animations)
        {
            Template = AgentTemplate.Character("starter.player", AgentControl.Human),
            Locomotion = humanoid.Locomotion,
        };
        var children = ImmutableArray.CreateBuilder<SceneNode>();
        foreach (SceneNode node in document.Children)
        {
            if (node is SceneAgentNode<Vector3> { Id: "player" } playerNode)
            {
                children.Add(new SceneAgentNode<HumanoidCharacterState>(playerNode.Id, playerNode.Name, candidateDefinition)
                {
                    Transform = playerNode.Transform,
                });
            }
            else
            {
                children.Add(node);
            }
        }
        SceneGroup candidateDocument = document with { Children = children.ToImmutable() };
        SceneInstance candidate = SceneCompiler.Compile(candidateDocument).Mount();
        try
        {
            if (saved is not null)
            {
                candidatePolicies.RestorePolicyCheckpoint(saved.Policy);
                candidateDefinition.ValidatePolicyState("player", saved.Animation);
                candidate.Agent<HumanoidCharacterState>("player").State =
                    candidate.Agent<HumanoidCharacterState>("player").State with { Animation = saved.Animation };
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or
            ArgumentException or FormatException or NotSupportedException)
        {
            candidate.Dispose();
            throw new InvalidDataException("Invalid humanoid policy snapshot; the live game has not been changed.", exception);
        }
        catch
        {
            candidate.Dispose();
            throw;
        }
        scene?.Dispose();
        scene = candidate;
        policies = candidatePolicies;
        humanoidDefinition = candidateDefinition;
        humanoidPlayer = scene.Agent<HumanoidCharacterState>("player");
        agents = objects.Select(item => scene.Agent<int>(item.Id)).ToArray();
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
        if (snapshot is null || snapshot.Position is null || !snapshot.Position.IsFinite || !CanStand(snapshot.Position.ToVector()) ||
            !float.IsFinite(snapshot.Yaw) || !float.IsFinite(snapshot.Pitch) || MathF.Abs(snapshot.Pitch) > 1.3f ||
            !float.IsFinite(snapshot.VerticalVelocity) ||
            !double.IsFinite(snapshot.Time) || snapshot.Time < 0 || !Enum.IsDefined(snapshot.View) ||
            !Definition.Has(snapshot.View == CameraView.FirstPerson ? GameConcept.FirstPersonCamera : GameConcept.ThirdPersonCamera) ||
            snapshot.ObjectHealth is null || snapshot.ObjectHealth.Count != objects.Length ||
            snapshot.ObjectHealth.Where((health, index) => health < 0 || health > objects[index].Health).Any() ||
            snapshot.Gun is null)
        {
            throw new InvalidDataException("Invalid starter state; the live game has not been changed.");
        }
        gun.Validate(snapshot.Gun);
        if ((snapshot.Humanoid is not null) != (humanoid is not null))
        {
            throw new InvalidDataException("Snapshot humanoid composition does not match this game.");
        }
        if (snapshot.Humanoid is { } character)
        {
            humanoid!.Animations.ValidateState(character.Animation);
            if ((character.Animation.Locomotion is not null) != (humanoid.Locomotion is not null))
            {
                throw new InvalidDataException("Snapshot locomotion composition does not match the game.");
            }
            if (character.Animation.Locomotion is { } locomotionState)
            {
                humanoid.Locomotion!.ValidateState(humanoid.Body, locomotionState);
            }
            var transition = humanoid.Body.Solve("saved-transition", character.Animation.TransitionFrom);
            var sampled = humanoid.Body.Solve("saved-animation", humanoid.Animations.Sample(character.Animation));
            if (!transition.IsSolved || !sampled.IsSolved || character.Policy is null ||
                character.Policy.Kernel is null || character.Policy.Agents is null ||
                character.Policy.Kernel.Agents is null || character.Policy.Kernel.Agents.Any(agent =>
                    agent is null || agent.ActiveStatePath is null || agent.BlackboardBlob is null || agent.EventCursorBlob is null))
            {
                throw new InvalidDataException("Snapshot contains an invalid humanoid pose or policy.");
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        // The session disposes its game; this guard makes that re-entrant call a no-op.
        session?.Dispose();
        gpuRays?.Dispose();
        characterPresenter?.Dispose();
        scene?.Dispose();
    }
}

/// <summary>The range's input actions. Arrow keys are fixed turn/look alternatives; the rest are rebindable.</summary>
public static class StarterInput
{
    public const string Forward = "move.forward";
    public const string Backward = "move.backward";
    public const string Left = "move.left";
    public const string Right = "move.right";
    public const string Jump = "jump";
    public const string Fire = "fire";
    public const string Reload = "reload";
    public const string View = "view";
    public const string Sprint = "sprint";
    public const string TurnLeft = "turn.left";
    public const string TurnRight = "turn.right";
    public const string LookUp = "look.up";
    public const string LookDown = "look.down";

    public static GameInputLayout Layout { get; } = new(
    [
        new(Forward, "Move forward", GameControl.Key(KeyboardKey.W)),
        new(Backward, "Move back", GameControl.Key(KeyboardKey.S)),
        new(Left, "Strafe left", GameControl.Key(KeyboardKey.A)),
        new(Right, "Strafe right", GameControl.Key(KeyboardKey.D)),
        new(Jump, "Jump", GameControl.Key(KeyboardKey.Space)),
        new(Fire, "Fire", GameControl.Mouse(MouseButton.Primary)),
        new(Reload, "Reload", GameControl.Key(KeyboardKey.R)),
        new(View, "Switch view", GameControl.Key(KeyboardKey.V)),
        new(Sprint, "Sprint", GameControl.Key(KeyboardKey.LeftShift)),
        new(TurnLeft, "Turn left", GameControl.Key(KeyboardKey.ArrowLeft), Rebindable: false),
        new(TurnRight, "Turn right", GameControl.Key(KeyboardKey.ArrowRight), Rebindable: false),
        new(LookUp, "Look up", GameControl.Key(KeyboardKey.ArrowUp), Rebindable: false),
        new(LookDown, "Look down", GameControl.Key(KeyboardKey.ArrowDown), Rebindable: false),
    ], GameLookMode.CapturedMouse);
}

/// <summary>Typed FPS view of the shell's input bindings, kept for code written against <see cref="GameKeyBindings"/>.</summary>
public sealed class StarterControls
{
    private readonly GameSession session;

    internal StarterControls(GameSession session)
    {
        this.session = session;
    }

    /// <summary>The native input adapter. It survives rebinding; only its profile changes.</summary>
    public AurelianInputAdapter Adapter => session.Input.Adapter;

    public GameKeyBindings Keys => new(
        Key(StarterInput.Forward), Key(StarterInput.Backward), Key(StarterInput.Left), Key(StarterInput.Right),
        Key(StarterInput.Jump), Key(StarterInput.Reload), Key(StarterInput.View),
        session.Input.Settings.MouseSensitivity, Key(StarterInput.Sprint));

    internal void Apply(GameKeyBindings keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        GameControls.Validate(keys);
        if (keys.MouseSensitivity < GameControlSettings.MinimumSensitivity || keys.MouseSensitivity > GameControlSettings.MaximumSensitivity)
        {
            throw new ArgumentException("Mouse sensitivity is outside the shell's supported range.");
        }
        var wanted = new Dictionary<string, GameControl>(StringComparer.Ordinal)
        {
            [StarterInput.Forward] = GameControl.Key(keys.Forward),
            [StarterInput.Backward] = GameControl.Key(keys.Backward),
            [StarterInput.Left] = GameControl.Key(keys.Left),
            [StarterInput.Right] = GameControl.Key(keys.Right),
            [StarterInput.Jump] = GameControl.Key(keys.Jump),
            [StarterInput.Reload] = GameControl.Key(keys.Reload),
            [StarterInput.View] = GameControl.Key(keys.SwitchView),
            [StarterInput.Sprint] = GameControl.Key(keys.Sprint),
        };
        var overrides = new List<GameBindingOverride>();
        foreach (GameInputAction action in StarterInput.Layout.Actions)
        {
            GameControl control = wanted.TryGetValue(action.Id, out GameControl? key) ? key : session.Input.Binding(action.Id);
            if (control != action.Primary)
            {
                overrides.Add(new GameBindingOverride(action.Id, control));
            }
        }
        session.ApplySettings(session.Settings with
        {
            Controls = session.Input.Settings with { MouseSensitivity = keys.MouseSensitivity, Bindings = overrides },
        });
    }

    private KeyboardKey Key(string action)
    {
        GameControl control = session.Input.Binding(action);
        return control.Device == DeviceKind.Keyboard ? (KeyboardKey)control.Code : KeyboardKey.Unknown;
    }
}
