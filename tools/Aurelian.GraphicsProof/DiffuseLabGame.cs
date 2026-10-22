using System.Numerics;
using Aurelian.Assets.Materials;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime;
using Aurelian.World.Scenes;
using InputMan.Core;

/// <summary>A controlled material comparison using the production renderer and ordinary game shell.</summary>
internal sealed class DiffuseLabGame : IGame
{
    private readonly Native3DScene scene = CreateScene();
    private NativeGameGraphics? graphics;
    private DirectDiffuseModel3D model;
    private int shot;
    private int lighting;

    public GameManifest Manifest { get; } = new("diffuse-lab", "AURELIAN | DIFFUSE LAB", new GameInputLayout(
    [
        new("lambert", "Lambert", GameControl.Key(KeyboardKey.Number1)),
        new("burley", "Disney Burley", GameControl.Key(KeyboardKey.Number2)),
        new("frostbite", "Frostbite diffuse", GameControl.Key(KeyboardKey.Number3)),
        new("eon", "EON diffuse", GameControl.Key(KeyboardKey.Number4)),
        new("path", "Aurelian path diffuse", GameControl.Key(KeyboardKey.Number5)),
        new("camera", "Next camera", GameControl.Key(KeyboardKey.C)),
        new("lighting", "Next lighting", GameControl.Key(KeyboardKey.L)),
    ]))
    {
        Tagline = "SAME MATERIALS. SAME LIGHT. FIVE DIFFUSE MODELS.",
        ControlsHint = "1 LAMBERT   2 BURLEY   3 FROSTBITE   4 EON   5 PATH   C CAMERA   L LIGHTING   ESC PAUSE",
        Graphics = Settings(0),
        DefaultDisplay = new(1440, 900),
    };

    public static Graphics3DSettings Settings(int lighting) => Graphics3DSettings.Default with
    {
        AntiAliasing = AntiAliasing3D.None,
        BloomIntensity = 0,
        AmbientOcclusionStrength = 0,
        SunDirection = Vector3.Normalize(new Vector3(-.6f, .65f, .5f)),
        SunColor = Vector3.One,
        SunIntensity = lighting == 2 ? 0 : 3.2f,
        SkyAmbient = lighting == 1 ? new(.22f, .26f, .32f) : Vector3.Zero,
        GroundAmbient = lighting == 1 ? new(.07f, .06f, .05f) : Vector3.Zero,
        Exposure = 1,
        Shadows = true,
    };

    public static (Vector3 Eye, Vector3 Target) Camera(int shot)
    {
        return shot switch
        {
            1 => (new(-9, 2.5f, 9), new(0, 1, 0)),
            2 => (new(-8, 8, 7), new(0, .8f, 0)),
            _ => (new(7.5f, 6, 12), new(.5f, .8f, 0)),
        };
    }

    public void NewGame()
    {
        model = DirectDiffuseModel3D.Lambert;
        shot = 0;
        lighting = 0;
    }

    public void FixedUpdate(GameTick tick)
    {
        string[] actions = ["lambert", "burley", "frostbite", "eon", "path"];
        for (int index = 0; index < actions.Length; index++)
        {
            if (tick.Input.WasPressed(actions[index]))
            {
                model = (DirectDiffuseModel3D)index;
            }
        }
        if (tick.Input.WasPressed("camera"))
        {
            shot = (shot + 1) % 3;
        }
        if (tick.Input.WasPressed("lighting"))
        {
            lighting = (lighting + 1) % 3;
        }
    }

    public GameView View(GameViewRequest request)
    {
        if (graphics is not null)
        {
            var authored = Settings(lighting);
            graphics.Settings = graphics.Settings with
            {
                DirectDiffuseModel = model,
                SunIntensity = authored.SunIntensity,
                SkyAmbient = authored.SkyAmbient,
                GroundAmbient = authored.GroundAmbient,
            };
            graphics.LocalLights = lighting == 2 ? Lights : [];
        }
        (Vector3 eye, Vector3 target) = Camera(shot);
        return new(scene, CameraMatrix(eye, target, request.Aspect), eye);
    }

    internal static Matrix4x4 CameraMatrix(Vector3 eye, Vector3 target, float aspect)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(42 * MathF.PI / 180, aspect, .1f, 80);
        projection.M22 = -projection.M22;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
    }

    internal static IReadOnlyList<LocalLight3D> Lights { get; } =
    [
        new(new(-3, 4, 4), new(1, .82f, .65f), 85, 18),
        new(new(4, 3, -2), new(.45f, .65f, 1), 45, 15),
    ];

    public void BuildHud(GameHud hud)
    {
        hud.StatusBar($"{model.ToString().ToUpperInvariant()}   |   {LightingName}   |   CAMERA {shot + 1}");
        hud.Panel(16, hud.Height - 78, hud.Width - 32, 62);
        hud.Text("1 LAMBERT   2 BURLEY   3 FROSTBITE   4 EON   5 PATH   C CAMERA   L LIGHTING", 32, hud.Height - 67, hud.Width - 64, 24);
        hud.Text("LEFT TO RIGHT: ROUGHNESS .25 / .65 / 1     ROWS: CHALK / CLAY / BLUE     RIGHT: METAL CONTROL",
            32, hud.Height - 42, hud.Width - 64, 24);
    }

    private string LightingName => lighting switch
    {
        1 => "SUN + FIXED AMBIENT",
        2 => "LOCAL LIGHTS",
        _ => "DIRECT SUN",
    };

    public bool Command(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2 && words[0] == "model" && Enum.TryParse(words[1], true, out DirectDiffuseModel3D selected)
            && Enum.IsDefined(selected))
        {
            model = selected;
            return true;
        }
        if (words.Length == 2 && words[0] == "shot" && int.TryParse(words[1], out int selectedShot) && selectedShot is >= 0 and < 3)
        {
            shot = selectedShot;
            return true;
        }
        if (words.Length == 2 && words[0] == "lighting" && int.TryParse(words[1], out int selectedLighting) && selectedLighting is >= 0 and < 3)
        {
            lighting = selectedLighting;
            return true;
        }
        return false;
    }

    public void Observe(GameFacts facts)
    {
        facts.Set("model", model.ToString());
        facts.Set("shot", shot);
        facts.Set("lighting", lighting);
    }

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        this.graphics = graphics;
        graphics.Clear = new(.018f, .022f, .03f, 1);
    }

    public void DetachGraphics() => graphics = null;
    public void Dispose()
    {
    }

    internal static Native3DScene CreateScene()
    {
        var nodes = new List<SceneNode>();
        float[] roughness = [.25f, .65f, 1];
        Vector4[] colors = [new(.65f, .62f, .56f, 1), new(.42f, .075f, .025f, 1), new(.025f, .12f, .36f, 1)];
        var plinth = new ModelMaterial("plinth") { Metallic = 0, Roughness = .85f, BaseColor = new(.08f, .09f, .11f, 1) };
        var stone = ProceduralSurfaceMaps.Pavers("diffuse-lab-stone", resolution: 128).Material("stone");
        nodes.Add(Scene.Model("stage", PrimitiveModels3D.BeveledBox("stage", new(12, .3f, 9), .1f, stone), new(0, -.2f, 0)));
        for (int row = 0; row < colors.Length; row++)
        {
            for (int column = 0; column < roughness.Length; column++)
            {
                string id = $"sample-{row}-{column}";
                float x = (column - 1) * 2.4f;
                float z = 2.5f - row * 2.5f;
                var material = new ModelMaterial(id) { Metallic = 0, Roughness = roughness[column], BaseColor = colors[row] };
                nodes.Add(Scene.Model(id, PrimitiveModels3D.Sphere(id, .76f, material, 64, 32), new(x, 1.1f, z)));
                nodes.Add(Scene.Model(id + "-base", PrimitiveModels3D.Cylinder(id + "-base", .9f, .3f, plinth, 48), new(x, .15f, z)));
            }
        }
        var metal = new ModelMaterial("metal-control") { Metallic = 1, Roughness = .42f, BaseColor = new(.8f, .56f, .2f, 1) };
        nodes.Add(Scene.Model("metal", PrimitiveModels3D.Sphere("metal", .8f, metal, 64, 32), new(4.7f, 1.1f, 0)));
        nodes.Add(Scene.Model("metal-base", PrimitiveModels3D.Cylinder("metal-base", .94f, .3f, plinth, 48), new(4.7f, .15f, 0)));
        using var mounted = SceneCompiler.Compile(Scene.World("diffuse-lab", nodes)).Mount();
        return SceneGeometry3D.BuildScene(mounted.Project());
    }
}
