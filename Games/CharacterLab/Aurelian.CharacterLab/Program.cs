using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Aetheris.Humanoid;
using Aurelian.Games;
using Aurelian.GameHost.Silk;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Humanoid;
using Aurelian.Runtime.Dominatus.Inspection;
using Aurelian.Shaders.Compute;
using Aurelian.World.Scenes;
using Copeland.TS.Gpu;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

string bodyPath = Option("--body") ?? throw new ArgumentException("Use --body <antonia.gameplay-body.json> [--proof] [--output <directory>].");
string output = Path.GetFullPath(Option("--output") ?? "artifacts/local/character-animation");
Directory.CreateDirectory(output);
bool proof = args.Contains("--proof", StringComparer.Ordinal);
if (proof)
    File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"accepted\":false,\"stage\":\"preparing\"}");
var body = HumanoidGameplayBody.Load(bodyPath);
var policies = new AurelianAgentRuntime(512);
var definition = new HumanoidCharacterDefinition(body, policies, HumanoidAnimationSet.Basic);
using var scene = SceneCompiler.Compile(Scene.World("character-lab", [Scene.Agent("antonia", definition)])).Mount();
var character = scene.Agents.OfType<SceneAgent<HumanoidCharacterState>>().Single();
string shaderSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "HumanoidSkinning.v.ts"));
var module = GpuComputeBinder.Compile(new([new("HumanoidSkinning.v.ts", shaderSource)]));
if (!module.Success) throw new InvalidDataException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
var shader = VdMirComputeBackend.Compile(module);
if (shader.Spirv.Length == 0 || !shader.SpirvValidated)
    throw new InvalidDataException(shader.DxcOutput + shader.SpirvValidationOutput);

if (proof)
{
    var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
        new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian Character Animation"));
    if (!initialized.Success) throw new InvalidOperationException(string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
    using var plant = initialized.Plant!;
    using var target = new VulkanNativeFrameTarget(plant, 960, 720);
    using var renderer = new VulkanSolid3DRenderer(plant, new GameAssets().Shader("Solid3D.v.ts"), target);
    using var gpu = new HumanoidGpuBody(plant, shader.Spirv, body);
    Prove(plant, target, renderer, gpu);
}
else
{
    var options = WindowOptions.DefaultVulkan;
    options.Size = new Vector2D<int>(960, 720);
    options.Title = "Aurelian Character Lab — WASD walk, hold click aim, arrows orbit, V auto-demo, Esc pause";
    options.WindowBorder = WindowBorder.Fixed;
    bool smoke = args.Contains("--launch-smoke", StringComparer.Ordinal);
    options.IsVisible = !smoke;
    using var window = Window.Create(options);
    window.Initialize();
    using var input = window.CreateInput();
    using var controls = new GameControls();
    using var captured = new CapturedGameInput(window, input, controls.Adapter, manageFocus: true);
    using var graphics = new NativeGameGraphics(window, options.Title, !smoke);
    using var gpu = new HumanoidGpuBody(graphics.Plant, shader.Spirv, body);
    var clock = Stopwatch.StartNew();
    double previous = clock.Elapsed.TotalSeconds;
    double time = 0;
    bool automatic = true;
    bool paused = false;
    float orbit = 0;
    Console.WriteLine("AURELIAN_CHARACTER_READY gpu=" + graphics.Plant.Facts.PhysicalDeviceName);
    while (!window.IsClosing)
    {
        window.DoEvents();
        double now = clock.Elapsed.TotalSeconds;
        float elapsed = (float)Math.Clamp(now - previous, 0, .05);
        previous = now;
        var commands = controls.Tick(elapsed);
        if (commands.SwitchView) automatic = !automatic;
        if (commands.Pause) paused = !paused;
        orbit += commands.Turn * elapsed;
        if (!paused)
        {
            time += elapsed;
            float speed = Math.Min(1, new Vector2(commands.Strafe, commands.Forward).Length()) * .8f;
            bool aiming = commands.Fire;
            if (automatic)
            {
                speed = time % 8 is >= 1 and < 5 ? .8f : 0;
                aiming = time % 8 is >= 5 and < 7;
            }
            Advance(speed, aiming, elapsed);
        }
        gpu.Present(definition.Pose(character), character.WorldTransform);
        _ = graphics.Renderer.Render([], Camera(orbit, targetWidth: 960, targetHeight: 720),
            new(.055f, .07f, .09f, 1), gpuGeometry: gpu.Geometry, lightDirection: new(.36f, .80f, -.48f));
        graphics.Presenter.Present((ulong)gpu.DispatchCount);
        if (smoke && gpu.DispatchCount == 4)
        {
            Console.WriteLine("AURELIAN_CHARACTER_NATIVE_SMOKE_PASSED frames=4");
            break;
        }
    }
}

void Advance(float speed, bool aiming, double elapsed)
{
    definition.Observe(character, new(speed, aiming));
    policies.Tick(TimeSpan.FromSeconds(elapsed));
    definition.Advance(character, elapsed);
}

void Prove(AurelianVulkanPlant plant, VulkanNativeFrameTarget target, VulkanSolid3DRenderer renderer, HumanoidGpuBody gpu)
{
    var frames = new List<object>();
    double maximumMm = 0;
    var motions = new HashSet<CharacterMotion>();
    var captureHashes = new HashSet<string>();
    for (int frame = 0; frame < 240; frame++)
    {
        float speed = frame is >= 30 and < 150 ? .8f : 0;
        bool aiming = frame is >= 150 and < 210;
        Advance(speed, aiming, 1.0 / 60);
        motions.Add(character.State.Animation.Motion);
        var pose = definition.Pose(character);
        CheckPose(pose, character.WorldTransform);
        bool capture = frame is 0 or 70 or 105 or 165 or 200 or 235 ||
            (args.Contains("--record", StringComparer.Ordinal) && frame % 4 == 0);
        var rendered = renderer.Render([], Camera(0, target.Width, target.Height),
            new(.055f, .07f, .09f, 1), capture, gpuGeometry: gpu.Geometry, lightDirection: new(.36f, .80f, -.48f));
        if (capture)
        {
            NativeGameGraphics.WritePng(Path.Combine(output, $"frame-{frame:D3}.png"), (int)target.Width, (int)target.Height, rendered.Pixels!);
            frames.Add(new { frame, motion = character.State.Animation.Motion.ToString(), rendered.PixelSha256 });
            captureHashes.Add(rendered.PixelSha256!);
        }
    }
    var correctiveCases = new[]
    {
        new[] { new AnatomicalJointRequest(HumanoidJointKind.LeftHip, 90) },
        new[] { new AnatomicalJointRequest(HumanoidJointKind.RightHip, 90) },
        new[] { new AnatomicalJointRequest(HumanoidJointKind.LeftHip, 90), new(HumanoidJointKind.RightHip, 90),
            new(HumanoidJointKind.LeftKnee, 90), new(HumanoidJointKind.RightKnee, 90) },
        new[] { new AnatomicalJointRequest(HumanoidJointKind.LeftHip, 80, 15), new(HumanoidJointKind.RightHip, 80, 15),
            new(HumanoidJointKind.LeftKnee, 110), new(HumanoidJointKind.RightKnee, 110) },
    };
    foreach (var joints in correctiveCases)
    {
        var solved = body.Solve("corrective-verification", joints);
        if (!solved.IsSolved) throw new InvalidDataException("Corrective verification solve failed.");
        CheckPose(solved.Pose!, Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(.2f, .1f, -.4f));
    }
    var before = character.State;
    _ = definition.Pose(character);
    _ = definition.Pose(character);
    if (character.State != before) throw new InvalidOperationException("Presentation mutated simulation state.");
    if (motions.Count != 3) throw new InvalidOperationException("The Dominatus policy did not select all three motions.");
    if (captureHashes.Count < 3) throw new InvalidOperationException("Distinct poses did not produce distinct Vulkan captures.");
    var inspection = policies.Inspector.Observe();
    string json = JsonSerializer.Serialize(new
    {
        accepted = true, device = plant.Facts.PhysicalDeviceName,
        animationFrames = 240, correctiveAndPlacementCases = 4, gpuDispatches = gpu.DispatchCount, maximumParityMm = maximumMm,
        bodyId = body.Id, motions = motions.Select(item => item.ToString()), captures = frames,
        bodySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bodyPath))),
        shaderSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(shaderSource))),
        outputVertices = gpu.Geometry.VertexCount,
        presentationDoesNotAdvanceState = true, inspection,
    }, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(output, "evidence.json"), json);
    Console.WriteLine($"AURELIAN_CHARACTER_ANIMATION_PASSED frames=240 parity={maximumMm:F6}mm gpu={plant.Facts.PhysicalDeviceName}");

    void CheckPose(SolvedHumanoidPose pose, Matrix4x4 world)
    {
        var reference = body.Evaluate(pose);
        if (!reference.Evidence.IsAdmissible)
            throw new InvalidDataException($"Animation fails deformation: {reference.Evidence.FlaggedFaceIds.Count} flags.");
        gpu.Present(pose, world);
        var actual = gpu.ReadVerticesForQualification();
        foreach (var vertex in actual)
        {
            if (!float.IsFinite(vertex.Normal.LengthSquared()) || Math.Abs(vertex.Normal.LengthSquared() - 1) > .001f)
                throw new InvalidDataException("GPU skinning produced a non-unit normal.");
        }
        int corner = 0;
        foreach (var face in body.Surface.Faces)
        {
            foreach (int vertex in new[] { face.A, face.B, face.C })
            {
                var p = reference.Positions[vertex];
                var canonical = new Vector3((float)p.X, (float)p.Z, -(float)p.Y) * .001f;
                var expected = Vector3.Transform(canonical, world);
                double error = Vector3.Distance(expected, actual[corner++].Position) * 1000;
                if (!double.IsFinite(error)) throw new InvalidDataException("Nonfinite GPU pose.");
                maximumMm = Math.Max(maximumMm, error);
            }
        }
        if (maximumMm > .02) throw new InvalidDataException($"GPU skinning disagreement: {maximumMm} mm.");
    }
}

static Matrix4x4 Camera(float angle, uint targetWidth, uint targetHeight)
{
    var eye = new Vector3(3.3f * MathF.Sin(angle + .5f), 1.2f, -3.3f * MathF.Cos(angle + .5f));
    var view = Matrix4x4.CreateLookAt(eye, new(0, .85f, 0), Vector3.UnitY);
    var projection = Matrix4x4.CreatePerspectiveFieldOfView(.68f, (float)targetWidth / targetHeight, .05f, 30);
    projection.M22 = -projection.M22;
    return view * projection;
}

string? Option(string name)
{
    int index = Array.IndexOf(args, name);
    if (index < 0) return null;
    if (index + 1 >= args.Length) throw new ArgumentException(name + " requires a value.");
    return args[index + 1];
}
