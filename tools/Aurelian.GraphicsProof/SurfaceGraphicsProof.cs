using System.Numerics;
using System.Text.Json;
using Aurelian.Assets.Lighting;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

internal static class SurfaceGraphicsProof
{
    public static void Run(string output, string? hdrPath = null)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian surface lighting"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        double perspectiveImprovement = PerspectiveTemporalProof.Run(plant, assets, output);
        Require(perspectiveImprovement > .5, "Perspective silhouettes failed the supersampled quality reference.");
        var bake = assets.Shader("EnvironmentCompile3D.v.ts");
        float[] constant = new float[32 * 16 * 4];
        for (int pixel = 0; pixel < constant.Length; pixel += 4)
        {
            constant[pixel] = .4f;
            constant[pixel + 1] = .7f;
            constant[pixel + 2] = 1.2f;
            constant[pixel + 3] = 1;
        }
        EnvironmentLighting constantEnvironment = VulkanEnvironmentCompiler.Compile(plant, bake, 32, 16, constant);
        float constantError = 0;
        for (int pixel = 0; pixel < EnvironmentLighting.Size * EnvironmentLighting.Size * 7; pixel++)
        {
            constantError = Math.Max(constantError, Math.Abs(constantEnvironment.Pixels[pixel * 4] - .4f));
            constantError = Math.Max(constantError, Math.Abs(constantEnvironment.Pixels[pixel * 4 + 1] - .7f));
            constantError = Math.Max(constantError, Math.Abs(constantEnvironment.Pixels[pixel * 4 + 2] - 1.2f));
        }
        Require(constantError < .0001f, "Environment integration failed the constant-radiance invariant.");
        EnvironmentLighting environment;
        if (hdrPath is not null)
        {
            LinearEnvironmentImage source = RadianceEnvironmentImporter.Load(hdrPath);
            environment = VulkanEnvironmentCompiler.Compile(plant, bake, source.Width, source.Height, source.Rgba.AsSpan());
        }
        else
        {
            environment = VulkanEnvironmentCompiler.Compile(plant, bake, 128, 64, Studio());
        }
        string artifact = Path.Combine(output, "studio.aenv");
        EnvironmentLightingAsset.Save(artifact, environment);
        environment = assets.LoadEnvironment(artifact);
        using var target = new VulkanNativeFrameTarget(plant, 960, 640);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"),
            surfacePrograms: new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")));
        Vector3 eye = new(6, 4.2f, 8);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, 1.5f, .1f, 60);
        projection.M22 *= -1;
        Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, new(0, .8f, 0), Vector3.UnitY) * projection;
        NativeFrameClearColor clear = new(.025f, .035f, .06f, 1);
        var room = SceneCompiler.Compile(Scene.World("studio", [
            Scene.Box("floor", new(12, .15f, 12), new(.55f, .58f, .64f, 1), new(0, -.075f, 0)),
            Scene.Box("back", new(12, 4, .15f), new(.38f, .42f, .5f, 1), new(0, 2, -4)),
            Scene.Box("side", new(.15f, 4, 8), new(.35f, .39f, .46f, 1), new(-4, 2, 0)),
            Scene.Box("table", new(3.8f, .2f, 1.7f), new(.28f, .14f, .055f, 1), new(0, 1.15f, -1.8f)),
            Scene.Box("leg-a", new(.18f, 1.1f, .18f), new(.18f, .12f, .06f, 1), new(-1.5f, .55f, -1.25f)),
            Scene.Box("leg-b", new(.18f, 1.1f, .18f), new(.18f, .12f, .06f, 1), new(1.5f, .55f, -1.25f)),
            Scene.Box("plinth-a", new(1.4f, .4f, 1.4f), new(.28f, .3f, .35f, 1), new(-1.6f, .2f, .5f)),
            Scene.Box("plinth-b", new(1.4f, .4f, 1.4f), new(.28f, .3f, .35f, 1), new(1.4f, .2f, .5f)),
            Scene.Box("strip", new(.035f, 2.5f, .035f), Vector4.One, new(-3.8f, 1.5f, -2)) with {
                Material = new("strip") { BaseColor = new(.01f, .01f, .01f, 1), Emissive = new(8, 3, .8f), Metallic = 0 },
            },
        ]));
        using var mounted = room.Mount();
        var projected = SceneGeometry3D.BuildScene(mounted.Project());
        Native3DScene scene = new(projected.Geometry, [.. projected.Models,
            Sphere(new(-1.6f, 1, .5f), .6f, new("gold") { BaseColor = new(.95f, .65f, .22f, 1), Metallic = 1, Roughness = .16f }),
            Sphere(new(1.4f, 1, .5f), .6f, new("ceramic") { BaseColor = new(.025f, .18f, .35f, 1), Metallic = 0, Roughness = .32f })]);
        Graphics3DSettings settings = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None,
            BloomIntensity = 0,
            SunIntensity = 1.2f,
            SkyAmbient = new(.16f),
            GroundAmbient = new(.07f),
        };
        renderer.Settings = settings;
        byte[] baseline = Capture("baseline");
        renderer.Environment = environment;
        byte[] ibl = Capture("environment");
        renderer.Settings = settings with { AmbientOcclusionStrength = 1.25f, AmbientOcclusionRadius = .8f };
        byte[] ao = Capture("environment-ao");
        int aoChanged = Changed(ibl, ao);
        renderer.LocalLights = [
            new(new(-2.6f, 2.2f, -1.5f), new(1, .4f, .13f), 15, 5),
            new(new(2.6f, 3, 1), new(.15f, .5f, 1), 35, 7) {
                Kind = LocalLightKind.Spot, Direction = Vector3.Normalize(new(-.7f, -1.8f, -.4f)),
                InnerAngle = .25f, OuterAngle = .65f,
            },
        ];
        byte[] local = Capture("local-lights");
        int localChanged = Changed(ao, local);
        // AO must leave direct light and emission unchanged when indirect radiance is zero.
        renderer.Environment = null;
        renderer.Settings = settings with { SkyAmbient = Vector3.Zero, GroundAmbient = Vector3.Zero, AmbientOcclusionStrength = 0 };
        byte[] directOnly = Capture("direct-only");
        renderer.Settings = renderer.Settings with { AmbientOcclusionStrength = 1.5f };
        byte[] directAo = Capture("direct-only-ao");
        Require(directOnly.SequenceEqual(directAo), "AO modified direct lighting or emission.");
        renderer.LocalLights = [new(new(100, 100, 100), Vector3.One, 50, 1)];
        byte[] outOfRange = Capture("outside-range");
        renderer.LocalLights = [];
        Require(outOfRange.SequenceEqual(Capture("no-local-lights")), "A local light contributed outside its finite range.");
        renderer.Environment = null;
        renderer.Settings = settings with { SunIntensity = 0, SkyAmbient = new(.2f), GroundAmbient = new(.2f), Shadows = false };
        using var flatMount = SceneCompiler.Compile(Scene.World("flat", [
            Scene.Mesh("floor", [
                new(new(-6, 0, -6), Vector3.UnitY, new(.5f, .5f, .5f, 1)),
                new(new(6, 0, -6), Vector3.UnitY, new(.5f, .5f, .5f, 1)),
                new(new(6, 0, 6), Vector3.UnitY, new(.5f, .5f, .5f, 1)),
                new(new(-6, 0, -6), Vector3.UnitY, new(.5f, .5f, .5f, 1)),
                new(new(6, 0, 6), Vector3.UnitY, new(.5f, .5f, .5f, 1)),
                new(new(-6, 0, 6), Vector3.UnitY, new(.5f, .5f, .5f, 1)),
            ]),
        ])).Mount();
        var flat = SceneGeometry3D.BuildScene(flatMount.Project());
        byte[] flatOff = renderer.Render(flat, camera, eye, clear, true).Pixels!;
        renderer.Settings = renderer.Settings with { AmbientOcclusionStrength = 2 };
        byte[] flatOn = renderer.Render(flat, camera, eye, clear, true).Pixels!;
        Require(Changed(flatOff, flatOn) == 0, "An isolated flat receiver gained visible self-occlusion.");
        renderer.Environment = environment;
        renderer.Settings = settings;
        renderer.LocalLights = Enumerable.Range(0, 32).Select(index => new LocalLight3D(
            new((index % 8 - 4) * 2, .5f + (index / 8) * .8f, -3 + (index % 3) * 2),
            new(.1f + index % 3 * .2f, .3f, .5f), .8f, 2.5f)).ToArray();
        byte[] culled = Capture("32-lights-culled");
        renderer.Settings = settings with { LocalLightCulling = false };
        Require(culled.SequenceEqual(Capture("32-lights-unculled")), "GPU light tiles lost a contributing light.");
        renderer.LocalLights = [];
        renderer.Settings = settings;
        byte[] globalProbe = Capture("global-probe");
        renderer.ReflectionProbe = new(new(0, 1.5f, 0), new(5, 2, 5));
        byte[] boxProbe = Capture("box-probe");
        int probeChanged = Changed(globalProbe, boxProbe);
        Require(probeChanged > 100, "Box projection did not change room reflection correspondence.");
        renderer.Environment = environment;
        renderer.LocalLights = [new(new(-2.6f, 2.2f, -1.5f), new(1, .4f, .13f), 15, 5),
            new(new(2.6f, 3, 1), new(.15f, .5f, 1), 35, 7) { Kind = LocalLightKind.Spot,
                Direction = Vector3.Normalize(new(-.7f, -1.8f, -.4f)), InnerAngle = .25f, OuterAngle = .65f }];
        byte[] spotUnshadowed = Capture("spot-unshadowed");
        renderer.LocalLights = [renderer.LocalLights[0], renderer.LocalLights[1] with { CastShadows = true }];
        byte[] spotShadowed = Capture("spot-shadowed");
        int shadowChanged = Changed(spotUnshadowed, spotShadowed);
        Require(shadowChanged > 100, "Requested spot shadows did not occlude native geometry.");
        LocalLight3D secondSpot = new(new(-2.6f, 3, -1.5f), new(1, .8f, .35f), 45, 7)
        {
            Kind = LocalLightKind.Spot,
            Direction = Vector3.Normalize(new(1, -2, 2)),
            InnerAngle = .25f,
            OuterAngle = .65f,
        };
        renderer.LocalLights = [.. renderer.LocalLights, secondSpot];
        byte[] secondUnshadowed = Capture("second-spot-unshadowed");
        renderer.LocalLights = [renderer.LocalLights[0], renderer.LocalLights[1], secondSpot with { CastShadows = true }];
        int secondShadowChanged = Changed(secondUnshadowed, Capture("second-spot-shadowed"));
        Require(secondShadowChanged > 100, "The second admitted spot shadow did not occlude native geometry.");
        renderer.Settings = settings with { LocalShadowBudget = 1 };
        bool rejectedBudget = false;
        try
        {
            renderer.Render(scene, camera, eye, clear);
        }
        catch (ArgumentException error) when (error.Message.Contains("AUR-LIGHT-LOCAL-003", StringComparison.Ordinal))
        {
            rejectedBudget = true;
        }
        Require(rejectedBudget, "Local shadow requests exceeded the declared budget without a diagnostic.");
        renderer.LocalLights = [renderer.LocalLights[0], renderer.LocalLights[1]];
        renderer.Settings = settings with { AmbientOcclusionStrength = 1.25f, AntiAliasing = AntiAliasing3D.Temporal, BloomIntensity = .08f };
        Native3DFrameResult result = default!;
        var warmedTimings = new List<Native3DGpuPassTime>();
        for (int frame = 0; frame < 32; frame++)
        {
            result = renderer.Render(scene, camera, eye, clear, frame == 31);
            if (frame >= 8)
            {
                warmedTimings.AddRange(result.GpuPassTimes);
            }
        }
        NativeGameGraphics.WritePng(Path.Combine(output, "combined.png"), (int)target.Width, (int)target.Height, result.Pixels!);
        Require(Changed(baseline, ibl) > 500 && aoChanged > 100 && localChanged > 500, "A requested lighting capability did not visibly affect the native scene.");
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            ConstantEnvironmentError = constantError,
            EnvironmentChangedPixels = Changed(baseline, ibl),
            AoChangedPixels = aoChanged,
            LocalLightChangedPixels = localChanged,
            DirectLightAoByteEqual = true,
            FiniteRangeByteEqual = true,
            FlatReceiverVisibleAoPixels = 0,
            Culled32LightsByteEqual = true,
            BoxProbeChangedPixels = probeChanged,
            SpotShadowChangedPixels = shadowChanged,
            SecondSpotShadowChangedPixels = secondShadowChanged,
            ShadowBudgetRejected = rejectedBudget,
            PerspectiveEdgeErrorReduction = perspectiveImprovement,
            EnvironmentArtifact = environment.ContentKey,
            GpuPassTimes = warmedTimings.GroupBy(time => time.Pass).Select(group => new
            {
                Pass = group.Key,
                AverageMilliseconds = group.Average(time => time.Milliseconds),
            }).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_SURFACE_GRAPHICS_PROOF_PASSED " + plant.Facts.PhysicalDeviceName);

        byte[] Capture(string name)
        {
            byte[] pixels = renderer.Render(scene, camera, eye, clear, true).Pixels!;
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), (int)target.Width, (int)target.Height, pixels);
            return pixels;
        }
    }

    private static float[] Studio()
    {
        float[] result = new float[128 * 64 * 4];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                float dome = Math.Max(0, MathF.Cos(MathF.PI * (y + .5f) / 64));
                Vector3 radiance = new(.08f + dome * .22f, .1f + dome * .3f, .16f + dome * .45f);
                if (x is > 15 and < 30 && y is > 12 and < 30)
                {
                    radiance = new(8, 5.2f, 2.7f);
                }
                if (x is > 76 and < 96 && y is > 16 and < 36)
                {
                    radiance = new(2.5f, 5.2f, 9);
                }
                int pixel = (y * 128 + x) * 4;
                result[pixel] = radiance.X;
                result[pixel + 1] = radiance.Y;
                result[pixel + 2] = radiance.Z;
                result[pixel + 3] = 1;
            }
        }
        return result;
    }

    internal static NativeModel3DBatch Sphere(Vector3 center, float radius, ModelMaterial material)
    {
        var vertices = new List<NativeModel3DVertex>();
        const int rows = 24;
        const int columns = 48;
        NativeModel3DVertex Vertex(int row, int column)
        {
            float latitude = MathF.PI * row / rows;
            float longitude = MathF.Tau * column / columns;
            Vector3 normal = new(MathF.Sin(latitude) * MathF.Cos(longitude), MathF.Cos(latitude), MathF.Sin(latitude) * MathF.Sin(longitude));
            Vector3 tangent = new(-MathF.Sin(longitude), 0, MathF.Cos(longitude));
            return new(center + normal * radius, normal, Vector4.One, new((float)column / columns, (float)row / rows), new(tangent, 1));
        }
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                var a = Vertex(row, column);
                var b = Vertex(row + 1, column);
                var c = Vertex(row + 1, column + 1);
                var d = Vertex(row, column + 1);
                vertices.AddRange([a, d, c, a, c, b]);
            }
        }
        return new(vertices.ToArray(), material with { DoubleSided = true });
    }

    private static int Changed(byte[] left, byte[] right)
    {
        int count = 0;
        for (int pixel = 0; pixel < left.Length; pixel += 4)
        {
            if (Math.Abs(left[pixel] - right[pixel]) + Math.Abs(left[pixel + 1] - right[pixel + 1])
                + Math.Abs(left[pixel + 2] - right[pixel + 2]) > 8)
            {
                count++;
            }
        }
        return count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
