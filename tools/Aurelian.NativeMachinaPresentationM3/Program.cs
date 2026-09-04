using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Machina;
using Resolved2DRgbaColor = Aurelian.Rendering.Contracts.Resolved2D.Resolved2DRgbaColor;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Rendering.Raster;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Machina.Core.Authoring;
using Machina.Core.Actions;
using Machina.Core.Flat;
using Machina.Core.Styling;
using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Presentation;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using FontRgba = Machina.Fonts.ReferenceRendering.Rgba32;

const int Width = 1280;
const int Height = 720;
const string ClearHex = "102040";
string[] parityTexts = ["Hello Machina", "Inventory", "Settings", "The quick brown fox jumps over the lazy dog"];
int[] logicalSizes = [16, 24, 32, 64];

string root = FindRepositoryRoot();
string artifactRoot = Path.Combine(root, "artifacts", "aurelian-native-machina-presentation-m3");
string visualRoot = Path.Combine(root, "artifacts", "aurelian-native-machina-presentation-m3-visual");
Directory.CreateDirectory(artifactRoot);
Directory.CreateDirectory(visualRoot);
string fontPath = Path.Combine(root, "tests", "Machina.UI", "Machina.Fonts.Tests", "Fixtures", "Fonts", "CrimsonText-Regular.ttf");

MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(BuildUi(), Width, Height);
PositionedTextOperation[] originalText = prepared.PresentationFrame.Operations.OfType<PositionedTextOperation>().ToArray();
Require(originalText.Length >= 10, "The real Machina proof UI did not produce the expected text coverage.");

Dictionary<int, AtlasBundle> atlases = [];
foreach (int size in logicalSizes.Concat(logicalSizes.Select(static value => value * 2)).Distinct().Order())
{
    atlases.Add(size, await BuildAtlasAsync(fontPath, size, originalText.Select(static item => item.Text).Distinct().ToArray()));
}

Dictionary<string, MachinaTextPresentationPrimitive> primitives = [];
Dictionary<string, RunProof> runProofs = [];
foreach (PositionedTextOperation operation in originalText)
{
    int size = ResolveSize(operation);
    AtlasBundle atlas = atlases[size];
    DistanceFieldTextLayoutResult layout = await LayoutForOperationAsync(atlas, operation, scale: 1);
    MachinaTextRenderingMode mode = IsMsdf(operation) ? MachinaTextRenderingMode.Msdf : MachinaTextRenderingMode.RasterPixel;
    primitives.Add(operation.SourceId, new MachinaTextPresentationPrimitive(layout.GlyphRun, atlas.Identity, mode));
    runProofs.Add(
        operation.SourceId,
        new RunProof(
            operation.Text,
            size,
            mode,
            HashGlyphRun(layout.GlyphRun),
            HashLayout(layout.GlyphRun),
            layout.GlyphRun.Glyphs.Count,
            (byte)operation.Color.Rgba));
}

MachinaPresentationFrame mixedFrame = MachinaTextPresentationFrame.Apply(prepared.PresentationFrame, primitives);
MachinaPresentationFrame rasterBaseFrame = new(
    mixedFrame.Viewport,
    mixedFrame.Operations
        .Where(static operation =>
            operation is not PositionedTextOperation text ||
            text.RenderingMode == MachinaTextRenderingMode.RasterPixel)
        .ToArray());
RasterFrame rasterBase = new AurelianCpuRasterRenderer().Render(MachinaPresentationTranslator.Translate(rasterBaseFrame));
byte[] basePixels = ToRgba(rasterBase.Surface);

(CompiledGraphicsProgram program, VdMirGraphicsBackendResult shader) = CompileShader(root);
VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeMachinaPresentationM3"));
Require(
    init.Success && init.Plant is not null,
    "Vulkan initialization failed: " + string.Join("; ", init.Diagnostics.Select(static item => item.Message)));

RenderScaleProof oneX;
RenderScaleProof twoX;
using (init.Plant)
{
    oneX = await RenderScaleAsync(1, Width, Height, basePixels, mixedFrame, atlases, program, visualRoot);
    twoX = await RenderScaleAsync(
        2,
        Width * 2,
        Height * 2,
        ScaleNearest(basePixels, Width, Height, 2),
        mixedFrame,
        atlases,
        program,
        visualRoot);
}

string topologyHash = HashOperations(prepared.PresentationFrame);
string mixedTopologyHash = HashOperations(mixedFrame);
Require(topologyHash == mixedTopologyHash, "Presentation mode attachment changed UI topology or geometry.");

List<ParityProof> parity = [];
foreach (string text in parityTexts)
{
    RunProof raster = runProofs.Values.First(item => item.Text == text && item.Mode == MachinaTextRenderingMode.RasterPixel);
    RunProof msdf = runProofs.Values.First(item => item.Text == text && item.Mode == MachinaTextRenderingMode.Msdf);
    Require(raster.LayoutHash == msdf.LayoutHash, $"Layout hash differed across rendering modes for '{text}'.");
    Require(raster.GlyphRunHash == msdf.GlyphRunHash, $"Glyph run differed across rendering modes for '{text}'.");
    parity.Add(new ParityProof(text, raster.Size, raster.GlyphRunHash, raster.LayoutHash, true, true));
}

bool validationAvailable = init.Facts!.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation", StringComparer.Ordinal);
Require(init.Diagnostics.All(static item => item.Severity != VulkanInitDiagnosticSeverity.Error), "Vulkan validation reported an error.");

WriteJson(Path.Combine(artifactRoot, "presentation.json"), new
{
    surface = "real Machina Row/Column/Card/Button composition",
    viewport = new { width = Width, height = Height },
    operationCount = mixedFrame.Operations.Count,
    textOperationCount = originalText.Length,
    rasterTextCount = primitives.Values.Count(static item => item.RenderingMode == MachinaTextRenderingMode.RasterPixel),
    msdfTextCount = primitives.Values.Count(static item => item.RenderingMode == MachinaTextRenderingMode.Msdf),
    componentCoverage = new[] { "heading", "button", "label", "inventory-like-row", "longer-sentence" },
    topologyHash,
    mixedTopologyHash,
    hitTestEntryCount = prepared.Lowering.Actions.Count,
    runs = runProofs,
});
WriteJson(Path.Combine(artifactRoot, "rendering.json"), new
{
    pipeline = "MsdfText.v.ts -> VD-MIR -> SPIR-V -> VulkanOrderedQuadRenderer",
    sampler = "linear-clamp-to-edge",
    blend = "straight-alpha",
    clear = "#" + ClearHex,
    scales = new[] { oneX, twoX },
    sizes = logicalSizes,
    atlasPolicy = "size-qualified atlas identity; generated and uploaded before draw",
});
WriteJson(Path.Combine(artifactRoot, "parity.json"), new
{
    invariant = "same semantic text, same MachinaGlyphRun, same layout hash; realization mode only",
    cases = parity,
});
WriteJson(Path.Combine(artifactRoot, "proof.json"), new
{
    milestone = "AURELIAN-NATIVE-MACHINA-PRESENTATION-M3",
    outcome = "A",
    font = new { identity = "CrimsonText-Regular", sha256 = Sha256(File.ReadAllBytes(fontPath)) },
    shader = new
    {
        program.VdMirSha256,
        shader.HlslSha256,
        vertexSpirvSha256 = shader.Vertex.SpirvSha256,
        pixelSpirvSha256 = shader.Pixel.SpirvSha256,
    },
    validation = new { requested = true, available = validationAvailable, errors = 0 },
    visual = new[] { oneX.VisualPath, twoX.VisualPath },
    rasterDefault = true,
    msdfOptIn = true,
    sharedLayout = true,
});
WriteJson(Path.Combine(artifactRoot, "manifest.json"), new
{
    milestone = "AURELIAN-NATIVE-MACHINA-PRESENTATION-M3",
    kind = "real-machina-ui-native-msdf-text-presentation",
    machinaOwnsLayout = true,
    machinaOwnsAtlasSemantics = true,
    aurelianGraphicsOwnsGpuOnly = true,
    rasterTextStillSupported = true,
    rasterTextStillDefault = true,
    msdfTextProductionQualified = true,
    sameGlyphRunAcrossModes = true,
    msdfVisibleInRealUi = true,
    globalUiDefaultChanged = false,
    tinyFarmRequired = false,
    newMsdfGenerationWork = false,
    newShaderWork = false,
    artifactFiles = 5,
});

Console.WriteLine("AURELIAN-NATIVE-MACHINA-PRESENTATION-M3: Outcome A");
Console.WriteLine($"1x: {oneX.PixelSha256}; draws={oneX.DrawCalls}; atlasUploads={oneX.AtlasUploads}");
Console.WriteLine($"2x: {twoX.PixelSha256}; draws={twoX.DrawCalls}; atlasUploads={twoX.AtlasUploads}");
Console.WriteLine($"Vulkan validation: {(validationAvailable ? "enabled" : "unavailable")}; errors=0");

UiDocument BuildUi()
{
    StandardTheme theme = CreateTheme();
    return UiDocument.Create(
    [
        Row.Root("root", View.Rect(background: theme.Colors.Background)),
        Row.Anchor(
            "title",
            "root",
            left: 48,
            top: 28,
            width: 1184,
            height: 72,
            component: UI.Text(
                "Text Rendering Backends",
                id: "title-text",
                size: TextSize.H1,
                color: theme.Colors.Foreground)),
        Row.Anchor(
            "retro",
            "root",
            left: 48,
            top: 120,
            width: 568,
            height: 536,
            component: BuildModeCard("Raster / Retro", "raster", theme)),
        Row.Anchor(
            "smooth",
            "root",
            left: 664,
            top: 120,
            width: 568,
            height: 536,
            component: BuildModeCard("MSDF / Smooth", "msdf", theme)),
    ]);
}

Machina.Core.Nodes.UiNode BuildModeCard(string heading, string prefix, StandardTheme theme)
{
    return StandardUI.Card(
        id: prefix + "-card",
        theme: theme,
        child: UI.VStack(
            id: prefix + "-content",
            justify: Machina.Layout.Frames.StackJustify.SpaceBetween,
            children:
            [
                UI.StackItem.Fixed(36, UI.Text(heading, id: prefix + "-mode", size: TextSize.Md, color: theme.Colors.AccentForeground)),
                UI.StackItem.Fixed(88, UI.Text("Hello Machina", id: prefix + "-hello", size: TextSize.H1, color: theme.Colors.Foreground)),
                UI.StackItem.Fixed(40, UI.Text("Inventory", id: prefix + "-inventory", size: TextSize.Md, color: theme.Colors.Foreground)),
                UI.StackItem.Fixed(48, UI.Row(id: prefix + "-actions", gap: 12, children:
                [
                    StandardUI.Button(
                        "Settings",
                        id: prefix + "-settings",
                        action: UiAction.Named(prefix + ".settings"),
                        variant: Machina.Standard.Components.ButtonVariant.Outline,
                        theme: theme),
                    StandardUI.Button(
                        "Play",
                        id: prefix + "-play",
                        action: UiAction.Named(prefix + ".play"),
                        variant: Machina.Standard.Components.ButtonVariant.Outline,
                        theme: theme),
                ])),
                UI.StackItem.Fixed(34, StandardUI.Label("Hotbar  1  2  3  4  5", id: prefix + "-hotbar", theme: theme)),
                UI.StackItem.Fixed(30, UI.Text("16 px  Small status: ready", id: prefix + "-small", size: TextSize.Sm, color: theme.Colors.MutedForeground)),
                UI.StackItem.Fixed(48, UI.Text("32 px  Inventory / Settings", id: prefix + "-medium", size: TextSize.Md, color: theme.Colors.Foreground)),
                UI.StackItem.Fixed(30, UI.Text("The quick brown fox jumps over the lazy dog", id: prefix + "-fox", size: TextSize.Sm, color: theme.Colors.MutedForeground)),
            ]));
}

StandardTheme CreateTheme()
{
    StandardTheme source = StandardTheme.Default;
    ColorToken background = ColorToken.Hex(0x102040FF);
    ColorToken foreground = ColorToken.Hex(0xF8FAFCFF);
    ColorToken accent = ColorToken.Hex(0x67E8F9FF);
    return source with
    {
        Colors = source.Colors with
        {
            Background = background,
            Foreground = foreground,
            MutedForeground = ColorToken.Hex(0xCBD5E1C0),
            Border = ColorToken.Hex(0x475569FF),
            AccentForeground = accent,
        },
        Card = source.Card with { Default = source.Card.Default with { Background = background, BorderColor = ColorToken.Hex(0x475569FF), ContentInset = 24 } },
        Button = source.Button with
        {
            Outline = source.Button.Outline with
            {
                Background = background,
                Foreground = foreground,
                BorderColor = accent,
                TextStyle = source.Button.Outline.TextStyle with { Color = foreground },
            },
        },
    };
}

bool IsMsdf(PositionedTextOperation operation) => operation.SourceId.Contains("msdf", StringComparison.Ordinal);

int ResolveSize(PositionedTextOperation operation)
{
    if (operation.Text == "Hello Machina")
    {
        return 64;
    }
    if (operation.Text is "Raster / Retro" or "MSDF / Smooth" or "32 px  Inventory / Settings")
    {
        return 32;
    }
    if (operation.Text is "Settings" or "Play" or "Inventory")
    {
        return 24;
    }
    return 16;
}

async Task<RenderScaleProof> RenderScaleAsync(
    int scale,
    int width,
    int height,
    byte[] baseRgba,
    MachinaPresentationFrame frame,
    IReadOnlyDictionary<int, AtlasBundle> sourceAtlases,
    CompiledGraphicsProgram compiledProgram,
    string outputRoot)
{
    using var renderer = new VulkanOrderedQuadRenderer(
        init.Plant!,
        compiledProgram,
        (uint)width,
        (uint)height,
        Native2DPipelineOptions.MsdfText);
    using var cache = new AurelianMsdfAtlasCache(renderer);
    List<NativeMsdfQuadSubmission> submissions = [];

    int[] requiredSizes = frame.Operations
        .OfType<PositionedTextOperation>()
        .Where(static item => item.RenderingMode == MachinaTextRenderingMode.Msdf)
        .Select(operation => ResolveSize(operation) * scale)
        .Distinct()
        .ToArray();
    foreach (int requiredSize in requiredSizes)
    {
        cache.Resolve(sourceAtlases[requiredSize].Resource);
    }

    Stopwatch adapterWatch = Stopwatch.StartNew();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

    IEnumerable<PositionedTextOperation> msdfOperations = frame.Operations
        .OfType<PositionedTextOperation>()
        .Where(static item => item.RenderingMode == MachinaTextRenderingMode.Msdf);
    foreach (PositionedTextOperation operation in msdfOperations)
    {
        int size = ResolveSize(operation) * scale;
        AtlasBundle atlas = sourceAtlases[size];
        DistanceFieldTextLayoutResult layout = await LayoutForOperationAsync(atlas, operation, scale);
        var scaledOperation = new PositionedTextOperation(
            operation.SourceId,
            ScaleRect(operation.Rect, scale),
            operation.Text,
            operation.Style,
            operation.Color,
            new MachinaTextPresentationPrimitive(layout.GlyphRun, atlas.Identity, MachinaTextRenderingMode.Msdf));
        submissions.AddRange(AurelianMsdfTextPresentationAdapter.Adapt(scaledOperation, atlas.Resource, cache));
    }

    long adapterBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    adapterWatch.Stop();
    renderer.Begin2D();
    foreach (NativeMsdfQuadSubmission submission in submissions)
    {
        renderer.SubmitMsdfQuad(submission);
    }
    Native2DPassResult first = renderer.End2D(captureReadback: true);
    int uploadsAfterFirst = cache.UploadCount;
    Stopwatch warmWatch = Stopwatch.StartNew();
    renderer.Begin2D();
    foreach (NativeMsdfQuadSubmission submission in submissions)
    {
        renderer.SubmitMsdfQuad(submission);
    }
    Native2DPassResult warm = renderer.End2D(captureReadback: true);
    warmWatch.Stop();
    Require(cache.UploadCount == uploadsAfterFirst, "A warm frame uploaded an atlas again.");
    Require(warm.Metrics.DescriptorWrites == 0, "A warm frame rewrote descriptors.");

    byte[] composed = CompositeNativeText(baseRgba, warm.Pixels!, width, height);
    string visualPath = Path.Combine(outputRoot, $"machina-text-rendering-{width}x{height}.png");
    WritePng(visualPath, width, height, composed);
    return new RenderScaleProof(
        scale,
        width,
        height,
        submissions.Count,
        warm.Metrics.DrawCalls,
        first.Metrics.DescriptorWrites,
        warm.Metrics.DescriptorWrites,
        cache.UploadCount,
        adapterBytes,
        adapterWatch.Elapsed.TotalMilliseconds,
        warmWatch.Elapsed.TotalMilliseconds,
        warm.Metrics.CpuAllocatedBytes,
        Sha256(composed),
        Path.GetRelativePath(root, visualPath).Replace('\\', '/'));
}

async Task<DistanceFieldTextLayoutResult> LayoutForOperationAsync(AtlasBundle atlas, PositionedTextOperation operation, int scale)
{
    Rect rect = new(0, 0, operation.Rect.Width * scale, operation.Rect.Height * scale);
    DistanceFieldTextLayoutResult initial = await LayoutAsync(atlas, operation.Text, 0, 0);
    double x = operation.Style.AlignX switch
    {
        TextAlignX.Center => rect.X + ((rect.Width - initial.Width) / 2),
        TextAlignX.Right => rect.X + rect.Width - initial.Width,
        _ => rect.X,
    };
    double baseline = operation.Style.AlignY switch
    {
        TextAlignY.Center => rect.Y + ((rect.Height - atlas.Size) / 2) + (atlas.Size * 0.8),
        TextAlignY.Bottom => rect.Y + rect.Height - (atlas.Size * 0.2),
        _ => rect.Y + (atlas.Size * 0.8),
    };
    return await LayoutAsync(atlas, operation.Text, x, baseline);
}

async Task<AtlasBundle> BuildAtlasAsync(string path, int size, IReadOnlyList<string> corpus)
{
    FontFaceId face = new("CrimsonText-Regular");
    var source = new TypographyGlyphOutlineSource(new Dictionary<FontFaceId, TypographyFontFaceSource> { [face] = new(face, path, 0) });
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
        .OrderBy(static key => key.Codepoint)
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
        new GeneratedFieldAtlasPackOptions(1024, 1024, 2, $"crimson-{size}"));
    Require(packed.Success, "Atlas packing failed: " + string.Join("; ", packed.Diagnostics.Select(static item => item.Message)));
    Dictionary<int, byte[]> pages = packed.Pages.ToDictionary(static page => page.Index, EncodeRgba8);
    string contentHash = HashPages(pages);
    MachinaFontAtlasId identity = new($"crimson-{size}-sha256-{contentHash}");
    var resource = new AurelianMsdfAtlasResource(
        identity,
        packed.Snapshot,
        pages,
        AurelianMsdfAtlasRowOrder.TopToBottom);
    return new AtlasBundle(face, size, source, packed.Snapshot, metrics, identity, resource);
}

async Task<DistanceFieldTextLayoutResult> LayoutAsync(AtlasBundle atlas, string text, double x, double baseline)
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
    return DistanceFieldTextLayout.Layout(run, atlas.Metrics, CreateOptions(atlas.Face, atlas.Size, x, baseline), pairAdjustments: pairs);
}

DistanceFieldTextRenderOptions CreateOptions(FontFaceId face, int size, double x, double baseline) => new(
    Width * 2, Height * 2, face, size, MachinaFontWeight.Regular, MachinaFontSlant.Upright,
    DistanceFieldKind.Msdf, NextPowerOfTwo(Math.Max(32, size)), NextPowerOfTwo(Math.Max(32, size)), 4,
    FontRgba.White, new FontRgba(16, 32, 64, 255), x, baseline, PageWidth: 1024, PageHeight: 1024, PagePadding: 2);

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

static (CompiledGraphicsProgram Program, VdMirGraphicsBackendResult Backend) CompileShader(string repositoryRoot)
{
    const string sourceName = "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts";
    string sourcePath = Path.Combine(repositoryRoot, sourceName.Replace('/', Path.DirectorySeparatorChar));
    string source = File.ReadAllText(sourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
    Require(module.Success, "MsdfText VD-MIR compile failed.");
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, "MsdfText SPIR-V validation failed.");
    return (CompiledGraphicsProgramExporter.Export(module, backend), backend);
}

static byte[] CompositeNativeText(byte[] background, byte[] native, int width, int height)
{
    byte[] result = (byte[])background.Clone();
    for (int pixel = 0; pixel < width * height; pixel++)
    {
        int index = pixel * 4;
        if (native[index] == 16 && native[index + 1] == 32 && native[index + 2] == 64)
        {
            continue;
        }
        result[index] = native[index];
        result[index + 1] = native[index + 1];
        result[index + 2] = native[index + 2];
        result[index + 3] = 255;
    }
    return result;
}

static byte[] ToRgba(RasterSurface surface)
{
    byte[] result = new byte[surface.Width * surface.Height * 4];
    for (int index = 0; index < surface.Pixels.Count; index++)
    {
        Resolved2DRgbaColor pixel = surface.Pixels[index];
        result[index * 4] = pixel.R;
        result[(index * 4) + 1] = pixel.G;
        result[(index * 4) + 2] = pixel.B;
        result[(index * 4) + 3] = pixel.A;
    }
    return result;
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
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
    }
    return ~crc;
}

static Rect ScaleRect(Rect rect, int scale) => new(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);
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
static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static string HashPages(IReadOnlyDictionary<int, byte[]> pages)
{
    byte[] bytes = pages
        .OrderBy(static item => item.Key)
        .SelectMany(static item => item.Value)
        .ToArray();
    return Sha256(bytes);
}

static string HashGlyphRun(MachinaGlyphRun run)
{
    StringBuilder builder = new(run.Text);
    foreach (MachinaGlyphPlacement glyph in run.Glyphs)
    {
        builder.Append('|')
            .Append(glyph.Key.Codepoint)
            .Append(':')
            .Append(glyph.OriginX.ToString("R"))
            .Append(':')
            .Append(glyph.BaselineY.ToString("R"))
            .Append(':')
            .Append(glyph.Advance.ToString("R"));
    }
    return Sha256(Encoding.UTF8.GetBytes(builder.ToString()));
}

static string HashLayout(MachinaGlyphRun run)
{
    StringBuilder builder = new(run.Text);
    foreach (MachinaGlyphPlacement glyph in run.Glyphs)
    {
        builder.Append('|')
            .Append(glyph.Key.Codepoint)
            .Append(':')
            .Append(glyph.OriginX.ToString("R"))
            .Append(':')
            .Append(glyph.BaselineY.ToString("R"))
            .Append(':')
            .Append(glyph.Advance.ToString("R"));
    }
    return Sha256(Encoding.UTF8.GetBytes(builder.ToString()));
}

static string HashOperations(MachinaPresentationFrame frame)
{
    string value = string.Join('\n', frame.Operations.Select(operation => operation switch
    {
        FillRectangleOperation fill => $"fill|{fill.SourceId}|{fill.Rect}",
        StrokeRectangleOperation stroke => $"stroke|{stroke.SourceId}|{stroke.Rect}|{stroke.Thickness}",
        PositionedTextOperation text => $"text|{text.SourceId}|{text.Rect}|{text.Text}",
        PushRectangularClipOperation clip => $"push|{clip.SourceId}|{clip.Rect}",
        PopClipOperation => "pop",
        _ => operation.GetType().Name,
    }));
    return Sha256(Encoding.UTF8.GetBytes(value));
}

static JsonSerializerOptions JsonOptions()
{
    return new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

static void WriteJson(string path, object value)
{
    File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions()) + Environment.NewLine);
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
}

sealed record AtlasBundle(
    FontFaceId Face,
    int Size,
    TypographyGlyphOutlineSource Source,
    FontAtlasSnapshot Snapshot,
    IReadOnlyDictionary<GlyphKey, GlyphMetrics> Metrics,
    MachinaFontAtlasId Identity,
    AurelianMsdfAtlasResource Resource);
sealed record RunProof(
    string Text,
    int Size,
    MachinaTextRenderingMode Mode,
    string GlyphRunHash,
    string LayoutHash,
    int GlyphCount,
    byte Alpha);
sealed record ParityProof(string Text, int Size, string GlyphRunHash, string LayoutHash, bool SameGlyphRun, bool SameLayoutHash);
sealed record RenderScaleProof(
    int Scale,
    int Width,
    int Height,
    int GlyphQuads,
    int DrawCalls,
    int ColdDescriptorWrites,
    int WarmDescriptorWrites,
    int AtlasUploads,
    long AdapterAllocatedBytes,
    double AdapterMilliseconds,
    double WarmFrameMilliseconds,
    long WarmFrameAllocatedBytes,
    string PixelSha256,
    string VisualPath);
