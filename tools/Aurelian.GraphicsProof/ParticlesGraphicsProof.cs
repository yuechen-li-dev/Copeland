using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

internal static class ParticlesGraphicsProof
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian GPU particles"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, 960, 640);
        using var particles = new VulkanParticles3D(plant, assets.ComputeShader("Particles3D.v.ts"),
            maximumEmitters: 4, particlesPerEmitter: 512, enableQualificationReadback: true);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"),
            surfacePrograms: new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
            {
                HeightFog = assets.Shader("HeightFog3D.v.ts"),
                TransparentModel = assets.Shader("TransparentModel3D.v.ts"),
                TransparencyResolve = assets.Shader("TransparencyResolve3D.v.ts"),
                ParticleBillboards = assets.Shader("ParticleBillboard3D.v.ts"),
                VolumeInject = assets.Shader("VolumeInject3D.v.ts"),
                VolumeIntegrate = assets.Shader("VolumeIntegrate3D.v.ts"),
                VolumeResolve = assets.Shader("VolumeResolve3D.v.ts"),
            });
        renderer.ParticleSystem = particles;
        renderer.Settings = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None, BloomIntensity = .16f, SunIntensity = 1.2f,
            LocalShadowBudget = 0, SkyAmbient = new(.08f, .11f, .16f), GroundAmbient = new(.025f),
        };
        using var mounted = SceneCompiler.Compile(Scene.World("particles", [
            Scene.Box("floor", new(12, .2f, 12), new(.21f, .24f, .3f, 1), new(0, -.1f, 0)),
            Scene.Box("wall", new(12, 5, .2f), new(.12f, .15f, .2f, 1), new(0, 2.5f, -3)),
            Scene.Box("fire-basin", new(1.5f, .25f, 1.2f), new(.11f, .1f, .09f, 1), new(-1.4f, .12f, 0)),
            Scene.Box("spark-basin", new(1.5f, .25f, 1.2f), new(.11f, .1f, .09f, 1), new(1.4f, .12f, 0)),
            Scene.Agent("fire", new ParticleEmitterDefinition(ParticleEmitter3D.Fire("fire", default, 17)), new(-1.4f, .3f, 0)),
            Scene.Agent("smoke", new ParticleEmitterDefinition(ParticleEmitter3D.Smoke("smoke", default, 99)), new(-1.4f, 1.1f, 0)),
            Scene.Agent("sparks", new ParticleEmitterDefinition(ParticleEmitter3D.Sparks("sparks", default, uint.MaxValue)), new(1.4f, .3f, 0)),
        ])).Mount();
        var scene = SceneGeometry3D.BuildScene(mounted.Project());
        var fire = mounted.Agent<ParticleEmitter3D>("fire");
        var smoke = mounted.Agent<ParticleEmitter3D>("smoke");
        var sparks = mounted.Agent<ParticleEmitter3D>("sparks");
        Vector3 eye = new(5, 3.4f, 8);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.8f, 1.5f, .1f, 60);
        projection.M22 *= -1;
        Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, new(0, 1.2f, 0), Vector3.UnitY) * projection;
        NativeFrameClearColor clear = new(.025f, .03f, .05f, 1);
        Native3DFrameResult Capture(string name)
        {
            var result = renderer.Render(scene, camera, eye, clear, capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), 960, 640, result.Pixels!);
            return result;
        }
        var baseline = Capture("baseline");
        fire.State = fire.State with { ElapsedSeconds = 2 };
        smoke.State = smoke.State with { ElapsedSeconds = 2 };
        sparks.State = sparks.State with { ElapsedSeconds = 2 };
        ParticleEmitter3D[] commands = [fire.State, smoke.State, sparks.State];
        renderer.ParticleEmitters = commands;
        var combined = Capture("fire-smoke-sparks");
        Require(combined.TriangleCount == baseline.TriangleCount + 3 * 512 * 2,
            "Frame inspection did not count GPU billboard triangles.");
        renderer.ParticleEmitters = [commands[0], commands[0]];
        ExpectDiagnostic(() => renderer.Render(scene, camera, eye, clear), "AUR-VFX-003:");
        renderer.ParticleEmitters = Enumerable.Range(0, 5).Select(index => commands[0] with { Id = "emitter-" + index }).ToArray();
        ExpectDiagnostic(() => renderer.Render(scene, camera, eye, clear), "AUR-VFX-003:");
        renderer.ParticleEmitters = commands;
        int changed = Changed(baseline.Pixels!, combined.Pixels!);
        Require(changed > 2000, "GPU VFX did not materially change the rendered scene.");
        NativeParticleVertex3D[] vertices = particles.ReadVerticesForQualification();
        Require(vertices.Length == 3 * 512 * 6 && vertices.All(vertex =>
            float.IsFinite(vertex.Position.LengthSquared()) && float.IsFinite(vertex.Color.LengthSquared())),
            "GPU output was not finite or correctly bounded.");
        int live = vertices.Where((vertex, index) => index % 6 == 0 && vertex.Color.W > 0).Count();
        Require(live > 150 && particles.Inspection.OverBudgetParticles == 0, "Starter particles did not spawn within their budgets.");
        var repeat = Capture("repeat");
        Require(combined.Pixels!.SequenceEqual(repeat.Pixels!), "Identical commands did not replay the same image.");
        Require(vertices.SequenceEqual(particles.ReadVerticesForQualification()), "GPU seed replay was not exact on this device.");
        renderer.ParticleEmitters = commands.Select(emitter => emitter with { Seed = emitter.Seed + 1 }).ToArray();
        var reseeded = Capture("different-seed");
        Require(Changed(combined.Pixels!, reseeded.Pixels!) > 500, "Seed did not affect GPU particle motion.");
        renderer.ParticleEmitters = [commands[0]];
        var fireOnly = Capture("fire-only");
        renderer.ParticleEmitters = [commands[1]];
        var smokeOnly = Capture("smoke-only");
        renderer.ParticleEmitters = [commands[2]];
        var sparksOnly = Capture("sparks-only");
        Require(Changed(baseline.Pixels!, fireOnly.Pixels!) > 500 && Changed(baseline.Pixels!, smokeOnly.Pixels!) > 500
            && Changed(baseline.Pixels!, sparksOnly.Pixels!) > 100, "An effect preset was invisible.");
        renderer.ParticleEmitters = [commands[0] with { ElapsedSeconds = .2f, SoftIntersectionDistance = .001f }];
        var hardIntersection = Capture("hard-intersection");
        renderer.ParticleEmitters = [commands[0] with { ElapsedSeconds = .2f, SoftIntersectionDistance = 2 }];
        var softIntersection = Capture("soft-intersection");
        int softChanged = Changed(hardIntersection.Pixels!, softIntersection.Pixels!);
        Require(softChanged > 200, "Per-emitter soft depth distance did not change the intersection.");
        renderer.ParticleEmitters = commands.Select(emitter => emitter with { ElapsedSeconds = 0 }).ToArray();
        Require(Capture("rewound").Pixels!.SequenceEqual(baseline.Pixels!), "Rewind to spawn time retained particles.");
        renderer.ParticleEmitters = commands.Select(emitter => emitter with { EmitUntilSeconds = .2f, ElapsedSeconds = 10 }).ToArray();
        Require(Capture("expired").Pixels!.SequenceEqual(baseline.Pixels!), "Stopped emitters did not drain.");
        renderer.ParticleEmitters = commands.Select(emitter => emitter with
        {
            Position = new(0, 1, -5), Speed = 0, Radius = 0,
        }).ToArray();
        Require(Capture("occluded").Pixels!.SequenceEqual(baseline.Pixels!), "Particles behind the wall leaked through opaque depth.");
        renderer.ParticleEmitters = [commands[0] with { Rate = 10000 }];
        Capture("over-budget");
        Require(particles.Inspection.OverBudgetParticles > 0 && particles.VertexCount == 512 * 6,
            "Particle budget overflow was not bounded and reported.");
        renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.Temporal };
        renderer.ParticleEmitters = commands;
        for (int frame = 0; frame < 12; frame++) { renderer.Render(scene, camera, eye, clear); }
        var temporalEffects = Capture("temporal");
        renderer.ParticleEmitters = [];
        var removed = Capture("temporal-removed");
        Require(particles.Inspection.Emitters == 0 && particles.VertexCount == 0,
            "Removing emitter commands left stale budget inspection.");
        renderer.ResetTemporalHistory();
        var fresh = Capture("temporal-fresh");
        var trail = ParticleTrailError(baseline.Pixels!, temporalEffects.Pixels!, removed.Pixels!, fresh.Pixels!);
        int trailError = trail.Maximum;
        Require(trail.CheckedPixels > 500, "Temporal trail specimen did not cover enough visible particle pixels.");
        Require(trailError <= 3, "Particle removal left a TAA trail.");
        renderer.ParticleEmitters = commands;
        renderer.Settings = renderer.Settings with
        {
            Fog = new() { Density = .08f, Color = new(.12f, .18f, .24f) },
        };
        var fogged = Capture("fogged");
        Require(Changed(combined.Pixels!, fogged.Pixels!) > 500, "Fog did not affect the particle scene.");
        renderer.Settings = renderer.Settings with { Volumetrics = new() { Enabled = true } };
        Capture("volumetric");
        renderer.Settings = renderer.Settings with { Fog = new(), Volumetrics = new(), AntiAliasing = AntiAliasing3D.None };
        using var gpuOnly = new VulkanParticles3D(plant, assets.ComputeShader("Particles3D.v.ts"));
        ExpectDiagnostic(() => gpuOnly.ReadVerticesForQualification(), "AUR-VFX-006:");
        renderer.ParticleSystem = gpuOnly;
        var production = Capture("gpu-only");
        Require(production.Pixels!.SequenceEqual(combined.Pixels!), "Production GPU-only path differs from qualification buffers.");
        var times = new List<Native3DGpuPassTime>();
        for (int frame = 0; frame < 60; frame++)
        {
            renderer.ParticleEmitters = commands.Select(emitter => emitter with { ElapsedSeconds = 2 + frame / 60f }).ToArray();
            var result = renderer.Render(scene, camera, eye, clear);
            if (frame >= 10) { times.AddRange(result.GpuPassTimes); }
        }
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName,
            ChangedPixels = changed, LiveParticles = live, ReplayExact = true, RewindClean = true,
            StoppedEmittersDrain = true, OpaqueOcclusion = true, TemporalRemovalMaximumByteError = trailError,
            TemporalRemovalCheckedPixels = trail.CheckedPixels,
            SoftIntersectionChangedPixels = softChanged,
            GPUOnlyMatches = true, Budget = gpuOnly.Inspection,
            MeanGpuMilliseconds = times.GroupBy(time => time.Pass).ToDictionary(group => group.Key, group => group.Average(time => time.Milliseconds)),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_GPU_VFX_PASSED " + output);
    }

    private static int Changed(byte[] first, byte[] second)
    {
        int count = 0;
        for (int index = 0; index < first.Length; index += 4)
        {
            if (Math.Abs(first[index] - second[index]) + Math.Abs(first[index + 1] - second[index + 1])
                + Math.Abs(first[index + 2] - second[index + 2]) > 8) { count++; }
        }
        return count;
    }

    private static (int Maximum, int CheckedPixels) ParticleTrailError(byte[] baseline, byte[] effects, byte[] removed, byte[] fresh)
    {
        int maximum = 0;
        int count = 0;
        for (int y = 1; y < 639; y++)
        {
            for (int x = 1; x < 959; x++)
            {
                int offset = (y * 960 + x) * 4;
                bool smooth = true;
                int effectDifference = 0;
                for (int channel = 0; channel < 3; channel++)
                {
                    effectDifference += Math.Abs(baseline[offset + channel] - effects[offset + channel]);
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            smooth &= Math.Abs(baseline[offset + channel] - baseline[offset + (dy * 960 + dx) * 4 + channel]) <= 2;
                        }
                    }
                }
                // Opaque silhouette AA changes between jitter phases are not particle trails.
                // Inspect formerly covered pixels on locally smooth opaque receivers instead.
                if (!smooth || effectDifference <= 8) { continue; }
                count++;
                for (int channel = 0; channel < 3; channel++)
                {
                    maximum = Math.Max(maximum, Math.Abs(removed[offset + channel] - fresh[offset + channel]));
                }
            }
        }
        return (maximum, count);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private static void ExpectDiagnostic(Action action, string code)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception.Message.StartsWith(code, StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException("Expected named diagnostic " + code);
    }
}
