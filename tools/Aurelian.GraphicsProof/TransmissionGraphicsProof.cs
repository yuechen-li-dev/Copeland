using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

internal static class TransmissionGraphicsProof
{
    private const int Width = 960;
    private const int Height = 640;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian volume and refraction"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
            {
                HeightFog = assets.Shader("HeightFog3D.v.ts"),
                VolumeInject = assets.Shader("VolumeInject3D.v.ts"),
                VolumeIntegrate = assets.Shader("VolumeIntegrate3D.v.ts"),
                VolumeResolve = assets.Shader("VolumeResolve3D.v.ts"),
                RefractiveModel = assets.Shader("RefractiveModel3D.v.ts"),
                RefractionResolve = assets.Shader("RefractionResolve3D.v.ts"),
                TransparentModel = assets.Shader("TransparentModel3D.v.ts"),
                TransparencyResolve = assets.Shader("TransparencyResolve3D.v.ts"),
            });
        var settings = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None,
            ToneMapping = false,
            BloomIntensity = 0,
            SunIntensity = 0,
            SkyAmbient = Vector3.Zero,
            GroundAmbient = Vector3.Zero,
        };
        var clear = new NativeFrameClearColor(.005f, .008f, .015f, 1);
        Vector3 eye = new(0, 0, 8);
        Matrix4x4 camera = Camera(eye, Vector3.Zero);
        var backdrop = new ModelMaterial("reference")
        {
            Metallic = 0,
            Unlit = true,
            BaseColor = new(.4f, .1f, .1f, 1),
            DoubleSided = true,
        };
        var plane = new Native3DScene([], [Quad(Vector3.Zero, 3, backdrop)]);
        renderer.Settings = settings;
        byte[] noFog = Capture("reference-no-fog", plane);
        renderer.Settings = settings with { Volumetrics = new() { Enabled = true } };
        byte[] zeroFog = Capture("reference-zero-density", plane);
        Require(noFog.SequenceEqual(zeroFog), "Zero-density volume changed the original frame.");
        Matrix4x4 orthographicProjection = Matrix4x4.CreateOrthographic(8, 6, .1f, 100);
        orthographicProjection.M22 *= -1;
        bool orthographicRejected = false;
        try
        {
            renderer.Render(plane, Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * orthographicProjection,
                eye, clear, false);
        }
        catch (NotSupportedException error)
        {
            orthographicRejected = error.Message.Contains("AUR-TRANSPORT-002", StringComparison.Ordinal);
        }
        Require(orthographicRejected, "The perspective transport profile silently accepted an orthographic camera.");

        var volume = new VolumetricLighting3D
        {
            Enabled = true,
            ScatteringAlbedo = Vector3.One,
            AmbientRadiance = new(.1f, .2f, .3f),
            MaximumDistance = 30,
            PixelSize = 8,
        };
        var fog = new HeightFog3D { Density = .125f, HeightFalloff = 0, Color = volume.AmbientRadiance };
        renderer.Settings = settings with { Volumetrics = volume, Fog = fog };
        byte[] fogged = Capture("reference-homogeneous-volume", plane);
        float transmission = MathF.Exp(-fog.Density * 8);
        Vector3 expected = new Vector3(.4f, .1f, .1f) * transmission + volume.AmbientRadiance * (1 - transmission);
        float fogError = Vector3.Distance(LinearCenter(fogged), expected);
        Require(fogError < .012f, $"Homogeneous volume failed Beer-Lambert reference: {fogError}.");

        var glass = new ModelMaterial("clear-glass")
        {
            Metallic = 0,
            BaseColor = Vector4.One,
            Transmission = 1,
            IndexOfRefraction = 1,
            Roughness = .045f,
            DoubleSided = true,
        };
        var pane = Quad(new(0, 0, 2), 1.6f, glass);
        var throughGlass = plane with { Models = [.. plane.Models, pane] };
        byte[] foggedGlass = Capture("reference-volume-through-glass", throughGlass);
        float doubleFogError = Vector3.Distance(LinearCenter(foggedGlass), expected);
        Require(doubleFogError < .012f, $"Glass counted atmosphere twice: {doubleFogError}.");
        renderer.Settings = settings;
        byte[] identityGlass = Capture("reference-ior-one", throughGlass);
        int identityError = CenterError(identityGlass, noFog);
        Require(identityError <= 1, $"IOR=1 changed background radiance: {identityError} bytes.");
        Vector4 surfaceTint = new(.5f, .3f, .9f, 1);
        byte[] coloredSheet = Capture("reference-surface-tint", plane with
        { Models = [.. plane.Models, pane with { Material = glass with { BaseColor = surfaceTint } }] });
        float surfaceTintError = Vector3.Distance(LinearCenter(coloredSheet), new Vector3(.4f, .1f, .1f) * new Vector3(.5f, .3f, .9f));
        Require(surfaceTintError < .012f, $"Transmission ignored the glTF base-color factor: {surfaceTintError}.");

        var tinted = glass with
        {
            IndexOfRefraction = 1.5f,
            Thickness = .5f,
            AttenuationColor = new(.15f, .65f, .95f),
            AttenuationDistance = .5f,
        };
        byte[] absorbed = Capture("reference-absorption", plane with { Models = [.. plane.Models, pane with { Material = tinted }] });
        Vector3 absorptionExpected = new Vector3(.4f, .1f, .1f) * tinted.AttenuationColor * .96f;
        float absorptionError = Vector3.Distance(LinearCenter(absorbed), absorptionExpected);
        Require(absorptionError < .012f, $"Glass absorption failed reference: {absorptionError}.");

        NativeModel3DBatch foreground = Quad(new(0, 0, 3), 2.5f,
            backdrop with { Slot = "foreground", BaseColor = new(.1f, .3f, .2f, 1) });
        var obscured = throughGlass with { Models = [.. throughGlass.Models, foreground] };
        byte[] hidden = Capture("reference-opaque-occlusion", obscured);
        byte[] foregroundOnly = Capture("reference-foreground-only", new([], [foreground]));
        Require(CenterError(hidden, foregroundOnly) == 0, "Refractive glass drew over an opaque foreground surface.");
        var ordered = plane with { Models = [.. plane.Models, pane with { Material = tinted }, pane with
        { Vertices = pane.Vertices.Select(vertex => vertex with { Position = vertex.Position - Vector3.UnitZ }).ToArray() }] };
        byte[] orderA = Capture("nearest-layer-a", ordered);
        byte[] orderB = Capture("nearest-layer-b", ordered with { Models = ordered.Models.Reverse().ToArray() });
        Require(orderA.SequenceEqual(orderB), "Nearest refractive surface depends on submission order.");

        ModelTextureBinding checkerTexture = Checker();
        var checkerMaterial = backdrop with { Slot = "checker", BaseColor = Vector4.One, BaseColorTexture = checkerTexture };
        var checkerScene = new Native3DScene([], [Quad(new(0, 0, -1), 3, checkerMaterial)]);
        var tilted = Quad(new(0, 0, 1), 1.7f, glass with { IndexOfRefraction = 1.5f }, .5f);
        byte[] thin = Capture("checker-thin-sheet", checkerScene with { Models = [.. checkerScene.Models, tilted] });
        byte[] thick = Capture("checker-refracted", checkerScene with
        { Models = [.. checkerScene.Models, tilted with { Material = tilted.Material with { Thickness = .8f } }] });
        int refractedPixels = Changed(thin, thick);
        Require(refractedPixels > 500, "Authored thickness did not refract the checkerboard.");
        var roughGlass = tilted.Material with { Thickness = .8f, Roughness = .8f };
        byte[] rough = Capture("checker-rough-glass", checkerScene with
        { Models = [.. checkerScene.Models, tilted with { Material = roughGlass }] });
        int roughPixels = Changed(thick, rough);
        Require(roughPixels > 500, "Roughness did not filter transmitted radiance.");
        var normalTexture = new ModelTextureBinding(new("glass-normal", 1, 1, [220, 128, 220, 255]), new());
        byte[] normalMapped = Capture("checker-normal-mapped", checkerScene with
        {
            Models = [.. checkerScene.Models, tilted with
            { Material = tilted.Material with { Thickness = .8f, NormalTexture = normalTexture } }],
        });
        int normalPixels = Changed(thick, normalMapped);
        Require(normalPixels > 500, "Normal maps did not alter refraction.");

        float[] constantEnvironment = new float[16 * 8 * 4];
        for (int offset = 0; offset < constantEnvironment.Length; offset += 4)
        {
            constantEnvironment[offset] = .2f;
            constantEnvironment[offset + 1] = .3f;
            constantEnvironment[offset + 2] = .1f;
            constantEnvironment[offset + 3] = 1;
        }
        renderer.Environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"),
            16, 8, constantEnvironment);
        byte[] fallback = Capture("reference-environment-fallback", new([], [pane with
        { Material = glass with { IndexOfRefraction = 1.5f, Thickness = .3f } }]));
        float fallbackError = Vector3.Distance(LinearCenter(fallback), new(.2f, .3f, .1f));
        Require(fallbackError < .02f, $"Missing screen geometry did not preserve environment transmission: {fallbackError}.");
        renderer.Environment = null;

        renderer.Settings = settings with { AntiAliasing = AntiAliasing3D.Temporal };
        var retainedGlass = plane with { Models = [.. plane.Models, pane with { Material = tinted }] };
        for (int frame = 0; frame < 8; frame++)
            renderer.Render(retainedGlass, camera, eye, clear, false);
        eye = new(.7f, .15f, 8);
        camera = Camera(eye, Vector3.Zero);
        byte[] movedCamera = Capture("reference-moved-camera", retainedGlass);
        renderer.ResetTemporalHistory();
        byte[] freshCamera = Capture("reference-moved-camera-fresh", retainedGlass);
        int cameraTrail = CenterError(movedCamera, freshCamera);
        Require(cameraTrail <= 1, $"Moving the camera retained refractive history: {cameraTrail} bytes.");
        eye = new(0, 0, 8);
        camera = Camera(eye, Vector3.Zero);
        var movedGlass = retainedGlass with
        {
            Models = [retainedGlass.Models[0], retainedGlass.Models[1] with
            { Vertices = pane.Vertices.Select(vertex => vertex with { Position = vertex.Position + new Vector3(10, 0, 0) }).ToArray() }],
        };
        byte[] movedGlassFrame = Capture("reference-moved-glass", movedGlass);
        renderer.ResetTemporalHistory();
        byte[] freshGlassFrame = Capture("reference-moved-glass-fresh", movedGlass);
        int glassTrail = CenterError(movedGlassFrame, freshGlassFrame);
        Require(glassTrail <= 1, $"Moved glass retained a tinted background: {glassTrail} bytes.");

        // A black unlit receiver isolates medium lighting from surface lighting.
        using var isolatedCaster = SceneCompiler.Compile(Scene.World("isolated-caster", [
            Scene.Box("caster", new(1, 2, 1), new(.1f, .1f, .1f, 1), new(1.5f, 0, 3)),
        ])).Mount();
        Native3DScene casterScene = SceneGeometry3D.BuildScene(isolatedCaster.Project());
        casterScene = casterScene with
        {
            Models = [Quad(new(0, 0, -5), 8, backdrop with { BaseColor = new(0, 0, 0, 1) })],
        };
        var isolatedSettings = settings with
        {
            SunDirection = Vector3.UnitZ,
            SunColor = Vector3.One,
            SunIntensity = 20,
            Fog = fog with { Density = .08f },
            Volumetrics = volume with { AmbientRadiance = Vector3.Zero, Anisotropy = 0, DepthSlices = 64 },
        };
        renderer.Settings = isolatedSettings;
        byte[] mediumShadow = Capture("isolated-medium-shadow", casterScene);
        renderer.Settings = isolatedSettings with { Shadows = false };
        byte[] mediumLit = Capture("isolated-medium-no-shadow", casterScene);
        int mediumShadowDifference = WorldPixelError(mediumShadow, mediumLit, new(1.5f, 0, -5), camera);
        Require(mediumShadowDifference > 4, $"Shadowed medium did not respond independently of opaque shading: {mediumShadowDifference} bytes.");
        renderer.Settings = isolatedSettings with { AntiAliasing = AntiAliasing3D.Temporal };
        for (int frame = 0; frame < 8; frame++)
            renderer.Render(casterScene, camera, eye, clear, false);
        var movedCaster = casterScene with
        {
            Geometry = casterScene.Geometry.Select(vertex => vertex with { Position = vertex.Position + new Vector3(3, 0, 0) }).ToArray(),
            LightingRevision = "caster-moved",
        };
        byte[] movedCasterFrame = Capture("isolated-moved-caster", movedCaster);
        renderer.ResetTemporalHistory();
        byte[] freshCasterFrame = Capture("isolated-moved-caster-fresh", movedCaster);
        int casterTrail = CenterError(movedCasterFrame, freshCasterFrame);
        Require(casterTrail <= 1, $"Moved caster retained atmospheric history: {casterTrail} bytes.");

        using var room = SceneCompiler.Compile(Scene.World("transmission-room", [
            Scene.Box("floor", new(12, .2f, 14), new(.3f, .32f, .36f, 1), new(0, -.1f, -1)),
            Scene.Box("left-wall", new(.2f, 5, 12), new(.2f, .25f, .32f, 1), new(-5, 2.5f, -1)),
            Scene.Box("window-header", new(10, 1.5f, .3f), new(.25f, .3f, .38f, 1), new(0, 4.25f, -5)),
            Scene.Box("window-left", new(2.2f, 3.5f, .3f), new(.25f, .3f, .38f, 1), new(-3.9f, 1.75f, -5)),
            Scene.Box("window-right", new(2.2f, 3.5f, .3f), new(.25f, .3f, .38f, 1), new(3.9f, 1.75f, -5)),
            Scene.Box("slat-a", new(.24f, 3.5f, .3f), new(.12f, .14f, .18f, 1), new(-1.5f, 1.75f, -5)),
            Scene.Box("slat-b", new(.24f, 3.5f, .3f), new(.12f, .14f, .18f, 1), new(0, 1.75f, -5)),
            Scene.Box("glass-plinth-a", new(2, .55f, 1), new(.2f, .25f, .3f, 1), new(-1.3f, .275f, -1)),
            Scene.Box("glass-plinth-b", new(2, .55f, 1), new(.2f, .25f, .3f, 1), new(1.1f, .275f, -.7f)),
            Scene.Box("slat-c", new(.24f, 3.5f, .3f), new(.12f, .14f, .18f, 1), new(1.5f, 1.75f, -5)),
        ])).Mount();
        Native3DScene roomScene = SceneGeometry3D.BuildScene(room.Project());
        eye = new(6, 3, 9);
        camera = Camera(eye, new(0, 1.8f, -2));
        var roomSettings = settings with
        {
            ToneMapping = true,
            SunDirection = Vector3.Normalize(new(.15f, .3f, -1)),
            SunColor = new(1, .8f, .55f),
            SunIntensity = 3,
            SkyAmbient = new(.015f, .02f, .03f),
            GroundAmbient = new(.01f),
            Fog = new() { Density = .04f, HeightFalloff = .08f, MaximumDistance = 40 },
            Volumetrics = volume with { AmbientRadiance = new(.015f), Anisotropy = .35f, DepthSlices = 64, Region = new(new(-5, 0, -5), new(5, 5, 6)) },
        };
        renderer.Settings = roomSettings;
        byte[] shafts = Capture("room-shadowed-shafts", roomScene);
        renderer.Settings = roomSettings with { Shadows = false };
        byte[] unshadowed = Capture("room-unshadowed-volume", roomScene);
        int shadowedPixels = Changed(shafts, unshadowed);
        Require(shadowedPixels > 500, "Window geometry did not shadow the participating medium.");
        renderer.Settings = roomSettings with { SunIntensity = 0 };
        var lamp = new LocalLight3D(new(-2, 3, -3), new(.3f, .6f, 1), 100, 12)
        {
            Kind = LocalLightKind.Spot,
            Direction = Vector3.Normalize(new(3, -2, 4)),
            CastShadows = true,
            InnerAngle = .25f,
            OuterAngle = .4f,
        };
        renderer.LocalLights = [lamp];
        byte[] spotlight = Capture("room-spotlight", roomScene);
        renderer.LocalLights = [lamp with { Position = lamp.Position + new Vector3(2, 0, 0) }];
        byte[] moved = Capture("room-moved-spotlight", roomScene);
        int movedPixels = Changed(spotlight, moved);
        Require(movedPixels > 500, "Moving a spotlight did not move the volume response.");
        renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.Temporal };
        for (int frame = 0; frame < 8; frame++)
            renderer.Render(roomScene, camera, eye, clear, false);
        renderer.LocalLights = [];
        byte[] lightOff = Capture("room-light-off", roomScene);
        renderer.ResetTemporalHistory();
        byte[] freshOff = Capture("room-light-off-fresh", roomScene);
        int offTrail = CenterError(lightOff, freshOff);
        Require(offTrail <= 1, $"Changed light retained atmospheric history: {offTrail} bytes.");

        renderer.Environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"),
            128, 64, SurfaceGraphicsProof.Studio());
        renderer.Settings = roomSettings with { AntiAliasing = AntiAliasing3D.Temporal, Exposure = .9f };
        renderer.LocalLights = [lamp with { Intensity = 35 }];
        Native3DScene combined = roomScene with
        {
            Models = [.. roomScene.Models,
                Quad(new(0, 1.5f, -3.5f), 1.5f, checkerMaterial),
                Quad(new(-1.3f, 1.5f, -1), .85f, glass with { IndexOfRefraction = 1.5f, Thickness = .2f }, .3f),
                Quad(new(1.1f, 1.5f, -.7f), .85f, tinted with { Thickness = .25f }, -.25f)],
        };
        for (int frame = 0; frame < 12; frame++)
            renderer.Render(combined, camera, eye, clear, false);
        Native3DFrameResult final = renderer.Render(combined, camera, eye, clear, true);
        Save("combined", final.Pixels!);
        var atmosphereTimes = new List<double>();
        var refractionTimes = new List<double>();
        for (int frame = 0; frame < 32; frame++)
        {
            Native3DFrameResult measured = renderer.Render(combined, camera, eye, clear, false);
            foreach (Native3DGpuPassTime time in measured.GpuPassTimes)
            {
                if (time.Pass == "atmosphere")
                    atmosphereTimes.Add(time.Milliseconds);
                else if (time.Pass == "refraction")
                    refractionTimes.Add(time.Milliseconds);
            }
        }
        string[] shaderNames = ["VolumeInject3D.v.ts", "VolumeIntegrate3D.v.ts", "VolumeResolve3D.v.ts",
            "RefractiveModel3D.v.ts", "RefractionResolve3D.v.ts", "TransparentModel3D.v.ts", "TemporalResolve3D.v.ts"];
        var programKeys = shaderNames.ToDictionary(name => name,
            name => LightingProgramIdentity.Compute(assets.Shader(name)));
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            ValidationLayers = plant.Facts.EnabledValidationLayers,
            ProgramKeys = programKeys,
            HomogeneousVolumeError = fogError,
            GlassAtmosphereError = doubleFogError,
            IdentityGlassErrorBytes = identityError,
            OrthographicTransportRejected = orthographicRejected,
            SurfaceTintError = surfaceTintError,
            AbsorptionError = absorptionError,
            EnvironmentFallbackError = fallbackError,
            RoughGlassChangedPixels = roughPixels,
            NormalMappedGlassChangedPixels = normalPixels,
            GlassTrailErrorBytes = glassTrail,
            CameraTrailErrorBytes = cameraTrail,
            IsolatedMediumShadowDifferenceBytes = mediumShadowDifference,
            CasterTrailErrorBytes = casterTrail,
            RefractedPixels = refractedPixels,
            ShadowedVolumeChangedPixels = shadowedPixels,
            MovedSpotlightChangedPixels = movedPixels,
            LightOffTrailErrorBytes = offTrail,
            FinalGpuPassTimes = final.GpuPassTimes,
            AtmosphereGpu = Distribution(atmosphereTimes),
            RefractionGpu = Distribution(refractionTimes),
            VolumeGrid = new { roomSettings.Volumetrics.PixelSize, roomSettings.Volumetrics.DepthSlices, roomSettings.Volumetrics.MaximumDistance },
            Limits = new[] { "nearest refractive layer", "authored parallel-slab thickness", "screen-space background", "single scattering" },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"AURELIAN_TRANSMISSION_GRAPHICS_PASSED {evidencePath}");

        byte[] Capture(string name, Native3DScene scene)
        {
            byte[] pixels = renderer.Render(scene, camera, eye, clear, true).Pixels!;
            Save(name, pixels);
            return pixels;
        }

        void Save(string name, byte[] pixels)
        {
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), Width, Height, pixels);
        }
    }

    private static Matrix4x4 Camera(Vector3 eye, Vector3 target)
    {
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)Width / Height, .1f, 100);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
    }

    private static NativeModel3DBatch Quad(Vector3 center, float halfSize, ModelMaterial material, float angle = 0)
    {
        Matrix4x4 rotation = Matrix4x4.CreateRotationY(angle);
        NativeModel3DVertex Vertex(float x, float y)
        {
            return new(Vector3.Transform(new(x * halfSize, y * halfSize, 0), rotation) + center,
                Vector3.TransformNormal(Vector3.UnitZ, rotation), Vector4.One, new((x + 1) * .5f, (y + 1) * .5f),
                new(Vector3.TransformNormal(Vector3.UnitX, rotation), 1));
        }
        return new([Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1), Vertex(-1, -1), Vertex(1, 1), Vertex(-1, 1)], material);
    }

    private static ModelTextureBinding Checker()
    {
        byte[] pixels = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                byte value = ((x / 4 + y / 4) & 1) == 0 ? (byte)210 : (byte)35;
                int offset = (y * 64 + x) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }
        return new(new("transmission-checker", 64, 64, [.. pixels]), new(WrapU: TextureWrap.Clamp, WrapV: TextureWrap.Clamp));
    }

    private static Vector3 LinearCenter(byte[] pixels)
    {
        int offset = ((Height / 2) * Width + Width / 2) * 4;
        return new(NativeSrgbTransfer.Decode(pixels[offset] / 255f),
            NativeSrgbTransfer.Decode(pixels[offset + 1] / 255f), NativeSrgbTransfer.Decode(pixels[offset + 2] / 255f));
    }

    private static int CenterError(byte[] a, byte[] b)
    {
        int maximum = 0;
        for (int y = Height / 2 - 30; y < Height / 2 + 30; y++)
        {
            for (int x = Width / 2 - 30; x < Width / 2 + 30; x++)
            {
                int offset = (y * Width + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                    maximum = Math.Max(maximum, Math.Abs(a[offset + channel] - b[offset + channel]));
            }
        }
        return maximum;
    }

    private static int Changed(byte[] a, byte[] b)
    {
        int count = 0;
        for (int offset = 0; offset < a.Length; offset += 4)
        {
            if (Math.Abs(a[offset] - b[offset]) + Math.Abs(a[offset + 1] - b[offset + 1]) + Math.Abs(a[offset + 2] - b[offset + 2]) > 12)
                count++;
        }
        return count;
    }

    private static int WorldPixelError(byte[] a, byte[] b, Vector3 point, Matrix4x4 camera)
    {
        Vector4 projected = Vector4.Transform(new Vector4(point, 1), camera);
        int x = (int)((projected.X / projected.W * .5f + .5f) * Width);
        int y = (int)((projected.Y / projected.W * .5f + .5f) * Height);
        int offset = (y * Width + x) * 4;
        return Math.Abs(a[offset] - b[offset]) + Math.Abs(a[offset + 1] - b[offset + 1]) + Math.Abs(a[offset + 2] - b[offset + 2]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static object Distribution(List<double> values)
    {
        double[] sorted = values.Order().ToArray();
        if (sorted.Length == 0)
            return new { Available = false, Samples = 0, MedianMilliseconds = 0d, P95Milliseconds = 0d };
        return new
        {
            Available = true,
            Samples = sorted.Length,
            MedianMilliseconds = sorted[sorted.Length / 2],
            P95Milliseconds = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1],
        };
    }
}
