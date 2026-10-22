@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record OutputMaterial { parameters: float4; }
stream OutputResources {
    @binding(0) output: OutputMaterial;
    @binding(1) hdr: Texture2D<float4>;
    @binding(2) hdrSampler: Sampler;
    @binding(3) bloom: Texture2D<float4>;
    @binding(4) bloomSampler: Sampler;
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
function RetainedChannel(value: f32, luminance: f32, parameters: float4): f32 {
    const x: f32 = Max(value * parameters.x, 0.0);
    const retention: f32 = Clamp(parameters.y - 1.0, 0.0, 1.0);
    const luminanceMapped: f32 = x * Filmic(luminance) / Max(luminance, 0.000001);
    var mapped: f32 = Clamp(Filmic(x) * (1.0 - retention) + luminanceMapped * retention, 0.0, 1.0);
    if (parameters.z > 0.5) {
        mapped = EncodeSrgb(mapped);
    }
    return mapped;
}
function UnboundedChannel(value: f32, luminance: f32, exposure: f32, retention: f32): f32 {
    const x: f32 = Max(value * exposure, 0.0);
    return Filmic(x) * (1.0 - retention) + x * Filmic(luminance) / Max(luminance, 0.000001) * retention;
}
function GamutDistance(value: f32, center: f32): f32 {
    if (value > center) {
        return (value - center) / Max(1.0 - center, 0.000001);
    }
    return (center - value) / Max(center, 0.000001);
}
function CompressedColor(color: float4, parameters: float4): float3 {
    const luminance: f32 = Max((0.2126 * color.x + 0.7152 * color.y + 0.0722 * color.z) * parameters.x, 0.0);
    const retention: f32 = Clamp(parameters.y - 3.0, 0.0, 1.0);
    const raw: float3 = float3(UnboundedChannel(color.x, luminance, parameters.x, retention),
        UnboundedChannel(color.y, luminance, parameters.x, retention),
        UnboundedChannel(color.z, luminance, parameters.x, retention));
    const center: f32 = Clamp(0.2126 * raw.x + 0.7152 * raw.y + 0.0722 * raw.z, 0.0, 1.0);
    const distance: f32 = Max(GamutDistance(raw.x, center), Max(GamutDistance(raw.y, center), GamutDistance(raw.z, center)));
    var scale: f32 = 1.0;
    if (distance > 0.8) {
        const excess: f32 = distance - 0.8;
        const compressed: f32 = 0.8 + 0.2 * excess / (excess + 0.2);
        scale = compressed / distance;
    }
    return float3(Clamp(center + (raw.x - center) * scale, 0.0, 1.0),
        Clamp(center + (raw.y - center) * scale, 0.0, 1.0),
        Clamp(center + (raw.z - center) * scale, 0.0, 1.0));
}
@vertex
function VertexMain(input: OutputInput): OutputVaryings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}
@pixel
function PixelMain(input: OutputVaryings, resources: OutputResources): OutputColor {
    const sampledColor: float4 = Sample(resources.hdr, resources.hdrSampler, input.uv);
    const glow: float4 = Sample(resources.bloom, resources.bloomSampler, input.uv);
    const intensity: f32 = resources.output.parameters.w;
    const combined: float4 = float4(sampledColor.x + glow.x * intensity, sampledColor.y + glow.y * intensity,
        sampledColor.z + glow.z * intensity, 1.0);
    // Tone control: 0 disabled; 1..2 legacy blend; 3..4 soft gamut compression.
    if (resources.output.parameters.y > 2.5) {
        const mapped: float3 = CompressedColor(combined, resources.output.parameters);
        if (resources.output.parameters.z > 0.5) {
            return { color: float4(EncodeSrgb(mapped.x), EncodeSrgb(mapped.y), EncodeSrgb(mapped.z), 1.0) };
        }
        return { color: float4(mapped.x, mapped.y, mapped.z, 1.0) };
    }
    if (resources.output.parameters.y > 1.0) {
        const luminance: f32 = Max((0.2126 * combined.x + 0.7152 * combined.y + 0.0722 * combined.z)
            * resources.output.parameters.x, 0.0);
        return { color: float4(RetainedChannel(combined.x, luminance, resources.output.parameters),
            RetainedChannel(combined.y, luminance, resources.output.parameters),
            RetainedChannel(combined.z, luminance, resources.output.parameters), 1.0) };
    }
    return { color: float4(OutputChannel(combined.x, resources.output.parameters), OutputChannel(combined.y, resources.output.parameters),
        OutputChannel(combined.z, resources.output.parameters), 1.0) };
}
