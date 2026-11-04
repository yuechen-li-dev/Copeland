using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHybridProof
{
    private const int Width = 640;
    private const int Height = 360;
    private static readonly NativeFrameClearColor Clear = new(.1f, .15f, .22f, 1);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Hybrid analytic visibility"));
        Require(initialization.Success, "Vulkan initialization failed.");
        using var plant = initialization.Plant!;
        using var validation = new RenderingR0Validation(plant);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, new GameAssets(), target, enableCulling: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        var instances = new List<SceneInstance3D>();
        foreach (var kind in new[] { AnalyticPrimitiveKind3D.UnitSphere, AnalyticPrimitiveKind3D.UnitBox, AnalyticPrimitiveKind3D.Capsule })
        {
            var material = new ModelMaterial(kind.ToString()) { BaseColor = new(.65f, .45f, .2f, 1), Metallic = 0, DoubleSided = true };
            instances.Add(new(kind.ToString(), NativeAnalyticPrimitives3D.Create(kind.ToString(), kind), material,
                Matrix4x4.CreateTranslation(((int)kind - 2) * 2.6f, 0, 0)));
        }
        Native3DScene scene = new([], []) { Instances = new NativeInstanceSet3D(instances) };
        Vector3 eye = new(0, 2, 8);
        var camera = RenderingBindlessProof.Camera(eye, Vector3.Zero);
        var frame = renderer.Render(scene, camera, eye, Clear, capture: true);
        NativeGameGraphics.WritePng(Path.Combine(output, "hybrid.png"), Width, Height, frame.Pixels!);
        var ids = renderer.ReadIdentitiesForQualification();
        Require(ids.Ids.Where((value, index) => index % 2 == 1).Any(value => (value & 0x80000000) != 0), "No analytic hits.");
        object boundaries = CheckBoundaries(renderer, scene, output);
        object performance = CheckPerformance(renderer, output);
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, plant.Facts, frame.PixelSha256, Boundaries = boundaries, Performance = performance,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_HYBRID_PASSED " + output);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
