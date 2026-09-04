using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Machina;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;

const string Hello = "Hello Machina";
const string Fox = "The quick brown fox jumps over the lazy dog";
const string Descenders = "Agjpqy";
const string Punctuation = "Hello.";
const int TargetWidth = 2560;
const int TargetHeight = 384;
int[] sizes = [16, 32, 64, 96, 128];

string root = FindRepositoryRoot();
string artifactRoot = Path.Combine(root, "artifacts", "aurelian-native-msdf-text-m2");
Directory.CreateDirectory(artifactRoot);
string fontPath = Path.Combine(root, "tests", "Machina.UI", "Machina.Fonts.Tests", "Fixtures", "Fonts", "CrimsonText-Regular.ttf");
string fontHash = Sha256(File.ReadAllBytes(fontPath));

Stopwatch shaderWatch = Stopwatch.StartNew();
(CompiledGraphicsProgram program, VdMirGraphicsBackendResult backend) = CompileShader(root);
shaderWatch.Stop();

VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeMsdfTextM2"));
if (!init.Success)
{
    throw new InvalidOperationException("Vulkan initialization failed: " + string.Join("; ", init.Diagnostics.Select(item => item.Message)));
}

List<RenderReport> renders = [];
List<SizeReport> sizeReports = [];
List<ParityReport> parity = [];
Dictionary<string, string> canonicalFieldHashes = [];
string canonicalAtlasHash = string.Empty;
string canonicalGlyphRunHash = string.Empty;
Native2DPassMetrics? repeatedMetrics = null;
Native2DPassMetrics? multiRunMetrics = null;
Native2DPassMetrics? multiColorMetrics = null;
double canonicalAtlasGenerationMilliseconds = 0;
double canonicalRendererCreationMilliseconds = 0;
double canonicalAtlasUploadMilliseconds = 0;
long canonicalAdapterBytes = 0;
bool disposedAtlasRejected = false;

using (init.Plant)
{
    foreach (int size in sizes)
    {
        string[] corpus = size == 64
            ? [Hello, Fox, Descenders, Punctuation, "M", ".", "g", "Q"]
            : size == 96
                ? [Hello, Fox, Descenders]
            : [Hello, Fox];
        Stopwatch atlasWatch = Stopwatch.StartNew();
        AtlasBundle atlas = await BuildAtlasAsync(fontPath, size, corpus);
        atlasWatch.Stop();

        Stopwatch rendererWatch = Stopwatch.StartNew();
        using var renderer = new VulkanOrderedQuadRenderer(
            init.Plant!,
            program,
            TargetWidth,
            TargetHeight,
            Native2DPipelineOptions.MsdfText);
        rendererWatch.Stop();

        Stopwatch uploadWatch = Stopwatch.StartNew();
        Dictionary<int, Native2DTextureHandle> textures = [];
        foreach (GeneratedFieldAtlasPage page in atlas.Pages)
        {
            textures.Add(page.Index, renderer.CreateTexture((uint)page.Width, (uint)page.Height, atlas.RgbaPages[page.Index]));
        }
        uploadWatch.Stop();

        List<string> cases = [Hello, Fox];
        if (size == 64)
        {
            cases.AddRange(["M", ".", "g", "Q", Punctuation, Descenders]);
        }
        else if (size == 96)
        {
            cases.Add(Descenders);
        }

        foreach (string text in cases)
        {
            DistanceFieldTextLayoutResult layout = await LayoutAsync(atlas, text, 16, size + 32);
            long before = GC.GetAllocatedBytesForCurrentThread();
            IReadOnlyList<NativeMsdfQuadSubmission> submissions = AurelianGlyphRunAdapter.Adapt(
                layout.GlyphRun,
                atlas.Snapshot,
                textures,
                Native2DTint.White);
            long adapterBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Native2DPassResult gpu = Render(renderer, submissions, capture: true);
            RgbaImage cpu = CpuDistanceFieldTextRenderer.RenderText(
                atlas.Snapshot,
                atlas.ReferencePages,
                layout,
                CreateOptions(atlas.Face, size, 16, size + 32));
            ParityReport comparison = Compare(text, size, gpu.Pixels!, cpu, layout.GlyphRun);
            Require(comparison.Iou >= 0.85, $"CPU/GPU IoU for {size}px '{text}' was {comparison.Iou:F4}.");
            Require(comparison.GpuBounds is not null, $"GPU render for {size}px '{text}' had no text pixels.");
            Require(gpu.Metrics.DrawCalls == 1, $"Expected one compatible draw for {size}px '{text}'.");
            PixelBounds gpuBounds = comparison.GpuBounds!;
            Require(Math.Abs(gpuBounds.Left - comparison.QualifiedInkLeft) <= 3, $"GPU left bound for {size}px '{text}' drifted from qualified placement.");
            Require(Math.Abs(gpuBounds.Right - comparison.QualifiedInkRight) <= 3, $"GPU right bound for {size}px '{text}' drifted from qualified placement.");
            Require(gpuBounds.Right < TargetWidth - 1, $"GPU render for {size}px '{text}' clipped at the target edge.");
            if (text == Descenders)
            {
                Require(gpuBounds.Bottom > comparison.BaselineY, $"Descenders at {size}px did not extend below the baseline.");
            }
            if (text == ".")
            {
                Require(gpuBounds.Width <= 10 && gpuBounds.Height <= 10, "The 64px period did not remain a bounded tiny glyph.");
            }

            parity.Add(comparison);
            renders.Add(new RenderReport(
                text,
                size,
                submissions.Count,
                gpu.PixelSha256!,
                gpu.Metrics.DrawCalls,
                gpu.Metrics.DescriptorWrites,
                comparison.GpuBounds,
                adapterBytes,
                gpu.Metrics));

            if (size == 64 && text == Hello)
            {
                canonicalGlyphRunHash = HashGlyphRun(layout.GlyphRun);
                canonicalAdapterBytes = adapterBytes;
            }
        }

        if (size == 64)
        {
            canonicalAtlasHash = HashPages(atlas.Pages);
            foreach (GeneratedGlyphDistanceField field in atlas.Fields)
            {
                canonicalFieldHashes[$"U+{field.Key.Codepoint:X4}"] = HashFloats(field.Data.Span);
            }
            canonicalAtlasGenerationMilliseconds = atlasWatch.Elapsed.TotalMilliseconds;
            canonicalRendererCreationMilliseconds = rendererWatch.Elapsed.TotalMilliseconds;
            canonicalAtlasUploadMilliseconds = uploadWatch.Elapsed.TotalMilliseconds;

            DistanceFieldTextLayoutResult helloLayout = await LayoutAsync(atlas, Hello, 16, 96);
            IReadOnlyList<NativeMsdfQuadSubmission> helloSubmissions = AurelianGlyphRunAdapter.Adapt(
                helloLayout.GlyphRun,
                atlas.Snapshot,
                textures,
                Native2DTint.White);
            for (int pass = 0; pass < 100; pass++)
            {
                Native2DPassResult result = Render(renderer, helloSubmissions, capture: pass == 99);
                if (pass == 99)
                {
                    repeatedMetrics = result.Metrics;
                }
            }

            DistanceFieldTextLayoutResult foxLayout = await LayoutAsync(atlas, Fox, 16, 192);
            DistanceFieldTextLayoutResult descenderLayout = await LayoutAsync(atlas, Descenders, 16, 288);
            NativeMsdfQuadSubmission[] multiRun =
            [
                .. AurelianGlyphRunAdapter.Adapt(helloLayout.GlyphRun, atlas.Snapshot, textures, Native2DTint.White),
                .. AurelianGlyphRunAdapter.Adapt(foxLayout.GlyphRun, atlas.Snapshot, textures, Native2DTint.White),
                .. AurelianGlyphRunAdapter.Adapt(descenderLayout.GlyphRun, atlas.Snapshot, textures, Native2DTint.White),
            ];
            Native2DPassResult multi = Render(renderer, multiRun, capture: true);
            multiRunMetrics = multi.Metrics;
            Require(multi.Metrics.DrawCalls == 1, "Three same-atlas/same-color runs must remain one compatible draw.");

            NativeMsdfQuadSubmission[] colors =
            [
                .. AurelianGlyphRunAdapter.Adapt(helloLayout.GlyphRun, atlas.Snapshot, textures, new Native2DTint(1f, 0.7f, 0.2f, 1f)),
                .. AurelianGlyphRunAdapter.Adapt(foxLayout.GlyphRun, atlas.Snapshot, textures, new Native2DTint(0.2f, 0.8f, 1f, 1f)),
            ];
            Native2DPassResult colored = Render(renderer, colors, capture: true);
            multiColorMetrics = colored.Metrics;
            Require(colored.Metrics.DrawCalls == 2, "Two run-level colors must produce two ordered compatible draws.");

            Native2DTextureHandle disposed = renderer.CreateTexture(1, 1, [128, 128, 128, 255]);
            renderer.DisposeTexture(disposed);
            renderer.Begin2D();
            try
            {
                renderer.SubmitMsdfQuad(new NativeMsdfQuadSubmission(
                    new Native2DRect(0, 0, 1, 1),
                    Native2DUvRect.Full,
                    disposed,
                    Native2DTint.White,
                    NativeMsdfParameters.Create(4, 1)));
            }
            catch (InvalidOperationException error) when (error.Message.Contains("unknown or disposed", StringComparison.Ordinal))
            {
                disposedAtlasRejected = true;
            }
            _ = renderer.End2D();
        }

        sizeReports.Add(new SizeReport(
            size,
            atlas.FieldDimension,
            atlas.Pages.Count,
            atlas.Fields.Count,
            atlasWatch.Elapsed.TotalMilliseconds,
            rendererWatch.Elapsed.TotalMilliseconds,
            uploadWatch.Elapsed.TotalMilliseconds));
    }
}

Require(disposedAtlasRejected, "Disposed atlas handle was not rejected.");
Require(init.Diagnostics.All(item => item.Severity != VulkanInitDiagnosticSeverity.Error), "Vulkan initialization reported validation errors.");
bool validationAvailable = init.Facts!.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation", StringComparer.Ordinal);

string outlineManifestHash = HashFile(Path.Combine(root, "artifacts", "machina-outline-conformance-m1", "manifest.json"));
string msdfManifestHash = HashFile(Path.Combine(root, "artifacts", "machina-msdf-realization-m1", "manifest.json"));

WriteJson(Path.Combine(artifactRoot, "shader.json"), new
{
    source = "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts",
    compileMilliseconds = shaderWatch.Elapsed.TotalMilliseconds,
    vdMirSha256 = program.VdMirSha256,
    hlslSha256 = backend.HlslSha256,
    vertexSpirvSha256 = backend.Vertex.SpirvSha256,
    pixelSpirvSha256 = backend.Pixel.SpirvSha256,
    rendererMetadataSha256 = Sha256(JsonSerializer.SerializeToUtf8Bytes(program, JsonOptions())),
    vertexSpirvValidated = backend.Vertex.SpirvValidated,
    pixelSpirvValidated = backend.Pixel.SpirvValidated,
    material = new { size = program.Material!.Size, fields = program.Material.Fields },
});
WriteJson(Path.Combine(artifactRoot, "rendering.json"), new
{
    format = "VK_FORMAT_R8G8B8A8_UNORM",
    sampler = "linear-clamp-to-edge",
    blend = "straight-alpha: src-alpha / one-minus-src-alpha; alpha: one / one-minus-src-alpha",
    target = new { width = TargetWidth, height = TargetHeight, clear = new[] { 16, 32, 64, 255 } },
    renders,
    sizes = sizeReports,
    multipleRuns = multiRunMetrics,
    multipleColors = multiColorMetrics,
    repeated100Final = repeatedMetrics,
});
WriteJson(Path.Combine(artifactRoot, "parity.json"), new
{
    reference = "CPU reconstruction over the same quantized RGBA8 atlas bytes",
    minimumIou = parity.Min(item => item.Iou),
    maximumMeanAbsoluteChannelDelta = parity.Max(item => item.MeanAbsoluteChannelDelta),
    cases = parity,
});
WriteJson(Path.Combine(artifactRoot, "proof.json"), new
{
    milestone = "AURELIAN-NATIVE-MSDF-TEXT-M2",
    outcome = "A",
    font = new { path = Path.GetRelativePath(root, fontPath).Replace('\\', '/'), sha256 = fontHash },
    upstream = new
    {
        outlineQualificationManifestSha256 = outlineManifestHash,
        msdfQualificationManifestSha256 = msdfManifestHash,
        machinaGlyphRunSha256 = canonicalGlyphRunHash,
        atlasFloatSha256 = canonicalAtlasHash,
        fieldSha256 = canonicalFieldHashes,
    },
    canonical = renders.Single(item => item.Size == 64 && item.Text == Hello),
    fox = renders.Single(item => item.Size == 64 && item.Text == Fox),
    glyphs = renders.Where(item => item.Size == 64 && item.Text is "M" or "." or "g" or "Q").ToArray(),
    punctuation = renders.Single(item => item.Size == 64 && item.Text == Punctuation),
    descenders = renders.Single(item => item.Size == 64 && item.Text == Descenders),
    validation = new { requested = true, available = validationAvailable, errors = 0, warnings = init.Diagnostics.Count(item => item.Severity == VulkanInitDiagnosticSeverity.Warning) },
    performance = new
    {
        shaderCompileMilliseconds = shaderWatch.Elapsed.TotalMilliseconds,
        atlasGenerationMilliseconds = canonicalAtlasGenerationMilliseconds,
        pipelineAndRendererCreationMilliseconds = canonicalRendererCreationMilliseconds,
        atlasUploadMilliseconds = canonicalAtlasUploadMilliseconds,
        canonicalAdapterAllocatedBytes = canonicalAdapterBytes,
        steadyPass = repeatedMetrics,
    },
});
WriteJson(Path.Combine(artifactRoot, "manifest.json"), new
{
    milestone = "AURELIAN-NATIVE-MSDF-TEXT-M2",
    kind = "qualified-glyphrun-msdf-atlas-vulkan-text",
    layoutLawChanged = false,
    msdfGenerationChanged = false,
    machinaGlyphRunIsAuthority = true,
    vectorDerivedAtlasIsAuthority = true,
    visualTypeScriptShaderAdded = true,
    vdMirShaderPathUsed = true,
    nativeOrderedQuadPathReused = true,
    fontParsingInAurelian = false,
    typographyDependencyInAurelianGraphics = false,
    gpuTextLayoutAdded = false,
    swapchainAdded = false,
    compositorIntegrated = false,
    tinyFarmIntegrated = false,
    artifactFiles = 5,
});

Console.WriteLine("AURELIAN-NATIVE-MSDF-TEXT-M2: Outcome A");
Console.WriteLine($"Hello 64 hash: {renders.Single(item => item.Size == 64 && item.Text == Hello).PixelSha256}");
Console.WriteLine($"Fox 64 hash: {renders.Single(item => item.Size == 64 && item.Text == Fox).PixelSha256}");
Console.WriteLine($"Minimum CPU/GPU IoU: {parity.Min(item => item.Iou):F6}");
Console.WriteLine($"Validation: {(validationAvailable ? "enabled" : "unavailable")}; errors=0");
Console.WriteLine($"Artifacts: {artifactRoot}");

static async Task<AtlasBundle> BuildAtlasAsync(string fontPath, int size, IReadOnlyList<string> corpus)
{
    FontFaceId face = new("CrimsonText-Regular");
    var source = new TypographyGlyphOutlineSource(new Dictionary<FontFaceId, TypographyFontFaceSource>
    {
        [face] = new(face, fontPath, 0),
    });
    var pipeline = new GlyphGenerationPipeline(source, new MsdfSharpDistanceFieldGenerator());
    int dimension = NextPowerOfTwo(Math.Max(32, size));
    var settings = new MsdfGenerationSettings(DistanceFieldKind.Msdf, dimension, dimension, 4, 1, "simple", 2);
    var outlineOptions = new GlyphOutlineLoadOptions(size, 0, GlyphHintingMode.None, normalizeToEm: true);
    GlyphKey[] keys = corpus
        .SelectMany(text => DistanceFieldTextRun.Create(text, face, size, MachinaFontWeight.Regular, MachinaFontSlant.Upright).GlyphKeys)
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
        bool whitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
        if (!whitespace)
        {
            Require(result.Success && result.DistanceField is not null, $"Field generation failed for U+{key.Codepoint:X4}: {string.Join("; ", result.Diagnostics.Select(item => item.Message))}");
            GeneratedGlyphDistanceField field = result.DistanceField
                ?? throw new InvalidOperationException($"Field generation returned no field for U+{key.Codepoint:X4}.");
            fields.Add(field);
        }
    }
    GeneratedFieldAtlasPackResult packed = new GeneratedFieldAtlasPacker().Pack(
        fields,
        new GeneratedFieldAtlasPackOptions(1024, 1024, 2, $"crimson-{size}"));
    Require(packed.Success, "Atlas packing failed: " + string.Join("; ", packed.Diagnostics.Select(item => item.Message)));

    Dictionary<int, byte[]> rgbaPages = packed.Pages.ToDictionary(page => page.Index, page => EncodeRgba8(page));
    Dictionary<int, DistanceFieldPageReference> referencePages = packed.Pages.ToDictionary(
        page => page.Index,
        page => CreateReferencePage(page, rgbaPages[page.Index]));
    return new AtlasBundle(face, size, dimension, source, packed.Snapshot, packed.Pages, fields, metrics, rgbaPages, referencePages);
}

static async Task<DistanceFieldTextLayoutResult> LayoutAsync(AtlasBundle atlas, string text, double x, double baseline)
{
    DistanceFieldTextRun run = DistanceFieldTextRun.Create(text, atlas.Face, atlas.Size, MachinaFontWeight.Regular, MachinaFontSlant.Upright);
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

static DistanceFieldTextRenderOptions CreateOptions(FontFaceId face, int size, double x, double baseline)
{
    int dimension = NextPowerOfTwo(Math.Max(32, size));
    return new DistanceFieldTextRenderOptions(
        TargetWidth,
        TargetHeight,
        face,
        size,
        MachinaFontWeight.Regular,
        MachinaFontSlant.Upright,
        DistanceFieldKind.Msdf,
        dimension,
        dimension,
        4,
        Rgba32.White,
        new Rgba32(16, 32, 64, 255),
        x,
        baseline,
        PageWidth: 1024,
        PageHeight: 1024,
        PagePadding: 2);
}

static Native2DPassResult Render(VulkanOrderedQuadRenderer renderer, IReadOnlyList<NativeMsdfQuadSubmission> submissions, bool capture)
{
    renderer.Begin2D();
    foreach (NativeMsdfQuadSubmission submission in submissions)
    {
        renderer.SubmitMsdfQuad(submission);
    }
    return renderer.End2D(capture);
}

static byte[] EncodeRgba8(GeneratedFieldAtlasPage page)
{
    Require(page.ChannelCount == 3, "M2 accepts RGB MSDF pages only.");
    byte[] result = new byte[checked(page.Width * page.Height * 4)];
    for (int pixel = 0; pixel < page.Width * page.Height; pixel++)
    {
        int source = pixel * 3;
        int destination = pixel * 4;
        result[destination] = ToByte(page.Data[source]);
        result[destination + 1] = ToByte(page.Data[source + 1]);
        result[destination + 2] = ToByte(page.Data[source + 2]);
        result[destination + 3] = 255;
    }
    return result;
}

static DistanceFieldPageReference CreateReferencePage(GeneratedFieldAtlasPage page, byte[] rgba)
{
    float[] rgb = new float[checked(page.Width * page.Height * 3)];
    for (int pixel = 0; pixel < page.Width * page.Height; pixel++)
    {
        rgb[pixel * 3] = rgba[pixel * 4] / 255f;
        rgb[(pixel * 3) + 1] = rgba[(pixel * 4) + 1] / 255f;
        rgb[(pixel * 3) + 2] = rgba[(pixel * 4) + 2] / 255f;
    }
    return new DistanceFieldPageReference("memory", DistanceFieldKind.Msdf, page.Index, page.Width, page.Height, 3, rgb);
}

static ParityReport Compare(string text, int size, byte[] gpu, RgbaImage cpu, MachinaGlyphRun run)
{
    int intersection = 0;
    int union = 0;
    long absoluteDelta = 0;
    BoundsBuilder gpuBounds = new();
    BoundsBuilder cpuBounds = new();
    for (int index = 0; index < cpu.Pixels.Length; index++)
    {
        int byteIndex = index * 4;
        Rgba32 expected = cpu.Pixels[index];
        bool gpuInk = gpu[byteIndex] >= 136;
        bool cpuInk = expected.R >= 136;
        if (gpuInk || cpuInk)
        {
            union++;
        }
        if (gpuInk && cpuInk)
        {
            intersection++;
        }
        int x = index % cpu.Width;
        int y = index / cpu.Width;
        if (gpuInk)
        {
            gpuBounds.Add(x, y);
        }
        if (cpuInk)
        {
            cpuBounds.Add(x, y);
        }
        absoluteDelta += Math.Abs(gpu[byteIndex] - expected.R);
        absoluteDelta += Math.Abs(gpu[byteIndex + 1] - expected.G);
        absoluteDelta += Math.Abs(gpu[byteIndex + 2] - expected.B);
        absoluteDelta += Math.Abs(gpu[byteIndex + 3] - expected.A);
    }
    double iou = union == 0 ? 1 : intersection / (double)union;
    return new ParityReport(
        text,
        size,
        iou,
        absoluteDelta / (double)(cpu.Pixels.Length * 4),
        gpuBounds.Build(),
        cpuBounds.Build(),
        run.Lines.Single().BaselineY,
        run.Glyphs.Count,
        run.Glyphs.Where(glyph => !glyph.IsWhitespace).Min(glyph => glyph.OriginX + glyph.PlaneBounds.Left),
        run.Glyphs.Where(glyph => !glyph.IsWhitespace).Max(glyph => glyph.OriginX + glyph.PlaneBounds.Right));
}

static (CompiledGraphicsProgram Program, VdMirGraphicsBackendResult Backend) CompileShader(string root)
{
    const string sourceName = "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts";
    string source = File.ReadAllText(Path.Combine(root, sourceName.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
    Require(module.Success, "MsdfText VD-MIR compile failed: " + string.Join("; ", module.Diagnostics.Select(item => item.Message)));
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, "MsdfText SPIR-V validation failed.");
    return (CompiledGraphicsProgramExporter.Export(module, backend), backend);
}

static string HashGlyphRun(MachinaGlyphRun run)
{
    StringBuilder builder = new();
    builder.Append(run.Text).Append('\n');
    foreach (MachinaGlyphPlacement glyph in run.Glyphs)
    {
        builder.Append(glyph.Key.Codepoint).Append('|')
            .Append(glyph.GlyphId).Append('|')
            .Append(glyph.OriginX.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
            .Append(glyph.BaselineY.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
            .Append(glyph.Advance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
    }
    return Sha256(Encoding.UTF8.GetBytes(builder.ToString()));
}

static string HashPages(IReadOnlyList<GeneratedFieldAtlasPage> pages)
{
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (GeneratedFieldAtlasPage page in pages.OrderBy(page => page.Index))
    {
        byte[] bytes = FloatsToBytes(page.Data);
        hash.AppendData(bytes);
    }
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}

static string HashFloats(ReadOnlySpan<float> values)
    => Sha256(FloatsToBytes(values));

static byte[] FloatsToBytes(ReadOnlySpan<float> values)
{
    byte[] bytes = new byte[checked(values.Length * 4)];
    for (int index = 0; index < values.Length; index++)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), BitConverter.SingleToInt32Bits(values[index]));
    }
    return bytes;
}

static byte ToByte(float value)
    => (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);

static int NextPowerOfTwo(int value)
{
    int result = 1;
    while (result < value)
    {
        result = checked(result * 2);
    }
    return result;
}

static string HashFile(string path)
{
    Require(File.Exists(path), $"Required upstream artifact is missing: {path}");
    return Sha256(File.ReadAllBytes(path));
}

static string Sha256(byte[] bytes)
    => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

static JsonSerializerOptions JsonOptions()
    => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

static void WriteJson(string path, object value)
    => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions()) + Environment.NewLine);

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed record AtlasBundle(
    FontFaceId Face,
    int Size,
    int FieldDimension,
    TypographyGlyphOutlineSource Source,
    FontAtlasSnapshot Snapshot,
    IReadOnlyList<GeneratedFieldAtlasPage> Pages,
    IReadOnlyList<GeneratedGlyphDistanceField> Fields,
    IReadOnlyDictionary<GlyphKey, GlyphMetrics> Metrics,
    IReadOnlyDictionary<int, byte[]> RgbaPages,
    IReadOnlyDictionary<int, DistanceFieldPageReference> ReferencePages);

sealed record RenderReport(
    string Text,
    int Size,
    int GlyphCount,
    string PixelSha256,
    int DrawCalls,
    int DescriptorWrites,
    PixelBounds? Bounds,
    long AdapterAllocatedBytes,
    Native2DPassMetrics Metrics);

sealed record SizeReport(
    int Size,
    int FieldDimension,
    int AtlasPages,
    int UniqueFields,
    double AtlasGenerationMilliseconds,
    double RendererCreationMilliseconds,
    double AtlasUploadMilliseconds);

sealed record ParityReport(
    string Text,
    int Size,
    double Iou,
    double MeanAbsoluteChannelDelta,
    PixelBounds? GpuBounds,
    PixelBounds? CpuBounds,
    double BaselineY,
    int LayoutGlyphCount,
    double QualifiedInkLeft,
    double QualifiedInkRight);

sealed record PixelBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left + 1;

    public int Height => Bottom - Top + 1;
}

sealed class BoundsBuilder
{
    private int left = int.MaxValue;
    private int top = int.MaxValue;
    private int right = int.MinValue;
    private int bottom = int.MinValue;

    public void Add(int x, int y)
    {
        left = Math.Min(left, x);
        top = Math.Min(top, y);
        right = Math.Max(right, x);
        bottom = Math.Max(bottom, y);
    }

    public PixelBounds? Build()
        => left == int.MaxValue ? null : new PixelBounds(left, top, right, bottom);
}
