using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Physics3D;
using Aurelian.Physics3D.Bepu;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

internal static class PhysicsProof
{
    private sealed record Distribution(int Samples, double MedianMilliseconds, double P95Milliseconds);
    private sealed record Measurement(int Bodies, int Workers, int AwakeBodies, long ContactPairsObserved,
        Distribution Step, Distribution Snapshot, double AllocatedBytesPerStep);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");
        var measurements = new List<Measurement>();
        int multipleWorkers = Math.Min(4, Environment.ProcessorCount);
        int[] workerCounts = multipleWorkers > 1 ? [1, multipleWorkers] : [1];
        foreach (int workers in workerCounts)
        {
            foreach (int bodies in new[] { 256, 1024, 4096 })
            {
                Measurement measurement = Benchmark(bodies, workers);
                measurements.Add(measurement);
                Console.WriteLine($"PHYSICS_BENCHMARK bodies={bodies} workers={workers} median_ms={measurement.Step.MedianMilliseconds:F3} p95_ms={measurement.Step.P95Milliseconds:F3}");
            }
        }

        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian BEPU physics"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        const int width = 960;
        const int height = 640;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")));
        renderer.Settings = Graphics3DSettings.Default with { BloomIntensity = 0, SunIntensity = 3 };

        using var physics = new BepuPhysicsWorld3D(new() { Substeps = 2 });
        ScenePlan plan = SceneCompiler.Compile(Scene.World("lab",
        [
            Scene.Box("floor", new(16, .5f, 16), new(.38f, .43f, .52f, 1), new(0, -.25f, 0), SceneCollision.Solid),
            Scene.Box("back", new(16, 5, .3f), new(.23f, .32f, .45f, 1), new(0, 2.5f, -5), SceneCollision.Solid),
        ]));
        using IDisposable statics = PhysicsScene3D.AddStatics(physics, SceneSpatial3D.Build(plan));
        using SceneInstance scene = plan.Mount();
        Vector4[] colors = [new(.8f, .25f, .1f, 1), new(.1f, .65f, .8f, 1), new(.7f, .6f, .15f, 1), new(.2f, .7f, .35f, 1)];
        for (int index = 0; index < 32; index++)
        {
            Vector3 position = new((index % 4 - 1.5f) * 1.15f, 2.5f + (index / 16) * 1.25f,
                ((index / 4) % 4 - 1.5f) * 1.15f);
            var body = new PhysicsBody3D("prototype", new PhysicsShape3D.Box(new(.8f)), PhysicsPose3D.At(Vector3.Zero));
            var visual = Scene.Group("body", [Scene.Box("crate", new(.8f), colors[index % colors.Length])]);
            var definition = new PhysicsAgentDefinition3D(physics, body, visual, "physics-proof/crate/v1");
            scene.Spawn($"crate-{index:D2}", definition, SceneTransform.At(position));
        }
        Vector3 eye = new(9, 8, 12);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(1.05f, width / (float)height, .1f, 100);
        projection.M22 *= -1;
        Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, new(0, 1.2f, 0), Vector3.UnitY) * projection;
        var clear = new NativeFrameClearColor(.025f, .035f, .055f, 1);
        byte[] initial = Capture("initial");
        long contacts = 0;
        for (int tick = 1; tick <= 300; tick++)
        {
            if (tick == 120)
            {
                physics.ApplyImpulse("crate-16", new(2, 4, 1), new(0, .3f, 0));
            }
            contacts += physics.Step(new(tick)).Contacts.Length;
            PhysicsScene3D.Publish(scene, physics);
            if (tick == 30 || tick == 120)
            {
                Capture($"tick-{tick:D3}");
            }
        }
        byte[] final = Capture("settled");
        PhysicsSnapshot3D finalState = physics.CaptureSnapshot();
        Require(contacts > 0, "The native demonstration produced no contact pairs.");
        Require(!initial.SequenceEqual(final), "The simulation did not change the rendered scene.");
        foreach (PhysicsBodyState3D body in finalState.Bodies.Where(body => body.MotionType == PhysicsMotionType3D.Dynamic))
        {
            Require(body.Pose.Position.Y >= .37f && body.Pose.Position.Y < 2,
                $"Body '{body.Id}' did not settle on the authored scene collision: {body.Pose.Position}.");
            Require(scene.Agent<PhysicsBodyState3D>(body.Id).State == body, "Scene agent state diverged from physics output.");
        }
        File.WriteAllText(Path.Combine(output, "snapshot.json"), JsonSerializer.Serialize(finalState,
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Backend = physics.Backend,
            Configuration = configuration,
            Runtime = Environment.Version.ToString(),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            LogicalProcessors = Environment.ProcessorCount,
            Device = plant.Facts.PhysicalDeviceName,
            ValidationLayers = plant.Facts.EnabledValidationLayers,
            Tick = physics.Tick,
            NativeSceneBodies = physics.BodyCount,
            ContactPairsObserved = contacts,
            Measurements = measurements,
            BenchmarkPolicy = "1/60 second; 8 solver iterations; 1 substep; 120 warmup + 180 measured steps; sleeping disabled; contacts enabled. Snapshot timed separately.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"AURELIAN_PHYSICS_PASSED {evidencePath}");

        byte[] Capture(string name)
        {
            Native3DScene projected = SceneGeometry3D.BuildScene(scene.Project());
            for (int frame = 0; frame < 8; frame++)
            {
                renderer.Render(projected, camera, eye, clear, false);
            }
            byte[] pixels = renderer.Render(projected, camera, eye, clear, true).Pixels!;
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), width, height, pixels);
            return pixels;
        }
    }

    private static Measurement Benchmark(int bodyCount, int workers)
    {
        using var world = new BepuPhysicsWorld3D(new() { WorkerCount = workers, EnableSleeping = false });
        world.AddBody(new("floor", new PhysicsShape3D.Box(new(50, .5f, 50)), PhysicsPose3D.At(new(0, -.25f, 0)))
        {
            MotionType = PhysicsMotionType3D.Static,
        });
        for (int index = 0; index < bodyCount; index++)
        {
            Vector3 position = new((index % 16 - 7.5f) * 1.05f, .5f + (index / 256) * .91f,
                ((index / 16) % 16 - 7.5f) * 1.05f);
            world.AddBody(new($"body-{index:D5}", new PhysicsShape3D.Box(new(.85f)), PhysicsPose3D.At(position)));
        }
        for (int tick = 1; tick <= 120; tick++)
        {
            world.Step(new(tick));
        }
        var steps = new List<double>(180);
        long contacts = 0;
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        for (int tick = 121; tick <= 300; tick++)
        {
            long start = Stopwatch.GetTimestamp();
            PhysicsStepResult3D result = world.Step(new(tick));
            steps.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            contacts += result.Contacts.Length;
        }
        double allocatedPerStep = (GC.GetTotalAllocatedBytes(true) - allocatedBefore) / 180d;
        PhysicsSnapshot3D snapshot = world.CaptureSnapshot();
        int awake = 0;
        foreach (PhysicsBodyState3D body in snapshot.Bodies)
        {
            body.Pose.Validate();
            body.Velocity.Validate();
            if (body.MotionType == PhysicsMotionType3D.Dynamic)
            {
                Require(body.Pose.Position.Y > .35f, $"Benchmark body '{body.Id}' fell through its ground.");
                if (body.Awake)
                {
                    awake++;
                }
            }
        }
        Require(awake == bodyCount, "An active-body benchmark silently measured sleeping bodies.");
        Require(contacts > 0, "The benchmark did not exercise collision contacts.");
        var snapshots = new List<double>(30);
        for (int sample = 0; sample < 30; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            world.CaptureSnapshot();
            snapshots.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return new(bodyCount, workers, awake, contacts, Summarize(steps), Summarize(snapshots), allocatedPerStep);
    }

    private static Distribution Summarize(List<double> values)
    {
        double[] sorted = values.Order().ToArray();
        return new(sorted.Length, sorted[sorted.Length / 2], sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
