using System.Globalization;
using System.Numerics;
using Aurelian.Audio;
using Aurelian.Games;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime;
using Aurelian.Spatial3D;
using Aurelian.World.Scenes;
using InputMan.Core;

namespace Cloudstep;

/// <summary>Input actions. Arrow keys are fixed alternatives for movement; everything else is rebindable.</summary>
public static class CloudstepInput
{
    public const string Forward = "move.forward";
    public const string Back = "move.back";
    public const string Left = "move.left";
    public const string Right = "move.right";
    public const string Jump = "jump";
    public const string CameraLeft = "camera.left";
    public const string CameraRight = "camera.right";

    public static GameInputLayout Layout { get; } = new(
    [
        new(Forward, "Move forward", GameControl.Key(KeyboardKey.W), GameControl.Key(KeyboardKey.ArrowUp)),
        new(Back, "Move back", GameControl.Key(KeyboardKey.S), GameControl.Key(KeyboardKey.ArrowDown)),
        new(Left, "Move left", GameControl.Key(KeyboardKey.A), GameControl.Key(KeyboardKey.ArrowLeft)),
        new(Right, "Move right", GameControl.Key(KeyboardKey.D), GameControl.Key(KeyboardKey.ArrowRight)),
        new(Jump, "Jump", GameControl.Key(KeyboardKey.Space)),
        new(CameraLeft, "Turn camera left", GameControl.Key(KeyboardKey.Q)),
        new(CameraRight, "Turn camera right", GameControl.Key(KeyboardKey.E)),
    ], GameLookMode.CapturedMouse);
}

/// <summary>
/// Cloudstep: a short 3D platforming course. This class adapts <see cref="CloudstepWorld"/> rules to the Aurelian shell:
/// input, camera, presentation, HUD, audio and saves. The shell supplies window, menus, settings and persistence.
/// </summary>
public sealed class CloudstepGame : IGame
{
    private const string TitleTrack = "cloudstep.music.title";
    private const string CourseTrack = "cloudstep.music.course";
    private static readonly string[] Sounds = ["jump", "land", "gem", "checkpoint", "fall", "goal", "crumble"];
    private readonly Dictionary<string, Task<AudioClipResource>> music = new(StringComparer.Ordinal);
    private string? playingTrack;
    private bool onCourse;
    private int musicSwitches;
    private CloudstepAutopilot? autopilot;
    private readonly CloudstepWorld world;
    private readonly GameSaveSchema saves;
    private readonly List<PlacedSceneBox> staticBoxes = [];
    private readonly CameraBoom3D boom = new(radius: 0.25f, margin: 0.1f, extendRate: 5);
    private readonly string assetDirectory;
    private GameContext? context;
    private Vector3 cameraPivot;
    private bool pivotPlaced;
    private float squash;
    private float squashVelocity;
    private string? toast;
    private float toastTimer;
    private double presentationTime;
    private float bestTime = float.NaN;

    public CloudstepGame(string? assetDirectory = null, Graphics3DSettings? researchLook = null)
    {
        if (researchLook is not null)
        {
            researchLook.Validate();
            Manifest = Manifest with { Graphics = researchLook };
        }
        world = new CloudstepWorld();
        this.assetDirectory = assetDirectory ?? Path.Combine(AppContext.BaseDirectory, "Assets");
        foreach (CourseBlock block in world.Course.Blocks)
        {
            CloudstepVisuals.AddBlock(staticBoxes, block);
        }
        saves = GameSaveSchema.Create(CloudstepJsonContext.Default.CloudstepSnapshot, world.Course.Identity + "|" + Manifest.Id,
            world.Capture, world.Validate, Restore);
    }

    public GameManifest Manifest { get; } = new("cloudstep", "CLOUDSTEP", CloudstepInput.Layout)
    {
        Tagline = "HOP THE SKY ISLANDS. GRAB THE GEMS. REACH THE BEACON.",
        ControlsHint = "WASD MOVE   SPACE JUMP   MOUSE / Q E CAMERA   ESC PAUSE",
        Graphics = Graphics3DSettings.Default with
        {
            SkyAmbient = new(0.30f, 0.40f, 0.58f),
            GroundAmbient = new(0.12f, 0.10f, 0.08f),
            SunIntensity = 3.6f,
            Exposure = 0.85f,
            SunDirection = Vector3.Normalize(new Vector3(0.45f, 0.8f, 0.35f)),
            Fog = new HeightFog3D { Density = 0.0015f, HeightFalloff = 0.08f, BaseHeight = -14, StartDistance = 35, Color = new(0.62f, 0.76f, 0.95f) },
            AmbientOcclusionStrength = 0.6f,
        },
        DefaultDisplay = new GameDisplaySettings(1280, 720),
    };

    public CloudstepWorld World => world;
    public GameSaveSchema Saves => saves;

    public void Start(GameContext context)
    {
        this.context = context;
        float[] tones = [520, 180, 1320, 660, 110, 880, 240];
        float[] lengths = [0.08f, 0.05f, 0.12f, 0.3f, 0.45f, 0.7f, 0.2f];
        for (int index = 0; index < Sounds.Length; index++)
        {
            string path = Path.Combine(assetDirectory, "Audio", Sounds[index] + ".wav");
            if (File.Exists(path))
            {
                context.AudioResources.LoadPcmWav(new AudioAssetId("cloudstep." + Sounds[index]), path);
            }
            else
            {
                context.AddTone("cloudstep." + Sounds[index], tones[index], lengths[index]);
            }
        }
        // Music decodes on worker threads; OnFrame installs each track on the game thread when it is ready.
        foreach ((string key, string file) in new[] { (TitleTrack, "insert-quarter.mp3"), (CourseTrack, "sunlight-on-the-cartridge.mp3") })
        {
            string path = Path.Combine(assetDirectory, "Music", file);
            if (File.Exists(path))
            {
                music[key] = Task.Run(() => Mp3Decoder.Decode(new AudioAssetId(key), File.ReadAllBytes(path), loopByDefault: true));
            }
        }
    }

    public void OnFrame(GameFrame frame)
    {
        if (frame.Screen == "title")
        {
            onCourse = false;
        }
        string desired = onCourse ? CourseTrack : TitleTrack;
        if (context is null || desired == playingTrack || !music.TryGetValue(desired, out Task<AudioClipResource>? decode)
            || !decode.IsCompleted)
        {
            return;
        }
        if (decode.IsFaulted)
        {
            Console.Error.WriteLine($"Cloudstep music '{desired}' could not be decoded: {decode.Exception?.GetBaseException().Message}");
            music.Remove(desired);
            return;
        }
        if (!context.AudioResources.TryGet(new AudioAssetId(desired), out _))
        {
            context.AudioResources.Add(decode.Result);
        }
        var id = new AudioEventId($"{desired}#{++musicSwitches}");
        if (playingTrack is null)
        {
            context.Audio.SetMusic(id, new AudioAssetId(desired), TimeSpan.FromSeconds(1));
        }
        else
        {
            context.Audio.CrossfadeMusic(id, new AudioAssetId(desired), TimeSpan.FromSeconds(1.5));
        }
        playingTrack = desired;
    }

    public void NewGame()
    {
        onCourse = true;
        world.Reset();
        pivotPlaced = false;
        boom.Reset();
        squash = 0;
        squashVelocity = 0;
        toast = null;
    }

    public void Update(GameUpdate update)
    {
        float turn = update.Input.Axis(CloudstepInput.CameraLeft, CloudstepInput.CameraRight) * 2.4f * update.Seconds;
        world.CameraYaw = MathF.IEEERemainder(world.CameraYaw + update.Input.Look.X + turn, MathF.Tau);
        world.CameraPitch = Math.Clamp(world.CameraPitch - update.Input.Look.Y, -0.15f, 1.2f);
    }

    public void FixedUpdate(GameTick tick)
    {
        Vector2 move = new(tick.Input.Axis(CloudstepInput.Left, CloudstepInput.Right),
            tick.Input.Axis(CloudstepInput.Back, CloudstepInput.Forward));
        bool pressed = tick.Input.WasPressed(CloudstepInput.Jump);
        bool held = tick.Input.IsDown(CloudstepInput.Jump);
        if (autopilot is not null && !autopilot.Done)
        {
            (move, pressed, held) = autopilot.Drive(world);
        }
        IReadOnlyList<CourseEvent> events = world.Tick(move, pressed, held, tick.Seconds);
        foreach (CourseEvent item in events)
        {
            React(item);
        }
        // Presentation springs advance with the simulation so they freeze while paused.
        squashVelocity += (-squash * 180 - squashVelocity * 14) * tick.Seconds;
        squash += squashVelocity * tick.Seconds;
        toastTimer = MathF.Max(0, toastTimer - tick.Seconds);
        presentationTime += tick.Seconds;
    }

    public GameView? View(GameViewRequest request)
    {
        CloudstepPlayer player = world.Player;
        Vector3 target = player.Feet + new Vector3(0, 1.25f, 0);
        if (!pivotPlaced || world.Dead)
        {
            cameraPivot = target;
            pivotPlaced = !world.Dead;
        }
        else
        {
            cameraPivot = Vector3.Lerp(cameraPivot, target, 0.25f);
        }
        float yaw = world.CameraYaw;
        float pitch = world.CameraPitch;
        Vector3 back = Vector3.Normalize(new Vector3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)));
        Vector3 eye = boom.Update(world.Physics, cameraPivot, back, 7.5f, 1f / 60);
        Vector3 look = Vector3.Normalize(cameraPivot - eye + new Vector3(0, 0.0001f, 0));
        return new GameView(SceneGeometry3D.BuildScene(BuildFrame()), Camera3D.Matrix(eye, look, request.Aspect, far: 260), eye);
    }

    public void BuildHud(GameHud hud)
    {
        double time = world.Finished ? world.FinishTime : world.Time;
        hud.Panel(16, 16, 420, 40);
        hud.Text($"GEMS {world.GemsCollected}/{world.Course.Gems.Length}    TIME {Clock(time)}    FALLS {world.Deaths}", 32, 27, 400, 20);
        if (toast is not null && toastTimer > 0)
        {
            hud.Panel(hud.Width / 2.0 - 160, 90, 320, 44, 0x071520C8);
            hud.Text(toast, hud.Width / 2.0 - 140, 100, 280, 26, 0xFFE7A8FF, GameHudTextSize.Medium);
        }
        if (world.Dead)
        {
            hud.Panel(0, 0, hud.Width, hud.Height, 0x0A1424A0);
        }
        if (world.Finished)
        {
            hud.Panel(hud.Width / 2.0 - 260, hud.Height / 2.0 - 90, 520, 180, 0x071520E8);
            hud.Text("COURSE CLEAR", hud.Width / 2.0 - 230, hud.Height / 2.0 - 70, 460, 50, 0xFFE7A8FF, GameHudTextSize.Large);
            hud.Text($"TIME {Clock(world.FinishTime)}    GEMS {world.GemsCollected}/{world.Course.Gems.Length}    FALLS {world.Deaths}",
                hud.Width / 2.0 - 230, hud.Height / 2.0 - 4, 460, 24, 0xFFF1D4FF, GameHudTextSize.Medium);
            string best = float.IsNaN(bestTime) ? "" : $"BEST {Clock(bestTime)}    ";
            hud.Text(best + "ESC > QUIT TO TITLE > NEW GAME TO RUN AGAIN", hud.Width / 2.0 - 230, hud.Height / 2.0 + 36, 460, 20);
        }
    }

    public void Observe(GameFacts facts)
    {
        CloudstepPlayer player = world.Player;
        facts.Set("gems", world.GemsCollected);
        facts.Set("falls", world.Deaths);
        facts.Set("checkpoint", world.Checkpoint);
        facts.Set("finished", world.Finished);
        facts.Set("grounded", player.Grounded);
        facts.Set("support", player.Support ?? "none");
        facts.Set("position", player.Feet);
        facts.Set("course.time", world.Time);
        facts.Set("dead", world.Dead);
        facts.Set("autopilot", autopilot is null ? "off" : autopilot.Leg);
        facts.Set("look.diffuse", Manifest.Graphics.DirectDiffuseModel.ToString());
        facts.Set("look.retention", Manifest.Graphics.ToneMapColorRetention);
    }

    /// <summary>Playtest commands: "autopilot on|off" tours the course; "checkpoint N" moves the hero to a checkpoint.</summary>
    public bool Command(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2 && words[0] == "autopilot" && words[1] is "on" or "off")
        {
            autopilot = words[1] == "on" ? new CloudstepAutopilot() : null;
            return true;
        }
        if (words.Length == 2 && words[0] == "checkpoint" && int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && index >= 0 && index < world.Course.Checkpoints.Length)
        {
            CourseCheckpoint point = world.Course.Checkpoints[index];
            world.Player.Place(point.Position, point.Yaw);
            world.CameraYaw = point.Yaw;
            pivotPlaced = false;
            boom.Reset();
            return true;
        }
        return false;
    }

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        graphics.Clear = new(0.56f, 0.74f, 0.93f, 1);
    }

    public void Dispose()
    {
    }

    private void Restore(CloudstepSnapshot snapshot)
    {
        world.Restore(snapshot);
        onCourse = true;
        pivotPlaced = false;
        boom.Reset();
    }

    private void React(CourseEvent item)
    {
        switch (item)
        {
            case CourseEvent.Jump:
                Play("jump");
                squash = -0.18f;
                squashVelocity = 0;
                break;
            case CourseEvent.Land:
                Play("land", 0.6f);
                squash = 0.22f;
                squashVelocity = 0;
                break;
            case CourseEvent.Gem:
                Play("gem");
                break;
            case CourseEvent.Checkpoint:
                Play("checkpoint");
                Toast("CHECKPOINT");
                break;
            case CourseEvent.Fall:
            case CourseEvent.Hazard:
            case CourseEvent.Crushed:
                Play("fall");
                Toast(item == CourseEvent.Crushed ? "SQUASHED!" : item == CourseEvent.Hazard ? "OUCH, THORNS" : "WHOOPS");
                break;
            case CourseEvent.Crumble:
                Play("crumble", 0.7f);
                break;
            case CourseEvent.Goal:
                Play("goal");
                float time = (float)world.FinishTime;
                bestTime = float.IsNaN(bestTime) ? time : MathF.Min(bestTime, time);
                break;
        }
    }

    private void Toast(string text)
    {
        toast = text;
        toastTimer = 1.6f;
    }

    private void Play(string sound, float volume = 1)
    {
        context?.PlaySound("cloudstep." + sound, volume);
    }

    private SceneFrame BuildFrame()
    {
        float time = (float)world.Time;
        var boxes = new List<PlacedSceneBox>(staticBoxes);
        var meshes = new List<PlacedSceneMesh>();
        foreach (CoursePlatform platform in world.Course.Platforms)
        {
            Matrix4x4 pose = world.Physics.Transform(platform.Id);
            CrumblePhase phase = world.CrumbleState(platform.Id);
            Vector4 top = platform.Motion == PlatformMotion.Crumble
                ? (phase == CrumblePhase.Solid ? CloudstepVisuals.Rgb(0xE3C78F) : CloudstepVisuals.Rgb(0xC9935A))
                : CloudstepVisuals.WoodTop;
            Vector4 body = platform.Motion == PlatformMotion.Crumble ? CloudstepVisuals.Rgb(0xA98A5B) : CloudstepVisuals.Wood;
            CloudstepVisuals.AddCapped(boxes, platform.Id, pose, platform.HalfSize, top, body, 0.12f);
        }
        for (int index = 0; index < world.Course.Checkpoints.Length; index++)
        {
            if (index == 0)
            {
                continue;
            }
            CloudstepVisuals.AddFlag(boxes, $"checkpoint{index}", world.Course.Checkpoints[index].Position + new Vector3(-1.4f, 0, 0),
                world.Checkpoint >= index, time);
        }
        CloudstepVisuals.AddBeacon(boxes, world.Course.Goal, time, world.Finished);
        for (int index = 0; index < world.Course.Gems.Length; index++)
        {
            if (!world.Collected(index))
            {
                CloudstepVisuals.AddGem(meshes, index, world.Course.Gems[index], time);
            }
        }
        foreach (CourseHazard hazard in world.Course.Hazards)
        {
            CloudstepVisuals.AddThorns(boxes, meshes, hazard);
        }
        float alpha = world.Dead ? 0 : 1;
        CloudstepVisuals.AddHero(boxes, meshes, world.Player.Feet, world.Player.Facing, Math.Clamp(squash, -0.3f, 0.35f), alpha);
        return new SceneFrame([.. boxes], [.. meshes]);
    }

    private static string Clock(double seconds)
    {
        int minutes = (int)(seconds / 60);
        return minutes.ToString(CultureInfo.InvariantCulture) + ":" + (seconds - minutes * 60).ToString("00.0", CultureInfo.InvariantCulture);
    }
}
