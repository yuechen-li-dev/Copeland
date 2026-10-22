using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Assets.Materials;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

/// <summary>A small native Vulkan weather gallery, using the shared particle and water paths.</summary>
internal static class RainNightDemo
{
    private const int Width = 1280;
    private const int Height = 800;
    private const float PuddleHeight = .025f;
    private static readonly Vector3 Eye = new(1.1f, 2.15f, 11);
    private static readonly Vector3 Look = new(-1.1f, 1.8f, -15);
    private static readonly NativeFrameClearColor Clear = new(.018f, .032f, .052f, 1);

    public static void BakeMaps(string output)
    {
        ExportMaps(output, CreateSurfaceMaps());
        Console.WriteLine("AURELIAN_SURFACE_MAPS_WRITTEN " + output);
    }

    public static void Run(string output, bool view, bool smoke)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        if (!view && !smoke)
        {
            File.WriteAllText(evidence, "{\"Accepted\":false}");
        }
        var assets = new GameAssets();
        SceneGroup street = CreateStreet(out IReadOnlyDictionary<string, SurfaceMapSet> surfaceMaps);
        ExportMaps(output, surfaceMaps);
        using var authored = SceneCompiler.Compile(street).Mount();
        Native3DScene scene = SceneGeometry3D.BuildScene(authored.Project()) with
        {
            WaterReflection = new(PuddleHeight),
        };
        if (view || smoke)
        {
            View(assets, scene, output, smoke);
            return;
        }

        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian Rain After Dark"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var particles = new VulkanParticles3D(plant, assets.ComputeShader("Particles3D.v.ts"),
            maximumEmitters: 2, particlesPerEmitter: 8192, enableQualificationReadback: true);
        using var renderer = CreateRenderer(plant, target, assets);
        Configure(renderer, particles);
        Matrix4x4 camera = Camera(Eye, Width, Height);
        Native3DScene still = AtTime(scene, 3);
        byte[] noRain = Capture("01-night-puddles", still, []);
        ParticleEmitter3D[] rain = Rain(3);
        byte[] rainy = Capture("02-rain-night", still, rain);
        int rainPixels = Changed(noRain, rainy);
        Require(rainPixels > 1000, "Rain did not visibly affect the native scene: " + rainPixels);
        NativeParticleVertex3D[] vertices = particles.ReadVerticesForQualification();
        var live = vertices.Where((vertex, index) => index % 6 == 0 && vertex.Color.W > 0).ToArray();
        Require(live.Length > 1000 && particles.Inspection.OverBudgetParticles == 0, "Rain did not fit its live GPU budget.");
        Require(vertices.All(vertex => float.IsFinite(vertex.Position.LengthSquared()) && float.IsFinite(vertex.Color.LengthSquared())),
            "Rain produced nonfinite vertices.");
        Require(live.All(vertex => vertex.CentreRadius.Y > 0), "A live drop survived below the floor.");
        Require(rainy.SequenceEqual(Capture("03-replay", still, rain)), "Identical rain commands did not replay exactly.");
        Require(vertices.SequenceEqual(particles.ReadVerticesForQualification()), "GPU rain vertices did not replay exactly.");

        Native3DScene flatNormals = still with
        {
            Models = still.Models.Select(batch => batch with { Material = batch.Material with { NormalTexture = null } }).ToArray(),
        };
        int normalPixels = Changed(rainy, Capture("17-flat-normals", flatNormals, rain));
        Require(normalPixels > 1000, "Generated normal maps did not visibly affect the native scene: " + normalPixels);
        Native3DScene constantRoughness = still with
        {
            Models = still.Models.Select(batch => batch.Material.MetallicRoughnessTexture is null ? batch : batch with
            {
                Material = batch.Material with { MetallicRoughnessTexture = null },
            }).ToArray(),
        };
        int roughnessPixels = Changed(rainy, Capture("18-constant-roughness", constantRoughness, rain));
        Require(roughnessPixels > 1000, "Generated roughness maps did not affect the native scene: " + roughnessPixels);
        Native3DScene noCavity = still with
        {
            Models = still.Models.Select(batch => batch with { Material = batch.Material with { OcclusionTexture = null } }).ToArray(),
        };
        int cavityPixels = Changed(rainy, Capture("19-no-cavity", noCavity, rain));
        Require(cavityPixels > 100, "Generated cavity maps did not affect ambient shading: " + cavityPixels);

        byte[] animated = Capture("04-animated", still, Rain(3.17f));
        int animatedPixels = Changed(rainy, animated);
        Require(animatedPixels > 1000, "Rain did not animate: " + animatedPixels);
        Capture("05-rewind", still, Rain(.4f));
        Require(rainy.SequenceEqual(Capture("06-restored", still, rain)), "Rain rewind retained simulation state.");
        var drained = rain.Select(item => item with { EmitUntilSeconds = .2f, ElapsedSeconds = 10 }).ToArray();
        Require(noRain.SequenceEqual(Capture("07-drained", still, drained)), "Stopped rain did not drain completely.");

        Native3DScene noReflection = still with { WaterReflection = null };
        byte[] unreflected = Capture("08-no-reflection", noReflection, []);
        int reflectionPixels = Changed(noRain, unreflected);
        Require(reflectionPixels > 1000, "Puddles did not reflect scene geometry: " + reflectionPixels);
        Native3DScene noRipples = still with { WaterFrame = new(3) };
        int ripplePixels = Changed(noRain, Capture("09-no-impacts", noRipples, []));
        Require(ripplePixels > 100, "Impact ripples did not affect the native puddles: " + ripplePixels);
        Native3DScene noPuddles = still with
        {
            Models = still.Models.Where(batch => batch.Material.Water is null).ToArray(),
            WaterReflection = null,
        };
        Capture("10-wet-pavement", noPuddles, []);

        // Compare native GPU geometry under two shutters. Wider streaks must preserve
        // opacity times area, rather than making longer exposures arbitrarily brighter.
        Capture("11-short-shutter", still, rain.Select(item => item with { RainShutterSeconds = .005f }).ToArray());
        NativeParticleVertex3D[] shortShutter = particles.ReadVerticesForQualification();
        Capture("12-long-shutter", still, rain.Select(item => item with { RainShutterSeconds = .05f }).ToArray());
        NativeParticleVertex3D[] longShutter = particles.ReadVerticesForQualification();
        int shutterSamples = 0;
        for (int first = 0; first < vertices.Length; first += 6)
        {
            if (shortShutter[first].Color.W <= 0 || longShutter[first].Color.W <= 0)
            {
                continue;
            }
            float shortLength = Vector3.Distance(shortShutter[first + 1].Position, shortShutter[first + 2].Position);
            float longLength = Vector3.Distance(longShutter[first + 1].Position, longShutter[first + 2].Position);
            Require(longLength > shortLength, "A longer shutter did not lengthen rain geometry.");
            float shortEnergy = shortLength * shortShutter[first].Color.W;
            float longEnergy = longLength * longShutter[first].Color.W;
            Require(Math.Abs(shortEnergy - longEnergy) < .00001f, "Shutter stretching failed the opacity-area invariant.");
            shutterSamples++;
        }
        Require(shutterSamples > 1000, "Too few GPU shutter samples.");

        Graphics3DSettings night = renderer.Settings;
        IReadOnlyList<LocalLight3D> streetLights = renderer.LocalLights;
        renderer.LocalLights = [];
        renderer.Settings = night with
        {
            SunIntensity = 0, SkyAmbient = Vector3.Zero, GroundAmbient = Vector3.Zero,
            Fog = new(), Volumetrics = new(), BloomIntensity = 0,
        };
        // The renderer requires geometry. An unilluminated backdrop supplies that
        // contract while giving the rain an exactly black reference field.
        Native3DScene empty = new(
        [
            new(new(-100, -100, -30), Vector3.UnitZ, Vector4.One),
            new(new(100, -100, -30), Vector3.UnitZ, Vector4.One),
            new(new(0, 100, -30), Vector3.UnitZ, Vector4.One),
        ], []);
        var black = new NativeFrameClearColor(0, 0, 0, 1);
        byte[] dark = Capture("13-dark-reference", empty, [], black);
        var unlitRain = rain.Select(item => item with { RainIncidentRadiance = Vector3.Zero }).ToArray();
        Require(dark.SequenceEqual(Capture("14-unlit-rain", empty, unlitRain, black)), "Unlit rain emitted light.");
        Require(Changed(dark, Capture("15-lit-rain", empty, rain, black)) > 1000, "Incident radiance did not illuminate rain.");

        // Readable qualification buffers may reside in host-visible memory. Measure
        // the GPU-only pool used by the window, after checking identical output.
        using var gpuOnly = new VulkanParticles3D(plant, assets.ComputeShader("Particles3D.v.ts"),
            maximumEmitters: 2, particlesPerEmitter: 8192);
        renderer.ParticleSystem = gpuOnly;
        renderer.Settings = night;
        renderer.LocalLights = streetLights;
        Require(rainy.SequenceEqual(Capture("16-gpu-only", still, rain)), "GPU-only rain differs from qualification output.");
        renderer.Settings = night with { AntiAliasing = AntiAliasing3D.Temporal };
        var measurements = new List<Native3DGpuPassTime>();
        for (int frame = 0; frame < 40; frame++)
        {
            float time = 3 + frame / 60f;
            renderer.ParticleEmitters = Rain(time);
            Vector3 movingEye = Eye + new Vector3(frame * .008f, 0, 0);
            var result = renderer.Render(AtTime(scene, time), Camera(movingEye, Width, Height), movingEye, Clear);
            if (frame >= 8)
            {
                measurements.AddRange(result.GpuPassTimes);
            }
        }
        renderer.ResetTemporalHistory();
        Native3DScene preview = AtTime(scene, 3.65f);
        renderer.ParticleEmitters = Rain(3.65f);
        for (int frame = 0; frame < 12; frame++)
        {
            renderer.Render(preview, camera, Eye, Clear);
        }
        Capture("rain-after-dark", preview, Rain(3.65f));
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            ValidationLayers = plant.Facts.EnabledValidationLayers,
            Width, Height,
            RainChangedPixels = rainPixels,
            AnimationChangedPixels = animatedPixels,
            ReflectionChangedPixels = reflectionPixels,
            ImpactChangedPixels = ripplePixels,
            NormalMapChangedPixels = normalPixels,
            RoughnessMapChangedPixels = roughnessPixels,
            CavityMapChangedPixels = cavityPixels,
            SurfaceMaps = surfaceMaps.Select(pair => new
            {
                Name = pair.Key, pair.Value.Normal.Width, pair.Value.Normal.Height,
                pair.Value.Normal.Identity, pair.Value.HeightMinimumMetres, pair.Value.HeightRangeMetres,
            }),
            AuthoredModelInstances = authored.Project().Models.Length,
            ModelTriangles = scene.Models.Sum(batch => batch.Vertices.Length / 3),
            LiveDrops = live.Length,
            ReplayExact = true,
            RewindExact = true,
            StoppedRainDrains = true,
            UnlitRainDoesNotEmit = true,
            GpuOnlyMatches = true,
            ShutterSamples = shutterSamples,
            Budget = gpuOnly.Inspection,
            WarmupFrames = 8,
            MeasuredFrames = 32,
            GpuPasses = measurements.GroupBy(item => item.Pass).ToDictionary(group => group.Key,
                group => Distribution(group.Select(item => item.Milliseconds))),
            Limits = new[] { "authored open-air rain volumes", "horizontal floor; no roof collision",
                "authored incident rain lighting; no per-drop local-light query", "one coplanar opaque reflection",
                "eight decorative authored water impacts; not particle collisions", "no glint-paper implementation" },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_RAIN_NIGHT_PASSED " + evidence);

        byte[] Capture(string name, Native3DScene input, ParticleEmitter3D[] emitters, NativeFrameClearColor? background = null)
        {
            renderer.ParticleEmitters = emitters;
            byte[] pixels = renderer.Render(input, camera, Eye, background ?? Clear, capture: true).Pixels!;
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), Width, Height, pixels);
            return pixels;
        }
    }

    private static VulkanSolid3DRenderer CreateRenderer(AurelianVulkanPlant plant, VulkanNativeFrameTarget target, GameAssets assets)
    {
        return new(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"),
            surfacePrograms: new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
            {
                HeightFog = assets.Shader("HeightFog3D.v.ts"),
                VolumeInject = assets.Shader("VolumeInject3D.v.ts"),
                VolumeIntegrate = assets.Shader("VolumeIntegrate3D.v.ts"),
                VolumeResolve = assets.Shader("VolumeResolve3D.v.ts"),
                RefractiveModel = assets.Shader("RefractiveModel3D.v.ts"),
                RefractionResolve = assets.Shader("RefractionResolve3D.v.ts"),
                WaterTemporal = assets.Shader("TemporalResolve3D.v.ts"),
                TransparentModel = assets.Shader("TransparentModel3D.v.ts"),
                TransparencyResolve = assets.Shader("TransparencyResolve3D.v.ts"),
                ParticleBillboards = assets.Shader("ParticleBillboard3D.v.ts"),
            });
    }

    private static void Configure(VulkanSolid3DRenderer renderer, VulkanParticles3D particles)
    {
        renderer.ParticleSystem = particles;
        renderer.Settings = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None,
            SunDirection = Vector3.Normalize(new(-.3f, .8f, -.5f)),
            SunIntensity = .12f,
            SunColor = new(.43f, .59f, .78f),
            SkyAmbient = new(.018f, .030f, .048f),
            GroundAmbient = new(.003f, .005f, .008f),
            BloomIntensity = .055f,
            BloomThreshold = 1.2f,
            Exposure = 1.35f,
            Fog = new() { Density = .018f, HeightFalloff = .06f, Color = new(.023f, .041f, .065f) },
            Volumetrics = new()
            {
                Enabled = true, PixelSize = 12, DepthSlices = 48, MaximumDistance = 75,
                AmbientRadiance = new(.06f, .10f, .16f), ScatteringAlbedo = new(.35f), Anisotropy = .2f,
            },
            LocalShadowBudget = 0,
        };
        renderer.LocalLights =
        [
            new(new(-3.35f, 3.45f, 4), new(1, .48f, .12f), 45, 11),
            new(new(-3.35f, 3.45f, -6), new(1, .48f, .12f), 34, 11),
            new(new(-3.35f, 3.45f, -16), new(1, .48f, .12f), 28, 11),
            new(new(-3.35f, 3.45f, -26), new(1, .48f, .12f), 22, 11),
            new(new(-4.2f, 1.6f, .8f), new(.09f, .55f, .46f), 8, 6),
            new(new(.5f, 1.8f, -37), new(1, .42f, .08f), 30, 9),
        ];
    }

    private static ParticleEmitter3D[] Rain(float time)
    {
        return
        [
            ParticleEmitter3D.Rain("lamp-rain", new(-2.1f, 12, 1), 773) with
            {
                ElapsedSeconds = time, Radius = 4, Rate = 1000, Size = .006f,
                Color = new(.9f, .93f, 1, .55f),
                RainIncidentRadiance = new(.45f, .23f, .075f),
            },
            ParticleEmitter3D.Rain("shop-rain", new(-2.5f, 12, 6), 991) with
            {
                ElapsedSeconds = time, Radius = 3.5f, Rate = 650, Size = .005f,
                Color = new(.9f, .93f, 1, .45f),
                RainIncidentRadiance = new(.06f, .22f, .20f),
            },
        ];
    }

    private static Native3DScene AtTime(Native3DScene scene, float time)
    {
        var impacts = ImmutableArray.CreateBuilder<WaterImpulse3D>(8);
        for (int index = 0; index < 8; index++)
        {
            // An explicit decorative schedule on the presentation clock, independent of particle simulation.
            float offset = index * .137f;
            float started = MathF.Floor((time - offset) / 1.1f) * 1.1f + offset;
            Vector2 centre = index < 4 ? new(.3f, 5.2f) : new(-2, -2);
            centre += new Vector2((index % 4 - 1.5f) * .45f, (index % 2 - .5f) * .65f);
            impacts.Add(new(centre, Math.Max(0, started), .007f, .20f, .65f, 1.1f));
        }
        return scene with { WaterFrame = new(time) { Impulses = impacts.MoveToImmutable() } };
    }

    private static SceneGroup CreateStreet(out IReadOnlyDictionary<string, SurfaceMapSet> surfaceMaps)
    {
        var nodes = new List<SceneNode>();
        IReadOnlyDictionary<string, SurfaceMapSet> maps = CreateSurfaceMaps();
        surfaceMaps = maps;
        var modelCache = new Dictionary<(Vector3 Size, ModelMaterial Material, float Bevel), ModelSlot>();
        ModelMaterial asphalt = maps["asphalt"].Material("wet-asphalt") with { Metallic = 0 };
        ModelMaterial concrete = maps["pavers"].Material("pavers") with { Metallic = 0 };
        var iron = new ModelMaterial("iron") { BaseColor = new(.012f, .016f, .019f, 1), Metallic = .65f, Roughness = .38f };
        var stone = new ModelMaterial("stone-trim")
        {
            BaseColor = new(.11f, .105f, .095f, 1), Metallic = 0, Roughness = .68f,
        };
        ModelMaterial brick = maps["masonry"].Material("masonry") with { Metallic = 0 };
        nodes.Add(Scene.Model("road", PrimitiveModels3D.Plane("road", new(13, 65), asphalt), new(0, -.04f, -15)));
        AddBox("left-walk", new(1.3f, .25f, 65), new(-4, .025f, -15), concrete, .025f);
        AddBox("right-walk", new(1.3f, .25f, 65), new(4, .025f, -15), concrete, .025f);
        for (int side = -1; side <= 1; side += 2)
        {
            for (int block = 0; block < 9; block++)
            {
                string id = $"building-{side}-{block}";
                float z = 8 - block * 5.2f;
                float height = 9 + block % 3 * .65f;
                AddBox(id, new(4, height, 5.1f), new(side * 6.7f, height / 2, z), brick);
                AddBox(id + "-plinth", new(.17f, .48f, 5.12f), new(side * 4.61f, .3f, z), stone, .025f);
                AddBox(id + "-cornice", new(.30f, .18f, 5.15f), new(side * 4.59f, height - .15f, z), stone, .02f);
                AddBox(id + "-belt", new(.14f, .13f, 5.12f), new(side * 4.6f, 3.15f, z), stone, .015f);
                nodes.Add(Scene.Model(id + "-pipe", PrimitiveModels3D.Cylinder(id + "-pipe", .0375f, height, iron, 12),
                    new(side * 4.48f, height / 2, z + 2.38f)));
                for (int row = 0; row < 3; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        string window = $"window-{side}-{block}-{row}-{column}";
                        float windowZ = z - 1.25f + column * 2.5f;
                        float windowY = 1.9f + row * 2.45f;
                        bool lit = (block * 3 + row + column + side + 1) % 5 == 1;
                        Vector3 glow = lit ? new(.72f, .30f, .075f) : new(.001f, .0025f, .004f);
                        AddBox(window + "-recess", new(.07f, 1.65f, 1.02f), new(side * 4.63f, windowY, windowZ), iron);
                        AddBox(window + "-glass", new(.045f, 1.42f, .82f), new(side * 4.57f, windowY, windowZ), Emissive("glass", glow));
                        AddBox(window + "-mullion", new(.08f, 1.48f, .045f), new(side * 4.53f, windowY, windowZ), iron);
                        AddBox(window + "-crossbar", new(.08f, .045f, .88f), new(side * 4.53f, windowY + .15f, windowZ), iron);
                        AddBox(window + "-sill", new(.25f, .11f, 1.18f), new(side * 4.53f, windowY - .82f, windowZ), stone, .015f);
                        AddBox(window + "-lintel", new(.16f, .12f, 1.14f), new(side * 4.58f, windowY + .87f, windowZ), stone, .015f);
                    }
                }
            }
            for (int slab = 0; slab < 38; slab++)
            {
                AddBox($"paving-joint-{side}-{slab}", new(1.28f, .005f, .016f),
                    new(side * 4, .154f, 13 - slab * 1.6f), iron);
                AddBox($"curb-joint-{side}-{slab}", new(.025f, .19f, .018f),
                    new(side * 3.35f, .04f, 13 - slab * 1.6f), iron);
            }
        }
        AddBox("end-building", new(13, 11, 2), new(0, 5.5f, -43), brick);
        AddBox("end-door-frame", new(1.6f, 3, .15f), new(.5f, 1.6f, -41.9f), stone);
        AddBox("end-door-light", new(1.2f, 2.6f, .12f), new(.5f, 1.6f, -41.8f), Emissive("end-light", new(1.6f, .56f, .09f)));
        AddBox("shop-frame", new(.15f, 2.65f, 3.8f), new(-4.5f, 1.55f, .8f), iron);
        AddBox("shop-glass", new(.08f, 2.3f, 3.4f), new(-4.39f, 1.55f, .8f), Emissive("shop-teal", new(.012f, .16f, .13f)));
        for (int bar = 0; bar < 4; bar++)
        {
            AddBox("shop-bar-" + bar, new(.13f, 2.45f, .065f), new(-4.32f, 1.55f, -.85f + bar * 1.1f), iron);
        }
        AddBox("shop-rail", new(.13f, .07f, 3.6f), new(-4.32f, 1.1f, .8f), iron);
        ModelSlot lampColumn = PrimitiveModels3D.Revolve("lantern-column",
        [
            new(.14f, 0), new(.14f, .08f), new(.10f, .18f), new(.10f, .28f),
            new(.06f, .34f), new(.045f, .5f), new(.035f, 2.75f), new(.08f, 2.83f), new(.08f, 2.94f),
        ], iron);
        ModelSlot lampRoof = PrimitiveModels3D.Cylinder("lantern-roof", .29f, .20f, iron, 8, topRadius: .045f);
        ModelSlot lampFinial = PrimitiveModels3D.Sphere("lantern-finial", .055f, iron, 16, 8);
        for (int index = 0; index < 5; index++)
        {
            float z = 4 - index * 10;
            nodes.Add(Scene.Model("lamp-column-" + index, lampColumn, new(-3.35f, .15f, z)));
            AddBox("lamp-bottom-" + index, new(.32f, .08f, .32f), new(-3.35f, 3.12f, z), iron);
            nodes.Add(Scene.Model("lamp-roof-" + index, lampRoof, new(-3.35f, 3.85f, z)));
            nodes.Add(Scene.Model("lamp-finial-" + index, lampFinial, new(-3.35f, 4.02f, z)));
            AddBox("lamp-glow-" + index, new(.25f, .57f, .25f), new(-3.35f, 3.45f, z), Emissive("lamp", new(8, 3.6f, .65f)));
            for (int corner = 0; corner < 4; corner++)
            {
                float x = -3.35f + (corner % 2 - .5f) * .29f;
                float lampZ = z + (corner / 2 - .5f) * .29f;
                AddBox($"lamp-rib-{index}-{corner}", new(.025f, .64f, .025f), new(x, 3.45f, lampZ), iron);
            }
        }
        var water = new WaterSurface3D
        {
            Waves =
            [
                new(new(1, .25f), .001f, .9f, .12f, .006f),
                new(new(-.4f, 1), .0006f, .6f, .08f, .004f),
            ],
            DetailAmplitude = .00075f, DetailWavelength = .14f,
            FoamStrength = 0, MaximumDepth = .1f, DeepColor = new(.012f, .018f, .026f),
        };
        nodes.Add(Puddle("near-puddle", new(5.8f, 7.8f), new(.2f, PuddleHeight, 4.8f), water));
        nodes.Add(Puddle("middle-puddle", new(3.1f, 4.8f), new(-1.6f, PuddleHeight, -2.4f), water));
        nodes.Add(Puddle("far-puddle", new(2.8f, 5.8f), new(1.2f, PuddleHeight, -9), water));
        return Scene.World("rain-after-dark", nodes);

        void AddBox(string id, Vector3 size, Vector3 position, ModelMaterial material, float bevel = 0)
        {
            if (material.NormalTexture is null && bevel == 0)
            {
                nodes.Add(Scene.Box(id, size, Vector4.One, position) with { Material = material });
                return;
            }
            var key = (size, material, bevel);
            if (!modelCache.TryGetValue(key, out ModelSlot? asset))
            {
                // These sub-pixel edge radii need one strip per side, rather than
                // the higher tessellation useful for larger authored rounded props.
                asset = PrimitiveModels3D.BeveledBox(id, size, bevel, material, bevelSegments: 1);
                modelCache.Add(key, asset);
            }
            nodes.Add(Scene.Model(id, asset, position));
        }
    }

    private static SceneMesh Puddle(string id, Vector2 size, Vector3 at, WaterSurface3D water)
    {
        SceneMesh patch = WaterSurfaceMesh3D.Create(id, new(2), water, 72, 72, at);
        return patch with
        {
            Vertices = patch.Vertices.Select(vertex =>
            {
                float x = vertex.Position.X;
                float z = vertex.Position.Z;
                float diskX = x * MathF.Sqrt(1 - z * z * .5f);
                float diskZ = z * MathF.Sqrt(1 - x * x * .5f);
                float angle = MathF.Atan2(diskZ, diskX);
                float edge = .91f + .06f * MathF.Sin(angle * 5) + .03f * MathF.Cos(angle * 9);
                return vertex with { Position = new(diskX * size.X * .5f * edge, 0, diskZ * size.Y * .5f * edge) };
            }).ToImmutableArray(),
            Material = water.Material(id) with { Roughness = .075f, AttenuationColor = new(.85f, .9f, .95f) },
        };
    }

    private static ModelMaterial Emissive(string name, Vector3 radiance)
    {
        return new(name) { BaseColor = new(.015f, .02f, .025f, 1), Metallic = 0, Roughness = .4f, Emissive = radiance };
    }

    private static IReadOnlyDictionary<string, SurfaceMapSet> CreateSurfaceMaps()
    {
        return new Dictionary<string, SurfaceMapSet>
        {
            ["asphalt"] = ProceduralSurfaceMaps.Asphalt("rain-asphalt", wetness: .85f, seed: 733),
            ["masonry"] = ProceduralSurfaceMaps.Masonry("rain-masonry", seed: 53),
            ["pavers"] = ProceduralSurfaceMaps.Pavers("rain-pavers", seed: 71),
        };
    }

    private static void ExportMaps(string output, IReadOnlyDictionary<string, SurfaceMapSet> surfaces)
    {
        string directory = Path.Combine(output, "materials");
        Directory.CreateDirectory(directory);
        foreach ((string name, SurfaceMapSet maps) in surfaces)
        {
            SurfaceMapFiles.Write(directory, name, maps);
        }
    }

    private static void View(GameAssets assets, Native3DScene scene, string output, bool smoke)
    {
        var options = WindowOptions.DefaultVulkan;
        options.Size = new Vector2D<int>(Width, Height);
        options.Title = "Rain After Dark | R rain, N normals, P puddles, Space pause, Esc close";
        options.IsVisible = !smoke;
        using var window = Window.Create(options);
        window.Initialize();
        using var input = window.CreateInput();
        using var graphics = new NativeGameGraphics(window, options.Title, true, assets);
        using var particles = new VulkanParticles3D(graphics.Plant, assets.ComputeShader("Particles3D.v.ts"),
            maximumEmitters: 2, particlesPerEmitter: 8192);
        Configure(graphics.Renderer, particles);
        graphics.Settings = graphics.Settings with { AntiAliasing = AntiAliasing3D.Temporal };
        graphics.Clear = Clear;
        bool raining = true;
        bool normalMaps = true;
        bool puddles = true;
        bool paused = false;
        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) =>
            {
                if (key == Key.Escape) { window.Close(); }
                if (key == Key.R) { raining = !raining; }
                if (key == Key.N)
                {
                    normalMaps = !normalMaps;
                    graphics.Renderer.ResetTemporalHistory();
                }
                if (key == Key.P) { puddles = !puddles; }
                if (key == Key.Space) { paused = !paused; }
            };
        }
        var clock = Stopwatch.StartNew();
        double previous = clock.Elapsed.TotalSeconds;
        float time = 3;
        ulong frame = 0;
        while (!window.IsClosing)
        {
            window.DoEvents();
            if (window.IsClosing) { break; }
            double now = clock.Elapsed.TotalSeconds;
            if (!paused)
            {
                time += (float)Math.Min(now - previous, .1);
            }
            previous = now;
            // Presentation time wraps within the particle command's documented hour bound.
            if (time > 3500) { time = 3; }
            graphics.Resize(window.FramebufferSize.X, window.FramebufferSize.Y);
            graphics.Renderer.ParticleEmitters = raining ? Rain(time) : [];
            Native3DScene current = AtTime(scene, time);
            if (!normalMaps)
            {
                current = current with
                {
                    Models = current.Models.Select(batch => batch with { Material = batch.Material with { NormalTexture = null } }).ToArray(),
                };
            }
            if (!puddles)
            {
                current = current with
                {
                    Models = current.Models.Where(batch => batch.Material.Water is null).ToArray(), WaterReflection = null,
                };
            }
            Vector3 eye = Eye + new Vector3(MathF.Sin(time * .13f) * .35f, 0, 0);
            var rendered = graphics.Render(current, Camera(eye, graphics.Width, graphics.Height), eye, capture: smoke);
            graphics.Present(++frame);
            if (smoke && frame == 12)
            {
                NativeGameGraphics.WritePng(Path.Combine(output, "native-window.png"), graphics.Width, graphics.Height, rendered.Pixels!);
                File.WriteAllText(Path.Combine(output, "window-evidence.json"), JsonSerializer.Serialize(new
                {
                    PresentedFrames = frame, Device = graphics.Plant.Facts.PhysicalDeviceName,
                    rendered.PixelSha256, Budget = particles.Inspection,
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("AURELIAN_RAIN_NIGHT_WINDOW_PASSED frames=" + frame);
                window.Close();
            }
        }
    }

    private static Matrix4x4 Camera(Vector3 eye, int width, int height)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.8f, (float)width / height, .1f, 100);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, Look, Vector3.UnitY) * projection;
    }

    private static int Changed(byte[] first, byte[] second)
    {
        int count = 0;
        for (int offset = 0; offset < first.Length; offset += 4)
        {
            int difference = Math.Abs(first[offset] - second[offset]) + Math.Abs(first[offset + 1] - second[offset + 1])
                + Math.Abs(first[offset + 2] - second[offset + 2]);
            if (difference > 4) { count++; }
        }
        return count;
    }

    private static object Distribution(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        return new { MedianMilliseconds = sorted[sorted.Length / 2], P95Milliseconds = sorted[(int)((sorted.Length - 1) * .95)] };
    }

    private static void Require(bool accepted, string message)
    {
        if (!accepted) { throw new InvalidOperationException(message); }
    }
}
