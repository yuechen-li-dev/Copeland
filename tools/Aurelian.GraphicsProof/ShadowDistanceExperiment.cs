using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.GameHost.Silk;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.Profile;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Machina.VectorAssets;
using Msdfgen;
using SkiaSharp;
using Point = Copeland.Profile.VectorPoint;

/// <summary>
/// A bounded static, planar receiver experiment. The usual renderer and material binding
/// carry a linear baked shadow field; no runtime shadow backend or default is replaced.
/// </summary>
internal static class ShadowDistanceExperiment
{
    private const int ImageSize = 512;
    private const int ReferenceScale = 4;
    private static readonly Vector3 Sun = new(4, 1, .7f);
    private static readonly ModelSampler LinearClamp = new(true, true, TextureWrap.Clamp, TextureWrap.Clamp);
    private static readonly ModelSampler NearestClamp = new(false, false, TextureWrap.Clamp, TextureWrap.Clamp);
    private static readonly ModelTexture White = SolidTexture(255);
    private static readonly ModelTexture Black = SolidTexture(0);
    private static readonly Graphics3DSettings Settings = Graphics3DSettings.Default with
    {
        SunDirection = Sun,
        SunIntensity = 1.5f,
        SunColor = Vector3.One,
        SkyAmbient = new(.08f),
        GroundAmbient = new(.04f),
        ToneMapping = false,
        Shadows = false,
        ShadowRadius = 24,
    };

    private sealed record Capture(string Name, byte[] Pixels, string Hash, IReadOnlyList<Native3DGpuPassTime> Timings);
    private sealed record Score(string Name, double MeanCoverageError, int SilhouetteMismatchPixels,
        double[] ThinBarCenterCoverage, string PixelSha256, IReadOnlyList<Native3DGpuPassTime> GpuPassTimes);
    private sealed record FieldPair(VectorIconMsdfArtifact Artifact, ModelTexture Msdf, ModelTexture Sdf, double BakeMilliseconds);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "shadow-evidence.json");
        File.WriteAllText(evidence, "{\"accepted\":false}");
        List<Point[]> polygons = Fixture();
        var contours = polygons.Select(points => new VectorContour(points.Select((point, index) =>
            (VectorSegment)new VectorLine(point, points[(index + 1) % points.Length])).ToArray())).ToArray();
        var shape = new VectorShape(contours);
        string svg = FixtureSvg(polygons);
        File.WriteAllText(Path.Combine(output, "projected-caster.svg"), svg);

        FieldPair field64 = Generate(shape, svg, 64, output);
        VectorBounds bounds = field64.Artifact.FieldBounds;
        ModelTexture raster = RasterMask(polygons, bounds, 64);
        NativeGameGraphics.WritePng(Path.Combine(output, "raster64-field.png"), 64, 64, raster.Rgba.ToArray());
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian Shadow Distance Research"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var assets = new GameAssets();
        var programs = new Dictionary<string, CompiledGraphicsProgram>();
        foreach (string mode in new[] { "Reference", "Raster", "Field", "Depth" })
        {
            programs.Add(mode, Compile(mode, output));
        }

        Vector3 eye = new(0, 12, 0);
        Matrix4x4 camera = TopCamera(eye);
        // Retain an invalid pre-union contour specimen. A distance-field encoding is not a
        // substitute for computing the union of overlapping projected occluders.
        List<Point[]> overlapping = Fixture();
        overlapping[2] = [new(13, 65), new(28, 52), new(42, 68), new(28, 93)];
        var overlapShape = new VectorShape(overlapping.Select(points => new VectorContour(points.Select((point, index) =>
            (VectorSegment)new VectorLine(point, points[(index + 1) % points.Length])).ToArray())).ToArray());
        VectorIconMsdfArtifact overlap = VectorIconMsdfCompiler.Compile(overlapShape, FixtureSvg(overlapping),
            "overlapping-before-union.svg", new VectorIconCompilationSettings(64, minimumShortAxis: 64));
        ModelTexture overlapTexture = Encode("overlap", overlap.FieldPixels.Span, 3, 64);
        Capture overlapField = Render("invalid-overlap-msdf64", "Field", Floor(bounds, overlapTexture, LinearClamp, overlap.FieldBounds), camera, eye);
        Capture overlapReference = Render("invalid-overlap-reference", "Reference", GeometricReference(overlapping, bounds), camera, eye, ReferenceScale);
        File.WriteAllText(Path.Combine(output, "overlapping-before-union.svg"), FixtureSvg(overlapping));
        Native3DScene floor = Floor(bounds, White, LinearClamp);
        Capture lit = Render("unshadowed", "Reference", floor, camera, eye);
        Capture dark = Render("fully-shadowed", "Reference", Floor(bounds, Black, LinearClamp), camera, eye);
        Native3DScene geometric = GeometricReference(polygons, bounds);
        Capture reference = Render("reference", "Reference", geometric, camera, eye, ReferenceScale);
        var scores = new List<Score>();
        Capture rasterCapture = Render("raster64-pcf", "Raster", Floor(bounds, raster, NearestClamp), camera, eye);
        scores.Add(Measure(rasterCapture, reference, lit, dark, polygons, camera));
        Capture sdf = Render("sdf64", "Field", Floor(bounds, field64.Sdf, LinearClamp), camera, eye);
        scores.Add(Measure(sdf, reference, lit, dark, polygons, camera));
        Capture msdf = Render("msdf64", "Field", Floor(bounds, field64.Msdf, LinearClamp), camera, eye);
        var fieldCaptures = new Dictionary<int, Capture> { [64] = msdf };
        scores.Add(Measure(msdf, reference, lit, dark, polygons, camera));
        Capture repeat = Render("msdf64-repeat", "Field", Floor(bounds, field64.Msdf, LinearClamp), camera, eye);
        Require(msdf.Hash == repeat.Hash, "MSDF repeat changed pixels.");

        // The actual default depth-shadow path sees an elevated caster outside the camera frustum.
        // Its projection onto Y=0 is exactly the same polygon fixture as the baked fields.
        Native3DScene casterScene = floor with { Geometry = Casters(polygons) };
        Capture depth = Render("depth1024-pcf", "Depth", casterScene, camera, eye, shadows: true);
        scores.Add(Measure(depth, reference, lit, dark, polygons, camera));
        var bakeTimes = new List<object>
        {
            new { Size = 64, field64.BakeMilliseconds, TextureBytes = field64.Msdf.Rgba.Length },
        };
        foreach (int size in new[] { 32, 128 })
        {
            FieldPair field = Generate(shape, svg, size, output);
            // All sizes must use their own generated field bounds: the generator's padding is in texels.
            // Reframe UVs to the common receiver without changing the source contours.
            Capture candidate = Render("msdf" + size, "Field", Floor(bounds, field.Msdf, LinearClamp, field.Artifact.FieldBounds), camera, eye);
            fieldCaptures.Add(size, candidate);
            scores.Add(Measure(candidate, reference, lit, dark, polygons, camera));
            bakeTimes.Add(new { Size = size, field.BakeMilliseconds, TextureBytes = field.Msdf.Rgba.Length });
        }

        var motion = new List<object>();
        foreach (float shift in new[] { -.008f, .008f })
        {
            Vector3 movedEye = eye + new Vector3(shift, 0, shift * .6f);
            Matrix4x4 movedCamera = TopCamera(movedEye);
            string label = shift < 0 ? "minus" : "plus";
            Capture movedLit = Render("lit-" + label, "Reference", floor, movedCamera, movedEye);
            Capture movedDark = Render("dark-" + label, "Reference", Floor(bounds, Black, LinearClamp), movedCamera, movedEye);
            Capture movedReference = Render("reference-" + label, "Reference", geometric, movedCamera, movedEye, ReferenceScale);
            Capture movedMsdf = Render("msdf64-" + label, "Field", Floor(bounds, field64.Msdf, LinearClamp), movedCamera, movedEye);
            Capture movedRaster = Render("raster64-" + label, "Raster", Floor(bounds, raster, NearestClamp), movedCamera, movedEye);
            Score fieldScore = Measure(movedMsdf, movedReference, movedLit, movedDark, polygons, movedCamera);
            Score rasterMotionScore = Measure(movedRaster, movedReference, movedLit, movedDark, polygons, movedCamera);
            Require(fieldScore.MeanCoverageError < rasterMotionScore.MeanCoverageError * .7,
                "MSDF lost its improvement under a subpixel camera shift.");
            motion.Add(new { WorldShift = shift, Msdf = fieldScore, Raster = rasterMotionScore });
        }
        Vector3 angledEye = new(7, 10, 12);
        Matrix4x4 angled = Matrix4x4.CreateLookAt(angledEye, Vector3.Zero, Vector3.UnitY)
            * Matrix4x4.CreatePerspectiveFieldOfView(.8f, 1, .1f, 80);
        angled.M12 *= -1;
        angled.M22 *= -1;
        angled.M32 *= -1;
        angled.M42 *= -1;
        Capture obliqueReference = Render("oblique-reference", "Reference", geometric, angled, angledEye, ReferenceScale);
        Capture oblique = Render("oblique-msdf64", "Field", Floor(bounds, field64.Msdf, LinearClamp), angled, angledEye);
        Capture obliqueLit = Render("oblique-lit", "Reference", floor, angled, angledEye);
        Capture obliqueDark = Render("oblique-dark", "Reference", Floor(bounds, Black, LinearClamp), angled, angledEye);
        Score obliqueScore = Measure(oblique, obliqueReference, obliqueLit, obliqueDark, polygons, angled);
        Capture obliqueRaster = Render("oblique-raster64", "Raster", Floor(bounds, raster, NearestClamp), angled, angledEye);
        Score obliqueRasterScore = Measure(obliqueRaster, obliqueReference, obliqueLit, obliqueDark, polygons, angled);
        Require(obliqueScore.MeanCoverageError < obliqueRasterScore.MeanCoverageError * .7,
            "MSDF lost its improvement under perspective projection.");
        ContactSheet(output, [reference, rasterCapture, sdf, msdf, fieldCaptures[128], depth]);

        Score rasterScore = scores.Single(item => item.Name == "raster64-pcf");
        Score msdfScore = scores.Single(item => item.Name == "msdf64");
        Require(msdfScore.MeanCoverageError < rasterScore.MeanCoverageError * .7,
            "MSDF did not improve coverage error by at least 30% over the equal-resolution raster mask.");
        Require(msdfScore.SilhouetteMismatchPixels < rasterScore.SilhouetteMismatchPixels,
            "MSDF did not improve the binary silhouette.");
        // A 3.6-unit bar is about two field texels wide. Subtexel bars are deliberately not acceptance gates.
        Require(msdfScore.ThinBarCenterCoverage[0] > .8, "The two-texel bar did not survive MSDF reconstruction.");
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Scope = "Static opaque directional caster onto a known planar Y=0 receiver; fixed sun and geometry",
            ProductionDefaultsChanged = false,
            ImageSize,
            ReferenceSupersampling = ReferenceScale,
            Encoding = "Linear RGBA8 UNORM upload; RGB signed distance; contour 0.5; median + fwidth",
            SourceGeometryHash = shape.NormalizedGeometryHash,
            field64.Artifact.FieldHash,
            field64.Artifact.FieldBounds,
            RepeatIdentical = true,
            BakeTimes = bakeTimes,
            Scores = scores,
            SubpixelCameraChecks = motion,
            ObliqueCheck = new { Msdf = obliqueScore, Raster = obliqueRasterScore },
            Metric = "Mean absolute displayed RGB shadow-contrast error normalized by local lit-minus-shadowed contrast; not linear visibility",
            NegativeOverlapSpecimen = Measure(overlapField, overlapReference, lit, dark, overlapping, camera),
            ThinBarWidthsInSourceUnits = new[] { 3.6, 1.8, .9, .45 },
            Limits = new[]
            {
                "No arbitrary mesh silhouette extraction, dynamic field updates, layered depth or nonplanar receivers",
                "Overlapping projected contours must be unioned before field generation; negative specimen retained",
                "Cannot recover a contour from detail already discarded by a raster shadow map",
                "Subtexel thin features and close edges can still disappear or change topology",
                "CPU bake timing includes both SDF and MSDF generation; GPU timestamps are medians of eight warmed frames",
                "The harness retains the existing depth-map allocation even for field modes; field-byte counts are not total VRAM savings",
            },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_SHADOW_DISTANCE_EXPERIMENT_PASSED " + plant.Facts.PhysicalDeviceName);
        Console.WriteLine(JsonSerializer.Serialize(scores, new JsonSerializerOptions { WriteIndented = true }));

        Capture Render(string name, string mode, Native3DScene scene, Matrix4x4 clip, Vector3 cameraEye,
            int scale = 1, bool shadows = false)
        {
            uint side = (uint)(ImageSize * scale);
            using var target = new VulkanNativeFrameTarget(plant, side, side);
            using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("Solid3D.v.ts"), target,
                modelProgram: programs[mode], shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"));
            renderer.Settings = Settings with { Shadows = shadows };
            Native3DFrameResult frame = renderer.Render(scene, clip, cameraEye, new(.08f, .08f, .08f, 1), capture: true);
            for (int warmup = 0; warmup < 3; warmup++)
            {
                frame = renderer.Render(scene, clip, cameraEye, new(.08f, .08f, .08f, 1), capture: true);
            }
            var samples = new List<Native3DGpuPassTime>();
            for (int sample = 0; sample < 8; sample++)
            {
                frame = renderer.Render(scene, clip, cameraEye, new(.08f, .08f, .08f, 1), capture: true);
                samples.AddRange(frame.GpuPassTimes);
            }
            var medianTimings = samples.GroupBy(item => item.Pass).Select(group =>
            {
                double[] ordered = group.Select(item => item.Milliseconds).Order().ToArray();
                return new Native3DGpuPassTime(group.Key, (ordered[3] + ordered[4]) / 2);
            }).ToArray();
            byte[] pixels = scale == 1 ? frame.Pixels! : Downsample(frame.Pixels!, scale);
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), ImageSize, ImageSize, pixels);
            return new(name, pixels, Convert.ToHexString(SHA256.HashData(pixels)), medianTimings);
        }
    }

    private static CompiledGraphicsProgram Compile(string mode, string output)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets");
        var sources = GpuSourceLoader.Load("ShadowExperimentModel.v.ts", name =>
        {
            string actual = name == "ShadowReconstruction.v.ts" ? "Shadow" + mode + ".v.ts" : name;
            string path = Path.Combine(directory, actual);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        });
        var module = GpuGraphicsBinder.Compile(new(sources));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated,
            backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput + backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        File.WriteAllText(Path.Combine(output, mode.ToLowerInvariant() + ".hlsl"), backend.Hlsl);
        File.WriteAllText(Path.Combine(output, mode.ToLowerInvariant() + ".vdmir.json"), VdMirJson.Serialize(module));
        return CompiledGraphicsProgramExporter.Export(module, backend);
    }

    private static FieldPair Generate(VectorShape shape, string source, int size, string output)
    {
        var watch = Stopwatch.StartNew();
        VectorIconMsdfArtifact artifact = VectorIconMsdfCompiler.Compile(shape, source, "projected-caster.svg",
            new VectorIconCompilationSettings(size, pixelRange: 4, minimumShortAxis: size));
        Shape native = new();
        native.SetYAxisOrientation(YAxisOrientation.Upward);
        foreach (VectorContour contour in shape.Contours)
        {
            Contour converted = new();
            foreach (VectorLine line in contour.Segments.Cast<VectorLine>())
            {
                converted.AddEdge(new LinearSegment(new(line.P0.X, line.P0.Y), new(line.P1.X, line.P1.Y), EdgeColor.WHITE));
            }
            native.AddContour(converted);
        }
        native.Normalize();
        native.OrientContours();
        Bitmap<float> sdf = new(size, size, 1);
        Projection projection = new(new(artifact.ProjectionScale, artifact.ProjectionScale),
            new(-artifact.FieldBounds.MinX, -artifact.FieldBounds.MinY));
        MsdfGenerator.GenerateSDF(sdf, native, projection, new Msdfgen.Range(4), new GeneratorConfig(false));
        watch.Stop();
        ModelTexture multi = Encode("msdf" + size, artifact.FieldPixels.Span, 3, size);
        ModelTexture single = Encode("sdf" + size, sdf.Pixels, 1, size);
        NativeGameGraphics.WritePng(Path.Combine(output, "msdf" + size + "-field.png"), size, size, multi.Rgba.ToArray());
        return new(artifact, multi, single, watch.Elapsed.TotalMilliseconds);
    }

    private static ModelTexture Encode(string identity, ReadOnlySpan<float> pixels, int channels, int size)
    {
        byte[] bytes = new byte[size * size * 4];
        for (int index = 0; index < size * size; index++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                float value = pixels[index * channels + (channels == 1 ? 0 : channel)];
                Require(float.IsFinite(value), "Non-finite field sample.");
                bytes[index * 4 + channel] = (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
            }
            bytes[index * 4 + 3] = 255;
        }
        return new(identity, size, size, bytes.ToImmutableArray());
    }

    private static ModelTexture RasterMask(List<Point[]> polygons, VectorBounds bounds, int size)
    {
        byte[] bytes = new byte[size * size * 4];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                Point point = new(bounds.MinX + (column + .5) * bounds.Width / size,
                    bounds.MinY + (row + .5) * bounds.Height / size);
                byte mask = polygons.Any(polygon => Contains(polygon, point)) ? (byte)255 : (byte)0;
                int index = (row * size + column) * 4;
                bytes[index] = mask;
                bytes[index + 1] = mask;
                bytes[index + 2] = mask;
                bytes[index + 3] = 255;
            }
        }
        return new("raster64", size, size, bytes.ToImmutableArray());
    }

    private static Native3DScene Floor(VectorBounds bounds, ModelTexture texture, ModelSampler sampler, VectorBounds? fieldBounds = null)
    {
        Point[] corners = [new(bounds.MinX, bounds.MinY), new(bounds.MaxX, bounds.MinY),
            new(bounds.MaxX, bounds.MaxY), new(bounds.MinX, bounds.MaxY)];
        return new([], [new(Triangles(corners, fieldBounds ?? bounds, 0), Material(texture, sampler))]);
    }

    private static ModelMaterial Material(ModelTexture texture, ModelSampler sampler) => new("shadow-receiver")
    {
        BaseColor = new(.55f, .6f, .65f, 1),
        Metallic = 0,
        Roughness = 1,
        DoubleSided = true,
        OcclusionTexture = new(texture, sampler),
    };

    private static Native3DScene GeometricReference(List<Point[]> polygons, VectorBounds bounds)
    {
        var floor = Floor(bounds, White, LinearClamp);
        var shadow = new NativeModel3DBatch(polygons.SelectMany(points => Triangles(points, bounds, .0001f)).ToArray(),
            Material(Black, LinearClamp));
        return floor with { Models = [.. floor.Models, shadow] };
    }

    private static NativeModel3DVertex[] Triangles(Point[] polygon, VectorBounds bounds, float height)
    {
        var vertices = new List<NativeModel3DVertex>();
        NativeModel3DVertex Vertex(Point point) => new(World(point, height), Vector3.UnitY, Vector4.One,
            new((float)((point.X - bounds.MinX) / bounds.Width), (float)((point.Y - bounds.MinY) / bounds.Height)), new(1, 0, 0, 1));
        for (int index = 1; index < polygon.Length - 1; index++)
        {
            // Counterclockwise XY contours become clockwise XZ triangles when viewed from +Y.
            vertices.AddRange([Vertex(polygon[0]), Vertex(polygon[index + 1]), Vertex(polygon[index])]);
        }
        return vertices.ToArray();
    }

    private static Native3DVertex[] Casters(List<Point[]> polygons)
    {
        Vector3 offset = new(Sun.X / Sun.Y * 4, 4, Sun.Z / Sun.Y * 4);
        var vertices = new List<Native3DVertex>();
        foreach (Point[] polygon in polygons)
        {
            for (int index = 1; index < polygon.Length - 1; index++)
            {
                foreach (Point point in new[] { polygon[0], polygon[index + 1], polygon[index] })
                {
                    vertices.Add(new(World(point, 0) + offset, Vector3.UnitY, Vector4.One));
                }
            }
        }
        return vertices.ToArray();
    }

    private static Vector3 World(Point point, float height) => new((float)(point.X - 50) * .1f, height, (float)(point.Y - 50) * .1f);

    private static Matrix4x4 TopCamera(Vector3 eye)
    {
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(12, 12, .1f, 30);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, new(eye.X, 0, eye.Z), Vector3.UnitZ) * projection;
    }

    private static Score Measure(Capture capture, Capture reference, Capture lit, Capture dark,
        List<Point[]> polygons, Matrix4x4 camera)
    {
        double error = 0;
        int mismatch = 0;
        int valid = 0;
        for (int index = 0; index < capture.Pixels.Length; index += 4)
        {
            double difference = Sum(lit.Pixels, index) - Sum(dark.Pixels, index);
            if (difference < 30)
            {
                continue;
            }
            double actual = Math.Clamp((Sum(lit.Pixels, index) - Sum(capture.Pixels, index)) / difference, 0, 1);
            double expected = Math.Clamp((Sum(lit.Pixels, index) - Sum(reference.Pixels, index)) / difference, 0, 1);
            error += Math.Abs(actual - expected);
            if ((actual > .5) != (expected > .5))
            {
                mismatch++;
            }
            valid++;
        }
        var retention = new List<double>();
        foreach (Point[] polygon in polygons.TakeLast(4))
        {
            double coverage = 0;
            for (int step = 0; step < 40; step++)
            {
                // Rectangles are stored around their perimeter. Average the two long edges at a matched t.
                double t = .1 + .8 * step / 39;
                Point start = new((polygon[0].X + polygon[3].X) / 2, (polygon[0].Y + polygon[3].Y) / 2);
                Point end = new((polygon[1].X + polygon[2].X) / 2, (polygon[1].Y + polygon[2].Y) / 2);
                var p = Vector4.Transform(new Vector4(World(new(start.X + (end.X - start.X) * t,
                    start.Y + (end.Y - start.Y) * t), 0), 1), camera);
                int x = Math.Clamp((int)((p.X / p.W * .5f + .5f) * ImageSize), 0, ImageSize - 1);
                int y = Math.Clamp((int)((p.Y / p.W * .5f + .5f) * ImageSize), 0, ImageSize - 1);
                int index = (y * ImageSize + x) * 4;
                double denominator = Sum(lit.Pixels, index) - Sum(dark.Pixels, index);
                coverage += Math.Clamp((Sum(lit.Pixels, index) - Sum(capture.Pixels, index)) / Math.Max(denominator, 1), 0, 1);
            }
            retention.Add(coverage / 40);
        }
        Require(valid > 10_000, "Receiver reference did not cover enough measurable pixels.");
        return new(capture.Name, error / valid, mismatch, retention.ToArray(), capture.Hash, capture.Timings);
    }

    private static double Sum(byte[] pixels, int index) => pixels[index] + pixels[index + 1] + pixels[index + 2];

    private static byte[] Downsample(byte[] source, int scale)
    {
        int side = ImageSize * scale;
        byte[] result = new byte[ImageSize * ImageSize * 4];
        for (int y = 0; y < ImageSize; y++)
        {
            for (int x = 0; x < ImageSize; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    int sum = 0;
                    for (int dy = 0; dy < scale; dy++)
                    {
                        for (int dx = 0; dx < scale; dx++)
                        {
                            sum += source[((y * scale + dy) * side + x * scale + dx) * 4 + channel];
                        }
                    }
                    result[(y * ImageSize + x) * 4 + channel] = (byte)((sum + scale * scale / 2) / (scale * scale));
                }
            }
        }
        return result;
    }

    private static List<Point[]> Fixture()
    {
        return
        [
            [new(8, 8), new(40, 13), new(10, 40)],
            Rectangle(48, 45, 55, 12, -.6),
            [new(10, 75), new(24, 67), new(38, 79), new(24, 94)],
            Rectangle(73, 81, 28, 3.6, .32),
            Rectangle(73, 72, 28, 1.8, .32),
            Rectangle(73, 63, 28, .9, .32),
            Rectangle(73, 54, 28, .45, .32),
        ];
    }

    private static Point[] Rectangle(double x, double y, double length, double width, double angle)
    {
        Point[] corners = [new(-length / 2, -width / 2), new(length / 2, -width / 2),
            new(length / 2, width / 2), new(-length / 2, width / 2)];
        return corners.Select(point => new Point(x + point.X * Math.Cos(angle) - point.Y * Math.Sin(angle),
            y + point.X * Math.Sin(angle) + point.Y * Math.Cos(angle))).ToArray();
    }

    private static bool Contains(Point[] polygon, Point point)
    {
        bool inside = false;
        for (int index = 0, previous = polygon.Length - 1; index < polygon.Length; previous = index++)
        {
            Point a = polygon[index];
            Point b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static string FixtureSvg(List<Point[]> polygons)
    {
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">\n");
        foreach (Point[] polygon in polygons)
        {
            svg.Append("<path d=\"M ");
            svg.Append(string.Join(" L ", polygon.Select(point => string.Create(CultureInfo.InvariantCulture, $"{point.X:R} {100 - point.Y:R}"))));
            svg.Append(" Z\"/>\n");
        }
        return svg.Append("</svg>\n").ToString();
    }

    private static ModelTexture SolidTexture(byte value) => new("solid-" + value, 1, 1,
        ImmutableArray.Create(value, value, value, (byte)255));

    private static void ContactSheet(string output, Capture[] captures)
    {
        using var bitmap = new SKBitmap(ImageSize * 3, (ImageSize + 36) * 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(20, 22, 26));
        using var paint = new SKPaint { Color = SKColors.White, TextSize = 22, IsAntialias = true };
        for (int index = 0; index < captures.Length; index++)
        {
            int x = index % 3 * ImageSize;
            int y = index / 3 * (ImageSize + 36);
            using var image = SKBitmap.Decode(Path.Combine(output, captures[index].Name + ".png"));
            canvas.DrawText(captures[index].Name, x + 12, y + 26, paint);
            canvas.DrawBitmap(image, x, y + 36);
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
