using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static class RenderingBindlessProof
{
    private const int Width = 640;
    private const int Height = 360;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian many-material Forest"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        using var validation = new RenderingR0Validation(plant);
        Require(plant.Facts.BindlessMaterialsEnabled, "Bindless qualification requires the admitted nonuniform indexing features and descriptor limits.");
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = CreateRenderer(plant, assets, target);
        renderer.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        renderer.FrameMode = Native3DFrameMode.Interactive;
        var tree = RenderingR2Proof.Tree();
        var captures = new List<object>();
        var measurements = new List<object>();
        foreach (int materialCount in new[] { 1, 64, 512 })
        {
            ModelMaterial[] materials = Materials(materialCount);
            var instances = Enumerable.Range(0, 20_000).Select(index => new SceneInstance3D("tree-" + index, tree,
                materials[index % materialCount], Matrix4x4.CreateScale(.8f + index % 7 * .07f)
                    * Matrix4x4.CreateRotationY(index * .71f)
                    * Matrix4x4.CreateTranslation((index % 200 - 100) * 2.5f, 0, -(index / 200) * 2.5f))).ToArray();
            Native3DScene scene = new([], []) { Instances = new NativeInstanceSet3D(instances) };
            var clear = new NativeFrameClearColor(.1f, .15f, .22f, 1);
            for (int pose = 0; pose < 4; pose++)
            {
                Vector3 eye = new(pose * 2, 2.5f + pose * .2f, 8 - pose);
                Matrix4x4 camera = Camera(eye, new(0, 1.4f, -20));
                renderer.EnableBindlessMaterials = false;
                renderer.ResetTemporalHistory();
                var grouped = renderer.Render(scene, camera, eye, clear, capture: true);
                renderer.EnableBindlessMaterials = true;
                renderer.ResetTemporalHistory();
                var bindless = renderer.Render(scene, camera, eye, clear, capture: true);
                Require(grouped.PixelSha256 == bindless.PixelSha256,
                    $"Material count {materialCount}, pose {pose}: bindless shading changed captured pixels.");
                Require(grouped.Retained!.MaterialDescriptorBinds == materialCount
                    && bindless.Retained is { BindlessMaterials: true, MaterialDescriptorBinds: 1, DrawGroups: 1 },
                    "Descriptor binding/draw counters did not reflect the real path.");
                captures.Add(new { Materials = materialCount, Pose = pose, Grouped = grouped.PixelSha256,
                    Bindless = bindless.PixelSha256, bindless.Retained });
                if (pose == 0)
                    NativeGameGraphics.WritePng(Path.Combine(output, $"forest-{materialCount}.png"), Width, Height, bindless.Pixels!);
            }
            Vector3 benchmarkEye = new(0, 3, 8);
            Matrix4x4 benchmarkCamera = Camera(benchmarkEye, new(0, 1.4f, -20));
            // ABBA controls reduce ordering bias; admission and mode-switch uploads precede each warmup.
            foreach (bool bindless in new[] { false, true, true, false })
            {
                renderer.EnableBindlessMaterials = bindless;
                renderer.Render(scene, benchmarkCamera, benchmarkEye, clear, capture: true);
                var cpu = new List<double>();
                var submit = new List<double>();
                var gpu = new List<double>();
                Native3DFrameResult result = null!;
                long measuredAt = 0;
                for (int frame = 0; frame < 48; frame++)
                {
                    if (frame == 12) measuredAt = Stopwatch.GetTimestamp();
                    result = renderer.Render(scene, benchmarkCamera, benchmarkEye, clear);
                    if (frame >= 12)
                    {
                        cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds);
                        submit.Add(result.CpuTimings.SubmitMilliseconds);
                        if (result.CompletedGpuFrame is { } completed)
                            gpu.Add((completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000d);
                        Require(result.Retained is { InstanceUploadBytes: 0, CpuVisitedInstances: 0 }, "A steady frame rebuilt resident instance data.");
                    }
                }
                renderer.WaitForPendingFrames();
                double framesPerSecond = 36 / Stopwatch.GetElapsedTime(measuredAt).TotalSeconds;
                measurements.Add(new { Materials = materialCount, Bindless = bindless,
                    MedianPrepareRecordMilliseconds = Median(cpu), MedianSubmitMilliseconds = Median(submit),
                    MedianGpuMilliseconds = Median(gpu), FramesPerSecond = framesPerSecond, result.Retained });
                Console.WriteLine($"Bindless={bindless}, materials={materialCount}: CPU {Median(cpu):F3} ms, GPU {Median(gpu):F3} ms");
            }
        }
        var boundaryCases = CheckCulledSceneRevisions(plant, assets, output);
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts, Instances = 20_000, SourceTriangles = (long)tree.Indices.Length / 3 * 20_000,
            ClusterCullingEnabled = false, Captures = captures, Measurements = measurements,
            CulledSceneRevisions = boundaryCases,
            RuntimeTieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_BINDLESS_PASSED " + output);
    }

    internal static VulkanSolid3DRenderer CreateRenderer(AurelianVulkanPlant plant, GameAssets assets, VulkanNativeFrameTarget target,
        bool enableCulling = false, VisibilityBufferPrograms? visibilityPrograms = null)
        => new(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
            {
                SubsurfaceDiffuse = assets.Shader("SubsurfaceDiffuse3D.v.ts"),
                SubsurfaceMerge = assets.Shader("SubsurfaceMerge3D.v.ts"),
            }, retainedPrograms: new(assets.Shader("InstancedModel3D.v.ts"), assets.Shader("InstancedShadow3D.v.ts"))
            {
                Bindless = assets.Shader("BindlessModel3D.v.ts"),
                VisibilityBuffer = visibilityPrograms ?? new(assets.Shader("VisibilityRaster3D.v.ts"), assets.Shader("VisibilityResolve3D.v.ts"))
                {
                    AnalyticRaster = assets.Shader("AnalyticRaster3D.v.ts"),
                    HybridResolve = assets.Shader("HybridResolve3D.v.ts"),
                },
                HierarchicalZ = new(assets.ComputeShader("HzbCull3D.v.ts"), assets.ComputeShader("HzbReduce3D.v.ts")),
                Visibility = enableCulling ? new(assets.ComputeShader("VisibilityCull3D.v.ts"),
                    assets.ComputeShader("VisibilityScan3D.v.ts"), assets.ComputeShader("VisibilityScatter3D.v.ts")) : null,
            });

    private static IReadOnlyList<object> CheckCulledSceneRevisions(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = CreateRenderer(plant, assets, target, enableCulling: true);
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Graphics3DSettings.Default with { BloomIntensity = 0, AmbientOcclusionStrength = 0 };
        var meshA = RenderingR2Proof.Tree();
        var meshB = new NativeMesh3D("tree-b", meshA.Vertices, meshA.Indices);
        ModelMaterial[] materials = Materials(6);
        SceneInstance3D[] instances = Enumerable.Range(0, 72).Select(index => new SceneInstance3D("instance-" + index,
            index % 2 == 0 ? meshA : meshB, materials[index % materials.Length],
            Matrix4x4.CreateScale(.6f, .7f + index % 3 * .1f, .8f)
                * Matrix4x4.CreateTranslation((index % 9 - 4) * 2, 0, -(index / 9) * 3))).ToArray();
        Native3DScene original = new([], []) { Instances = new NativeInstanceSet3D(instances) };
        Native3DScene changed = original with
        {
            Instances = new NativeInstanceSet3D(instances.Select(instance => instance with
            {
                Material = instance.Material with { Slot = "changed-" + instance.Material.Slot,
                    BaseColor = new(.7f, .8f, .9f, 1), Roughness = .35f },
                Transform = instance.Transform * Matrix4x4.CreateTranslation(.03f, 0, 0),
            })),
        };
        Vector3 eye = new(1, 3, 9);
        Matrix4x4 camera = Camera(eye, new(0, 1, -8));
        var clear = new NativeFrameClearColor(.08f, .1f, .14f, 1);
        var cases = new List<object>();
        foreach (Native3DScene scene in new[] { original, changed, original })
        {
            renderer.EnableBindlessMaterials = false;
            renderer.ResetTemporalHistory();
            var reference = renderer.Render(scene, camera, eye, clear, capture: true);
            renderer.EnableBindlessMaterials = true;
            renderer.ResetTemporalHistory();
            var actual = renderer.Render(scene, camera, eye, clear, capture: true);
            Require(reference.PixelSha256 == actual.PixelSha256, "Mixed mesh/material/culling/TAA capture differs.");
            Require(actual.Retained is { MaterialDescriptorBinds: 1, IndirectGroups: > 1, BindlessMaterials: true },
                "Mixed meshes did not use nonzero-instance-offset indirect bindless groups.");
            foreach (var group in renderer.ReadVisibilityForQualification())
            {
                Require(group.CpuVisible.Select((visible, index) => !visible || group.Ranks[index] > 0).All(value => value),
                    "Bindless regrouping lost a CPU-visible cluster.");
            }
            cases.Add(new { Reference = reference.PixelSha256, Actual = actual.PixelSha256, actual.Retained });
        }
        renderer.Render(original, camera, eye, clear);
        renderer.Render(changed, camera, eye, clear);
        var final = renderer.Render(original, camera, eye, clear, capture: true);
        renderer.EnableBindlessMaterials = false;
        renderer.ResetTemporalHistory();
        var control = renderer.Render(original, camera, eye, clear, capture: true);
        Require(final.PixelSha256 == control.PixelSha256, "In-flight descriptor/material retirement changed pixels.");
        NativeGameGraphics.WritePng(Path.Combine(output, "mixed-mesh-culling.png"), Width, Height, final.Pixels!);
        cases.Add(new { InFlightRevision = final.PixelSha256, Reference = control.PixelSha256 });
        return cases;
    }

    internal static ModelMaterial[] Materials(int count)
    {
        ModelTexture normal = Texture("normal", 7, normal: true);
        ModelTexture metal = Texture("metal", 23);
        ModelTexture occlusion = Texture("occlusion", 43);
        return Enumerable.Range(0, count).Select(index =>
        {
            var sampler = new ModelSampler(index % 3 != 0, index % 3 != 0,
                (TextureWrap)(index % 3), (TextureWrap)((index / 3) % 3));
            bool subsurface = index % 8 == 0;
            return new ModelMaterial("material-" + index)
            {
                Metallic = subsurface ? 0 : index % 4 * .2f, Roughness = .25f + index % 5 * .12f,
                Emissive = new(.03f, .015f, .02f), AlphaMask = index % 7 == 0, DoubleSided = index % 2 == 0,
                SubsurfaceStrength = subsurface ? .2f : 0,
                BaseColorTexture = new(Texture("base-" + index, index, alpha: true), sampler),
                EmissiveTexture = new(Texture("emission-" + index, index + 211), sampler),
                NormalTexture = new(normal, sampler), MetallicRoughnessTexture = new(metal, sampler),
                OcclusionTexture = new(occlusion, sampler), NormalScale = .4f,
            };
        }).ToArray();
    }

    private static ModelTexture Texture(string name, int seed, bool normal = false, bool alpha = false)
    {
        var bytes = new byte[16 * 16 * 4];
        for (int index = 0; index < 256; index++)
        {
            bytes[index * 4] = normal ? (byte)(120 + index % 17) : (byte)(35 + (seed * 17 + index * 13) % 200);
            bytes[index * 4 + 1] = normal ? (byte)(120 + index % 13) : (byte)(35 + (seed * 23 + index * 7) % 200);
            bytes[index * 4 + 2] = normal ? (byte)250 : (byte)(35 + (seed * 31 + index * 3) % 200);
            bytes[index * 4 + 3] = alpha && index % 7 == 0 ? (byte)0 : (byte)255;
        }
        return new(name, 16, 16, bytes.ToImmutableArray());
    }

    internal static Matrix4x4 Camera(Vector3 eye, Vector3 target)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)Width / Height, .1f, 95);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
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
