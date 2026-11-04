using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingReferenceLightingProof
{
    private static object CheckTemporal(AurelianVulkanPlant plant, GameAssets assets, byte[] program, string output, List<MetricPair> pairs)
    {
        const int width = 96;
        const int height = 64;
        const int frames = 12;
        var model = new ModelMaterial("temporal-surface") { BaseColor = new(.65f, .5f, .3f, 1), Metallic = 0, Roughness = .65f, DoubleSided = true };
        var source = new List<Native3DVertex>();
        PrimitiveGeometry3D.AddBox(source, Vector3.Zero, Vector3.One, Vector4.One);
        for (int vertex = 0; vertex < source.Count; vertex += 3)
        {
            if (Vector3.Dot(Vector3.Cross(source[vertex + 1].Position - source[vertex].Position,
                source[vertex + 2].Position - source[vertex].Position), source[vertex].Normal) < 0)
                (source[vertex + 1], source[vertex + 2]) = (source[vertex + 2], source[vertex + 1]);
        }
        var mesh = new NativeMesh3D("temporal-box", source.Select(vertex => new NativeModel3DVertex(vertex.Position,
            vertex.Normal, Vector4.One, Vector2.Zero, new(1, 0, 0, 1))), Enumerable.Range(0, source.Count).Select(index => (uint)index));
        var settings = Graphics3DSettings.Default with
        {
            DirectDiffuseModel = DirectDiffuseModel3D.Lambert, SunDirection = Vector3.Normalize(new(.5f, 1, .25f)),
            SunColor = Vector3.One, SunIntensity = 3, SkyAmbient = Vector3.Zero, GroundAmbient = Vector3.Zero,
            EnvironmentIntensity = 0, BloomIntensity = 0, AmbientOcclusionStrength = 0, Shadows = true,
            Exposure = 1, ToneMapColorRetention = 0, ToneMapGamutCompression = false,
        };
        var lighting = ReferenceLighting3D.FromSettings(settings);
        var observations = new List<object>();
        foreach (string sequence in new[] { "static", "camera-pan", "moving-occluder" })
        {
            using var target = new VulkanNativeFrameTarget(plant, width, height);
            using var jitterTarget = new VulkanNativeFrameTarget(plant, width, height);
            using var noneTarget = new VulkanNativeFrameTarget(plant, width, height);
            using var temporal = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true);
            using var jitter = RenderingBindlessProof.CreateRenderer(plant, assets, jitterTarget, enableCulling: true);
            using var none = RenderingBindlessProof.CreateRenderer(plant, assets, noneTarget, enableCulling: true);
            temporal.Settings = settings with { AntiAliasing = AntiAliasing3D.Temporal };
            jitter.Settings = settings with { AntiAliasing = AntiAliasing3D.Temporal, TemporalHistoryWeight = 0 };
            none.Settings = settings with { AntiAliasing = AntiAliasing3D.None };
            Native3DScene initial = SceneAt(0, sequence);
            var firstCamera = CameraAt(0, sequence);
            Matrix4x4 firstView = Matrix(firstCamera);
            for (int warmup = 0; warmup < 16; warmup++)
            {
                temporal.Render(initial, firstView, firstCamera.Eye, new(0, 0, 0, 1), capture: true);
                jitter.Render(initial, firstView, firstCamera.Eye, new(0, 0, 0, 1), capture: true);
                none.Render(initial, firstView, firstCamera.Eye, new(0, 0, 0, 1), capture: true);
            }
            ReferenceImage3D? staticReference = null;
            var referenceFrames = new List<byte[]>();
            var temporalFrames = new List<byte[]>();
            var jitterFrames = new List<byte[]>();
            var noneFrames = new List<byte[]>();
            for (int frame = 0; frame < frames; frame++)
            {
                Native3DScene scene = SceneAt(frame, sequence);
                ReferenceCamera3D camera = CameraAt(frame, sequence);
                ReferenceImage3D reference = staticReference ?? Capture(plant, program,
                    new(width, height, 1, 197), camera, ReferenceGeometry(scene), lighting, 1024);
                if (sequence == "static") staticReference = reference;
                string referenceFile = Save(plant, assets, output, $"{sequence}-{frame:D2}-reference", reference);
                byte[] referencePixels = VulkanReferenceOutput3D.ToneMap(plant, assets.Shader("ToneMap3D.v.ts"), reference);
                referenceFrames.Add(referencePixels);
                Matrix4x4 matrix = Matrix(camera);
                byte[] taa = temporal.Render(scene, matrix, camera.Eye, new(0, 0, 0, 1), capture: true).Pixels!;
                byte[] noHistory = jitter.Render(scene, matrix, camera.Eye, new(0, 0, 0, 1), capture: true).Pixels!;
                byte[] baseline = none.Render(scene, matrix, camera.Eye, new(0, 0, 0, 1), capture: true).Pixels!;
                temporalFrames.Add(taa);
                jitterFrames.Add(noHistory);
                noneFrames.Add(baseline);
                foreach (var variant in new[] { (Name: "taa", Pixels: taa), (Name: "jitter", Pixels: noHistory), (Name: "none", Pixels: baseline) })
                {
                    string file = $"{sequence}-{frame:D2}-{variant.Name}.png";
                    NativeGameGraphics.WritePng(Path.Combine(output, file), width, height, variant.Pixels);
                    pairs.Add(new($"{sequence}-{frame:D2}-{variant.Name}", referenceFile, file, sequence + "-" + variant.Name));
                }
            }
            double taaFlicker = ResidualFlicker(temporalFrames, referenceFrames);
            double jitterFlicker = ResidualFlicker(jitterFrames, referenceFrames);
            double noneFlicker = ResidualFlicker(noneFrames, referenceFrames);
            double taaError = SequenceError(temporalFrames, referenceFrames);
            double jitterError = SequenceError(jitterFrames, referenceFrames);
            double noneError = SequenceError(noneFrames, referenceFrames);
            if (sequence == "static")
                Require(taaFlicker < jitterFlicker * .8 && taaError < noneError, "Static TAA failed reference-based improvement.");
            else
                Require(taaFlicker < jitterFlicker * 1.2, "Temporal motion residual regressed against the no-history jitter control.");
            observations.Add(new { Sequence = sequence, Frames = frames, SamplesPerReference = 1024,
                TemporalResidualFlicker = taaFlicker, JitterResidualFlicker = jitterFlicker, NoAaResidualFlicker = noneFlicker,
                TemporalMeanDisplayLinearError = taaError, JitterMeanDisplayLinearError = jitterError, NoAaMeanDisplayLinearError = noneError });
            Console.WriteLine($"Temporal {sequence}: residual TAA {taaFlicker:F6}, jitter {jitterFlicker:F6}; error TAA {taaError:F6}, no AA {noneError:F6}");
            temporal.ResetTemporalHistory();
            Native3DScene cutScene = SceneAt(0, "static");
            ReferenceCamera3D cutCamera = firstCamera with { Eye = new(-3, 2, 4) };
            byte[] cut = temporal.Render(cutScene, Matrix(cutCamera), cutCamera.Eye, new(0, 0, 0, 1), capture: true).Pixels!;
            using var freshTarget = new VulkanNativeFrameTarget(plant, width, height);
            using var fresh = RenderingBindlessProof.CreateRenderer(plant, assets, freshTarget, enableCulling: true);
            fresh.Settings = temporal.Settings;
            byte[] freshCut = fresh.Render(cutScene, Matrix(cutCamera), cutCamera.Eye, new(0, 0, 0, 1), capture: true).Pixels!;
            Require(cut.SequenceEqual(freshCut), "Camera cut retained prior sequence history.");
        }
        return new { Sequences = observations, CameraCutsByteIdenticalToFreshRenderer = true,
            Metric = "Mean absolute delta of display-linear error residual: |(I[t]-I[t-1])-(R[t]-R[t-1])|; no reprojection" };

        Native3DScene SceneAt(int frame, string sequence)
        {
            var instances = new List<SceneInstance3D>();
            Add("floor", new(2.5f, .05f, 2), new(0, -.05f, 0));
            for (int bar = 0; bar < 7; bar++) Add("bar-" + bar, new(.018f, .75f, .018f), new(-1.1f + bar * .32f, .75f, -.3f));
            float x = sequence == "moving-occluder" ? -.8f + frame * .14f : 0;
            float previousX = sequence == "moving-occluder" ? -.8f + Math.Max(frame - 1, 0) * .14f : x;
            Matrix4x4 transform = Matrix4x4.CreateScale(.23f, .4f, .23f) * Matrix4x4.CreateTranslation(x, .4f, .5f);
            instances.Add(new("occluder", mesh, model, transform)
            {
                PreviousTransform = Matrix4x4.CreateScale(.23f, .4f, .23f) * Matrix4x4.CreateTranslation(previousX, .4f, .5f),
            });
            return new([], []) { Instances = instances, TemporalRevision = "reference-temporal-v1",
                LightingRevision = sequence == "moving-occluder" ? sequence + frame : "reference-static-sun" };

            void Add(string id, Vector3 size, Vector3 position)
            {
                instances.Add(new(id, mesh, model, Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(position)));
            }
        }
        ReferenceCamera3D CameraAt(int frame, string sequence) => new(new(3 + (sequence == "camera-pan" ? frame * .035f : 0), 2.5f, 5),
            new(0, .5f, 0), Vector3.UnitY, .8f);
        Matrix4x4 Matrix(ReferenceCamera3D camera)
        {
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)width / height, .1f, 100);
            projection.M22 *= -1;
            return Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        }
    }

    private static ReferenceTriangle3D[] ReferenceGeometry(Native3DScene scene)
    {
        var result = new List<ReferenceTriangle3D>();
        foreach (SceneInstance3D instance in scene.Instances)
        {
            var material = new ReferenceMaterial3D(new(instance.Material.BaseColor.X, instance.Material.BaseColor.Y, instance.Material.BaseColor.Z),
                instance.Material.Roughness, instance.Material.Metallic);
            for (int index = 0; index < instance.Mesh.Indices.Length; index += 3)
            {
                Vector3 A(int lane) => Vector3.Transform(instance.Mesh.Vertices[(int)instance.Mesh.Indices[index + lane]].Position, instance.Transform);
                result.Add(new(A(0), A(1), A(2), material));
            }
        }
        return result.ToArray();
    }

    private static double SequenceError(List<byte[]> images, List<byte[]> references)
    {
        double error = 0;
        for (int frame = 0; frame < images.Count; frame++)
            for (int index = 0; index < images[frame].Length; index++)
                if (index % 4 < 3) error += Math.Abs(DisplayLinear(images[frame][index]) - DisplayLinear(references[frame][index]));
        return error / (images.Count * images[0].Length * .75);
    }

    private static double ResidualFlicker(List<byte[]> images, List<byte[]> references)
    {
        double error = 0;
        for (int frame = 1; frame < images.Count; frame++)
        {
            for (int index = 0; index < images[frame].Length; index++)
            {
                if (index % 4 == 3) continue;
                double actual = DisplayLinear(images[frame][index]) - DisplayLinear(images[frame - 1][index]);
                double expected = DisplayLinear(references[frame][index]) - DisplayLinear(references[frame - 1][index]);
                error += Math.Abs(actual - expected);
            }
        }
        return error / ((images.Count - 1) * images[0].Length * .75);
    }

    private static double DisplayLinear(byte value)
    {
        double encoded = value / 255.0;
        return encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
    }
}
