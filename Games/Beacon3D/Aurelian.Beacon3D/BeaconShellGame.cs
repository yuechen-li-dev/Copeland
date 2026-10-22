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
public sealed class BeaconShellGame(HumanoidPlayerOptions? humanoid = null, string? lighting = null) : IGame
{
    private BeaconGame game = new(humanoid: humanoid);
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
        new("fire", "Fire", GameControl.Mouse(MouseButton.Primary)),
        new("reload", "Reload", GameControl.Key(KeyboardKey.R)),
        new("view", "Switch view", GameControl.Key(KeyboardKey.V)),
        new("sprint", "Sprint", GameControl.Key(KeyboardKey.LeftShift)),
        new("restart", "Restart run", GameControl.Key(KeyboardKey.Enter)),
    ], GameLookMode.CapturedMouse))
    {
        Tagline = "SURVIVE THREE WAVES. COLLECT THREE BEACONS. ESCAPE.",
        ControlsHint = "WASD MOVE   MOUSE AIM   CLICK FIRE   R RELOAD   V VIEW   ESC PAUSE",
        // Published transfer assets are compiled for Lambert; ordinary play uses PathDiffuse.
        Graphics = Graphics3DSettings.Default with
        {
            DirectDiffuseModel = lighting is null ? DirectDiffuseModel3D.PathDiffuse : DirectDiffuseModel3D.Lambert,
        },
    };

    public BeaconGame World => game;

    public void NewGame()
    {
        game.Dispose();
        game = new BeaconGame(humanoid: humanoid);
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
        game.Step(new BeaconInput(input.Axis("back", "forward"), input.Axis("left", "right"),
            input.Axis("turn.left", "turn.right"), input.Axis("look.down", "look.up"),
            input.WasPressed("jump"), input.Look.X, input.Look.Y, input.IsDown("fire"),
            input.WasPressed("reload"), input.WasPressed("view"), input.IsDown("sprint")), tick.Seconds);
    }

    public GameView View(GameViewRequest request) => new(new Native3DScene(BeaconScene.Build(game), []),
        game.Camera(request.Aspect), game.CameraEye);

    public void BuildHud(GameHud hud)
    {
        hud.StatusBar($"WAVE {game.Wave}/3   KILLS {game.Kills}/9   BEACONS {game.CollectedCount}/3   HEALTH {game.Health}   AMMO {game.Ammo}/12");
        hud.Crosshair();
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

    public bool Command(string command) => command == "inspect brains";

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        graphics.Clear = new(.055f, .095f, .15f, 1);
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

    public NativeGpuGeometry3D? PrepareGpuGeometry(NativeGameGraphics graphics) =>
        character?.Present(game.CharacterPose!, game.CharacterWorld, game.View == CameraView.ThirdPerson);

    public void DetachGraphics()
    {
        character?.Dispose();
        character = null;
        rays?.Dispose();
        rays = null;
    }

    public void Dispose() => game.Dispose();
}
