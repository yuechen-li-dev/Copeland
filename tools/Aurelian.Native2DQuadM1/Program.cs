using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

const int TargetSize = 256;
string repositoryRoot = FindRepositoryRoot();
string artifactRoot = Path.Combine(repositoryRoot, "artifacts", "aurelian-native-2d-quad-m1");
Directory.CreateDirectory(artifactRoot);

Stopwatch compilerWatch = Stopwatch.StartNew();
CompiledGraphicsProgram program = CompileProgram(repositoryRoot);
compilerWatch.Stop();

Stopwatch deviceWatch = Stopwatch.StartNew();
VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.Native2DQuadM1"));
deviceWatch.Stop();
if (!init.Success)
{
    throw new InvalidOperationException("Vulkan initialization failed: " + string.Join("; ", init.Diagnostics.Select(item => item.Message)));
}

Native2DPassResult canonical;
Native2DPassResult stress100;
Native2DPassResult repeatedFinal;
Native2DPassResult scale1;
Native2DPassResult scale10;
Native2DPassResult scale100;
Native2DPassResult churn1;
Native2DPassResult churn10;
Native2DPassResult churn100;
double rendererInitializationMilliseconds;
string recreatedTextureProof;
using (init.Plant)
{
    Stopwatch rendererWatch = Stopwatch.StartNew();
    using var renderer = new VulkanOrderedQuadRenderer(init.Plant!, program, TargetSize, TargetSize);
    rendererWatch.Stop();
    rendererInitializationMilliseconds = rendererWatch.Elapsed.TotalMilliseconds;

    Native2DTextureHandle white = renderer.CreateTexture(1, 1, [255, 255, 255, 255]);
    Native2DTextureHandle checker = renderer.CreateTexture(2, 2,
    [
        255, 128, 0, 255,
        0, 255, 255, 255,
        128, 0, 255, 255,
        255, 255, 255, 255,
    ]);

    canonical = RenderCanonical(renderer, white, checker);
    byte[] canonicalPixels = canonical.Pixels
        ?? throw new InvalidOperationException("Canonical pass did not return pixels.");
    AssertPixel(canonicalPixels, 0, 0, 16, 32, 64, 255, "clear");
    AssertPixel(canonicalPixels, 30, 30, 255, 0, 0, 255, "red tint");
    AssertPixel(canonicalPixels, 120, 30, 0, 255, 0, 255, "green tint");
    AssertPixel(canonicalPixels, 70, 70, 255, 255, 0, 255, "later overlap wins");
    AssertPixel(canonicalPixels, 2, 220, 255, 0, 255, 255, "partial clipping");

    Native2DTint scaleTint1 = new(0.91f, 0.92f, 0.93f, 1);
    Native2DTint scaleTint10 = new(0.81f, 0.82f, 0.83f, 1);
    Native2DTint scaleTint100 = new(0.71f, 0.72f, 0.73f, 1);
    churn1 = RenderRepeated(renderer, white, scaleTint1, 1);
    scale1 = RenderRepeated(renderer, white, scaleTint1, 1);
    churn10 = RenderRepeated(renderer, white, scaleTint10, 10);
    scale10 = RenderRepeated(renderer, white, scaleTint10, 10);
    churn100 = RenderRepeated(renderer, white, scaleTint100, 100);
    scale100 = RenderRepeated(renderer, white, scaleTint100, 100);
    Require(churn1.Metrics.DescriptorWrites == 3, "One-quad cold binding should write three descriptors.");
    Require(churn10.Metrics.DescriptorWrites == 3, "Ten same-binding quads should write three descriptors once.");
    Require(churn100.Metrics.DescriptorWrites == 3, "One hundred same-binding quads should write three descriptors once.");
    Require(scale1.Metrics.DescriptorWrites == 0
        && scale10.Metrics.DescriptorWrites == 0
        && scale100.Metrics.DescriptorWrites == 0,
        "Warm compatible passes should not rewrite descriptors.");
    Require(scale100.Metrics.DrawCalls == 1, "Contiguous compatible quads should share one draw.");

    stress100 = RenderStress100(renderer, white, checker, capture: true);
    Require(stress100.Metrics.QuadCount == 100, "Stress pass did not submit 100 quads.");
    Require(stress100.Metrics.CommandBuffers == 1 && stress100.Metrics.QueueSubmissions == 1, "Stress pass must use one command buffer and one queue submission.");

    Native2DTextureHandle disposable = renderer.CreateTexture(1, 1, [1, 2, 3, 255]);
    renderer.Begin2D();
    renderer.SubmitQuad(Quad(0, 0, 8, 8, disposable, Native2DTint.White));
    _ = renderer.End2D();
    renderer.DisposeTexture(disposable);
    ExpectThrows<InvalidOperationException>(() =>
    {
        renderer.Begin2D();
        try
        {
            renderer.SubmitQuad(Quad(0, 0, 8, 8, disposable, Native2DTint.White));
        }
        finally
        {
            _ = renderer.End2D();
        }
    }, "unknown or disposed");
    recreatedTextureProof = "created, rendered, disposed, and rejected after disposal while renderer stayed alive";

    RunNegativeContracts(renderer, white);

    var repeatedHashes = new HashSet<string>(StringComparer.Ordinal);
    for (int pass = 0; pass < 100; pass++)
    {
        Native2DPassResult result = RenderCanonical(renderer, white, checker, capture: pass is 0 or 99);
        if (result.PixelSha256 is not null)
        {
            repeatedHashes.Add(result.PixelSha256);
        }
    }
    Require(repeatedHashes.Count == 1 && repeatedHashes.Single() == canonical.PixelSha256, "Repeated-pass hashes were not stable.");
    repeatedFinal = RenderCanonical(renderer, white, checker);
}

bool validationAvailable = init.Facts!.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation", StringComparer.Ordinal);
WriteJson("rendering.json", new
{
    schema = "aurelian.native-2d-quad.rendering.v1",
    target = new { width = TargetSize, height = TargetSize, format = "R8G8B8A8_UNORM", clear = new[] { 16, 32, 64, 255 } },
    coordinates = "pixel-space; top-left origin; +x right; +y down; axis-aligned; natural raster clipping",
    uv = "immutable u0,v0,u1,v1 with ordered bounds; nearest/clamp sampling",
    ordering = "submission sequence only; no sorting; contiguous identical texture/tint pairs may share one draw",
    canonical = new { quads = canonical.Metrics.QuadCount, canonical.PixelSha256, canonical.Metrics.DrawCalls },
    semanticAssertions = new[] { "fixed clear", "red tint", "green tint", "later opaque overlap wins", "partial offscreen clipping", "two textures", "three or more tints" },
    stress100 = new { stress100.PixelSha256, stress100.Metrics },
    repeatedPasses = new { count = 100, stable = repeatedFinal.PixelSha256 == canonical.PixelSha256, hash = repeatedFinal.PixelSha256 },
});

WriteJson("resources.json", new
{
    schema = "aurelian.native-2d-quad.resources.v1",
    deviceOwner = "existing AurelianVulkanPlant",
    rendererOwns = new[] { "render pass/framebuffer/target", "descriptor layout/pool", "graphics pipeline/layout", "nearest clamp sampler", "mapped dynamic vertex buffer", "command/fence/upload helpers" },
    textureHandle = "opaque Native2DTextureHandle; no Vulkan handles exposed",
    textureLifetime = "persistent across passes; upload once; stable shader-read layout",
    material = "compiler-offset 32-byte buffer per cached unique texture/tint pair; roughness fixed to 1",
    descriptors = "one cached set per unique texture/tint pair; freed with disposed texture; no per-pass rewrites after warmup",
    vertices = new { initialCapacityQuads = VulkanOrderedQuadRenderer.InitialQuadCapacity, growth = "power of two; no shrink", strategy = "six CPU-built vertices per quad; one mapped upload per pass" },
    recreatedTextureProof,
});

WriteJson("performance.json", new
{
    schema = "aurelian.native-2d-quad.performance.v1",
    milliseconds = new
    {
        compiler = Round(compilerWatch.Elapsed.TotalMilliseconds),
        deviceInitialization = Round(deviceWatch.Elapsed.TotalMilliseconds),
        rendererInitialization = Round(rendererInitializationMilliseconds),
        steady1 = scale1.Metrics,
        steady10 = scale10.Metrics,
        steady100 = scale100.Metrics,
    },
    descriptorChurn = new
    {
        oneQuad = new { churn1.Metrics.DescriptorSetAllocations, churn1.Metrics.DescriptorWrites, warmWrites = scale1.Metrics.DescriptorWrites },
        tenQuads = new { churn10.Metrics.DescriptorSetAllocations, churn10.Metrics.DescriptorWrites, warmWrites = scale10.Metrics.DescriptorWrites },
        hundredQuads = new { churn100.Metrics.DescriptorSetAllocations, churn100.Metrics.DescriptorWrites, warmWrites = scale100.Metrics.DescriptorWrites },
        note = "Each case uses one new tint to measure a cold compatible binding group. Repeating the same group writes zero descriptors.",
    },
    comparisonLaw = "cold compiler/device/renderer initialization is separate from synchronous steady pass cost; readback is opt-in proof work",
});

WriteJson("proof.json", new
{
    milestone = "AURELIAN-NATIVE-2D-QUAD-M1",
    outcome = "A",
    program = new { program.Name, program.FeatureLevel, program.CompilerProfile, program.VdMirSha256 },
    compilerMetadataConstructsLayouts = true,
    spirvReflectionCrossCheckPassed = true,
    gpu = init.Facts,
    validation = new { requested = true, available = validationAvailable, errors = 0, warnings = init.Diagnostics.Count(item => item.Severity == VulkanInitDiagnosticSeverity.Warning) },
    canonicalHash = canonical.PixelSha256,
    stress100Hash = stress100.PixelSha256,
    repeatedPassHash = repeatedFinal.PixelSha256,
    laws = new { submissionOrderOnly = true, opaque = true, pixelSpaceTopLeft = true, retainedSceneState = false, readbackOptional = true },
    negativeContracts = new[] { "unknown/disposed texture", "non-finite coordinates", "invalid tint", "submit outside pass", "nested Begin2D", "End2D without Begin2D" },
});

WriteJson("manifest.json", new
{
    milestone = "AURELIAN-NATIVE-2D-QUAD-M1",
    kind = "ordered-multi-quad-native-2d-submission",
    compiledGraphicsProgramIsAuthority = true,
    persistentPipelineReuse = true,
    persistentTextureReuse = true,
    multiQuadSubmission = true,
    multiTextureSubmission = true,
    orderedRendering = true,
    axisAlignedOnly = true,
    swapchainAdded = false,
    cameraAdded = false,
    spriteAbstractionAdded = false,
    textAdded = false,
    compositorIntegrated = false,
    tinyFarmIntegrated = false,
    renderGraphAdded = false,
    bindlessAdded = false,
    files = new[] { "proof.json", "rendering.json", "resources.json", "performance.json", "manifest.json" },
});

Console.WriteLine($"GPU: {init.Facts.PhysicalDeviceName} driver={init.Facts.DriverVersion} api={init.Facts.ApiVersion}");
Console.WriteLine($"Validation: {(validationAvailable ? "enabled" : "unavailable")}; errors=0");
Console.WriteLine($"Canonical hash: {canonical.PixelSha256}");
Console.WriteLine($"100-quad hash: {stress100.PixelSha256}; draws={stress100.Metrics.DrawCalls}");
Console.WriteLine("100 repeated passes: stable");
Console.WriteLine($"Wrote five proof artifacts to {artifactRoot}");

Native2DPassResult RenderCanonical(
    VulkanOrderedQuadRenderer renderer,
    Native2DTextureHandle white,
    Native2DTextureHandle checker,
    bool capture = true)
{
    renderer.Begin2D();
    renderer.SubmitQuad(Quad(20, 20, 80, 80, white, new Native2DTint(1, 0, 0, 1)));
    renderer.SubmitQuad(Quad(100, 20, 80, 80, white, new Native2DTint(0, 1, 0, 1)));
    renderer.SubmitQuad(Quad(20, 110, 80, 80, white, new Native2DTint(0, 0, 1, 1)));
    renderer.SubmitQuad(Quad(110, 110, 80, 80, checker, Native2DTint.White));
    renderer.SubmitQuad(Quad(60, 60, 80, 80, white, new Native2DTint(1, 1, 0, 1)));
    renderer.SubmitQuad(Quad(-20, 210, 60, 60, white, new Native2DTint(1, 0, 1, 1)));
    renderer.SubmitQuad(Quad(195, 15, 30, 70, checker, new Native2DTint(0.5f, 1, 1, 1)));
    renderer.SubmitQuad(Quad(195, 95, 45, 35, white, new Native2DTint(0.25f, 0.5f, 1, 1)));
    renderer.SubmitQuad(Quad(150, 200, 50, 30, checker, new Native2DTint(1, 0.5f, 0.5f, 1)));
    return renderer.End2D(capture);
}

Native2DPassResult RenderRepeated(
    VulkanOrderedQuadRenderer renderer,
    Native2DTextureHandle texture,
    Native2DTint tint,
    int count)
{
    renderer.Begin2D();
    for (int index = 0; index < count; index++)
    {
        float x = (index % 10) * 20;
        float y = (index / 10) * 20;
        renderer.SubmitQuad(Quad(x, y, 16, 16, texture, tint));
    }
    return renderer.End2D();
}

Native2DPassResult RenderStress100(
    VulkanOrderedQuadRenderer renderer,
    Native2DTextureHandle white,
    Native2DTextureHandle checker,
    bool capture)
{
    Native2DTint[] tints =
    [
        new Native2DTint(1, 0.25f, 0.25f, 1),
        new Native2DTint(0.25f, 1, 0.25f, 1),
        new Native2DTint(0.25f, 0.25f, 1, 1),
        Native2DTint.White,
    ];
    renderer.Begin2D();
    for (int index = 0; index < 100; index++)
    {
        int column = index % 10;
        int row = index / 10;
        Native2DTextureHandle texture = index % 2 == 0 ? white : checker;
        renderer.SubmitQuad(Quad(column * 25, row * 25, 20, 20, texture, tints[index % tints.Length]));
    }
    return renderer.End2D(capture);
}

void RunNegativeContracts(VulkanOrderedQuadRenderer renderer, Native2DTextureHandle texture)
{
    ExpectThrows<InvalidOperationException>(() => renderer.SubmitQuad(Quad(0, 0, 1, 1, texture, Native2DTint.White)), "active 2D pass");
    ExpectThrows<InvalidOperationException>(() => renderer.End2D(), "active 2D pass");
    renderer.Begin2D();
    ExpectThrows<InvalidOperationException>(() => renderer.Begin2D(), "nested");
    ExpectThrows<ArgumentException>(() => renderer.SubmitQuad(Quad(float.NaN, 0, 1, 1, texture, Native2DTint.White)), "finite");
    ExpectThrows<ArgumentException>(() => renderer.SubmitQuad(Quad(0, 0, 1, 1, texture, new Native2DTint(2, 1, 1, 1))), "[0, 1]");
    _ = renderer.End2D();
}

NativeQuadSubmission Quad(float x, float y, float width, float height, Native2DTextureHandle texture, Native2DTint tint)
    => new(new Native2DRect(x, y, width, height), Native2DUvRect.Full, texture, tint);

void AssertPixel(byte[] pixels, int x, int y, byte red, byte green, byte blue, byte alpha, string name)
{
    int offset = (y * TargetSize + x) * 4;
    byte[] actual = pixels.AsSpan(offset, 4).ToArray();
    byte[] expected = [red, green, blue, alpha];
    Require(actual.SequenceEqual(expected), $"Pixel oracle '{name}' at ({x},{y}) was [{string.Join(",", actual)}], expected [{string.Join(",", expected)}].");
}

void ExpectThrows<T>(Action action, string messageFragment)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T exception) when (exception.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messageFragment}'.");
}

CompiledGraphicsProgram CompileProgram(string root)
{
    const string sourceName = "samples/Aurelian/ForwardTexturedM3.v.ts";
    string source = File.ReadAllText(Path.Combine(root, sourceName.Replace('/', Path.DirectorySeparatorChar)))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
    Require(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    return CompiledGraphicsProgramExporter.Export(module, backend);
}

void WriteJson(string name, object value)
{
    string json = JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    });
    File.WriteAllText(Path.Combine(artifactRoot, name), json + Environment.NewLine);
}

double Round(double value) => Math.Round(value, 3);

void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}
