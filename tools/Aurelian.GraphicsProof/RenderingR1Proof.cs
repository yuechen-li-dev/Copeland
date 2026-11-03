using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.FramePlans;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static class RenderingR1Proof
{
    private const int Width = 640;
    private const int Height = 360;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian rendering R1"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var results = new List<Measurement>();
        var hashes = new Dictionary<string, string>();
        foreach (var profile in new[]
        {
            (Name: "control", Options: new FramePlanOptions(false, false, false)),
            (Name: "compiled", Options: new FramePlanOptions(true, true, false)),
            (Name: "second-queue", Options: new FramePlanOptions(true, true, true)),
        })
        {
            using var target = new VulkanNativeFrameTarget(plant, Width, Height);
            using var particles = new VulkanParticles3D(plant, assets.ComputeShader("Particles3D.v.ts"), 16, 8192);
            using var renderer = CreateRenderer(plant, assets, target, profile.Options);
            renderer.ParticleSystem = particles;
            renderer.FrameMode = Native3DFrameMode.Interactive;
            renderer.Settings = Graphics3DSettings.Default with
            {
                AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
                LocalShadowBudget = 0, Fog = new() { Density = .025f, Color = new(.1f, .16f, .24f) },
            };
            Native3DScene scene = Grid();
            Vector3 eye = new(0, 2, 5);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(.9f, (float)Width / Height, .1f, 100);
            projection.M22 *= -1;
            Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * projection;
            var clear = new NativeFrameClearColor(.02f, .03f, .05f, 1);
            foreach (var workload in new[] { "inactive", "particles-small", "particles-bandwidth" })
            {
                int count = workload == "inactive" ? 0 : workload == "particles-small" ? 1 : 16;
                renderer.ParticleEmitters = Enumerable.Range(0, count).Select(index =>
                    ParticleEmitter3D.Sparks("sparks-" + index, new(index == 0 ? 0 : 20 + index, .1f, 0), (uint)(17 + index)) with
                    {
                        ElapsedSeconds = 2, Rate = workload == "particles-small" ? 100 : 4096, Lifetime = 2,
                    }).ToArray();
                var first = renderer.Render(scene, camera, eye, clear, capture: true);
                string key = profile.Name + "-" + workload;
                hashes.Add(key, first.PixelSha256!);
                if (profile.Name != "control")
                {
                    Require(hashes["control-" + workload] == first.PixelSha256, "Frame-plan profile changed pixels: " + key);
                }
                NativeGameGraphics.WritePng(Path.Combine(output, key + ".png"), Width, Height, first.Pixels!);
                FramePlan plan = renderer.CurrentFramePlan!;
                File.WriteAllText(Path.Combine(output, key + ".frame-plan.json"), plan.Describe());
                renderer.InstallFramePlan(plan.Describe());
                FramePlan installed = renderer.CurrentFramePlan!;
                var cpu = new List<double>();
                var gpu = new List<double>();
                var overlap = new List<NativeQueueOverlap>();
                long started = Stopwatch.GetTimestamp();
                for (int frame = 0; frame < 48; frame++)
                {
                    var result = renderer.Render(scene, camera, eye, clear);
                    if (frame >= 12)
                    {
                        cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds);
                        if (result.CompletedGpuFrame is { } completed)
                        {
                            gpu.Add((completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000d);
                        }
                        if (renderer.LastQueueOverlap is { } interval && count > 0) overlap.Add(interval);
                    }
                }
                renderer.WaitForPendingFrames();
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Require(ReferenceEquals(renderer.CurrentFramePlan, installed), "Steady frames rebuilt or replaced the installed plan.");
                var replay = renderer.Render(scene, camera, eye, clear, capture: true);
                Require(first.PixelSha256 == replay.PixelSha256, "Pipelining or JSON artifact replay changed pixels: " + key);
                if (profile.Name == "compiled")
                {
                    Require(!plan.Order.Any(id => plan.Graph.Passes[id].Name is "ambient-occlusion" or "light-tiles"),
                        "Inactive passes were recorded.");
                    Require(renderer.TransientPhysicalImages == 1, "Diffuse/fog lifetime did not reuse one physical image.");
                }
                results.Add(new(profile.Name, workload, plan.Hash, plan.Order.Length,
                    plan.Tape.Count(instruction => instruction.Op == FrameTapeOp.Barrier), plan.Batches.Length,
                    renderer.TransientPhysicalImages, renderer.TransientTextureBytes, Median(cpu), Median(gpu),
                    48_000 / elapsed, overlap.Count(interval => interval.OverlapMilliseconds > 0),
                    overlap.Count == 0 ? 0 : Median(overlap.Select(interval => interval.OverlapMilliseconds).ToList()),
                    overlap.Count == 0 ? 0 : Median(overlap.Select(interval => interval.ComputeMilliseconds).ToList())));
                Console.WriteLine($"R1 {key}: {plan.Order.Length} passes, {renderer.TransientTextureBytes} transient bytes");
            }
        }
        validation.RequireClean();
        var control = results.Single(result => result.Profile == "control" && result.Workload == "inactive");
        var compiled = results.Single(result => result.Profile == "compiled" && result.Workload == "inactive");
        Require(compiled.TransientBytes * 2 == control.TransientBytes && compiled.Passes == control.Passes - 2,
            "The motivating inactive-pass/transient-memory case did not improve.");
        bool secondQueue = plant.ComputeQueue.HasValue;
        if (secondQueue)
        {
            Require(results.Any(result => result.Profile == "second-queue" && result.Batches > 1), "No native multi-queue submission.");
        }
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts, Width, Height,
            RuntimeTieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            SecondQueueAvailable = secondQueue, IdenticalProfileCaptures = hashes,
            SavedTransientBytes = control.TransientBytes - compiled.TransientBytes,
            Measurements = results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_RENDERING_R1_PASSED " + evidence);
    }

    private static VulkanSolid3DRenderer CreateRenderer(AurelianVulkanPlant plant, GameAssets assets,
        VulkanNativeFrameTarget target, FramePlanOptions options)
    {
        return new(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
            {
                HeightFog = assets.Shader("HeightFog3D.v.ts"),
                SubsurfaceDiffuse = assets.Shader("SubsurfaceDiffuse3D.v.ts"),
                SubsurfaceMerge = assets.Shader("SubsurfaceMerge3D.v.ts"),
                TransparentModel = assets.Shader("TransparentModel3D.v.ts"),
                TransparencyResolve = assets.Shader("TransparencyResolve3D.v.ts"),
                ParticleBillboards = assets.Shader("ParticleBillboard3D.v.ts"),
            }, framePlans: options);
    }

    private static Native3DScene Grid()
    {
        var vertices = new List<NativeModel3DVertex>();
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float left = (x - 32) * .04f;
                float top = (y - 32) * .04f;
                NativeModel3DVertex Vertex(float px, float py) => new(new(px, py, 0), Vector3.UnitZ,
                    Vector4.One, Vector2.Zero, new(1, 0, 0, 1));
                vertices.AddRange([Vertex(left, top), Vertex(left + .04f, top), Vertex(left, top + .04f),
                    Vertex(left, top + .04f), Vertex(left + .04f, top), Vertex(left + .04f, top + .04f)]);
            }
        }
        var material = new ModelMaterial("r1-grid")
        {
            BaseColor = new(.6f, .22f, .08f, 1), Metallic = 0, DoubleSided = true,
            SubsurfaceStrength = .4f,
        };
        return new([], [new(vertices.ToArray(), material)]);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values.Count == 0 ? 0 : values[values.Count / 2];
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed record Measurement(string Profile, string Workload, string PlanHash, int Passes, int Barriers,
        int Batches, int TransientImages, ulong TransientBytes, double MedianPrepareRecordMilliseconds,
        double MedianGpuFrameMilliseconds, double FramesPerSecond, int OverlappingSamples,
        double MedianOverlapMilliseconds, double MedianParticleComputeMilliseconds);
}
