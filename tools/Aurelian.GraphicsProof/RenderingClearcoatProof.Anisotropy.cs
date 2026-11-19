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
    private sealed record AnisotropyFurnaceRow(string Name, float ViewCosine, ReferenceMaterial3D Material,
        Vector3 Expected, Vector3 Measured, Vector3 Additive);

    public static void RunAnisotropy(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R8 OpenPBR specular anisotropy"));
        Require(initialized.Success, "Anisotropy Vulkan initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var furnace = AnisotropyFurnace(plant, assets, output);
        var rig = AnisotropyRig(plant, assets, output);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Furnace = furnace,
            Rig = rig,
        }, Json));
        Console.WriteLine("AURELIAN_R8_ANISOTROPY_PASSED " + output);
    }

    private static object AnisotropyFurnace(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        const int samples = 4096;
        byte[] program = assets.ComputeShader("ReferenceIntegrator3D.v.ts");
        var rows = new List<AnisotropyFurnaceRow>();
        foreach (float nv in new[] { .015f, .055f, .18f, .55f, 1f })
        {
            var cases = new List<(string Name, ReferenceMaterial3D Material)>();
            foreach (var convention in Enum.GetValues<AnisotropyConvention3D>())
            {
                foreach ((float roughness, float strength, float rotation) in new[]
                    { (.045f, .5f, .1f), (.16f, .94f, .8f), (.42f, .8f, 0f), (.7f, .94f, .5f), (.91f, 1f, .095f), (1f, 1f, MathF.PI / 2) })
                {
                    var white = new ReferenceMaterial3D(Vector3.One, Roughness: roughness)
                    {
                        SpecularAnisotropy = new(strength, rotation, convention),
                    };
                    cases.Add(("white-dielectric", white));
                    cases.Add(("white-metal", white with { Metallic = 1 }));
                    cases.Add(("white-stack", white with { Clearcoat = new(.81f, .24f, 1.75f), Sheen = new(Vector3.One, .54f) }));
                    cases.Add(("colored-stack", white with
                    {
                        BaseColor = new(.22f, .44f, .67f), Metallic = .57f,
                        Clearcoat = new(.93f, .36f, 1.62f), Sheen = new(new(.11f, .51f, .8f), .69f, .92f),
                    }));
                }
            }
            var measured = TraceFurnace(plant, program, nv, cases.Select(item => item.Material).ToArray(), samples);
            for (int index = 0; index < cases.Count; index++)
            {
                var (name, material) = cases[index];
                var (expected, additive) = ExpectedAnisotropy(material, nv);
                rows.Add(new(name, nv, material, expected, measured[index], additive));
            }
        }
        float peak = rows.Max(row => Max(row.Measured));
        float additivePeak = rows.Max(row => Max(row.Additive));
        int additiveOverflows = rows.Count(row => Max(row.Additive) > 1.01f);
        var worst = rows.MaxBy(row => Error(row.Expected, row.Measured))!;
        float error = Error(worst.Expected, worst.Measured);
        var result = new { Cases = rows.Count, SamplesPerPixel = samples, Peak = peak, WorstError = error,
            AdditivePeak = additivePeak, AdditiveOverflows = additiveOverflows, Rows = rows };
        File.WriteAllText(Path.Combine(output, "furnace.json"), JsonSerializer.Serialize(result, Json));
        Console.WriteLine($"R8 anisotropy furnace {rows.Count} cases, peak {peak:F4}, independent error {error:F4}, additive peak {additivePeak:F4}.");
        Require(rows.All(row => float.IsFinite(Max(row.Measured)) && MathF.Min(row.Measured.X, MathF.Min(row.Measured.Y, row.Measured.Z)) >= 0)
            && peak < 1.012f && error < .035f && additiveOverflows > 30,
            $"Anisotropy white furnace failed. Worst specimen {worst}.");
        return result;
    }

    private static (Vector3 Expected, Vector3 Additive) ExpectedAnisotropy(ReferenceMaterial3D material, float nv)
    {
        var anisotropy = material.SpecularAnisotropy!;
        double alpha = (double)material.Roughness * material.Roughness;
        double flatten = 1 - anisotropy.Strength;
        double widthT = alpha * Math.Sqrt(2 / (1 + flatten * flatten));
        double widthB = Math.Max(.002025, widthT * flatten);
        if (anisotropy.Convention == AnisotropyConvention3D.Gltf)
        {
            widthT = alpha + (1 - alpha) * anisotropy.Strength * anisotropy.Strength;
            widthB = alpha;
        }
        var basis = IntegrateAnisotropicSpecular(nv, Math.PI / 2 - anisotropy.RotationRadians, widthT, widthB);
        Vector3 f0 = new Vector3(material.Dielectric * (1 - material.Metallic)) + material.BaseColor * material.Metallic;
        Vector3 specular = f0 * basis.X + new Vector3(basis.Y);
        Vector3 diffuse = material.BaseColor * (1 - material.Metallic);
        double reserve = .025 + .14 * Math.Clamp(1 - nv / .055, 0, 1) * Math.Max(1 - 18 * widthB / widthT, 0);
        var response = diffuse * Vector3.Max(Vector3.Zero, Vector3.One - specular - new Vector3((float)reserve)) + specular;
        Vector3 additive = diffuse + specular;
        if (material.Clearcoat is { } coat)
        {
            var coatBasis = IntegrateSpecular(nv, coat.Roughness);
            float r = (coat.IndexOfRefraction - 1) / (coat.IndexOfRefraction + 1);
            float albedo = coatBasis.X * r * r + coatBasis.Y;
            float transmission = 1 - coat.Weight * Math.Min(1, albedo + .006f);
            response = response * transmission + new Vector3(coat.Weight * albedo);
            additive += new Vector3(coat.Weight * albedo);
        }
        if (material.Sheen is { } sheen)
        {
            float budget = (float)FiberWeight(nv, sheen.Roughness) * sheen.Weight;
            response = (1 - budget) * response + budget * sheen.Color;
            additive += budget * sheen.Color;
        }
        return (response, additive);
    }

    // Product quadrature over GGX NDF slopes. The Vulkan path instead samples
    // visible normals and combines the GGX, diffuse, clearcoat and sheen PDFs.
    private static Vector2 IntegrateAnisotropicSpecular(double nv, double azimuth, double widthT, double widthB)
    {
        const int radialSteps = 512;
        const int azimuthSteps = 384;
        double vx = Math.Sqrt(1 - nv * nv) * Math.Cos(azimuth);
        double vy = Math.Sqrt(1 - nv * nv) * Math.Sin(azimuth);
        double smithView = Math.Sqrt(Math.Pow(widthT * vx, 2) + Math.Pow(widthB * vy, 2) + nv * nv);
        double normal = 0;
        double grazing = 0;
        for (int angle = 0; angle < azimuthSteps; angle++)
        {
            var (sine, cosine) = Math.SinCos((angle + .5) * Math.Tau / azimuthSteps);
            double tx = widthT * cosine;
            double ty = widthB * sine;
            for (int radial = 0; radial < radialSteps; radial++)
            {
                double q = (radial + .5) / radialSteps;
                double slope = Math.Sqrt(q / (1 - q));
                double x = slope * tx;
                double y = slope * ty;
                double hz = 1 / Math.Sqrt(1 + x * x + y * y);
                double vh = Math.Min(1, (vx * x + vy * y + nv) * hz);
                double nl = 2 * vh * hz - nv;
                if (nl <= 0) continue;
                double lx = widthT * (2 * vh * x * hz - vx);
                double ly = widthB * (2 * vh * y * hz - vy);
                double smithLight = Math.Sqrt(lx * lx + ly * ly + nl * nl);
                double value = 2 * vh * nl / (hz * (nl * smithView + nv * smithLight));
                double f = Math.Pow(1 - vh, 5);
                normal += value * (1 - f);
                grazing += value * f;
            }
        }
        return new((float)(normal / (radialSteps * azimuthSteps)), (float)(grazing / (radialSteps * azimuthSteps)));
    }

    private sealed record Highlight(float Horizontal, float Vertical)
    {
        public float Ratio => Horizontal / Vertical;
    }

    private static object AnisotropyRig(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        const int width = 960;
        const int height = 320;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target);
        var settings = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, Shadows = false,
            SunDirection = Vector3.UnitZ, SunColor = Vector3.One, SunIntensity = 2,
            SkyAmbient = new(.018f), GroundAmbient = new(.018f), Exposure = .83f,
        };
        renderer.Settings = settings;
        var brush = new ModelMaterial("R8/brushed") { BaseColor = new(.58f, .41f, .26f, 1), Metallic = .75f, Roughness = .5f, DoubleSided = true };
        var layered = brush with { Clearcoat = new(.9f, .24f, 1.65f), Sheen = new(new(.12f, .43f, .8f), .49f, .87f) };
        var sphere = SurfaceGraphicsProof.Sphere(Vector3.Zero, .84f, brush);
        var vertices = sphere.Vertices.Select(vertex =>
        {
            Vector3 normal = vertex.Normal;
            Vector3 axis = MathF.Abs(normal.X) < .999f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 tangent = Vector3.Normalize(axis - normal * Vector3.Dot(axis, normal));
            return vertex with { Tangent = new(tangent, 1) };
        }).ToArray();
        var mesh = new NativeMesh3D("R8/brushed-sphere", vertices, Enumerable.Range(0, vertices.Length).Select(index => (uint)index));
        var eye = new Vector3(0, 0, 200);
        var projection = Matrix4x4.CreateOrthographic(8, 8f * height / width, .1f, 250);
        projection.M22 *= -1;
        var camera = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * projection;
        Vector3 Center(int index) => new(2 * index - 3, 0, 0);
        ModelMaterial[] Materials(float rotation) =>
        [
            brush with { SpecularAnisotropy = new(.86f, rotation) },
            layered with { SpecularAnisotropy = new(.86f, rotation) },
            layered with { SpecularAnisotropy = new(.76f, rotation, AnisotropyConvention3D.Gltf) },
            layered with { SpecularAnisotropy = new(0, rotation) },
        ];
        Native3DScene Batches(ModelMaterial[] set) => new([], set.Select((material, index) => new NativeModel3DBatch(
            vertices.Select(vertex => vertex with { Position = vertex.Position + Center(index) }).ToArray(), material)).ToArray());
        Native3DScene Instances(ModelMaterial[] set) => new([], [])
        {
            Instances = new NativeInstanceSet3D(set.Select((material, index) => new SceneInstance3D("R8/brushed-" + index,
                mesh, material, Matrix4x4.CreateTranslation(Center(index))))),
        };
        (Native3DFrameResult Frame, ReferenceImage3D Lighting) Capture(string? name, Native3DScene scene)
        {
            renderer.ResetTemporalHistory();
            var frame = renderer.Render(scene, camera, eye, Clear, capture: true);
            if (name is not null) NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), width, height, frame.Pixels!);
            return (frame, renderer.ReadLightingForQualification());
        }
        Highlight Measure(ReferenceImage3D image, int index)
        {
            const int shoulder = 23;
            int x = 120 + 240 * index - 2;
            int y = height / 2 - 2;
            float horizontal = (Mean(image, x + shoulder, y, 4, 4).X + Mean(image, x - shoulder, y, 4, 4).X) * .5f;
            float vertical = (Mean(image, x, y + shoulder, 4, 4).X + Mean(image, x, y - shoulder, 4, 4).X) * .5f;
            return new(horizontal, vertical);
        }

        var materials = Materials(0);
        var horizontal = Capture("neutral-anisotropy-0", Batches(materials));
        var vertical = Capture("neutral-anisotropy-90", Batches(Materials(MathF.PI / 2)));
        var alongTangent = Enumerable.Range(0, 4).Select(index => Measure(horizontal.Lighting, index)).ToArray();
        var alongBitangent = Enumerable.Range(0, 4).Select(index => Measure(vertical.Lighting, index)).ToArray();
        Console.WriteLine($"R8 highlight H/V at 0° {string.Join(", ", alongTangent.Select(value => value.Ratio.ToString("F3")))}"
            + $" and 90° {string.Join(", ", alongBitangent.Select(value => value.Ratio.ToString("F3")))}.");
        Require(Enumerable.Range(0, 3).All(index => alongTangent[index].Ratio > 1.4f && alongBitangent[index].Ratio < .72f)
            && Math.Abs(alongTangent[3].Ratio - alongBitangent[3].Ratio) < .0001f,
            "A 90-degree rotation did not exchange the lit highlight shoulders in every anisotropic material.");
        materials[3] = layered;
        var disabled = Capture(null, Batches(materials));
        Require(horizontal.Frame.PixelSha256 == disabled.Frame.PixelSha256, "A zero factor changed the Vulkan image.");

        renderer.EnableBindlessMaterials = true;
        var retained = Instances(materials);
        var bindless = Capture("neutral-anisotropy-bindless", retained);
        renderer.EnableVisibilityBuffer = true;
        var visibility = Capture("neutral-anisotropy-visibility", retained);
        Require(bindless.Frame.Retained is { BindlessMaterials: true } && visibility.Frame.Retained is { VisibilityBuffer: true },
            "The brush rig did not exercise bindless raster and visibility resolve.");
        int bindlessError = PixelDifference(horizontal.Frame.Pixels!, bindless.Frame.Pixels!);
        var visibilityDifference = CompareFabricVisibility(horizontal.Frame.Pixels!, visibility.Frame.Pixels!, width, height);
        var bindlessShoulders = Enumerable.Range(0, 4).Select(index => Measure(bindless.Lighting, index)).ToArray();
        var visibilityShoulders = Enumerable.Range(0, 4).Select(index => Measure(visibility.Lighting, index)).ToArray();
        // Barycentric reconstruction can round the packed tangent differently
        // at a few pixels. Also check the linear HDR ratios independently of tone mapping.
        bool highlightAgreement = Enumerable.Range(0, 4).All(index =>
            Math.Abs(bindlessShoulders[index].Ratio / alongTangent[index].Ratio - 1) < .01f
            && Math.Abs(visibilityShoulders[index].Ratio / alongTangent[index].Ratio - 1) < .01f);
        Require(bindlessError <= 1 && visibilityDifference.ChangedCoverage == 0 && highlightAgreement
            && visibilityDifference.InteriorMaximumCodeError <= 3 && visibilityDifference.MaximumCodeError <= 8
            && visibilityDifference.EdgePixelsAboveTwoCodes < width * height / 2500,
            $"The alternate material paths changed the brush highlight. Bindless {bindlessError}, visibility {visibilityDifference}.");

        renderer.Environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), White);
        renderer.Settings = settings with { SunIntensity = 0, EnvironmentIntensity = 1 };
        var whiteMaterials = materials.Select(material => material with
        {
            BaseColor = Vector4.One, Metallic = 0,
            Sheen = material.Sheen is { } sheen ? sheen with { Color = Vector3.One } : null,
        }).ToArray();
        var ambient = Capture("native-white-anisotropy", Instances(whiteMaterials));
        var energy = Enumerable.Range(0, 4).Select(index =>
            Mean(ambient.Lighting, 120 + index * 240 + 78, height / 2 - 12, 12, 24)).ToArray();
        Require(energy.All(value => value.X is > .983f and < 1.004f), "The real Vulkan environment resolve changed the white energy budget.");
        return new { AlongTangent = alongTangent, AlongBitangent = alongBitangent,
            BindlessShoulders = bindlessShoulders, VisibilityShoulders = visibilityShoulders, EnvironmentEnergy = energy,
            BindlessMaximumCodeError = bindlessError, Visibility = visibilityDifference, ZeroStrengthExact = true };
    }
}
