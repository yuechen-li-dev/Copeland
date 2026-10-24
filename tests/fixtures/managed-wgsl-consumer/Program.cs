using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Gpu.Wgsl;

const string source = """
    @space(clip.position) type Clip = float4;
    stream Input { @location(0) position: float3; }
    stream Varyings { @builtin(position) position: Clip; @location(0) local: float3; }
    stream Output { @target(0) color: float4; @builtin(frag_depth) depth: f32; }
    @vertex function VertexMain(input: Input): Varyings {
        return { position: float4(input.position, 1.0), local: input.position };
    }
    @pixel function PixelMain(input: Varyings): Output {
        const distance: f32 = Sqrt(input.local.x * input.local.x + input.local.y * input.local.y);
        if (distance > 1.0) { Discard(); }
        return { color: float4(distance, 0.0, 0.0, 1.0), depth: distance / 3.0 };
    }
    """;
var first = Compile(source);
var replay = Compile(source);
var changed = Compile(source.Replace("distance / 3.0", "distance / 4.0"));
if (first.Code != replay.Code || first.SemanticHash != replay.SemanticHash || first.SemanticHash == changed.SemanticHash)
    throw new InvalidOperationException("Shader identity/determinism failed.");
if (!first.Code.Contains("@builtin(frag_depth)") || !first.Code.Contains("@vertex") || !first.Code.Contains("@fragment"))
    throw new InvalidOperationException("Shader stage/depth contract missing.");
var invalid = WgslGraphicsBackend.Compile(new GpuCompilationRequest([new GpuSourceFile("invalid.v.ts", "not a shader")]));
if (invalid.Success || invalid.Diagnostics.Count == 0) throw new InvalidOperationException("Invalid VTS was admitted.");
Console.WriteLine("Managed VTS/WGSL package consumer passed: deterministic shader, changed semantics, stage/depth contract and invalid-source rejection.");

static WgslGraphicsProgram Compile(string text)
{
    var result = WgslGraphicsBackend.Compile(new GpuCompilationRequest([new GpuSourceFile("consumer.v.ts", text)]));
    if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics));
    return result.Program!;
}
