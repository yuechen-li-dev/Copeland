import { Add3, Scale3 } from "./Lighting3D";
export function SliceEnd(index: f32, depth: float4): f32 {
    return depth.x * Exp(Log(depth.y / depth.x) * (index + 1.0) / depth.z);
}
export function Phase(cosine: f32, g: f32): f32 {
    const denominator: f32 = Max(1.0 + g * g - 2.0 * g * cosine, 0.001);
    return (1.0 - g * g) / (12.566370614 * Pow(denominator, 1.5));
}
export function VolumeBand(map: Texture2D<float4>, sampler: Sampler,
    uv: float2, index: f32, grid: float4): float4 {
    const x: f32 = Clamp(uv.x, 0.5 / grid.x, 1.0 - 0.5 / grid.x);
    const y: f32 = Clamp(uv.y, 0.5 / grid.y, 1.0 - 0.5 / grid.y);
    return Sample(map, sampler, float2(x, (index + y) / grid.z));
}
// Atlas stores cumulative in-scattering (rgb) and transmittance (a).
// Interpolate optical depth within a slice; never blend across atlas tile edges.
export function VolumeAt(map: Texture2D<float4>, sampler: Sampler,
    uv: float2, distance: f32, grid: float4, depth: float4): float4 {
    if (grid.w < 0.5 || distance <= 0.0) {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    const location: f32 = Log(Max(distance, depth.x) / depth.x) / Log(depth.y / depth.x) * depth.z - 1.0;
    const upper: f32 = Clamp(-Floor(-location), 0.0, depth.z - 1.0);
    const end: f32 = SliceEnd(upper, depth);
    var start: f32 = 0.0;
    var previous: float4 = float4(0.0, 0.0, 0.0, 1.0);
    if (upper > 0.0) {
        start = SliceEnd(upper - 1.0, depth);
        previous = VolumeBand(map, sampler, uv, upper - 1.0, grid);
    }
    const next: float4 = VolumeBand(map, sampler, uv, upper, grid);
    const fraction: f32 = Clamp((distance - start) / Max(end - start, 0.00001), 0.0, 1.0);
    const ratio: f32 = Clamp(next.w / Max(previous.w, 0.000001), 0.000001, 1.0);
    const partial: f32 = Pow(ratio, fraction);
    var blend: f32 = fraction;
    if (ratio < 0.9999) {
        blend = (1.0 - partial) / (1.0 - ratio);
    }
    return float4(previous.x + (next.x - previous.x) * blend,
        previous.y + (next.y - previous.y) * blend,
        previous.z + (next.z - previous.z) * blend, previous.w * partial);
}
export function ComposeVolume(color: float3, volume: float4): float3 {
    return Add3(Scale3(color, volume.w), float3(volume.x, volume.y, volume.z));
}
