using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime.Rendering;

internal static class RenderingPolicyProof
{
    private static readonly NativeFrameClearColor Clear = new(.1f, .15f, .22f, 1);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var mesh = RenderingLodProof.CompileAsset(output, out object asset);
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Dominatus rendering policy"));
        Require(initialization.Success, "Vulkan initialization failed.");
        using var plant = initialization.Plant!;
        using var validation = new RenderingR0Validation(plant);
        using var target = new VulkanNativeFrameTarget(plant, 640, 360);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, new GameAssets(), target, enableCulling: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        var evidence = new List<object>();
        Vector3 near = new(0, 3, 8);
        var nearCamera = RenderingBindlessProof.Camera(near, new(0, 1.4f, -20));
        var plane = new NativeMesh3D("unoccluded-plane", [
            new(new(-10, -10, 0), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
            new(new(10, -10, 0), Vector3.UnitZ, Vector4.One, Vector2.UnitX, new(1, 0, 0, 1)),
            new(new(10, 10, 0), Vector3.UnitZ, Vector4.One, Vector2.One, new(1, 0, 0, 1)),
            new(new(-10, 10, 0), Vector3.UnitZ, Vector4.One, Vector2.UnitY, new(1, 0, 0, 1)),
        ], [0, 1, 2, 0, 2, 3]);
        Native3DScene unoccluded = new([], []) { Instances = new NativeInstanceSet3D([
            new("plane", plane, new("plane") { DoubleSided = true }, Matrix4x4.Identity)]) };
        evidence.Add(RunCase(renderer, unoccluded, nearCamera, near,
            "unoccluded-plane-negative-hzb", true, VisibilityPolicyMode.Baseline, output));
        evidence.Add(RunCase(renderer, RenderingHzbProof.Forest(true, dense: true), nearCamera, near,
            "dense-occlusion-positive-hzb", true, VisibilityPolicyMode.Hzb, output));
        var materials = RenderingBindlessProof.Materials(64);
        Native3DScene forest = new([], [])
        {
            Instances = new NativeInstanceSet3D(Enumerable.Range(0, 20_000).Select(index => new SceneInstance3D(
                "tree-" + index, mesh, materials[index % materials.Length],
                Matrix4x4.CreateScale(.8f + index % 7 * .07f) * Matrix4x4.CreateRotationY(index * .71f)
                * Matrix4x4.CreateTranslation((index % 200 - 100) * 2.5f, 0, -(index / 200) * 2.5f)))),
        };
        Vector3 far = new(0, 50, 120);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, 640f / 360, .1f, 1000);
        projection.M22 *= -1;
        var farCamera = Matrix4x4.CreateLookAt(far, new(0, 1.4f, -60), Vector3.UnitY) * projection;
        evidence.Add(RunCase(renderer, forest, farCamera, far, "prepared-lod-opportunity", false, VisibilityPolicyMode.Lod, output));
        evidence.Add(RunCase(renderer, forest, farCamera, far, "combined-opportunities", true, null, output));
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, plant.Facts, Asset = asset, Hysteresis = .05, MinCommitSeconds = .75, Cases = evidence,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_DOMINATUS_RENDERING_POLICY_PASSED");
    }

    private static object RunCase(VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera,
        Vector3 eye, string name, bool hzbAvailable, VisibilityPolicyMode? expected, string output)
    {
        renderer.EnableHierarchicalZ = false;
        renderer.LodPixelError = 0;
        for (int warm = 0; warm < 128; warm++) renderer.Render(scene, camera, eye, Clear);
        var bridge = new NativeVisibilityPolicy3D(hzbAvailable, traceCapacity: 4096);
        var transitions = new List<object>();
        var policyCpu = new List<double>();
        VisibilityPolicyMode? previous = null;
        long started = Stopwatch.GetTimestamp();
        long last = started;
        int frames = 0;
        int rendered = 0;
        for (; frames < 12_000; frames++)
        {
            long now = Stopwatch.GetTimestamp();
            bridge.BeforeFrame(renderer, scene, camera, Stopwatch.GetElapsedTime(last, now));
            long policyTicks = Stopwatch.GetTimestamp() - now;
            last = now;
            var mode = bridge.Controller.Current;
            if (mode != previous)
            {
                transitions.Add(new { Mode = mode.ToString(), Seconds = Stopwatch.GetElapsedTime(started).TotalSeconds });
                previous = mode;
            }
            var result = renderer.Render(scene, camera, eye, Clear);
            long observedAt = Stopwatch.GetTimestamp();
            bridge.AfterFrame(result);
            policyTicks += Stopwatch.GetTimestamp() - observedAt;
            rendered++;
            if (frames > 32) policyCpu.Add(policyTicks * 1000d / Stopwatch.Frequency);
            if (frames > 3)
                Require(result.Retained is { CpuVisitedInstances: 0, InstanceUploadBytes: 0 },
                    "Policy switch rebuilt resident scene/material/visibility resources.");
            if (Stopwatch.GetElapsedTime(started).TotalSeconds >= 6) break;
        }
        renderer.WaitForPendingFrames();
        double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        policyCpu.Sort();
        var current = bridge.Controller.Current;
        var costs = bridge.Controller.Costs.Where(cost => cost.Samples >= 8).ToArray();
        File.WriteAllText(Path.Combine(output, name + "-measurements.json"), JsonSerializer.Serialize(new
        {
            Selected = current.ToString(), Costs = costs, Transitions = transitions,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Require(costs.Length == (hzbAvailable ? (scene.Instances[0].Mesh.LodRanges.Length > 1 ? 4 : 2) : 2),
            name + ": not all applicable modes were measured.");
        double chosenCost = costs.Single(cost => cost.Mode == current).GpuMilliseconds;
        Require(chosenCost <= costs.Min(cost => cost.GpuMilliseconds) * 1.12, name + ": did not choose a measured competitive mode.");
        if (expected is { } required) Require(current == required, name + ": selected " + current);
        var inspection = bridge.Controller.Inspector.Observe();
        Require(inspection.Trace.Any(entry => entry.Decision is not null), "No real Dominatus utility decisions were traced.");
        renderer.ResetTemporalHistory();
        var actual = renderer.Render(scene, camera, eye, Clear, capture: true);
        renderer.EnableHierarchicalZ = false;
        renderer.ResetTemporalHistory();
        var reference = renderer.Render(scene, camera, eye, Clear, capture: true);
        Require(actual.PixelSha256 == reference.PixelSha256, "Adaptive HZB changed the chosen-LOD image.");
        Console.WriteLine($"{name}: {current}, {chosenCost:F3} ms; {frames} frames");
        return new { Name = name, Selected = current.ToString(), Frames = rendered, Seconds = seconds,
            FramesPerSecond = rendered / seconds, MedianPolicyCpuMilliseconds = policyCpu[policyCpu.Count / 2], Costs = costs,
            Transitions = transitions, PixelExact = true, Decisions = inspection.Trace.Where(entry => entry.Decision is not null) };
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
