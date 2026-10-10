using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Cloth3D;
using Aurelian.Cloth3D.Vulkan;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

internal static class ClothFoundationProof
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        byte[] spirv = ClothCompute3D.Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Cloth3D.v.ts")));
        File.WriteAllBytes(Path.Combine(output, "cloth.spv"), spirv);
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian cloth foundation"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var options = new ClothStepOptions3D();
        var contacts = new ClothContacts3D
        {
            PlaneNormal = Vector3.UnitY, SphereCenter = new(0, 1, 0), SphereRadius = .7f,
        };
        var benchmarks = new List<object>();
        foreach (int side in new[] { 9, 17, 33 })
        {
            var plan = Sheet(side);
            using var cpu = new ClothSolver3D(plan);
            using var gpu = new VulkanClothSolver3D(plant, plan, spirv);
            var cpuTimes = new List<double>();
            var gpuTimes = new List<double>();
            var readbackTimes = new List<double>();
            float maximumStretch = 0;
            float maximumPenetration = 0;
            float maximumPinError = 0;
            float earlyAgreement = 0;
            float cpuMaximumStretch = 0;
            float cpuMaximumPenetration = 0;
            for (int tick = 0; tick < 180; tick++)
            {
                Vector3 offset = tick >= 60 ? new(.15f * MathF.Sin((tick - 60) / 60f), 0, 0) : Vector3.Zero;
                foreach (int pin in plan.Definition.Pins)
                {
                    Vector3 target = plan.Definition.Positions[pin] + offset;
                    cpu.SetPin(pin, target);
                    gpu.SetPin(pin, target);
                }
                var clock = Stopwatch.StartNew();
                cpu.Step(options, contacts);
                cpuTimes.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart();
                gpu.Step(options, contacts);
                gpuTimes.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart();
                var snapshot = gpu.Capture();
                readbackTimes.Add(clock.Elapsed.TotalMilliseconds);
                var metrics = ClothState3D.Measure(plan, snapshot, contacts);
                var reference = cpu.Capture();
                var cpuMetrics = ClothState3D.Measure(plan, reference, contacts);
                cpuMaximumStretch = MathF.Max(cpuMaximumStretch, cpuMetrics.MaximumStretch);
                cpuMaximumPenetration = MathF.Max(cpuMaximumPenetration, cpuMetrics.MaximumSurfacePenetration);
                maximumStretch = MathF.Max(maximumStretch, metrics.MaximumStretch);
                maximumPenetration = MathF.Max(maximumPenetration, metrics.MaximumSurfacePenetration);
                maximumPinError = MathF.Max(maximumPinError, metrics.MaximumPinError);
                Require(metrics.Finite, "Cloth produced nonfinite metrics.");
                if (tick == 11)
                {
                    earlyAgreement = snapshot.Positions.Zip(reference.Positions, Vector3.Distance).Max();
                    Require(earlyAgreement < .001f, $"CPU/GPU cloth disagreement: {earlyAgreement} m.");
                }
            }
            var final = gpu.Capture();
            var finalMetrics = ClothState3D.Measure(plan, final, contacts);
            var record = new
            {
                Side = side, Vertices = plan.Definition.Positions.Length,
                Triangles = plan.Definition.Indices.Length / 3, Constraints = plan.Constraints.Length,
                Colors = plan.ConstraintColors.Length, ContactColors = plan.TriangleColors.Length,
                Options = options, contacts, Ticks = final.Tick, EarlyCpuGpuErrorMetres = earlyAgreement,
                MaximumStretch = maximumStretch, MaximumSurfacePenetrationMetres = maximumPenetration,
                MaximumPinErrorMetres = maximumPinError, Final = finalMetrics,
                CpuMaximumStretch = cpuMaximumStretch, CpuMaximumSurfacePenetrationMetres = cpuMaximumPenetration,
                FinalCpuGpuErrorMetres = final.Positions.Zip(cpu.Capture().Positions, Vector3.Distance).Max(),
                CpuStepP95Milliseconds = Percentile(cpuTimes.Skip(20)),
                GpuSubmitAndWaitP95Milliseconds = Percentile(gpuTimes.Skip(20)),
                FullReadbackP95Milliseconds = Percentile(readbackTimes.Skip(20)),
                gpu.DispatchCount,
            };
            Write(Path.Combine(output, $"grid-{side}.json"), record);
            benchmarks.Add(record);
            Require(maximumStretch < .15f, $"Grid {side}: cloth stretch exceeded 15%: {maximumStretch}.");
            Require(maximumPenetration < .006f, $"Grid {side}: surface penetration exceeded 6 mm: {maximumPenetration}.");
            Require(maximumPinError < .00001f, "Moving cloth pin missed its authored target.");
            Require(cpuMaximumStretch < .15f && cpuMaximumPenetration < .006f, "CPU reference exceeded its stretch/contact budget.");
            if (side == 17)
            {
                VerifyReplay(gpu, options, contacts);
                using var restSolver = new ClothSolver3D(plan);
                Render(plant, plan, restSolver.Capture(), final, output, contacts);
                Write(Path.Combine(output, "final-state.json"), new
                {
                    final.ContentKey, final.Tick,
                    Positions = final.Positions.Select(Vector), Velocities = final.Velocities.Select(Vector),
                    PinTargets = final.PinTargets.Select(Vector),
                });
            }
            Console.WriteLine(JsonSerializer.Serialize(record, new JsonSerializerOptions { IncludeFields = true }));
        }
        VerifyInterior(plant, spirv, output);
        Write(evidence, new
        {
            Accepted = true, plant.Facts.PhysicalDeviceName, plant.Facts.EnabledValidationLayers,
            Benchmarks = benchmarks,
            Scope = "Small-step XPBD flat patterns; discrete one-way plane and sphere triangle-surface contact; no self-contact or CCD certificate.",
            TimingScope = "Release wall clock; GPU column includes command recording, submission and wait, excludes Capture; no GPU-only speedup claim.",
        });
        Console.WriteLine("AURELIAN_CLOTH_FOUNDATION_PASSED " + output);
    }

    private static CompiledCloth3D Sheet(int side) => (ClothDefinition3D.Grid("foundation", side, side,
        new(3, 3), new(-1.5f, 2.6f, -1.5f)) with { Pins = [0, side - 1] }).Compile();

    private static void VerifyReplay(IClothSolver3D solver, ClothStepOptions3D options, ClothContacts3D contacts)
    {
        var checkpoint = solver.Capture();
        solver.Step(options, contacts);
        var expected = solver.Capture();
        solver.Restore(checkpoint);
        solver.Step(options, contacts);
        var actual = solver.Capture();
        Require(expected.Positions.SequenceEqual(actual.Positions) && expected.Velocities.SequenceEqual(actual.Velocities),
            "GPU same-device cloth rewind did not replay exactly.");
    }

    private static void VerifyInterior(AurelianVulkanPlant plant, byte[] spirv, string output)
    {
        var definition = new ClothDefinition3D("face", [new(-2, .1f, -2), new(2, .1f, -2), new(0, .1f, 2)],
            [new(0, 0), new(4, 0), new(2, 4)], [0, 1, 2]);
        var plan = definition.Compile();
        using var solver = new VulkanClothSolver3D(plant, plan, spirv);
        var contacts = new ClothContacts3D { SphereCenter = Vector3.Zero, SphereRadius = .5f };
        float before = ClothState3D.Measure(plan, solver.Capture(), contacts).MaximumSurfacePenetration;
        solver.Step(new() { Gravity = Vector3.Zero, Substeps = 1, Iterations = 1, ContactIterations = 8 }, contacts);
        float after = ClothState3D.Measure(plan, solver.Capture(), contacts).MaximumSurfacePenetration;
        Require(after < .0001f,
            "Vulkan triangle interior contact did not fix a vertex-only collision miss.");
        Write(Path.Combine(output, "triangle-interior.json"), new
        {
            AllVerticesInitiallyClear = definition.Positions.All(position => position.Length() > contacts.SphereRadius),
            BeforeSurfacePenetrationMetres = before, AfterSurfacePenetrationMetres = after,
        });
    }

    private static void Render(AurelianVulkanPlant plant, CompiledCloth3D plan, ClothSnapshot3D rest,
        ClothSnapshot3D final, string output, ClothContacts3D contacts)
    {
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, 960, 640);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")));
        renderer.Settings = Graphics3DSettings.Default with { BloomIntensity = 0, SunIntensity = 3 };
        var cloth = new ClothAgentDefinition3D(plan);
        using var scene = SceneCompiler.Compile(Scene.World("cloth-proof",
        [
            Scene.Box("floor", new(10, .5f, 10), new(.38f, .43f, .52f, 1), new(0, -.25f, 0)),
            Scene.Box("left-pin", new(.06f, 2.6f, .06f), new(.2f, .22f, .25f, 1), new(-1.5f, 1.3f, -1.5f)),
            Scene.Box("right-pin", new(.06f, 2.6f, .06f), new(.2f, .22f, .25f, 1), new(1.5f, 1.3f, -1.5f)),
            Scene.Agent("sheet", cloth),
        ])).Mount();
        var agent = scene.Agent<ClothSnapshot3D>("sheet");
        Vector3 eye = new(5.4f, 4.5f, 6);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, .1f, 50);
        projection.M22 *= -1;
        var camera = Matrix4x4.CreateLookAt(eye, new(0, 1.2f, 0), Vector3.UnitY) * projection;
        string? initialHash = null;
        foreach (var (name, snapshot) in new[] { ("initial", rest), ("draped", final) })
        {
            agent.State = snapshot;
            SceneFrame frame = scene.Project();
            frame = frame with
            {
                Boxes = frame.Boxes.Select(box => box.Id switch
                {
                    "left-pin" => box with { WorldTransform = Matrix4x4.CreateTranslation(snapshot.Positions[plan.Definition.Pins[0]] - new Vector3(0, 1.3f, 0)) },
                    "right-pin" => box with { WorldTransform = Matrix4x4.CreateTranslation(snapshot.Positions[plan.Definition.Pins[1]] - new Vector3(0, 1.3f, 0)) },
                    _ => box,
                }).ToImmutableArray(),
            };
            Native3DScene native = SceneGeometry3D.BuildScene(frame);
            native = native with
            {
                Models = native.Models.Append(SurfaceGraphicsProof.Sphere(contacts.SphereCenter!.Value,
                    contacts.SphereRadius, new("support") { BaseColor = new(.65f, .4f, .13f, 1), Metallic = .65f, Roughness = .35f })).ToArray(),
            };
            var clear = new NativeFrameClearColor(.035f, .045f, .065f, 1);
            for (int sample = 0; sample < 12; sample++)
            {
                renderer.Render(native, camera, eye, clear, false);
            }
            var result = renderer.Render(native, camera, eye, clear, true);
            if (initialHash is null)
            {
                initialHash = result.PixelSha256;
            }
            else Require(initialHash != result.PixelSha256, "Cloth simulation did not change Vulkan pixels.");
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), 960, 640, result.Pixels!);
        }
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];
    private static double Percentile(IEnumerable<double> values)
    {
        double[] ordered = values.Order().ToArray();
        return ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
    }
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value,
        new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    private static void Require(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }
}
