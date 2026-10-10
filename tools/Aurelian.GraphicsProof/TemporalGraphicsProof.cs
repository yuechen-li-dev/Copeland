using System.Numerics;
using System.Collections.Immutable;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static class TemporalGraphicsProof
{
    private const int Size = 192;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian temporal graphics proof"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Size, Size);
        using var renderer = Create(target, true);
        using var referenceTarget = new VulkanNativeFrameTarget(plant, Size * 4, Size * 4);
        using var referenceRenderer = Create(referenceTarget, false);
        NativeFrameClearColor clear = new(0, 0, 0, 1);
        Native3DVertex[] scene = Scene(0);
        renderer.Settings = Graphics3DSettings.Basic;
        byte[] baseline = renderer.Render(scene, Matrix4x4.Identity, clear, true).Pixels!;
        referenceRenderer.Settings = Graphics3DSettings.Basic;
        byte[] supersampled = referenceRenderer.Render(scene, Matrix4x4.Identity, clear, true).Pixels!;
        float[] reference = Downsample(supersampled);
        renderer.Settings = Graphics3DSettings.Basic with { AntiAliasing = AntiAliasing3D.Temporal };
        renderer.ResetTemporalHistory();
        var frames = new List<Native3DFrameResult>();
        for (int frame = 0; frame < 48; frame++)
            frames.Add(renderer.Render(scene, Matrix4x4.Identity, clear, true));
        byte[] temporal = frames[^1].Pixels!;
        double baselineError = Error(baseline, reference);
        double temporalError = Error(temporal, reference);
        Save("none", baseline);
        Save("taa", temporal);
        NativeGameGraphics.WritePng(Path.Combine(output, "reference-4x.png"), Size * 4, Size * 4, supersampled);

        // Corresponding world positions move independently of the camera. Test
        // trails directly beside the departing silhouette, including its first pixel.
        renderer.ResetTemporalHistory();
        Native3DFrameResult moving = frames[^1];
        for (int frame = 0; frame < 24; frame++)
            moving = renderer.Render(Scene(-.4f + frame * .025f, movingOnly: true), Matrix4x4.Identity, clear, true);
        byte[] movingSupersampled = referenceRenderer.Render(Scene(-.4f + 23 * .025f, movingOnly: true), Matrix4x4.Identity, clear, true).Pixels!;
        NativeGameGraphics.WritePng(Path.Combine(output, "moving-reference-4x.png"), Size * 4, Size * 4, movingSupersampled);
        float[] movingReference = Downsample(movingSupersampled);
        using var controlTarget = new VulkanNativeFrameTarget(plant, Size, Size);
        using var controlRenderer = Create(controlTarget, true);
        controlRenderer.Settings = Graphics3DSettings.Basic with { AntiAliasing = AntiAliasing3D.Temporal, TemporalHistoryWeight = 0 };
        byte[] movingControl = [];
        for (int frame = 0; frame < 24; frame++)
            movingControl = controlRenderer.Render(Scene(-.4f + frame * .025f, movingOnly: true), Matrix4x4.Identity, clear, true).Pixels!;
        Save("moving-no-history", movingControl);
        int trails = 0;
        int excessHistory = 0;
        for (int pixel = 0; pixel < Size * Size; pixel++)
        {
            int index = pixel * 3;
            float expected = movingReference[index] + movingReference[index + 1] + movingReference[index + 2];
            float actual = Linear(moving.Pixels![pixel * 4]) + Linear(moving.Pixels[pixel * 4 + 1]) + Linear(moving.Pixels[pixel * 4 + 2]);
            if (expected < .005f && actual > .08f) trails++;
            float withoutHistory = Linear(movingControl[pixel * 4]) + Linear(movingControl[pixel * 4 + 1]) + Linear(movingControl[pixel * 4 + 2]);
            if (expected < .005f && actual > withoutHistory + .08f) excessHistory++;
        }
        Save("moving-taa", moving.Pixels!);

        // Camera motion must reproject stationary world geometry through the
        // previous view, using the same reference and no-history control.
        renderer.ResetTemporalHistory();
        controlRenderer.ResetTemporalHistory();
        byte[] cameraImage = [];
        byte[] cameraControl = [];
        Matrix4x4 movingCamera = Matrix4x4.Identity;
        Native3DVertex[] stationaryWorld = Scene(0, movingOnly: true);
        for (int frame = 0; frame < 24; frame++)
        {
            movingCamera = Matrix4x4.CreateTranslation(-.4f + frame * .025f, 0, 0);
            cameraImage = renderer.Render(stationaryWorld, movingCamera, clear, true).Pixels!;
            cameraControl = controlRenderer.Render(stationaryWorld, movingCamera, clear, true).Pixels!;
        }
        float[] cameraReference = Downsample(referenceRenderer.Render(stationaryWorld, movingCamera, clear, true).Pixels!);
        int cameraTrails = HistoryTrails(cameraImage, cameraControl, cameraReference);
        Save("camera-motion", cameraImage);

        // An explicit cut must discard every old sample, even if geometry counts match.
        renderer.ResetTemporalHistory();
        Matrix4x4 cutCamera = Matrix4x4.CreateTranslation(.5f, 0, 0);
        byte[] cut = renderer.Render(stationaryWorld, cutCamera, clear, true).Pixels!;
        Save("camera-cut", cut);
        controlRenderer.ResetTemporalHistory();
        byte[] freshCut = controlRenderer.Render(stationaryWorld, cutCamera, clear, true).Pixels!;
        Require(cut.SequenceEqual(freshCut), "Camera cut retained stale color history.");
        // Exercise the actual compute-skinned buffer and GPU-to-GPU previous
        // position copy, rather than a CPU imitation of animated geometry.
        Native3DVertex[] restVertices = Scene(0, movingOnly: true);
        float[] rest = new float[restVertices.Length * 24];
        for (int vertex = 0; vertex < restVertices.Length; vertex++)
        {
            int offset = vertex * 24;
            Vector3 point = restVertices[vertex].Position;
            rest[offset] = point.X * 1000;
            rest[offset + 1] = -point.Z * 1000;
            rest[offset + 2] = point.Y * 1000;
            rest[offset + 5] = 1;
            rest[offset + 7] = 1;
        }
        using var skinning = new VulkanSkinning3D(plant, assets.ComputeShader("HumanoidSkinning.v.ts"), rest, restVertices.Length, 1);
        float[] palette = [0, 0, 0, 1, 0, 0, 0, 0];
        renderer.ResetTemporalHistory();
        controlRenderer.ResetTemporalHistory();
        byte[] gpuImage = [];
        byte[] gpuControl = [];
        for (int frame = 0; frame < 24; frame++)
        {
            skinning.Deform(palette, 0, 0, Matrix4x4.CreateTranslation(-.4f + frame * .025f, 0, 0));
            gpuImage = renderer.Render([], Matrix4x4.Identity, clear, true, gpuGeometry: skinning.Geometry).Pixels!;
            gpuControl = controlRenderer.Render([], Matrix4x4.Identity, clear, true, gpuGeometry: skinning.Geometry).Pixels!;
        }
        float[] gpuReference = Downsample(referenceRenderer.Render([], Matrix4x4.Identity, clear, true, gpuGeometry: skinning.Geometry).Pixels!);
        int gpuTrails = HistoryTrails(gpuImage, gpuControl, gpuReference);
        Save("gpu-skinned-motion", gpuImage);

        // At extreme minification an alternating sRGB checker must resolve to
        // linear half-grey across the surface, independent of texel phase.
        byte[] checker = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                int offset = (y * 32 + x) * 4;
                byte color = (x + y) % 2 == 0 ? (byte)255 : (byte)0;
                checker[offset] = color;
                checker[offset + 1] = color;
                checker[offset + 2] = color;
                checker[offset + 3] = 255;
            }
        }
        ModelMaterial checkMaterial = new("mip-check")
        {
            Unlit = true,
            DoubleSided = true,
            BaseColorTexture = new(new("mip-check", 32, 32, checker.ToImmutableArray()), new()),
        };
        NativeModel3DVertex Vertex(float x, float y, float u, float v) =>
            new(new(x, y, .3f), Vector3.UnitY, Vector4.One, new(u, v), new(1, 0, 0, 1));
        NativeModel3DVertex[] plane = [Vertex(-.8f, -.8f, 0, 0), Vertex(.8f, -.8f, 173.35f, 0),
            Vertex(.8f, .8f, 173.35f, 155.7f), Vertex(-.8f, -.8f, 0, 0),
            Vertex(.8f, .8f, 173.35f, 155.7f), Vertex(-.8f, .8f, 0, 155.7f)];
        renderer.Settings = Graphics3DSettings.Basic;
        byte[] mipImage = renderer.Render(new Native3DScene([], [new(plane, checkMaterial)]),
            Matrix4x4.Identity, Vector3.Zero, clear, true).Pixels!;
        int mipError = 0;
        for (int y = Size / 4; y < Size * 3 / 4; y++)
            for (int x = Size / 4; x < Size * 3 / 4; x++)
                mipError = Math.Max(mipError, Math.Abs(mipImage[(y * Size + x) * 4] - 188));
        Save("gpu-mips-linear-grey", mipImage);
        var times = frames.Skip(8).SelectMany(frame => frame.GpuPassTimes)
            .GroupBy(time => time.Pass).Select(group => new { Pass = group.Key, AverageMilliseconds = group.Average(time => time.Milliseconds) });
        bool accepted = temporalError < baselineError * .85 && excessHistory < 5
            && cameraTrails < 5 && gpuTrails < 5 && mipError <= 2;
        var evidence = new
        {
            Accepted = accepted,
            Device = plant.Facts.PhysicalDeviceName,
            Width = Size,
            Height = Size,
            ReferenceScale = 4,
            BaselineLinearMae = baselineError,
            TemporalLinearMae = temporalError,
            Improvement = 1 - temporalError / baselineError,
            OutsideCoveragePixels = trails,
            HistoryTrailPixels = excessHistory,
            CameraMotionTrailPixels = cameraTrails,
            GpuSkinningTrailPixels = gpuTrails,
            MipMaximumSrgbByteError = mipError,
            Gpu = times.ToArray(),
        };
        string json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(evidencePath, json);
        Console.WriteLine(json);
        Require(accepted, "Temporal image quality failed; see supersampled error and silhouette trail evidence.");

        VulkanSolid3DRenderer Create(VulkanNativeFrameTarget surface, bool post) => new(plant,
            assets.Shader("Solid3D.v.ts"), surface, modelProgram: assets.Shader("StaticModel3D.v.ts"),
            shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"),
            temporalProgram: post ? assets.Shader("TemporalResolve3D.v.ts") : null,
            bloomProgram: post ? assets.Shader("Bloom3D.v.ts") : null);
        void Save(string name, byte[] pixels) => NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), Size, Size, pixels);
    }

    private static int HistoryTrails(byte[] actual, byte[] control, float[] reference)
    {
        int count = 0;
        for (int pixel = 0; pixel < Size * Size; pixel++)
        {
            float expected = reference[pixel * 3] + reference[pixel * 3 + 1] + reference[pixel * 3 + 2];
            float excess = 0;
            for (int channel = 0; channel < 3; channel++)
                excess += Linear(actual[pixel * 4 + channel]) - Linear(control[pixel * 4 + channel]);
            if (expected < .005f && excess > .08f) count++;
        }
        return count;
    }

    private static Native3DVertex[] Scene(float movement, bool movingOnly = false)
    {
        var vertices = new List<Native3DVertex>();
        if (!movingOnly)
        {
            Quad(new(-.84f, -.72f), new(.7f, .41f), .017f, 0, new(.15f, .3f, 1, 1));
            Quad(new(-.7f, .63f), new(.68f, -.46f), .013f, 0, Vector4.One);
        }
        Quad(new(-.27f, -.55f), new(.04f, .48f), .085f, movement, new(1, .3f, .08f, 1));
        return vertices.ToArray();

        void Quad(Vector2 a, Vector2 b, float halfWidth, float x, Vector4 color)
        {
            Vector2 normal = Vector2.Normalize(new(-(b - a).Y, (b - a).X)) * halfWidth;
            Vector3 p = new(a.X + normal.X + x, a.Y + normal.Y, .3f);
            Vector3 q = new(a.X - normal.X + x, a.Y - normal.Y, .3f);
            Vector3 r = new(b.X - normal.X + x, b.Y - normal.Y, .3f);
            Vector3 s = new(b.X + normal.X + x, b.Y + normal.Y, .3f);
            foreach (Vector3 point in new[] { p, q, r, p, r, s }) vertices.Add(new(point, Vector3.UnitY, color));
        }
    }

    private static float[] Downsample(byte[] source)
    {
        var result = new float[Size * Size * 3];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 sum = Vector3.Zero;
                for (int sampleY = 0; sampleY < 4; sampleY++)
                {
                    for (int sampleX = 0; sampleX < 4; sampleX++)
                    {
                        int sourcePixel = (y * 4 + sampleY) * Size * 4 + x * 4 + sampleX;
                        int offset = sourcePixel * 4;
                        sum += new Vector3(Linear(source[offset]), Linear(source[offset + 1]), Linear(source[offset + 2]));
                    }
                }
                int destination = (y * Size + x) * 3;
                result[destination] = sum.X / 16;
                result[destination + 1] = sum.Y / 16;
                result[destination + 2] = sum.Z / 16;
            }
        }
        return result;
    }

    private static double Error(byte[] image, float[] reference)
    {
        double error = 0;
        for (int pixel = 0; pixel < Size * Size; pixel++)
            for (int channel = 0; channel < 3; channel++)
                error += Math.Abs(Linear(image[pixel * 4 + channel]) - reference[pixel * 3 + channel]);
        return error / reference.Length;
    }

    private static float Linear(byte value)
    {
        float x = value / 255f;
        return x <= .04045f ? x / 12.92f : MathF.Pow((x + .055f) / 1.055f, 2.4f);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
