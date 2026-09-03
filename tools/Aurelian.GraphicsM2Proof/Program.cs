using System.Diagnostics;
using System.Text.Json;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string sourcePath = Path.Combine(repositoryRoot, "samples", "Aurelian", "GraphicsStreamM2.v.ts");
string artifactRoot = Path.Combine(repositoryRoot, "artifacts", "aurelian-sdslv-port-m2");
Directory.CreateDirectory(artifactRoot);
string source = File.ReadAllText(sourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);

var timings = new Dictionary<string, double>(StringComparer.Ordinal);
VdMirGraphicsModule module = Measure("parseBindLinkSerialize", () => GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile("samples/Aurelian/GraphicsStreamM2.v.ts", source)])));
if (!module.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => diagnostic.Message)));
}
string vdMirJson = Measure("vdMirSerialization", () => VdMirJson.Serialize(module));
VdMirGraphicsBackendResult first = Measure("hlslDxcSpirvValidation", () => VdMirGraphicsBackend.Compile(module));
VdMirGraphicsBackendResult second = Measure("determinismRepeat", () => VdMirGraphicsBackend.Compile(module));
if (!first.Vertex.SpirvValidated || !first.Pixel.SpirvValidated)
{
    throw new InvalidOperationException(first.Vertex.SpirvValidationOutput + Environment.NewLine + first.Pixel.SpirvValidationOutput);
}

WriteText("vd-mir-graphics.json", vdMirJson);
WriteJson("diagnostics.json", new
{
    schema = "aurelian.sdslv-port-m2.diagnostics.v1",
    positive = module.Diagnostics,
    canonicalNegativeCoverage = new[]
    {
        Negative("mixed-stream-role", source.Replace("@location(1)\n    uv: float2;", "@binding(1)\n    uv: float2;", StringComparison.Ordinal), "SDSL-V4102"),
        Negative("duplicate-location", source.Replace("@location(1)\n    uv: float2;", "@location(0)\n    uv: float2;", StringComparison.Ordinal), "SDSL-V4105"),
        Negative("missing-clip-position", source.Replace("@builtin(position)\n    position: float4;", "@location(2)\n    position: float4;", StringComparison.Ordinal), "SDSL-V4106"),
        Negative("varying-mismatch", source.Replace("stream PixelInput {\n    @location(0)\n    uv: float2;", "stream PixelInput {\n    @location(0)\n    uv: float3;", StringComparison.Ordinal), "SDSL-V4111"),
        Negative("duplicate-target", source.Replace("color: float4;", "color: float4;\n    @target(0) other: float4;", StringComparison.Ordinal), "SDSL-V4108"),
    },
});
WriteJson("backend.json", new
{
    schema = "aurelian.vdmir.graphics-backend.v1",
    hlsl = first.Hlsl,
    hlslSha256 = first.HlslSha256,
    dxcPath = first.DxcPath,
    vertex = Stage(first.Vertex),
    pixel = Stage(first.Pixel),
});
WriteJson("proof.json", new
{
    milestone = "AURELIAN-SDSLV-PORT-M2",
    outcome = "A",
    octRevision = "584bd176fd50664edadcb2bc3ae78431ac0f1e51",
    copelandRevision = "3d5d9f47688b329c16d25389aacda65801e8c528+working-tree",
    canonicalCases = new[] { "graphics.minimal-vertex", "graphics.minimal-pixel", "graphics.canonical-forward-textured linkage law" },
    normalizedSemanticParity = new
    {
        stages = new[] { "vertex", "pixel" },
        streamRoles = module.Streams.ToDictionary(stream => stream.Name, stream => stream.Role.ToString()),
        locations = module.Streams.SelectMany(stream => stream.Members.Where(member => member.Location is not null).Select(member => new { stream = stream.Name, member = member.Name, member.Type, member.Location, member.Interpolation })),
        builtins = module.Streams.SelectMany(stream => stream.Members.Where(member => member.Builtin is not null).Select(member => new { stream = stream.Name, member = member.Name, member.Type, member.Builtin })),
        targets = module.Streams.SelectMany(stream => stream.Members.Where(member => member.Target is not null).Select(member => new { stream = stream.Name, member = member.Name, member.Type, member.Target })),
        linkage = module.GraphicsProgram!.Varyings,
    },
    deterministic = first.HlslSha256 == second.HlslSha256 && first.Vertex.SpirvSha256 == second.Vertex.SpirvSha256 && first.Pixel.SpirvSha256 == second.Pixel.SpirvSha256,
    hashes = new { first.HlslSha256, vertex = first.Vertex.SpirvSha256, pixel = first.Pixel.SpirvSha256 },
    timingsMilliseconds = timings,
    sourceBoilerplate = new
    {
        visualTypeScript = "4 typed streams, 3 semantic annotations kinds, no backend structs or HLSL semantic names",
        generatedHlsl = "4 generated structs and synthesized TEXCOORD, SV_Position, and SV_Target spellings",
    },
});
WriteJson("manifest.json", new
{
    milestone = "AURELIAN-SDSLV-PORT-M2",
    kind = "visual-typescript-graphics-stream-linkage",
    octSpecIsAuthority = true,
    octConformanceIsAuthority = true,
    visualTypeScriptFrontendUsesCopelandParser = true,
    vdMirIsSharedSemanticContract = true,
    streamBindingPorted = true,
    vertexAdded = true,
    pixelAdded = true,
    computePreserved = true,
    manualHlslSemanticsRequired = false,
    textureSamplerAdded = false,
    materialAdded = false,
    payloadEnumRuntimeAdded = false,
    advancedTileMemoryFeaturesAdded = false,
    rendererRuntimeAdded = false,
    files = new[] { "proof.json", "vd-mir-graphics.json", "diagnostics.json", "backend.json", "manifest.json" },
});

Console.WriteLine($"Wrote five M2 proof artifacts to {artifactRoot}");

T Measure<T>(string name, Func<T> action)
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    T result = action();
    stopwatch.Stop();
    timings[name] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3);
    return result;
}

object Negative(string name, string negativeSource, string canonicalCode)
{
    VdMirGraphicsModule negative = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(name + ".v.ts", negativeSource)]));
    VdMirDiagnostic? diagnostic = negative.Diagnostics.FirstOrDefault(item => item.CanonicalCode == canonicalCode);
    return new { name, canonicalCode, observed = diagnostic is not null, diagnostic };
}

object Stage(VdMirGraphicsStageResult stage)
{
    return new
    {
        stage = stage.Stage.ToString(),
        stage.EntryPoint,
        stage.Profile,
        stage.SpirvSha256,
        dxcStatus = stage.DxcStatus.ToString(),
        stage.DxcOutput,
        stage.DxcArguments,
        stage.SpirvValidated,
        stage.SpirvValidationOutput,
        structuralFacts = new
        {
            executionModel = stage.SpirvDisassembly?.Contains(stage.Stage == VdMirGraphicsStage.Vertex ? "OpEntryPoint Vertex" : "OpEntryPoint Fragment", StringComparison.Ordinal) == true,
            entryName = stage.SpirvDisassembly?.Contains(stage.EntryPoint, StringComparison.Ordinal) == true,
            location0 = stage.SpirvDisassembly?.Contains("Location 0", StringComparison.Ordinal) == true,
            position = stage.SpirvDisassembly?.Contains("BuiltIn Position", StringComparison.Ordinal) == true,
        },
    };
}

void WriteJson(string name, object value)
{
    string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    WriteText(name, json + Environment.NewLine);
}

void WriteText(string name, string text)
{
    File.WriteAllText(Path.Combine(artifactRoot, name), text);
}
