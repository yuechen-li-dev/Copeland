using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Machina;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Rendering.Raster;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Machina.Core.Actions;
using Machina.Core.Assets;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Layout.Geometry;
using Machina.Pipeline;
using Machina.Presentation;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;
using Machina.VectorAssets;
using FontRgba = Machina.Fonts.ReferenceRendering.Rgba32;
using Resolved2DRgbaColor = Aurelian.Rendering.Contracts.Resolved2D.Resolved2DRgbaColor;
using MachinaTextOperation = Machina.Presentation.PositionedTextOperation;

const int Width = 1280;
const int Height = 720;
string root = FindRepositoryRoot();
string output = Path.Combine(root, "artifacts", "aurelian-native-vector-icon-msdf-m5");
Directory.CreateDirectory(output);

long compileAllocationStart = GC.GetAllocatedBytesForCurrentThread();
Stopwatch compilationWatch = Stopwatch.StartNew();
List<object> compileTimings = [];
Dictionary<string, VectorIconMsdfArtifact> artifacts = new(StringComparer.Ordinal);
foreach (VectorIconFixture fixture in VectorIconFixtures.Canonical)
{
    Stopwatch watch = Stopwatch.StartNew();
    VectorIconCompilationResult result = VectorIconMsdfCompiler.CompileSvg(fixture.Source, fixture.Name + ".svg");
    watch.Stop();
    Require(result.Success, fixture.Name + ": " + string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Reason)));
    artifacts.Add(fixture.Name, result.Artifact!);
    compileTimings.Add(new { fixture.Name, milliseconds = watch.Elapsed.TotalMilliseconds });
}
compilationWatch.Stop();
long compileAllocations = GC.GetAllocatedBytesForCurrentThread() - compileAllocationStart;
Stopwatch packWatch = Stopwatch.StartNew();
VectorIconAtlas atlas = VectorIconAtlasPacker.Pack(artifacts.Values.ToArray());
packWatch.Stop();
var vectorResource = new AurelianMsdfVectorAtlasResource(atlas, VectorIconAtlasPacker.ToRgba8(atlas));
VectorIcons icons = new(artifacts);

MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(BuildUi(icons), Width, Height);
MachinaVectorIconPresentationPrimitive[] iconPrimitives = prepared.PresentationFrame.Operations
    .OfType<MachinaVectorIconPresentationPrimitive>()
    .ToArray();
MachinaAnalyticShapePrimitive[] analyticPrimitives = prepared.PresentationFrame.Operations
    .OfType<MachinaAnalyticShapePrimitive>()
    .ToArray();
Require(iconPrimitives.Length >= 17, "The showcase did not lower all semantic icon uses.");
Require(analyticPrimitives.Length >= 4, "The showcase did not lower analytic controls.");
Require(prepared.Lowering.Actions.Count >= 2, "Settings and Play are not real actionable controls.");

(CompiledGraphicsProgram msdfProgram, VdMirGraphicsBackendResult msdfBackend) = CompileShader(root, "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts");
(CompiledGraphicsProgram analyticProgram, VdMirGraphicsBackendResult analyticBackend) = CompileShader(root, "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts");
string fontPath = Path.Combine(root, "tests", "Machina.UI", "Machina.Fonts.Tests", "Fixtures", "Fonts", "CrimsonText-Regular.ttf");
MachinaTextOperation[] msdfTextOperations = prepared.PresentationFrame.Operations
    .OfType<MachinaTextOperation>()
    .Where(static operation => operation.SourceId is "heading" or "settings-msdf-label")
    .ToArray();
Require(msdfTextOperations.Length == 2, "The showcase must contain heading and Settings MSDF text operations.");
FontBundle font = await BuildFontAsync(fontPath, msdfTextOperations.Select(static operation => operation.Text).ToArray());

VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeVectorIconMsdfM5"));
Require(init.Success && init.Plant is not null, string.Join("; ", init.Diagnostics.Select(static item => item.Message)));

Native2DPassResult iconCold;
Native2DPassResult iconWarm;
Native2DPassResult iconWarmRuntime;
Native2DPassResult analyticPass;
int vectorUploads;
int fontUploads;
long adapterAllocations;
int headingQuadCount = 0;
bool disposedVectorCacheRejected = false;
using (AurelianVulkanPlant plant = init.Plant!)
{
    using var msdfRenderer = new VulkanOrderedQuadRenderer(
        plant,
        msdfProgram,
        Width,
        Height,
        new Native2DPipelineOptions(Native2DPipelineKind.MsdfText, TransparentClear: true));
    using var vectorCache = new AurelianMsdfVectorAtlasCache(msdfRenderer);
    using var fontCache = new AurelianMsdfAtlasCache(msdfRenderer);
    long adapterStart = GC.GetAllocatedBytesForCurrentThread();
    NativeMsdfQuadSubmission[] iconSubmissions = iconPrimitives
        .Select(primitive => AurelianMsdfVectorIconAdapter.Adapt(primitive, vectorResource, vectorCache))
        .ToArray();
    adapterAllocations = GC.GetAllocatedBytesForCurrentThread() - adapterStart;
    NativeMsdfQuadSubmission[] headingSubmissions = (await Task.WhenAll(msdfTextOperations.Select(async operation =>
    {
        DistanceFieldTextLayoutResult layout = await LayoutHeadingAsync(font, operation);
        var qualified = new MachinaTextOperation(
            operation.SourceId,
            operation.Rect,
            operation.Text,
            operation.Style,
            operation.Color,
            new MachinaTextPresentationPrimitive(layout.GlyphRun, font.Identity, MachinaTextRenderingMode.Msdf));
        return AurelianMsdfTextPresentationAdapter.Adapt(qualified, font.Resource, fontCache);
    })))
        .SelectMany(static submissions => submissions)
        .ToArray();
    headingQuadCount = headingSubmissions.Length;

    iconCold = RenderMsdf(msdfRenderer, iconSubmissions, headingSubmissions, true);
    int uploadsAfterCold = vectorCache.UploadCount + fontCache.UploadCount;
    iconWarm = RenderMsdf(msdfRenderer, iconSubmissions, headingSubmissions, true);
    Require(vectorCache.UploadCount + fontCache.UploadCount == uploadsAfterCold, "Warm frame uploaded an atlas.");
    Require(iconWarm.Metrics.DescriptorWrites == 0, "Warm frame rewrote descriptors.");
    iconWarmRuntime = RenderMsdf(msdfRenderer, iconSubmissions, headingSubmissions, false);
    Require(iconWarmRuntime.Pixels is null, "The runtime warm pass unexpectedly performed a readback.");
    Require(iconWarmRuntime.Metrics.DescriptorWrites == 0, "The runtime warm pass rewrote descriptors.");
    vectorUploads = vectorCache.UploadCount;
    fontUploads = fontCache.UploadCount;

    vectorCache.Dispose();
    try
    {
        vectorCache.Resolve(vectorResource);
    }
    catch (ObjectDisposedException)
    {
        disposedVectorCacheRejected = true;
    }
    Require(disposedVectorCacheRejected, "A disposed vector atlas cache accepted a resource.");

    using var analyticRenderer = new VulkanOrderedQuadRenderer(
        plant,
        analyticProgram,
        Width,
        Height,
        new Native2DPipelineOptions(Native2DPipelineKind.AnalyticShape2D, TransparentClear: true));
    analyticRenderer.Begin2D();
    foreach (MachinaAnalyticShapePrimitive primitive in analyticPrimitives)
    {
        NativeAnalyticShapeSubmission? submission = AurelianAnalyticShapePresentationAdapter.Adapt(primitive);
        if (submission.HasValue)
        {
            analyticRenderer.SubmitAnalyticShape(submission.Value);
        }
    }
    analyticPass = analyticRenderer.End2D(captureReadback: true);
}

Require(iconWarm.Pixels is not null && analyticPass.Pixels is not null, "Native readback was unavailable.");
Require(iconWarm.Metrics.QuadCount == iconPrimitives.Length + headingQuadCount, "Every icon must be one quad; only heading glyphs may add quads.");
Require(iconWarm.PixelSha256 == iconCold.PixelSha256, "Repeated native MSDF frame changed hash.");

List<object> directParity = [];
foreach ((string name, VectorIconMsdfArtifact artifact) in artifacts)
{
    foreach (int size in new[] { 16, 24, 32, 64, 128 })
    {
        VectorIconParityMetrics metric = VectorIconCpuQualification.Compare(artifact, size);
        Require(metric.IntersectionOverUnion >= 0.72, $"{name} {size}px CPU vector/MSDF IoU {metric.IntersectionOverUnion:R}");
        Require(metric.MeanEdgeDistance <= 1.5, $"{name} {size}px mean edge distance {metric.MeanEdgeDistance:R}");
        directParity.Add(new { name, metric.Size, metric.IntersectionOverUnion, metric.MeanEdgeDistance, metric.MaximumEdgeDistance });
    }
}

List<object> gpuParity = [];
foreach (MachinaVectorIconPresentationPrimitive primitive in iconPrimitives)
{
    int size = (int)Math.Round(primitive.DestinationRect.Width);
    if (primitive.DestinationRect.Width != primitive.DestinationRect.Height || size <= 0)
    {
        continue;
    }
    bool[] cpu = VectorIconCpuQualification.RenderMsdf(artifacts.Values.Single(artifact => artifact.Identity == primitive.Icon), size);
    bool[] gpu = CropAlpha(iconWarm.Pixels!, Width, Height, primitive.DestinationRect, size);
    double iou = IoU(cpu, gpu);
    Require(iou >= 0.72, $"GPU parity for {primitive.SourceId} was {iou:R}.");
    gpuParity.Add(new { primitive.SourceId, size, iou, gpuHash = Hash(CropRgba(iconWarm.Pixels!, Width, Height, primitive.DestinationRect, size)) });
}

byte[] screenshot = Fill(Width, Height, 0x10, 0x20, 0x40, 0xFF);
Composite(screenshot, analyticPass.Pixels!);
Composite(screenshot, RenderRasterText(
    prepared.PresentationFrame,
    msdfTextOperations.Select(static operation => operation.SourceId).ToHashSet(StringComparer.Ordinal)));
Composite(screenshot, iconWarm.Pixels!);
string screenshotPath = Path.Combine(output, "showcase-1280x720.png");
WritePng(screenshotPath, Width, Height, screenshot);
byte[] screenshot2x = ScaleNearest(screenshot, Width, Height, 2);
string screenshot2xPath = Path.Combine(output, "showcase-2560x1440.png");
WritePng(screenshot2xPath, Width * 2, Height * 2, screenshot2x);

WriteJson(Path.Combine(output, "icons.json"), artifacts.Select(pair => new
{
    name = pair.Key,
    contours = pair.Value.Shape.Contours.Count,
    segments = pair.Value.Shape.Contours.Sum(static contour => contour.Segments.Count),
    bounds = pair.Value.PlaneBounds,
    pair.Value.Width,
    pair.Value.Height,
    pair.Value.Provenance.SourceHash,
    normalizedHash = pair.Value.Shape.NormalizedGeometryHash,
    pair.Value.FieldHash,
    atlasEntryHash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(atlas.Entries[pair.Value.Identity]))),
    provenance = VectorIconFixtures.Canonical.Single(fixture => fixture.Name == pair.Key).Provenance,
}));
WriteJson(Path.Combine(output, "atlas.json"), new
{
    atlas.Identity,
    atlas.Width,
    atlas.Height,
    atlas.Padding,
    orientation = atlas.RowOrder.ToString(),
    atlas.AtlasHash,
    iconCount = atlas.Entries.Count,
    occupancy = atlas.Entries.Values.Sum(static entry => entry.Width * entry.Height) / (double)(atlas.Width * atlas.Height),
    entries = atlas.Entries.Values,
});
int nonFiniteFieldValues = artifacts.Values.Sum(static artifact => artifact.FieldPixels.Span.ToArray().Count(static value => !float.IsFinite(value)));
Require(nonFiniteFieldValues == 0, "Compiled vector fields contained non-finite samples.");
WriteJson(Path.Combine(output, "parity.json"), new
{
    directVectorVsCpuMsdfThreshold = new { minimumIoU = 0.72, meanEdgeDistancePixels = 1.5 },
    cpuMsdfVsGpuMsdfThreshold = new { minimumIoU = 0.72 },
    directVectorVsCpuMsdf = directParity,
    cpuMsdfVsGpuMsdf = gpuParity,
    nonFiniteFieldValues,
});
WriteJson(Path.Combine(output, "rendering.json"), new
{
    iconPrimitiveCount = iconPrimitives.Length,
    oneQuadPerIcon = true,
    verticesPerIcon = 6,
    cold = iconCold.Metrics,
    warm = iconWarmRuntime.Metrics,
    warmWithReadback = iconWarm.Metrics,
    vectorAtlasUploads = vectorUploads,
    fontAtlasUploads = fontUploads,
    warmAtlasUploads = 0,
    warmDescriptorWrites = iconWarm.Metrics.DescriptorWrites,
    analytic = analyticPass.Metrics,
    msdfShader = new { msdfProgram.VdMirSha256, msdfBackend.HlslSha256, vertex = msdfBackend.Vertex.SpirvSha256, pixel = msdfBackend.Pixel.SpirvSha256 },
    analyticShader = new { analyticProgram.VdMirSha256, analyticBackend.HlslSha256 },
    iconWarm.PixelSha256,
    screenshots = new[] { Path.GetFileName(screenshotPath), Path.GetFileName(screenshot2xPath) },
});
WriteJson(Path.Combine(output, "proof.json"), new
{
    sourceParseMilliseconds = compileTimings,
    totalCompilationMilliseconds = compilationWatch.Elapsed.TotalMilliseconds,
    atlasPackMilliseconds = packWatch.Elapsed.TotalMilliseconds,
    compileAllocations,
    adapterAllocations,
    perFrameVectorCompilation = 0,
    perFrameMsdfGeneration = 0,
    perFrameAtlasPacking = 0,
    disposedVectorCacheRejected,
    bitmapIntermediateUsed = false,
    cpuTessellationUsed = false,
    nativeValidationErrors = init.Diagnostics.Count(message => message.Severity == VulkanInitDiagnosticSeverity.Error),
    semanticIconCount = iconPrimitives.Length,
    realActions = prepared.Lowering.Actions.Keys.Select(static id => id.Value),
    rasterTextOperations = prepared.PresentationFrame.Operations.OfType<MachinaTextOperation>().Count() - msdfTextOperations.Length,
    msdfTextOperations = msdfTextOperations.Length,
});
WriteJson(Path.Combine(output, "manifest.json"), new
{
    milestone = "AURELIAN-NATIVE-VECTOR-ICON-MSDF-M5",
    kind = "compiled-static-vector-icon-msdf-native-ui",
    boundedSvgSubset = true,
    bitmapIntermediateUsed = false,
    cpuTessellationUsed = false,
    directVectorMsdfUsed = true,
    singleColorTintedIcons = true,
    msdfShaderReused = true,
    oneQuadPerIcon = true,
    machinaOwnsIconSemantics = true,
    aurelianGraphicsOwnsGpuOnly = true,
    warmAtlasUploads = 0,
    fullSvgImplemented = false,
    svgTextImplemented = false,
    svgFiltersImplemented = false,
    gradientsImplemented = false,
    gpuMsdfGenerationImplemented = false,
});

Console.WriteLine(JsonSerializer.Serialize(new
{
    outcome = "A",
    icons = artifacts.Count,
    semanticUses = iconPrimitives.Length,
    atlas = atlas.Identity,
    gpuHash = iconWarm.PixelSha256,
    validationErrors = init.Diagnostics.Count(message => message.Severity == VulkanInitDiagnosticSeverity.Error),
}, JsonOptions()));

static UiNode BuildUi(VectorIcons icons)
{
    ColorToken white = ColorToken.Hex(0xEEF8FFFF);
    ColorToken cyan = ColorToken.Hex(0x39D9FFFF);
    UiStyle button = new(Background: ColorToken.Hex(0x0E7490FF), Foreground: white, BorderColor: cyan, BorderThickness: 1, Shape: UiShapeKind.RoundedRect, CornerRadius: 10);
    List<UiNode> children =
    [
        UI.At(UI.Text("VECTOR-NATIVE UI", id: "heading", color: cyan, size: TextSize.H1), x: 44, y: 24, width: 500, height: 40),
        UI.At(UI.Button("Settings", id: "settings-button", action: UiAction.Named("settings"), style: button), x: 36, y: 96, width: 190, height: 56),
        UI.At(UI.Icon(icons.Settings, 24, id: "settings-icon"), x: 52, y: 112, width: 24, height: 24),
        UI.At(UI.Text("Settings", id: "settings-msdf-label", color: white), x: 88, y: 108, width: 120, height: 32),
        UI.At(UI.Button("Play", id: "play-button", action: UiAction.Named("play"), style: button), x: 250, y: 96, width: 190, height: 56),
        UI.At(UI.Icon(icons.Play, 24, id: "play-icon"), x: 266, y: 112, width: 24, height: 24),
        UI.At(UI.Text("Play", id: "play-raster-label", color: white), x: 302, y: 112, width: 100, height: 24),
        UI.At(UI.Rect(id: "active-pill", style: new UiStyle(Background: ColorToken.Hex(0x1F8A70FF), Shape: UiShapeKind.Pill)), x: 36, y: 176, width: 190, height: 48),
        UI.At(UI.Icon(icons.Check, 24, id: "active-check"), x: 52, y: 188, width: 24, height: 24),
        UI.At(UI.Text("Active", id: "active-label", color: white), x: 88, y: 188, width: 100, height: 24),
        UI.At(UI.Rect(id: "info-row", style: new UiStyle(Background: ColorToken.Hex(0x132F4CFF), Shape: UiShapeKind.RoundedRect, CornerRadius: 10)), x: 250, y: 176, width: 360, height: 48),
        UI.At(UI.Icon(icons.InfoCircle, 24, id: "info-icon"), x: 266, y: 188, width: 24, height: 24),
        UI.At(UI.Text("Vector assets compiled", id: "info-label", color: white), x: 302, y: 188, width: 260, height: 24),
        UI.At(UI.Text("CANONICAL CORPUS", id: "corpus-label", color: cyan), x: 44, y: 260, width: 300, height: 24),
    ];
    MachinaVectorIconId[] corpus = [icons.Settings, icons.Play, icons.Pause, icons.Check, icons.Close, icons.Heart, icons.InfoCircle, icons.Folder];
    for (int index = 0; index < corpus.Length; index++)
    {
        children.Add(UI.At(UI.Icon(corpus[index], 64, id: $"corpus-{index}"), x: 44 + (index * 112), y: 304, width: 64, height: 64));
    }
    children.Add(UI.At(UI.Text("16   24   32        64                 128 px — same compiled icon", id: "sizes-label", color: white), x: 44, y: 424, width: 760, height: 24));
    int[] sizes = [16, 24, 32, 64, 128];
    int x = 44;
    for (int index = 0; index < sizes.Length; index++)
    {
        int size = sizes[index];
        children.Add(UI.At(UI.Icon(icons.Settings, size, id: $"size-{size}"), x: x, y: 464, width: size, height: size));
        x += size + 40;
    }
    children.Add(UI.At(UI.Rect(id: "mixed-control", style: button), x: 760, y: 464, width: 360, height: 80));
    children.Add(UI.At(UI.Icon(icons.Settings, 32, id: "mixed-icon"), x: 784, y: 488, width: 32, height: 32));
    children.Add(UI.At(UI.Text("MSDF icon + raster label", id: "mixed-raster-label", color: white), x: 832, y: 490, width: 260, height: 28));
    children.Add(UI.At(UI.Rect(id: "circle-proof", style: new UiStyle(Background: ColorToken.Hex(0x7C3AEDFF), Shape: UiShapeKind.Circle)), x: 1160, y: 40, width: 48, height: 48));
    return UI.Surface(id: "m5-showcase", width: Width, height: Height, children: children);
}

static Native2DPassResult RenderMsdf(
    VulkanOrderedQuadRenderer renderer,
    IReadOnlyList<NativeMsdfQuadSubmission> icons,
    IReadOnlyList<NativeMsdfQuadSubmission> text,
    bool capture)
{
    renderer.Begin2D();
    foreach (NativeMsdfQuadSubmission submission in icons)
    {
        renderer.SubmitMsdfQuad(submission);
    }
    foreach (NativeMsdfQuadSubmission submission in text)
    {
        renderer.SubmitMsdfQuad(submission);
    }
    return renderer.End2D(capture);
}

static async Task<FontBundle> BuildFontAsync(string path, IReadOnlyList<string> texts)
{
    FontFaceId face = new("CrimsonText-Regular");
    var source = new TypographyGlyphOutlineSource(new Dictionary<FontFaceId, TypographyFontFaceSource> { [face] = new(face, path, 0) });
    var pipeline = new GlyphGenerationPipeline(source, new MsdfSharpDistanceFieldGenerator());
    var settings = new MsdfGenerationSettings(DistanceFieldKind.Msdf, 32, 32, 4, 1, "simple", 2);
    var outline = new GlyphOutlineLoadOptions(32, 0, GlyphHintingMode.None, normalizeToEm: true);
    GlyphKey[] keys = texts
        .SelectMany(text => DistanceFieldTextRun.Create(text, face, 32, MachinaFontWeight.Regular, MachinaFontSlant.Upright).GlyphKeys)
        .Distinct()
        .ToArray();
    List<GeneratedGlyphDistanceField> fields = [];
    Dictionary<GlyphKey, GlyphMetrics> metrics = [];
    foreach (GlyphKey key in keys)
    {
        GlyphGenerationResult result = await pipeline.GenerateAsync(key, outline, settings);
        if (result.Metrics is not null)
        {
            metrics[key] = result.Metrics;
        }
        if (!Rune.IsWhiteSpace(new Rune(key.Codepoint)))
        {
            Require(result.Success && result.DistanceField is not null, $"Heading glyph U+{key.Codepoint:X4} failed.");
            fields.Add(result.DistanceField!);
        }
    }
    GeneratedFieldAtlasPackResult packed = new GeneratedFieldAtlasPacker().Pack(fields, new GeneratedFieldAtlasPackOptions(512, 256, 2, "m5-heading"));
    Require(packed.Success, string.Join("; ", packed.Diagnostics.Select(static item => item.Message)));
    Dictionary<int, byte[]> pages = packed.Pages.ToDictionary(static page => page.Index, EncodeRgba8);
    string hash = Hash(pages.OrderBy(static pair => pair.Key).SelectMany(static pair => pair.Value).ToArray());
    MachinaFontAtlasId identity = new("m5-heading-sha256-" + hash);
    var resource = new AurelianMsdfAtlasResource(identity, packed.Snapshot, pages, AurelianMsdfAtlasRowOrder.TopToBottom);
    return new FontBundle(face, source, metrics, identity, resource, fields.Count);
}

static async Task<DistanceFieldTextLayoutResult> LayoutHeadingAsync(FontBundle font, MachinaTextOperation operation)
{
    DistanceFieldTextRun run = DistanceFieldTextRun.Create(operation.Text, font.Face, 32, MachinaFontWeight.Regular, MachinaFontSlant.Upright);
    return DistanceFieldTextLayout.Layout(run, font.Metrics, new DistanceFieldTextRenderOptions(
        Width, Height, font.Face, 32, MachinaFontWeight.Regular, MachinaFontSlant.Upright,
        DistanceFieldKind.Msdf, 32, 32, 4, FontRgba.White, FontRgba.Transparent,
        operation.Rect.X, operation.Rect.Y + 28,
        ShowBaselineGuide: false,
        FlipY: false), []);
}

static byte[] EncodeRgba8(GeneratedFieldAtlasPage page)
{
    byte[] result = new byte[page.Width * page.Height * 4];
    for (int pixel = 0; pixel < page.Width * page.Height; pixel++)
    {
        result[pixel * 4] = Quantize(page.Data[pixel * page.ChannelCount]);
        result[(pixel * 4) + 1] = Quantize(page.Data[(pixel * page.ChannelCount) + 1]);
        result[(pixel * 4) + 2] = Quantize(page.Data[(pixel * page.ChannelCount) + 2]);
        result[(pixel * 4) + 3] = 255;
    }
    return result;
}

static byte Quantize(float value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

static (CompiledGraphicsProgram, VdMirGraphicsBackendResult) CompileShader(string root, string sourceName)
{
    string source = File.ReadAllText(Path.Combine(root, sourceName.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n");
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
    Require(module.Success, string.Join("; ", module.Diagnostics.Select(static item => item.Message)));
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
    return (CompiledGraphicsProgramExporter.Export(module, backend), backend);
}

static byte[] RenderRasterText(MachinaPresentationFrame frame, IReadOnlySet<string> excluded)
{
    MachinaTextOperation[] text = frame.Operations
        .OfType<MachinaTextOperation>()
        .Where(operation => !excluded.Contains(operation.SourceId))
        .ToArray();
    RasterFrame raster = new AurelianCpuRasterRenderer().Render(MachinaPresentationTranslator.Translate(new MachinaPresentationFrame(frame.Viewport, text)));
    byte[] result = new byte[Width * Height * 4];
    for (int index = 0; index < raster.Surface.Pixels.Count; index++)
    {
        Resolved2DRgbaColor pixel = raster.Surface.Pixels[index];
        result[index * 4] = pixel.R;
        result[(index * 4) + 1] = pixel.G;
        result[(index * 4) + 2] = pixel.B;
        result[(index * 4) + 3] = pixel.A;
    }
    return result;
}

static bool[] CropAlpha(byte[] source, int width, int height, Rect rect, int size)
{
    bool[] result = new bool[size * size];
    int left = (int)Math.Round(rect.X);
    int top = (int)Math.Round(rect.Y);
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            int sx = left + x;
            int sy = top + y;
            result[(y * size) + x] = sx >= 0 && sx < width && sy >= 0 && sy < height && source[((sy * width) + sx) * 4 + 3] >= 128;
        }
    }
    return result;
}

static byte[] CropRgba(byte[] source, int width, int height, Rect rect, int size)
{
    byte[] result = new byte[size * size * 4];
    int left = (int)Math.Round(rect.X);
    int top = (int)Math.Round(rect.Y);
    for (int y = 0; y < size; y++)
    {
        int sourceOffset = (((top + y) * width) + left) * 4;
        source.AsSpan(sourceOffset, size * 4).CopyTo(result.AsSpan(y * size * 4, size * 4));
    }
    return result;
}

static double IoU(bool[] left, bool[] right)
{
    int intersection = 0;
    int union = 0;
    for (int index = 0; index < left.Length; index++)
    {
        intersection += left[index] && right[index] ? 1 : 0;
        union += left[index] || right[index] ? 1 : 0;
    }
    return union == 0 ? 1 : intersection / (double)union;
}

static byte[] Fill(int width, int height, byte r, byte g, byte b, byte a)
{
    byte[] result = new byte[width * height * 4];
    for (int index = 0; index < result.Length; index += 4)
    {
        result[index] = r;
        result[index + 1] = g;
        result[index + 2] = b;
        result[index + 3] = a;
    }
    return result;
}

static void Composite(byte[] destination, byte[] source)
{
    for (int index = 0; index < destination.Length; index += 4)
    {
        int alpha = source[index + 3];
        if (alpha == 0)
        {
            continue;
        }
        int inverse = 255 - alpha;
        destination[index] = (byte)(((source[index] * alpha) + (destination[index] * inverse) + 127) / 255);
        destination[index + 1] = (byte)(((source[index + 1] * alpha) + (destination[index + 1] * inverse) + 127) / 255);
        destination[index + 2] = (byte)(((source[index + 2] * alpha) + (destination[index + 2] * inverse) + 127) / 255);
        destination[index + 3] = 255;
    }
}

static byte[] ScaleNearest(byte[] source, int width, int height, int scale)
{
    byte[] result = new byte[width * scale * height * scale * 4];
    int targetWidth = width * scale;
    for (int y = 0; y < height * scale; y++)
    {
        for (int x = 0; x < targetWidth; x++)
        {
            int sourceIndex = (((y / scale) * width) + (x / scale)) * 4;
            int targetIndex = ((y * targetWidth) + x) * 4;
            source.AsSpan(sourceIndex, 4).CopyTo(result.AsSpan(targetIndex, 4));
        }
    }
    return result;
}

static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions()) + Environment.NewLine);

static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

static void WritePng(string path, int width, int height, byte[] rgba)
{
    using FileStream stream = File.Create(path);
    stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
    byte[] header = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
    header[8] = 8;
    header[9] = 6;
    WriteChunk(stream, "IHDR", header);
    using MemoryStream compressed = new();
    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
    {
        for (int y = 0; y < height; y++)
        {
            zlib.WriteByte(0);
            zlib.Write(rgba, y * width * 4, width * 4);
        }
    }
    WriteChunk(stream, "IDAT", compressed.ToArray());
    WriteChunk(stream, "IEND", []);
}

static void WriteChunk(Stream stream, string type, byte[] data)
{
    byte[] typeBytes = Encoding.ASCII.GetBytes(type);
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
    stream.Write(length);
    stream.Write(typeBytes);
    stream.Write(data);
    byte[] crcInput = [.. typeBytes, .. data];
    Span<byte> crc = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
    stream.Write(crc);
}

static uint Crc32(ReadOnlySpan<byte> bytes)
{
    uint crc = 0xFFFFFFFF;
    foreach (byte value in bytes)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
    }
    return ~crc;
}

static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "Copeland.slnx")))
    {
        current = current.Parent;
    }
    return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed record FontBundle(
    FontFaceId Face,
    TypographyGlyphOutlineSource Source,
    IReadOnlyDictionary<GlyphKey, GlyphMetrics> Metrics,
    MachinaFontAtlasId Identity,
    AurelianMsdfAtlasResource Resource,
    int GlyphCount);
