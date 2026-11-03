using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;

internal static class RenderingR2Proof
{
    private const int Width = 640;
    private const int Height = 360;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian R2 Forest"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        Require(plant.Facts.IndirectDrawingEnabled, "The Forest qualification requires multiDrawIndirect, drawIndirectFirstInstance and drawIndirectCount.");
        var assets = new GameAssets();
        WriteProfileProbes(output);
        CheckDirectFallback(plant, assets);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")),
            retainedPrograms: new(assets.Shader("InstancedModel3D.v.ts"), assets.Shader("InstancedShadow3D.v.ts"))
            {
                Visibility = new(assets.ComputeShader("VisibilityCull3D.v.ts"), assets.ComputeShader("VisibilityScan3D.v.ts"),
                    assets.ComputeShader("VisibilityScatter3D.v.ts")),
            });
        renderer.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        renderer.FrameMode = Native3DFrameMode.Interactive;
        CheckBoundaries(renderer, output);
        var mesh = Tree();
        var material = new ModelMaterial("forest") { BaseColor = Vector4.One, Metallic = 0, Roughness = .8f };
        var all = Enumerable.Range(0, 20_000).Select(index => new SceneInstance3D("tree-" + index, mesh, material,
            Matrix4x4.CreateScale(.8f + index % 7 * .07f)
            * Matrix4x4.CreateRotationY(index * .71f)
            * Matrix4x4.CreateTranslation((index % 200 - 100) * 2.5f, 0, -(index / 200) * 2.5f))).ToArray();
        long sourceTriangles = (long)mesh.Indices.Length / 3 * all.Length;
        Require(sourceTriangles >= 20_000_000, "Forest source triangle requirement was not met.");
        int nondegeneratePerMesh = Enumerable.Range(0, mesh.Indices.Length / 3).Count(triangle =>
        {
            Vector3 a = mesh.Vertices[(int)mesh.Indices[triangle * 3]].Position;
            Vector3 b = mesh.Vertices[(int)mesh.Indices[triangle * 3 + 1]].Position;
            Vector3 c = mesh.Vertices[(int)mesh.Indices[triangle * 3 + 2]].Position;
            return Vector3.Cross(b - a, c - a).LengthSquared() > 1e-12f;
        });
        long nondegenerateSourceTriangles = (long)nondegeneratePerMesh * all.Length;
        Require(nondegenerateSourceTriangles >= 20_000_000, "Forest does not meet the triangle requirement without degenerate UV-pole triangles.");
        var clear = new NativeFrameClearColor(.12f, .18f, .25f, 1);
        var comparisons = new List<object>();
        var measurements = new List<object>();
        var resident = new NativeInstanceSet3D(all);
        Native3DScene frozen = new([], []) { Instances = resident };
        for (int pose = 0; pose < 8; pose++)
        {
            Vector3 eye = new(-8 + pose * 2.4f, 2 + pose % 3, 8 - pose * .8f);
            Matrix4x4 camera = Camera(eye, new(pose - 4, 1.3f, -20));
            var original = renderer.Render(frozen with { Instances = all }, camera, eye, clear, capture: true);
            foreach (var mode in Enum.GetValues<ClusterVisibilityMode>())
            {
                renderer.VisibilityMode = mode;
                var actual = renderer.Render(frozen, camera, eye, clear, capture: true);
                Require(actual.PixelSha256 == original.PixelSha256, $"Frozen camera {pose}, {mode}: visibility changed pixels.");
                var reads = renderer.ReadVisibilityForQualification();
                Require(reads.Count == 1, "Forest did not execute an indirect group.");
                var read = reads[0];
                int falseNegatives = 0;
                int falsePositives = 0;
                for (int index = 0; index < read.Candidates; index++)
                {
                    if (read.CpuVisible[index] && read.Ranks[index] == 0) falseNegatives++;
                    if (!read.CpuVisible[index] && read.Ranks[index] > 0) falsePositives++;
                }
                Require(falseNegatives == 0, "GPU culling rejected a CPU-visible cluster.");
                Require(read.DrawCount == read.Ranks.Count(rank => rank > 0), "Indirect count and visibility ranks disagree.");
                comparisons.Add(new { Pose = pose, Mode = mode.ToString(), read.Candidates, read.DrawCount,
                    FalseNegatives = falseNegatives, FalsePositives = falsePositives, actual.PixelSha256 });
                if (pose == 0 && mode == ClusterVisibilityMode.ClusterAndCone)
                {
                    NativeGameGraphics.WritePng(Path.Combine(output, "forest.png"), Width, Height, actual.Pixels!);
                    File.WriteAllText(Path.Combine(output, "forest.frame-plan.json"), renderer.CurrentFramePlan!.Describe());
                }
            }
            Console.WriteLine($"R2 frozen camera {pose} passed");
        }
        renderer.VisibilityMode = ClusterVisibilityMode.ClusterAndCone;
        Vector3 benchmarkEye = new(0, 3, 8);
        Matrix4x4 benchmarkCamera = Camera(benchmarkEye, new(0, 1, -20));
        foreach (int count in new[] { 1250, 5000, 20_000 })
        {
            var subset = all.Take(count).ToArray();
            foreach (bool persistent in new[] { false, true })
            {
                Native3DScene scene = new([], []) { Instances = persistent ? new NativeInstanceSet3D(subset) : subset };
                renderer.Render(scene, benchmarkCamera, benchmarkEye, clear, capture: true);
                var cpu = new List<double>();
                var submit = new List<double>();
                var gpu = new List<double>();
                Native3DFrameResult result = null!;
                for (int frame = 0; frame < 48; frame++)
                {
                    result = renderer.Render(scene, benchmarkCamera, benchmarkEye, clear);
                    if (frame >= 12)
                    {
                        cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds);
                        submit.Add(result.CpuTimings.SubmitMilliseconds);
                        if (result.CompletedGpuFrame is { } completed)
                            gpu.Add((completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000d);
                    }
                    if (persistent && frame >= 2)
                    {
                        Require(result.Retained is { CpuVisitedInstances: 0, InstanceUploadBytes: 0, IndirectGroups: 1 },
                            "Resident frames walked/uploaded instances or failed to use indirect drawing.");
                    }
                }
                renderer.WaitForPendingFrames();
                measurements.Add(new { Instances = count, Resident = persistent, SourceTriangles = (long)mesh.Indices.Length / 3 * count,
                    MedianPrepareRecordMilliseconds = Median(cpu), MedianSubmitMilliseconds = Median(submit),
                    MedianGpuMilliseconds = Median(gpu), result.Retained });
                Console.WriteLine($"R2 count {count} resident={persistent}: {Median(cpu):F3} ms prepare/record, {Median(gpu):F3} ms GPU");
            }
        }
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts, Width, Height, SourceTriangles = sourceTriangles,
            NondegenerateSourceTriangles = nondegenerateSourceTriangles,
            DirectResidentFallbackVerified = true,
            RuntimeTieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            Comparisons = comparisons, Measurements = measurements,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_RENDERING_R2_PASSED " + evidence);
    }

    private static Matrix4x4 Camera(Vector3 eye, Vector3 target)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)Width / Height, .1f, 95);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
    }

    private static void CheckDirectFallback(AurelianVulkanPlant plant, GameAssets assets)
    {
        using var target = new VulkanNativeFrameTarget(plant, 96, 96);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")),
            retainedPrograms: new(assets.Shader("InstancedModel3D.v.ts"), assets.Shader("InstancedShadow3D.v.ts")));
        renderer.Settings = Graphics3DSettings.Default with { AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0 };
        renderer.FrameMode = Native3DFrameMode.Interactive;
        SceneInstance3D[] instances = [new("tree", Tree(), new ModelMaterial("fallback"), Matrix4x4.Identity)];
        Native3DScene scene = new([], []) { Instances = instances };
        Vector3 eye = new(0, 2, 6);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, 1, .1f, 100);
        projection.M22 *= -1;
        Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, new(0, 1.2f, 0), Vector3.UnitY) * projection;
        var clear = new NativeFrameClearColor(.02f, .03f, .05f, 1);
        var control = renderer.Render(scene, camera, eye, clear, capture: true);
        scene = scene with { Instances = new NativeInstanceSet3D(instances) };
        for (int frame = 0; frame < 6; frame++) renderer.Render(scene, camera, eye, clear);
        var actual = renderer.Render(scene, camera, eye, clear, capture: true);
        Require(actual.PixelSha256 == control.PixelSha256
            && actual.Retained is { CpuVisitedInstances: 0, InstanceUploadBytes: 0, IndirectGroups: 0 },
            "Resident direct-draw fallback changed pixels or continued to visit/upload instances.");
    }

    private static void CheckBoundaries(VulkanSolid3DRenderer renderer, string output)
    {
        NativeModel3DVertex Vertex(float x, float y) => new(new(x, y, 0), Vector3.UnitZ, Vector4.One,
            Vector2.Zero, new(1, 0, 0, 1));
        var mesh = new NativeMesh3D("r2-boundary-plane", [Vertex(-.4f, -.4f), Vertex(.4f, -.4f), Vertex(0, .4f)], [0, 1, 2]);
        var material = new ModelMaterial("plane") { Metallic = 0, BaseColor = new(.6f, .2f, .1f, 1) };
        var cases = new List<object>();
        foreach (bool doubleSided in new[] { false, true })
        {
            foreach (var transform in new[]
            {
                Matrix4x4.Identity,
                Matrix4x4.CreateScale(.5f, 1.7f, .9f) * Matrix4x4.CreateRotationY(.4f),
                new Matrix4x4(1, .4f, 0, 0, .7f, 1, .2f, 0, 0, .1f, 1, 0, 0, 0, 0, 1),
            })
            {
                SceneInstance3D[] instances = [
                    new("left", mesh, material with { DoubleSided = doubleSided }, transform * Matrix4x4.CreateTranslation(-.6f, 0, 0)),
                    new("right", mesh, material with { Slot = "other", DoubleSided = doubleSided, BaseColor = new(.1f, .4f, .7f, 1) },
                        transform * Matrix4x4.CreateTranslation(.6f, 0, 0)),
                ];
                foreach (float side in new[] { 1f, -1f })
                {
                    Vector3 eye = new(0, 0, side * 3);
                    Matrix4x4 camera = Camera(eye, Vector3.Zero);
                    var clear = new NativeFrameClearColor(.02f, .03f, .04f, 1);
                    Native3DScene scene = new([], []) { Instances = instances };
                    var control = renderer.Render(scene, camera, eye, clear, capture: true);
                    scene = scene with { Instances = new NativeInstanceSet3D(instances) };
                    var actual = renderer.Render(scene, camera, eye, clear, capture: true);
                    Require(control.PixelSha256 == actual.PixelSha256, "Transform/material/cone boundary changed pixels.");
                    var read = renderer.ReadVisibilityForQualification();
                    Require(read.Count == 2 && read.All(group => group.DrawCount == group.CpuVisible.Count(value => value)),
                        "Multiple material groups, firstInstance offsets or cone culling disagree.");
                    cases.Add(new { DoubleSided = doubleSided, Side = side, Draws = read.Sum(group => group.DrawCount) });
                    if (!doubleSided && transform == Matrix4x4.Identity && side < 0)
                        Require(read.All(group => group.DrawCount == 0), "Back-facing planar cones were not culled.");
                    var replacement = scene with { Instances = new NativeInstanceSet3D(instances.Select(instance =>
                        instance with { Transform = instance.Transform * Matrix4x4.CreateTranslation(.02f, 0, 0) })) };
                    renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.Temporal };
                    renderer.ResetTemporalHistory();
                    var temporalControl = renderer.Render(scene with { Instances = instances }, camera, eye, clear, capture: true);
                    renderer.Render(scene, camera, eye, clear);
                    renderer.Render(replacement, camera, eye, clear);
                    var replay = renderer.Render(scene, camera, eye, clear, capture: true);
                    Require(replay.PixelSha256 == temporalControl.PixelSha256, "In-flight resident replacement left stale transforms/history.");
                    renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.None };
                }
            }
        }
        File.WriteAllText(Path.Combine(output, "boundary-cases.json"), JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteProfileProbes(string output)
    {
        var records = new List<object>();
        string source = BuiltinShaderLibrary.Read("SurfaceModel3D.v.ts");
        foreach (var probe in new[]
        {
            (Name: "integer-visibility-target", Source: source.Replace("@target(0) color: float4;", "@target(0) color: uint2;", StringComparison.Ordinal)),
            (Name: "primitive-id", Source: source.Replace("@builtin(front_face) frontFace: bool;",
                "@builtin(primitive_id) primitive: u32; @builtin(front_face) frontFace: bool;", StringComparison.Ordinal)),
            (Name: "material-storage-buffer", Source: source.Replace("@binding(0) material: StaticModelMaterial;",
                "@binding(0) material: StorageBuffer<StaticModelMaterial>;", StringComparison.Ordinal)),
        })
        {
            Require(probe.Source != source, "Capability probe did not modify its source: " + probe.Name);
            var sources = BuiltinShaderLibrary.LoadSources("SurfaceModel3D.v.ts").Select(file =>
                file.Path == "SurfaceModel3D.v.ts" ? file with { Source = probe.Source } : file).ToArray();
            var result = GpuGraphicsBinder.Compile(new(sources));
            records.Add(new { probe.Name, result.Success, Diagnostics = result.Diagnostics.Select(diagnostic =>
                new { diagnostic.Code, diagnostic.Message }).ToArray() });
            File.WriteAllText(Path.Combine(output, probe.Name + ".v.ts"), probe.Source);
        }
        File.WriteAllText(Path.Combine(output, "profile-capability-probes.json"),
            JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static NativeMesh3D Tree()
    {
        var vertices = new List<NativeModel3DVertex>();
        var indices = new List<uint>();
        void Ellipsoid(Vector3 center, Vector3 radius, int rings, int slices, Vector4 color)
        {
            uint start = (uint)vertices.Count;
            for (int ring = 0; ring <= rings; ring++)
            {
                float latitude = MathF.PI * ring / rings;
                for (int slice = 0; slice <= slices; slice++)
                {
                    float longitude = MathF.Tau * slice / slices;
                    Vector3 unit = new(MathF.Sin(latitude) * MathF.Cos(longitude), MathF.Cos(latitude),
                        MathF.Sin(latitude) * MathF.Sin(longitude));
                    Vector3 normal = Vector3.Normalize(unit / radius);
                    Vector3 tangent = new(-MathF.Sin(longitude), 0, MathF.Cos(longitude));
                    vertices.Add(new(center + unit * radius, normal, color, new((float)slice / slices, (float)ring / rings), new(tangent, 1)));
                }
            }
            for (int ring = 0; ring < rings; ring++)
            {
                for (int slice = 0; slice < slices; slice++)
                {
                    uint a = start + (uint)(ring * (slices + 1) + slice);
                    uint b = a + (uint)slices + 1;
                    indices.AddRange([a, a + 1, b, a + 1, b + 1, b]);
                }
            }
        }
        Ellipsoid(new(0, .7f, 0), new(.14f, .8f, .14f), 8, 8, new(.3f, .12f, .045f, 1));
        Ellipsoid(new(0, 1.7f, 0), new(.75f, 1.2f, .75f), 16, 32, new(.12f, .34f, .055f, 1));
        return new("r2-forest-tree", vertices, indices);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values.Count == 0 ? 0 : values[values.Count / 2];
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
