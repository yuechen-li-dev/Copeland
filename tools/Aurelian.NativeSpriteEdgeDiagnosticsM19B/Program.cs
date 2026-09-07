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
using SkiaSharp;

const int SurfaceWidth = 96;
const int SurfaceHeight = 96;
string root = FindRepositoryRoot();
string output = Path.Combine(root, "artifacts", "oblivion-notebook-native-sprite-edge-diagnostics-m19b");
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

CompiledGraphicsProgram program = CompileShader(root, "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts");
VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeSpriteEdgeDiagnosticsM19B"));
Require(init.Success && init.Plant is not null, string.Join("; ", init.Diagnostics.Select(item => item.Message)));

using AurelianVulkanPlant plant = init.Plant!;
using var cpu = new StrategyAssets(assetDirectory);
var measurements = new List<object>();

foreach ((string asset, ProfileNativeCompositionResource resource) in resources)
{
    float scale = asset == "worker" ? 0.9f : 0.8f;
    var fractional = new ProfileNativeInstance(48.375f, 70.625f, scale);
    var snapped = fractional with
    {
        OriginX = MathF.Round(fractional.OriginX),
        OriginY = MathF.Round(fractional.OriginY),
    };

    byte[] cpuFractional = RenderCpu(cpu, asset, fractional);
    byte[] nativeFractional = RenderNative(
        plant,
        program,
        resource,
        fractional,
        ProfileNativeReconstructionOptions.GlyphSmallScreenCompensation);
    byte[] nativeNeutralThreshold = RenderNative(
        plant,
        program,
        resource,
        fractional,
        ProfileNativeReconstructionOptions.RuntimeDefault);
    byte[] nativeCoverageThreshold = RenderNative(
        plant,
        program,
        resource,
        fractional,
        new ProfileNativeReconstructionOptions(0.45f));
    byte[] cpuSnapped = RenderCpu(cpu, asset, snapped);
    byte[] nativeSnapped = RenderNative(
        plant,
        program,
        resource,
        snapped,
        ProfileNativeReconstructionOptions.GlyphSmallScreenCompensation);

    ProfileImageComparison fractionalComparison = ProfileEdgeDiagnostics.Compare(
        cpuFractional, nativeFractional, SurfaceWidth, SurfaceHeight);
    ProfileImageComparison snappedComparison = ProfileEdgeDiagnostics.Compare(
        cpuSnapped, nativeSnapped, SurfaceWidth, SurfaceHeight);
    ProfileImageComparison neutralThresholdComparison = ProfileEdgeDiagnostics.Compare(
        cpuFractional, nativeNeutralThreshold, SurfaceWidth, SurfaceHeight);
    ProfileImageComparison coverageThresholdComparison = ProfileEdgeDiagnostics.Compare(
        cpuFractional, nativeCoverageThreshold, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics cpuFractionalEdges = ProfileEdgeDiagnostics.Measure(cpuFractional, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics nativeFractionalEdges = ProfileEdgeDiagnostics.Measure(nativeFractional, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics cpuSnappedEdges = ProfileEdgeDiagnostics.Measure(cpuSnapped, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics nativeSnappedEdges = ProfileEdgeDiagnostics.Measure(nativeSnapped, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics nativeNeutralThresholdEdges = ProfileEdgeDiagnostics.Measure(nativeNeutralThreshold, SurfaceWidth, SurfaceHeight);
    ProfileEdgeMetrics nativeCoverageThresholdEdges = ProfileEdgeDiagnostics.Measure(nativeCoverageThreshold, SurfaceWidth, SurfaceHeight);

    WriteInspectionImage(
        Path.Combine(output, $"{asset}-dark-{zoom}x.png"),
        zoom,
        new byte[] { 18, 27, 25, 255 },
        cpuFractional,
        nativeFractional,
        nativeNeutralThreshold);
    WriteInspectionImage(
        Path.Combine(output, $"{asset}-light-{zoom}x.png"),
        zoom,
        new byte[] { 225, 220, 202, 255 },
        cpuFractional,
        nativeFractional,
        nativeNeutralThreshold);
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
        coverageThreshold = new
        {
            threshold = 0.45,
            nativeEdges = nativeCoverageThresholdEdges,
            comparison = coverageThresholdComparison,
        },
        snappingDelta = new
        {
            silhouetteIoU = snappedComparison.SilhouetteIntersectionOverUnion - fractionalComparison.SilhouetteIntersectionOverUnion,
            nativeTransitionWidthPixels = nativeSnappedEdges.EstimatedTransitionWidthPixels - nativeFractionalEdges.EstimatedTransitionWidthPixels,
        },
    });
}

var report = new
{
    milestone = "OBLIVION-NOTEBOOK-NATIVE-SPRITE-EDGE-DIAGNOSTICS-M19B",
    device = init.Facts!.PhysicalDeviceName,
    command = "dotnet run --project tools/Aurelian.NativeSpriteEdgeDiagnosticsM19B -c Release -- --asset worker --asset tree --zoom 8",
    inspection = new
    {
        panelOrder = new[] { "CPU reference", "former native policy", "fixed native policy", "amplified CPU/former-native alpha difference" },
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
        reconstruction = "12 field-pixel range, size-aware threshold, quarter-screen-pixel ramp",
        placement = "fractional and rounded presentation origins measured separately",
    },
    diagnosis = new
    {
        rootCause = "Profile draws inherited the glyph-oriented small-screen threshold compensation. On independently layered tiny Profile components this eroded opaque coverage and contributed substantial apparent softness.",
        ruledOut = new[] { "pixel snapping", "nearest sampler", "mipmapping", "atlas crop precision", "alpha blend mode" },
        fix = "Profile runtime submission now uses an explicit neutral 0.5 reconstruction threshold. Glyph rendering retains its existing size compensation.",
        residual = "Worker partial-coverage width remains about 2.57 pixels versus 0.90 on direct CPU contours.",
        nextStep = "Evaluate derivative-based or explicit per-quad screen-space reconstruction width in this isolated tool; the remaining seam is MSDF minification/reconstruction.",
    },
    measurements,
};
WriteJson(Path.Combine(output, "edge-diagnostics.json"), report);
WriteJson(Path.Combine(output, "manifest.json"), Directory.GetFiles(output)
    .Where(path => !string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.Ordinal))
    .Order()
    .Select(path => new
{
    file = Path.GetFileName(path),
    bytes = new FileInfo(path).Length,
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
}).ToArray());
Console.WriteLine($"AURELIAN_NATIVE_SPRITE_EDGE_DIAGNOSTICS_M19B_READY assets={string.Join(',', requestedAssets)} zoom={zoom} device={init.Facts.PhysicalDeviceName}");

static byte[] RenderCpu(StrategyAssets assets, string asset, ProfileNativeInstance instance)
{
    using var bitmap = new SKBitmap(new SKImageInfo(SurfaceWidth, SurfaceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    assets.Draw(canvas, asset, instance.OriginX, instance.OriginY, instance.Scale);
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
    return pixels;
}

static byte[] RenderNative(
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram program,
    ProfileNativeCompositionResource resource,
    ProfileNativeInstance instance,
    ProfileNativeReconstructionOptions reconstruction)
{
    using var renderer = new VulkanOrderedQuadRenderer(
        plant,
        program,
        SurfaceWidth,
        SurfaceHeight,
        new Native2DPipelineOptions(
            Native2DPipelineKind.MsdfText,
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
    byte[] native,
    byte[] snapped)
{
    const int gap = 2;
    int width = (SurfaceWidth * 4) + (gap * 3);
    byte[] baseImage = new byte[width * SurfaceHeight * 4];
    Fill(baseImage, matte);
    CopyPanel(Composite(cpu, matte), baseImage, width, 0);
    CopyPanel(Composite(native, matte), baseImage, width, SurfaceWidth + gap);
    CopyPanel(Composite(snapped, matte), baseImage, width, (SurfaceWidth + gap) * 2);
    CopyPanel(Difference(cpu, native), baseImage, width, (SurfaceWidth + gap) * 3);
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
    return assets.Count == 0 ? ["worker", "tree"] : assets.Distinct(StringComparer.Ordinal).ToArray();
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
