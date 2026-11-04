using System.Numerics;
using System.Collections.Immutable;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHybridProof
{
    private static object CheckReflectedNormalMaps(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        const int width = 320;
        const int height = 240;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.Settings = Graphics3DSettings.Default with { Shadows = false, AntiAliasing = AntiAliasing3D.None,
            BloomIntensity = 0, AmbientOcclusionStrength = 0 };
        var texture = new ModelTexture("affine-strong-normal", 1, 1, ImmutableArray.Create<byte>(170, 220, 210, 255));
        var material = new ModelMaterial("affine-mapped") { BaseColor = new(.55f, .4f, .2f, 1), DoubleSided = true,
            Metallic = 0, Roughness = .7f, NormalTexture = new(texture, new()), NormalScale = 1 };
        Matrix4x4 shear = Matrix4x4.Identity;
        shear.M21 = .3f;
        Matrix4x4 reflection = Matrix4x4.CreateScale(-1, 1, 1);
        var exact = new List<SceneInstance3D>();
        var control = new List<SceneInstance3D>();
        var wrong = new List<SceneInstance3D>();
        foreach (var kind in new[] { AnalyticPrimitiveKind3D.UnitSphere, AnalyticPrimitiveKind3D.UnitBox, AnalyticPrimitiveKind3D.Capsule })
        {
            Matrix4x4 transform = Matrix4x4.CreateScale(-1, .8f, .7f) * shear * Matrix4x4.CreateTranslation(((int)kind - 2) * 2.4f, 0, 0);
            exact.Add(new("exact-" + kind, NativeAnalyticPrimitives3D.Create("exact-" + kind, kind), material, transform));
            NativeMesh3D source = Mesh(kind, 96);
            NativeModel3DVertex[] vertices = source.Vertices.Select(vertex => vertex with
            {
                Position = Vector3.Transform(vertex.Position, reflection),
                Normal = Vector3.TransformNormal(vertex.Normal, reflection),
                Tangent = new(-vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z, -vertex.Tangent.W),
            }).ToArray();
            uint[] indices = source.Indices.ToArray();
            for (int index = 0; index < indices.Length; index += 3)
                (indices[index + 1], indices[index + 2]) = (indices[index + 2], indices[index + 1]);
            control.Add(new("mesh-" + kind, new("mesh-" + kind, vertices, indices), material, reflection * transform));
            NativeModel3DVertex[] wrongVertices = vertices.Select(vertex => vertex with
            {
                Tangent = vertex.Tangent with { W = -vertex.Tangent.W },
            }).ToArray();
            wrong.Add(new("wrong-" + kind, new("wrong-" + kind, wrongVertices, indices), material, reflection * transform));
        }
        Vector3 eye = new(0, 2, 8);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)width / height, .1f, 100);
        projection.M22 *= -1;
        Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * projection;
        byte[] Capture(List<SceneInstance3D> instances, string name)
        {
            Native3DScene scene = new([], []) { Instances = new NativeInstanceSet3D(instances) };
            byte[] pixels = renderer.Render(scene, camera, eye, Clear, capture: true).Pixels!;
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), width, height, pixels);
            return pixels;
        }
        byte[] actual = Capture(exact, "reflected-mapped-analytic");
        byte[] correct = Capture(control, "reflected-mapped-mesh");
        byte[] incorrect = Capture(wrong, "reflected-mapped-wrong-handedness");
        double Error(byte[] pixels)
        {
            long difference = 0;
            for (int pixel = 0; pixel < width * height; pixel++)
            {
                for (int channel = 0; channel < 3; channel++)
                    difference += Math.Abs(actual[pixel * 4 + channel] - pixels[pixel * 4 + channel]);
            }
            return difference / (width * height * 3d);
        }
        double correctError = Error(correct);
        double wrongError = Error(incorrect);
        Require(correctError < 1.2 && wrongError > correctError * 2 + .02,
            $"Reflected normal-map control did not distinguish handedness: correct={correctError}, wrong={wrongError}.");
        return new { CorrectMeanRgbError = correctError, WrongHandednessMeanRgbError = wrongError, MeshSlices = 96 };
    }

    private static object CheckAffineRaster(AurelianVulkanPlant plant, GameAssets assets, VulkanRayQueryScene queries,
        Native3DScene scene, OracleObject[] objects, string output)
    {
        const int width = 320;
        const int height = 240;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.EnableHierarchicalZ = true;
        renderer.Settings = Graphics3DSettings.Default with { Shadows = false, AntiAliasing = AntiAliasing3D.None,
            BloomIntensity = 0, AmbientOcclusionStrength = 0 };
        Vector3 first = Vector3.Transform(Vector3.Zero, objects[0].Instance.Transform);
        var poses = new[] { (Eye: new Vector3(0, 4, 18), Target: Vector3.Zero),
            (Eye: new Vector3(1, -2, -17), Target: Vector3.Zero),
            (Eye: first, Target: first - Vector3.UnitZ),
            (Eye: first + Vector3.UnitZ * 1.05f, Target: first) };
        var results = new List<object>();
        int caseIndex = 0;
        foreach (var pose in poses)
        {
            foreach (bool orthographic in new[] { false, true })
            {
                Matrix4x4 projection = orthographic ? Matrix4x4.CreateOrthographic(21, 21f * height / width, .1f, 100)
                    : Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)width / height, .1f, 100);
                projection.M22 *= -1;
                Matrix4x4 camera = Matrix4x4.CreateLookAt(pose.Eye, pose.Target, Vector3.UnitY) * projection;
                double[,] inverse = Inverse(camera);
                renderer.ResetTemporalHistory();
                var frame = renderer.Render(scene, camera, pose.Eye, Clear, capture: true);
                NativeGameGraphics.WritePng(Path.Combine(output, $"affine-raster-{caseIndex}.png"), width, height, frame.Pixels!);
                uint[] identities = renderer.ReadIdentitiesForQualification().Ids;
                float[] depth = renderer.ReadHzbForQualification().History;
                var requests = new List<RayQueryRequest>();
                var pixels = new List<int>();
                for (int y = 1; y < height - 1; y += 2)
                {
                    for (int x = 1; x < width - 1; x += 2)
                    {
                        double nx = (x + .5) * 2 / width - 1;
                        double ny = (y + .5) * 2 / height - 1;
                        D3 start = Project(new(nx, ny, 0), inverse);
                        D3 end = Project(new(nx, ny, 1), inverse);
                        Vector3 origin = new((float)start.X, (float)start.Y, (float)start.Z);
                        Vector3 delta = new((float)(end.X - start.X), (float)(end.Y - start.Y), (float)(end.Z - start.Z));
                        requests.Add(new(origin, Vector3.Normalize(delta), delta.Length()));
                        pixels.Add(y * width + x);
                    }
                }
                RayQueryHit[] hits = queries.Trace(requests);
                int interior = 0;
                int background = 0;
                int boundaries = 0;
                double maximumDepthError = 0;
                for (int index = 0; index < hits.Length; index++)
                {
                    int pixel = pixels[index];
                    uint identity = identities[pixel * 2];
                    if (new[] { pixel - 1, pixel + 1, pixel - width, pixel + width }
                        .Any(neighbor => identities[neighbor * 2] != identity))
                    {
                        boundaries++;
                        continue;
                    }
                    RayQueryHit hit = hits[index];
                    if (identity == 0)
                    {
                        Require(hit.Kind == 0, $"Raster/query background mismatch in pose {caseIndex}, pixel {pixel}.");
                        background++;
                        continue;
                    }
                    Require(hit.Kind == 2 && hit.Primitive + 1 == identity,
                        $"Raster/query identity mismatch pose {caseIndex}, pixel {pixel}: raster {identity}, query {hit}.");
                    Vector4 clip = Vector4.Transform(new Vector4(hit.Point, 1), camera);
                    double error = Math.Abs(depth[pixel] - clip.Z / clip.W);
                    Require(error < 3e-5, $"Raster/query depth mismatch pose {caseIndex}, pixel {pixel}: {error}.");
                    uint expectedFace = (identities[pixel * 2 + 1] & 1);
                    Require(float.IsFinite(hit.Normal.LengthSquared()) && Math.Abs(hit.Normal.Length() - 1) < .0001f,
                        "Affine query normal is not unit length.");
                    Require(hit.FrontFace == (expectedFace == 0), "Raster/query front-face identity differs.");
                    maximumDepthError = Math.Max(maximumDepthError, error);
                    interior++;
                }
                Require(interior > 100, "Raster/query comparison has too little foreground.");
                results.Add(new { Case = caseIndex++, Orthographic = orthographic, Eye = pose.Eye,
                    Interior = interior, Background = background, Boundaries = boundaries, MaximumDepthError = maximumDepthError });
                Console.WriteLine($"Affine raster case {caseIndex}: {interior} interior hits, max D32 difference {maximumDepthError:G5}");
            }
        }
        return results;
    }

    private static object CheckAffineFiltering(AurelianVulkanPlant plant, GameAssets assets, byte[] program, SceneInstance3D source)
    {
        RayQueryAnalytic3D[] primitives = [new(source.Mesh.PrimitiveKind, source.Transform, 1, 1),
            new(source.Mesh.PrimitiveKind, source.Transform, 2, 2)];
        Vector3 center = Vector3.Transform(Vector3.Zero, source.Transform);
        RayQueryRequest[] rays = [new(center + Vector3.UnitZ * 3, -Vector3.UnitZ, 20, 1, 1),
            new(center + Vector3.UnitZ * 3, -Vector3.UnitZ, 20, 2, 2),
            new(center + Vector3.UnitZ * 3, -Vector3.UnitZ, 20, 1, 2),
            new(center + Vector3.UnitZ * 3, -Vector3.UnitZ, 20, 0, 3),
            new(center + Vector3.UnitZ * 3, -Vector3.UnitZ, 20, 3, 3)];
        using var queries = VulkanRayQueryScene.CreateAnalytic(plant, program, [], primitives, 64);
        RayQueryHit[] hits = queries.Trace(rays);
        using var legacy = new VulkanRayQueryScene(plant, assets.ComputeShader("RayQuery.v.ts"), [],
            [new(center, 1, 1, 1), new(center, 1, 2, 2)], 64);
        Require(legacy.Trace(rays).SequenceEqual(hits), "Legacy sphere packets changed relative to the shared analytic control.");
        Require(hits[0].Kind == 2 && hits[0].Primitive == 0 && hits[1].Kind == 2 && hits[1].Primitive == 1,
            "Affine layer or query mask lost authority.");
        Require(hits[2].Kind == 0 && hits[3].Kind == 0 && hits[4].Kind == 2 && hits[4].Primitive == 0,
            "Affine exclusion or stable duplicate ordering failed.");
        RayQueryTriangle triangle = new(center + new Vector3(-2, -2, 1), center + new Vector3(2, -2, 1), center + new Vector3(0, 2, 1));
        using var mixed = VulkanRayQueryScene.CreateAnalytic(plant, program, [triangle], primitives, 64);
        RayQueryHit tie = mixed.Trace([rays[4]])[0];
        Require(tie.Kind == 1 && tie.Primitive == 0, "Triangle/analytic equal-distance ordering changed.");
        RayQueryHit exit = mixed.Trace([rays[4] with { MinimumDistance = 2.01f }])[0];
        Require(exit.Kind == 2 && Math.Abs(exit.Distance - 4) < .00001f, "Clipped entry did not select the exit root.");
        return new { LayersAndMasks = true, StableDuplicate = true, TriangleTieWins = true,
            LegacySpherePacketsByteEquivalent = true, ClippedEntryExitDistance = exit.Distance };
    }
}
