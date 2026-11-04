using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHybridProof
{
    internal static void RunAffineQueries(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Shared affine analytic intersections"));
        Require(initialized.Success, "Ray-query plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        byte[] program = assets.ComputeShader("AffineRayQuery3D.v.ts");
        Native3DScene scene = AffineScene();
        SceneInstance3D[] ordered = scene.Instances.GroupBy(item => (item.Mesh, item.Material)).SelectMany(group => group).ToArray();
        var analytics = ordered.Select(item => new RayQueryAnalytic3D(item.Mesh.PrimitiveKind, item.Transform)).ToArray();
        using var queries = VulkanRayQueryScene.CreateAnalytic(plant, program, [], analytics, 65_536);
        var oracle = ordered.Select((item, index) => new OracleObject(item, (uint)index, Inverse(item.Transform))).ToArray();
        object rays = CheckAffineRaySuite(queries, oracle);
        object raster = CheckAffineRaster(plant, assets, queries, scene, oracle, output);
        object mapped = CheckReflectedNormalMaps(plant, assets, output);
        object filters = CheckAffineFiltering(plant, assets, program, ordered[0]);
        object capture = CheckAffineReferenceCapture(plant, assets, output);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Rays = rays, Raster = raster,
            Filters = filters, Capture = capture, ReflectedNormalMaps = mapped,
            Primitives = analytics.Select(item => new { item.Kind, item.Transform }),
            Scope = "Shared exact canonical surfaces under affine transforms; no analytic light sampling or material textures.",
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine("AURELIAN_AFFINE_ANALYTIC_PASSED " + output);
    }

    private static Native3DScene AffineScene()
    {
        Matrix4x4 shear = Matrix4x4.Identity;
        shear.M21 = .65f;
        shear.M31 = -.25f;
        shear.M32 = .3f;
        Matrix4x4[] transforms = [Matrix4x4.Identity, Matrix4x4.CreateScale(.35f, 1.2f, .6f),
            Matrix4x4.CreateRotationY(.7f) * shear, Matrix4x4.CreateScale(-.8f, .7f, .5f) * shear,
            Matrix4x4.CreateScale(.12f, 1.5f, .3f) * Matrix4x4.CreateRotationZ(.35f),
            Matrix4x4.CreateScale(1.1f, .2f, .8f) * shear];
        var instances = new List<SceneInstance3D>();
        foreach (var kind in new[] { AnalyticPrimitiveKind3D.UnitSphere, AnalyticPrimitiveKind3D.UnitBox, AnalyticPrimitiveKind3D.Capsule })
        {
            var mesh = NativeAnalyticPrimitives3D.Create("affine-" + kind, kind);
            var material = new ModelMaterial("affine-" + kind) { BaseColor = new(.65f, .45f, .2f, 1), Metallic = 0, DoubleSided = true };
            for (int index = 0; index < transforms.Length; index++)
            {
                Matrix4x4 transform = transforms[index] * Matrix4x4.CreateTranslation((index - 2.5f) * 3.1f, (2 - (int)kind) * 3.4f, 0);
                instances.Add(new($"{kind}-{index}", mesh, material, transform));
            }
        }
        return new([], []) { Instances = new NativeInstanceSet3D(instances) };
    }

    private static object CheckAffineRaySuite(VulkanRayQueryScene queries, OracleObject[] objects)
    {
        var requests = new List<RayQueryRequest>();
        var random = new Random(491);
        foreach (var item in objects)
        {
            for (int sample = 0; sample < 384; sample++)
            {
                Vector3 origin = sample % 3 == 0 ? Vector3.Zero : new Vector3(Next(), Next(), -3);
                Vector3 target = new(Next() * .6f, Next() * .6f, Next() * .6f);
                Vector3 worldOrigin = Vector3.Transform(origin, item.Instance.Transform);
                Vector3 worldTarget = Vector3.Transform(target, item.Instance.Transform);
                requests.Add(new(worldOrigin, Vector3.Normalize(worldTarget - worldOrigin), 100, MinimumDistance: .0001f));
            }
            // Parallel slabs, origins on a surface, clipped entry roots and interior exits.
            foreach (Vector3 axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            {
                foreach (float start in new[] { -3f, -1f, 0f, .5f, 1f, 3f })
                {
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        Vector3 origin = Vector3.Transform(axis * start, item.Instance.Transform);
                        Vector3 direction = Vector3.Normalize(Vector3.TransformNormal(axis * sign, item.Instance.Transform));
                        requests.Add(new(origin, direction, 100, MinimumDistance: .001f));
                    }
                }
            }
        }
        RayQueryHit[] hits = queries.Trace(requests);
        double maximumDistanceError = 0;
        double maximumNormalError = 0;
        int checkedHits = 0;
        int background = 0;
        int boundaries = 0;
        for (int index = 0; index < requests.Count; index++)
        {
            RayQueryRequest ray = requests[index];
            OracleHit[] candidates = objects.SelectMany(item => Intersections(item, D3.From(ray.Origin),
                D3.From(ray.Direction) * ray.MaximumDistance, ray.MinimumDistance / ray.MaximumDistance)).OrderBy(hit => hit.T).ToArray();
            RayQueryHit actual = hits[index];
            if (candidates.Length == 0)
            {
                Require(actual.Kind == 0, $"Affine oracle ray {index}: expected miss, got {actual}.");
                background++;
                continue;
            }
            OracleHit expected = candidates[0];
            if (expected.Margin < .01 || candidates.Length > 1 && Math.Abs(expected.T - candidates[1].T) < 1e-6)
            {
                boundaries++;
                continue;
            }
            Require(actual.Kind == 2 && actual.Primitive == expected.Instance, $"Affine oracle ray {index}: expected {expected.Instance}, got {actual}.");
            double distanceError = Math.Abs(actual.Distance - expected.T * ray.MaximumDistance);
            D3 normal = expected.Normal;
            if (normal.Dot(D3.From(ray.Direction)) > 0) normal *= -1;
            double normalError = Math.Sqrt((D3.From(actual.Normal) - normal).Dot(D3.From(actual.Normal) - normal));
            maximumDistanceError = Math.Max(maximumDistanceError, distanceError);
            maximumNormalError = Math.Max(maximumNormalError, normalError);
            Require(distanceError < .0003 && normalError < .0003,
                $"Affine oracle ray {index}: distance error {distanceError}, normal error {normalError}.");
            checkedHits++;
        }
        Require(checkedHits > 5000 && background > 100, "Insufficient affine ray coverage.");
        Console.WriteLine($"Affine oracle: {checkedHits} hits, {background} misses; distance {maximumDistanceError:G5}, normal {maximumNormalError:G5}");
        return new { Requests = requests.Count, Hits = checkedHits, Background = background, Boundaries = boundaries,
            MaximumDistanceError = maximumDistanceError, MaximumNormalError = maximumNormalError };

        float Next() => (float)(random.NextDouble() * 2 - 1);
    }
}
