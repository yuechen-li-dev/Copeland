using System.Numerics;
using Aetheris.Humanoid;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Humanoid;
using Aurelian.Runtime.Dominatus.Inspection;
using Aurelian.World.Scenes;
using InputMan.Core;

namespace Aurelian.CharacterLab;

/// <summary>Animation laboratory using the same menus, input and window lifecycle as the games.</summary>
public sealed class CharacterLabGame : IGame
{
    private readonly HumanoidGameplayBody body;
    private AurelianAgentRuntime policies = null!;
    private HumanoidCharacterDefinition definition = null!;
    private SceneInstance scene = null!;
    private SceneAgent<HumanoidCharacterState> character = null!;
    private HumanoidGpuBody? gpu;
    private double time;
    private bool automatic = true;
    private float orbit;

    public CharacterLabGame(string bodyPath)
    {
        body = HumanoidGameplayBody.Load(bodyPath);
        NewGame();
    }

    public GameManifest Manifest { get; } = new("character-lab", "CHARACTER LAB", new GameInputLayout(
    [
        new("forward", "Walk forward", GameControl.Key(KeyboardKey.W)),
        new("back", "Walk back", GameControl.Key(KeyboardKey.S)),
        new("left", "Walk left", GameControl.Key(KeyboardKey.A)),
        new("right", "Walk right", GameControl.Key(KeyboardKey.D)),
        new("orbit.left", "Orbit left", GameControl.Key(KeyboardKey.ArrowLeft)),
        new("orbit.right", "Orbit right", GameControl.Key(KeyboardKey.ArrowRight)),
        new("aim", "Aim", GameControl.Mouse(MouseButton.Primary)),
        new("automatic", "Automatic demonstration", GameControl.Key(KeyboardKey.V)),
    ]))
    {
        Tagline = "WALK, AIM AND INSPECT THE CHARACTER",
        ControlsHint = "WASD WALK   HOLD CLICK AIM   ARROWS ORBIT   V AUTO   ESC PAUSE",
        DefaultDisplay = new(960, 720),
        Graphics = Aurelian.Rendering.Contracts.Models.Graphics3DSettings.Default with
        {
            SunDirection = Vector3.Normalize(new Vector3(.36f, .80f, -.48f)),
        },
    };

    public void NewGame()
    {
        scene?.Dispose();
        policies = new AurelianAgentRuntime(512);
        definition = new HumanoidCharacterDefinition(body, policies, HumanoidAnimationSet.Basic);
        scene = SceneCompiler.Compile(Scene.World("character-lab", [Scene.Agent("antonia", definition)])).Mount();
        character = scene.Agents.OfType<SceneAgent<HumanoidCharacterState>>().Single();
        time = 0;
        orbit = 0;
        automatic = true;
    }

    public void FixedUpdate(GameTick tick)
    {
        if (tick.Input.WasPressed("automatic"))
        {
            automatic = !automatic;
        }
        time += tick.Seconds;
        orbit += tick.Input.Axis("orbit.left", "orbit.right") * tick.Seconds;
        float speed = Math.Min(1, tick.Input.Direction("left", "right", "back", "forward").Length()) * .8f;
        bool aiming = tick.Input.IsDown("aim");
        if (automatic)
        {
            speed = time % 8 is >= 1 and < 5 ? .8f : 0;
            aiming = time % 8 is >= 5 and < 7;
        }
        definition.Observe(character, new(speed, aiming));
        policies.Tick(TimeSpan.FromSeconds(tick.Seconds));
        definition.Advance(character, tick.Seconds);
    }

    public GameView View(GameViewRequest request)
    {
        Vector3 eye = new(3.3f * MathF.Sin(orbit + .5f), 1.2f, -3.3f * MathF.Cos(orbit + .5f));
        var view = Matrix4x4.CreateLookAt(eye, new(0, .85f, 0), Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.68f, request.Aspect, .05f, 30);
        projection.M22 = -projection.M22;
        return new(new Native3DScene([], []), view * projection, eye);
    }

    public void BuildHud(GameHud hud) => hud.StatusBar($"{character.State.Animation.Motion}   AUTO {(automatic ? "ON" : "OFF")}   V TO TOGGLE");

    public void Observe(GameFacts facts)
    {
        facts.Set("time", time);
        facts.Set("automatic", automatic);
        facts.Set("motion", character.State.Animation.Motion.ToString());
    }

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        gpu = new HumanoidGpuBody(graphics.Plant, new GameAssets().ComputeShader("HumanoidSkinning.v.ts"), body);
        graphics.Clear = new(.055f, .07f, .09f, 1);
    }

    public NativeGpuGeometry3D? PrepareGpuGeometry(NativeGameGraphics graphics)
    {
        gpu!.Present(definition.Pose(character), character.WorldTransform);
        return gpu.Geometry;
    }

    public void DetachGraphics()
    {
        gpu?.Dispose();
        gpu = null;
    }

    public void Dispose() => scene.Dispose();
}
