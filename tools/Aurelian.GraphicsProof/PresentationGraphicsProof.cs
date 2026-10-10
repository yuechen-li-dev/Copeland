using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

internal static class PresentationGraphicsProof
{
    private const int Width = 960;
    private const int Height = 640;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian presentation graphics"));
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
                SubsurfaceDiffuse = assets.Shader("SubsurfaceDiffuse3D.v.ts"),
                SubsurfaceMerge = assets.Shader("SubsurfaceMerge3D.v.ts"),
                TransparentModel = assets.Shader("TransparentModel3D.v.ts"),
                TransparencyResolve = assets.Shader("TransparencyResolve3D.v.ts"),
            });
        Graphics3DSettings settings = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None,
            BloomIntensity = 0,
            ToneMapping = false,
            SunDirection = Vector3.Normalize(new(.7f, .9f, .2f)),
            SunIntensity = 1.8f,
        };
        renderer.Settings = settings;
        NativeFrameClearColor clear = new(.025f, .035f, .06f, 1);

        using var corridor = SceneCompiler.Compile(Scene.World("shadow-coverage", [
            Scene.Box("floor", new(24, .2f, 120), new(.5f, .5f, .5f, 1), new(0, -.1f, -45)),
            Scene.Box("near", new(2, 4, 2), new(.4f, .3f, .2f, 1), new(-3, 2, 1)),
            Scene.Box("mid", new(3, 5, 3), new(.4f, .3f, .2f, 1), new(1, 2.5f, -28)),
            Scene.Box("far", new(4, 6, 4), new(.4f, .3f, .2f, 1), new(0, 3, -70)),
        ])).Mount();
        var corridorScene = SceneGeometry3D.BuildScene(corridor.Project());
        Vector3 eye = new(0, 7, 12);
        Matrix4x4 camera = Camera(eye, new(0, 0, -45), 180);
        byte[] covered = Capture("cascades-100m", corridorScene, camera, eye);
        renderer.Settings = settings with { ShadowDistance = 24 };
        byte[] shortCoverage = Capture("cascades-24m", corridorScene, camera, eye);
        Vector3 farReceiver = new(-3.5f, .001f, -70.8f);
        double farShadowGain = Brightness(shortCoverage, farReceiver, camera) - Brightness(covered, farReceiver, camera);
        Require(farShadowGain > 8, $"Far caster failed coverage: brightness gain {farShadowGain}.");
        renderer.Settings = settings;
        Vector3 elevatedEye = new(0, 28, 12);
        byte[] elevatedShadow = Capture("cascades-elevated", corridorScene, Camera(elevatedEye, new(0, 0, -45), 180), elevatedEye);
        renderer.Settings = settings with { Shadows = false };
        int elevatedChanged = Changed(elevatedShadow, Capture("elevated-no-shadows", corridorScene,
            Camera(elevatedEye, new(0, 0, -45), 180), elevatedEye));
        Require(elevatedChanged > 200, "Elevated camera lost directional coverage.");

        // Exact horizontal Beer-Lambert reference, measured through real HDR and sRGB output.
        eye = new(0, 0, 8);
        camera = Camera(eye, Vector3.Zero, 60);
        ModelMaterial red = new("red") { BaseColor = new(.4f, .1f, .1f, 1), Metallic = 0, Unlit = true, DoubleSided = true };
        Native3DScene flatReceiver = new([], [Quad(0, red with { Unlit = false })]);
        renderer.Settings = settings with { Shadows = false };
        byte[] flatWithoutShadows = Capture("flat-receiver-no-shadows", flatReceiver, camera, eye);
        renderer.Settings = settings;
        byte[] flatWithShadows = Capture("flat-receiver-shadows", flatReceiver, camera, eye);
        int receiverError = MaximumError(flatWithoutShadows, flatWithShadows);
        Require(receiverError <= 1, $"An isolated grazing receiver gained self-shadow bands: {receiverError} bytes.");
        Native3DScene plane = new([], [Quad(0, red)]);
        renderer.Settings = settings with { Shadows = false };
        byte[] noFog = Capture("fog-disabled", plane, camera, eye);
        var fog = new HeightFog3D { Density = .125f, HeightFalloff = 0, Color = new(.1f, .2f, .3f) };
        renderer.Settings = renderer.Settings with { Fog = fog };
        byte[] fogged = Capture("fog-homogeneous", plane, camera, eye);
        Vector3 expected = new Vector3(.4f, .1f, .1f) * MathF.Exp(-1) + fog.Color * (1 - MathF.Exp(-1));
        Vector3 measured = LinearPixel(fogged, Width / 2, Height / 2);
        float fogError = Vector3.Distance(expected, measured);
        Require(fogError < .009f, $"Height fog disagreed with analytic extinction: {fogError}.");
        renderer.Settings = settings with { Shadows = false, Fog = fog with { Density = 0 } };
        Require(noFog.SequenceEqual(Capture("fog-zero-density", plane, camera, eye)), "Zero fog density changed the image.");
        renderer.Settings = settings with { Shadows = false, Fog = fog with { HeightFalloff = .5f, BaseHeight = 0 } };
        byte[] lowFog = Capture("fog-low", plane, camera, eye);
        Vector3 highEye = new(0, 4, 8);
        Native3DScene highPlane = new([], [Quad(0, red, 4)]);
        byte[] highFog = Capture("fog-high", highPlane, Camera(highEye, new(0, 4, 0), 60), highEye);
        Require(Vector3.Distance(LinearPixel(highFog, Width / 2, Height / 2), new(.4f, .1f, .1f))
            < Vector3.Distance(LinearPixel(lowFog, Width / 2, Height / 2), new(.4f, .1f, .1f)), "Height falloff failed to reduce high-altitude extinction.");

        renderer.Settings = settings with { Shadows = false };
        ModelMaterial blue = red with { Slot = "blue-alpha", BaseColor = new(.05f, .2f, .8f, .4f), AlphaBlend = true };
        ModelMaterial green = red with { Slot = "green-alpha", BaseColor = new(.05f, .7f, .1f, .5f), AlphaBlend = true };
        Native3DScene layers = new([], [Quad(0, red), Quad(2, blue), Quad(1, green)]);
        byte[] transparent = Capture("transparency", layers, camera, eye);
        byte[] reverse = Capture("transparency-reversed", layers with { Models = [layers.Models[0], layers.Models[2], layers.Models[1]] }, camera, eye);
        int orderError = MaximumError(transparent, reverse);
        Require(orderError <= 1, $"Weighted transparency depends on draw order: {orderError} bytes.");
        Require(Changed(noFog, transparent) > 10000, "Transparent layers did not visibly blend.");
        Native3DScene hidden = new([], [Quad(0, red), Quad(-1, blue)]);
        Require(noFog.SequenceEqual(Capture("transparency-behind-opaque", hidden, camera, eye)), "A transparent layer leaked through opaque depth.");
        Native3DScene zeroAlpha = new([], [Quad(0, red), Quad(2, blue with { BaseColor = blue.BaseColor with { W = 0 } })]);
        Require(noFog.SequenceEqual(Capture("transparency-zero-alpha", zeroAlpha, camera, eye)), "Zero alpha changed opaque output.");
        Native3DScene oneAlpha = new([], [Quad(0, red), Quad(2, blue with { BaseColor = blue.BaseColor with { W = 1 } })]);
        byte[] alphaOne = Capture("transparency-one-alpha", oneAlpha, camera, eye);
        Native3DScene opaqueBlue = new([], [Quad(0, red), Quad(2, blue with { AlphaBlend = false, BaseColor = blue.BaseColor with { W = 1 } })]);
        Require(MaximumError(alphaOne, Capture("transparency-opaque-reference", opaqueBlue, camera, eye)) <= 1,
            "Full opacity failed the opaque reference.");
        renderer.Settings = settings with { Shadows = false, Fog = fog };
        byte[] fogTransparent = Capture("transparency-fog", new([], [Quad(2, blue with { BaseColor = blue.BaseColor with { W = 1 } })]), camera, eye);
        byte[] fogOpaque = Capture("opaque-fog-reference", new([], [Quad(2, blue with { AlphaBlend = false, BaseColor = blue.BaseColor with { W = 1 } })]), camera, eye);
        Require(MaximumError(fogTransparent, fogOpaque) <= 1, "Transparent and opaque surfaces disagree about fog depth.");

        renderer.Settings = settings with { Shadows = false, AntiAliasing = AntiAliasing3D.Temporal };
        for (int frame = 0; frame < 16; frame++) renderer.Render(layers, camera, eye, clear);
        // Move the same batches out of view, preserving their material, identity
        // and vertex count. This must exercise local rejection, not a global cut.
        NativeModel3DBatch MoveAway(NativeModel3DBatch batch)
        {
            return batch with
            {
                Vertices = batch.Vertices.Select(vertex =>
                vertex with { Position = vertex.Position + new Vector3(50, 0, 0) }).ToArray()
            };
        }
        Native3DScene removedLayers = layers with { Models = [layers.Models[0], MoveAway(layers.Models[1]), MoveAway(layers.Models[2])] };
        byte[] removed = Capture("transparency-removed-taa", removedLayers, camera, eye);
        renderer.ResetTemporalHistory();
        byte[] fresh = Capture("transparency-fresh-taa", removedLayers, camera, eye);
        // Compare interiors; the silhouette legitimately has different jitter coverage.
        int trail = InteriorError(removed, fresh);
        Require(trail <= 1, $"Transparent history left a color trail: {trail} bytes.");

        using var room = SceneCompiler.Compile(Scene.World("presentation-room", [
            Scene.Box("floor", new(12, .15f, 12), new(.5f, .52f, .58f, 1), new(0, -.075f, 0)),
            Scene.Box("back", new(12, 4, .2f), new(.35f, .4f, .48f, 1), new(0, 2, -4)),
            Scene.Box("plinth-a", new(1.8f, .4f, 1.8f), new(.22f, .26f, .32f, 1), new(-1.5f, .2f, 0)),
            Scene.Box("plinth-b", new(1.8f, .4f, 1.8f), new(.22f, .26f, .32f, 1), new(1.5f, .2f, 0)),
            Scene.Box("strip", new(.04f, 2.5f, .04f), Vector4.One, new(-3, 1.5f, -3)) with
            {
                Material = new("strip") { BaseColor = new(.01f, .01f, .01f, 1), Metallic = 0, Emissive = new(5, 2, .4f) },
            },
        ])).Mount();
        Native3DScene roomScene = SceneGeometry3D.BuildScene(room.Project());
        EnvironmentLighting studioEnvironment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"),
            128, 64, SurfaceGraphicsProof.Studio());
        renderer.Environment = studioEnvironment;
        ModelMaterial wax = new("wax") { BaseColor = new(.58f, .25f, .12f, 1), Metallic = 0, Roughness = .3f, SubsurfaceRadius = .25f };
        NativeModel3DBatch leftSphere = SurfaceGraphicsProof.Sphere(new(-1.5f, 1.1f, 0), .7f, wax);
        NativeModel3DBatch rightSphere = SurfaceGraphicsProof.Sphere(new(1.5f, 1.1f, 0), .7f,
            new("ceramic") { BaseColor = new(.02f, .22f, .42f, 1), Metallic = 0, Roughness = .22f });
        Native3DScene studio = roomScene with { Models = [.. roomScene.Models, leftSphere, rightSphere] };
        eye = new(5, 3.5f, 7);
        camera = Camera(eye, new(0, 1, 0), 60);
        renderer.Settings = settings with { ToneMapping = true, AmbientOcclusionStrength = 1, AmbientOcclusionRadius = .5f };
        byte[] noScattering = Capture("subsurface-off", studio, camera, eye);
        Native3DScene scattered = studio with
        {
            Models = [.. roomScene.Models, leftSphere with
            { Material = wax with { DoubleSided = true, SubsurfaceStrength = 1 } }, rightSphere]
        };
        byte[] scattering = Capture("subsurface-on", scattered, camera, eye);
        int scatteredPixels = Changed(noScattering, scattering);
        Require(scatteredPixels > 100, "Subsurface diffusion did not change the lit wax surface.");
        // No diffusion across material boundaries: everything outside the wax silhouette is identical.
        Vector2 spherePixel = Project(new(-1.5f, 1.1f, 0), camera);
        int outsideError = OutsideError(noScattering, scattering, spherePixel, 110);
        Require(outsideError == 0, "Diffusion contaminated the ceramic, emission or background.");
        ModelMaterial specularOnly = wax with
        {
            Slot = "specular-emissive",
            BaseColor = new(0, 0, 0, 1),
            DoubleSided = true,
            Emissive = new(.4f, .1f, .03f)
        };
        Native3DScene specularScene = new([], [leftSphere with { Material = specularOnly }]);
        byte[] specularOff = Capture("subsurface-specular-emissive-off", specularScene, camera, eye);
        byte[] specularOn = Capture("subsurface-specular-emissive-on", specularScene with
        { Models = [leftSphere with { Material = specularOnly with { SubsurfaceStrength = 1 } }] }, camera, eye);
        Require(specularOff.SequenceEqual(specularOn), "Subsurface diffusion altered isolated specular or emission.");
        renderer.Environment = null;
        renderer.Settings = settings with { Shadows = false, SunIntensity = 0, SkyAmbient = new(.2f), GroundAmbient = new(.2f) };
        ModelMaterial matte = new("constant-diffuse") { BaseColor = new(.3f), Metallic = 0, Roughness = 1, DoubleSided = true };
        Native3DScene constantPlane = new([], [Quad(0, matte)]);
        eye = new(0, 0, 8);
        camera = Camera(eye, Vector3.Zero, 60);
        byte[] constant = Capture("subsurface-constant-off", constantPlane, camera, eye);
        byte[] constantDiffused = Capture("subsurface-constant-on", constantPlane with { Models = [Quad(0, matte with { SubsurfaceStrength = 1, SubsurfaceRadius = .2f })] }, camera, eye);
        Require(MaximumError(constant, constantDiffused) <= 1, "Diffusion changed constant illumination or surface coverage.");

        renderer.Settings = settings with
        {
            AntiAliasing = AntiAliasing3D.Temporal,
            BloomIntensity = .08f,
            ToneMapping = true,
            AmbientOcclusionStrength = 1,
            AmbientOcclusionRadius = .5f,
            Fog = new() { Density = .025f, HeightFalloff = .3f, Color = new(.18f, .24f, .32f) }
        };
        renderer.Environment = studioEnvironment;
        Native3DScene combined = scattered with
        {
            Models = [.. scattered.Models,
            Quad(1.3f, blue with { Slot = "display-pane", Unlit = false, Roughness = .15f }, 1.1f, .85f)]
        };
        eye = new(5, 3.5f, 7);
        camera = Camera(eye, new(0, 1, 0), 60);
        Native3DFrameResult result = new(0, null, null);
        for (int frame = 0; frame < 32; frame++) result = renderer.Render(combined, camera, eye, clear, frame == 31);
        Save("combined", result.Pixels!);
        string[] expectedPasses = ["directional-shadow", "linear-lighting", "temporal-resolve", "bloom", "tone-map-output",
            "ambient-occlusion", "local-light-culling", "surface-lighting", "local-shadows", "subsurface-diffusion",
            "atmosphere", "transparency", "refraction"];
        Require(result.GpuPassTimes.Count == expectedPasses.Length
            && result.GpuPassTimes.Select(time => time.Pass).ToHashSet().SetEquals(expectedPasses)
            && result.GpuPassTimes.All(time => double.IsFinite(time.Milliseconds) && time.Milliseconds >= 0),
            "GPU pass timings did not qualify all named presentation stages.");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            FarShadowBrightnessGain = farShadowGain,
            ElevatedShadowChangedPixels = elevatedChanged,
            IsolatedReceiverShadowByteError = receiverError,
            FogLinearError = fogError,
            TransparencyOrderByteError = orderError,
            TransparentHistoryTrailByteError = trail,
            SubsurfaceChangedPixels = scatteredPixels,
            SubsurfaceOutsideByteError = outsideError,
            CombinedGpuPasses = result.GpuPassTimes,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"AURELIAN_PRESENTATION_GRAPHICS_PASSED {evidencePath}");

        byte[] Capture(string name, Native3DScene scene, Matrix4x4 transform, Vector3 position)
        {
            byte[] pixels = renderer.Render(scene, transform, position, clear, true).Pixels!;
            Save(name, pixels);
            return pixels;
        }

        void Save(string name, byte[] pixels)
        {
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), Width, Height, pixels);
        }
    }

    private static Matrix4x4 Camera(Vector3 eye, Vector3 target, float far)
    {
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)Width / Height, .1f, far);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
    }

    private static NativeModel3DBatch Quad(float z, ModelMaterial material, float centerY = 0, float halfSize = 2)
    {
        NativeModel3DVertex Vertex(float x, float y)
        {
            return new(new(x * halfSize, y * halfSize + centerY, z), Vector3.UnitZ, Vector4.One,
                new((x + 1) * .5f, (y + 1) * .5f), new(1, 0, 0, 1));
        }
        return new([Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1), Vertex(-1, -1), Vertex(1, 1), Vertex(-1, 1)], material);
    }

    private static Vector2 Project(Vector3 point, Matrix4x4 camera)
    {
        Vector4 clip = Vector4.Transform(new Vector4(point, 1), camera);
        return new((clip.X / clip.W * .5f + .5f) * Width, (clip.Y / clip.W * .5f + .5f) * Height);
    }

    private static double Brightness(byte[] pixels, Vector3 point, Matrix4x4 camera)
    {
        Vector2 pixel = Project(point, camera);
        double sum = 0;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                int offset = (Math.Clamp((int)pixel.Y + y, 0, Height - 1) * Width + Math.Clamp((int)pixel.X + x, 0, Width - 1)) * 4;
                sum += (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3.0;
            }
        }
        return sum / 9;
    }

    private static Vector3 LinearPixel(byte[] pixels, int x, int y)
    {
        int offset = (y * Width + x) * 4;
        return new(NativeSrgbTransfer.Decode(pixels[offset] / 255f), NativeSrgbTransfer.Decode(pixels[offset + 1] / 255f),
            NativeSrgbTransfer.Decode(pixels[offset + 2] / 255f));
    }

    private static int Changed(byte[] left, byte[] right)
    {
        int count = 0;
        for (int offset = 0; offset < left.Length; offset += 4)
        {
            if (Math.Abs(left[offset] - right[offset]) + Math.Abs(left[offset + 1] - right[offset + 1])
                + Math.Abs(left[offset + 2] - right[offset + 2]) > 3) count++;
        }
        return count;
    }

    private static int MaximumError(byte[] left, byte[] right)
    {
        int error = 0;
        for (int offset = 0; offset < left.Length; offset++)
        {
            if (offset % 4 != 3) error = Math.Max(error, Math.Abs(left[offset] - right[offset]));
        }
        return error;
    }

    private static int InteriorError(byte[] left, byte[] right)
    {
        int error = 0;
        for (int y = Height / 2 - 60; y < Height / 2 + 60; y++)
        {
            for (int x = Width / 2 - 60; x < Width / 2 + 60; x++)
            {
                int offset = (y * Width + x) * 4;
                for (int channel = 0; channel < 3; channel++) error = Math.Max(error, Math.Abs(left[offset + channel] - right[offset + channel]));
            }
        }
        return error;
    }

    private static int OutsideError(byte[] left, byte[] right, Vector2 center, float radius)
    {
        int error = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Vector2.Distance(new(x, y), center) <= radius) continue;
                int offset = (y * Width + x) * 4;
                for (int channel = 0; channel < 3; channel++) error = Math.Max(error, Math.Abs(left[offset + channel] - right[offset + channel]));
            }
        }
        return error;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
