@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record OutputMaterial { parameters: float4; }
stream OutputResources {
    @binding(0) output: OutputMaterial;
    @binding(1) hdr: Texture2D<float4>;
    @binding(2) hdrSampler: Sampler;
}
stream OutputInput { @location(0) position: float2; }
stream OutputVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream OutputColor { @target(0) color: float4; }
function Filmic(x: f32): f32 {
    const limited: f32 = Min(x, 60000.0);
    return Clamp((limited * (2.51 * limited + 0.03)) / (limited * (2.43 * limited + 0.59) + 0.14), 0.0, 1.0);
}
function EncodeSrgb(x: f32): f32 {
    if (x <= 0.0031308)
    {
        return x * 12.92;
    }
    return 1.055 * Pow(x, 1.0 / 2.4) - 0.055;
}
function OutputChannel(value: f32, parameters: float4): f32 {
    var x: f32 = Max(value * parameters.x, 0.0);
    if (parameters.y > 0.5)
    {
        x = Filmic(x);
    }
    else { x = Clamp(x, 0.0, 1.0); }
    if (parameters.z > 0.5)
    {
        x = EncodeSrgb(x);
    }
    return x;
}
@vertex
function VertexMain(input: OutputInput): OutputVaryings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}
@pixel
function PixelMain(input: OutputVaryings, resources: OutputResources): OutputColor {
    const sampledColor: float4 = Sample(resources.hdr, resources.hdrSampler, input.uv);
    return { color: float4(OutputChannel(sampledColor.x, resources.output.parameters), OutputChannel(sampledColor.y, resources.output.parameters),
        OutputChannel(sampledColor.z, resources.output.parameters), 1.0) };
}
