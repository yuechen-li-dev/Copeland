using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingReferenceLightingProof
{
    private static object CheckEnvironment(AurelianVulkanPlant plant, GameAssets assets, byte[] program, string output, List<MetricPair> pairs)
    {
        var material = new ReferenceMaterial3D(new(.7f, .5f, .3f), Dielectric: 0);
        var camera = new ReferenceCamera3D(new(0, .01f, 0), -Vector3.UnitY, -Vector3.UnitZ, 1, 2);
        var settings = new ReferenceCapture3D(32, 32, 1, 123, PixelJitter: false);
        EnvironmentRadiance3D constant = Source(16, 8, (_, _) => new(.3f, .5f, .8f));
        var constantLighting = new ReferenceLighting3D(Environment: constant);
        ReferenceImage3D constantImage = Capture(plant, program, settings, camera, Floor(material), constantLighting, 1024);
        ReferenceImage3D bsdfConstant = Capture(plant, program, settings with { NextEventEstimation = false }, camera, Floor(material), constantLighting, 16);
        ReferenceImage3D uniformConstant = Capture(plant, program, settings, camera, Floor(material),
            constantLighting with { ImportanceSampleEnvironment = false }, 1024);
        Vector3 constantExpected = material.BaseColor * new Vector3(.3f, .5f, .8f);
        double constantError = ConstantRmse(constantImage, constantExpected);
        double constantBsdfError = ConstantRmse(bsdfConstant, constantExpected);
        double uniformConstantError = ConstantRmse(uniformConstant, constantExpected);
        Require(constantError < .00002 && constantBsdfError < 2e-6 && constantError < uniformConstantError * .1,
            "Adaptive constant environment failed to remove unnecessary light-sampling noise.");
        float[] constantPixels = new float[settings.Width * settings.Height * 3];
        for (int pixel = 0; pixel < settings.Width * settings.Height; pixel++)
        {
            constantPixels[pixel * 3] = constantExpected.X;
            constantPixels[pixel * 3 + 1] = constantExpected.Y;
            constantPixels[pixel * 3 + 2] = constantExpected.Z;
        }
        string constantReference = Save(plant, assets, output, "constant-environment-oracle", new(settings.Width, settings.Height, 1, constantPixels));
        pairs.Add(new("constant-adaptive", constantReference, Save(plant, assets, output, "constant-adaptive", constantImage), "constant-adaptive"));
        pairs.Add(new("constant-uniform", constantReference, Save(plant, assets, output, "constant-uniform", uniformConstant), "constant-uniform"));

        EnvironmentRadiance3D source = Source(64, 32, (x, y) => x is >= 20 and < 22 && y is >= 4 and < 6
            ? new(45, 32, 18) : new(.003f, .006f, .01f));
        File.WriteAllBytes(Path.Combine(output, "environment-source.rgba32f"), MemoryMarshal.AsBytes(source.Rgba.AsSpan()).ToArray());
        EnvironmentLighting atlas = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        Require(atlas.SourceKey == source.ContentKey, "Real-time compilation and reference do not share the source identity.");
        var lighting = new ReferenceLighting3D(Environment: source);
        ReferenceImage3D low = Capture(plant, program, settings, camera, Floor(material), lighting, 64);
        ReferenceImage3D uniform = Capture(plant, program, settings, camera, Floor(material), lighting with { ImportanceSampleEnvironment = false }, 64);
        ReferenceImage3D reference = Capture(plant, program, settings with { Seed = 987 }, camera, Floor(material), lighting, 4096);
        Vector3 quadrature = EnvironmentIntegral(source) * material.BaseColor;
        double quadratureError = Vector3.Distance(Average(reference), quadrature);
        double importanceError = RenderingReferenceProof.Rmse(low, reference);
        double uniformError = RenderingReferenceProof.Rmse(uniform, reference);
        Require(quadratureError < .003, "Environment differs from independent solid-angle quadrature: " + quadratureError);
        Require(importanceError < uniformError * .5, "Environment importance sampling failed the equal-sample ablation.");
        string expectedFile = Save(plant, assets, output, "environment-reference", reference);
        string importanceFile = Save(plant, assets, output, "environment-importance-64", low);
        string uniformFile = Save(plant, assets, output, "environment-uniform-64", uniform);
        pairs.Add(new("environment-importance-64", expectedFile, importanceFile, "importance"));
        pairs.Add(new("environment-uniform-64", expectedFile, uniformFile, "uniform"));

        var missCamera = new ReferenceCamera3D(new(0, 1, 0), new(0, 2, 0), Vector3.UnitZ, 1.2f);
        ReferenceImage3D miss = Capture(plant, program, settings, missCamera, Floor(material), lighting, 1);
        double lookupError = 0;
        for (int y = 0; y < settings.Height; y++)
        {
            for (int x = 0; x < settings.Width; x++)
            {
                float scale = MathF.Tan(missCamera.VerticalFieldOfViewRadians / 2);
                Vector3 direction = Vector3.Normalize(new Vector3(((x + .5f) / settings.Width * 2 - 1) * scale,
                    1, (1 - (y + .5f) / settings.Height * 2) * scale));
                Vector3 expected = Lookup(source, direction.X, direction.Y, direction.Z);
                int pixel = (y * settings.Width + x) * 3;
                Vector3 actual = new(miss.LinearRgb[pixel], miss.LinearRgb[pixel + 1], miss.LinearRgb[pixel + 2]);
                lookupError = Math.Max(lookupError, Vector3.Distance(actual, expected));
            }
        }
        Require(lookupError < .0005, "Primary environment lookup/filter/orientation mismatch: " + lookupError);
        Save(plant, assets, output, "environment-primary-miss", miss);
        object raster = CheckRasterEnvironment(plant, assets, program, source, atlas, material.BaseColor, output, pairs);
        Console.WriteLine($"Environment: importance RMSE {importanceError:F6}, uniform {uniformError:F6}, quadrature {quadratureError:G5}, constant MIS {constantError:G5}");
        return new { Source = source.ContentKey, Atlas = atlas.ContentKey, SourceWidth = source.Width, SourceHeight = source.Height,
            ImportanceRmse = importanceError, UniformRmse = uniformError, QuadratureError = quadratureError,
            PrimaryLookupMaximumError = lookupError, ConstantEnvironmentRmse = constantError, ConstantBsdfOnlyRmse = constantBsdfError,
            ConstantUniformMisRmse = uniformConstantError,
            Raster = raster,
            Proposal = "Luminance times solid angle; 64x32 maximum CDF plus 5 percent uniform support; bilinear source lookup" };
    }

    private static object CheckRasterEnvironment(AurelianVulkanPlant plant, GameAssets assets, byte[] program,
        EnvironmentRadiance3D source, EnvironmentLighting atlas, Vector3 baseColor, string output, List<MetricPair> pairs)
    {
        const int size = 32;
        var camera = new ReferenceCamera3D(Vector3.UnitY, Vector3.Zero, -Vector3.UnitZ, 1.05f);
        var material = new ReferenceMaterial3D(baseColor, .65f);
        ReferenceImage3D reference = Capture(plant, program, new(size, size, 1, 491), camera, Floor(material),
            new(Environment: source), 4096);
        string referenceFile = Save(plant, assets, output, "environment-pbr-reference", reference);
        using var target = new VulkanNativeFrameTarget(plant, size, size);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true);
        renderer.Settings = Graphics3DSettings.Default with { AntiAliasing = AntiAliasing3D.None, Shadows = false,
            BloomIntensity = 0, AmbientOcclusionStrength = 0, SunIntensity = 0, SkyAmbient = Vector3.Zero, GroundAmbient = Vector3.Zero,
            DirectDiffuseModel = DirectDiffuseModel3D.Lambert, SolidRoughness = .65f, SolidMetallic = 0,
            Exposure = 1, ToneMapColorRetention = 0, ToneMapGamutCompression = false };
        Native3DVertex Vertex(Vector3 point) => new(point, Vector3.UnitY, new(baseColor, 1));
        var vertices = Floor(material).SelectMany(triangle => new[] { Vertex(triangle.A), Vertex(triangle.B), Vertex(triangle.C) }).ToArray();
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, 1, .1f, 100);
        projection.M22 *= -1;
        Matrix4x4 matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        renderer.Environment = atlas;
        byte[] image = renderer.Render(new Native3DScene(vertices, []), matrix, camera.Eye, new(0, 0, 0, 1), capture: true).Pixels!;
        renderer.Environment = null;
        byte[] off = renderer.Render(new Native3DScene(vertices, []), matrix, camera.Eye, new(0, 0, 0, 1), capture: true).Pixels!;
        NativeGameGraphics.WritePng(Path.Combine(output, "environment-runtime.png"), size, size, image);
        NativeGameGraphics.WritePng(Path.Combine(output, "environment-runtime-off.png"), size, size, off);
        pairs.Add(new("compiled-environment", referenceFile, "environment-runtime.png", "compiled-environment"));
        pairs.Add(new("environment-off", referenceFile, "environment-runtime-off.png", "environment-off"));
        Require(image.Where((_, index) => index % 4 < 3).Sum(value => (long)value) > 10000,
            "Compiled environment did not reach the ordinary raster lighting path.");
        return new { SourceKey = source.ContentKey, AtlasKey = atlas.ContentKey,
            ReferenceSamples = reference.Samples, Material = material,
            Scope = "Real-time prefiltered diffuse and split-sum specular measured against direct environment integration; approximation error is retained." };
    }

    private static EnvironmentRadiance3D Source(int width, int height, Func<int, int, Vector3> color)
    {
        float[] pixels = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 value = color(x, y);
                int offset = (y * width + x) * 4;
                pixels[offset] = value.X;
                pixels[offset + 1] = value.Y;
                pixels[offset + 2] = value.Z;
                pixels[offset + 3] = 1;
            }
        }
        return new(width, height, pixels);
    }

    private static Vector3 EnvironmentIntegral(EnvironmentRadiance3D source)
    {
        const int rows = 512;
        const int columns = 1024;
        double r = 0;
        double g = 0;
        double b = 0;
        for (int y = 0; y < rows / 2; y++)
        {
            double theta = Math.PI * (y + .5) / rows;
            double cosine = Math.Cos(theta);
            double sine = Math.Sin(theta);
            double weight = Math.Tau / columns * (Math.Cos(Math.PI * y / rows) - Math.Cos(Math.PI * (y + 1) / rows)) * cosine / Math.PI;
            for (int x = 0; x < columns; x++)
            {
                double phi = ((x + .5) / columns - .5) * Math.Tau;
                Vector3 radiance = Lookup(source, Math.Cos(phi) * sine, cosine, Math.Sin(phi) * sine);
                r += radiance.X * weight;
                g += radiance.Y * weight;
                b += radiance.Z * weight;
            }
        }
        return new((float)r, (float)g, (float)b);
    }

    private static Vector3 Lookup(EnvironmentRadiance3D source, double dx, double dy, double dz)
    {
        double x = Math.Clamp((Math.Atan2(dz, dx) / Math.Tau + .5) * source.Width - .5, 0, source.Width - 1);
        double y = Math.Clamp(Math.Acos(Math.Clamp(dy, -1, 1)) / Math.PI * source.Height - .5, 0, source.Height - 1);
        int left = (int)x;
        int top = (int)y;
        Vector3 Read(int column, int row)
        {
            int offset = (row * source.Width + column) * 4;
            return new(source.Rgba[offset], source.Rgba[offset + 1], source.Rgba[offset + 2]);
        }
        Vector3 first = Vector3.Lerp(Read(left, top), Read(Math.Min(left + 1, source.Width - 1), top), (float)(x - left));
        Vector3 second = Vector3.Lerp(Read(left, Math.Min(top + 1, source.Height - 1)),
            Read(Math.Min(left + 1, source.Width - 1), Math.Min(top + 1, source.Height - 1)), (float)(x - left));
        return Vector3.Lerp(first, second, (float)(y - top));
    }

    private static Vector3 Average(ReferenceImage3D image)
    {
        Vector3 total = Vector3.Zero;
        for (int pixel = 0; pixel < image.LinearRgb.Length / 3; pixel++)
            total += new Vector3(image.LinearRgb[pixel * 3], image.LinearRgb[pixel * 3 + 1], image.LinearRgb[pixel * 3 + 2]);
        return total / (image.Width * image.Height);
    }

    private static double ConstantRmse(ReferenceImage3D image, Vector3 expected)
    {
        double sum = 0;
        for (int pixel = 0; pixel < image.LinearRgb.Length / 3; pixel++)
        {
            Vector3 value = new(image.LinearRgb[pixel * 3], image.LinearRgb[pixel * 3 + 1], image.LinearRgb[pixel * 3 + 2]);
            sum += Vector3.DistanceSquared(value, expected);
        }
        return Math.Sqrt(sum / image.LinearRgb.Length);
    }
}
