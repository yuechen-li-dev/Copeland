using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

internal static class PerspectiveTemporalProof
{
    private const int Width = 384;
    private const int Height = 256;
    private const int Scale = 4;

    public static double Run(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = Create(target, true);
        using var referenceTarget = new VulkanNativeFrameTarget(plant, Width * Scale, Height * Scale);
        using var referenceRenderer = Create(referenceTarget, false);
        using var mounted = SceneCompiler.Compile(Scene.World("perspective-coverage", [
            Scene.Box("backdrop", new(12, 6, .2f), new(.08f, .08f, .08f, 1), new(0, 2, -4)) with
            {
                Material = new("backdrop") { BaseColor = new(.08f, .08f, .08f, 1), Unlit = true },
            },
            Scene.Box("table", new(3.8f, .2f, 1.7f), Vector4.One, new(0, 1.15f, -1.8f)) with
            {
                Material = new("table") { BaseColor = new(.65f, .35f, .1f, 1), Unlit = true },
            },
        ])).Mount();
        Native3DScene projected = SceneGeometry3D.BuildScene(mounted.Project());
        Native3DScene scene = projected with
        {
            Models = [.. projected.Models,
                SurfaceGraphicsProof.Sphere(new(-1.6f, 1, .5f), .6f,
                    new("gold") { BaseColor = new(.95f, .65f, .22f, 1), Unlit = true }),
                SurfaceGraphicsProof.Sphere(new(1.4f, 1, .5f), .6f,
                    new("blue") { BaseColor = new(.025f, .18f, .35f, 1), Unlit = true })],
        };
        Vector3 eye = new(6, 4.2f, 8);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, 1.5f, .1f, 60);
        projection.M22 *= -1;
        Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, new(0, .8f, 0), Vector3.UnitY) * projection;
        NativeFrameClearColor clear = new(0, 0, 0, 1);
        renderer.Settings = Graphics3DSettings.Basic;
        referenceRenderer.Settings = Graphics3DSettings.Basic;
        byte[] baseline = renderer.Render(scene, camera, eye, clear, true).Pixels!;
        byte[] reference = referenceRenderer.Render(scene, camera, eye, clear, true).Pixels!;
        renderer.Settings = Graphics3DSettings.Basic with { AntiAliasing = AntiAliasing3D.Temporal };
        renderer.ResetTemporalHistory();
        byte[] temporal = [];
        for (int frame = 0; frame < 48; frame++)
        {
            temporal = renderer.Render(scene, camera, eye, clear, frame == 47).Pixels!;
        }
        double baselineError = 0;
        double temporalError = 0;
        int edgePixels = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector3 sum = Vector3.Zero;
                Vector3 minimum = new(float.MaxValue);
                Vector3 maximum = Vector3.Zero;
                for (int sampleY = 0; sampleY < Scale; sampleY++)
                {
                    for (int sampleX = 0; sampleX < Scale; sampleX++)
                    {
                        int offset = ((y * Scale + sampleY) * Width * Scale + x * Scale + sampleX) * 4;
                        Vector3 value = new(Linear(reference[offset]), Linear(reference[offset + 1]), Linear(reference[offset + 2]));
                        sum += value;
                        minimum = Vector3.Min(minimum, value);
                        maximum = Vector3.Max(maximum, value);
                    }
                }
                // Compare actual partial-coverage pixels, rather than letting the
                // many unchanged flat pixels dilute silhouette error.
                if ((maximum - minimum).Length() < .03f)
                {
                    continue;
                }
                edgePixels++;
                Vector3 expected = sum / (Scale * Scale);
                int destination = (y * Width + x) * 4;
                baselineError += Distance(baseline, destination, expected);
                temporalError += Distance(temporal, destination, expected);
            }
        }
        baselineError /= edgePixels * 3;
        temporalError /= edgePixels * 3;
        double improvement = 1 - temporalError / baselineError;
        using var controlTarget = new VulkanNativeFrameTarget(plant, Width, Height);
        using var controlRenderer = Create(controlTarget, true);
        controlRenderer.Settings = renderer.Settings with { TemporalHistoryWeight = 0 };
        int objectTrails = MotionTrails("perspective-object-motion", frame => scene with
        {
            Models = scene.Models.Select(batch => batch.Material.Slot == "backdrop" ? batch : batch with
            {
                Vertices = batch.Vertices.Select(vertex => vertex with
                {
                    Position = vertex.Position + new Vector3(-.4f + frame * .04f, 0, 0),
                }).ToArray(),
            }).ToArray(),
        }, _ => camera);
        int cameraTrails = MotionTrails("perspective-camera-motion", _ => scene,
            frame => Matrix4x4.CreateTranslation(-.4f + frame * .04f, 0, 0) * camera);
        NativeGameGraphics.WritePng(Path.Combine(output, "perspective-none.png"), Width, Height, baseline);
        NativeGameGraphics.WritePng(Path.Combine(output, "perspective-taa.png"), Width, Height, temporal);
        NativeGameGraphics.WritePng(Path.Combine(output, "perspective-reference-4x.png"), Width * Scale, Height * Scale, reference);
        var evidence = new
        {
            Accepted = improvement > .5 && objectTrails < 5 && cameraTrails < 5,
            EdgePixels = edgePixels,
            BaselineEdgeLinearMae = baselineError,
            TemporalEdgeLinearMae = temporalError,
            Improvement = improvement,
            ReferenceScale = Scale,
            Frames = 48,
            ObjectHistoryTrailPixels = objectTrails,
            CameraHistoryTrailPixels = cameraTrails,
        };
        string json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "perspective-temporal.json"), json);
        Console.WriteLine(json);
        return improvement;

        int MotionTrails(string name, Func<int, Native3DScene> frameScene, Func<int, Matrix4x4> frameCamera)
        {
            renderer.ResetTemporalHistory();
            controlRenderer.ResetTemporalHistory();
            byte[] actual = [];
            byte[] control = [];
            const int frames = 24;
            for (int frame = 0; frame < frames; frame++)
            {
                Native3DScene movingScene = frameScene(frame);
                Matrix4x4 movingCamera = frameCamera(frame);
                actual = renderer.Render(movingScene, movingCamera, eye, clear, frame == frames - 1).Pixels!;
                control = controlRenderer.Render(movingScene, movingCamera, eye, clear, frame == frames - 1).Pixels!;
            }
            byte[] expectedImage = referenceRenderer.Render(frameScene(frames - 1), frameCamera(frames - 1), eye, clear, true).Pixels!;
            int trails = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Vector3 sum = Vector3.Zero;
                    for (int sampleY = 0; sampleY < Scale; sampleY++)
                    {
                        for (int sampleX = 0; sampleX < Scale; sampleX++)
                        {
                            int source = ((y * Scale + sampleY) * Width * Scale + x * Scale + sampleX) * 4;
                            sum += new Vector3(Linear(expectedImage[source]), Linear(expectedImage[source + 1]), Linear(expectedImage[source + 2]));
                        }
                    }
                    Vector3 expected = sum / (Scale * Scale);
                    int offset = (y * Width + x) * 4;
                    if (Distance(actual, offset, expected) > Distance(control, offset, expected) + .08)
                    {
                        trails++;
                    }
                }
            }
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), Width, Height, actual);
            NativeGameGraphics.WritePng(Path.Combine(output, name + "-no-history.png"), Width, Height, control);
            if (trails >= 5)
            {
                throw new InvalidOperationException($"Perspective temporal motion retained {trails} excessive history pixels: {name}.");
            }
            return trails;
        }

        VulkanSolid3DRenderer Create(VulkanNativeFrameTarget surface, bool temporalEnabled)
        {
            return new(plant, assets.Shader("SurfaceSolid3D.v.ts"), surface,
                modelProgram: assets.Shader("SurfaceModel3D.v.ts"),
                shadowProgram: assets.Shader("Shadow3D.v.ts"),
                outputProgram: assets.Shader("ToneMap3D.v.ts"),
                temporalProgram: temporalEnabled ? assets.Shader("TemporalResolve3D.v.ts") : null,
                surfacePrograms: new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                    assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")));
        }
    }

    private static double Distance(byte[] pixels, int offset, Vector3 expected)
    {
        return Math.Abs(Linear(pixels[offset]) - expected.X)
            + Math.Abs(Linear(pixels[offset + 1]) - expected.Y)
            + Math.Abs(Linear(pixels[offset + 2]) - expected.Z);
    }

    private static float Linear(byte value)
    {
        float x = value / 255f;
        return x <= .04045f ? x / 12.92f : MathF.Pow((x + .055f) / 1.055f, 2.4f);
    }
}
