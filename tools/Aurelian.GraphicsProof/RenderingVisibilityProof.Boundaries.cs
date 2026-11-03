using System.Numerics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;

internal static partial class RenderingVisibilityProof
{
    private static object CheckBoundaries(AurelianVulkanPlant plant, GameAssets assets, string output,
        Native3DScene forest, List<Measurement> measurements)
    {
        using var directTarget = new VulkanNativeFrameTarget(plant, Width, Height);
        using var culledTarget = new VulkanNativeFrameTarget(plant, Width, Height);
        using var direct = RenderingBindlessProof.CreateRenderer(plant, assets, directTarget);
        using var culled = RenderingBindlessProof.CreateRenderer(plant, assets, culledTarget, enableCulling: true);
        var settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        direct.Settings = culled.Settings = settings;
        direct.FrameMode = culled.FrameMode = Native3DFrameMode.Interactive;
        direct.EnableVisibilityBuffer = culled.EnableVisibilityBuffer = true;
        var clear = new NativeFrameClearColor(.1f, .15f, .22f, 1);
        Vector3 eye = new(0, 3, 8);
        Matrix4x4 camera = RenderingBindlessProof.Camera(eye, new(0, 1.4f, -20));
        direct.Render(forest, camera, eye, clear, capture: true);
        var originalIds = direct.ReadIdentitiesForQualification();
        culled.Render(forest, camera, eye, clear, capture: true);
        var culledIds = culled.ReadIdentitiesForQualification();
        Require(originalIds.Ids.SequenceEqual(culledIds.Ids), "Cluster compaction changed Forest instance/triangle/facing identity.");
        Console.WriteLine("Forest direct/compacted instance, triangle and facing IDs are identical.");
        var culledComparison = Compare(culled, forest, camera, eye, output, "forest-512-culled");
        Measure(culled, forest, camera, eye, "forest-512-culled", measurements);

        var grid = TriangleGrid();
        var second = new NativeMesh3D("visibility-grid-b", grid.Vertices, grid.Indices);
        var material = new ModelMaterial("oracle") { DoubleSided = true, Unlit = true };
        Native3DScene scene = new([], [])
        {
            Instances = new NativeInstanceSet3D([
                new("left", grid, material, Matrix4x4.CreateScale(.7f) * Matrix4x4.CreateTranslation(-.85f, 0, 0)),
                new("right", second, material with { Slot = "right" }, Matrix4x4.CreateScale(.7f) * Matrix4x4.CreateTranslation(.85f, 0, 0)),
                new("upper", grid, material with { Slot = "upper" }, Matrix4x4.CreateScale(.45f, .3f, .7f)
                    * Matrix4x4.CreateRotationZ(.1f) * Matrix4x4.CreateTranslation(0, 1.1f, -.1f)),
            ]),
        };
        eye = new(0, .1f, 4);
        camera = RenderingBindlessProof.Camera(eye, Vector3.Zero);
        direct.EnableVisibilityBuffer = culled.EnableVisibilityBuffer = true;
        direct.Render(scene, camera, eye, clear, capture: true);
        var gridIds = direct.ReadIdentitiesForQualification();
        culled.Render(scene, camera, eye, clear, capture: true);
        var indirectIds = culled.ReadIdentitiesForQualification();
        Require(gridIds.Ids.SequenceEqual(indirectIds.Ids), "Multiple mesh runs/nonzero first-instance or first-triangle offsets changed IDs.");
        var oracle = CheckRayOracle(scene, camera, indirectIds);
        var gridComparison = Compare(culled, scene, camera, eye, output, "identity-grid");
        var scaledClipComparison = Compare(culled, scene, camera * .00001f, eye, output, "scaled-clip");

        // A triangle crosses both the near plane and the eye plane; reconstruction must not divide by a vertex w.
        var nearMesh = new NativeMesh3D("near-plane", [
            new(new(-.8f, -.6f, 0), Vector3.UnitZ, new(1, .1f, .2f, 1), new(0, 0), new(1, 0, 0, 1)),
            new(new(.8f, -.6f, 0), Vector3.UnitZ, new(.2f, 1, .1f, 1), new(1, 0), new(1, 0, 0, 1)),
            new(new(0, .3f, 4.2f), Vector3.UnitZ, new(.1f, .2f, 1, 1), new(.5f, 1), new(1, 0, 0, 1)),
        ], [0, 1, 2]);
        Native3DScene crossing = new([], []) { Instances = new NativeInstanceSet3D([new("crossing", nearMesh, material, Matrix4x4.Identity)]) };
        var nearComparison = Compare(culled, crossing, camera, eye, output, "eye-plane-crossing");
        culled.EnableVisibilityBuffer = true;
        culled.Render(crossing, camera, eye, clear, capture: true);
        var nearOracle = CheckRayOracle(crossing, camera, culled.ReadIdentitiesForQualification(), minimumHits: 100);

        // Static revisions and two pending frame slots must preserve temporal correspondence and resource lifetime.
        culled.Settings = Graphics3DSettings.Default with { BloomIntensity = 0, AmbientOcclusionStrength = 0 };
        var temporalComparisons = new List<object>();
        foreach (var revision in new[] { scene, crossing, scene })
            temporalComparisons.Add(Compare(culled, revision, camera, eye, output, "revision-" + temporalComparisons.Count));
        culled.EnableVisibilityBuffer = true;
        culled.Render(scene, camera, eye, clear);
        culled.Render(crossing, camera, eye, clear);
        var final = culled.Render(scene, camera, eye, clear, capture: true);
        culled.ResetTemporalHistory();
        var reset = culled.Render(scene, camera, eye, clear, capture: true);
        Require(final.PixelSha256 == reset.PixelSha256, "In-flight revision retirement changed visibility output.");

        return new { ForestIds = Hash(originalIds.Ids), CulledForestIds = Hash(culledIds.Ids), CulledComparison = culledComparison,
            GridIds = Hash(gridIds.Ids), IndirectGridIds = Hash(indirectIds.Ids), Oracle = oracle,
            GridComparison = gridComparison, ScaledClipComparison = scaledClipComparison,
            NearComparison = nearComparison, NearOracle = nearOracle,
            TemporalComparisons = temporalComparisons, InFlightRevision = final.PixelSha256,
            IntegerTransport = CheckIntegerTransport(plant, assets, scene, camera, eye) };
    }

    private static object CheckIntegerTransport(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        Matrix4x4 camera, Vector3 eye)
    {
        const uint first = 16_777_217;
        const uint second = uint.MaxValue;
        var sources = BuiltinShaderLibrary.LoadSources("VisibilityRaster3D.v.ts").Select(source => source with
        {
            Source = source.Source.Replace("uint2(input.instance + 1, (input.firstTriangle + builtins.primitive) * 2 + face)",
                "uint2(16777217, 0xffffffff)", StringComparison.Ordinal),
        }).ToArray();
        var module = GpuGraphicsBinder.Compile(new(sources));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, "Integer transport shader was not validated.");
        var raster = CompiledGraphicsProgramExporter.Export(module, backend);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target,
            visibilityPrograms: new(raster, assets.Shader("VisibilityResolve3D.v.ts")));
        renderer.EnableVisibilityBuffer = true;
        renderer.Settings = Graphics3DSettings.Basic;
        renderer.Render(scene, camera, eye, new(.1f, .15f, .22f, 1), capture: true);
        var image = renderer.ReadIdentitiesForQualification();
        int hits = 0;
        for (int index = 0; index < image.Ids.Length; index += 2)
        {
            if (image.Ids[index] == 0) continue;
            Require(image.Ids[index] == first && image.Ids[index + 1] == second, "Integer IDs lost bits in native attachment transport.");
            hits++;
        }
        Require(hits > 100, "Integer transport did not rasterize a meaningful region.");
        return new { First = first, Second = second, Hits = hits, Sha256 = Hash(image.Ids) };
    }

    private static NativeMesh3D TriangleGrid()
    {
        var vertices = new List<NativeModel3DVertex>();
        var indices = new List<uint>();
        for (int triangle = 0; triangle < 258; triangle++)
        {
            float x = (triangle % 16 - 7.5f) * .12f;
            float y = (triangle / 16 - 7.5f) * .12f;
            uint first = (uint)vertices.Count;
            vertices.Add(new(new(x - .05f, y - .04f, 0), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)));
            vertices.Add(new(new(x + .05f, y - .04f, 0), Vector3.UnitZ, Vector4.One, Vector2.UnitX, new(1, 0, 0, 1)));
            vertices.Add(new(new(x, y + .05f, 0), Vector3.UnitZ, Vector4.One, Vector2.UnitY, new(1, 0, 0, 1)));
            indices.AddRange(triangle % 2 == 0 ? [first, first + 1, first + 2] : [first, first + 2, first + 1]);
        }
        return new("visibility-identity-grid", vertices, indices);
    }

    private static string Hash(uint[] words) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(words.AsSpan())));
}
