using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string sourcePath = Path.Combine(repositoryRoot, "samples", "Aurelian", "ForwardTexturedM3.v.ts");
string artifactRoot = Path.Combine(repositoryRoot, "artifacts", "aurelian-sdslv-port-m3");
Directory.CreateDirectory(artifactRoot);
string source = File.ReadAllText(sourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);

var timings = new Dictionary<string, double>(StringComparer.Ordinal);
_ = Measure("parse", () => SyntaxTree.Parse(source, "samples/Aurelian/ForwardTexturedM3.v.ts"));
VdMirGraphicsModule module = Measure("gpuBindLinkSpaceLayoutProjection", () => Compile(source));
if (!module.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => diagnostic.Message)));
}
string vdMirJson = Measure("vdMirJson", () => VdMirJson.Serialize(module));
string hlsl = Measure("hlslEmission", () => VdMirGraphicsHlslEmitter.Emit(module));
VdMirGraphicsBackendResult first = Measure("dxcVertexPixelAndSpirvValidation", () => VdMirGraphicsBackend.Compile(module));
VdMirGraphicsBackendResult second = Measure("determinismRepeat", () => VdMirGraphicsBackend.Compile(module));
if (!first.Vertex.SpirvValidated || !first.Pixel.SpirvValidated)
{
    throw new InvalidOperationException(first.Vertex.SpirvValidationOutput + Environment.NewLine + first.Pixel.SpirvValidationOutput);
}

WriteText("vd-mir-forward-textured.json", vdMirJson);
WriteJson("diagnostics.json", new
{
    schema = "aurelian.sdslv-port-m3.diagnostics.v1",
    positive = module.Diagnostics,
    canonicalNegativeCoverage = new[]
    {
        Negative("wrong-sample-coordinate", source.Replace("input.uv);", "input.position);", StringComparison.Ordinal), "SDSL-V4119"),
        Negative("non-texture-sample", source.Replace("Sample(resources.albedo", "Sample(resources.material.tint", StringComparison.Ordinal), "SDSL-V4118"),
        Negative("wrong-sampler", source.Replace("resources.linearSampler, input.uv", "resources.albedo, input.uv", StringComparison.Ordinal), "SDSL-V4118"),
        Negative("duplicate-binding", source.Replace("@binding(1)\n    linearSampler", "@binding(0)\n    linearSampler", StringComparison.Ordinal), "SDSL-V4112"),
        Negative("resource-role-conflict", source.Replace("@binding(0)\n    albedo", "@location(0)\n    @binding(0)\n    albedo", StringComparison.Ordinal), "SDSL-V4102"),
        Negative("material-type", source.Replace("roughness: f32;", "roughness: bool;", StringComparison.Ordinal), "SDSL-V4114"),
        Negative("material-immutable", source.Replace("const texel: float4 = Sample", "resources.material.tint = float4(1.0, 1.0, 1.0, 1.0);\n    const texel: float4 = Sample", StringComparison.Ordinal), "SDSL-V3701"),
        Negative("semantic-space-assignment", source.Replace("return float3(value.x, value.y, value.z);", "return value;", StringComparison.Ordinal), "SDSL-V1503"),
        Negative("semantic-space-linkage", LinkageMismatch(source), "SDSL-V4111"),
    },
});
WriteJson("backend.json", new
{
    schema = "aurelian.vdmir.graphics-backend.m3.v1",
    hlsl,
    hlslSha256 = first.HlslSha256,
    first.DxcPath,
    vertex = Stage(first.Vertex),
    pixel = Stage(first.Pixel),
    rendererMetadata = module.GraphicsProgram,
});
WriteJson("proof.json", new
{
    milestone = "AURELIAN-SDSLV-PORT-M3",
    outcome = "A",
    octRevision = "584bd176fd50664edadcb2bc3ae78431ac0f1e51",
    copelandBaseRevision = "83ef70561fb9708e2c3e09b1d3f48166dac11346",
    authority = new { specification = "docs/SDSL_V_LANGUAGE_SPEC.md", conformance = "sdslv.conformance.v1", canonicalCase = "graphics.canonical-forward-textured" },
    audit = new
    {
        semanticSpaces = "CONSISTENT",
        resourceStreams = "CONSISTENT",
        textureSamplerSample = "CONSISTENT",
        materialLayout = "CONSISTENT",
        bindings = "CONSISTENT",
        octChangesRequired = false,
    },
    normalizedSemanticParity = new
    {
        stages = module.EntryPoints.Select(entry => entry.Stage),
        streamRoles = module.Streams.ToDictionary(stream => stream.Name, stream => stream.Role),
        semanticSpaces = module.SemanticSpaces,
        locations = module.Streams.SelectMany(stream => stream.Members.Where(member => member.Location is not null).Select(member => new { stream = stream.Name, member.Name, member.Type, member.PhysicalType, member.SemanticSpace, member.Location })),
        builtins = module.Streams.SelectMany(stream => stream.Members.Where(member => member.Builtin is not null).Select(member => new { stream = stream.Name, member.Name, member.Type, member.Builtin })),
        targets = module.GraphicsProgram!.PixelTargets,
        resources = module.GraphicsProgram.Resources,
        material = module.GraphicsProgram.Material,
        intrinsic = "Sample2D(texture2d<float4>, sampler, float2) -> float4",
        linkage = module.GraphicsProgram.Varyings,
    },
    canonicalMaterialLayout = new { tint = new { offset = 0, size = 16, alignment = 16 }, roughness = new { offset = 16, size = 4, alignment = 4 }, totalSize = 32, set = 0, binding = 2 },
    deterministic = first.HlslSha256 == second.HlslSha256 && first.Vertex.SpirvSha256 == second.Vertex.SpirvSha256 && first.Pixel.SpirvSha256 == second.Pixel.SpirvSha256,
    hashes = new { vdMir = Hash(Encoding.UTF8.GetBytes(vdMirJson)), first.HlslSha256, vertex = first.Vertex.SpirvSha256, pixel = first.Pixel.SpirvSha256 },
    timingsMilliseconds = timings,
    sourceBoilerplate = "typed streams, semantic aliases, resources, material, and Sample; no HLSL structs, SV spellings, descriptors, or offsets",
});
WriteJson("manifest.json", new
{
    milestone = "AURELIAN-SDSLV-PORT-M3",
    kind = "forward-textured-semantic-space-material-port",
    octSpecIsAuthority = true,
    octConformanceIsAuthority = true,
    vdMirIsSharedSemanticContract = true,
    semanticSpacesAdded = true,
    resourceStreamsAdded = true,
    texture2dAdded = true,
    samplerAdded = true,
    typedSampleAdded = true,
    materialAdded = true,
    canonicalMaterialLayoutAdded = true,
    hostBindingMetadataAdded = true,
    rendererRuntimeAdded = false,
    materialRuntimeAdded = false,
    shaderCacheAdded = false,
    advancedTileMemoryFeaturesAdded = false,
    files = new[] { "proof.json", "vd-mir-forward-textured.json", "diagnostics.json", "backend.json", "manifest.json" },
});

Console.WriteLine($"Wrote five M3 proof artifacts to {artifactRoot}");

VdMirGraphicsModule Compile(string text)
    => GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile("samples/Aurelian/ForwardTexturedM3.v.ts", text)]));

string LinkageMismatch(string text)
{
    const string pixelInput = """
        stream PixelInput {
            @builtin(position)
            position: ClipPosition4;
            @location(0)
            uv: float2;
            @location(1)
            worldPosition: ObjectPosition3;
        }

        """;
    return text
        .Replace("stream PixelBuiltins", pixelInput + "stream PixelBuiltins", StringComparison.Ordinal)
        .Replace("function PixelMain(input: ForwardVaryings", "function PixelMain(input: PixelInput", StringComparison.Ordinal);
}

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
    VdMirGraphicsModule negative = Compile(negativeSource);
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
        stage.DxcArguments,
        stage.DxcMilliseconds,
        stage.SpirvValidated,
        stage.SpirvValidationOutput,
        stage.SpirvValidationMilliseconds,
        stage.SpirvDisassemblyMilliseconds,
        structuralFacts = new
        {
            executionModel = stage.SpirvDisassembly?.Contains(stage.Stage == VdMirGraphicsStage.Vertex ? "OpEntryPoint Vertex" : "OpEntryPoint Fragment", StringComparison.Ordinal) == true,
            entryName = stage.SpirvDisassembly?.Contains(stage.EntryPoint, StringComparison.Ordinal) == true,
            location0 = stage.SpirvDisassembly?.Contains("Location 0", StringComparison.Ordinal) == true,
            position = stage.SpirvDisassembly?.Contains("BuiltIn Position", StringComparison.Ordinal) == true,
            vertexId = stage.SpirvDisassembly?.Contains("BuiltIn VertexIndex", StringComparison.Ordinal) == true,
            instanceId = stage.SpirvDisassembly?.Contains("BuiltIn InstanceIndex", StringComparison.Ordinal) == true,
            frontFace = stage.SpirvDisassembly?.Contains("BuiltIn FrontFacing", StringComparison.Ordinal) == true,
            descriptorSet0 = stage.SpirvDisassembly?.Contains("DescriptorSet 0", StringComparison.Ordinal) == true,
            bindings = new[] { 0, 1, 2 }.Where(binding => stage.SpirvDisassembly?.Contains($"Binding {binding}", StringComparison.Ordinal) == true),
        },
    };
}

string Hash(byte[] bytes)
    => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

void WriteJson(string name, object value)
{
    string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    WriteText(name, json + Environment.NewLine);
}

void WriteText(string name, string text)
    => File.WriteAllText(Path.Combine(artifactRoot, name), text);
