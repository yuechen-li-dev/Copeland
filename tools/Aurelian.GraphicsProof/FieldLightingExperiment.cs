using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Aetheris.Continuum.Backends.Sdf;
using Aetheris.Kernel.Core.Brep;
using Aetheris.Kernel.Core.Brep.Queries;
using Aetheris.Kernel.Core.Brep.Tessellation;
using Aetheris.Kernel.Core.Math;
using Aurelian.Games;
using Aurelian.GameHost.Silk;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Qualification;
using Aurelian.Lighting.Aetheris;
using Aurelian.Runtime;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using SkiaSharp;

internal static class FieldLightingExperiment
{
    private const int ProbeSize = 256;
    private const int GallerySize = 640;
    private sealed record Shape(BrepBody Body, SdfNode Field, Vector3 Position, Vector4 Color);
    private sealed record Fixture(Shape[] Shapes, SdfNode Field, Native3DScene Scene);
    private sealed record Capture(string Name, string Hash, byte[] Pixels, IReadOnlyList<Native3DGpuPassTime> Timings);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "field-evidence.json");
        File.WriteAllText(evidencePath, "{\"accepted\":false}");
        Fixture original = BuildFixture(0);
        Fixture moved = BuildFixture(1.2f);
        AetherisLightField field = AetherisLightField.Create(original.Field);
        AetherisLightField movingField = AetherisLightField.Create(moved.Field);
        Require(field.Program.StructuralHash != movingField.Program.StructuralHash, "Moving geometry did not invalidate source identity.");
        File.WriteAllText(Path.Combine(output, "AetherisField.v.ts"), field.WorldSource);
        File.WriteAllText(Path.Combine(output, "moved-AetherisField.v.ts"), movingField.WorldSource);
        foreach (string name in new[] { "FieldTrace3D.v.ts", "FieldSolid3D.v.ts", "FieldVisibilityPolicy.v.ts" })
        {
            File.WriteAllText(Path.Combine(output, name), AetherisLightField.AssetSource(name));
        }
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian Aetheris 3D Field Lighting"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var assets = new GameAssets();
        var samples = new Dictionary<string, object>
        {
            ["fixture"] = VerifyFieldSamples(plant, field, output, "fixture"),
            ["cylinder"] = VerifyFieldSamples(plant, AetherisLightField.Create(new SdfCylinderNode(1200, 3000)), output, "cylinder"),
            ["cone"] = VerifyFieldSamples(plant, AetherisLightField.Create(new SdfConeNode(1500, 200, 3000)), output, "cone"),
            ["torus"] = VerifyFieldSamples(plant, AetherisLightField.Create(new SdfTorusNode(2000, 350)), output, "torus"),
            ["rotated-box"] = VerifyFieldSamples(plant, AetherisLightField.Create(new SdfTransformNode(
                new SdfBoxNode(2000, 1000, 3000), Transform3D.CreateRotationZ(.37))), output, "rotated-box"),
        };
        var hard = Compile(field, "FieldSolid3D.v.ts", "hard");
        var soft = Compile(field, "FieldSolid3D.v.ts", "soft");
        var movedProgram = Compile(movingField, "FieldSolid3D.v.ts", "moved");
        var probe = Compile(field, "FieldProbe3D.v.ts", "probe");
        var movedProbe = Compile(movingField, "FieldProbe3D.v.ts", "moved-probe");
        Vector3 sun = Vector3.Normalize(new Vector3(.7f, 1, .35f));
        Vector3 opposite = Vector3.Normalize(new Vector3(-.5f, 1, -.65f));
        Graphics3DSettings settings = Graphics3DSettings.Default with
        {
            SunDirection = sun,
            SunColor = Vector3.One,
            SunIntensity = 2,
            SkyAmbient = new(.1f),
            GroundAmbient = new(.05f),
            ShadowRadius = 12,
            ToneMapping = true,
        };
        Vector3 eye = new(9, 9, 11);
        Matrix4x4 camera = Camera3D.Matrix(eye, Vector3.Normalize(new Vector3(0, 1.2f, 0) - eye), 1);
        Capture depth = Render("depth", original.Scene, assets.Shader("Solid3D.v.ts"), settings, camera, eye, GallerySize);
        Capture analytic = Render("field-hard", original.Scene, hard, settings, camera, eye, GallerySize);
        Capture penumbra = Render("field-soft-experimental", original.Scene, soft, settings, camera, eye, GallerySize);
        Capture repeat = Render("field-repeat", original.Scene, hard, settings, camera, eye, GallerySize);
        Capture unshadowed = Render("unshadowed", original.Scene, hard, settings with { Shadows = false }, camera, eye, GallerySize);
        Capture shifted = Render("moved-field", moved.Scene, movedProgram, settings, camera, eye, GallerySize);
        Capture newSun = Render("opposite-sun", original.Scene, hard, settings with { SunDirection = opposite }, camera, eye, GallerySize);
        Require(analytic.Hash == repeat.Hash, "Repeated field lighting changed pixels.");
        Require(Changed(analytic, unshadowed) > 1500, "3D field shadows did not materially affect the scene.");
        Require(Changed(analytic, penumbra) > 500, "Experimental soft visibility had no visible effect.");
        Require(Changed(analytic, shifted) > 1500 && Changed(analytic, newSun) > 1500, "Geometry/sun changes did not update lighting.");
        var comparisons = new List<object>();
        comparisons.Add(VerifyRays("rays", original, probe, sun));
        comparisons.Add(VerifyRays("opposite-rays", original, probe, opposite));
        comparisons.Add(VerifyRays("moved-rays", moved, movedProbe, sun));
        comparisons.Add(VerifyRays("elevated-receiver-rays", original, probe, sun, .4f));
        // Deliberately starve the trace. This must expose unresolved rays, never claim misses.
        var starved = Compile(field, "FieldProbe3D.v.ts", "starved");
        Capture negative = Render("budget-exhaustion", Receiver(0), starved,
            settings with { ToneMapping = false }, TopCamera(0), new(0, 12, 0), ProbeSize);
        int unknown = CountUnknown(negative.Pixels);
        Require(unknown > ProbeSize * ProbeSize / 2, "Budget exhaustion was silently classified as a miss.");
        var fallbackProgram = Compile(field, "FieldSolid3D.v.ts", "starved");
        Capture fallback = Render("budget-raster-fallback", original.Scene, fallbackProgram, settings, camera, eye, GallerySize);
        int fallbackDifferences = Changed(depth, fallback);
        Require(fallbackDifferences < 30, "Starved field failed to use the existing raster fallback: " + fallbackDifferences);
        var floorField = AetherisLightField.Create(new SdfTransformNode(original.Shapes[0].Field,
            Transform3D.CreateTranslation(new(0, 0, -100))));
        var floorProgram = Compile(floorField, "FieldSolid3D.v.ts", "receiver-only");
        Capture floorShadow = Render("receiver-only", Receiver(0), floorProgram, settings, camera, eye, GallerySize);
        Capture floorUnshadowed = Render("receiver-only-unshadowed", Receiver(0), floorProgram,
            settings with { Shadows = false }, camera, eye, GallerySize);
        int receiverAcne = Changed(floorShadow, floorUnshadowed);
        Require(receiverAcne < 16, "Field floor self-shadowed: " + receiverAcne);
        ContactSheet(output, [depth, analytic, penumbra, shifted, newSun, unshadowed]);
        CopyLicences(output);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Milestone = "Analytic opaque direct-light visibility; no indirect lighting or reflections",
            FieldSourceHash = field.Program.StructuralHash,
            WorldSourceHash = field.WorldSourceSha256,
            MovedSourceHash = movingField.Program.StructuralHash,
            FieldSourceLicense = field.Program.SourceLicense,
            AssemblyHashes = new[] { typeof(AetherisLightField), typeof(SdfNode), typeof(BrepBody), typeof(GpuGraphicsBinder) }
                .Select(type => new { Name = type.Assembly.GetName().Name,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(type.Assembly.Location))).ToLowerInvariant() }),
            ShaderCompiler = "Visual TypeScript -> canonical VD-MIR -> DXC SPIR-V, spirv-val",
            GeometryAuthority = "Aetheris primitives/CSG; display tessellation and independent analytic BRep ray queries",
            Units = "Source mm Z-up; game metres Y-up, (x,y,z) -> (1000x,-1000z,1000y)",
            Samples = samples,
            RayComparisons = comparisons,
            RepeatIdentical = true,
            BudgetExhaustionUnresolved = unknown,
            RasterFallbackDifferentPixels = fallbackDifferences,
            ReceiverSelfShadowPixels = receiverAcne,
            Captures = new[] { depth, analytic, penumbra, shifted, newSun, unshadowed }
                .Select(item => new { item.Name, item.Hash, item.Timings }),
            Limits = new[]
            {
                "Static source specialization: changing geometry recompiles its field pipeline; moving sun needs no recompilation",
                "Only admitted rigid analytic primitive/Boolean fields; mesh, deforming and non-rigid fields need other backends",
                "0.5mm hit tolerance and 0.05mm numeric margin tested here, not globally certified for f32 grazing rays",
                "256 steps; unresolved rays use the existing raster shadow fallback; proof counts unresolved separately",
                "Soft distance-ratio visibility is a penumbra heuristic, not a sampled area light or Lumen GI",
                "Existing depth-map allocation and pass are retained for fallback; timings are warmed medians, not VRAM savings",
                "No field clipmaps, surface radiance cache, indirect transport, temporal accumulation or skinned-field update path",
            },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_AETHERIS_FIELD_LIGHTING_PASSED " + plant.Facts.PhysicalDeviceName);

        CompiledGraphicsProgram Compile(AetherisLightField source, string root, string mode)
        {
            IReadOnlyList<GpuSourceFile> sources = GpuSourceLoader.Load(root, name =>
            {
                if (name == "AetherisField.v.ts") return source.WorldSource;
                if (name == "FieldVisibilityPolicy.v.ts" && mode == "soft") return File.ReadAllText(Asset("FieldSoftPolicy.v.ts"));
                if (name is "FieldProbeBudget.v.ts" or "FieldTraceBudget.v.ts" && mode == "starved")
                {
                    return File.ReadAllText(Asset("FieldStarvedBudget.v.ts"));
                }
                return AetherisLightField.AssetSource(name) ?? ReadRuntimeSource(name);
            });
            var module = GpuGraphicsBinder.Compile(new(sources));
            Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
            var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
            Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated,
                backend.Vertex.DxcOutput + backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
            File.WriteAllText(Path.Combine(output, mode + ".hlsl"), backend.Hlsl);
            return CompiledGraphicsProgramExporter.Export(module, backend);
        }

        Capture Render(string name, Native3DScene scene, CompiledGraphicsProgram program, Graphics3DSettings options,
            Matrix4x4 clip, Vector3 cameraEye, int size)
        {
            using var target = new VulkanNativeFrameTarget(plant, (uint)size, (uint)size);
            using var renderer = new VulkanSolid3DRenderer(plant, program, target,
                shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"));
            renderer.Settings = options;
            Native3DFrameResult frame = renderer.Render(scene, clip, cameraEye, new(.07f, .09f, .12f, 1), capture: true);
            var timings = new List<Native3DGpuPassTime>();
            for (int index = 0; index < 8; index++)
            {
                frame = renderer.Render(scene, clip, cameraEye, new(.07f, .09f, .12f, 1), capture: true);
                if (index >= 3) timings.AddRange(frame.GpuPassTimes);
            }
            var medians = timings.GroupBy(item => item.Pass).Select(group =>
                new Native3DGpuPassTime(group.Key, group.Select(item => item.Milliseconds).Order().ElementAt(2))).ToArray();
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), size, size, frame.Pixels!);
            return new(name, frame.PixelSha256!, frame.Pixels!, medians);
        }

        object VerifyRays(string name, Fixture fixture, CompiledGraphicsProgram program, Vector3 light, float elevation = 0)
        {
            Capture actual = Render(name, Receiver(elevation), program, settings with { SunDirection = light, ToneMapping = false },
                TopCamera(elevation), new(0, 12 + elevation, 0), ProbeSize);
            int unresolved = CountUnknown(actual.Pixels);
            int mismatches = 0;
            int expectedHits = 0;
            byte[] reference = new byte[actual.Pixels.Length];
            for (int row = 0; row < ProbeSize; row++)
            {
                for (int column = 0; column < ProbeSize; column++)
                {
                    Vector3 origin = new(6 - (column + .5f) * 12 / ProbeSize, elevation + .004f,
                        6 - (row + .5f) * 12 / ProbeSize);
                    bool hit = fixture.Shapes.Any(shape => ExactHit(shape, origin, light));
                    int offset = (row * ProbeSize + column) * 4;
                    byte value = hit ? (byte)0 : (byte)255;
                    reference[offset] = reference[offset + 1] = reference[offset + 2] = value;
                    reference[offset + 3] = 255;
                    if (hit) expectedHits++;
                    if ((actual.Pixels[offset] < 128) != hit) mismatches++;
                }
            }
            NativeGameGraphics.WritePng(Path.Combine(output, name + "-brep-reference.png"), ProbeSize, ProbeSize, reference);
            Require(unresolved == 0, name + " has unresolved rays: " + unresolved);
            Require(expectedHits > 1000 && mismatches < 32, name + " disagrees with BRep: " + mismatches);
            return new { Name = name, RayCount = ProbeSize * ProbeSize, ExpectedHits = expectedHits, Mismatches = mismatches, Unresolved = unresolved };
        }
    }

    private static object VerifyFieldSamples(AurelianVulkanPlant plant, AetherisLightField field, string output, string name)
    {
        var sources = GpuSourceLoader.Load("FieldSamples.v.ts", name => name == "AetherisField.v.ts"
            ? field.Program.FieldSource : ReadRuntimeSource(name));
        var module = GpuComputeBinder.Compile(new(sources));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Require(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
        File.WriteAllText(Path.Combine(output, name + "-field-samples.hlsl"), backend.Hlsl);
        float[] input = new float[3000];
        double[] expected = new double[1000];
        for (int index = 0; index < 1000; index++)
        {
            var point = new Vector3(-5 + index % 10 * 1.1f, -.2f + index / 10 % 10 * .55f, -5 + index / 100 * 1.1f);
            input[index * 3] = point.X;
            input[index * 3 + 1] = point.Y;
            input[index * 3 + 2] = point.Z;
            expected[index] = field.EvaluateMetres(point);
        }
        using var probe = new VulkanScalarComputeProbe(plant, backend.Spirv, module.EntryPoint!.EmittedName, input);
        float[] actual = probe.Execute(125);
        double maximumError = Enumerable.Range(0, 1000).Max(index => Math.Abs(actual[index] - expected[index]));
        Require(maximumError < .00005, "GPU field exceeded the numeric margin: " + maximumError);
        Require(actual.SequenceEqual(probe.Execute(125)), "Repeated field samples changed.");
        return new { Count = 1000, MaximumErrorMetres = maximumError, AllowedErrorMetres = .00005, RepeatIdentical = true };
    }

    private static Fixture BuildFixture(float shift)
    {
        var shapes = new List<Shape>();
        Shape Box(Vector3 size, Vector3 position, Vector4 color)
        {
            var field = new SdfBoxNode(size.X * 1000, size.Z * 1000, size.Y * 1000);
            return new(BrepPrimitives.CreateBox(size.X * 1000, size.Z * 1000, size.Y * 1000).Value, field, position, color);
        }
        Vector4 stone = new(.55f, .48f, .35f, 1);
        shapes.Add(Box(new(12, .2f, 12), new(0, -.1f, 0), new(.32f, .38f, .44f, 1)));
        // A subtraction aperture represented independently by four exact BRep slabs.
        Vector3 frame = new(-2.2f, 2.1f, -1);
        shapes.Add(Box(new(.3f, 3, .35f), frame + new Vector3(-1.35f, 0, 0), stone));
        shapes.Add(Box(new(.3f, 3, .35f), frame + new Vector3(1.35f, 0, 0), stone));
        shapes.Add(Box(new(2.4f, .3f, .35f), frame + new Vector3(0, -1.35f, 0), stone));
        shapes.Add(Box(new(2.4f, .3f, .35f), frame + new Vector3(0, 1.35f, 0), stone));
        shapes.Add(new(BrepPrimitives.CreateSphere(750).Value, new SdfSphereNode(750), new(1.3f + shift, 1.6f, .4f), new(.68f, .15f, .065f, 1)));
        for (int index = 0; index < 3; index++)
        {
            shapes.Add(Box(new(.045f * (index + 1), 2.5f, .045f), new(2.7f + index * .45f, 1.25f, -2.2f), new(.08f, .4f, .5f, 1)));
        }
        SdfNode Place(SdfNode source, Vector3 position) => new SdfTransformNode(source,
            Transform3D.CreateTranslation(new(position.X * 1000, -position.Z * 1000, position.Y * 1000)));
        SdfNode root = Place(shapes[0].Field, shapes[0].Position);
        var aperture = new SdfSubtractNode(new SdfBoxNode(3000, 350, 3000), new SdfBoxNode(2400, 500, 2400));
        root = new SdfUnionNode(root, Place(aperture, frame));
        foreach (Shape shape in shapes.Skip(5)) root = new SdfUnionNode(root, Place(shape.Field, shape.Position));
        var vertices = new List<Native3DVertex>();
        foreach (Shape shape in shapes)
        {
            var mesh = BrepDisplayTessellator.Tessellate(shape.Body,
                DisplayTessellationOptions.Default with { ChordTolerance = 1, AngularToleranceRadians = .08 });
            Require(mesh.IsSuccess, "Aetheris display tessellation failed.");
            foreach (DisplayFaceMeshPatch patch in mesh.Value.FacePatches)
            {
                foreach (int index in patch.TriangleIndices)
                {
                    Point3D point = patch.Positions[index];
                    Vector3D normal = patch.Normals[index];
                    vertices.Add(new(new Vector3((float)point.X, (float)point.Z, -(float)point.Y) * .001f + shape.Position,
                        new((float)normal.X, (float)normal.Z, -(float)normal.Y), shape.Color));
                }
            }
        }
        return new(shapes.ToArray(), root, new(vertices.ToArray(), []));
    }

    private static bool ExactHit(Shape shape, Vector3 origin, Vector3 direction)
    {
        Vector3 relative = (origin - shape.Position) * 1000;
        var ray = new Ray3D(new(relative.X, -relative.Z, relative.Y), Direction3D.Create(new(direction.X, -direction.Z, direction.Y)));
        var result = BrepSpatialQueries.Raycast(shape.Body, ray);
        Require(result.IsSuccess, "Independent BRep ray query rejected a primitive.");
        return result.Value.Any(hit => hit.T >= 0 && hit.T <= 48000);
    }

    private static Native3DScene Receiver(float elevation)
    {
        var vertices = new List<Native3DVertex>();
        PrimitiveGeometry3D.AddBox(vertices, new(0, elevation - .1f, 0), new(6, .1f, 6), Vector4.One);
        return new(vertices.ToArray(), []);
    }

    private static Matrix4x4 TopCamera(float elevation)
    {
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(12, 12, .1f, 30);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(new(0, 12 + elevation, 0), new(0, elevation, 0), Vector3.UnitZ) * projection;
    }

    private static int CountUnknown(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4)
        .Count(index => pixels[index * 4] > 240 && pixels[index * 4 + 2] > 240 && pixels[index * 4 + 1] < 220);

    private static int Changed(Capture first, Capture second) => Enumerable.Range(0, first.Pixels.Length / 4).Count(index =>
        Math.Abs(first.Pixels[index * 4] - second.Pixels[index * 4])
        + Math.Abs(first.Pixels[index * 4 + 1] - second.Pixels[index * 4 + 1])
        + Math.Abs(first.Pixels[index * 4 + 2] - second.Pixels[index * 4 + 2]) > 8);

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);
    private static string? ReadRuntimeSource(string name) => File.Exists(Asset(name)) ? File.ReadAllText(Asset(name)) : null;

    private static void ContactSheet(string output, Capture[] captures)
    {
        using var bitmap = new SKBitmap(GallerySize * 3, (GallerySize + 36) * 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(20, 22, 26));
        using var paint = new SKPaint { Color = SKColors.White, TextSize = 22, IsAntialias = true };
        for (int index = 0; index < captures.Length; index++)
        {
            int x = index % 3 * GallerySize;
            int y = index / 3 * (GallerySize + 36);
            using var image = SKBitmap.Decode(Path.Combine(output, captures[index].Name + ".png"));
            canvas.DrawText(captures[index].Name, x + 12, y + 26, paint);
            canvas.DrawBitmap(image, x, y + 36);
        }
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, "comparison.png"), encoded.ToArray());
    }

    private static void CopyLicences(string output)
    {
        Directory.CreateDirectory(Path.Combine(output, "licenses"));
        File.WriteAllText(Path.Combine(output, "licenses/GPL-3.0.txt"), AetherisLightField.LicenseText("GPL-3.0"));
        File.WriteAllText(Path.Combine(output, "licenses/AGPL-3.0.txt"), AetherisLightField.LicenseText("AGPL-3.0"));
        File.Copy(Path.GetFullPath("src/Integrations/Aurelian.Lighting.Aetheris/NOTICE.md"), Path.Combine(output, "NOTICE.md"), true);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
