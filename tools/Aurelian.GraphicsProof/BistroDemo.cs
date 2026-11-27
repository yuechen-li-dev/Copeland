using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Assets;
using Aurelian.Assets.Models;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

internal static class BistroDemo
{
    private sealed record Stop(string Name, Vector3 Eye, Vector3 Look, float FieldOfView = 1.02f);

    // World coordinates after the original FBX has been converted to glTF Y-up metres.
    private static readonly Stop[] Route =
    [
        new("entrance", new(1.05f, 1.98f, -1.9f), new(6.8f, 1.82f, -1.5f)),
        new("dining-room", new(4.45f, 1.98f, -2.8f), new(7.1f, 1.65f, -.2f)),
        new("back-room", new(5.4f, 1.98f, -6.9f), new(4.1f, 1.65f, -9.8f)),
    ];

    private static readonly Stop ReferenceAngle =
        new("reference-angle", new(-.12f, 2.45f, -1.47f), new(6.85f, 1.89f, -1.18f), .47f);

    private static bool CompareAlphaGlass => Environment.GetEnvironmentVariable("AURELIAN_BISTRO_ALPHA_GLASS") == "1";

    // Keep warm light around the fixtures and use cooler fill near the
    // windows to pick out the table edges and chair backs.
    private static readonly LocalLight3D[] Lights =
    [
        Pendant(new(4.991f, 2.89f, -.489f), 7.2f, new(1, .85f, .69f)),
        Pendant(new(7.989f, 2.94f, 1.015f), 6.3f, new(1, .86f, .73f)),
        Pendant(new(4.263f, 2.95f, -4.920f), 6.1f, new(1, .80f, .62f)),
        Pendant(new(6.107f, 2.80f, -8.468f), 3.2f, new(1, .85f, .72f)),
        BarGlobe(new(7.225f, 2.48f, -2.785f)),
        BarGlobe(new(8.795f, 2.48f, -1.92f)),
        BarGlobe(new(10.37f, 2.48f, -1.055f)),
        BarGlobe(new(6.625f, 2.48f, -4.21f)),
        new(new(3.75f, 2.14f, 2.65f), new(.76f, .88f, 1), 8.2f, 6),
        new(new(6.65f, 2.26f, 3.44f), new(.78f, .89f, 1), 6.2f, 5),
        new(new(.92f, 2.25f, -1.3f), new(.77f, .86f, 1), 2.4f, 5),
        new(new(3.33f, 2.46f, 1.85f), new(1, .83f, .65f), 1.1f, 3),
        new(new(4.62f, 2.55f, -7.7f), new(1, .86f, .72f), 1.5f, 3.8f),
    ];

    public static void Run(string manifestPath, string output, bool play, bool raster, bool scaled)
    {
        var scene = Import(manifestPath);
        Directory.CreateDirectory(output);
        var options = WindowOptions.DefaultVulkan;
        options.Size = new Vector2D<int>(1280, 720);
        options.Title = "Aurelian | Bistro after dark";
        options.IsVisible = play;
        options.VSync = play;
        using var window = Window.Create(options);
        window.Initialize();
        var assets = new GameAssets();
        var scale = !raster && scaled ? RenderScale3D.TwoThirds : RenderScale3D.Native;
        using var graphics = new NativeGameGraphics(window, options.Title, play, assets, enableRayQueries: !raster,
            vSync: play, renderScale: scale, enableEyeExposure: !raster, enableOcularGlare: !raster);
        using var validation = new RenderingR0Validation(graphics.Plant);
        Configure(graphics, assets, raster);
        if (play)
        {
            Walk(window, graphics, scene);
        }
        else
        {
            Capture(window, graphics, scene, output, raster);
        }
        graphics.Renderer.WaitForPendingFrames();
        validation.RequireClean();
        Console.WriteLine("AURELIAN_BISTRO_PASSED " + output);
    }

    private static Native3DScene Import(string manifestPath)
    {
        var catalog = ModelAssetCatalog.Load(manifestPath);
        var manifest = AssetManifestParser.Parse(File.ReadAllText(manifestPath), manifestPath).Manifest!;
        var models = manifest.Models.Select(record =>
        {
            var slot = catalog.Model(record.Id);
            var materials = slot.Current.Occurrences.Select(occurrence => occurrence.Primitive.Material)
                .DistinctBy(material => material.Slot)
                .Select(TuneMaterial)
                .OfType<ModelMaterial>()
                .ToImmutableDictionary(material => material.Slot);
            return new PlacedSceneModel(record.Id, Matrix4x4.Identity, slot, slot.Current.ContentIdentity, materials);
        }).ToImmutableArray();
        var frame = new SceneFrame([], []) { Models = models };
        var scene = SceneGeometry3D.BuildScene(frame, new SceneGeometry3DCache());
        // Keep the authentic cutout silhouettes in raster. Alpha-tested ray hits are a separate capability.
        var resident = new NativeInstanceSet3D(scene.Instances.Select(instance => instance.Material.AlphaMask
            ? instance with { RayMask = 0 } : instance));
        int translucentVertices = scene.Models.Sum(batch => batch.Vertices.Length);
        Console.WriteLine($"Bistro imported {resident.Count} retained occurrences and {scene.Models.Count} translucent batches ({translucentVertices} vertices).");
        return scene with { Instances = resident };
    }

    private static ModelMaterial? TuneMaterial(ModelMaterial material)
    {
        switch (material.Slot)
        {
            case "TransparentGlass":
            case "TransparentGlass.DoubleSided":
                return CompareAlphaGlass ? material with
                {
                    BaseColor = new(.27f, .31f, .33f, .085f),
                    BaseColorTexture = null,
                    Metallic = 0,
                    MetallicRoughnessTexture = null,
                    Roughness = .085f,
                    NormalScale = .35f,
                    SpecularAntiAliasing = true,
                } : material with
                {
                    AlphaBlend = false,
                    BaseColor = new(.99f, .995f, 1, 1),
                    BaseColorTexture = null,
                    Metallic = 0,
                    MetallicRoughnessTexture = null,
                    Transmission = 1,
                    IndexOfRefraction = 1.55f,
                    // Start the screen trace just beyond the glass wall so
                    // wine immediately behind it stays visible.
                    Thickness = .0035f,
                    Roughness = .075f,
                    NormalScale = .3f,
                    SpecularAntiAliasing = true,
                };
            case "TransparentGlassWine":
                if (!CompareAlphaGlass)
                {
                    return material with
                    {
                        AlphaBlend = false,
                        BaseColor = new(.75f, .86f, .53f, 1),
                        BaseColorTexture = null,
                        Metallic = 0,
                        MetallicRoughnessTexture = null,
                        Transmission = 1,
                        IndexOfRefraction = 1.55f,
                        Thickness = .0055f,
                        AttenuationColor = new(.32f, .59f, .14f),
                        AttenuationDistance = .011f,
                        Roughness = .11f,
                        NormalScale = .4f,
                    };
                }
                return material with
                {
                    BaseColor = new(.14f, .19f, .035f, .34f),
                    BaseColorTexture = null,
                    Metallic = 0,
                    MetallicRoughnessTexture = null,
                    Roughness = .135f,
                    NormalScale = .4f,
                    SpecularAntiAliasing = true,
                };
            case "Red_Wine":
            case "White_Wine":
            case "Beer":
                return material with { Metallic = 0, Roughness = .18f, SpecularAntiAliasing = true };
            case "MASTER_Interior_01_Floor_Tile_Hexagonal_BLENDSHADER":
                return material with
                {
                    Roughness = material.Roughness * .42f, NormalScale = .62f,
                    Clearcoat = new(.24f, .24f), SpecularAntiAliasing = true,
                };
            case "Paris_Table_03":
            case "MASTER_Wood_Painted3":
            case "MASTER_Wood_Painted3.001":
                return material with
                {
                    Roughness = material.Roughness * .81f, NormalScale = .8f,
                    Clearcoat = new(.24f, .34f), SpecularAntiAliasing = true,
                };
            case "Plates_Ceramic":
                return material with { NormalScale = .5f, Clearcoat = new(.3f, .22f), SpecularAntiAliasing = true };
            case "MASTER_Interior_01_Plaster_Red":
                return material with { BaseColor = new(.84f, .91f, .94f, 1), NormalScale = .75f };
            case "MASTER_Interior_01_Plaster":
            case "MASTER_Interior_01_Plaster2":
                return material with { NormalScale = .64f };
            case "Cutlery_chrome":
            case "Metal_Chrome1":
                return material with { NormalScale = .45f, SpecularAntiAliasing = true };
            default:
                return null;
        }
    }

    private static void Configure(NativeGameGraphics graphics, GameAssets assets, bool raster)
    {
        var renderer = graphics.Renderer;
        graphics.Clear = new(.008f, .011f, .017f, 1);
        graphics.EnableAdaptiveVisibility = false;
        renderer.EnableVisibilityBuffer = !raster;
        renderer.EnableRayShadows = !raster;
        renderer.VisibilityMode = ClusterVisibilityMode.Unculled;
        renderer.StaticEmissionIntensity = .68f;
        graphics.LocalLights = raster ? Lights.Select(light => light with { CastShadows = false }).ToArray() : Lights;
        graphics.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, SunIntensity = 0, LocalShadowBudget = 4,
            SkyAmbient = raster ? new(.045f, .048f, .055f) : Vector3.Zero,
            GroundAmbient = raster ? new(.028f, .024f, .019f) : Vector3.Zero,
            EnvironmentIntensity = 1, AmbientOcclusionStrength = .42f, AmbientOcclusionRadius = .42f,
            Exposure = 1, ToneMapColorRetention = .66f, ToneMapGamutCompression = true,
            BloomIntensity = raster ? .07f : 0,
            GlossyReflections = raster ? GlossyReflectionMode3D.Off : GlossyReflectionMode3D.FullResolution,
            RefractionTraceDistance = 8,
            EyeExposure = raster ? null : new()
            {
                CompensationEv = -.12f, MinimumEv = -2, MaximumEv = 1.05f,
                LowLight = new() { Strength = .18f },
            },
            OcularGlare = raster ? null : new() { Strength = .24f },
        };
        if (raster) return;

        var source = new EnvironmentRadiance3D(1, 1, [.047f, .05f, .056f, 1]);
        renderer.EnvironmentSource = source;
        graphics.Environment = VulkanEnvironmentCompiler.Compile(graphics.Plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        renderer.DynamicDiffuse = new()
        {
            Mode = DynamicDiffuseMode3D.Runtime,
            Grid = new(new(-1.3f, .48f, -12), new(11.8f, 3.4f, 4.0f), 9, 3, 11, 128),
            RaysPerProbePerFrame = 20,
            SecondarySkyLighting = false,
            Intensity = 1.55f,
        };
    }

    private static void Capture(IWindow window, NativeGameGraphics graphics, Native3DScene scene, string output, bool raster)
    {
        var captures = new List<object>();
        ulong frame = 0;
        for (int stopIndex = 0; stopIndex <= Route.Length; stopIndex++)
        {
            bool referenceAngle = stopIndex == Route.Length;
            Stop stop = referenceAngle ? ReferenceAngle : Route[stopIndex];
            if (referenceAngle) graphics.Renderer.ResetTemporalHistory();
            var times = new List<double>();
            for (int index = 0; index < 49; index++)
            {
                float position = stopIndex == 0 ? 0 : stopIndex - 1 + Math.Min(index / 24f, 1);
                var (eye, look) = referenceAngle ? (stop.Eye, stop.Look) : PoseAt(position);
                bool capture = index == 48;
                window.DoEvents();
                var result = graphics.Render(scene, View(graphics, eye, look, stop.FieldOfView), eye, capture);
                if (!graphics.Present(++frame)) throw new InvalidOperationException("Bistro lost its Vulkan swapchain.");
                if (index >= 28 && !capture && result.CompletedGpuFrame is { } complete)
                    times.Add(complete.ElapsedMilliseconds);
                if (capture)
                {
                    var file = stop.Name + ".png";
                    NativeGameGraphics.WritePng(Path.Combine(output, file), graphics.Width, graphics.Height, result.Pixels!);
                    captures.Add(new { File = file, result.PixelSha256, result.TriangleCount,
                        GpuMedianMs = times.Order().ElementAt(times.Count / 2), Passes = result.GpuPassTimes,
                        RayScene = graphics.Renderer.RaySceneStatistics });
                    Console.WriteLine($"Captured {file}.");
                }
            }
        }
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Device = graphics.Plant.Facts.PhysicalDeviceName, RasterControl = raster,
            graphics.Width, graphics.Height, graphics.Renderer.RenderWidth, graphics.Renderer.RenderHeight,
            Sampling = graphics.Renderer.LocalLightSampling.Mode.ToString(),
            Visibility = graphics.Renderer.VisibilityMode.ToString(),
            GlossyReflections = graphics.Settings.GlossyReflections.ToString(),
            Captures = captures,
            RetainedOccurrences = scene.Instances.Count,
            RayExcludedCutouts = scene.Instances.Count(instance => instance.Material.AlphaMask),
            TranslucentBatches = scene.Models.Count,
            TranslucentVertices = scene.Models.Sum(batch => batch.Vertices.Length),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Walk(IWindow window, NativeGameGraphics graphics, Native3DScene scene)
    {
        using var input = window.CreateInput();
        Console.WriteLine("W/S walk the route. A/D or left/right look. Up/down tilt. R reset. Esc close.");
        var clock = Stopwatch.StartNew();
        double prior = 0;
        ulong frame = 0;
        float position = 0, yaw = 0, pitch = 0;
        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) =>
            {
                if (key != Key.R) return;
                (position, yaw, pitch) = (0, 0, 0);
                graphics.Renderer.ResetTemporalHistory();
            };
        }
        while (!window.IsClosing)
        {
            window.DoEvents();
            if (window.IsClosing) break;
            double now = clock.Elapsed.TotalSeconds;
            float delta = (float)Math.Clamp(now - prior, 0, .08);
            prior = now;
            foreach (var keyboard in input.Keyboards)
            {
                if (keyboard.IsKeyPressed(Key.Escape)) window.Close();
                if (keyboard.IsKeyPressed(Key.W)) position += delta * .38f;
                if (keyboard.IsKeyPressed(Key.S)) position -= delta * .38f;
                if (keyboard.IsKeyPressed(Key.A) || keyboard.IsKeyPressed(Key.Left)) yaw -= delta * 1.55f;
                if (keyboard.IsKeyPressed(Key.D) || keyboard.IsKeyPressed(Key.Right)) yaw += delta * 1.55f;
                if (keyboard.IsKeyPressed(Key.Up)) pitch += delta;
                if (keyboard.IsKeyPressed(Key.Down)) pitch -= delta;
            }
            if (window.IsClosing) break;
            position = Math.Clamp(position, 0, Route.Length - 1);
            pitch = Math.Clamp(pitch, -1.1f, 1.1f);
            var (eye, look) = PoseAt(position);
            var forward = Vector3.Normalize(look - eye);
            float heading = MathF.Atan2(forward.X, forward.Z) + yaw;
            float tilt = Math.Clamp(MathF.Asin(forward.Y) + pitch, -1.35f, 1.35f);
            Vector3 direction = new(MathF.Sin(heading) * MathF.Cos(tilt), MathF.Sin(tilt), MathF.Cos(heading) * MathF.Cos(tilt));
            var size = window.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0) continue;
            graphics.Resize(size.X, size.Y);
            graphics.Render(scene, View(graphics, eye, eye + direction), eye);
            graphics.Present(++frame);
        }
    }

    private static (Vector3 Eye, Vector3 Look) PoseAt(float position)
    {
        int segment = Math.Min((int)position, Route.Length - 2);
        float t = position - segment;
        return (Vector3.Lerp(Route[segment].Eye, Route[segment + 1].Eye, t),
            Vector3.Lerp(Route[segment].Look, Route[segment + 1].Look, t));
    }

    private static Matrix4x4 View(NativeGameGraphics graphics, Vector3 eye, Vector3 look, float fieldOfView = 1.02f)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fieldOfView, (float)graphics.Width / graphics.Height, .07f, 45);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, look, Vector3.UnitY) * projection;
    }

    private static LocalLight3D Pendant(Vector3 location, float intensity, Vector3 color)
        => new(location, color, intensity, 5.6f)
        {
            Kind = LocalLightKind.Spot, Direction = -Vector3.UnitY, InnerAngle = .45f, OuterAngle = 1.40f, CastShadows = true,
        };

    private static LocalLight3D BarGlobe(Vector3 location)
        => new(location, new(1, .82f, .55f), 2.6f, 3.2f);
}
