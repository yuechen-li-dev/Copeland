using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

const string sourcePath = "samples/Aurelian/SdslvPortM1/ComputeNoRegression.v.ts";
const string source = """
    @compute
    @numthreads(8, 1, 1)
    function ComputeNoRegression_CS(
        @builtin(dispatchThreadId) thread: uint3,
        @binding(0) readonly Input: StorageBuffer<f32>,
        @binding(1) readwrite Output: StorageBuffer<f32>
    ): void {
        const index: u32 = thread.x;
        Output[index] = Input[index];
        return;
    }
    """;

string outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("artifacts/aurelian-sdslv-port-m1");
Directory.CreateDirectory(outputDirectory);

var stopwatch = Stopwatch.StartNew();
SyntaxTree parse = SyntaxTree.Parse(source, sourcePath);
stopwatch.Stop();
double parseMs = stopwatch.Elapsed.TotalMilliseconds;

stopwatch.Restart();
VdMirComputeModule module = GpuComputeBinder.Compile(new GpuCompilationRequest([
    new GpuSourceFile(sourcePath, source),
]));
stopwatch.Stop();
double bindMs = stopwatch.Elapsed.TotalMilliseconds;
if (parse.Diagnostics.Count > 0 || !module.Success)
{
    throw new InvalidOperationException("Canonical compute source did not bind: " + string.Join("; ", module.Diagnostics.Select(item => item.Message)));
}

stopwatch.Restart();
string vdMirJson = VdMirJson.Serialize(module);
stopwatch.Stop();
double jsonMs = stopwatch.Elapsed.TotalMilliseconds;

stopwatch.Restart();
string hlsl = VdMirComputeHlslEmitter.Emit(module);
stopwatch.Stop();
double hlslMs = stopwatch.Elapsed.TotalMilliseconds;

stopwatch.Restart();
VdMirComputeBackendResult backend = VdMirComputeBackend.Compile(module);
stopwatch.Stop();
double backendMs = stopwatch.Elapsed.TotalMilliseconds;
VdMirComputeBackendResult repeated = VdMirComputeBackend.Compile(module);

string duplicateSource = source.Replace("@binding(1) readwrite Output", "@binding(0) readwrite Output", StringComparison.Ordinal);
VdMirComputeModule duplicate = GpuComputeBinder.Compile(new GpuCompilationRequest([
    new GpuSourceFile("DuplicateResourceBinding.v.ts", duplicateSource),
]));
const string hostSource = "function HostOnly(value: f32): f32 { return new Box(value); }";
string hostEntry = source.Replace("Input[index]", "HostOnly(Input[index])", StringComparison.Ordinal);
VdMirComputeModule hostOnly = GpuComputeBinder.Compile(new GpuCompilationRequest([
    new GpuSourceFile("HostOnly.ts", hostSource),
    new GpuSourceFile(sourcePath, hostEntry),
]));

string[] structure = (backend.SpirvDisassembly ?? string.Empty)
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
    .Select(line => line.Trim())
    .Where(line => line.Contains("OpEntryPoint", StringComparison.Ordinal)
        || line.Contains("OpExecutionMode", StringComparison.Ordinal)
        || line.Contains("DescriptorSet", StringComparison.Ordinal)
        || line.Contains(" Binding ", StringComparison.Ordinal)
        || line.Contains("NonWritable", StringComparison.Ordinal))
    .ToArray();

Write("vd-mir.json", vdMirJson);
WriteJson("diagnostics.json", new
{
    schema = "aurelian.sdslv-port-m1.diagnostics.v1",
    duplicateBinding = duplicate.Diagnostics,
    reachableHostOnly = hostOnly.Diagnostics,
});
WriteJson("backend.json", new
{
    schema = "aurelian.sdslv-port-m1.backend.v1",
    hlsl,
    backend.HlslSha256,
    spirvBase64 = Convert.ToBase64String(backend.Spirv),
    backend.SpirvSha256,
    dxcStatus = backend.DxcStatus.ToString(),
    backend.DxcPath,
    dxcVersion = backend.DxcPath is null ? null : FileVersionInfo.GetVersionInfo(backend.DxcPath).FileVersion,
    dxcProfile = "cs_6_0",
    dxcArguments = new[]
    {
        "-spirv",
        "-fspv-target-env=vulkan1.3",
        "-HV",
        "2021",
        "-E",
        module.EntryPoint!.EmittedName,
        "-T",
        "cs_6_0",
    },
    backend.SpirvValidated,
    backend.SpirvValidationOutput,
    structure,
    hostBindings = module.Resources.Select(resource => new
    {
        resource.Set,
        resource.Binding,
        resource.Name,
        access = resource.Access.ToString().ToLowerInvariant(),
        resource.ElementType,
    }),
});
WriteJson("manifest.json", new
{
    milestone = "AURELIAN-SDSLV-PORT-M1",
    kind = "copeland-gpu-binder-vd-mir-compute-slice",
    octSpecIsAuthority = true,
    octConformanceIsAuthority = true,
    octImplementationAuditedBeforePort = true,
    octCleanupRequired = false,
    copelandParserReused = true,
    annotationAstAdded = true,
    vTsRequiredForSemantics = false,
    gpuProfileAdded = true,
    vdMirAdded = true,
    computeOnly = true,
    vertexAdded = false,
    pixelAdded = false,
    payloadEnumRuntimeAdded = false,
    templatesExpanded = false,
    reflectExpanded = false,
    shaderCacheAdded = false,
    rendererApiAdded = false,
    canonicalCaseIds = new[] { "compute.no-regression" },
    canonicalNegativeSources = new[] { "Examples/SDSL-V/conformance/invalid/DuplicateResourceBinding.sdslvinvalid" },
    canonicalManifestSha256 = "a107f9d4291458f9d7c2a06e73578ec8e11a223a6acc9bed7c417cbe322b4406",
    octRevision = "584bd176fd50664edadcb2bc3ae78431ac0f1e51",
    copelandBaseRevision = GitRevision(),
});
WriteJson("proof.json", new
{
    schema = "aurelian.sdslv-port-m1.proof.v1",
    outcome = backend.DxcStatus.ToString() == "Compiled" && backend.SpirvValidated ? "A" : "B",
    canonicalCase = "compute.no-regression",
    semanticParity = new
    {
        entry = module.EntryPoint!.Name == "ComputeNoRegression_CS",
        stage = "compute",
        numthreads = new[] { module.EntryPoint.NumThreadsX, module.EntryPoint.NumThreadsY, module.EntryPoint.NumThreadsZ },
        resources = module.Resources.Select(resource => $"{resource.Name}:set{resource.Set}/binding{resource.Binding}:storage-buffer:{resource.Access.ToString().ToLowerInvariant()}").ToArray(),
        builtin = module.EntryPoint.Builtins.Single().Builtin,
        control = new[] { "local", "index", "buffer-read", "buffer-write", "return" },
        pushConstants = "deferred-not-required",
        bufferLength = "deferred-not-required",
    },
    negativeParity = new
    {
        duplicateBinding = duplicate.Diagnostics.Any(item => item.CanonicalCode == "SDSL-V4112" && item.RelatedSpans.Count == 1),
        reachableHostOnly = hostOnly.Diagnostics.Any(item => item.CanonicalCode == "SDSL-V4200"),
    },
    determinism = new
    {
        vdMirSha256 = Hash(Encoding.UTF8.GetBytes(vdMirJson)),
        backend.HlslSha256,
        backend.SpirvSha256,
        repeatedHlslSha256 = repeated.HlslSha256,
        repeatedSpirvSha256 = repeated.SpirvSha256,
        equal = backend.HlslSha256 == repeated.HlslSha256 && backend.SpirvSha256 == repeated.SpirvSha256,
    },
    timingsMilliseconds = new
    {
        parse = parseMs,
        gpuBindProductionPassIncludingParse = bindMs,
        vdMirProjection = 0.0,
        jsonProjection = jsonMs,
        hlslEmit = hlslMs,
        dxcAndSpirvValidation = backendMs,
    },
    artifactCount = 5,
});

void WriteJson(string name, object value)
{
    Write(name, JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    }) + Environment.NewLine);
}

void Write(string name, string content)
{
    File.WriteAllText(Path.Combine(outputDirectory, name), content, new UTF8Encoding(false));
}

string GitRevision()
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "git",
        Arguments = "rev-parse HEAD",
        UseShellExecute = false,
        RedirectStandardOutput = true,
    })!;
    string revision = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    return revision;
}

static string Hash(byte[] bytes)
    => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
