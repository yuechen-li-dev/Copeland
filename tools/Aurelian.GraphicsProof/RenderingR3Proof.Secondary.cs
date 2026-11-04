using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Assets.Models;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    public static void RunSecondary(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R4 secondary materials"));
        Require(initialized.Success, "Secondary materials initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var camera = new ReferenceCamera3D(new(4.3f, 3.3f, 7), new(0, .8f, 0), Vector3.UnitY, .65f);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)Width / Height, .1f, 100);
        projection.M22 *= -1;
        var matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        var grid = new DiffuseProbeGrid(new(-1.7f, .2f, -.8f), new(1.7f, 2.5f, 1.7f), 5, 3, 5, 128);
        var rows = new List<object>();
        var pairs = new List<object>();
        foreach (string specimen in new[] { "albedo", "emissive", "smooth", "normal-map", "all-channels", "negative-tangent", "analytic-sphere", "analytic-box-reflected", "imported-glb" })
        {
            var scene = SecondaryScene(box, specimen);
            LocalLight3D[] lights = specimen == "emissive" ? [] : [new(new(.5f, 2.8f, 2), Vector3.One, 25, 10)];
            using var target = new VulkanNativeFrameTarget(plant, Width, Height);
            using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
            renderer.Settings = Settings() with { DirectDiffuseModel = DirectDiffuseModel3D.Lambert };
            renderer.LocalLights = lights;
            renderer.DynamicDiffuse = new() { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid,
                RaysPerProbePerFrame = 128, CaptureSecondaryWitness = true };
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            var actual = renderer.ReadLightingForQualification();
            var (oracleField, witness) = SecondaryOracleField((NativeInstanceSet3D)scene.Instances, grid,
                renderer.ReadSecondaryWitnessForQualification(), lights);
            SaveSamplingImage(plant, assets, output, specimen + "-full", actual);
            renderer.DynamicDiffuse = renderer.DynamicDiffuse with { SecondaryMaterials = false };
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            var factors = renderer.ReadLightingForQualification();
            SaveSamplingImage(plant, assets, output, specimen + "-factor-only", factors);
            renderer.SetCompiledDiffuseProbes(oracleField);
            renderer.DynamicDiffuse = renderer.DynamicDiffuse with { Mode = DynamicDiffuseMode3D.CompiledOnly, SecondaryMaterials = true };
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            var expected = renderer.ReadLightingForQualification();
            SaveSamplingImage(plant, assets, output, specimen + "-oracle", expected);
            double error = Rmse(actual.LinearRgb, expected.LinearRgb);
            double control = Rmse(factors.LinearRgb, expected.LinearRgb);
            Console.WriteLine($"Secondary {specimen}: HDR RMSE {error:F7}, factor-only {control:F7}");
            Require(error < .004 && error < control * .15, "Secondary surface evaluation did not improve real GI against the independent material oracle.");
            rows.Add(new { Specimen = specimen, FullRmse = error, FactorOnlyRmse = control, Witness = witness });
            pairs.Add(new { Name = specimen + "-full", Reference = specimen + "-oracle.png", Test = specimen + "-full.png", Group = specimen + "-full" });
            pairs.Add(new { Name = specimen + "-factor-only", Reference = specimen + "-oracle.png", Test = specimen + "-factor-only.png", Group = specimen + "-factor-only" });
            if (specimen == "emissive")
            {
                var tiled = SecondaryTiledEmission(box, scene);
                var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), tiled, [], camera, samples: 2048, bounces: 2);
                SaveSamplingImage(plant, assets, output, "emissive-path-reference", reference);
                double pathError = Rmse(actual.LinearRgb, reference.LinearRgb);
                double pathControl = Rmse(factors.LinearRgb, reference.LinearRgb);
                Console.WriteLine($"Secondary emissive two-bounce reference: {pathError:F6} vs {pathControl:F6}");
                Require(pathError < pathControl * .7, "Textured emissive bounce did not improve the independent path-traced reference.");
                rows.Add(new { Specimen = "emissive-two-bounce", FullRmse = pathError, FactorOnlyRmse = pathControl });
            }
        }
        object motion = CheckSecondaryMotion(plant, assets, box, camera, matrix, grid, output);
        object performance = MeasureSecondaryMaterials(plant, assets, SecondaryScene(box, "all-channels"), camera, grid);
        pairs.Add(new { Name = "identity", Reference = "albedo-oracle.png", Test = "albedo-oracle.png", Group = "identity" });
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        { Qualification = "secondary-materials", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, FullR4Complete = false, Device = plant.Facts.PhysicalDeviceName, Cases = rows, Motion = motion, Performance = performance,
            Oracle = "Independent CPU attribute/texture/BRDF evaluation at qualified GPU hit positions; CPU-convolved field through the real GI resolve. Emissive tiles additionally use independent two-bounce path tracing.",
            Scope = "Opaque LOD0 secondary hits, explicit texture LOD 0, shared resident geometry/material images and samplers; no alpha continuation or recursive transport.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_R4_SECONDARY_PASSED " + output);
    }

    private static Native3DScene SecondaryScene(NativeMesh3D box, string specimen)
    {
        bool smooth = specimen is "smooth" or "all-channels";
        bool all = specimen == "all-channels";
        bool analytic = specimen.StartsWith("analytic-", StringComparison.Ordinal);
        bool normal = specimen is "normal-map" or "negative-tangent" || all || analytic;
        bool affine = all || analytic;
        var vertices = new List<NativeModel3DVertex>();
        foreach (var xy in new[] { new Vector2(-1, -1), new(1, -1), new(1, 1), new(-1, 1) })
        {
            Vector3 n = smooth ? Vector3.Normalize(new(.8f * xy.X, .45f * xy.Y, 1)) : Vector3.UnitZ;
            Vector2 uv = (xy + Vector2.One) * .5f;
            if (all) uv = uv * 2.3f - new Vector2(.17f, .31f);
            Vector4 color = all ? new(.6f + .2f * xy.X, .7f + .15f * xy.Y, .85f, 1) : Vector4.One;
            vertices.Add(new(new(xy, 0), n, color, uv, new(1, 0, 0, specimen == "negative-tangent" ? -1 : 1)));
        }
        var mesh = new NativeMesh3D("secondary-" + specimen, vertices, [0, 1, 2, 0, 2, 3]);
        var sampler = all ? new ModelSampler(true, true, TextureWrap.Repeat, TextureWrap.Mirror) : new(false, false, TextureWrap.Clamp, TextureWrap.Clamp);
        ModelTextureBinding Texture(string name, Func<int, int, Vector4> texel)
        {
            var bytes = new List<byte>();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    Vector4 value = texel(x, y);
                    foreach (float channel in new[] { value.X, value.Y, value.Z, value.W }) bytes.Add((byte)MathF.Round(channel * 255));
                }
            return new(new(name, 4, 4, bytes.ToImmutableArray()), sampler);
        }
        var checker = Texture("secondary-color", (x, y) => (x + y) % 2 == 0 ? new(.9f, .08f, .15f, 1) : new(.06f, .35f, .85f, 1));
        var material = new ModelMaterial("secondary-source")
        {
            BaseColor = specimen == "emissive" ? new(.05f, .05f, .05f, 1) : new(.8f, .7f, .6f, 1),
            Metallic = all ? .65f : 0, Roughness = .75f, DoubleSided = true,
            BaseColorTexture = specimen == "albedo" || all || analytic ? checker : null,
            EmissiveTexture = specimen == "emissive" || all ? checker : null,
            Emissive = specimen switch
            {
                "emissive" => new(3.5f, 3, 2.5f),
                "all-channels" => new(.2f, .1f, .3f),
                _ => Vector3.Zero,
            },
            NormalTexture = normal ? Texture("secondary-normal", (x, y) => new((x % 2 == 0 ? .85f : .2f), .7f, .9f, 1)) : null,
            NormalScale = .8f,
            MetallicRoughnessTexture = all ? Texture("secondary-mr", (x, y) => new(.3f, .3f + y * .15f, .2f + x * .2f, 1)) : null,
            OcclusionTexture = all ? Texture("secondary-ao", (x, y) => new(.2f + x * .15f, .8f, .3f, 1)) : null,
            OcclusionStrength = .6f,
        };
        var floor = new ModelMaterial("secondary-floor") { BaseColor = new(.65f, .65f, .65f, 1), Metallic = 0, Roughness = 1, DoubleSided = true };
        if (analytic)
        {
            mesh = NativeAnalyticPrimitives3D.Create(specimen, specimen == "analytic-sphere"
                ? AnalyticPrimitiveKind3D.UnitSphere : AnalyticPrimitiveKind3D.UnitBox);
        }
        float xScale = specimen == "analytic-box-reflected" ? -1.7f : 1.7f;
        var transform = Matrix4x4.CreateScale(xScale, 1.3f, affine ? .7f : 1) * Matrix4x4.CreateRotationY(affine ? .23f : 0)
            * Matrix4x4.CreateTranslation(0, 1.3f, -1.5f);
        if (specimen == "imported-glb")
        {
            string path = "Games/Starter/Aurelian.Starter/Assets/Models/crate-channels.glb";
            var imported = GlbModelImporter.Load("secondary-import", path);
            Require(imported.Success, ModelAssetCatalog.Describe(imported.Diagnostics));
            var occurrence = imported.Model!.Occurrences.Single(item => item.Primitive.Material.Slot == "panel");
            mesh = NativeMesh3D.FromPrimitive(occurrence.Primitive);
            material = occurrence.Primitive.Material;
            transform = occurrence.Transform * Matrix4x4.CreateScale(1.5f) * Matrix4x4.CreateRotationY(.23f)
                * Matrix4x4.CreateTranslation(0, .2f, -.9f);
        }
        return new([], []) { Instances = new NativeInstanceSet3D([
            new("secondary-source", mesh, material, transform),
            new("floor", box, floor, Matrix4x4.CreateScale(2.1f, .05f, 2.1f) * Matrix4x4.CreateTranslation(0, -.05f, 0)),
        ]) };
    }

    private static Native3DScene SecondaryTiledEmission(NativeMesh3D box, Native3DScene source)
    {
        var original = source.Instances.Single(instance => instance.Identity == "secondary-source");
        var instances = source.Instances.Where(instance => instance.Identity != "secondary-source").ToList();
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Vector4 sample = SampleSecondaryTexture(original.Material.EmissiveTexture, new((x + .5f) / 4, (y + .5f) / 4), true, Vector4.One);
                var material = original.Material with { Slot = $"emission-tile-{x}-{y}", EmissiveTexture = null,
                    Emissive = original.Material.Emissive * new Vector3(sample.X, sample.Y, sample.Z) };
                float left = -1 + x * .5f;
                float bottom = -1 + y * .5f;
                var vertices = new[] { new Vector3(left, bottom, 0), new(left + .5f, bottom, 0), new(left + .5f, bottom + .5f, 0), new(left, bottom + .5f, 0) }
                    .Select(position => new NativeModel3DVertex(position, Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)));
                instances.Add(new($"tile-{x}-{y}", new($"tile-{x}-{y}", vertices, [0, 1, 2, 0, 2, 3]), material, original.Transform));
            }
        }
        return source with { Instances = new NativeInstanceSet3D(instances) };
    }
}
