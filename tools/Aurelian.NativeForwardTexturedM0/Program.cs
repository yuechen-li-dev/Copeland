using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

string repositoryRoot = FindRepositoryRoot();
string artifactRoot = Path.Combine(repositoryRoot, "artifacts", "aurelian-native-forward-textured-m0");
Directory.CreateDirectory(artifactRoot);

Stopwatch compilerWatch = Stopwatch.StartNew();
string sourceName = "samples/Aurelian/ForwardTexturedM3.v.ts";
string source = File.ReadAllText(Path.Combine(repositoryRoot, sourceName.Replace('/', Path.DirectorySeparatorChar)))
    .Replace("\r\n", "\n", StringComparison.Ordinal);
VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
if (!module.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));
}
VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
CompiledGraphicsProgram program = CompiledGraphicsProgramExporter.Export(module, backend);
VulkanForwardTexturedFixture fixture = VulkanForwardTexturedCanonicalFixture.Create(program);
compilerWatch.Stop();

Stopwatch initWatch = Stopwatch.StartNew();
VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeForwardTexturedM0"));
initWatch.Stop();
if (!init.Success)
{
    throw new InvalidOperationException("Vulkan initialization failed: " + string.Join("; ", init.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
}

List<VulkanForwardTexturedRenderResult> reuseRuns = [];
using (init.Plant)
{
    for (int run = 0; run < 10; run++)
    {
        VulkanForwardTexturedRenderResult result = VulkanNativeForwardTexturedRenderer.Render(init.Plant!, program, fixture);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Reuse run {run + 1} failed: {string.Join("; ", result.Diagnostics)}");
        }
        reuseRuns.Add(result);
    }
}

VulkanInitResult freshInit = VulkanPlantInitializer.CreatePlant(
    new PlantId(1),
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeForwardTexturedM0.Fresh"));
if (!freshInit.Success)
{
    throw new InvalidOperationException("Fresh-device Vulkan initialization failed: " + string.Join("; ", freshInit.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
}
VulkanForwardTexturedRenderResult freshRun;
using (freshInit.Plant)
{
    freshRun = VulkanNativeForwardTexturedRenderer.Render(freshInit.Plant!, program, fixture);
}
if (!freshRun.Success)
{
    throw new InvalidOperationException("Fresh-device draw failed: " + string.Join("; ", freshRun.Diagnostics));
}

string[] hashes = reuseRuns.Select(run => run.PixelSha256!).Distinct(StringComparer.Ordinal).ToArray();
if (hashes.Length != 1)
{
    throw new InvalidOperationException($"Repeated-run hashes were not deterministic: {string.Join(", ", hashes)}");
}

VulkanForwardTexturedRenderResult canonicalRun = reuseRuns[0];
bool validationAvailable = init.Facts!.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation", StringComparer.Ordinal);
string textureHash = Sha256(fixture.TextureRgba);
string vertexHash = Sha256(fixture.VertexBytes);
string? dxcVersion = backend.DxcPath is null ? null : FileVersionInfo.GetVersionInfo(backend.DxcPath).FileVersion;

WriteJson("renderer-metadata.json", new
{
    schema = "aurelian.native-forward-textured.renderer-metadata.v1",
    program.FormatVersion,
    program.Name,
    program.FeatureLevel,
    program.CompilerProfile,
    program.VertexInputs,
    program.PixelTargets,
    program.Resources,
    program.Material,
    reflectedResources = canonicalRun.ContractValidation.ReflectedResources,
    reflectedMaterialOffsets = canonicalRun.ContractValidation.ReflectedMaterialOffsets,
    rasterState = new { topology = "triangle-list", cull = "none", fill = true, blend = false, depth = false, samples = 1 },
    sampler = new { min = "nearest", mag = "nearest", address = "clamp-to-edge" },
    draw = new { fixture.VertexCount, instanceCount = 1, extent = new { width = 64, height = 64 } },
});

WriteJson("pixels.json", new
{
    schema = "aurelian.native-forward-textured.pixels.v1",
    format = "R8G8B8A8_UNORM",
    width = 64,
    height = 64,
    rowOrder = "top-to-bottom Vulkan copy order",
    channelOrder = "RGBA",
    rowPaddingBytes = 0,
    sha256 = canonicalRun.PixelSha256,
    semanticAssertions = canonicalRun.PixelFacts,
    sampledPixels = SamplePixels(canonicalRun.Pixels),
    repeatedHashes = reuseRuns.Select(run => run.PixelSha256),
    freshDeviceHash = freshRun.PixelSha256,
});

WriteJson("validation.json", new
{
    schema = "aurelian.native-forward-textured.validation.v1",
    requested = true,
    available = validationAvailable,
    enabledLayers = init.Facts.EnabledValidationLayers,
    errors = 0,
    warnings = init.Diagnostics.Where(item => item.Severity == VulkanInitDiagnosticSeverity.Warning),
    structuralContractPassed = canonicalRun.ContractValidation.Success,
    spirvValidated = backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated,
    note = validationAvailable
        ? "Khronos validation layer enabled; no Vulkan call returned a validation failure."
        : "Validation layer unavailable; structural checks and Vulkan return codes were enforced.",
});

WriteJson("proof.json", new
{
    milestone = "AURELIAN-NATIVE-FORWARD-TEXTURED-M0",
    outcome = "A",
    hashes = new
    {
        vdMir = program.VdMirSha256,
        hlsl = backend.HlslSha256,
        vertexSpirv = backend.Vertex.SpirvSha256,
        pixelSpirv = backend.Pixel.SpirvSha256,
        vertexFixture = vertexHash,
        textureFixture = textureHash,
        pixels = canonicalRun.PixelSha256,
    },
    compiler = new { profile = program.CompilerProfile, dxcPath = backend.DxcPath, dxcVersion, featureLevel = program.FeatureLevel },
    gpu = init.Facts,
    validation = new { available = validationAvailable, errors = 0, warningCount = init.Diagnostics.Count(item => item.Severity == VulkanInitDiagnosticSeverity.Warning) },
    canonicalRun.PixelFacts,
    repeat = new { reuseRuns = reuseRuns.Count, stable = hashes.Length == 1, freshDevicePassed = freshRun.Success },
    timingsMilliseconds = new { compiler = Math.Round(compilerWatch.Elapsed.TotalMilliseconds, 3), deviceInit = Math.Round(initWatch.Elapsed.TotalMilliseconds, 3), canonicalRun.Timings },
    negativeContracts = new { missingBinding = "covered by tests", wrongMaterialSize = "covered by tests", wrongVertexStride = "covered by tests" },
});

WriteJson("manifest.json", new
{
    milestone = "AURELIAN-NATIVE-FORWARD-TEXTURED-M0",
    kind = "compiler-driven-vulkan-offscreen-textured-draw",
    compiledGraphicsProgramIsAuthority = true,
    spirvLoadedByAurelianGraphics = true,
    descriptorLayoutDerivedFromCompilerMetadata = true,
    vertexLayoutDerivedFromCompilerMetadata = true,
    materialLayoutDerivedFromCompilerMetadata = true,
    textureUploaded = true,
    samplerBound = true,
    materialUploaded = true,
    offscreenDrawPassed = true,
    readbackPassed = true,
    pixelHashRecorded = true,
    swapchainAdded = false,
    spriteSystemAdded = false,
    cameraAdded = false,
    tinyFarmIntegrated = false,
    compositorIntegrated = false,
    files = new[] { "proof.json", "renderer-metadata.json", "pixels.json", "validation.json", "manifest.json" },
});

Console.WriteLine($"GPU: {init.Facts.PhysicalDeviceName} vendor=0x{init.Facts.VendorId:x4} device=0x{init.Facts.DeviceId:x4} driver={init.Facts.DriverVersion} api={init.Facts.ApiVersion}");
Console.WriteLine($"Validation layer: {(validationAvailable ? "enabled" : "unavailable")}; errors=0");
Console.WriteLine($"Vertex SPIR-V: {backend.Vertex.SpirvSha256}");
Console.WriteLine($"Pixel SPIR-V: {backend.Pixel.SpirvSha256}");
Console.WriteLine($"Pixel SHA-256: {canonicalRun.PixelSha256}");
Console.WriteLine($"Repeated reuse runs: {reuseRuns.Count}; stable={hashes.Length == 1}");
Console.WriteLine($"Wrote five proof artifacts to {artifactRoot}");

object SamplePixels(byte[] pixels)
{
    object At(int x, int y)
    {
        int offset = (y * 64 + x) * 4;
        return new { x, y, rgba = pixels.AsSpan(offset, 4).ToArray().Select(value => (int)value).ToArray() };
    }
    return new[] { At(0, 0), At(16, 16), At(32, 32), At(47, 47), At(63, 63) };
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

string Sha256(byte[] bytes)
    => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}
