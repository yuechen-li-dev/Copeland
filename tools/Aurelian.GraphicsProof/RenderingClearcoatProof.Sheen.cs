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
    private sealed record SheenFurnaceRow(string Name, float ViewCosine, Sheen3D Sheen, bool Clearcoat,
        float FiberBudget, Vector3 Expected, Vector3 Measured, Vector3 Additive);

    public static void RunSheen(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R8 OpenPBR fabric sheen"));
        Require(initialized.Success, "Sheen Vulkan initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var furnace = SheenFurnace(plant, assets);
        File.WriteAllText(Path.Combine(output, "furnace.json"), JsonSerializer.Serialize(furnace, Json));
        var rig = FabricRig(plant, assets, output);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new { Accepted = true, Device = plant.Facts.PhysicalDeviceName, Furnace = furnace, Rig = rig }, Json));
        Console.WriteLine("AURELIAN_R8_SHEEN_PASSED " + output);
    }

    private static object SheenFurnace(AurelianVulkanPlant plant, GameAssets assets)
    {
        const int samples = 3072;
        byte[] program = assets.ComputeShader("ReferenceIntegrator3D.v.ts");
        var rows = new List<SheenFurnaceRow>();
        foreach (float nv in new[] { .018f, .095f, .25f, .58f, 1f })
        {
            var cases = new List<(string Name, ReferenceMaterial3D Material)>();
            foreach (float roughness in new[] { .04f, .18f, .42f, .66f, 1f })
            {
                var white = new ReferenceMaterial3D(Vector3.One, Dielectric: 0);
                cases.Add(("white-fibers", white with { Sheen = new(Vector3.One, roughness) }));
                cases.Add(("black-fibers", white with { Sheen = new(Vector3.Zero, roughness, .72f) }));
                cases.Add(("tinted-fibers-over-coat", white with
                {
                    Clearcoat = new(.83f, .2f, 1.8f),
                    Sheen = new(new(.18f, .45f, .92f), roughness, .85f),
                }));
                cases.Add(("fiber-only-signal", new(Vector3.Zero, Dielectric: 0)
                {
                    Sheen = new(new(.12f, .54f, .9f), roughness),
                }));
                cases.Add(("white-fibers-over-coat", white with
                {
                    Clearcoat = new(1, .3f),
                    Sheen = new(Vector3.One, roughness),
                }));
            }
            var measured = TraceFurnace(plant, program, nv, cases.Select(item => item.Material).ToArray(), samples);
            for (int index = 0; index < cases.Count; index++)
            {
                var (name, material) = cases[index];
                var sheen = material.Sheen!;
                float budget = (float)FiberWeight(nv, sheen.Roughness) * sheen.Weight;
                Vector3 lower = material.Clearcoat is null ? material.BaseColor : Expected(material, nv).Layered;
                Vector3 reflected = sheen.Color * budget;
                rows.Add(new(name, nv, sheen, material.Clearcoat is not null, budget, (1 - budget) * lower + reflected,
                    measured[index], lower + reflected));
            }
        }
        float peak = rows.Max(row => Max(row.Measured));
        float additive = rows.Max(row => Max(row.Additive));
        var worst = rows.MaxBy(row => Error(row.Expected, row.Measured))!;
        float error = Error(worst.Expected, worst.Measured);
        Console.WriteLine($"R8 sheen furnace {rows.Count} cases, peak {peak:F4}, independent budget error {error:F4}, additive peak {additive:F4}.");
        Require(rows.All(row => float.IsFinite(Max(row.Measured)) && MathF.Min(row.Measured.X, MathF.Min(row.Measured.Y, row.Measured.Z)) >= 0)
            && peak < 1.014f && error < .022f && additive > 1.45f, $"Sheen furnace failed. Worst specimen {worst}.");
        return new { Cases = rows.Count, SamplesPerPixel = samples, Peak = peak, WorstError = error, AdditivePeak = additive, Rows = rows };
    }

    // Directional albedo from the published fit, evaluated in double precision
    // independently of the Vulkan sampling, Jacobian and composition.
    private static double FiberWeight(double viewCosine, double roughness)
    {
        double r = Math.Clamp(roughness, .01, 1);
        double sigma = (.0206607 * r + 1.58491 * r * r) / (.0379424 + 1.32227 * r + r * r);
        double mean = (-.193854 * r - 1.14885 * r * r + 1.7932 * Math.Pow(r, 3) - .95943 * Math.Pow(r, 5)) / (.046391 + r);
        double bias = (.000654023 * r - .0207818 * r * r + .119681 * Math.Pow(r, 3)) / (1.26264 - 1.92021 * r + r * r);
        double standardized = (viewCosine - mean) / sigma;
        double gaussian = Math.Exp(-standardized * standardized / 2) / (sigma * Math.Sqrt(2 * Math.PI));
        return Math.Clamp(gaussian + bias, 0, 1);
    }

    private static object FabricRig(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        const int width = 960;
        const int height = 320;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target);
        var settings = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, Shadows = false,
            SunDirection = Vector3.UnitZ, SunColor = Vector3.One, SunIntensity = 2,
            SkyAmbient = new(.025f), GroundAmbient = new(.025f), Exposure = 1.35f,
        };
        renderer.Settings = settings;
        var fabric = new ModelMaterial("R8/cloth") { BaseColor = new(.13f, .035f, .029f, 1), Metallic = 0, Roughness = .79f, DoubleSided = true };
        Vector3 dye = new(.13f, .48f, 1);
        var materials = new[]
        {
            fabric, fabric with { Slot = "R8/fine-fibers", Sheen = new(dye, .24f) },
            fabric with { Slot = "R8/rough-fibers", Sheen = new(dye, .57f) },
            fabric with { Slot = "R8/fibers-over-coat", Sheen = new(dye, .57f), Clearcoat = new(1, .28f) },
        };
        var sphere = SurfaceGraphicsProof.Sphere(Vector3.Zero, .84f, fabric);
        var mesh = new NativeMesh3D("R8/fabric-sphere", sphere.Vertices, Enumerable.Range(0, sphere.Vertices.Length).Select(index => (uint)index));
        var eye = new Vector3(0, 0, 10);
        var projection = Matrix4x4.CreateOrthographic(8, 8f * height / width, .1f, 20);
        projection.M22 *= -1;
        var camera = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * projection;
        Vector3 Center(int index) => new(2 * index - 3, 0, 0);
        Native3DScene Batches(ModelMaterial[] set) => new([], set.Select((material, index) => new NativeModel3DBatch(
            sphere.Vertices.Select(vertex => vertex with { Position = vertex.Position + Center(index) }).ToArray(), material)).ToArray());
        Native3DScene Instances(ModelMaterial[] set) => new([], [])
        {
            Instances = new NativeInstanceSet3D(set.Select((material, index) => new SceneInstance3D("R8/fabric-" + index,
                mesh, material, Matrix4x4.CreateTranslation(Center(index))))),
        };
        (Native3DFrameResult Frame, ReferenceImage3D Lighting) Capture(string? name, Native3DScene scene)
        {
            renderer.ResetTemporalHistory();
            var frame = renderer.Render(scene, camera, eye, Clear, capture: true);
            if (name is not null)
            {
                NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), width, height, frame.Pixels!);
            }
            return (frame, renderer.ReadLightingForQualification());
        }

        var direct = Capture("neutral-fabric-rig", Batches(materials));
        Vector3[] Ring(int offset) => Enumerable.Range(0, 4)
            .Select(index => Mean(direct.Lighting, 120 + 240 * index + offset, height / 2 - 2, 3, 4)).ToArray();
        Vector3[] centers = Ring(-1);
        Vector3[] rim = Ring(96);
        Vector3[] shoulders = Ring(79);
        Console.WriteLine($"R8 sheen rig blue rim {rim[0].Z:F3}/{rim[1].Z:F3}/{rim[2].Z:F3}/{rim[3].Z:F3}, rough shoulder {shoulders[2].Z:F3}.");
        Require(rim[1].Z > rim[0].Z + .025f && rim[2].Z > rim[0].Z + .08f
            && shoulders[2].Z > shoulders[1].Z + .025f && rim[2].Z > rim[2].X * 2
            && centers[3].Z > centers[2].Z + .2f && rim[3].Z > rim[0].Z + .08f,
            "Neutral lighting did not capture fiber tint, roughness spread and the retained clearcoat highlight.");

        var before = Capture(null, Batches([fabric]));
        var zero = Capture(null, Batches([fabric with { Sheen = new(dye, .57f, 0) }]));
        Require(before.Frame.PixelSha256 == zero.Frame.PixelSha256, "Zero sheen coverage changed the unlayered Vulkan image.");
        renderer.EnableBindlessMaterials = true;
        var retained = Instances(materials);
        var bindless = Capture("neutral-fabric-bindless", retained);
        renderer.EnableVisibilityBuffer = true;
        var visibility = Capture("neutral-fabric-visibility", retained);
        Require(bindless.Frame.Retained is { BindlessMaterials: true } && visibility.Frame.Retained is { VisibilityBuffer: true },
            "The fabric rig did not exercise the retained and visibility paths.");
        int bindingError = PixelDifference(direct.Frame.Pixels!, bindless.Frame.Pixels!);
        var visibilityDifference = CompareFabricVisibility(direct.Frame.Pixels!, visibility.Frame.Pixels!, width, height);
        Require(bindingError == 0 && visibilityDifference.ChangedCoverage == 0
            && visibilityDifference.InteriorMaximumCodeError <= 2 && visibilityDifference.MaximumCodeError <= 16
            && visibilityDifference.EdgePixelsAboveTwoCodes < width * height / 2500,
            $"The alternative Vulkan material paths changed the fabric rig. Visibility comparison {visibilityDifference}.");

        renderer.Environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), White);
        renderer.Settings = settings with { SunIntensity = 0, EnvironmentIntensity = 1 };
        var whites = materials.Select(material => material with
        {
            BaseColor = Vector4.One,
            Sheen = material.Sheen is { } sheen ? sheen with { Color = Vector3.One } : null,
        }).ToArray();
        var environmental = Capture("native-white-fabric", Instances(whites));
        var energy = Enumerable.Range(0, 4).Select(index => Mean(environmental.Lighting, 120 + index * 240 + 78, height / 2 - 12, 12, 24)).ToArray();
        Require(energy.All(value => value.X is > .983f and < 1.004f), "The Vulkan ambient resolve exceeded the fabric energy budget.");
        return new { Centers = centers, Rim = rim, Shoulders = shoulders, BindingMaximumCodeError = bindingError,
            Visibility = visibilityDifference, EnvironmentMeans = energy, ZeroCoverageExact = true };
    }

    private sealed record FabricDifference(int ChangedCoverage, int InteriorMaximumCodeError, int MaximumCodeError,
        int EdgePixelsAboveTwoCodes);

    private static FabricDifference CompareFabricVisibility(byte[] expected, byte[] actual, int width, int height)
    {
        int coverageChanges = 0;
        int interiorMaximum = 0;
        int maximum = 0;
        int edgeChanges = 0;
        bool IsBackground(byte[] pixels, int index)
            => pixels.AsSpan(index * 4, 3).SequenceEqual(expected.AsSpan(0, 3));

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int index = y * width + x;
                if (IsBackground(expected, index) != IsBackground(actual, index)) coverageChanges++;
                int delta = 0;
                for (int channel = 0; channel < 3; channel++)
                {
                    int offset = 4 * index + channel;
                    delta = Math.Max(delta, Math.Abs(expected[offset] - actual[offset]));
                }
                maximum = Math.Max(maximum, delta);
                if (delta == 0) continue;
                bool touchesSilhouette = false;
                for (int row = -1; row <= 1; row++)
                {
                    for (int column = -1; column <= 1; column++)
                    {
                        touchesSilhouette |= IsBackground(expected, index + row * width + column);
                    }
                }
                if (!touchesSilhouette) interiorMaximum = Math.Max(delta, interiorMaximum);
                else if (delta > 2) edgeChanges++;
            }
        }
        return new(coverageChanges, interiorMaximum, maximum, edgeChanges);
    }

    private static int PixelDifference(byte[] expected, byte[] actual)
    {
        int worst = 0;
        for (int offset = 0; offset < expected.Length; offset++)
        {
            worst = Math.Max(worst, Math.Abs(expected[offset] - actual[offset]));
        }
        return worst;
    }
}
