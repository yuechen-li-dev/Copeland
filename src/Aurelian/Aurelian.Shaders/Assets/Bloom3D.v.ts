@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record BloomMaterial {
    // source texel size, threshold, soft knee
    filter: float4;
    // prefilter, second-input contribution, reserved
    operation: float4;
}
stream Resources {
    @binding(0) bloom: BloomMaterial;
    @binding(1) source: Texture2D<float4>;
    @binding(2) sourceSampler: Sampler;
    @binding(3) lower: Texture2D<float4>;
    @binding(4) lowerSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings { @builtin(position) position: ClipPosition4; @location(0) uv: float2; }
stream Output { @target(0) color: float4; }
@vertex
function VertexMain(input: Input): Varyings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}
function Prefilter(color: float4, settings: float4): float4 {
    const brightness: f32 = Max(color.x, Max(color.y, color.z));
    const knee: f32 = Max(settings.w, 0.00001);
    const soft: f32 = Clamp(brightness - settings.z + knee, 0.0, 2.0 * knee);
    const contribution: f32 = Max(brightness - settings.z, soft * soft / (4.0 * knee)) / Max(brightness, 0.00001);
    return color * float4(contribution, contribution, contribution, 0.0);
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const filter: float4 = resources.bloom.filter;
    var sum: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    // Separable tent footprint in one pass: broad highlights survive downsampling.
    for (var y: u32 = 0; y < 3; y = y + 1) {
        for (var x: u32 = 0; x < 3; x = x + 1) {
            const dx: f32 = Convert<f32>(x) - 1.0;
            const dy: f32 = Convert<f32>(y) - 1.0;
            const weight: f32 = (2.0 - Abs(dx)) * (2.0 - Abs(dy));
            var color: float4 = Sample(resources.source, resources.sourceSampler,
                float2(input.uv.x + dx * filter.x, input.uv.y + dy * filter.y));
            if (resources.bloom.operation.x > 0.5) { color = Prefilter(color, filter); }
            sum = Add4(sum, color * float4(weight, weight, weight, weight));
            total = total + weight;
        }
    }
    const low: float4 = Sample(resources.lower, resources.lowerSampler, input.uv);
    const addition: f32 = resources.bloom.operation.y;
    const result: float4 = Add4(Divide4(sum, total), low * float4(addition, addition, addition, 0.0));
    return { color: float4(result.x, result.y, result.z, 1.0) };
}

function Add4(a: float4, b: float4): float4 {
    return float4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
}
function Divide4(a: float4, divisor: f32): float4 {
    return float4(a.x / divisor, a.y / divisor, a.z / divisor, a.w / divisor);
}
