using System.Globalization;
using System.Numerics;
using Aurelian.Games;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime;
using Aurelian.World.Scenes;
using InputMan.Core;

namespace Hearth;

public static class HearthInput
{
    public const string OrbitLeft = "orbit.left";
    public const string OrbitRight = "orbit.right";
    public const string ZoomIn = "zoom.in";
    public const string ZoomOut = "zoom.out";
    public const string NextShot = "shot.next";

    public static GameInputLayout Layout { get; } = new(
    [
        new(OrbitLeft, "Orbit left", GameControl.Key(KeyboardKey.A), GameControl.Key(KeyboardKey.ArrowLeft)),
        new(OrbitRight, "Orbit right", GameControl.Key(KeyboardKey.D), GameControl.Key(KeyboardKey.ArrowRight)),
        new(ZoomIn, "Move closer", GameControl.Key(KeyboardKey.W), GameControl.Key(KeyboardKey.ArrowUp)),
        new(ZoomOut, "Move away", GameControl.Key(KeyboardKey.S), GameControl.Key(KeyboardKey.ArrowDown)),
        new(NextShot, "Next fire", GameControl.Key(KeyboardKey.Space)),
    ], GameLookMode.CapturedMouse);
}

/// <summary>A camera framing one subject of the showcase.</summary>
public sealed record HearthShot(string Name, Vector3 Target, float Yaw, float Pitch, float Distance);

/// <summary>
/// Hearth: a night campsite for judging fire and smoke. It has no rules: an orbit camera, four fires, their particle
/// effects and their flickering light. Everything visual is a pure function of the fixed-step clock, so playtest
/// captures replay exactly.
/// </summary>
public sealed class HearthGame : IGame
{
    public static readonly HearthShot[] Shots =
    [
        new("wide", new(0.6f, 1.0f, -0.6f), 0.35f, 0.32f, 8.5f),
        new("campfire", new(0, 0.75f, 0), 0.2f, 0.18f, 3.4f),
        new("torch", HearthScene.TorchRight.Base + new Vector3(0, 0.2f, 0), -0.45f, 0.12f, 2.0f),
        new("brazier", HearthScene.Brazier.Base + new Vector3(0, 0.55f, 0), 1.15f, 0.2f, 2.8f),
        new("smoke", new(0, 2.6f, 0), 0.6f, -0.05f, 6.5f),
    ];

    private readonly IReadOnlyList<PlacedSceneBox> scenery = HearthScene.Build();
    private NativeGameGraphics? graphics;
    private double time;
    private int shot;
    private float yaw;
    private float pitch;
    private float distance;
    private bool daylight;
    private bool lightingChanged;

    /// <summary>Daylight variant of the look: the same scene under a high sun, for judging how smoke takes sunlight.</summary>
    public static Graphics3DSettings Daylight { get; } = Graphics3DSettings.Default with
    {
        SunDirection = Vector3.Normalize(new Vector3(0.5f, 0.75f, 0.35f)),
        SunIntensity = 3.2f,
        SkyAmbient = new(0.32f, 0.42f, 0.6f),
        GroundAmbient = new(0.14f, 0.12f, 0.1f),
        Exposure = 0.8f,
        AmbientOcclusionStrength = 0.6f,
        Fog = new HeightFog3D { Density = 0.004f, HeightFalloff = 0.1f, BaseHeight = 0, StartDistance = 10, Color = new(0.6f, 0.7f, 0.85f) },
    };

    public HearthGame()
    {
        Frame(0);
    }

    public GameManifest Manifest { get; } = new("hearth", "HEARTH", HearthInput.Layout)
    {
        Tagline = "FIRE AND SMOKE BY NIGHT",
        ControlsHint = "MOUSE / A D ORBIT   W S ZOOM   SPACE NEXT FIRE   ESC PAUSE",
        Graphics = Graphics3DSettings.Default with
        {
            SunDirection = Vector3.Normalize(new Vector3(-0.4f, 0.6f, 0.5f)),
            SunColor = new(0.55f, 0.65f, 1.0f),
            SunIntensity = 0.18f,
            SkyAmbient = new(0.025f, 0.035f, 0.07f),
            GroundAmbient = new(0.012f, 0.01f, 0.01f),
            Exposure = 1.1f,
            BloomIntensity = 0.12f,
            AmbientOcclusionStrength = 0.6f,
            Fog = new HeightFog3D { Density = 0.02f, HeightFalloff = 0.25f, BaseHeight = 0, StartDistance = 4, Color = new(0.02f, 0.025f, 0.045f) },
        },
        DefaultDisplay = new GameDisplaySettings(1280, 720),
    };

    public double Time => time;
    public HearthShot Shot => Shots[shot];

    public void NewGame()
    {
        time = 0;
        Frame(0);
    }

    public void Update(GameUpdate update)
    {
        yaw = MathF.IEEERemainder(yaw + update.Input.Look.X
            + update.Input.Axis(HearthInput.OrbitLeft, HearthInput.OrbitRight) * 1.2f * update.Seconds, MathF.Tau);
        pitch = Math.Clamp(pitch - update.Input.Look.Y, -0.3f, 1.2f);
        distance = Math.Clamp(distance - update.Input.Axis(HearthInput.ZoomOut, HearthInput.ZoomIn) * 3 * update.Seconds, 1, 16);
    }

    public void FixedUpdate(GameTick tick)
    {
        time += tick.Seconds;
        if (tick.Input.WasPressed(HearthInput.NextShot))
        {
            Frame((shot + 1) % Shots.Length);
        }
    }

    public GameView? View(GameViewRequest request)
    {
        HearthShot current = Shots[shot];
        Vector3 back = new(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        Vector3 eye = current.Target + back * distance;
        Vector3 look = Vector3.Normalize(current.Target - eye);
        if (graphics is not null)
        {
            if (lightingChanged)
            {
                graphics.Settings = daylight ? Daylight : Manifest.Graphics;
                graphics.Clear = daylight ? new(0.55f, 0.7f, 0.9f, 1) : new(0.01f, 0.012f, 0.025f, 1);
                lightingChanged = false;
            }
            graphics.ParticleEmitters = HearthEffects.Emitters(HearthScene.Fires, time);
            graphics.LocalLights = HearthEffects.Lights(HearthScene.Fires, time);
        }
        var frame = new SceneFrame([.. scenery], []);
        return new GameView(SceneGeometry3D.BuildScene(frame), Camera3D.Matrix(eye, look, request.Aspect, far: 80), eye);
    }

    public void BuildHud(GameHud hud)
    {
        hud.Panel(16, 16, 300, 40);
        hud.Text($"{Shots[shot].Name.ToUpperInvariant()}    SPACE FOR NEXT", 32, 27, 280, 20);
    }

    public void Observe(GameFacts facts)
    {
        facts.Set("shot", Shots[shot].Name);
        facts.Set("time", time);
        facts.Set("daylight", daylight);
    }

    /// <summary>
    /// Playtest commands: "shot NAME" frames a subject; "orbit DEGREES" turns the camera about it; "daylight on|off"
    /// swaps the night look for a high sun.
    /// </summary>
    public bool Command(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2 && words[0] == "shot")
        {
            int index = Array.FindIndex(Shots, item => item.Name == words[1]);
            if (index < 0)
            {
                return false;
            }
            Frame(index);
            return true;
        }
        if (words.Length == 2 && words[0] == "daylight" && words[1] is "on" or "off")
        {
            daylight = words[1] == "on";
            lightingChanged = true;
            return true;
        }
        if (words.Length == 2 && words[0] == "orbit"
            && float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float degrees))
        {
            yaw = MathF.IEEERemainder(yaw + degrees * MathF.PI / 180, MathF.Tau);
            return true;
        }
        return false;
    }

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        this.graphics = graphics;
        graphics.Clear = new(0.01f, 0.012f, 0.025f, 1);
    }

    public void DetachGraphics()
    {
        graphics = null;
    }

    public void Dispose()
    {
    }

    private void Frame(int index)
    {
        shot = index;
        yaw = Shots[index].Yaw;
        pitch = Shots[index].Pitch;
        distance = Shots[index].Distance;
    }
}
