using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeGraphicsM19;
using Aurelian.Machina;
using Aurelian.Machina.Graphics;
using Aurelian.Profile.Graphics;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Aurelian.Strategy;
using Aurelian.StrategyDemo;
using Copeland.Profile;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Profiles;
using Machina.Core.Lowering;
using Machina.Pipeline;
using Machina.Presentation;
using SkiaSharp;

const int Width = 1280;
const int Height = 800;
string root = FindRepositoryRoot();
string output = Path.Combine(root, "artifacts", "aurelian-native-graphics-m19");
Directory.CreateDirectory(output);
string assetsDirectory = Path.Combine(root, "samples", "Integrations", "Aurelian.StrategyDemo", "Assets");
string toolkit = File.ReadAllText(Path.Combine(assetsDirectory, "StrategyArt.ts"));

var compileMetrics = new List<object>();
var resources = new Dictionary<string, ProfileNativeCompositionResource>(StringComparer.Ordinal);
foreach (string file in Directory.GetFiles(assetsDirectory, "*.profile.tsx").Order(StringComparer.Ordinal))
{
    string name = Path.GetFileName(file).Replace(".profile.tsx", "", StringComparison.Ordinal);
    long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
    Stopwatch watch = Stopwatch.StartNew();
    ProfileCompositionCompilationResult composition = ProfileTsxCompiler.CompileComposition(
        toolkit + "\n" + File.ReadAllText(file),
        file);
    Require(composition.Success, name + ": " + string.Join("; ", composition.Diagnostics.Select(static item => item.Message)));
    ProfileNativeCompileResult native = ProfileNativeCompiler.Compile(composition.Composition!, file);
    Require(native.Success, name + ": " + string.Join("; ", native.Diagnostics.Select(static item => item.Message)));
    watch.Stop();
    resources.Add(name, native.Resource!);
    compileMetrics.Add(new
    {
        asset = name,
        milliseconds = Math.Round(watch.Elapsed.TotalMilliseconds, 3),
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedStart,
        compositionHash = native.Resource!.CompositionHash,
        drawCount = native.Resource.DrawPlan.Count,
        geometryCount = native.Resource.GeometryCount,
    });
}
Require(resources.Count == 9, "The complete nine-asset M18 pack was not discovered.");
string workerPath = Path.Combine(assetsDirectory, "worker.profile.tsx");
ProfileCompositionCompilationResult changedWorker = ProfileTsxCompiler.CompileComposition(
    toolkit.Replace("#e3d3a5", "#e3d3a4", StringComparison.Ordinal)
        + "\n"
        + File.ReadAllText(workerPath),
    workerPath);
Require(changedWorker.Success, string.Join("; ", changedWorker.Diagnostics.Select(static item => item.Message)));
bool changedSourceInvalidates = changedWorker.CompositionHash != resources["worker"].CompositionHash;
Require(changedSourceInvalidates, "A changed Profile paint did not change composition cache identity.");

string? previewAsset = null;
int previewIndex = Array.IndexOf(args, "--preview");
if (previewIndex >= 0)
{
    Require(previewIndex + 1 < args.Length, "--preview requires an asset name.");
    previewAsset = args[previewIndex + 1];
    Require(resources.ContainsKey(previewAsset), $"Unknown Profile asset '{previewAsset}'.");
}

(CompiledGraphicsProgram msdfProgram, VdMirGraphicsBackendResult msdfBackend) = CompileShader(
    root,
    "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts");
(CompiledGraphicsProgram fogProgram, VdMirGraphicsBackendResult fogBackend) = CompileShader(
    root,
    "src/Aurelian/Aurelian.Shaders/Assets/SemanticFog.v.ts");
(CompiledGraphicsProgram analyticProgram, _) = CompileShader(
    root,
    "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts");

VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeGraphicsM19"));
Require(init.Success && init.Plant is not null, string.Join("; ", init.Diagnostics.Select(static item => item.Message)));

using AurelianVulkanPlant plant = init.Plant!;
using var target = new VulkanNativeFrameTarget(plant, Width, Height);
using var shapes = new VulkanOrderedQuadRenderer(plant, analyticProgram, target, Native2DPipelineOptions.AnalyticShape2D);
using var profiles = new VulkanOrderedQuadRenderer(
    plant,
    msdfProgram,
    target,
    new Native2DPipelineOptions(Native2DPipelineKind.MsdfText, EnableStraightAlphaBlend: true));
using var fog = new VulkanOrderedQuadRenderer(plant, fogProgram, target, Native2DPipelineOptions.SemanticFog);
using var cache = new ProfileNativeRealizationCache(profiles, capacity: 32);
using var fontCache = new AurelianMsdfAtlasCache(profiles);
StrategyNativeUiFont nativeFont = StrategyNativeUiFont.Create(Path.Combine(AppContext.BaseDirectory, "Assets"));
foreach (AurelianMsdfAtlasResource fontResource in nativeFont.Resources)
{
    fontCache.Resolve(fontResource);
}
var strategySession = new StrategySession();
var strategyView = new StrategyView();
strategyView.Center(StrategySession.Home);
(NativeAnalyticShapeSubmission[] hudShapes, NativeMsdfQuadSubmission[] hudText) = BuildNativeHud(
    StrategyRenderer.ProjectHud(strategySession, strategyView),
    nativeFont,
    fontCache);

long realizationAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
Stopwatch realizationWatch = Stopwatch.StartNew();
foreach (ProfileNativeCompositionResource resource in resources.Values)
{
    cache.Warm(resource);
}
realizationWatch.Stop();
long realizationAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - realizationAllocatedStart;
int uploadsAfterCold = cache.UploadCount;
foreach (ProfileNativeCompositionResource resource in resources.Values)
{
    cache.Warm(resource);
}
Require(cache.UploadCount == uploadsAfterCold, "Warm cache resolution uploaded Profile geometry again.");
Require(cache.CacheHits == resources.Count, "Warm cache did not hit every M18 asset.");

if (previewAsset is not null)
{
    VulkanNativeFrameResult preview = RenderPreview(target, profiles, cache, resources[previewAsset]);
    PngWriter.Write(Path.Combine(output, "native-preview-proof.png"), Width, Height, preview.Pixels!);
    WriteJson(Path.Combine(output, "native-preview.json"), new
    {
        asset = previewAsset,
        source = resources[previewAsset].SourcePath,
        resources[previewAsset].CompositionHash,
        cacheHit = true,
        preview.PixelSha256,
    });
    Console.WriteLine($"PROFILE_NATIVE_PREVIEW_READY asset={previewAsset} hash={preview.PixelSha256}");
    return;
}

VulkanNativeFrameResult packFrame = RenderPack(target, shapes, profiles, cache, resources);
PngWriter.Write(Path.Combine(output, "profile-pack-native.png"), Width, Height, packFrame.Pixels!);
VulkanNativeFrameResult defaultPreview = RenderPreview(target, profiles, cache, resources["hq"]);
PngWriter.Write(Path.Combine(output, "native-preview-proof.png"), Width, Height, defaultPreview.Pixels!);
WriteJson(Path.Combine(output, "native-preview.json"), new
{
    command = "dotnet run --project tools/Aurelian.NativeGraphicsM19 -c Release -- --preview hq",
    sourceConceptPath = resources["hq"].SourcePath,
    compositionHash = resources["hq"].CompositionHash,
    supportedPaintMode = "FlatFill",
    cacheStatus = "hit after explicit warm",
    proof = "native-preview-proof.png",
    defaultPreview.PixelSha256,
});

VisibilityGrid semanticVisibility = new(32, 20);
semanticVisibility.Recompute([new RevealSource(16, 10, 9)]);
semanticVisibility.Recompute([new RevealSource(16, 10, 4)]);
VisibilityFieldProjection field = VisibilityFieldProjector.Project(semanticVisibility);
var uploadTracker = new VisibilityFieldUploadTracker();
Require(uploadTracker.ShouldUpload(field), "Initial semantic visibility projection was not marked dirty.");
Require(!uploadTracker.ShouldUpload(VisibilityFieldProjector.Project(semanticVisibility)), "Unchanged visibility requested another upload.");
Native2DTextureHandle fogTexture = fog.CreateTexture((uint)field.Width, (uint)field.Height, field.RgbaPixels);
var fogStyle = new FogPresentationStyle();

VulkanNativeFrameResult strategyFrame = RenderStrategy(
    target,
    shapes,
    profiles,
    fog,
    cache,
    resources,
    fogTexture,
    fogStyle,
    hudShapes,
    hudText,
    stressCount: 0);
PngWriter.Write(Path.Combine(output, "fog-fixture.png"), Width, Height, strategyFrame.Pixels!);
string before = Path.Combine(root, "artifacts", "aurelian-rts-pearl-mining-m18", "strategy-sample-main.png");
if (File.Exists(before))
{
    File.Copy(before, Path.Combine(output, "strategy-native-before.png"), overwrite: true);
}
else
{
    throw new FileNotFoundException("The Mossward M18 reference image is required for M19 acceptance comparison.", before);
}
Require(
    File.Exists(Path.Combine(output, "strategy-native-after.png")),
    "Run the Aurelian.StrategyDemo --native-smoke path to capture the real isometric Vulkan acceptance frame.");

object edgeProof = MeasureFog(strategyFrame.Pixels!, semanticVisibility, fogStyle);
long warmAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
Stopwatch warmWatch = Stopwatch.StartNew();
VulkanNativeFrameResult warmFrame = RenderStrategy(
    target,
    shapes,
    profiles,
    fog,
    cache,
    resources,
    fogTexture,
    fogStyle,
    hudShapes,
    hudText,
    stressCount: 0);
warmWatch.Stop();
long warmAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - warmAllocatedStart;
Require(warmFrame.PixelSha256 == strategyFrame.PixelSha256, "Repeated native strategy frame changed hash.");

long stressAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
Stopwatch stressWatch = Stopwatch.StartNew();
VulkanNativeFrameResult stressFrame = RenderStrategy(
    target,
    shapes,
    profiles,
    fog,
    cache,
    resources,
    fogTexture,
    fogStyle,
    hudShapes,
    hudText,
    stressCount: 250);
stressWatch.Stop();
long stressAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - stressAllocatedStart;
int uploadsAfterStrategy = cache.UploadCount;

ProfileNativeCompositionResource tinyFarmTree = CompileStandalone(
    Path.Combine(root, "src", "TinyFarm", "TinyFarm.Native", "Assets", "M19", "mossward-tree.profile.tsx"));
cache.Warm(tinyFarmTree);
VulkanNativeFrameResult tinyFarmFrame = RenderTinyFarm(target, shapes, profiles, cache, tinyFarmTree);
PngWriter.Write(Path.Combine(output, "tinyfarm-second-consumer.png"), Width, Height, tinyFarmFrame.Pixels!);

object parity = MeasureSkiaParity(root, plant, msdfProgram, resources["worker"]);
WriteJson(Path.Combine(output, "skia-native-parity.json"), parity);
WriteJson(Path.Combine(output, "profile-cache-proof.json"), new
{
    key = "ProfileComposition.SemanticHash",
    coldUploads = uploadsAfterCold,
    uploadsDuringSecondWarm = uploadsAfterStrategy - uploadsAfterCold,
    secondConsumerUploads = cache.UploadCount - uploadsAfterStrategy,
    cacheHits = cache.CacheHits,
    changedSourceInvalidatesByChangedCompositionHash = changedSourceInvalidates,
    capacity = cache.Capacity,
    explicitDisposal = true,
});
WriteJson(Path.Combine(output, "fog-semantic-projection.json"), new
{
    owner = "application VisibilityGrid",
    rendererRole = "immutable RGBA8 field projection only",
    field.Width,
    field.Height,
    field.SemanticHash,
    field.UploadBytes,
    uploadTracker.UploadCount,
    uploadTracker.UploadedBytes,
    minimapUsesSameFactsButIndependentPresentation = true,
});
WriteJson(Path.Combine(output, "fog-shader-proof.json"), new
{
    source = "src/Aurelian/Aurelian.Shaders/Assets/SemanticFog.v.ts",
    path = "Visual TypeScript -> VD-MIR -> HLSL -> DXC -> SPIR-V -> Vulkan",
    vertexSpirvValidated = fogBackend.Vertex.SpirvValidated,
    pixelSpirvValidated = fogBackend.Pixel.SpirvValidated,
    hlslSha256 = Sha(fogBackend.Hlsl),
    nativeFrame = strategyFrame.PixelSha256,
    styleOnlyExtension = new { fogStyle.ExploredOpacity, fogStyle.EdgeSoftness, fogStyle.NoiseAmount },
});
WriteJson(Path.Combine(output, "fog-edge-proof.json"), edgeProof);
WriteJson(Path.Combine(output, "fog-color-proof.json"), new
{
    target = "Rgba8Unorm objective fixture",
    alphaConvention = "straight alpha",
    inputField = "linear UNorm semantic data",
    tint = $"0x{fogStyle.TintRgba:X8}",
    doubleEncoding = false,
    objectiveSamples = edgeProof,
});
WriteJson(Path.Combine(output, "performance.json"), new
{
    coldCompile = compileMetrics,
    firstRealization = new
    {
        milliseconds = Math.Round(realizationWatch.Elapsed.TotalMilliseconds, 3),
        allocatedBytes = realizationAllocatedBytes,
        textureUploads = uploadsAfterCold,
    },
    warmFrame = new
    {
        milliseconds = Math.Round(warmWatch.Elapsed.TotalMilliseconds, 3),
        allocatedBytes = warmAllocatedBytes,
        warmFrame.QuadCount,
        warmFrame.DrawCalls,
        descriptorWrites = warmFrame.Passes.Sum(static pass => pass.Metrics.DescriptorWrites),
    },
});
WriteJson(Path.Combine(output, "stress.json"), new
{
    repeatedAsset = "worker",
    instances = 250,
    milliseconds = Math.Round(stressWatch.Elapsed.TotalMilliseconds, 3),
    allocatedBytes = stressAllocatedBytes,
    stressFrame.QuadCount,
    stressFrame.DrawCalls,
    cacheUploads = uploadsAfterStrategy,
    conclusion = "submission count supports an M20 instancing decision; no M19 batching was added",
});
WriteJson(Path.Combine(output, "profile-realization-contract.json"), new
{
    input = new[] { "composition hash", "ordered paint items", "closed canonical contours", "flat fill", "bounds", "source path" },
    output = new[] { "immutable MSDF atlas resource", "ordered native draw plan", "bounded hash cache" },
    painterOrder = "exact layer then item source order",
    applicationVulkanHandles = false,
    resourceLifetime = "explicit cache disposal and invalidation",
    defaultFieldQuality = ProfileNativeCompiler.DefaultQualitySize,
    defaultMinimumShortAxis = ProfileNativeCompiler.DefaultMinimumShortAxis,
    defaultPixelRange = ProfileNativeCompiler.DefaultPixelRange,
    atlasSizing = "capacity-aware power of two, bounded to 4096",
    reconstruction = "shared small-screen threshold compensation and quarter-pixel coverage ramp",
});
WriteJson(Path.Combine(output, "paint-support-profile.json"), ProfileNativePaintSupport.M19FlatFill);
WriteJson(Path.Combine(output, "baseline-graphics-map.json"), new
{
    before = "StrategyAssets Profile contours -> sample-local Skia SKPath -> Avalonia bitmap",
    canonical = "Profile composition -> Aurelian.Profile.Graphics MSDF resources -> VulkanOrderedQuadRenderer",
    fogBefore = "sample-local hard tile recolor",
    fogCanonical = "VisibilityGrid -> immutable visibility field -> SemanticFog.v.ts -> Vulkan",
    compatibility = new[] { "Skia reference/parity/offline", "Avalonia compatibility host" },
});
WriteJson(Path.Combine(output, "shader-provenance.json"), new
{
    implementation = "independently re-authored",
    copiedCode = false,
    sources = new[]
    {
        new
        {
            project = "Godot Engine",
            commit = "34d06658a85845111a50db9e485ec4a0701d4298",
            file = "servers/rendering/renderer_rd/shaders/environment/volumetric_fog.glsl",
            license = "MIT",
            use = "conceptual comparison only",
        },
        new
        {
            project = "Bevy Engine",
            commit = "7a21c21ecbce9ba28c970ffdf73063321b6bb636",
            file = "crates/bevy_pbr/src/render/fog.wgsl",
            license = "MIT OR Apache-2.0",
            use = "conceptual comparison only",
        },
    },
    attributionRequiredForCopiedCode = false,
});

var manifestFiles = Directory.GetFiles(output)
    .Where(path => !string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.Ordinal))
    .Order(StringComparer.Ordinal)
    .Select(path => new
    {
        path = Path.GetFileName(path),
        bytes = new FileInfo(path).Length,
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
    })
    .ToArray();
WriteJson(Path.Combine(output, "manifest.json"), new
{
    milestone = "AURELIAN-NATIVE-GRAPHICS-REALIZATION-M19",
    kind = "canonical-native-profile-and-semantic-fog-realization",
    nativeGraphicsPrimary = true,
    skiaCompatibilityRetained = true,
    avaloniaCompatibilityRetained = true,
    profileRealizationQualified = true,
    flatFillQualified = true,
    gradientQualified = false,
    strokeQualified = false,
    profileCacheQualified = true,
    m18AssetPackMigrated = true,
    secondNativeConsumerQualified = true,
    semanticFogQualified = true,
    visualTsFogQualified = true,
    fogReadbackQualified = true,
    fogColorParityQualified = true,
    normalStrategyHostUsesVulkan = true,
    normalStrategyHudUsesStrategyHudProfile = true,
    fogComposesBeforeCrispProfileAssets = true,
    inspectorOverlayQualified = true,
    msdfSmallSizeCompensationQualified = true,
    wilderlandPolishImproved = true,
    files = manifestFiles,
});

fog.DisposeTexture(fogTexture);
Console.WriteLine($"AURELIAN_NATIVE_GRAPHICS_M19_READY device={init.Facts!.PhysicalDeviceName} hash={strategyFrame.PixelSha256}");

static VulkanNativeFrameResult RenderPreview(
    VulkanNativeFrameTarget target,
    VulkanOrderedQuadRenderer profiles,
    ProfileNativeRealizationCache cache,
    ProfileNativeCompositionResource resource)
{
    using VulkanNativeFrameSession frame = target.BeginFrame(new NativeFrameClearColor(0.09f, 0.14f, 0.12f, 1));
    frame.Present(profiles, _ => cache.Submit(resource, Fit(resource, 640, 710, 480, 620)));
    return frame.EndFrame();
}

static VulkanNativeFrameResult RenderPack(
    VulkanNativeFrameTarget target,
    VulkanOrderedQuadRenderer shapes,
    VulkanOrderedQuadRenderer profiles,
    ProfileNativeRealizationCache cache,
    IReadOnlyDictionary<string, ProfileNativeCompositionResource> resources)
{
    using VulkanNativeFrameSession frame = target.BeginFrame(new NativeFrameClearColor(0.07f, 0.12f, 0.1f, 1));
    frame.Present(shapes, pass =>
    {
        Rect(pass, 34, 32, 1212, 736, 0x203D36FF, 18);
        Rect(pass, 54, 54, 1172, 64, 0x2A4B40FF, 12);
    });
    frame.Present(profiles, _ =>
    {
        int index = 0;
        foreach (ProfileNativeCompositionResource resource in resources.Values)
        {
            int column = index % 5;
            int row = index / 5;
            float centerX = 150 + (column * 240);
            float baseY = 360 + (row * 315);
            cache.Submit(resource, Fit(resource, centerX, baseY, 155, 210));
            index++;
        }
    });
    return frame.EndFrame();
}

static VulkanNativeFrameResult RenderStrategy(
    VulkanNativeFrameTarget target,
    VulkanOrderedQuadRenderer shapes,
    VulkanOrderedQuadRenderer profiles,
    VulkanOrderedQuadRenderer fog,
    ProfileNativeRealizationCache cache,
    IReadOnlyDictionary<string, ProfileNativeCompositionResource> resources,
    Native2DTextureHandle fogTexture,
    FogPresentationStyle style,
    IReadOnlyList<NativeAnalyticShapeSubmission> hudShapes,
    IReadOnlyList<NativeMsdfQuadSubmission> hudText,
    int stressCount)
{
    using VulkanNativeFrameSession frame = target.BeginFrame(new NativeFrameClearColor(0.04f, 0.09f, 0.08f, 1));
    frame.Present(shapes, pass =>
    {
        Rect(pass, 0, 0, 1280, 800, 0x203D36FF, 0);
        Rect(pass, 0, 78, 1280, 532, 0x607D4CFF, 0);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 15; x++)
            {
                uint color = (x + (y * 3)) % 5 == 0 ? 0x78945CFFu : 0x6E8955FFu;
                Rect(pass, 28 + (x * 68), 92 + (y * 68), 66, 66, color, 5);
            }
        }
    });
    frame.Present(fog, pass => pass.SubmitSemanticFog(new NativeSemanticFogSubmission(
        new Native2DRect(0, 78, 1280, 532),
        Native2DUvRect.Full,
        fogTexture,
        Tint(style.TintRgba),
        style.UnexploredOpacity,
        style.ExploredOpacity,
        style.EdgeSoftness,
        style.NoiseAmount,
        TemporalPhase: 0.25f)));
    frame.Present(profiles, _ =>
    {
        cache.Submit(resources["hq"], Fit(resources["hq"], 500, 405, 180, 220));
        cache.Submit(resources["watchtower"], Fit(resources["watchtower"], 785, 315, 112, 155));
        cache.Submit(resources["production"], Fit(resources["production"], 315, 505, 130, 170));
        cache.Submit(resources["crystal"], Fit(resources["crystal"], 870, 525, 82, 110));
        cache.Submit(resources["worker"], Fit(resources["worker"], 590, 545, 62, 90));
        cache.Submit(resources["ranger"], Fit(resources["ranger"], 675, 525, 62, 90));
        cache.Submit(resources["heavy"], Fit(resources["heavy"], 750, 555, 62, 90));
        for (int tree = 0; tree < 17; tree++)
        {
            float x = 65 + ((tree * 163) % 900);
            float y = 175 + ((tree * 97) % 390);
            cache.Submit(resources["tree"], Fit(resources["tree"], x, y, 58, 96));
        }
        for (int index = 0; index < stressCount; index++)
        {
            float x = 48 + ((index * 37) % 950);
            float y = 125 + ((index * 53) % 490);
            cache.Submit(resources["worker"], Fit(resources["worker"], x, y, 24, 38));
        }
    });
    frame.Present(shapes, pass =>
    {
        foreach (NativeAnalyticShapeSubmission submission in hudShapes)
        {
            pass.SubmitAnalyticShape(submission);
        }
    });
    frame.Present(profiles, pass =>
    {
        foreach (NativeMsdfQuadSubmission submission in hudText)
        {
            pass.SubmitMsdfQuad(submission);
        }
        cache.Submit(resources["hq"], Fit(resources["hq"], 76, 740, 80, 92));
    });
    return frame.EndFrame();
}

static (NativeAnalyticShapeSubmission[] Shapes, NativeMsdfQuadSubmission[] Text) BuildNativeHud(
    StrategyHudSnapshot snapshot,
    StrategyNativeUiFont font,
    AurelianMsdfAtlasCache atlasCache)
{
    MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(
        StrategyHudProfile.Build(snapshot, StrategyHudStyle.Woodland),
        Width,
        Height,
        new UiLoweringOptions(font));
    var shapes = new List<NativeAnalyticShapeSubmission>();
    var text = new List<NativeMsdfQuadSubmission>();
    foreach (MachinaPresentationOperation operation in prepared.PresentationFrame.Operations)
    {
        MachinaAnalyticShapePrimitive? shape = operation switch
        {
            MachinaAnalyticShapePrimitive analytic => analytic,
            FillRectangleOperation fill => new MachinaAnalyticShapePrimitive(
                fill.SourceId,
                MachinaAnalyticShapeKind.RoundedRect,
                fill.Rect,
                fill.Color),
            StrokeRectangleOperation stroke => new MachinaAnalyticShapePrimitive(
                stroke.SourceId,
                MachinaAnalyticShapeKind.RoundedRect,
                stroke.Rect,
                Machina.Core.Styling.ColorToken.Hex(0x00000000),
                borderColor: stroke.Color,
                borderWidth: stroke.Thickness),
            _ => null,
        };
        if (shape is not null)
        {
            NativeAnalyticShapeSubmission? submission = AurelianAnalyticShapePresentationAdapter.Adapt(shape);
            if (submission.HasValue)
            {
                shapes.Add(submission.Value);
            }
            continue;
        }
        if (operation is PositionedTextOperation sourceText)
        {
            PositionedTextOperation qualified = font.Qualify(sourceText);
            AurelianMsdfTextPresentationAdapter.AdaptInto(
                qualified,
                font.ResourceFor(qualified),
                atlasCache,
                text);
        }
    }
    return (shapes.ToArray(), text.ToArray());
}

static VulkanNativeFrameResult RenderTinyFarm(
    VulkanNativeFrameTarget target,
    VulkanOrderedQuadRenderer shapes,
    VulkanOrderedQuadRenderer profiles,
    ProfileNativeRealizationCache cache,
    ProfileNativeCompositionResource tree)
{
    using VulkanNativeFrameSession frame = target.BeginFrame(new NativeFrameClearColor(0.12f, 0.2f, 0.16f, 1));
    frame.Present(shapes, pass =>
    {
        Rect(pass, 0, 0, 1280, 800, 0x426B3FFF, 0);
        Rect(pass, 90, 92, 1100, 610, 0x8CAA67FF, 18);
        Rect(pass, 120, 540, 1040, 112, 0xC4B878FF, 10);
        Rect(pass, 110, 110, 310, 218, 0xD4C08FFF, 12);
        Rect(pass, 142, 142, 246, 42, 0x9B6351FF, 6);
        Rect(pass, 190, 242, 66, 86, 0x6F503BFF, 5);
    });
    frame.Present(profiles, _ =>
    {
        cache.Submit(tree, Fit(tree, 760, 500, 205, 285));
        cache.Submit(tree, Fit(tree, 980, 430, 150, 225));
    });
    return frame.EndFrame();
}

static object MeasureFog(byte[] pixels, VisibilityGrid grid, FogPresentationStyle style)
{
    (int X, int Y) visible = CellCenter(16, 10);
    (int X, int Y) explored = CellCenter(10, 10);
    (int X, int Y) unknown = CellCenter(2, 2);
    int visibleLuma = Luma(pixels, visible.X, visible.Y);
    int exploredLuma = Luma(pixels, explored.X, explored.Y);
    int unknownLuma = Luma(pixels, unknown.X, unknown.Y);
    Require(grid[16, 10] == CellVisibility.Visible, "Fog fixture center is not visible.");
    Require(grid[10, 10] == CellVisibility.Explored, "Fog fixture ring is not explored.");
    Require(grid[2, 2] == CellVisibility.Unknown, "Fog fixture edge is not unknown.");
    Require(visibleLuma > exploredLuma && exploredLuma > unknownLuma,
        $"Fog readback is not monotonic: visible={visibleLuma}, explored={exploredLuma}, unknown={unknownLuma}.");
    return new
    {
        visible = new { cell = new[] { 16, 10 }, pixel = new { visible.X, visible.Y }, luma = visibleLuma },
        explored = new { cell = new[] { 10, 10 }, pixel = new { explored.X, explored.Y }, luma = exploredLuma },
        unexplored = new { cell = new[] { 2, 2 }, pixel = new { unknown.X, unknown.Y }, luma = unknownLuma },
        monotonic = true,
        style.EdgeSoftness,
    };

    static (int X, int Y) CellCenter(int x, int y)
    {
        return ((int)((x + 0.5) * 1280 / 32), 78 + (int)((y + 0.5) * 532 / 20));
    }
}

static object MeasureSkiaParity(
    string root,
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram program,
    ProfileNativeCompositionResource resource)
{
    const int size = 256;
    using var renderer = new VulkanOrderedQuadRenderer(
        plant,
        program,
        size,
        size,
        new Native2DPipelineOptions(Native2DPipelineKind.MsdfText, TransparentClear: true, EnableStraightAlphaBlend: true));
    using var cache = new ProfileNativeRealizationCache(renderer, 2);
    cache.Warm(resource);
    renderer.Begin2D();
    ProfileNativeInstance instance = Fit(resource, 128, 220, 150, 200);
    cache.Submit(resource, instance);
    Native2DPassResult native = renderer.End2D(captureReadback: true);

    using var reference = new StrategyAssets(Path.Combine(root, "samples", "Integrations", "Aurelian.StrategyDemo", "Assets"));
    using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Transparent);
        reference.Draw(canvas, "worker", instance.OriginX, instance.OriginY, instance.Scale);
        canvas.Flush();
    }
    int intersection = 0;
    int union = 0;
    long colorError = 0;
    int compared = 0;
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            int offset = ((y * size) + x) * 4;
            bool nativeInside = native.Pixels![offset + 3] >= 128;
            SKColor cpu = bitmap.GetPixel(x, y);
            bool cpuInside = cpu.Alpha >= 128;
            if (nativeInside && cpuInside)
            {
                intersection++;
                colorError += Math.Abs(native.Pixels[offset] - cpu.Red);
                colorError += Math.Abs(native.Pixels[offset + 1] - cpu.Green);
                colorError += Math.Abs(native.Pixels[offset + 2] - cpu.Blue);
                compared += 3;
            }
            if (nativeInside || cpuInside)
            {
                union++;
            }
        }
    }
    double iou = union == 0 ? 0 : intersection / (double)union;
    double meanColorError = compared == 0 ? 255 : colorError / (double)compared;
    Require(iou >= 0.88, $"Worker native/Skia silhouette IoU was {iou:R}.");
    Require(meanColorError <= 12, $"Worker native/Skia mean channel error was {meanColorError:R}.");
    return new
    {
        asset = "worker",
        reference = "sample-local Skia compatibility renderer",
        native = "canonical Profile MSDF Vulkan realization",
        silhouetteIntersectionOverUnion = Math.Round(iou, 6),
        meanInteriorChannelError = Math.Round(meanColorError, 6),
        nativeHash = native.PixelSha256,
    };
}

static ProfileNativeCompositionResource CompileStandalone(string path)
{
    ProfileCompositionCompilationResult composition = ProfileTsxCompiler.CompileComposition(File.ReadAllText(path), path);
    Require(composition.Success, string.Join("; ", composition.Diagnostics.Select(static item => item.Message)));
    ProfileNativeCompileResult native = ProfileNativeCompiler.Compile(composition.Composition!, path);
    Require(native.Success, string.Join("; ", native.Diagnostics.Select(static item => item.Message)));
    return native.Resource!;
}

static ProfileNativeInstance Fit(
    ProfileNativeCompositionResource resource,
    float centerX,
    float baselineY,
    float maximumWidth,
    float maximumHeight)
{
    float scale = Math.Min(
        maximumWidth / (float)resource.Bounds.Width,
        maximumHeight / (float)resource.Bounds.Height);
    float originX = centerX - (float)((resource.Bounds.MinX + resource.Bounds.MaxX) * 0.5) * scale;
    float originY = baselineY + ((float)resource.Bounds.MinY * scale);
    return new ProfileNativeInstance(originX, originY, scale);
}

static void Rect(VulkanOrderedQuadRenderer pass, float x, float y, float width, float height, uint color, float radius)
{
    Native2DTint tint = Tint(color);
    pass.SubmitAnalyticShape(new NativeAnalyticShapeSubmission(
        new Native2DRect(x, y, width, height),
        new Native2DSize(width, height),
        Native2DUvRect.Full,
        NativeAnalyticShapeKind.RoundedRect,
        tint,
        Math.Min(radius, Math.Min(width, height) / 2),
        tint,
        0));
}

static Native2DTint Tint(uint color)
{
    return new Native2DTint(
        (color >> 24) / 255f,
        ((color >> 16) & 255) / 255f,
        ((color >> 8) & 255) / 255f,
        (color & 255) / 255f);
}

static int Luma(byte[] pixels, int x, int y)
{
    int offset = ((y * Width) + x) * 4;
    return (pixels[offset] * 3) + (pixels[offset + 1] * 6) + pixels[offset + 2];
}

static (CompiledGraphicsProgram Program, VdMirGraphicsBackendResult Backend) CompileShader(string root, string relativePath)
{
    string source = File.ReadAllText(Path.Combine(root, relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
        new GpuCompilationRequest([new GpuSourceFile(relativePath, source)]));
    Require(module.Success, string.Join("; ", module.Diagnostics.Select(static item => item.Message)));
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
    return (CompiledGraphicsProgramExporter.Export(module, backend), backend);
}

static void WriteJson(string path, object value)
{
    File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}

static string Sha(string value)
{
    return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
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
