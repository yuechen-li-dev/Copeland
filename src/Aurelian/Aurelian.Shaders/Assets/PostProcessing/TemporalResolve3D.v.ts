// This shader incorporates the AGPL Aetheris policy; see Assets/Licenses.
import { TemporalPolicy } from "./TemporalPolicy";

@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record TemporalMaterial {
    // inverse dimensions, history valid, maximum history weight
    parameters: float4;
    // previous minus current jitter in UV, inverse output extent
    jitter: float4;
    // current projection jitter in UV, reflection mask enabled, reconstruction mode
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
    @binding(7) reflection: Texture2D<float4>;
    @binding(8) reflectionSampler: Sampler;
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
    if (resources.temporal.reconstruction.w > 0.5) {
        return { color: ResolveScaled(resources, input.uv) };
    }
    let parameters: float4 = resources.temporal.parameters;
    let current: float4 = Sample(resources.current, resources.currentSampler, input.uv);
    let motion: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    // The receiver motion says nothing about motion at the second hit.
    if (resources.temporal.reconstruction.z > 0.5
        && Abs(Sample(resources.reflection, resources.reflectionSampler, input.uv).w) > 0.0) {
        return { color: float4(current.x, current.y, current.z, -1.0) };
    }
    // OIT coverage has no opaque-surface motion correspondence. Reconstruct
    // this pixel immediately and mark its history depth invalid, so removing
    // or moving translucent geometry cannot leave a retained color trail.
    if (current.w < 0.99999) {
        let reactive: float4 = ReconstructCurrent(resources, float2(input.uv.x + resources.temporal.reconstruction.x,
            input.uv.y + resources.temporal.reconstruction.y));
        return { color: float4(reactive.x, reactive.y, reactive.z, -1.0) };
    }
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
    var slopeX: f32 = 1.0;
    var slopeY: f32 = 1.0;
    for (var y: u32 = 0; y < 3; y = y + 1) {
        for (var x: u32 = 0; x < 3; x = x + 1) {
            let uv: float2 = float2(input.uv.x + (Convert<f32>(x) - 1.0) * parameters.x,
                input.uv.y + (Convert<f32>(y) - 1.0) * parameters.y);
            let neighbor: float4 = Sample(resources.current, resources.currentSampler, uv);
            let depth: float4 = Sample(resources.motion, resources.motionSampler, uv);
            minimum = Minimum(minimum, neighbor);
            maximum = Maximum(maximum, neighbor);
            minimumDepth = Min(minimumDepth, depth.w);
            maximumDepth = Max(maximumDepth, depth.w);
            // Use the gentler side of each axis to estimate the current surface
            // slope. Quad derivatives can include the silhouette itself and
            // therefore erase the very discontinuity we need to recognize.
            if (y == 1 && x != 1) {
                slopeX = Min(slopeX, Abs(depth.w - motion.w));
            }
            if (x == 1 && y != 1) {
                slopeY = Min(slopeY, Abs(depth.w - motion.w));
            }
            if (depth.w < 1.0) {
                let dx: f32 = (depth.x - uv.x - resources.temporal.jitter.x) / parameters.x;
                let dy: f32 = (depth.y - uv.y - resources.temporal.jitter.y) / parameters.y;
                coverageMotion = Max(coverageMotion, Max(Abs(dx), Abs(dy)));
            }
        }
    }
    // Bilinear color with four independently qualified depth taps. A nearest depth
    // cannot represent the fractional foreground coverage retained in the color.
    let pixel: float2 = float2(previousUv.x / parameters.x - 0.5, previousUv.y / parameters.y - 0.5);
    let origin: float2 = float2(Floor(pixel.x), Floor(pixel.y));
    let fraction: float2 = float2(pixel.x - origin.x, pixel.y - origin.y);
    let movement: float2 = float2((previousUv.x - input.uv.x) / parameters.x,
        (previousUv.y - input.uv.y) / parameters.y);
    let speed: f32 = Max(Abs(movement.x), Abs(movement.y));
    let surfaceSlope: f32 = slopeX + slopeY;
    let edge: f32 = Clamp((maximumDepth - minimumDepth - 2.0 * surfaceSlope - 0.00002) * 10000.0, 0.0, 1.0);
    // A moving silhouette is reactive: previous coverage is no longer evidence
    // for the newly exposed background, even when its stored depth is background.
    if (edge > 0.2 && coverageMotion > 0.05) {
        let reactive: float4 = ReconstructCurrent(resources, float2(input.uv.x + resources.temporal.reconstruction.x,
            input.uv.y + resources.temporal.reconstruction.y));
        return { color: float4(reactive.x, reactive.y, reactive.z, motion.w) };
    }
    let tolerance: f32 = Max(0.00002, Min(surfaceSlope * 2.0, 0.002));
    var accumulated: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    for (var y: u32 = 0; y < 2; y = y + 1) {
        for (var x: u32 = 0; x < 2; x = x + 1) {
            let fx: f32 = Convert<f32>(x);
            let fy: f32 = Convert<f32>(y);
            let uv: float2 = float2((origin.x + fx + 0.5) * parameters.x, (origin.y + fy + 0.5) * parameters.y);
            let history: float4 = Sample(resources.history, resources.historySampler, uv);
            let weight: f32 = (1.0 - Abs(fx - fraction.x)) * (1.0 - Abs(fy - fraction.y));
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
    let historical: float4 = Divide4(accumulated, total);
    let clipped: float4 = ClipHistory(historical, minimum, maximum);
    var colorConfidence: f32 = 1.0 - Clamp(Abs(Luma(historical) - Luma(current)) / Max(Luma(maximum) - Luma(minimum), 0.15), 0.0, 1.0);
    if (speed < 0.05 && coverageMotion < 0.05 && edge > 0.2) { colorConfidence = 1.0; }
    let decision: float3 = TemporalPolicy(Min(total * 4.0, 1.0), 1.0, colorConfidence, edge);
    let blend: f32 = Min(decision.y, parameters.w);
    let resolved: float4 = Add4(current * float4(1.0 - blend, 1.0 - blend, 1.0 - blend, 1.0 - blend),
        clipped * float4(blend, blend, blend, blend));
    return { color: float4(resolved.x, resolved.y, resolved.z, motion.w) };
}

function Add4(a: float4, b: float4): float4 {
    return float4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
}
function Divide4(a: float4, divisor: f32): float4 {
    return float4(a.x / divisor, a.y / divisor, a.z / divisor, a.w / divisor);
}

function CubicWeight(distance: f32): f32 {
    let d: f32 = Abs(distance);
    if (d < 1.0) { return ((1.5 * d - 2.5) * d) * d + 1.0; }
    if (d < 2.0) { return ((-0.5 * d + 2.5) * d - 4.0) * d + 2.0; }
    return 0.0;
}

function SpatialCubic(resources: TemporalResources, center: float2, inverse: float2): float4 {
    let origin: float2 = float2(Floor(center.x), Floor(center.y));
    let fraction: float2 = float2(center.x - origin.x, center.y - origin.y);
    var sum: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var low: float4 = float4(60000.0, 60000.0, 60000.0, 1.0);
    var high: float4 = float4(0.0, 0.0, 0.0, 0.0);
    for (var y: u32 = 0; y < 4; y = y + 1) {
        for (var x: u32 = 0; x < 4; x = x + 1) {
            let offset: float2 = float2(Convert<f32>(x) - 1.0, Convert<f32>(y) - 1.0);
            let tap: float2 = float2((origin.x + offset.x + 0.5) * inverse.x, (origin.y + offset.y + 0.5) * inverse.y);
            let value: float4 = Sample(resources.current, resources.currentSampler, tap);
            let weight: f32 = CubicWeight(offset.x - fraction.x) * CubicWeight(offset.y - fraction.y);
            sum = Add4(sum, value * float4(weight, weight, weight, weight));
            if (x >= 1 && x <= 2 && y >= 1 && y <= 2) {
                low = Minimum(low, value);
                high = Maximum(high, value);
            }
        }
    }
    return ClipHistory(sum, low, high);
}

function ResolveScaled(resources: TemporalResources, uv: float2): float4 {
    let parameters: float4 = resources.temporal.parameters;
    let inverse: float2 = float2(parameters.x, parameters.y);
    let correction: float2 = float2(resources.temporal.reconstruction.x, resources.temporal.reconstruction.y);
    let center: float2 = float2((uv.x + correction.x) / inverse.x - 0.5, (uv.y + correction.y) / inverse.y - 0.5);
    if (resources.temporal.reconstruction.w == 2.0) {
        let spatial: float4 = SpatialCubic(resources, center, inverse);
        return float4(spatial.x, spatial.y, spatial.z, -1.0);
    }
    let origin: float2 = float2(Floor(center.x), Floor(center.y));
    let fraction: float2 = float2(center.x - origin.x, center.y - origin.y);
    let anchor: float2 = float2(Floor(center.x + 0.5), Floor(center.y + 0.5));
    let halfTexel: float2 = float2(inverse.x * 0.5, inverse.y * 0.5);
    let maximumUv: float2 = float2(1.0 - halfTexel.x, 1.0 - halfTexel.y);
    let guideUv: float2 = float2(Clamp((anchor.x + 0.5) * inverse.x, halfTexel.x, maximumUv.x),
        Clamp((anchor.y + 0.5) * inverse.y, halfTexel.y, maximumUv.y));
    let guide: float4 = Sample(resources.motion, resources.motionSampler, guideUv);
    let jitterDelta: float2 = float2(resources.temporal.jitter.x, resources.temporal.jitter.y);
    var color: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var linearColor: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var layer: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var totalColor: f32 = 0.0;
    var totalLayer: f32 = 0.0;
    var low: float4 = float4(60000.0, 60000.0, 60000.0, 1.0);
    var high: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var lowDepth: f32 = 1.0;
    var highDepth: f32 = 0.0;
    var speed: f32 = 0.0;
    var lowMotion: float2 = float2(1.0e6, 1.0e6);
    var highMotion: float2 = float2(-1.0e6, -1.0e6);
    var reactive: bool = false;
    for (var y: u32 = 0; y < 2; y = y + 1) {
        for (var x: u32 = 0; x < 2; x = x + 1) {
            let offset: float2 = float2(Convert<f32>(x), Convert<f32>(y));
            let address: float2 = float2(origin.x + offset.x + 0.5, origin.y + offset.y + 0.5);
            let tap: float2 = float2(Clamp(address.x * inverse.x, halfTexel.x, maximumUv.x),
                Clamp(address.y * inverse.y, halfTexel.y, maximumUv.y));
            let linearWeight: f32 = (1.0 - Abs(offset.x - fraction.x)) * (1.0 - Abs(offset.y - fraction.y));
            let squaredWeight: f32 = linearWeight * linearWeight;
            let weight: f32 = squaredWeight * squaredWeight;
            let value: float4 = Sample(resources.current, resources.currentSampler, tap);
            let motion: float4 = Sample(resources.motion, resources.motionSampler, tap);
            color = Add4(color, value * float4(weight, weight, weight, weight));
            linearColor = Add4(linearColor, value * float4(linearWeight, linearWeight, linearWeight, linearWeight));
            totalColor = totalColor + weight;
            if (Abs(motion.w - guide.w) <= 0.001) {
                layer = Add4(layer, value * float4(weight, weight, weight, weight));
                totalLayer = totalLayer + weight;
            }
            low = Minimum(low, value);
            high = Maximum(high, value);
            lowDepth = Min(lowDepth, motion.w);
            highDepth = Max(highDepth, motion.w);
            if (motion.w < 1.0) {
                let delta: float2 = float2((motion.x - tap.x - jitterDelta.x) / inverse.x,
                    (motion.y - tap.y - jitterDelta.y) / inverse.y);
                speed = Max(speed, Max(Abs(delta.x), Abs(delta.y)));
                lowMotion = float2(Min(lowMotion.x, delta.x), Min(lowMotion.y, delta.y));
                highMotion = float2(Max(highMotion.x, delta.x), Max(highMotion.y, delta.y));
            }
            if (value.w < 0.99999 || (resources.temporal.reconstruction.z > 0.5
                && Abs(Sample(resources.reflection, resources.reflectionSampler, tap).w) > 0.0)) { reactive = true; }
        }
    }
    color = Divide4(color, totalColor);
    if (reactive || resources.temporal.reconstruction.w == 3.0) {
        color = SpatialCubic(resources, center, inverse);
    }
    if (reactive) { return float4(color.x, color.y, color.z, -1.0); }
    let spread: f32 = highDepth - lowDepth;
    if (speed > 0.08 && spread > 0.001 && totalLayer > 0.0) {
        color = linearColor;
        // On the revealed side, the farther guide owns the new sample. Discard
        // the departing foreground even when it is still inside the gather.
        if (guide.w > lowDepth + 0.001) { color = Divide4(layer, totalLayer); }
        return float4(color.x, color.y, color.z, -1.0);
    }
    if (parameters.z < 0.5) {
        return float4(color.x, color.y, color.z, guide.w);
    }
    var priorUv: float2 = uv;
    var priorDepth: f32 = 1.0;
    if (guide.w < 1.0) {
        priorUv = float2(uv.x + guide.x - guideUv.x - jitterDelta.x, uv.y + guide.y - guideUv.y - jitterDelta.y);
        priorDepth = guide.z;
    }
    if (priorDepth < 0.0 || priorDepth > 1.0 || priorUv.x < 0.0 || priorUv.x >= 1.0 || priorUv.y < 0.0 || priorUv.y >= 1.0) {
        return float4(color.x, color.y, color.z, guide.w);
    }
    let outputInverse: float2 = float2(resources.temporal.jitter.z, resources.temporal.jitter.w);
    let outputPixel: float2 = float2(priorUv.x / outputInverse.x - 0.5, priorUv.y / outputInverse.y - 0.5);
    let base: float2 = float2(Floor(outputPixel.x), Floor(outputPixel.y));
    let phase: float2 = float2(outputPixel.x - base.x, outputPixel.y - base.y);
    let tolerance: f32 = Clamp(spread, 0.00003, 0.00045);
    var history: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    for (var y: u32 = 0; y < 2; y = y + 1) {
        for (var x: u32 = 0; x < 2; x = x + 1) {
            let offset: float2 = float2(Convert<f32>(x), Convert<f32>(y));
            let tap: float2 = float2((base.x + offset.x + 0.5) * outputInverse.x,
                (base.y + offset.y + 0.5) * outputInverse.y);
            let prior: float4 = Sample(resources.history, resources.historySampler, tap);
            if (Abs(prior.w - priorDepth) <= tolerance || (speed <= 0.08 && prior.w >= lowDepth - tolerance && prior.w <= highDepth + tolerance)) {
                let weight: f32 = (1.0 - Abs(offset.x - phase.x)) * (1.0 - Abs(offset.y - phase.y));
                history = Add4(history, prior * float4(weight, weight, weight, weight));
                total = total + weight;
            }
        }
    }
    if (total > 0.01) {
        let historical: float4 = Divide4(history, total);
        let clipped: float4 = ClipHistory(historical, low, high);
        let depthEvidence: f32 = Min(total * 4.0, 1.0);
        let motionSpread: f32 = Max(highMotion.x - lowMotion.x, highMotion.y - lowMotion.y);
        let motionConfidence: f32 = 1.0 - Clamp(motionSpread * 0.5, 0.0, 1.0);
        let edgeRisk: f32 = Clamp((spread - 0.0001) * 1200.0, 0.0, 1.0);
        var colorConfidence: f32 = 1.0 - Clamp(Abs(Luma(historical) - Luma(color)) / Max(Luma(high) - Luma(low), 0.08), 0.0, 1.0);
        if (speed <= 0.08 && edgeRisk >= 0.2) { colorConfidence = 1.0; }
        let decision: float3 = TemporalPolicy(depthEvidence, motionConfidence, colorConfidence, edgeRisk);
        // Give the latest raster sample more weight when it lands close to
        // this output pixel's center. The utility decision caps reuse.
        let nearestDistance: f32 = Max(Abs(center.x - anchor.x), Abs(center.y - anchor.y));
        let currentSupport: f32 = Clamp(1.0 - nearestDistance * 2.0, 0.0, 1.0);
        let blend: f32 = Min(parameters.w, decision.y) * (1.0 - 0.6 * currentSupport * currentSupport);
        color = Add4(color * float4(1.0 - blend, 1.0 - blend, 1.0 - blend, 1.0 - blend),
            clipped * float4(blend, blend, blend, blend));
    }
    return float4(color.x, color.y, color.z, guide.w);
}

function ReconstructCurrent(resources: TemporalResources, uv: float2): float4 {
    let parameters: float4 = resources.temporal.parameters;
    let pixel: float2 = float2(uv.x / parameters.x - 0.5, uv.y / parameters.y - 0.5);
    let origin: float2 = float2(Floor(pixel.x), Floor(pixel.y));
    let fraction: float2 = float2(pixel.x - origin.x, pixel.y - origin.y);
    var sum: float4 = float4(0.0, 0.0, 0.0, 0.0);
    for (var y: u32 = 0; y < 2; y = y + 1) {
        for (var x: u32 = 0; x < 2; x = x + 1) {
            let fx: f32 = Convert<f32>(x);
            let fy: f32 = Convert<f32>(y);
            let weight: f32 = (1.0 - Abs(fx - fraction.x)) * (1.0 - Abs(fy - fraction.y));
            let sample: float4 = Sample(resources.current, resources.currentSampler,
                float2((origin.x + fx + 0.5) * parameters.x, (origin.y + fy + 0.5) * parameters.y));
            sum = Add4(sum, sample * float4(weight, weight, weight, weight));
        }
    }
    return sum;
}
