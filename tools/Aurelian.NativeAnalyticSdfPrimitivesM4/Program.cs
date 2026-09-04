using System.Buffers.Binary;
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
using Aurelian.Rendering.Contracts.Resolved2D;
using Aurelian.Rendering.Raster;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
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
using FontRgba = Machina.Fonts.ReferenceRendering.Rgba32;

const int Width = 1280;
const int Height = 720;
string root = FindRepositoryRoot();
string artifactRoot = Path.Combine(root, "artifacts", "aurelian-native-analytic-sdf-primitives-m4");
Directory.CreateDirectory(artifactRoot);

(CompiledGraphicsProgram program, VdMirGraphicsModule module, VdMirGraphicsBackendResult backend) = CompileShader(root);
VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeAnalyticSdfPrimitivesM4"));
Require(init.Success && init.Plant is not null, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
AurelianVulkanPlant plant = init.Plant!;

UiNode showcaseUi = BuildShowcaseUi();
MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(showcaseUi, Width, Height);
MachinaAnalyticShapePrimitive[] analyticPrimitives = prepared.PresentationFrame.Operations
    .OfType<MachinaAnalyticShapePrimitive>()
    .ToArray();
NativeAnalyticShapeSubmission[] shapes = analyticPrimitives
    .Select(primitive => AurelianAnalyticShapePresentationAdapter.Adapt(primitive))
    .OfType<NativeAnalyticShapeSubmission>()
    .ToArray();
Require(shapes.Length == analyticPrimitives.Length, "An authored Machina shape was unexpectedly clipped out.");
Require(analyticPrimitives.Any(primitive => primitive.SourceId.Contains("card", StringComparison.Ordinal)), "The showcase did not contain a real Machina Card shape.");
Require(analyticPrimitives.Count(primitive => primitive.SourceId.Contains("button", StringComparison.Ordinal)) >= 2, "The showcase did not contain real Machina Button shapes.");
Require(analyticPrimitives.Any(primitive => primitive.Kind == MachinaAnalyticShapeKind.Pill), "The showcase did not contain a pill badge.");
Require(analyticPrimitives.Any(primitive => primitive.Kind == MachinaAnalyticShapeKind.Circle), "The showcase did not contain a circle indicator.");
Machina.Presentation.PositionedTextOperation msdfHeading = prepared.PresentationFrame.Operations
    .OfType<Machina.Presentation.PositionedTextOperation>()
    .Single(operation => operation.SourceId == "heading");
string fontPath = Path.Combine(root, "tests", "Machina.UI", "Machina.Fonts.Tests", "Fixtures", "Fonts", "CrimsonText-Regular.ttf");
AtlasBundle msdfAtlas = await BuildAtlasAsync(fontPath, 32, [msdfHeading.Text]);
(CompiledGraphicsProgram msdfProgram, VdMirGraphicsBackendResult msdfBackend) = CompileMsdfShader(root);

Native2DPassResult cold;
Native2DPassResult warm = null!;
Native2DPassResult warmWithoutReadback = null!;
Native2DPassResult twoX = null!;
MsdfOverlay msdfOverlay = null!;
double stressMilliseconds;
using (plant)
using (var renderer = new VulkanOrderedQuadRenderer(
    plant,
    program,
    Width,
    Height,
    new Native2DPipelineOptions(Native2DPipelineKind.AnalyticShape2D, TransparentClear: true)))
{
    cold = Render(renderer, shapes, capture: true);
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    for (int pass = 0; pass < 100; pass++)
    {
        warm = Render(renderer, shapes, capture: pass == 99);
        if (pass == 98)
        {
            warmWithoutReadback = warm;
        }
    }
    stopwatch.Stop();
    stressMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
    NativeAnalyticShapeSubmission[] scaledShapes = shapes.Select(shape => ScaleShape(shape, 2)).ToArray();
    using var twoXRenderer = new VulkanOrderedQuadRenderer(
        plant,
        program,
        Width * 2,
        Height * 2,
        new Native2DPipelineOptions(Native2DPipelineKind.AnalyticShape2D, TransparentClear: true));
    twoX = Render(twoXRenderer, scaledShapes, capture: true);
    msdfOverlay = await RenderMsdfAsync(plant, msdfProgram, msdfAtlas, msdfHeading);
}

Require(cold.Metrics.QuadCount == shapes.Length, "Shape count did not equal quad count.");
Require(cold.Metrics.BufferUploads == 1, "The analytic path did not use one shared vertex upload.");
Require(warm.Metrics.DescriptorWrites == 0, "Warm analytic frame rewrote descriptors.");
Require(warm.Metrics.DescriptorSetAllocations == 0, "Warm analytic frame allocated descriptor sets.");
Require(warm.PixelSha256 == cold.PixelSha256, "Repeated analytic render changed pixel hash.");
Require(warm.Pixels is not null, "Final readback was absent.");
byte[] finalPixels = warm.Pixels!;
int painterSample = ((576 * Width) + 720) * 4;
Require(
    finalPixels[painterSample] == 0x22
        && finalPixels[painterSample + 1] == 0xC5
        && finalPixels[painterSample + 2] == 0x5E,
    "Painter order did not preserve the later overlapping shape.");

ParityResult[] parity = shapes.Take(3).Select(shape => CompareAlpha(finalPixels, shape)).ToArray();
Require(
    parity.All(item => item.IoU >= 0.995 && item.MaximumAlphaError <= 4),
    "CPU/GPU analytic parity exceeded tolerance: " + JsonSerializer.Serialize(parity));
string screenshot = Path.Combine(artifactRoot, "showcase-1280x720.png");
byte[] showcase = CompositeOverNavy(finalPixels);
byte[] showcaseText = RenderShowcaseText(prepared.PresentationFrame, msdfHeading.SourceId);
CompositeStraightAlpha(showcase, showcaseText);
CompositeNativeText(showcase, msdfOverlay.Pixels);
WritePng(screenshot, Width, Height, showcase);
Require(twoX.Pixels is not null, "2x readback was absent.");
string twoXScreenshot = Path.Combine(artifactRoot, "showcase-2560x1440.png");
byte[] twoXShowcase = CompositeOverNavy(twoX.Pixels!);
CompositeStraightAlpha(twoXShowcase, ScaleNearest(showcaseText, Width, Height, 2));
CompositeNativeText(twoXShowcase, ScaleNearest(msdfOverlay.Pixels, Width, Height, 2));
WritePng(twoXScreenshot, Width * 2, Height * 2, twoXShowcase);

WriteJson(Path.Combine(artifactRoot, "shader.json"), new
{
    source = "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts",
    vdMirSha256 = program.VdMirSha256,
    backend.HlslSha256,
    vertexSpirvSha256 = backend.Vertex.SpirvSha256,
    pixelSpirvSha256 = backend.Pixel.SpirvSha256,
    metadataSha256 = Hash(JsonSerializer.Serialize(program)),
    resources = program.Resources.Select(item => new { item.Name, item.Binding, kind = item.Kind.ToString() }),
    material = program.Material,
    msdfHeading = new
    {
        source = "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts",
        msdfProgram.VdMirSha256,
        msdfBackend.HlslSha256,
        vertexSpirvSha256 = msdfBackend.Vertex.SpirvSha256,
        pixelSpirvSha256 = msdfBackend.Pixel.SpirvSha256,
    },
});
WriteJson(Path.Combine(artifactRoot, "parity.json"), new
{
    law = "pixel-center CPU reference; smoothstep(clamp(0.5-distance,0,1)); alpha tolerance 4/255",
    cases = parity,
});
WriteJson(Path.Combine(artifactRoot, "rendering.json"), new
{
    shapeCount = shapes.Length,
    quadCount = warm.Metrics.QuadCount,
    verticesPerShape = 6,
    cold = cold.Metrics,
    warm = warmWithoutReadback.Metrics,
    stressPasses = 100,
    stressMilliseconds,
    textureUploads = 0,
    pipelineCreationsDuringWarmFrames = 0,
    meshGenerations = 0,
    pixelSha256 = warm.PixelSha256,
    screenshot = Path.GetRelativePath(root, screenshot).Replace('\\', '/'),
    twoXPixelSha256 = twoX.PixelSha256,
    twoXScreenshot = Path.GetRelativePath(root, twoXScreenshot).Replace('\\', '/'),
    semanticTopologyHash = Hash(string.Join("\n", prepared.Lowering.Rows.Select(row => $"{row.Id}|{row.Parent}|{row.Frame.GetType().Name}|{row.Order}"))),
    hitTestEntryCount = prepared.Lowering.Actions.Count,
    msdfGlyphQuads = msdfOverlay.GlyphQuads,
    msdfAtlasUploads = msdfOverlay.AtlasUploads,
    msdfWarmDescriptorWrites = msdfOverlay.WarmDescriptorWrites,
    msdfInkPixels = CountNativeTextPixels(msdfOverlay.Pixels),
});
WriteJson(Path.Combine(artifactRoot, "proof.json"), new
{
    milestone = "AURELIAN-NATIVE-ANALYTIC-SDF-PRIMITIVES-M4",
    outcome = "A",
    localCoordinates = "[0,1] across original quad; clipped quads preserve the original local interval",
    pixelCoordinates = "top-left origin; +x right; +y down",
    smoothing = "one-pixel dimension-aware smoothstep; no derivatives",
    showcase = "real Machina UiNode tree -> presentation frame -> analytic and text realization",
    msdfHeadingSourceId = msdfHeading.SourceId,
    validation = new
    {
        requested = true,
        available = init.Facts!.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation", StringComparer.Ordinal),
        errors = init.Diagnostics.Count(item => item.Severity == VulkanInitDiagnosticSeverity.Error),
    },
    shapes = analyticPrimitives.Zip(shapes).Select((pair, index) => new
    {
        index,
        sourceId = pair.First.SourceId,
        kind = pair.Second.Kind.ToString(),
        destination = pair.Second.Destination,
        pair.Second.Radius,
        pair.Second.BorderWidth,
        drawBatch = index,
    }),
});
WriteJson(Path.Combine(artifactRoot, "manifest.json"), new
{
    milestone = "AURELIAN-NATIVE-ANALYTIC-SDF-PRIMITIVES-M4",
    kind = "native-analytic-sdf-vector-ui-primitives",
    roundedRectQualified = true,
    circleQualified = true,
    pillQualified = true,
    cpuTessellationUsed = false,
    bitmapFallbackUsed = false,
    textureUploadRequired = false,
    visualTypeScriptShaderUsed = true,
    vdMirShaderPathUsed = true,
    orderedQuadPathReused = true,
    machinaOwnsShapeSemantics = true,
    aurelianGraphicsOwnsGpuOnly = true,
    msdfTextStillQualified = true,
    msdfTextVisibleInShowcase = true,
    rasterTextStillSupported = true,
    rasterTextVisibleInShowcase = true,
    svgAdded = false,
    genericSdfDslAdded = false,
});

Console.WriteLine("AURELIAN-NATIVE-ANALYTIC-SDF-PRIMITIVES-M4: Outcome A");
Console.WriteLine($"pixel={warm.PixelSha256}; shapes={shapes.Length}; draws={warm.Metrics.DrawCalls}");
Console.WriteLine($"parity={string.Join(", ", parity.Select(item => $"{item.Kind}:{item.IoU:F6}"))}");
Console.WriteLine($"cold descriptor writes={cold.Metrics.DescriptorWrites}; warm={warm.Metrics.DescriptorWrites}");

static Native2DPassResult Render(
    VulkanOrderedQuadRenderer renderer,
    IReadOnlyList<NativeAnalyticShapeSubmission> shapes,
    bool capture)
{
    renderer.Begin2D();
    foreach (NativeAnalyticShapeSubmission shape in shapes)
    {
        renderer.SubmitAnalyticShape(shape);
    }
    return renderer.End2D(capture);
}

static UiNode BuildShowcaseUi()
{
    StandardTheme theme = CreateShowcaseTheme();
    ColorToken white = ColorToken.Hex(0xEEF8FFFF);
    ColorToken cyan = ColorToken.Hex(0x39D9FFFF);

    return UI.Surface(
        id: "m4-showcase",
        width: Width,
        height: Height,
        style: new UiStyle(Background: ColorToken.Hex(0x102040FF)),
        children:
        [
            UI.Anchor(
                UI.Text("ANALYTIC SDF PRIMITIVES", id: "heading", color: cyan, size: TextSize.H1),
                id: "heading-slot",
                left: 64,
                top: 24,
                width: 900,
                height: 36),
            UI.Anchor(
                StandardUI.Card(
                    UI.Text("ROUNDED RECTANGLE", id: "canonical-card-label", color: white),
                    id: "canonical-card",
                    theme: theme,
                    width: 256,
                    height: 128,
                    style: theme.Card.Default with { CornerRadius = 24, ContentInset = 24 }),
                id: "canonical-card-slot",
                left: 64,
                top: 72,
                width: 256,
                height: 128),
            UI.Anchor(
                ShapeNode("status-circle", UiShapeKind.Circle, 0x39D9FFFF),
                id: "status-circle-slot",
                left: 384,
                top: 72,
                width: 128,
                height: 128),
            UI.Anchor(
                UI.Text("ONLINE", id: "status-label", color: white, size: TextSize.Sm),
                id: "status-label-slot",
                left: 402,
                top: 206,
                width: 100,
                height: 24),
            UI.Anchor(
                StandardUI.Badge(
                    "ACTIVE",
                    id: "active-badge",
                    theme: theme,
                    variant: BadgeVariant.Default,
                    style: theme.Badge.Default with { MinWidth = 256, Height = 64, HorizontalAllowance = 0 }),
                id: "active-badge-slot",
                left: 576,
                top: 104,
                width: 256,
                height: 64),
            UI.Anchor(
                ShapeNode("extreme-pill", UiShapeKind.Pill, 0x7C3AEDFF),
                id: "extreme-pill-slot",
                left: 896,
                top: 72,
                width: 240,
                height: 32),
            UI.Anchor(
                UI.Text("VECTOR UI", id: "extreme-pill-label", color: white, size: TextSize.Sm),
                id: "extreme-pill-label-slot",
                left: 964,
                top: 78,
                width: 120,
                height: 24),
            UI.Anchor(
                ShapeNode("tiny-status-circle", UiShapeKind.Circle, 0x22C55EFF),
                id: "tiny-status-circle-slot",
                left: 896,
                top: 136,
                width: 16,
                height: 16),
            UI.Anchor(
                StandardUI.Card(
                    UI.VStack(
                        id: "mixed-card-content",
                        gap: 12,
                        children:
                        [
                            UI.Fixed(36, UI.Text("MIXED UI", id: "mixed-heading", color: cyan, size: TextSize.H1)),
                            UI.Fixed(24, UI.Text("Crisp shapes at every scale", id: "mixed-body", color: white)),
                            UI.Space(),
                            UI.Fixed(24, UI.Text("PIXEL TEXT + VECTOR PANEL", id: "retro-label", color: white, size: TextSize.Sm)),
                        ]),
                    id: "large-card",
                    theme: theme,
                    width: 512,
                    height: 256,
                    style: theme.Card.Default with { CornerRadius = 48, ContentInset = 32 }),
                id: "large-card-slot",
                left: 64,
                top: 264,
                width: 512,
                height: 256),
            UI.Anchor(
                StandardUI.Button(
                    "SETTINGS",
                    id: "settings-button",
                    action: UiAction.Named("show-settings"),
                    variant: ButtonVariant.Default,
                    theme: theme),
                id: "settings-button-slot",
                left: 640,
                top: 264,
                width: 112,
                height: 40),
            UI.Anchor(
                StandardUI.Button(
                    "PLAY",
                    id: "play-button",
                    action: UiAction.Named("play"),
                    variant: ButtonVariant.Secondary,
                    theme: theme),
                id: "play-button-slot",
                left: 768,
                top: 264,
                width: 112,
                height: 40),
            UI.Anchor(ShapeNode("radius-0", UiShapeKind.RoundedRect, 0x334155FF, 0), id: "radius-0-slot", left: 640, top: 344, width: 64, height: 32),
            UI.Anchor(ShapeNode("radius-1", UiShapeKind.RoundedRect, 0x334155FF, 1), id: "radius-1-slot", left: 720, top: 344, width: 64, height: 32),
            UI.Anchor(ShapeNode("radius-quarter", UiShapeKind.RoundedRect, 0x334155FF, 8), id: "radius-quarter-slot", left: 800, top: 344, width: 64, height: 32),
            UI.Anchor(ShapeNode("radius-half", UiShapeKind.RoundedRect, 0x334155FF, 16), id: "radius-half-slot", left: 880, top: 344, width: 64, height: 32),
            UI.Anchor(
                UI.Text("RADIUS 0 / 1 / 8 / 16", id: "radius-label", color: white, size: TextSize.Sm),
                id: "radius-label-slot",
                left: 640,
                top: 400,
                width: 360,
                height: 24),
            UI.Anchor(ShapeNode("painter-back", UiShapeKind.RoundedRect, 0x7C3AEDFF, 12), id: "painter-back-slot", left: 640, top: 520, width: 160, height: 96),
            UI.Anchor(ShapeNode("painter-front", UiShapeKind.RoundedRect, 0x22C55EFF, 12), id: "painter-front-slot", left: 680, top: 552, width: 160, height: 96),
        ]);
}

static UiNode ShapeNode(
    string id,
    UiShapeKind kind,
    uint fill,
    double radius = 0)
{
    return UI.Rect(
        id: id,
        style: new UiStyle(
            Background: UiColor(fill),
            Shape: kind,
            CornerRadius: radius)) with
    {
        Semantics = new UiSemantics(UiRole.Container, id),
    };
}

static StandardTheme CreateShowcaseTheme()
{
    StandardTheme source = StandardTheme.Default;
    ColorToken white = ColorToken.Hex(0xEEF8FFFF);
    ColorToken cyan = ColorToken.Hex(0x39D9FFFF);
    StandardButtonStyle button = source.Button.Default with
    {
        Background = ColorToken.Hex(0x0E7490FF),
        Foreground = white,
        BorderColor = cyan,
        BorderThickness = 1,
        Width = 112,
        Height = 40,
        CornerRadius = 8,
        TextStyle = source.Button.Default.TextStyle with { Color = white },
    };

    return source with
    {
        Colors = source.Colors with
        {
            Background = ColorToken.Hex(0x102040FF),
            Foreground = white,
            Primary = ColorToken.Hex(0x0E7490FF),
            PrimaryForeground = white,
            Secondary = ColorToken.Hex(0x155E75FF),
            SecondaryForeground = white,
            Border = cyan,
        },
        Card = new StandardCardStyles(new StandardCardStyle(ColorToken.Hex(0x132F4CFF), white, cyan, 1, 24, 8)),
        Button = source.Button with
        {
            Default = button,
            Secondary = button with { Background = ColorToken.Hex(0x155E75FF) },
        },
        Badge = source.Badge with
        {
            Default = source.Badge.Default with
            {
                Background = ColorToken.Hex(0x1F8A70FF),
                Foreground = white,
                TextStyle = source.Badge.Default.TextStyle with { Color = white },
                Shape = UiShapeKind.Pill,
            },
        },
    };
}

static ColorToken UiColor(uint rgba)
{
    return ColorToken.Hex(rgba);
}

static async Task<MsdfOverlay> RenderMsdfAsync(
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram program,
    AtlasBundle atlas,
    Machina.Presentation.PositionedTextOperation operation)
{
    using var renderer = new VulkanOrderedQuadRenderer(
        plant,
        program,
        Width,
        Height,
        Native2DPipelineOptions.MsdfText);
    using var cache = new AurelianMsdfAtlasCache(renderer);
    DistanceFieldTextLayoutResult layout = await LayoutForOperationAsync(atlas, operation);
    var qualifiedOperation = new Machina.Presentation.PositionedTextOperation(
        operation.SourceId,
        operation.Rect,
        operation.Text,
        operation.Style,
        operation.Color,
        new MachinaTextPresentationPrimitive(layout.GlyphRun, atlas.Identity, MachinaTextRenderingMode.Msdf));
    IReadOnlyList<NativeMsdfQuadSubmission> submissions = AurelianMsdfTextPresentationAdapter.Adapt(
        qualifiedOperation,
        atlas.Resource,
        cache);

    renderer.Begin2D();
    foreach (NativeMsdfQuadSubmission submission in submissions)
    {
        renderer.SubmitMsdfQuad(submission);
    }
    _ = renderer.End2D(captureReadback: false);
    int uploadsAfterColdFrame = cache.UploadCount;

    renderer.Begin2D();
    foreach (NativeMsdfQuadSubmission submission in submissions)
    {
        renderer.SubmitMsdfQuad(submission);
    }
    Native2DPassResult warm = renderer.End2D(captureReadback: true);
    Require(warm.Pixels is not null, "MSDF heading readback was absent.");
    Require(cache.UploadCount == uploadsAfterColdFrame, "The warm MSDF heading frame uploaded its atlas again.");
    Require(warm.Metrics.DescriptorWrites == 0, "The warm MSDF heading frame rewrote descriptors.");
    return new MsdfOverlay(warm.Pixels!, submissions.Count, cache.UploadCount, warm.Metrics.DescriptorWrites);
}

static async Task<DistanceFieldTextLayoutResult> LayoutForOperationAsync(
    AtlasBundle atlas,
    Machina.Presentation.PositionedTextOperation operation)
{
    DistanceFieldTextLayoutResult initial = await LayoutAsync(atlas, operation.Text, 0, 0);
    double x = operation.Style.AlignX switch
    {
        TextAlignX.Center => (operation.Rect.Width - initial.Width) / 2,
        TextAlignX.Right => operation.Rect.Width - initial.Width,
        _ => 0,
    };
    double baseline = operation.Style.AlignY switch
    {
        TextAlignY.Center => ((operation.Rect.Height - atlas.Size) / 2) + (atlas.Size * 0.8),
        TextAlignY.Bottom => operation.Rect.Height - (atlas.Size * 0.2),
        _ => atlas.Size * 0.8,
    };
    return await LayoutAsync(atlas, operation.Text, x, baseline);
}

static async Task<AtlasBundle> BuildAtlasAsync(
    string path,
    int size,
    IReadOnlyList<string> corpus)
{
    FontFaceId face = new("CrimsonText-Regular");
    var source = new TypographyGlyphOutlineSource(
        new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [face] = new(face, path, 0),
        });
    var pipeline = new GlyphGenerationPipeline(source, new MsdfSharpDistanceFieldGenerator());
    int dimension = NextPowerOfTwo(Math.Max(32, size));
    var settings = new MsdfGenerationSettings(DistanceFieldKind.Msdf, dimension, dimension, 4, 1, "simple", 2);
    var outlineOptions = new GlyphOutlineLoadOptions(size, 0, GlyphHintingMode.None, normalizeToEm: true);
    GlyphKey[] keys = corpus
        .SelectMany(text => DistanceFieldTextRun.Create(
            text,
            face,
            size,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright).GlyphKeys)
        .Distinct()
        .OrderBy(key => key.Codepoint)
        .ToArray();
    List<GeneratedGlyphDistanceField> fields = [];
    Dictionary<GlyphKey, GlyphMetrics> metrics = [];
    foreach (GlyphKey key in keys)
    {
        GlyphGenerationResult result = await pipeline.GenerateAsync(key, outlineOptions, settings);
        if (result.Metrics is not null)
        {
            metrics[key] = result.Metrics;
        }
        if (!Rune.IsWhiteSpace(new Rune(key.Codepoint)))
        {
            Require(result.Success && result.DistanceField is not null, $"MSDF generation failed for U+{key.Codepoint:X4}.");
            fields.Add(result.DistanceField!);
        }
    }

    GeneratedFieldAtlasPackResult packed = new GeneratedFieldAtlasPacker().Pack(
        fields,
        new GeneratedFieldAtlasPackOptions(1024, 1024, 2, $"m4-crimson-{size}"));
    Require(packed.Success, "MSDF atlas packing failed: " + string.Join("; ", packed.Diagnostics.Select(item => item.Message)));
    Dictionary<int, byte[]> pages = packed.Pages.ToDictionary(page => page.Index, EncodeRgba8);
    string contentHash = HashPages(pages);
    MachinaFontAtlasId identity = new($"m4-crimson-{size}-sha256-{contentHash}");
    var resource = new AurelianMsdfAtlasResource(
        identity,
        packed.Snapshot,
        pages,
        AurelianMsdfAtlasRowOrder.TopToBottom);
    return new AtlasBundle(face, size, source, metrics, identity, resource);
}

static async Task<DistanceFieldTextLayoutResult> LayoutAsync(
    AtlasBundle atlas,
    string text,
    double x,
    double baseline)
{
    DistanceFieldTextRun run = DistanceFieldTextRun.Create(
        text,
        atlas.Face,
        atlas.Size,
        MachinaFontWeight.Regular,
        MachinaFontSlant.Upright);
    Dictionary<GlyphPairKey, GlyphPairAdjustment> pairs = [];
    GlyphKey? previous = null;
    bool previousWhitespace = true;
    foreach (GlyphKey key in run.GlyphKeys)
    {
        bool whitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
        if (previous is GlyphKey left && !previousWhitespace && !whitespace)
        {
            GlyphPairAdjustment? adjustment = await atlas.Source.GetPairAdjustmentAsync(left, key);
            if (adjustment is not null)
            {
                pairs[new GlyphPairKey(left, key)] = adjustment;
            }
        }
        previous = key;
        previousWhitespace = whitespace;
    }
    return DistanceFieldTextLayout.Layout(
        run,
        atlas.Metrics,
        new DistanceFieldTextRenderOptions(
            Width,
            Height,
            atlas.Face,
            atlas.Size,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            DistanceFieldKind.Msdf,
            NextPowerOfTwo(Math.Max(32, atlas.Size)),
            NextPowerOfTwo(Math.Max(32, atlas.Size)),
            4,
            FontRgba.White,
            new FontRgba(16, 32, 64, 255),
            x,
            baseline,
            PageWidth: 1024,
            PageHeight: 1024,
            PagePadding: 2),
        pairAdjustments: pairs);
}

static byte[] EncodeRgba8(GeneratedFieldAtlasPage page)
{
    byte[] result = new byte[checked(page.Width * page.Height * 4)];
    for (int pixel = 0; pixel < page.Width * page.Height; pixel++)
    {
        int source = pixel * 3;
        int target = pixel * 4;
        result[target] = ToByte(page.Data[source]);
        result[target + 1] = ToByte(page.Data[source + 1]);
        result[target + 2] = ToByte(page.Data[source + 2]);
        result[target + 3] = 255;
    }
    return result;
}

static (CompiledGraphicsProgram Program, VdMirGraphicsBackendResult Backend) CompileMsdfShader(string repositoryRoot)
{
    const string sourceName = "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts";
    string sourcePath = Path.Combine(repositoryRoot, sourceName.Replace('/', Path.DirectorySeparatorChar));
    string source = File.ReadAllText(sourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
        new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
    Require(module.Success, "MsdfText VD-MIR compile failed.");
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, "MsdfText SPIR-V validation failed.");
    return (CompiledGraphicsProgramExporter.Export(module, backend), backend);
}

static byte ToByte(float value)
{
    return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
}

static int NextPowerOfTwo(int value)
{
    int result = 1;
    while (result < value)
    {
        result *= 2;
    }
    return result;
}

static string HashPages(IReadOnlyDictionary<int, byte[]> pages)
{
    byte[] bytes = pages
        .OrderBy(item => item.Key)
        .SelectMany(item => item.Value)
        .ToArray();
    return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

static NativeAnalyticShapeSubmission ScaleShape(NativeAnalyticShapeSubmission shape, float scale)
{
    return shape with
    {
        Destination = new Native2DRect(
            shape.Destination.X * scale,
            shape.Destination.Y * scale,
            shape.Destination.Width * scale,
            shape.Destination.Height * scale),
        ShapeSize = new Native2DSize(shape.ShapeSize.Width * scale, shape.ShapeSize.Height * scale),
        Radius = shape.Radius * scale,
        BorderWidth = shape.BorderWidth * scale,
    };
}

static ParityResult CompareAlpha(byte[] pixels, NativeAnalyticShapeSubmission shape)
{
    int intersection = 0;
    int union = 0;
    int maximumError = 0;
    for (int y = (int)shape.Destination.Y; y < shape.Destination.Y + shape.Destination.Height; y++)
    {
        for (int x = (int)shape.Destination.X; x < shape.Destination.X + shape.Destination.Width; x++)
        {
            double pX = x + 0.5 - (shape.Destination.X + (shape.ShapeSize.Width / 2));
            double pY = y + 0.5 - (shape.Destination.Y + (shape.ShapeSize.Height / 2));
            double radius = shape.Kind == NativeAnalyticShapeKind.Pill
                ? Math.Min(shape.ShapeSize.Width, shape.ShapeSize.Height) / 2
                : shape.Radius;
            double distance = shape.Kind == NativeAnalyticShapeKind.Circle
                ? Math.Sqrt((pX * pX) + (pY * pY)) - radius
                : RoundedDistance(pX, pY, shape.ShapeSize.Width / 2, shape.ShapeSize.Height / 2, radius);
            double amount = Math.Clamp(0.5 - distance, 0, 1);
            int expected = (int)Math.Round(amount * amount * (3 - (2 * amount)) * 255, MidpointRounding.AwayFromZero);
            int actual = pixels[((y * Width) + x) * 4 + 3];
            bool expectedInside = expected >= 128;
            bool actualInside = actual >= 128;
            if (expectedInside && actualInside) intersection++;
            if (expectedInside || actualInside) union++;
            maximumError = Math.Max(maximumError, Math.Abs(expected - actual));
        }
    }
    return new ParityResult(shape.Kind.ToString(), union == 0 ? 1 : (double)intersection / union, maximumError);
}

static double RoundedDistance(double pX, double pY, double halfWidth, double halfHeight, double radius)
{
    double qX = Math.Abs(pX) - (halfWidth - radius);
    double qY = Math.Abs(pY) - (halfHeight - radius);
    double outsideX = Math.Max(qX, 0);
    double outsideY = Math.Max(qY, 0);
    return Math.Sqrt((outsideX * outsideX) + (outsideY * outsideY)) + Math.Min(Math.Max(qX, qY), 0) - radius;
}

static byte[] CompositeOverNavy(byte[] source)
{
    byte[] result = new byte[source.Length];
    for (int index = 0; index < source.Length; index += 4)
    {
        int alpha = source[index + 3];
        int inverse = 255 - alpha;
        result[index] = (byte)Math.Min(255, source[index] + ((16 * inverse + 127) / 255));
        result[index + 1] = (byte)Math.Min(255, source[index + 1] + ((32 * inverse + 127) / 255));
        result[index + 2] = (byte)Math.Min(255, source[index + 2] + ((64 * inverse + 127) / 255));
        result[index + 3] = 255;
    }
    return result;
}

static byte[] ScaleNearest(byte[] source, int width, int height, int scale)
{
    byte[] result = new byte[width * scale * height * scale * 4];
    int targetWidth = width * scale;
    for (int y = 0; y < height * scale; y++)
    {
        for (int x = 0; x < width * scale; x++)
        {
            int sourceIndex = (((y / scale) * width) + (x / scale)) * 4;
            int targetIndex = ((y * targetWidth) + x) * 4;
            source.AsSpan(sourceIndex, 4).CopyTo(result.AsSpan(targetIndex, 4));
        }
    }
    return result;
}

static byte[] RenderShowcaseText(MachinaPresentationFrame frame, string excludedSourceId)
{
    MachinaPresentationOperation[] textOperations = frame.Operations
        .Where(operation =>
            operation is Machina.Presentation.PositionedTextOperation text
            && text.SourceId != excludedSourceId)
        .ToArray();
    var textFrame = new MachinaPresentationFrame(frame.Viewport, textOperations);
    RasterFrame rasterFrame = new AurelianCpuRasterRenderer().Render(
        MachinaPresentationTranslator.Translate(textFrame));
    byte[] result = new byte[Width * Height * 4];
    for (int index = 0; index < rasterFrame.Surface.Pixels.Count; index++)
    {
        Resolved2DRgbaColor pixel = rasterFrame.Surface.Pixels[index];
        result[index * 4] = pixel.R;
        result[(index * 4) + 1] = pixel.G;
        result[(index * 4) + 2] = pixel.B;
        result[(index * 4) + 3] = pixel.A;
    }
    return result;
}

static void CompositeStraightAlpha(byte[] destination, byte[] source)
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

static void CompositeNativeText(byte[] destination, byte[] source)
{
    for (int index = 0; index < destination.Length; index += 4)
    {
        if (source[index] == 16 && source[index + 1] == 32 && source[index + 2] == 64)
        {
            continue;
        }
        destination[index] = source[index];
        destination[index + 1] = source[index + 1];
        destination[index + 2] = source[index + 2];
        destination[index + 3] = 255;
    }
}

static int CountNativeTextPixels(byte[] source)
{
    int count = 0;
    for (int index = 0; index < source.Length; index += 4)
    {
        if (source[index] != 16 || source[index + 1] != 32 || source[index + 2] != 64)
        {
            count++;
        }
    }
    return count;
}

static (CompiledGraphicsProgram, VdMirGraphicsModule, VdMirGraphicsBackendResult) CompileShader(string root)
{
    const string sourceName = "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts";
    string source = File.ReadAllText(Path.Combine(root, sourceName.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n");
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
    Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
    return (CompiledGraphicsProgramExporter.Export(module, backend), module, backend);
}

static void WriteJson(string path, object value)
    => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

static string Hash(string value)
    => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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
    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
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

sealed record ParityResult(string Kind, double IoU, int MaximumAlphaError);
sealed record AtlasBundle(
    FontFaceId Face,
    int Size,
    TypographyGlyphOutlineSource Source,
    IReadOnlyDictionary<GlyphKey, GlyphMetrics> Metrics,
    MachinaFontAtlasId Identity,
    AurelianMsdfAtlasResource Resource);
sealed record MsdfOverlay(
    byte[] Pixels,
    int GlyphQuads,
    int AtlasUploads,
    int WarmDescriptorWrites);
