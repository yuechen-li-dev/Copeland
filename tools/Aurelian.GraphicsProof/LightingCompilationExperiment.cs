using System.Diagnostics;
using System.Numerics;
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
using Aurelian.Lighting.Aetheris;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Runtime;
using Aurelian.Runtime.Lighting;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using SkiaSharp;

internal static class LightingCompilationExperiment
{
    private const int Size = 128;
    private const int PreviewSize = 512;
    private sealed record Body(BrepBody Brep, Vector3 Position, AetherisLightingBody Declaration);
    private sealed record Room(Body[] Bodies, AetherisLightingScene Lighting, Native3DScene Display);
    private sealed record Hit(Body Body, Vector3 Point, Vector3 Normal, double Distance);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "compilation-evidence.json");
        File.WriteAllText(evidencePath, "{\"accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian Lighting Compilation"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var assets = new GameAssets();
        Room room = BuildRoom();
        CompiledGraphicsProgram bakeProgram = Compile(room.Lighting, "FieldBake3D.v.ts", "room");
        Room unlitPanel = BuildRoom(emission: false);
        CompiledGraphicsProgram noEmissionProgram = Compile(unlitPanel.Lighting, "FieldBake3D.v.ts", "no-emission");
        Room moved = BuildRoom(shift: 1.1f);
        CompiledGraphicsProgram movedProgram = Compile(moved.Lighting, "FieldBake3D.v.ts", "moved");
        CompiledGraphicsProgram starved = Compile(room.Lighting, "FieldBake3D.v.ts", "starved", starved: true);
        CompiledGraphicsProgram coarseProgram = Compile(room.Lighting, "FieldBake3D.v.ts", "coarse", coarse: true);
        Vector3 sun = Vector3.Normalize(new Vector3(.6f, 1, .4f));
        Vector3 eye = new(2, 3, 5);
        var snapshots = new List<object>();
        var retained = new Dictionary<LightingBakeTicket, VulkanLightingBake>();
        var reported = new HashSet<LightingBakeTicket>();
        var plans = new Dictionary<string, (CompiledGraphicsProgram Program, float[] Parameters)>(StringComparer.Ordinal);
        var controller = new LightingCompilationController(request =>
        {
            var source = plans[request.Compilation.ContentKey];
            var batch = new VulkanLightingBake(plant, request.Compilation, source.Program, source.Parameters);
            try
            {
                LightingBakeTicket ticket = batch.Submit(request.Generation);
                retained.Add(ticket, batch);
                return ticket;
            }
            catch
            {
                batch.Dispose();
                throw;
            }
        }, traceCapacity: 2048);
        var availability = new LightingPlantAvailability(0, true,
            new HashSet<LightingArtifactKind> { LightingArtifactKind.ShadowVisibility,
                LightingArtifactKind.DiffuseTransfer, LightingArtifactKind.ReflectionRadiance }, .00001);
        LightingScheduleBudget budget = new(600_000, 10);
        var shadowPlan = Plan("shadow", room, bakeProgram, 0, eye);
        var diffusePlan = Plan("diffuse", room, bakeProgram, 1, Vector3.Zero);
        var reflectionPlan = Plan("reflection", room, bakeProgram, 2, eye);
        var coarsePlan = Plan("diffuse-coarse", room, coarseProgram, 1, Vector3.Zero);
        try
        {
            controller.SetTarget(shadowPlan);
            controller.SetTarget(diffusePlan);
            controller.SetTarget(reflectionPlan);
            controller.SetTarget(coarsePlan);
            Complete(["shadow", "diffuse", "reflection", "diffuse-coarse"]);
            VulkanLightingBake shadow = Published("shadow");
            VulkanLightingBake diffuse = Published("diffuse");
            VulkanLightingBake reflection = Published("reflection");
            float[] shadowValues = shadow.ReadLinear();
            float[] diffuseValues = diffuse.ReadLinear();
            float[] reflectionValues = reflection.ReadLinear();
            int shadowMismatches = VerifyShadow(room, shadowValues, sun);
            object radianceComparisons = VerifyRadiance(room, diffuseValues, reflectionValues, sun, eye, strict: true);
            object coarseComparisons = VerifyRadiance(room, Published("diffuse-coarse").ReadLinear(), reflectionValues, sun, eye, strict: false);
            Require(diffuseValues.Max() > 1, "Emissive panel did not contribute HDR radiance.");
            Require(Coloured(diffuseValues) > 1000, "Diffuse transport did not carry authored surface colours.");
            Require(Coloured(reflectionValues) > 1000, "Reflection rays did not find coloured scene surfaces.");
            Preview("shadow", shadow, 1);
            Preview("diffuse", diffuse, 2);
            Preview("reflection", reflection, 1);
            Preview("diffuse-coarse", Published("diffuse-coarse"), 2);

            int before = retained.Count;
            for (int index = 0; index < 60; index++)
            {
                controller.Tick([availability], [], budget);
            }
            Require(retained.Count == before, "Unchanged lighting retraced after publication.");
            // Runtime scaling samples exactly the same texture; it cannot mutate the artifact key.
            using (var target = new VulkanNativeFrameTarget(plant, PreviewSize, PreviewSize))
            using (var view = diffuse.CreateView(target, assets.Shader("ToneMap3D.v.ts")))
            {
                var first = view.Render([1, 1, 1, 0], capture: true);
                var repeat = view.Render([1, 1, 1, 0], capture: true);
                var brighter = view.Render([2, 1, 1, 0], capture: true);
                Require(first.PixelSha256 == repeat.PixelSha256, "Retained samples changed on repeat.");
                Require(first.PixelSha256 != brighter.PixelSha256 && diffuse.SubmissionCount == 1,
                    "Runtime light scaling did not reuse the compiled response.");
            }
            // An authored material change invalidates transport, while the previous completed owner remains intact.
            var darkPlan = Plan("diffuse", unlitPanel, noEmissionProgram, 1, Vector3.Zero);
            controller.SetTarget(darkPlan);
            Require(controller.Observe("diffuse").Published is null, "Changed material still exposed stale lighting.");
            Complete(["diffuse"]);
            var dark = Published("diffuse");
            float[] darkValues = dark.ReadLinear();
            int emissionPixels = Different(diffuseValues, darkValues, .03f);
            Require(emissionPixels > 1000, "Removing emission did not materially change one-bounce transport.");
            Preview("diffuse-no-emission", dark, 2);

            controller.SetTarget(Plan("shadow", moved, movedProgram, 0, eye));
            Complete(["shadow"]);
            var shifted = Published("shadow");
            int movedPixels = Different(shadowValues, shifted.ReadLinear(), .5f);
            Require(movedPixels > 300, "Moved occluder failed to invalidate baked shadows.");
            Preview("shadow-moved", shifted, 1);

            // View-dependent reflection inputs belong to the compilation domain.
            controller.SetTarget(Plan("reflection", room, bakeProgram, 2, new(-2, 3, 5)));
            Complete(["reflection"]);
            var newView = Published("reflection");
            int reflectionPixels = Different(reflectionValues, newView.ReadLinear(), .03f);
            Require(reflectionPixels > 1000, "Reflection eye change reused a stale view-dependent bake.");
            Preview("reflection-new-eye", newView, 1);

            controller.SetTarget(Plan("negative", room, starved, 0, eye));
            Complete(["negative"], allowFailure: true);
            Require(controller.Observe("negative").Phase == "Failed" && controller.Observe("negative").Published is null,
                "Exhausted tracing was published as valid lighting.");
            VulkanLightingBake rejected = retained.Values.Single(item => item.Compilation.Id == "negative");
            float[] rejectedValues = rejected.ReadLinear();
            int unresolvedTexels = Enumerable.Range(0, rejectedValues.Length / 4).Count(index => rejectedValues[index * 4 + 3] < .999f);
            Require(unresolvedTexels > Size * Size / 2, "Starved trace did not expose unresolved texels.");
            using (var target = new VulkanNativeFrameTarget(plant, PreviewSize, PreviewSize))
            {
                bool rejectedSampling = false;
                try
                {
                    using var view = rejected.CreateView(target, assets.Shader("ToneMap3D.v.ts"));
                }
                catch (InvalidOperationException exception) when (exception.Message.StartsWith("Diagnostic resolution-mask", StringComparison.Ordinal))
                {
                    rejectedSampling = true;
                }
                Require(rejectedSampling, "Unresolved output was available to the texture sampling path.");
            }
            File.WriteAllText(Path.Combine(output, "controller-inspection.json"), JsonSerializer.Serialize(controller.Inspector.Observe(),
                new JsonSerializerOptions { WriteIndented = true }));
            CaptureRoom();
            Sheet(output);
            Directory.CreateDirectory(Path.Combine(output, "licenses"));
            foreach (string name in new[] { "GPL-3.0", "AGPL-3.0" })
            {
                File.WriteAllText(Path.Combine(output, "licenses", name + ".txt"), AetherisLightField.LicenseText(name));
            }
            File.Copy("src/Integrations/Aurelian.Lighting.Aetheris/NOTICE.md", Path.Combine(output, "NOTICE.md"), true);
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Accepted = true,
                Device = plant.Facts.PhysicalDeviceName,
                Scope = "Static horizontal receiver compilation, fixed Lambertian materials, one-bounce quadrature and view-dependent sharp reflection radiance",
                ShadowBrepMismatches = shadowMismatches,
                ShadowRays = Size * Size,
                RadianceComparisons = radianceComparisons,
                CoarseRadianceComparisons = coarseComparisons,
                EmissionChangedPixels = emissionPixels,
                MovedShadowPixels = movedPixels,
                ReflectionEyeChangedPixels = reflectionPixels,
                UnchangedFrames = 60,
                UnchangedFrameSubmissions = 0,
                RuntimeScalingRebakes = 0,
                BudgetExhaustionUnresolvedTexels = unresolvedTexels,
                UnqualifiedSamplingRejected = true,
                AllSubmissionsNonblocking = true,
                DedicatedComputeQueue = false,
                MultiGpuQualified = false,
                Captures = snapshots,
                Limits = new[]
                {
                    "GPU compilation uses the graphics queue; CPU-nonblocking submission does not establish simultaneous GPU execution",
                    "Whole 128x128 batches, not tiled light-space maps, general mesh lightmaps or probe volumes",
                    "One-bounce diffuse transport uses sixteen fixed directions, with visible quadrature error; the coarse 0.5mm contact experiment retains one named discrepancy",
                    "Reflection output is incoming sharp reflection radiance for one declared eye, not a complete glossy BRDF",
                    "Readback qualifies the resolution mask once per bake; steady lookup has no tracing/readback requirement",
                    "No dynamic character overlay, multi-bounce solve, temporal reconstruction or multi-GPU execution",
                },
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("AURELIAN_LIGHTING_COMPILATION_PASSED " + plant.Facts.PhysicalDeviceName);
        }
        finally
        {
            foreach (VulkanLightingBake batch in retained.Values)
            {
                batch.Dispose();
            }
        }

        LightingCompilation Plan(string id, Room source, CompiledGraphicsProgram program, int mode, Vector3 cameraEye)
        {
            LightingArtifactKind kind = mode switch
            {
                0 => LightingArtifactKind.ShadowVisibility,
                1 => LightingArtifactKind.DiffuseTransfer,
                _ => LightingArtifactKind.ReflectionRadiance,
            };
            var recipe = new AetherisLightingBakeRecipe(source.Lighting, kind,
                new(new(-2.7f), new(5.4f)), sun, cameraEye, traceSteps: program == starved ? 1 : 256,
                surfaceHitToleranceMetres: program == coarseProgram ? .0005f : .0001f);
            float[] parameters = recipe.UniformValues();
            var compilation = recipe.Compilation(id, program, VulkanLightingBake.ParameterIdentity(parameters), Size, Size);
            plans[compilation.ContentKey] = (program, parameters);
            return compilation;
        }

        void Complete(string[] ids, bool allowFailure = false)
        {
            var timeout = Stopwatch.StartNew();
            while (ids.Any(id => controller.Observe(id).Published is null
                && (!allowFailure || controller.Observe(id).Phase != "Failed")))
            {
                Require(timeout.Elapsed.TotalSeconds < 30, "Lighting controller timed out: "
                    + string.Join("; ", ids.Select(id => controller.Observe(id))));
                var finished = new List<LightingBakeCompletion>();
                foreach ((LightingBakeTicket ticket, VulkanLightingBake batch) in retained)
                {
                    if (!reported.Contains(ticket) && batch.IsComplete())
                    {
                        batch.ReadLinear();
                        bool resolved = batch.IsQualified;
                        finished.Add(new(ticket, true, resolved, batch.GpuMilliseconds(), resolved ? null : "UnresolvedRays"));
                        reported.Add(ticket);
                    }
                }
                controller.Tick([availability], finished, budget);
                Thread.Sleep(1);
            }
        }

        VulkanLightingBake Published(string id) => retained[controller.Observe(id).Published!];

        void Preview(string name, VulkanLightingBake batch, float exposure)
        {
            using var target = new VulkanNativeFrameTarget(plant, PreviewSize, PreviewSize);
            using var view = batch.CreateView(target, assets.Shader("ToneMap3D.v.ts"));
            var times = new List<double>();
            for (int index = 0; index < 8; index++)
            {
                var frame = view.Render([exposure, 1, 1, 0]);
                if (index >= 3)
                {
                    times.Add(frame.GpuPassTimes[0].Milliseconds);
                }
            }
            var captured = view.Render([exposure, 1, 1, 0], capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), PreviewSize, PreviewSize, captured.Pixels!);
            snapshots.Add(new
            {
                Name = name,
                batch.Compilation.ContentKey,
                BakeMilliseconds = batch.GpuMilliseconds(),
                LookupMedianMilliseconds = times.Order().ElementAt(2),
                batch.SubmissionCount,
                captured.PixelSha256,
            });
        }

        CompiledGraphicsProgram Compile(AetherisLightingScene source, string root, string name, bool starved = false, bool coarse = false)
        {
            var overrides = new Dictionary<string, string>();
            if (starved)
            {
                overrides["FieldTraceBudget.v.ts"] = "export function TraceBudget(): u32 {\n    return 1;\n}\n";
            }
            if (coarse)
            {
                overrides["FieldSurfacePrecision.v.ts"] = "export function SurfaceHitTolerance(): f32 {\n    return 0.0005;\n}\n";
            }
            var files = source.Sources(root, ReadAsset, overrides);
            foreach (var file in files)
            {
                File.WriteAllText(Path.Combine(output, name + "-" + Path.GetFileName(file.Path)), file.Source);
            }
            var module = GpuGraphicsBinder.Compile(new(files));
            Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
            var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
            Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated,
                backend.Vertex.DxcOutput + backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
            File.WriteAllText(Path.Combine(output, name + ".hlsl"), backend.Hlsl);
            return CompiledGraphicsProgramExporter.Export(module, backend);
        }

        void CaptureRoom()
        {
            var program = Compile(room.Lighting, "FieldSolid3D.v.ts", "room-direct");
            using var target = new VulkanNativeFrameTarget(plant, PreviewSize, PreviewSize);
            using var renderer = new VulkanSolid3DRenderer(plant, program, target,
                shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"));
            renderer.Settings = Graphics3DSettings.Default with { SunDirection = sun, SkyAmbient = new(.08f), GroundAmbient = new(.02f) };
            var camera = Camera3D.Matrix(new(7, 6, 9), Vector3.Normalize(new Vector3(0, 1, 0) - new Vector3(7, 6, 9)), 1);
            var frame = renderer.Render(room.Display, camera, new(7, 6, 9), new(.08f, .09f, .1f, 1), capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, "room-direct.png"), PreviewSize, PreviewSize, frame.Pixels!);
        }
    }

    private static Room BuildRoom(float shift = 0, bool emission = true)
    {
        var bodies = new List<Body>();
        AddBox("floor", new(6, .2f, 6), new(0, -.1f, 0), new(.6f), Vector3.Zero);
        AddBox("red-wall", new(.2f, 3, 6), new(-3.1f, 1.5f, 0), new(.8f, .04f, .025f), Vector3.Zero);
        AddBox("back-wall", new(6, 3, .2f), new(0, 1.5f, -3.1f), new(.55f), Vector3.Zero);
        AddBox("blue-block", new(1, 1, 1), new(.8f + shift, .5f, -.3f), new(.04f, .12f, .7f), Vector3.Zero);
        AddBox("lamp", new(1.5f, 1.3f, .08f), new(.8f, 1.8f, -2.92f), new(.8f), emission ? new(8, 5, 2) : Vector3.Zero);
        var source = new AetherisLightingScene(bodies.Select(item => item.Declaration));
        var vertices = new List<Native3DVertex>();
        foreach (Body body in bodies)
        {
            var mesh = BrepDisplayTessellator.Tessellate(body.Brep, DisplayTessellationOptions.Default);
            Require(mesh.IsSuccess, "Aetheris room tessellation failed.");
            foreach (DisplayFaceMeshPatch patch in mesh.Value.FacePatches)
            {
                foreach (int index in patch.TriangleIndices)
                {
                    Point3D point = patch.Positions[index];
                    Vector3D normal = patch.Normals[index];
                    vertices.Add(new(new Vector3((float)point.X, (float)point.Z, -(float)point.Y) * .001f + body.Position,
                        new((float)normal.X, (float)normal.Z, -(float)normal.Y), new(body.Declaration.Albedo, 1)));
                }
            }
        }
        return new(bodies.ToArray(), source, new(vertices.ToArray(), []));

        void AddBox(string id, Vector3 size, Vector3 position, Vector3 albedo, Vector3 emitted)
        {
            var shape = new SdfBoxNode(size.X * 1000, size.Z * 1000, size.Y * 1000);
            var placed = new SdfTransformNode(shape, Transform3D.CreateTranslation(new(position.X * 1000,
                -position.Z * 1000, position.Y * 1000)));
            bodies.Add(new(BrepPrimitives.CreateBox(size.X * 1000, size.Z * 1000, size.Y * 1000).Value, position,
                new(id, placed, albedo, emitted)));
        }
    }

    private static int VerifyShadow(Room room, float[] values, Vector3 direction)
    {
        int mismatches = 0;
        int hits = 0;
        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                Vector3 point = new(-2.7f + (column + .5f) * 5.4f / Size, .004f, -2.7f + (row + .5f) * 5.4f / Size);
                bool hit = room.Bodies.Any(body =>
                {
                    Vector3 origin = (point - body.Position) * 1000;
                    var ray = new Ray3D(new(origin.X, -origin.Z, origin.Y),
                        Direction3D.Create(new(direction.X, -direction.Z, direction.Y)));
                    var result = BrepSpatialQueries.Raycast(body.Brep, ray);
                    Require(result.IsSuccess, "BRep shadow oracle failed.");
                    return result.Value.Any(item => item.T >= 0 && item.T <= 48_000);
                });
                if (hit)
                {
                    hits++;
                }
                if ((values[(row * Size + column) * 4] < .5f) != hit)
                {
                    mismatches++;
                }
            }
        }
        Require(hits > 500 && mismatches < 16, "Cached field visibility disagrees with BRep: " + mismatches);
        return mismatches;
    }

    private static int Different(float[] left, float[] right, float threshold)
        => Enumerable.Range(0, left.Length / 4).Count(index => Math.Abs(left[index * 4] - right[index * 4])
            + Math.Abs(left[index * 4 + 1] - right[index * 4 + 1]) + Math.Abs(left[index * 4 + 2] - right[index * 4 + 2]) > threshold);

    private static int Coloured(float[] values)
        => Enumerable.Range(0, values.Length / 4).Count(index => Math.Abs(values[index * 4] - values[index * 4 + 2]) > .03f);

    private static object VerifyRadiance(Room room, float[] diffuse, float[] reflection, Vector3 sun, Vector3 eye, bool strict)
    {
        int compared = 0;
        int diffuseOutliers = 0;
        int reflectionOutliers = 0;
        float maximumDiffuseError = 0;
        float maximumReflectionError = 0;
        var discrepancies = new List<object>();
        for (int row = 5; row < Size; row += 12)
        {
            for (int column = 5; column < Size; column += 12)
            {
                Vector3 point = new(-2.7f + (column + .5f) * 5.4f / Size, 0, -2.7f + (row + .5f) * 5.4f / Size);
                Vector3 origin = point + new Vector3(0, .004f, 0);
                // Points covered by the solid block are not visible receiver surfaces.
                if (room.Lighting.Field.EvaluateMetres(origin) < .001)
                {
                    continue;
                }
                Vector3 expectedDiffuse = Vector3.Zero;
                for (int sample = 0; sample < 16; sample++)
                {
                    double radius = Math.Sqrt((sample + .5) / 16);
                    double angle = sample * 2.399963229728653;
                    Vector3 direction = new((float)(radius * Math.Cos(angle)), (float)Math.Sqrt(1 - radius * radius),
                        (float)(radius * Math.Sin(angle)));
                    Hit? hit = Closest(room, origin, direction);
                    if (hit is not null)
                    {
                        expectedDiffuse += Radiance(hit);
                    }
                }
                expectedDiffuse /= 16;
                Vector3 view = Vector3.Normalize(eye - point);
                Hit? reflected = Closest(room, origin, new(-view.X, view.Y, -view.Z));
                Vector3 expectedReflection = reflected is null ? new(.02f) : Radiance(reflected);
                int offset = (row * Size + column) * 4;
                float diffuseError = Error(expectedDiffuse, diffuse, offset);
                float reflectionError = Error(expectedReflection, reflection, offset);
                maximumDiffuseError = Math.Max(maximumDiffuseError, diffuseError);
                maximumReflectionError = Math.Max(maximumReflectionError, reflectionError);
                if (diffuseError > .01f)
                {
                    diffuseOutliers++;
                }
                if (reflectionError > .02f)
                {
                    reflectionOutliers++;
                }
                if (diffuseError > .01f || reflectionError > .02f)
                {
                    discrepancies.Add(new
                    {
                        Row = row,
                        Column = column,
                        DiffuseError = diffuseError,
                        ReflectionError = reflectionError,
                        ExpectedDiffuse = new[] { expectedDiffuse.X, expectedDiffuse.Y, expectedDiffuse.Z },
                        ActualDiffuse = diffuse[offset..(offset + 3)],
                    });
                }
                compared++;
            }
        }
        Require(compared > 80 && diffuseOutliers <= (strict ? 0 : 1) && maximumDiffuseError < .55f && reflectionOutliers == 0,
            $"BRep radiance oracle disagreement: diffuse={diffuseOutliers}, reflection={reflectionOutliers}, points={compared}.");
        return new
        {
            ReceiverPoints = compared,
            DiffuseOutliers = diffuseOutliers,
            ReflectionOutliers = reflectionOutliers,
            MaximumDiffuseError = maximumDiffuseError,
            MaximumReflectionError = maximumReflectionError,
            DiffuseErrorThreshold = .01f,
            ReflectionErrorThreshold = .02f,
            DiffuseAccuracyPassed = diffuseOutliers == 0,
            ReflectionAccuracyPassed = reflectionOutliers == 0,
            Discrepancies = discrepancies,
            LegacyContactToleranceProbe = DiagnoseContactTolerance(room, 41, 125),
        };

        Vector3 Radiance(Hit hit)
        {
            float visibility = Closest(room, hit.Point + hit.Normal * .004f, sun) is null ? 1 : 0;
            return hit.Body.Declaration.Emission
                + hit.Body.Declaration.Albedo * (Math.Max(Vector3.Dot(hit.Normal, sun), 0) * visibility / MathF.PI);
        }

        static float Error(Vector3 expected, float[] values, int offset)
            => Math.Max(Math.Abs(expected.X - values[offset]),
                Math.Max(Math.Abs(expected.Y - values[offset + 1]), Math.Abs(expected.Z - values[offset + 2])));
    }

    private static Hit? Closest(Room room, Vector3 origin, Vector3 direction)
    {
        Hit? nearest = null;
        foreach (Body body in room.Bodies)
        {
            Vector3 relative = (origin - body.Position) * 1000;
            var ray = new Ray3D(new(relative.X, -relative.Z, relative.Y),
                Direction3D.Create(new(direction.X, -direction.Z, direction.Y)));
            var query = BrepSpatialQueries.Raycast(body.Brep, ray);
            Require(query.IsSuccess, "BRep transport oracle rejected a body.");
            foreach (RayHit hit in query.Value)
            {
                if (hit.T < 0 || hit.T > 48_000 || nearest is not null && hit.T >= nearest.Distance)
                {
                    continue;
                }
                Direction3D normal = hit.Normal ?? throw new InvalidOperationException("BRep oracle returned no surface normal.");
                Vector3 gamePoint = new Vector3((float)hit.Point.X, (float)hit.Point.Z, -(float)hit.Point.Y) * .001f + body.Position;
                nearest = new(body, gamePoint, new((float)normal.X, (float)normal.Z, -(float)normal.Y), hit.T);
            }
        }
        return nearest;
    }

    // A named negative witness for the retained 0.5mm contact tolerance. This is diagnostic CPU
    // evaluation of the already-owned field, not another production traversal backend.
    private static object DiagnoseContactTolerance(Room room, int row, int column)
    {
        var bodies = room.Bodies.Select(body => (body.Declaration.Id, Field: AetherisLightField.Create(body.Declaration.Geometry))).ToArray();
        Vector3 origin = new(-2.7f + (column + .5f) * 5.4f / Size, .004f, -2.7f + (row + .5f) * 5.4f / Size);
        var differences = new List<object>();
        for (int sample = 0; sample < 16; sample++)
        {
            double radius = Math.Sqrt((sample + .5) / 16);
            double angle = sample * 2.399963229728653;
            Vector3 direction = new((float)(radius * Math.Cos(angle)), (float)Math.Sqrt(1 - radius * radius),
                (float)(radius * Math.Sin(angle)));
            Hit? exact = Closest(room, origin, direction);
            float travel = 0;
            string? fieldBody = null;
            float contactDistance = 0;
            for (int step = 0; step < 256 && travel < 48; step++)
            {
                Vector3 point = origin + direction * travel;
                float distance = (float)room.Lighting.Field.EvaluateMetres(point);
                if (distance <= .0005f)
                {
                    fieldBody = bodies.OrderBy(body => Math.Abs(body.Field.EvaluateMetres(point))).First().Id;
                    contactDistance = distance;
                    break;
                }
                travel += (distance - .00005f) * .9f;
            }
            if (fieldBody != exact?.Body.Declaration.Id)
            {
                differences.Add(new
                {
                    Sample = sample,
                    ExactBody = exact?.Body.Declaration.Id,
                    FieldBody = fieldBody,
                    ContactDistanceMetres = contactDistance,
                    TravelMetres = travel,
                });
            }
        }
        return new { Row = row, Column = column, Differences = differences };
    }

    private static string? ReadAsset(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static void Sheet(string output)
    {
        string[] names = ["room-direct", "shadow", "shadow-moved", "diffuse", "diffuse-no-emission", "reflection"];
        using var bitmap = new SKBitmap(PreviewSize * 3, (PreviewSize + 32) * 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(20, 22, 26));
        using var paint = new SKPaint { Color = SKColors.White, TextSize = 20, IsAntialias = true };
        for (int index = 0; index < names.Length; index++)
        {
            int x = index % 3 * PreviewSize;
            int y = index / 3 * (PreviewSize + 32);
            using var image = SKBitmap.Decode(Path.Combine(output, names[index] + ".png"));
            canvas.DrawText(names[index], x + 10, y + 24, paint);
            canvas.DrawBitmap(image, x, y + 32);
        }
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, "comparison.png"), encoded.ToArray());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
