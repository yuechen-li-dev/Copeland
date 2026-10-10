// This shader incorporates the AGPL Aetheris policy; see Assets/Licenses.
import { TemporalPolicy } from "./TemporalPolicy";

@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record TemporalMaterial {
    // inverse dimensions, history valid, maximum history weight
    parameters: float4;
    // previous minus current jitter in UV, reserved
    jitter: float4;
    // current projection jitter in UV; reactive coverage uses single-frame reconstruction
    reconstruction: float4;
}
stream TemporalResources {
    @binding(0) temporal: TemporalMaterial;
    @binding(1) current: Texture2D<float4>;
    @binding(2) currentSampler: Sampler;
    @binding(3) motion: Texture2D<float4>;
    @binding(4) motionSampler: Sampler;
    @binding(5) history: Texture2D<float4>;
    @binding(6) historySampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output { @target(0) color: float4; }

@vertex
function VertexMain(input: Input): Varyings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}
function Luma(value: float4): f32 {
    return value.x * 0.2126 + value.y * 0.7152 + value.z * 0.0722;
}
function Minimum(a: float4, b: float4): float4 {
    return float4(Min(a.x, b.x), Min(a.y, b.y), Min(a.z, b.z), Min(a.w, b.w));
}
function Maximum(a: float4, b: float4): float4 {
    return float4(Max(a.x, b.x), Max(a.y, b.y), Max(a.z, b.z), Max(a.w, b.w));
}
function ClipHistory(value: float4, minimum: float4, maximum: float4): float4 {
    return float4(Clamp(value.x, minimum.x, maximum.x), Clamp(value.y, minimum.y, maximum.y),
        Clamp(value.z, minimum.z, maximum.z), value.w);
}
@pixel
function PixelMain(input: Varyings, resources: TemporalResources): Output {
    const parameters: float4 = resources.temporal.parameters;
    const current: float4 = Sample(resources.current, resources.currentSampler, input.uv);
    const motion: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    if (parameters.z < 0.5) {
        return { color: float4(current.x, current.y, current.z, motion.w) };
    }
    var previousUv: float2 = float2(motion.x - resources.temporal.jitter.x, motion.y - resources.temporal.jitter.y);
    var previousDepth: f32 = motion.z;
    if (motion.w >= 1.0) {
        previousUv = input.uv;
        previousDepth = 1.0;
    }
    if (previousUv.x < 0.0 || previousUv.x > 1.0 || previousUv.y < 0.0 || previousUv.y > 1.0
        || previousDepth < 0.0 || previousDepth > 1.0) {
        return { color: float4(current.x, current.y, current.z, motion.w) };
    }
    var minimum: float4 = current;
    var maximum: float4 = current;
    var minimumDepth: f32 = motion.w;
    var maximumDepth: f32 = motion.w;
    var coverageMotion: f32 = 0.0;
    for (var y: u32 = 0; y < 3; y = y + 1) {
        for (var x: u32 = 0; x < 3; x = x + 1) {
            const uv: float2 = float2(input.uv.x + (Convert<f32>(x) - 1.0) * parameters.x,
                input.uv.y + (Convert<f32>(y) - 1.0) * parameters.y);
            const neighbor: float4 = Sample(resources.current, resources.currentSampler, uv);
            const depth: float4 = Sample(resources.motion, resources.motionSampler, uv);
            minimum = Minimum(minimum, neighbor);
            maximum = Maximum(maximum, neighbor);
            minimumDepth = Min(minimumDepth, depth.w);
            maximumDepth = Max(maximumDepth, depth.w);
            if (depth.w < 1.0) {
                const dx: f32 = (depth.x - uv.x - resources.temporal.jitter.x) / parameters.x;
                const dy: f32 = (depth.y - uv.y - resources.temporal.jitter.y) / parameters.y;
                coverageMotion = Max(coverageMotion, Max(Abs(dx), Abs(dy)));
            }
        }
    }
    // Bilinear color with four independently qualified depth taps. A nearest depth
    // cannot represent the fractional foreground coverage retained in the color.
    const pixel: float2 = float2(previousUv.x / parameters.x - 0.5, previousUv.y / parameters.y - 0.5);
    const origin: float2 = float2(Floor(pixel.x), Floor(pixel.y));
    const fraction: float2 = float2(pixel.x - origin.x, pixel.y - origin.y);
    const movement: float2 = float2((previousUv.x - input.uv.x) / parameters.x,
        (previousUv.y - input.uv.y) / parameters.y);
    const speed: f32 = Max(Abs(movement.x), Abs(movement.y));
    const edge: f32 = Clamp((maximumDepth - minimumDepth - 2.0 * Min(Fwidth(motion.w), 0.002)) * 10000.0, 0.0, 1.0);
    // A moving silhouette is reactive: previous coverage is no longer evidence
    // for the newly exposed background, even when its stored depth is background.
    if (edge > 0.2 && coverageMotion > 0.05) {
        const reactive: float4 = ReconstructCurrent(resources, float2(input.uv.x + resources.temporal.reconstruction.x,
            input.uv.y + resources.temporal.reconstruction.y));
        return { color: float4(reactive.x, reactive.y, reactive.z, motion.w) };
    }
    const tolerance: f32 = Max(0.00002, Min(Fwidth(motion.w) * 2.0, 0.002));
    var accumulated: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    for (var y: u32 = 0; y < 2; y = y + 1) {
        for (var x: u32 = 0; x < 2; x = x + 1) {
            const fx: f32 = Convert<f32>(x);
            const fy: f32 = Convert<f32>(y);
            const uv: float2 = float2((origin.x + fx + 0.5) * parameters.x, (origin.y + fy + 0.5) * parameters.y);
            const history: float4 = Sample(resources.history, resources.historySampler, uv);
            const weight: f32 = (1.0 - Abs(fx - fraction.x)) * (1.0 - Abs(fy - fraction.y));
            var accepted: bool = Abs(history.w - previousDepth) <= tolerance;
            // Only stationary subpixel coverage may straddle the local silhouette.
            // Moving/disoccluded surfaces require actual previous-surface depth.
            if (speed < 0.05 && coverageMotion < 0.05 && edge > 0.2 && history.w >= minimumDepth - tolerance && history.w <= maximumDepth + tolerance) {
                accepted = true;
            }
            if (accepted) {
                accumulated = Add4(accumulated, history * float4(weight, weight, weight, weight));
                total = total + weight;
            }
        }
    }
    if (total < 0.01) {
        return { color: float4(current.x, current.y, current.z, motion.w) };
    }
    const historical: float4 = Divide4(accumulated, total);
    const clipped: float4 = ClipHistory(historical, minimum, maximum);
    var colorConfidence: f32 = 1.0 - Clamp(Abs(Luma(historical) - Luma(current)) / Max(Luma(maximum) - Luma(minimum), 0.15), 0.0, 1.0);
    if (speed < 0.05 && coverageMotion < 0.05 && edge > 0.2) { colorConfidence = 1.0; }
    const decision: float3 = TemporalPolicy(Min(total * 4.0, 1.0), 1.0, colorConfidence, edge);
    const blend: f32 = Min(decision.y, parameters.w);
    const resolved: float4 = Add4(current * float4(1.0 - blend, 1.0 - blend, 1.0 - blend, 1.0 - blend),
        clipped * float4(blend, blend, blend, blend));
    return { color: float4(resolved.x, resolved.y, resolved.z, motion.w) };
}

function Add4(a: float4, b: float4): float4 {
    return float4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
}
function Divide4(a: float4, divisor: f32): float4 {
    return float4(a.x / divisor, a.y / divisor, a.z / divisor, a.w / divisor);
}

function ReconstructCurrent(resources: TemporalResources, uv: float2): float4 {
    const parameters: float4 = resources.temporal.parameters;
    const pixel: float2 = float2(uv.x / parameters.x - 0.5, uv.y / parameters.y - 0.5);
    const origin: float2 = float2(Floor(pixel.x), Floor(pixel.y));
    const fraction: float2 = float2(pixel.x - origin.x, pixel.y - origin.y);
    var sum: float4 = float4(0.0, 0.0, 0.0, 0.0);
    for (var y: u32 = 0; y < 2; y = y + 1) {
        for (var x: u32 = 0; x < 2; x = x + 1) {
            const fx: f32 = Convert<f32>(x);
            const fy: f32 = Convert<f32>(y);
            const weight: f32 = (1.0 - Abs(fx - fraction.x)) * (1.0 - Abs(fy - fraction.y));
            const sample: float4 = Sample(resources.current, resources.currentSampler,
                float2((origin.x + fx + 0.5) * parameters.x, (origin.y + fy + 0.5) * parameters.y));
            sum = Add4(sum, sample * float4(weight, weight, weight, weight));
        }
    }
    return sum;
}
