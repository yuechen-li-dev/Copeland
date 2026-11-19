using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingClearcoatProof
{
    private static readonly NativeFrameClearColor Clear = new(.055f, .055f, .055f, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    private static readonly EnvironmentRadiance3D White = new(16, 8, Enumerable.Repeat(1f, 16 * 8 * 4));

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R8 OpenPBR clearcoat"));
        Require(initialized.Success, "R8 Vulkan initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var furnace = Furnace(plant, assets);
        File.WriteAllText(Path.Combine(output, "furnace.json"), JsonSerializer.Serialize(furnace, Json));
        var rig = NeutralRig(plant, assets, output);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new { Accepted = true, Device = plant.Facts.PhysicalDeviceName, Furnace = furnace, Rig = rig }, Json));
        Console.WriteLine("AURELIAN_R8_CLEARCOAT_PASSED " + output);
    }

    private sealed record FurnaceRow(string Name, float ViewCosine, Clearcoat3D Coat, Vector3 Expected, Vector3 Gpu, Vector3 Additive);
    private sealed record FurnaceResult(int Cases, int SamplesPerPixel, float MaximumEnergy, float WorstGpuError,
        float PeakAdditiveEnergy, int AdditiveOverflows, FurnaceRow[] Rows);

    private static FurnaceResult Furnace(AurelianVulkanPlant plant, GameAssets assets)
    {
        const int samples = 1024;
        byte[] program = assets.ComputeShader("ReferenceIntegrator3D.v.ts");
        var rows = new List<FurnaceRow>();
        foreach (float nv in new[] { .055f, .21f, .56f, 1f })
        {
            var cases = new List<(string Name, ReferenceMaterial3D Material)>();
            foreach (float roughness in new[] { .045f, .19f, .42f, .87f, 1f })
            {
                cases.Add(("white-lambert", new(Vector3.One, Dielectric: 0) { Clearcoat = new(1, roughness) }));
                cases.Add(("white-dielectric", new(Vector3.One, Roughness: .21f)
                    { Clearcoat = new(.4f, roughness, 2.1f) }));
                cases.Add(("dark-copper", new(new(.35f, .075f, .017f), Roughness: .36f, Metallic: 1)
                    { Clearcoat = new(1, roughness, 1.8f) }));
                cases.Add(("black-substrate", new(Vector3.Zero, Dielectric: 0)
                    { Clearcoat = new(1, roughness, 3f) }));
            }
            cases.Add(("white-rough-diffuse", new(Vector3.One, Roughness: .93f, DiffuseModel: DirectDiffuseModel3D.Eon)
                { Clearcoat = new(1, .42f) }));
            cases.Add(("index-matched", new(Vector3.One, Dielectric: 0) { Clearcoat = new(1, .1f, 1) }));
            var results = TraceFurnace(plant, program, nv, cases.Select(item => item.Material).ToArray(), samples);
            for (int index = 0; index < cases.Count; index++)
            {
                var specimen = cases[index];
                var gpu = results[index];
                var (expected, additive) = Expected(specimen.Material, nv);
                rows.Add(new(specimen.Name, nv, specimen.Material.Clearcoat!, expected, gpu, additive));
            }
        }
        float energy = rows.Max(row => Max(row.Gpu));
        float error = rows.Max(row => Error(row.Expected, row.Gpu));
        float additivePeak = rows.Max(row => Max(row.Additive));
        int overflows = rows.Count(row => Max(row.Additive) > 1.01f);
        Console.WriteLine($"R8 furnace {rows.Count} cases, peak {energy:F4}, worst CPU/GPU error {error:F4}, additive peak {additivePeak:F4}.");
        Require(rows.All(row => float.IsFinite(Max(row.Gpu)) && MathF.Min(row.Gpu.X, MathF.Min(row.Gpu.Y, row.Gpu.Z)) >= 0),
            "Clearcoat returned invalid radiance in the white furnace.");
        var worst = rows.MaxBy(row => Error(row.Expected, row.Gpu));
        Require(energy < 1.012f && error < .023f && overflows > 12,
            $"R8 white furnace failed. Worst specimen {worst}.");
        return new(rows.Count, samples, energy, error, additivePeak, overflows, rows.ToArray());
    }

    private static Vector3[] TraceFurnace(AurelianVulkanPlant plant, byte[] program, float nv,
        IReadOnlyList<ReferenceMaterial3D> materials, int samples)
    {
        const int tile = 6;
        var triangles = new List<ReferenceTriangle3D>();
        for (int index = 0; index < materials.Count; index++)
        {
            float x = (index - materials.Count / 2f) * tile;
            Vector3 a = new(x, -tile / nv, 0);
            Vector3 b = a + new Vector3(tile, 0, 0);
            Vector3 c = b + new Vector3(0, 2 * tile / nv, 0);
            Vector3 d = a + new Vector3(0, 2 * tile / nv, 0);
            triangles.Add(new(a, b, c, materials[index]));
            triangles.Add(new(a, c, d, materials[index]));
        }
        Vector3 eye = 200 * new Vector3(0, MathF.Sqrt(1 - nv * nv), nv);
        var camera = new ReferenceCamera3D(eye, Vector3.Zero, Vector3.UnitY, .8f, tile);
        var settings = new ReferenceCapture3D(materials.Count * tile, tile, 1, 392, PixelJitter: false);
        using var capture = new ReferenceIntegrator3D(plant, program, settings, camera, triangles, [],
            lighting: new(Environment: White));
        RenderingReferenceProof.AccumulateTo(capture, samples);
        var image = capture.Capture();
        return Enumerable.Range(0, materials.Count).Select(index => Mean(image, index * tile, 0, tile, tile)).ToArray();
    }

    private static (Vector3 Layered, Vector3 Additive) Expected(ReferenceMaterial3D material, float nv)
    {
        Clearcoat3D coat = material.Clearcoat!;
        if (!coat.Active)
        {
            return (material.BaseColor, material.BaseColor);
        }
        var coordinates = IntegrateSpecular(nv, Math.Max(coat.Roughness, .045));
        double r = (coat.IndexOfRefraction - 1.0) / (coat.IndexOfRefraction + 1.0);
        double f90 = Math.Min(1, (coat.IndexOfRefraction - 1) * 10);
        double reflected = coordinates.X * r * r + coordinates.Y * f90;
        double transmission = 1 - coat.Weight * Math.Min(1, reflected + .006 * f90);
        Vector3 substrate = material.BaseColor;
        if (material.Dielectric > 0 || material.Metallic > 0)
        {
            Vector2 basis = IntegrateSpecular(nv, material.Roughness);
            Vector3 specular = (new Vector3(material.Dielectric * (1 - material.Metallic)) + material.BaseColor * material.Metallic) * basis.X
                + new Vector3(basis.Y);
            substrate = (1 - material.Metallic) * (Vector3.One - specular - new Vector3(.006f)) * material.BaseColor + specular;
        }
        var coatEnergy = new Vector3((float)(coat.Weight * reflected));
        return (substrate * (float)transmission + coatEnergy, substrate + coatEnergy);
    }

    // Integrate in the NDF slope domain independently of the VNDF used by the
    // baked LUT and GPU estimator. Sampling the half-vector resolves narrow peaks.
    private static Vector2 IntegrateSpecular(double nv, double roughness)
    {
        const int steps = 384;
        double vx = Math.Sqrt(1 - nv * nv);
        double alphaSquared = Math.Pow(roughness, 4);
        double smithView = Math.Sqrt(alphaSquared + (1 - alphaSquared) * nv * nv);
        double normal = 0;
        double grazing = 0;
        for (int polar = 0; polar < steps; polar++)
        {
            double u = (polar + .5) / steps;
            double hz = Math.Sqrt((1 - u) / (1 - u + alphaSquared * u));
            double tangent = Math.Sqrt(1 - hz * hz);
            for (int azimuth = 0; azimuth < steps; azimuth++)
            {
                double hx = tangent * Math.Cos((azimuth + .5) * Math.Tau / steps);
                double vh = Math.Min(1, nv * hz + vx * hx);
                double nl = 2 * vh * hz - nv;
                if (nl <= 0) continue;
                double smithLight = Math.Sqrt(alphaSquared + (1 - alphaSquared) * nl * nl);
                double value = 2 * vh * nl / (hz * (nl * smithView + nv * smithLight));
                double f = Math.Pow(1 - vh, 5);
                normal += value * (1 - f);
                grazing += value * f;
            }
        }
        return new((float)(normal / (steps * steps)), (float)(grazing / (steps * steps)));
    }

    private static object NeutralRig(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        const int width = 960;
        const int height = 320;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target);
        var paint = new ModelMaterial("R8/paint") { BaseColor = new(.19f, .038f, .014f, 1), Metallic = 0, Roughness = .69f, DoubleSided = true };
        var sphere = SurfaceGraphicsProof.Sphere(Vector3.Zero, .84f, paint);
        var mesh = new NativeMesh3D("R8/neutral-sphere", sphere.Vertices,
            Enumerable.Range(0, sphere.Vertices.Length).Select(index => (uint)index));
        var materials = new[] { paint, paint with { Slot = "R8/coat-sharp", Clearcoat = new(1, .23f) },
            paint with { Slot = "R8/coat-medium", Clearcoat = new(1, .38f) },
            paint with { Slot = "R8/coat-rough", Clearcoat = new(1, .61f) } };
        var baseline = Graphics3DSettings.Default with
        {
            BloomIntensity = 0, AntiAliasing = AntiAliasing3D.None,
            Shadows = false, SunDirection = Vector3.UnitZ, SunColor = Vector3.One, SunIntensity = 2,
            SkyAmbient = new(.022f), GroundAmbient = new(.022f), Exposure = .72f,
        };
        renderer.Settings = baseline;
        Vector3 eye = new(0, 0, 10);
        var projection = Matrix4x4.CreateOrthographic(8, 8f * height / width, .1f, 20);
        projection.M22 *= -1;
        Matrix4x4 clip = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * projection;
        Vector3 Center(int index) => new(2 * index - 3, 0, 0);

        Native3DScene Batches(ModelMaterial[] set) => new([], set.Select((material, index) =>
            new NativeModel3DBatch(sphere.Vertices.Select(vertex => vertex with { Position = vertex.Position + Center(index) }).ToArray(), material)).ToArray());
        Native3DScene Instances(ModelMaterial[] set) => new([], [])
        {
            Instances = new NativeInstanceSet3D(set.Select((material, index) => new SceneInstance3D("R8/ball-" + index,
                mesh, material, Matrix4x4.CreateTranslation(Center(index))))),
        };
        (Native3DFrameResult Frame, ReferenceImage3D Linear) Capture(string? name, Native3DScene scene)
        {
            renderer.ResetTemporalHistory();
            var result = renderer.Render(scene, clip, eye, Clear, capture: true);
            if (name is not null)
            {
                NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), width, height, result.Pixels!);
            }
            return (result, renderer.ReadLightingForQualification());
        }

        var direct = Capture("neutral-material-rig", Batches(materials));
        var highlight = Enumerable.Range(0, 4).Select(index => Mean(direct.Linear, 120 + index * 240 - 2, height / 2 - 2, 4, 4)).ToArray();
        var shoulder = Enumerable.Range(0, 4).Select(index => Mean(direct.Linear, 120 + index * 240 + 36, height / 2 - 2, 4, 4)).ToArray();
        Console.WriteLine($"R8 rig blue peak {highlight[0].Z:F3}/{highlight[1].Z:F3}/{highlight[2].Z:F3}/{highlight[3].Z:F3}; shoulder {shoulder[0].Z:F3}/{shoulder[1].Z:F3}/{shoulder[2].Z:F3}/{shoulder[3].Z:F3}.");
        Require(highlight[1].Z > 5 * highlight[0].Z && highlight[1].Z > highlight[2].Z * 2.5f
            && highlight[2].Z > highlight[3].Z * 1.5f && shoulder[3].Z > shoulder[1].Z * 1.5f,
            "The real neutral rig did not produce narrowing at low coat roughness and broadening at high coat roughness.");

        var none = Capture(null, Batches([paint]));
        var zero = Capture(null, Batches([paint with { Clearcoat = new(0, .23f) }]));
        var matched = Capture(null, Batches([paint with { Clearcoat = new(1, .23f, 1) }]));
        Require(zero.Frame.PixelSha256 == none.Frame.PixelSha256 && matched.Frame.PixelSha256 == none.Frame.PixelSha256,
            "Zero coverage and matched IOR did not preserve the uncoated capture.");

        renderer.EnableBindlessMaterials = true;
        var retained = Instances(materials);
        var bindless = Capture("neutral-material-bindless", retained);
        renderer.EnableVisibilityBuffer = true;
        var visibility = Capture("neutral-material-visibility", retained);
        Require(bindless.Frame.Retained is { BindlessMaterials: true } && visibility.Frame.Retained is { VisibilityBuffer: true },
            "The rig did not use the resident material and visibility paths.");
        float bindlessError = Error(Mean(direct.Linear, 356, 156, 8, 8), Mean(bindless.Linear, 356, 156, 8, 8));
        float visibilityError = Error(Mean(direct.Linear, 356, 156, 8, 8), Mean(visibility.Linear, 356, 156, 8, 8));
        Require(bindlessError < .03f && visibilityError < .03f, "Visible clearcoat was lost between native material paths.");

        renderer.Environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), White);
        renderer.Settings = baseline with { SunIntensity = 0, EnvironmentIntensity = 1 };
        var whites = materials.Select(material => material with { BaseColor = Vector4.One }).ToArray();
        var environmental = Capture("native-white-furnace", Instances(whites));
        var environmentMeans = Enumerable.Range(0, 4)
            .Select(index => Mean(environmental.Linear, 120 + index * 240 - 28, height / 2 - 28, 56, 56)).ToArray();
        Require(environmentMeans.All(pixel => pixel.X is > .98f and < 1.006f),
            "White ambient energy was not bounded by the coat partition.");
        return new { Highlight = highlight, Shoulder = shoulder, BindlessError = bindlessError, VisibilityError = visibilityError,
            EnvironmentMeans = environmentMeans, ZeroCoverageExact = true, MatchedIorExact = true };
    }

    private static Vector3 Mean(ReferenceImage3D image, int x, int y, int width, int height)
    {
        var sum = Vector3.Zero;
        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
            {
                int offset = (row * image.Width + column) * 3;
                sum += new Vector3(image.LinearRgb[offset], image.LinearRgb[offset + 1], image.LinearRgb[offset + 2]);
            }
        }
        return sum / (width * height);
    }

    private static float Max(Vector3 value) => MathF.Max(value.X, MathF.Max(value.Y, value.Z));
    private static float Error(Vector3 expected, Vector3 actual) => Max(Vector3.Abs(expected - actual));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
