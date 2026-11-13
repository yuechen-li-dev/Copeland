using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Runtime;
using Aurelian.Spatial3D.Vulkan;
using Aurelian.Runtime.Inspection;
using System.Security.Cryptography;
using System.Text.Json;
using InputMan.Core;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Beacon3D;

/// <summary>Beacon's rules and presentation hosted by the common application shell.</summary>
public sealed class BeaconShellGame(HumanoidPlayerOptions? humanoid = null, string? lighting = null, bool regionalGi = false, bool dynamicGi = false) : IGame
{
    public BeaconPresentation Presentation { get; } = new();
    private BeaconResidentScene3D resident = new();
    private GameView? currentView;
    private bool diffuseReady;
    private readonly bool useGi = regionalGi || dynamicGi;
    private BeaconGame game = new(humanoid: humanoid);
    private NativeGameGraphics? attachedGraphics;
    private HumanoidGamePresenter? character;
    private VulkanSpatialRayQueries3D? rays;

    public GameManifest Manifest { get; } = new("beacon-run", "BEACON RUN", new GameInputLayout(
    [
        new("forward", "Forward", GameControl.Key(KeyboardKey.W)),
        new("back", "Back", GameControl.Key(KeyboardKey.S)),
        new("left", "Strafe left", GameControl.Key(KeyboardKey.A)),
        new("right", "Strafe right", GameControl.Key(KeyboardKey.D)),
        new("turn.left", "Turn left", GameControl.Key(KeyboardKey.ArrowLeft)),
        new("turn.right", "Turn right", GameControl.Key(KeyboardKey.ArrowRight)),
        new("look.up", "Look up", GameControl.Key(KeyboardKey.ArrowUp)),
        new("look.down", "Look down", GameControl.Key(KeyboardKey.ArrowDown)),
        new("jump", "Jump", GameControl.Key(KeyboardKey.Space)),
        new("aim", "Aim down sights", GameControl.Mouse(MouseButton.Secondary)),
        new("motion", "Toggle presentation motion", GameControl.Key(KeyboardKey.B)),
        new("fire", "Fire", GameControl.Mouse(MouseButton.Primary)),
        new("reload", "Reload", GameControl.Key(KeyboardKey.R)),
        new("view", "Switch view", GameControl.Key(KeyboardKey.V)),
        new("sprint", "Sprint", GameControl.Key(KeyboardKey.LeftShift)),
        new("restart", "Restart run", GameControl.Key(KeyboardKey.Enter)),
    ], GameLookMode.CapturedMouse))
    {
        Tagline = "SURVIVE THREE WAVES. COLLECT THREE BEACONS. ESCAPE.",
        ControlsHint = "WASD MOVE   MOUSE AIM   CLICK FIRE   RMB AIM   R RELOAD   B MOTION   V VIEW   ESC PAUSE",
        // Published transfer assets are compiled for Lambert; ordinary play uses PathDiffuse.
        Graphics = regionalGi || dynamicGi ? BeaconDiffuseLighting3D.Settings : Graphics3DSettings.Default with
        {
            DirectDiffuseModel = lighting is null ? DirectDiffuseModel3D.PathDiffuse : DirectDiffuseModel3D.Lambert,
        },
    };

    public BeaconGame World => game;

    public void Start(GameContext context)
    {
        Presentation.GroundWeather = !useGi && lighting is null;
        Presentation.Start(context);
        Presentation.Reset(game);
    }

    public void OnFrame(GameFrame frame) => Presentation.Frame(game, frame);

    public void NewGame()
    {
        game.Dispose();
        resident = new();
        diffuseReady = false;
        game = new BeaconGame(humanoid: humanoid);
        Presentation.Reset(game);
        if (rays is not null)
        {
            game.UseRayQueries(rays);
        }
    }

    public void FixedUpdate(GameTick tick)
    {
        var input = tick.Input;
        if ((game.Won || game.Dead) && input.WasPressed("restart"))
        {
            NewGame();
            return;
        }
        if (input.WasPressed("motion"))
        {
            Presentation.Enabled = !Presentation.Enabled;
        }
        int oldShots = game.Shots;
        bool wasReloading = game.Reloading;
        game.Step(new BeaconInput(input.Axis("back", "forward"), input.Axis("left", "right"),
            input.Axis("turn.left", "turn.right"), input.Axis("look.down", "look.up"),
            input.WasPressed("jump"), input.Look.X, input.Look.Y, input.IsDown("fire"),
            input.WasPressed("reload"), input.WasPressed("view"), input.IsDown("sprint")), tick.Seconds);
        Presentation.Step(game, input.IsDown("aim"), input.IsDown("sprint"), tick.Seconds, oldShots, wasReloading);
    }

    public GameView View(GameViewRequest request)
    {
        FirstPersonPose3D pose = game.View == CameraView.FirstPerson ? Presentation.Pose : FirstPersonPose3D.Neutral;
        Native3DScene scene = useGi ? resident.Build(game, pose) : new(BeaconScene.Build(game, pose), []);
        if (!useGi && lighting is null)
        {
            scene = BeaconEnvironment.Present(scene, game.Time, Presentation.Impacts);
        }
        currentView = game.View == CameraView.FirstPerson
            ? new(scene, pose.Camera(game.CameraEye, game.Direction, request.Aspect), pose.Eye(game.CameraEye, game.Direction))
            : new(scene, game.Camera(request.Aspect), game.CameraEye);
        return currentView;
    }

    public void BuildHud(GameHud hud)
    {
        hud.StatusBar($"WAVE {game.Wave}/3   KILLS {game.Kills}/9   BEACONS {game.CollectedCount}/3   HEALTH {game.Health}   AMMO {game.Ammo}/12");
        hud.Crosshair();
        hud.Text("RMB AIM   SHIFT SPRINT   B MOTION   V VIEW", 20, 46, hud.Width - 40, 22);
        string objective = game.GateOpen ? "GATE OPEN - HEAD NORTH TO ESCAPE" : "SURVIVE THE STALKERS. COLLECT THE GOLD BEACONS.";
        if (game.Won || game.Dead)
        {
            objective = (game.Won ? "YOU WIN!" : "RUN ENDED") + "   ENTER TO TRY AGAIN   ESC FOR MENU";
        }
        hud.Panel(16, hud.Height - 52, hud.Width - 32, 36);
        hud.Text(objective, 32, hud.Height - 42, hud.Width - 64, 24);
    }

    public void Observe(GameFacts facts)
    {
        facts.Set("gi.mode", attachedGraphics?.Renderer.DynamicDiffuse.Mode.ToString() ?? "Off");
        facts.Set("gi.probes", attachedGraphics?.Renderer.DynamicDiffuse.Grid?.Count ?? 0);
        facts.Set("gi.environment", attachedGraphics?.Renderer.EnvironmentSource is not null);
        facts.Set("presentation.enabled", Presentation.Enabled);
        facts.Set("presentation.aim", Presentation.Pose.Aim);
        facts.Set("presentation.fov", Presentation.Pose.FieldOfView * 180 / MathF.PI);
        facts.Set("presentation.recoil", Presentation.Motion.Recoil);
        facts.Set("presentation.steps", Presentation.Motion.Footsteps);
        facts.Set("presentation.landings", Presentation.Motion.Landings);
        facts.Set("audio.shelter", Presentation.AudioState.Shelter);
        facts.Set("audio.outside", Presentation.AudioState.ExteriorGain);
        facts.Set("audio.inside", Presentation.AudioState.InteriorGain);
        foreach (string sound in new[] { "dry", "wet", "water", "shot", "reload", "land" })
        {
            facts.Set("audio." + sound, Presentation.Sounds.GetValueOrDefault(sound));
        }
        facts.Set("hash", game.SemanticHash());
        facts.Set("time", game.Time);
        facts.Set("health", game.Health);
        facts.Set("shots", game.Shots);
        facts.Set("wave", game.Wave);
        facts.Set("ammo", game.Ammo);
        facts.Set("kills", game.Kills);
        facts.Set("motion", game.Agents.Single(agent => agent.Id == "runner").State.Animation?.Motion.ToString() ?? "None");
        byte[] policy = JsonSerializer.SerializeToUtf8Bytes(game.InspectBrains(), AgentInspectionJsonContext.Default.AgentInspection);
        facts.Set("policy.hash", Convert.ToHexString(SHA256.HashData(policy)));
        facts.Set("view", game.View.ToString());
        facts.Set("won", game.Won);
        facts.Set("dead", game.Dead);
    }

    public bool Command(string command)
    {
        if (command == "presentation on" || command == "presentation off")
        {
            Presentation.Enabled = command == "presentation on";
            return true;
        }
        return command == "inspect brains";
    }

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        attachedGraphics = graphics;
        graphics.Clear = new(.055f, .095f, .15f, 1);
        if (useGi)
        {
            if (humanoid is not null || lighting is not null)
                throw new NotSupportedException("BEACON-GI-003: The GI profile uses rigid resident geometry and its own probe field.");
            if (!graphics.Plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal))
                throw new NotSupportedException("BEACON-GI-003: The GI research profile requires hardware ray queries.");
            graphics.Settings = BeaconDiffuseLighting3D.Settings;
            graphics.Renderer.EnvironmentSource = BeaconDiffuseLighting3D.Source;
            graphics.Renderer.Environment = VulkanEnvironmentCompiler.Compile(graphics.Plant,
                new GameAssets().Shader("EnvironmentCompile3D.v.ts"), BeaconDiffuseLighting3D.Source);
            graphics.Renderer.LocalLights = BeaconDiffuseLighting3D.Lights;
            graphics.Renderer.EnableVisibilityBuffer = true;
            graphics.Renderer.EnableRayShadows = true;
            graphics.DiffusePolicy = regionalGi ? BeaconDiffuseLighting3D.Policy() : null;
        }
        if (lighting is not null && !graphics.LoadLighting(lighting, BeaconLighting.Recipe().ContentKey))
        {
            throw new InvalidDataException(graphics.LightingDiagnostic);
        }
        if (humanoid is not null)
        {
            character = new HumanoidGamePresenter(graphics.Plant, humanoid);
        }
        if (graphics.Plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal))
        {
            rays = GameRayQueries.Create(graphics.Plant, game.SpatialWorld);
            game.UseRayQueries(rays);
        }
    }

    public NativeGpuGeometry3D? PrepareGpuGeometry(NativeGameGraphics graphics)
    {
        if (useGi && !diffuseReady)
        {
            graphics.Renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime;
            if (regionalGi)
            {
                var view = currentView ?? throw new InvalidOperationException("Beacon view must be projected before GI preparation.");
                graphics.Renderer.Render(view.Scene, view.Camera, view.Eye, graphics.Clear);
                graphics.SetCompiledDiffuseProbes(graphics.Renderer.CaptureCompiledDiffuseProbes());
                graphics.Renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime with
                {
                    Mode = Aurelian.Rendering.Contracts.Lighting.DynamicDiffuseMode3D.CompiledResidual,
                    RaysPerProbePerFrame = 32,
                };
                graphics.DiffusePolicy = BeaconDiffuseLighting3D.Policy();
            }
            diffuseReady = true;
        }
        return character?.Present(game.CharacterPose!, game.CharacterWorld, game.View == CameraView.ThirdPerson);
    }

    public void DetachGraphics()
    {
        attachedGraphics = null;
        diffuseReady = false;
        resident = new();
        character?.Dispose();
        character = null;
        rays?.Dispose();
        rays = null;
    }

    public void Dispose()
    {
        Presentation.Dispose();
        game.Dispose();
    }
}
