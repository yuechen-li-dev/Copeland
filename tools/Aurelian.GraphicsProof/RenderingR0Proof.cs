using System.Diagnostics;
using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Beacon3D;
using Aurelian.GameMenus;
using Cloudstep;
using Silk.NET.Maths;
using Silk.NET.Windowing;

internal static class RenderingR0Proof
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian rendering R0"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, 320, 240);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"),
            surfacePrograms: new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")),
            retainedPrograms: new(assets.Shader("InstancedModel3D.v.ts"), assets.Shader("InstancedShadow3D.v.ts")));
        renderer.Settings = Graphics3DSettings.Default with { AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0 };
        NativeMesh3D mesh = Triangle(1);
        var material = new ModelMaterial("r0") { Metallic = 0, BaseColor = new(.6f, .3f, .1f, 1) };
        SceneInstance3D[] single = [new("one", mesh, material, Matrix4x4.Identity)];
        Vector3 eye = new(0, 0, 3);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(.9f, 4f / 3, .1f, 100);
        projection.M22 *= -1;
        Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * projection;
        var clear = new NativeFrameClearColor(.02f, .03f, .05f, 1);
        var legacy = new Native3DScene([], single.Select(instance => instance.Expand()).ToArray());
        var retained = new Native3DScene([], []) { Instances = single };
        Native3DFrameResult original = renderer.Render(legacy, camera, eye, clear, capture: true);
        Native3DFrameResult indexed = renderer.Render(retained, camera, eye, clear, capture: true);
        Require(original.Pixels!.SequenceEqual(indexed.Pixels!), "Identity instance changed captured pixels.");
        NativeGameGraphics.WritePng(Path.Combine(output, "indexed.png"), 320, 240, indexed.Pixels!);
        CheckSharedIndices(renderer, camera, eye, clear, output);
        renderer.FrameMode = Native3DFrameMode.Interactive;
        for (int frame = 0; frame < 12; frame++)
        {
            renderer.Render(retained, camera, eye, clear);
        }
        var pipelined = renderer.Render(retained, camera, eye, clear, capture: true);
        Require(indexed.Pixels!.SequenceEqual(pipelined.Pixels!), "Pipelining changed the frozen scene.");
        CheckMotion(renderer, mesh, material, camera, eye, clear, output);
        CheckChangingResources(renderer, mesh, material, camera, eye, clear);
        var measurements = new List<object>();
        foreach (int repetitions in new[] { 12, 48 })
        {
            var repeatedMesh = Triangle(repetitions);
            var many = Enumerable.Range(0, 5000).Select(index => new SceneInstance3D("instance-" + index,
                repeatedMesh, material, Matrix4x4.CreateScale(.02f)
                    * Matrix4x4.CreateTranslation((index % 100 - 50) * .025f, (index / 100 - 25) * .025f, 0))).ToArray();
            Native3DScene scene = new([], []) { Instances = many };
            renderer.Render(scene, camera, eye, clear, capture: true);
            var samples = new List<double>();
            var recordSamples = new List<double>();
            Native3DFrameResult result = null!;
            for (int frame = 0; frame < 40; frame++)
            {
                long start = Stopwatch.GetTimestamp();
                result = renderer.Render(scene, camera, eye, clear);
                if (frame >= 8)
                {
                    samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    recordSamples.Add(result.CpuTimings!.PrepareRecordMilliseconds);
                }
                Require(result.Retained is { UploadedMeshes: 0, GeometryUploadBytes: 0, DrawGroups: 1, Instances: 5000 },
                    "Steady retained frames must upload transforms only and issue one material/mesh draw group.");
            }
            renderer.WaitForPendingFrames();
            samples.Sort();
            recordSamples.Sort();
            measurements.Add(new { Repetitions = repetitions, VerticesPerMesh = repeatedMesh.Vertices.Length,
                MedianCpuMilliseconds = samples[samples.Count / 2],
                MedianPrepareRecordMilliseconds = recordSamples[recordSamples.Count / 2], result.Retained });
            measurements.Add(MeasureLegacy(renderer, many, camera, eye, clear, repetitions));
        }
        var nativeHost = MeasureNativeHosts(assets, output);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            DriverVersion = plant.Facts.DriverVersion,
            RuntimeTieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            ValidationLayers = plant.Facts.EnabledValidationLayers,
            LegacyHash = original.PixelSha256,
            IndexedHash = indexed.PixelSha256,
            InteractiveHash = pipelined.PixelSha256,
            Measurements = measurements,
            NativeHosts = nativeHost,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_RENDERING_R0_PASSED " + evidence);
    }

    private static void CheckSharedIndices(VulkanSolid3DRenderer renderer, Matrix4x4 camera,
        Vector3 eye, NativeFrameClearColor clear, string output)
    {
        NativeModel3DVertex[] vertices =
        [
            new(new(-.8f, -.8f, 0), Vector3.UnitZ, Vector4.One, new(0, 0), new(1, 0, 0, 1)),
            new(new(.8f, -.8f, 0), Vector3.UnitZ, Vector4.One, new(1, 0), new(1, 0, 0, 1)),
            new(new(.8f, .8f, 0), Vector3.UnitZ, Vector4.One, new(1, 1), new(1, 0, 0, 1)),
            new(new(-.8f, .8f, 0), Vector3.UnitZ, Vector4.One, new(0, 1), new(1, 0, 0, 1)),
        ];
        var mesh = new NativeMesh3D("indexed-quad", vertices, [0, 1, 2, 0, 2, 3]);
        var texture = new ModelTexture("r0-checker", 2, 2, ImmutableArray.Create<byte>(
            255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255));
        var material = new ModelMaterial("indexed-texture")
        {
            Metallic = 0,
            Unlit = true,
            BaseColorTexture = new(texture, new()),
        };
        var instance = new SceneInstance3D("quad", mesh, material, Matrix4x4.Identity);
        var expanded = renderer.Render(new Native3DScene([], [instance.Expand()]), camera, eye, clear, capture: true);
        var indexed = renderer.Render(new Native3DScene([], []) { Instances = [instance] }, camera, eye, clear, capture: true);
        Require(expanded.Pixels!.SequenceEqual(indexed.Pixels!), "Shared indices or textured UVs changed the legacy image.");
        int centre = (120 * 320 + 160) * 4;
        Require(indexed.Pixels![centre] > 25 || indexed.Pixels[centre + 1] > 25 || indexed.Pixels[centre + 2] > 25,
            "The shared-index specimen did not render its visible textured surface.");
        NativeGameGraphics.WritePng(Path.Combine(output, "shared-index-texture.png"), 320, 240, indexed.Pixels);
    }

    private static object MeasureLegacy(VulkanSolid3DRenderer renderer, SceneInstance3D[] instances,
        Matrix4x4 camera, Vector3 eye, NativeFrameClearColor clear, int repetitions)
    {
        renderer.FrameMode = Native3DFrameMode.Capture;
        var samples = new List<double>();
        for (int frame = 0; frame < 12; frame++)
        {
            long start = Stopwatch.GetTimestamp();
            var scene = new Native3DScene([], instances.Select(instance => instance.Expand()).ToArray());
            renderer.Render(scene, camera, eye, clear);
            if (frame >= 4)
            {
                samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
        }
        samples.Sort();
        renderer.FrameMode = Native3DFrameMode.Interactive;
        return new
        {
            Control = "Legacy CPU expansion, validation, upload and per-batch draws",
            Repetitions = repetitions,
            MedianCompletedCpuFrameMilliseconds = samples[samples.Count / 2],
            ExpandedGeometryBytesPerFrame = (long)instances.Length * instances[0].Mesh.Indices.Length * 64,
        };
    }

    private static void CheckChangingResources(VulkanSolid3DRenderer renderer, NativeMesh3D mesh,
        ModelMaterial material, Matrix4x4 camera, Vector3 eye, NativeFrameClearColor clear)
    {
        byte[]? control = null;
        foreach (Native3DFrameMode mode in Enum.GetValues<Native3DFrameMode>())
        {
            renderer.FrameMode = mode;
            renderer.ResetTemporalHistory();
            for (int frame = 0; frame < 24; frame++)
            {
                var changed = material with { Roughness = .2f + frame * .02f };
                renderer.LocalLights = [new(new(MathF.Sin(frame * .1f), 1, 2), Vector3.One, 8, 6)];
                var scene = new Native3DScene([], []) { Instances = [new("changing", mesh, changed, Matrix4x4.Identity)] };
                var result = renderer.Render(scene, camera, eye, clear, capture: frame == 23);
                if (frame != 23)
                {
                    continue;
                }
                if (control is null)
                {
                    control = result.Pixels!;
                }
                else
                {
                    Require(control.SequenceEqual(result.Pixels!), "Dynamic lights/material retirement changed pipelined output.");
                }
            }
        }
        renderer.LocalLights = [];
    }

    private static void CheckMotion(VulkanSolid3DRenderer renderer, NativeMesh3D mesh, ModelMaterial material,
        Matrix4x4 camera, Vector3 eye, NativeFrameClearColor clear, string output)
    {
        renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.Temporal, BloomIntensity = .05f };
        byte[]? control = null;
        foreach (Native3DFrameMode mode in Enum.GetValues<Native3DFrameMode>())
        {
            renderer.FrameMode = mode;
            renderer.ResetTemporalHistory();
            for (int frame = 0; frame < 32; frame++)
            {
                var transform = Matrix4x4.CreateScale(.8f, 1.1f, 1) * Matrix4x4.CreateRotationZ(frame * .01f)
                    * Matrix4x4.CreateTranslation(MathF.Sin(frame * .1f) * .2f, 0, 0);
                var instance = new SceneInstance3D("moving", mesh, material, transform);
                var scene = new Native3DScene([], []) { Instances = [instance] };
                var result = renderer.Render(scene, camera, eye, clear, capture: frame == 31);
                if (frame == 31)
                {
                    if (control is null)
                    {
                        control = result.Pixels!;
                    }
                    else
                    {
                        Require(control.SequenceEqual(result.Pixels!), "Animated TAA/bloom differs between serial and interactive frames.");
                    }
                    NativeGameGraphics.WritePng(Path.Combine(output, "motion-" + mode + ".png"), 320, 240, result.Pixels!);
                }
            }
        }
        renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0 };
    }

    private static IReadOnlyList<object> MeasureNativeHosts(GameAssets assets, string output)
    {
        var results = new List<object>();
        foreach (string name in new[] { "Beacon", "Cloudstep" })
        {
            foreach (bool interactive in new[] { false, true })
            {
                IGame game = name == "Beacon" ? new BeaconShellGame() : new CloudstepGame();
                try
                {
                    var options = WindowOptions.DefaultVulkan;
                    options.Size = new Vector2D<int>(1280, 720);
                    options.IsVisible = false;
                    options.ShouldSwapAutomatically = false;
                    options.Title = "Aurelian R0 " + name;
                    using var window = Window.Create(options);
                    window.Initialize();
                    using var graphics = new NativeGameGraphics(window, options.Title, false, assets,
                        vSync: false, enableFramesInFlight: interactive);
                    using var hostValidation = new RenderingR0Validation(graphics.Plant);
                    graphics.Settings = game.Manifest.Graphics;
                    graphics.Menus.Scale = Math.Min(graphics.Width / 960.0, graphics.Height / 600.0);
                    var menu = new GameMenuView(graphics.Font);
                    var page = new GameMenuPage("r0", name, "R0 frame pipeline", [new("continue", "CONTINUE")]);
                    var samples = new List<double>();
                    var record = new List<double>();
                    var submit = new List<double>();
                    var gpuFrames = new Dictionary<long, Native3DCompletedGpuFrame>();
                    Native3DFrameResult rendered = null!;
                    long started = Stopwatch.GetTimestamp();
                    const int count = 80;
                    for (int frame = 0; frame < count; frame++)
                    {
                        window.DoEvents();
                        long before = Stopwatch.GetTimestamp();
                        var view = game.View(new(1280, 720) { RetainedGeometry = interactive })!;
                        rendered = graphics.Render(view.Scene, view.Camera, view.Eye);
                        graphics.Menus.Render(menu, page, 0);
                        Require(graphics.Present((ulong)frame + 1), "Native presentation became out of date during the frozen benchmark.");
                        if (frame == 15)
                        {
                            started = Stopwatch.GetTimestamp();
                        }
                        if (frame >= 16)
                        {
                            samples.Add(Stopwatch.GetElapsedTime(before).TotalMilliseconds);
                            record.Add(rendered.CpuTimings!.PrepareRecordMilliseconds);
                            submit.Add(rendered.CpuTimings.SubmitMilliseconds);
                            if (rendered.CompletedGpuFrame is { } gpu)
                            {
                                gpuFrames[gpu.Sequence] = gpu;
                            }
                        }
                    }
                    graphics.Renderer.WaitForPendingFrames();
                    // Include the final queued scene and overlay/copy work in throughput.
                    graphics.Plant.Vk.DeviceWaitIdle(graphics.Plant.Device);
                    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var ordered = gpuFrames.Values.OrderBy(frame => frame.Sequence).ToArray();
                    var gaps = ordered.Zip(ordered.Skip(1), (left, right) =>
                        (right.StartTicks - left.EndTicks) * right.NanosecondsPerTick / 1_000_000.0).ToArray();
                    samples.Sort();
                    record.Sort();
                    submit.Sort();
                    Array.Sort(gaps);
                    results.Add(new
                    {
                        Game = name,
                        Interactive = interactive,
                        Width = 1280,
                        Height = 720,
                        Frames = count - 16,
                        MedianCpuFrameMilliseconds = samples[samples.Count / 2],
                        MedianPrepareRecordMilliseconds = record[record.Count / 2],
                        MedianSubmitMilliseconds = submit[submit.Count / 2],
                        MeanCompletedFrameMilliseconds = elapsed / (count - 16),
                        MedianGpuInterSceneGapMilliseconds = gaps[gaps.Length / 2],
                        GpuGapIncludes = "UI, copy, presentation and submission gaps between 3D scenes",
                        graphics.Presenter.PresentMode,
                        rendered.Retained,
                    });
                    // Exercise retirement while replacing the actual swapchain, renderer and UI resources.
                    window.Size = new Vector2D<int>(960, 540);
                    window.DoEvents();
                    Require(graphics.ResizeToWindow(), "Native resize did not rebuild the surface.");
                    graphics.Menus.Scale = Math.Min(graphics.Width / 960.0, graphics.Height / 600.0);
                    var resized = game.View(new(960, 540) { RetainedGeometry = interactive })!;
                    graphics.Render(resized.Scene, resized.Camera, resized.Eye);
                    var captured = graphics.Menus.Render(menu, page, 0, capture: true);
                    NativeGameGraphics.WritePng(Path.Combine(output, name + "-" + interactive + "-resized.png"),
                        graphics.Width, graphics.Height, captured.Pixels!);
                    Require(graphics.Present(1000), "Resized native presentation failed.");
                    graphics.Renderer.WaitForPendingFrames();
                    graphics.Plant.Vk.DeviceWaitIdle(graphics.Plant.Device);
                    hostValidation.RequireClean();
                }
                finally
                {
                    (game as IDisposable)?.Dispose();
                }
            }
        }
        return results;
    }

    private static NativeMesh3D Triangle(int repetitions)
    {
        var triangle = new NativeModel3DVertex[]
        {
            new(new(-.8f, -.7f, 0), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
            new(new(.8f, -.7f, 0), Vector3.UnitZ, Vector4.One, Vector2.UnitX, new(1, 0, 0, 1)),
            new(new(0, .8f, 0), Vector3.UnitZ, Vector4.One, Vector2.UnitY, new(1, 0, 0, 1)),
        };
        return new("triangle-" + repetitions, Enumerable.Range(0, repetitions).SelectMany(_ => triangle),
            Enumerable.Range(0, repetitions * 3).Select(index => (uint)index));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
