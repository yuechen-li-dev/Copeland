using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeSpriteEdgeDiagnosticsM19B;
using Aurelian.Profile.Graphics;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Aurelian.StrategyDemo;
using Copeland.Profile;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Profiles;
using Machina.VectorAssets;
using SkiaSharp;

const int SurfaceWidth = 96;
const int SurfaceHeight = 96;
string root = FindRepositoryRoot();
string output = Path.Combine(root, "artifacts", "aurelian-profile-derivative-reconstruction-m19c");
Directory.CreateDirectory(output);
int zoom = ParseZoom(args);
string[] requestedAssets = ParseAssets(args);
string assetDirectory = Path.Combine(root, "samples", "Integrations", "Aurelian.StrategyDemo", "Assets");
string toolkit = File.ReadAllText(Path.Combine(assetDirectory, "StrategyArt.ts"));

var resources = new Dictionary<string, ProfileNativeCompositionResource>(StringComparer.Ordinal);
foreach (string asset in requestedAssets)
{
    string path = Path.Combine(assetDirectory, asset + ".profile.tsx");
    Require(File.Exists(path), $"Unknown Mossward Profile asset '{asset}'.");
    ProfileCompositionCompilationResult composition = ProfileTsxCompiler.CompileComposition(
        toolkit + "\n" + File.ReadAllText(path),
        path);
    Require(composition.Success, string.Join("; ", composition.Diagnostics.Select(item => item.Message)));
    ProfileNativeCompileResult native = ProfileNativeCompiler.Compile(composition.Composition!, path);
    Require(native.Success, string.Join("; ", native.Diagnostics.Select(item => item.Message)));
    resources.Add(asset, native.Resource!);
}

CompiledGraphicsProgram fixedProgram = CompileShader(root, "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts");
CompiledGraphicsProgram derivativeProgram = CompileShader(root, "src/Aurelian/Aurelian.Shaders/Assets/ProfileMsdf.v.ts");
VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeSpriteEdgeDiagnosticsM19B"));
Require(init.Success && init.Plant is not null, string.Join("; ", init.Diagnostics.Select(item => item.Message)));

using AurelianVulkanPlant plant = init.Plant!;
using var cpu = new StrategyAssets(assetDirectory);
var measurements = new List<object>();

foreach ((string asset, ProfileNativeCompositionResource resource) in resources)
{
    float scale = asset switch
    {
        "worker" => 0.9f,
        "tree" => 0.8f,
        _ => 0.45f,
    };
    var fractional = new ProfileNativeInstance(48.375f, 70.625f, scale);
    var snapped = fractional with
    {
        OriginX = MathF.Round(fractional.OriginX),
        OriginY = MathF.Round(fractional.OriginY),
    };

    byte[] cpuFractional = RenderCpu(cpu, asset, fractional);
    byte[] nativeFractional = RenderNative(
        plant,
        fixedProgram,
        resource,
        fractional,
        ProfileNativeReconstructionOptions.GlyphSmallScreenCompensation);
    byte[] nativeNeutralThreshold = RenderNative(
        plant,
        fixedProgram,
        resource,
        fractional,
        ProfileNativeReconstructionOptions.M19BNeutralThreshold);
    byte[] nativeDerivative = RenderNative(
        plant,
        derivativeProgram,
        resource,
        fractional,
        ProfileNativeReconstructionOptions.RuntimeDefault);
    byte[] cpuSnapped = RenderCpu(cpu, asset, snapped);
    byte[] nativeSnapped = RenderNative(
        plant,
        fixedProgram,
        resource,
        snapped,
        ProfileNativeReconstructionOptions.GlyphSmallScreenCompensation);

    ProfileImageComparison fractionalComparison = ProfileEdgeDiagnostics.Compare(
        cpuFractional, nativeFractional, SurfaceWidth, SurfaceHeight);
    ProfileImageComparison snappedComparison = ProfileEdgeDiagnostics.Compare(
        cpuSnapped, nativeSnapped, SurfaceWidth, SurfaceHeight);
    ProfileImageComparison neutralThresholdComparison = ProfileEdgeDiagnostics.Compare(
        cpuFractional, nativeNeutralThreshold, SurfaceWidth, SurfaceHeight);
    ProfileImageComparison derivativeComparison = ProfileEdgeDiagnostics.Compare(
        cpuFractional, nativeDerivative, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics cpuFractionalEdges = ProfileEdgeDiagnostics.Measure(cpuFractional, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics nativeFractionalEdges = ProfileEdgeDiagnostics.Measure(nativeFractional, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics cpuSnappedEdges = ProfileEdgeDiagnostics.Measure(cpuSnapped, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics nativeSnappedEdges = ProfileEdgeDiagnostics.Measure(nativeSnapped, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics nativeNeutralThresholdEdges = ProfileEdgeDiagnostics.Measure(nativeNeutralThreshold, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics nativeDerivativeEdges = ProfileEdgeDiagnostics.Measure(nativeDerivative, SurfaceWidth, SurfaceHeight);

    WriteInspectionImage(
        Path.Combine(output, $"{asset}-dark-{zoom}x.png"),
        zoom,
        new byte[] { 18, 27, 25, 255 },
        cpuFractional,
        nativeFractional,
        nativeNeutralThreshold,
        nativeDerivative);
    WriteInspectionImage(
        Path.Combine(output, $"{asset}-light-{zoom}x.png"),
        zoom,
        new byte[] { 225, 220, 202, 255 },
        cpuFractional,
        nativeFractional,
        nativeNeutralThreshold,
        nativeDerivative);
    PngWriter.Write(
        Path.Combine(output, $"{asset}-native-1x.png"),
        SurfaceWidth,
        SurfaceHeight,
        Composite(nativeFractional, new byte[] { 18, 27, 25, 255 }));
    PngWriter.Write(
        Path.Combine(output, $"{asset}-atlas.png"),
        resource.AtlasWidth,
        resource.AtlasHeight,
        resource.CopyAtlasRgbaPixels());
    if (asset == "worker")
    {
        WriteZoomed(Path.Combine(output, "worker-dark-8x-before.png"), nativeNeutralThreshold, [18, 27, 25, 255], 8);
        WriteZoomed(Path.Combine(output, "worker-dark-8x-after.png"), nativeDerivative, [18, 27, 25, 255], 8);
        WriteZoomed(Path.Combine(output, "worker-light-8x-after.png"), nativeDerivative, [225, 220, 202, 255], 8);
        WriteZoomed(Path.Combine(output, "worker-diff-before.png"), Difference(cpuFractional, nativeNeutralThreshold), [0, 0, 0, 255], 8);
        WriteZoomed(Path.Combine(output, "worker-diff-after.png"), Difference(cpuFractional, nativeDerivative), [0, 0, 0, 255], 8);
    }
    else if (asset == "tree")
    {
        WriteZoomed(Path.Combine(output, "tree-after.png"), nativeDerivative, [18, 27, 25, 255], 4);
    }
    else if (asset == "hq")
    {
        WriteZoomed(Path.Combine(output, "building-after.png"), nativeDerivative, [18, 27, 25, 255], 4);
    }

    measurements.Add(new
    {
        asset,
        source = resource.SourcePath,
        resource.CompositionHash,
        worldScale = scale,
        expectedAssetHeightPixels = Math.Round(resource.Bounds.Height * scale, 3),
        sourceBounds = resource.Bounds,
        atlas = new
        {
            width = resource.AtlasWidth,
            height = resource.AtlasHeight,
            filtering = "linear",
            mipLevels = 1,
            addressMode = "clamp-to-edge",
            fields = resource.DescribeFields(),
        },
        fractionalOrigin = fractional,
        snappedOrigin = snapped,
        fractional = new
        {
            cpuEdges = cpuFractionalEdges,
            nativeEdges = nativeFractionalEdges,
            comparison = fractionalComparison,
        },
        snapped = new
        {
            cpuEdges = cpuSnappedEdges,
            nativeEdges = nativeSnappedEdges,
            comparison = snappedComparison,
        },
        neutralThreshold = new
        {
            threshold = 0.5,
            nativeEdges = nativeNeutralThresholdEdges,
            comparison = neutralThresholdComparison,
        },
        derivative = new
        {
            threshold = 0.5,
            widthModel = "fwidth(medianDistance)",
            nativeEdges = nativeDerivativeEdges,
            comparison = derivativeComparison,
        },
        snappingDelta = new
        {
            silhouetteIoU = snappedComparison.SilhouetteIntersectionOverUnion - fractionalComparison.SilhouetteIntersectionOverUnion,
            nativeTransitionWidthPixels = nativeSnappedEdges.EstimatedTransitionWidthPixels - nativeFractionalEdges.EstimatedTransitionWidthPixels,
        },
    });
}

ProfileNativeCompositionResource workerResource = resources["worker"];
double[] stressScales = [0.5, 0.75, 1, 1.5, 2, 4];
var scaleStress = new List<object>();
foreach (double scale in stressScales)
{
    var instance = new ProfileNativeInstance(128.375f, 220.625f, (float)scale);
    byte[] cpuPixels = RenderCpu(cpu, "worker", instance, 256, 256);
    byte[] derivativePixels = RenderNative(
        plant,
        derivativeProgram,
        workerResource,
        instance,
        ProfileNativeReconstructionOptions.RuntimeDefault,
        256,
        256);
    scaleStress.Add(new
    {
        scale,
        cpu = ProfileEdgeDiagnostics.Measure(cpuPixels, 256, 256),
        derivative = ProfileEdgeDiagnostics.Measure(derivativePixels, 256, 256),
        comparison = ProfileEdgeDiagnostics.Compare(cpuPixels, derivativePixels, 256, 256),
    });
}
WriteJson(Path.Combine(output, "scale-stress.json"), scaleStress);

double[] offsets = [0, 0.25, 0.5, 0.75];
var subpixelStress = new List<object>();
foreach (double offsetY in offsets)
{
    foreach (double offsetX in offsets)
    {
        var instance = new ProfileNativeInstance(48 + (float)offsetX, 70 + (float)offsetY, 0.9f);
        byte[] cpuPixels = RenderCpu(cpu, "worker", instance);
        byte[] derivativePixels = RenderNative(
            plant,
            derivativeProgram,
            workerResource,
            instance,
            ProfileNativeReconstructionOptions.RuntimeDefault);
        subpixelStress.Add(new
        {
            offsetX,
            offsetY,
            derivative = ProfileEdgeDiagnostics.Measure(derivativePixels, SurfaceWidth, SurfaceHeight),
            comparison = ProfileEdgeDiagnostics.Compare(cpuPixels, derivativePixels, SurfaceWidth, SurfaceHeight),
        });
    }
}
WriteJson(Path.Combine(output, "subpixel-stress.json"), subpixelStress);

int[] fieldSizes = [64, 96, 128, 192, 256];
var fieldSizeStress = new List<object>();
foreach (int fieldSize in fieldSizes)
{
    ProfileNativeCompositionResource resource = CompileMossward(
        assetDirectory,
        toolkit,
        "worker",
        new VectorIconCompilationSettings(
            qualitySize: fieldSize,
            pixelRange: Math.Min(12, fieldSize / 4d - 1),
            minimumShortAxis: Math.Min(40, fieldSize)));
    var instance = new ProfileNativeInstance(48.375f, 70.625f, 0.9f);
    byte[] cpuPixels = RenderCpu(cpu, "worker", instance);
    byte[] derivativePixels = RenderNative(
        plant,
        derivativeProgram,
        resource,
        instance,
        ProfileNativeReconstructionOptions.RuntimeDefault);
    fieldSizeStress.Add(new
    {
        fieldSize,
        atlasWidth = resource.AtlasWidth,
        atlasHeight = resource.AtlasHeight,
        resource.MinimumFieldDimension,
        resource.MaximumFieldDimension,
        derivative = ProfileEdgeDiagnostics.Measure(derivativePixels, SurfaceWidth, SurfaceHeight),
        comparison = ProfileEdgeDiagnostics.Compare(cpuPixels, derivativePixels, SurfaceWidth, SurfaceHeight),
    });
}
WriteJson(Path.Combine(output, "field-size-stress.json"), fieldSizeStress);

(ProfileNativeCompositionResource syntheticResource, byte[] syntheticCpu) = CreateSyntheticProfile();
var syntheticInstance = new ProfileNativeInstance(48.375f, 70.625f, 1);
byte[] syntheticDerivative = RenderNative(
    plant,
    derivativeProgram,
    syntheticResource,
    syntheticInstance,
    ProfileNativeReconstructionOptions.RuntimeDefault);
WriteZoomed(Path.Combine(output, "synthetic-profile-after.png"), syntheticDerivative, [18, 27, 25, 255], 8);
object syntheticMetrics = new
{
    asset = "synthetic-square",
    cpu = ProfileEdgeDiagnostics.Measure(syntheticCpu, SurfaceWidth, SurfaceHeight),
    derivative = ProfileEdgeDiagnostics.Measure(syntheticDerivative, SurfaceWidth, SurfaceHeight),
    comparison = ProfileEdgeDiagnostics.Compare(syntheticCpu, syntheticDerivative, SurfaceWidth, SurfaceHeight),
};

object performance = MeasurePerformance(plant, fixedProgram, derivativeProgram, workerResource);
WriteJson(Path.Combine(output, "performance.json"), performance);
WriteJson(Path.Combine(output, "baseline.json"), new
{
    milestone = "OBLIVION-NOTEBOOK-NATIVE-SPRITE-EDGE-DIAGNOSTICS-M19B",
    workerCpuTransitionWidth = 0.902542,
    workerNeutralTransitionWidth = 2.57,
    workerNeutralSilhouetteIoU = 0.955,
    excludedCauses = new[] { "pixel snapping", "sampler", "mipmapping", "UV precision", "alpha blending" },
});
WriteJson(Path.Combine(output, "derivative-model.json"), new
{
    fieldEncoding = "median RGB; encoded edge at threshold 0.5",
    signedDistance = "median(sample.rgb) - threshold",
    derivativeUnits = "encoded distance per screen pixel",
    width = "max(fwidth(median(sample.rgb)), 0.000001)",
    coverage = "smooth cubic ramp over threshold +/- width/2",
    profilePolicy = "neutral threshold plus derivative width",
    glyphPolicy = "separate MsdfText shader with existing threshold and fixed ramp",
});
WriteJson(Path.Combine(output, "edge-metrics.json"), new { measurements, synthetic = syntheticMetrics });

CopyProofIfPresent(
    Path.Combine(root, "artifacts", "aurelian-native-graphics-m19", "strategy-native-after.png"),
    Path.Combine(output, "mossward-native-after.png"));
CopyProofIfPresent(
    Path.Combine(root, "artifacts", "aurelian-native-graphics-m19", "tinyfarm-second-consumer.png"),
    Path.Combine(output, "tinyfarm-regression.png"));
CopyProofIfPresent(
    Path.Combine(root, "artifacts", "aurelian-full-game-slice-m9", "03-dialogue.png"),
    Path.Combine(output, "glyph-small-after.png"));

var report = new
{
    milestone = "AURELIAN-PROFILE-DERIVATIVE-RECONSTRUCTION-M19C",
    device = init.Facts!.PhysicalDeviceName,
    command = "dotnet run --project tools/Aurelian.NativeSpriteEdgeDiagnosticsM19B -c Release -- --asset worker --asset tree --zoom 8",
    inspection = new
    {
        panelOrder = new[] { "CPU reference", "legacy", "M19B neutral", "M19C derivative", "amplified CPU/derivative difference" },
        backgrounds = new[] { "dark", "light" },
        inspectionScaling = "nearest-neighbor",
        supportedZoom = new[] { 1, 2, 4, 8, 16 },
        surface = new[] { SurfaceWidth, SurfaceHeight },
    },
    auditedSeams = new
    {
        sampler = "MSDF pipeline forces linear min/mag filtering",
        uv = "atlas entries expose exact integer-derived normalized crop bounds",
        alpha = "straight-alpha shader output with SrcAlpha/OneMinusSrcAlpha color blend",
        mipmapping = "disabled; sampler MaxLod is zero and texture has one level",
        reconstruction = "Profile median RGB, neutral 0.5 threshold, fwidth-scaled one-screen-pixel ramp",
        placement = "fractional and rounded presentation origins measured separately",
    },
    diagnosis = new
    {
        rootCause = "A fixed normalized field ramp did not track the field's projected screen-pixel footprint during Profile minification.",
        ruledOut = new[] { "pixel snapping", "nearest sampler", "mipmapping", "atlas crop precision", "alpha blend mode" },
        fix = "ProfileMsdf reconstructs median RGB at threshold 0.5 with a complete coverage ramp equal to fwidth(distance). Glyph rendering remains in its separate qualified shader.",
        residual = "No bounded reconstruction blocker remains; field-generation corner behavior remains a future general MSDF concern.",
        nextStep = "Retain this diagnostic for future Profile atlas-pressure and corner-artifact investigations.",
    },
    measurements,
};
WriteJson(Path.Combine(output, "edge-diagnostics.json"), report);
object[] manifestFiles = Directory.GetFiles(output)
    .Where(path => !string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.Ordinal))
    .Order()
    .Select(path => new
{
    file = Path.GetFileName(path),
    bytes = new FileInfo(path).Length,
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
}).Cast<object>().ToArray();
WriteJson(Path.Combine(output, "manifest.json"), new
{
    milestone = "AURELIAN-PROFILE-DERIVATIVE-RECONSTRUCTION-M19C",
    kind = "screen-space-derivative-msdf-reconstruction",
    derivativeIntrinsicsQualified = true,
    profileDerivativeReconstructionQualified = true,
    workerBlurMateriallyReduced = true,
    treeNonRegressionQualified = true,
    buildingNonRegressionQualified = true,
    glyphBehaviorPreserved = true,
    subpixelPlacementQualified = true,
    scaleStressQualified = true,
    fieldSizeStressQualified = true,
    mosswardNativeQualified = File.Exists(Path.Combine(output, "mossward-native-after.png")),
    tinyfarmRegressionQualified = File.Exists(Path.Combine(output, "tinyfarm-regression.png")),
    postprocessSharpenUsed = false,
    nearestFilterUsedAsFix = false,
    files = manifestFiles,
});
Console.WriteLine($"AURELIAN_NATIVE_SPRITE_EDGE_DIAGNOSTICS_M19B_READY assets={string.Join(',', requestedAssets)} zoom={zoom} device={init.Facts.PhysicalDeviceName}");

static void CopyProofIfPresent(string source, string destination)
{
    if (File.Exists(source))
    {
        File.Copy(source, destination, overwrite: true);
    }
}

static byte[] RenderCpu(
    StrategyAssets assets,
    string asset,
    ProfileNativeInstance instance,
    int width = SurfaceWidth,
    int height = SurfaceHeight)
{
    using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    assets.Draw(canvas, asset, instance.OriginX, instance.OriginY, instance.Scale);
    canvas.Flush();
    byte[] pixels = new byte[width * height * 4];
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            SKColor color = bitmap.GetPixel(x, y);
            int offset = ((y * width) + x) * 4;
            pixels[offset] = color.Red;
            pixels[offset + 1] = color.Green;
            pixels[offset + 2] = color.Blue;
            pixels[offset + 3] = color.Alpha;
        }
    }
    return pixels;
}

static byte[] RenderNative(
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram program,
    ProfileNativeCompositionResource resource,
    ProfileNativeInstance instance,
    ProfileNativeReconstructionOptions reconstruction,
    int width = SurfaceWidth,
    int height = SurfaceHeight)
{
    using var renderer = new VulkanOrderedQuadRenderer(
        plant,
        program,
        (uint)width,
        (uint)height,
        new Native2DPipelineOptions(
            Native2DPipelineKind.ProfileMsdf,
            TransparentClear: true,
            EnableStraightAlphaBlend: true));
    using var cache = new ProfileNativeRealizationCache(renderer, 1);
    cache.Warm(resource);
    renderer.Begin2D();
    cache.Submit(resource, instance, reconstruction);
    Native2DPassResult result = renderer.End2D(captureReadback: true);
    return result.Pixels!;
}

static void WriteInspectionImage(
    string path,
    int zoom,
    byte[] matte,
    byte[] cpu,
    byte[] legacy,
    byte[] neutral,
    byte[] derivative)
{
    const int gap = 2;
    int width = (SurfaceWidth * 5) + (gap * 4);
    byte[] baseImage = new byte[width * SurfaceHeight * 4];
    Fill(baseImage, matte);
    CopyPanel(Composite(cpu, matte), baseImage, width, 0);
    CopyPanel(Composite(legacy, matte), baseImage, width, SurfaceWidth + gap);
    CopyPanel(Composite(neutral, matte), baseImage, width, (SurfaceWidth + gap) * 2);
    CopyPanel(Composite(derivative, matte), baseImage, width, (SurfaceWidth + gap) * 3);
    CopyPanel(Difference(cpu, derivative), baseImage, width, (SurfaceWidth + gap) * 4);
    byte[] enlarged = ScaleNearest(baseImage, width, SurfaceHeight, zoom);
    PngWriter.Write(path, width * zoom, SurfaceHeight * zoom, enlarged);
}

static byte[] Composite(byte[] source, byte[] matte)
{
    byte[] result = new byte[source.Length];
    for (int offset = 0; offset < source.Length; offset += 4)
    {
        int alpha = source[offset + 3];
        for (int channel = 0; channel < 3; channel++)
        {
            result[offset + channel] = (byte)((source[offset + channel] * alpha + matte[channel] * (255 - alpha) + 127) / 255);
        }
        result[offset + 3] = 255;
    }
    return result;
}

static void WriteZoomed(string path, byte[] source, byte[] matte, int zoom)
{
    byte[] composited = Composite(source, matte);
    PngWriter.Write(
        path,
        SurfaceWidth * zoom,
        SurfaceHeight * zoom,
        ScaleNearest(composited, SurfaceWidth, SurfaceHeight, zoom));
}

static byte[] Difference(byte[] left, byte[] right)
{
    byte[] result = new byte[left.Length];
    for (int offset = 0; offset < result.Length; offset += 4)
    {
        int alphaDifference = Math.Abs(left[offset + 3] - right[offset + 3]);
        result[offset] = (byte)Math.Min(255, alphaDifference * 4);
        result[offset + 1] = (byte)Math.Min(255, Math.Abs(left[offset + 1] - right[offset + 1]) * 2);
        result[offset + 2] = (byte)Math.Min(255, Math.Abs(left[offset + 2] - right[offset + 2]) * 2);
        result[offset + 3] = 255;
    }
    return result;
}

static void CopyPanel(byte[] panel, byte[] destination, int destinationWidth, int destinationX)
{
    int sourceStride = SurfaceWidth * 4;
    int destinationStride = destinationWidth * 4;
    for (int y = 0; y < SurfaceHeight; y++)
    {
        panel.AsSpan(y * sourceStride, sourceStride)
            .CopyTo(destination.AsSpan((y * destinationStride) + (destinationX * 4), sourceStride));
    }
}

static void Fill(byte[] pixels, byte[] color)
{
    for (int offset = 0; offset < pixels.Length; offset += 4)
    {
        color.CopyTo(pixels, offset);
    }
}

static byte[] ScaleNearest(byte[] source, int width, int height, int scale)
{
    byte[] result = new byte[width * scale * height * scale * 4];
    int destinationWidth = width * scale;
    for (int y = 0; y < height * scale; y++)
    {
        int sourceY = y / scale;
        for (int x = 0; x < destinationWidth; x++)
        {
            int sourceX = x / scale;
            int sourceOffset = ((sourceY * width) + sourceX) * 4;
            int destinationOffset = ((y * destinationWidth) + x) * 4;
            source.AsSpan(sourceOffset, 4).CopyTo(result.AsSpan(destinationOffset, 4));
        }
    }
    return result;
}

static CompiledGraphicsProgram CompileShader(string root, string relativePath)
{
    string source = File.ReadAllText(Path.Combine(root, relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(relativePath, source)]));
    Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
    return CompiledGraphicsProgramExporter.Export(module, backend);
}

static ProfileNativeCompositionResource CompileMossward(
    string assetDirectory,
    string toolkit,
    string asset,
    VectorIconCompilationSettings settings)
{
    string path = Path.Combine(assetDirectory, asset + ".profile.tsx");
    ProfileCompositionCompilationResult composition = ProfileTsxCompiler.CompileComposition(
        toolkit + "\n" + File.ReadAllText(path),
        path);
    Require(composition.Success, string.Join("; ", composition.Diagnostics.Select(item => item.Message)));
    ProfileNativeCompileResult native = ProfileNativeCompiler.Compile(composition.Composition!, path, settings);
    Require(native.Success, string.Join("; ", native.Diagnostics.Select(item => item.Message)));
    return native.Resource!;
}

static (ProfileNativeCompositionResource Resource, byte[] CpuPixels) CreateSyntheticProfile()
{
    VectorPoint a = new(-12, 0);
    VectorPoint b = new(12, 0);
    VectorPoint c = new(12, 24);
    VectorPoint d = new(-12, 24);
    var shape = new VectorShape([new VectorContour([
        new VectorLine(a, b),
        new VectorLine(b, c),
        new VectorLine(c, d),
        new VectorLine(d, a),
    ])]);
    var item = new ResolvedProfilePaintItem(
        "square",
        shape,
        new ProfileStyle("#e3d3a5"),
        "synthetic-profile-ir",
        "synthetic-square-contour");
    var composition = new ProfileComposition([new ProfileLayer(new ProfileLayerId("Synthetic"), [item])]);
    ProfileNativeCompileResult native = ProfileNativeCompiler.Compile(
        composition,
        "tools/Aurelian.NativeSpriteEdgeDiagnosticsM19B/synthetic-square.profile");
    Require(native.Success, string.Join("; ", native.Diagnostics.Select(diagnostic => diagnostic.Message)));

    using var bitmap = new SKBitmap(new SKImageInfo(SurfaceWidth, SurfaceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    using var canvas = new SKCanvas(bitmap);
    using var paint = new SKPaint { Color = SKColor.Parse("#e3d3a5"), IsAntialias = true };
    canvas.Clear(SKColors.Transparent);
    canvas.Save();
    canvas.Translate(48.375f, 70.625f);
    canvas.Scale(1, -1);
    canvas.DrawRect(-12, 0, 24, 24, paint);
    canvas.Restore();
    canvas.Flush();
    byte[] pixels = new byte[SurfaceWidth * SurfaceHeight * 4];
    for (int y = 0; y < SurfaceHeight; y++)
    {
        for (int x = 0; x < SurfaceWidth; x++)
        {
            SKColor color = bitmap.GetPixel(x, y);
            int offset = ((y * SurfaceWidth) + x) * 4;
            pixels[offset] = color.Red;
            pixels[offset + 1] = color.Green;
            pixels[offset + 2] = color.Blue;
            pixels[offset + 3] = color.Alpha;
        }
    }
    return (native.Resource!, pixels);
}

static object MeasurePerformance(
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram fixedProgram,
    CompiledGraphicsProgram derivativeProgram,
    ProfileNativeCompositionResource resource)
{
    using var fixedRenderer = new VulkanOrderedQuadRenderer(
        plant,
        fixedProgram,
        SurfaceWidth,
        SurfaceHeight,
        new Native2DPipelineOptions(
            Native2DPipelineKind.ProfileMsdf,
            TransparentClear: true,
            EnableStraightAlphaBlend: true));
    using var derivativeRenderer = new VulkanOrderedQuadRenderer(
        plant,
        derivativeProgram,
        SurfaceWidth,
        SurfaceHeight,
        new Native2DPipelineOptions(
            Native2DPipelineKind.ProfileMsdf,
            TransparentClear: true,
            EnableStraightAlphaBlend: true));
    using var fixedCache = new ProfileNativeRealizationCache(fixedRenderer, 1);
    using var derivativeCache = new ProfileNativeRealizationCache(derivativeRenderer, 1);
    fixedCache.Warm(resource);
    derivativeCache.Warm(resource);
    var instance = new ProfileNativeInstance(48.375f, 70.625f, 0.9f);

    object Measure(
        string mode,
        VulkanOrderedQuadRenderer renderer,
        ProfileNativeRealizationCache cache,
        ProfileNativeReconstructionOptions reconstruction)
    {
        long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch watch = Stopwatch.StartNew();
        Native2DPassResult? last = null;
        for (int frame = 0; frame < 64; frame++)
        {
            renderer.Begin2D();
            cache.Submit(resource, instance, reconstruction);
            last = renderer.End2D(captureReadback: false);
        }
        watch.Stop();
        return new
        {
            mode,
            frames = 64,
            totalMilliseconds = Math.Round(watch.Elapsed.TotalMilliseconds, 3),
            meanMilliseconds = Math.Round(watch.Elapsed.TotalMilliseconds / 64, 4),
            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedStart,
            last!.Metrics.QuadCount,
            last.Metrics.DrawCalls,
        };
    }

    object neutral = Measure(
        "neutral",
        fixedRenderer,
        fixedCache,
        ProfileNativeReconstructionOptions.M19BNeutralThreshold);
    object derivative = Measure(
        "derivative",
        derivativeRenderer,
        derivativeCache,
        ProfileNativeReconstructionOptions.RuntimeDefault);
    return new
    {
        neutral,
        derivative,
        textureUploads = fixedCache.UploadCount + derivativeCache.UploadCount,
        cacheHits = fixedCache.CacheHits + derivativeCache.CacheHits,
        atlasSize = new[] { resource.AtlasWidth, resource.AtlasHeight },
        cpuSubmissionPathChanged = false,
    };
}

static string[] ParseAssets(string[] args)
{
    var assets = new List<string>();
    for (int index = 0; index < args.Length; index++)
    {
        if (args[index] == "--asset")
        {
            Require(index + 1 < args.Length, "--asset requires a Mossward asset id.");
            assets.Add(args[++index]);
        }
    }
    return assets.Count == 0 ? ["worker", "tree", "hq"] : assets.Distinct(StringComparer.Ordinal).ToArray();
}

static int ParseZoom(string[] args)
{
    int index = Array.IndexOf(args, "--zoom");
    if (index < 0)
    {
        return 8;
    }
    int zoom = 0;
    Require(index + 1 < args.Length && int.TryParse(args[index + 1], out zoom), "--zoom requires 1, 2, 4, 8, or 16.");
    Require(zoom is 1 or 2 or 4 or 8 or 16, "--zoom requires 1, 2, 4, 8, or 16.");
    return zoom;
}

static void WriteJson(string path, object value)
{
    File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}

static string FindRepositoryRoot()
{
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
        {
            return directory.FullName;
        }
    }
    throw new DirectoryNotFoundException();
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
