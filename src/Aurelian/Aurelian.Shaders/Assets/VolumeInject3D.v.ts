import { WorldAt, ConeAttenuation } from "./SurfaceLighting";
import { Unit, Sub3, Add3, Scale3, Mul3, Dot3, ShadowVisibility, LimitRadiance } from "./Lighting3D";
import { SliceEnd, Phase } from "./VolumeLighting";
@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    inverseX: float4;
    inverseY: float4;
    inverseZ: float4;
    inverseW: float4;
    eye: float4;
    grid: float4;
    depth: float4;
    fogParameters: float4;
    scattering: float4;
    ambient: float4;
    regionMin: float4;
    regionMax: float4;
    light: float4;
    sun: float4;
    shadowX: float4;
    shadowY: float4;
    shadowZ: float4;
    shadowW: float4;
    shadowParameters: float4;
    cascadeMidX: float4;
    cascadeMidY: float4;
    cascadeMidZ: float4;
    cascadeMidW: float4;
    cascadeFarX: float4;
    cascadeFarY: float4;
    cascadeFarZ: float4;
    cascadeFarW: float4;
    cascadeEnds: float4;
    cascadeForward: float4;
    cascadeBias: float4;
    spotAX: float4;
    spotAY: float4;
    spotAZ: float4;
    spotAW: float4;
    spotBX: float4;
    spotBY: float4;
    spotBZ: float4;
    spotBW: float4;
    environmentParameters: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) shadow: Texture2D<float4>;
    @binding(2) shadowSampler: Sampler;
    @binding(3) cascadeMid: Texture2D<float4>;
    @binding(4) cascadeMidSampler: Sampler;
    @binding(5) cascadeFar: Texture2D<float4>;
    @binding(6) cascadeFarSampler: Sampler;
    @binding(7) spotA: Texture2D<float4>;
    @binding(8) spotASampler: Sampler;
    @binding(9) spotB: Texture2D<float4>;
    @binding(10) spotBSampler: Sampler;
    @binding(11) lights: Texture2D<float4>;
    @binding(12) lightsSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output { @target(0) color: float4; }
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}

function Row(row: float4, p: float3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
function Visibility(map: Texture2D<float4>, sampler: Sampler, p: float3,
    x: float4, y: float4, z: float4, w: float4, enabled: f32, bias: f32, texel: f32): f32 {
    const divisor: f32 = Row(w, p);
    if (divisor <= 0.00001) { return 1.0; }
    return ShadowVisibility(map, sampler, float3(Row(x, p) / divisor, Row(y, p) / divisor, Row(z, p) / divisor),
        float4(enabled, bias, texel, 0.0), 1.0);
}
function SunVisibility(resources: Resources, p: float3): f32 {
    const m: PassMaterial = resources.material;
    const delta: float3 = Sub3(p, float3(m.eye.x, m.eye.y, m.eye.z));
    const distance: f32 = Dot3(delta, float3(m.cascadeForward.x, m.cascadeForward.y, m.cascadeForward.z));
    const near: f32 = Visibility(resources.shadow, resources.shadowSampler, p,
        m.shadowX, m.shadowY, m.shadowZ, m.shadowW, m.shadowParameters.x, m.cascadeBias.x, 1.0 / 1024.0);
    if (distance < m.cascadeEnds.x * 0.9) { return near; }
    const middle: f32 = Visibility(resources.cascadeMid, resources.cascadeMidSampler, p,
        m.cascadeMidX, m.cascadeMidY, m.cascadeMidZ, m.cascadeMidW, m.shadowParameters.x, m.cascadeBias.y, 1.0 / 1024.0);
    if (distance < m.cascadeEnds.x) {
        const blend: f32 = (distance - m.cascadeEnds.x * 0.9) / (m.cascadeEnds.x * 0.1);
        return near * (1.0 - blend) + middle * blend;
    }
    if (distance < m.cascadeEnds.y * 0.9) { return middle; }
    const far: f32 = Visibility(resources.cascadeFar, resources.cascadeFarSampler, p,
        m.cascadeFarX, m.cascadeFarY, m.cascadeFarZ, m.cascadeFarW, m.shadowParameters.x, m.cascadeBias.z, 1.0 / 1024.0);
    if (distance < m.cascadeEnds.y) {
        const blend: f32 = (distance - m.cascadeEnds.y * 0.9) / (m.cascadeEnds.y * 0.1);
        return middle * (1.0 - blend) + far * blend;
    }
    const fade: f32 = Clamp((distance - m.cascadeEnds.z * 0.9) / (m.cascadeEnds.z * 0.1), 0.0, 1.0);
    return far * (1.0 - fade) + fade;
}
function CellSource(resources: Resources, p: float3, ray: float3, distance: f32): float4 {
    const m: PassMaterial = resources.material;
    var density: f32 = m.fogParameters.x * Exp(Clamp(-m.fogParameters.y * (p.y - m.fogParameters.z), -40.0, 40.0));
    if (distance < m.fogParameters.w || distance > m.depth.w) { density = 0.0; }
    if (m.regionMin.w > 0.5 && (p.x < m.regionMin.x || p.y < m.regionMin.y || p.z < m.regionMin.z
        || p.x > m.regionMax.x || p.y > m.regionMax.y || p.z > m.regionMax.z)) { density = 0.0; }
    density = Min(density, 1000.0);
    if (density <= 0.0) { return float4(0.0, 0.0, 0.0, 0.0); }
    const direction: float3 = Unit(float3(m.light.x, m.light.y, m.light.z));
    var incoming: float3 = Add3(float3(m.ambient.x, m.ambient.y, m.ambient.z),
        Scale3(float3(m.sun.x, m.sun.y, m.sun.z), m.sun.w * SunVisibility(resources, p) * Phase(Dot3(ray, direction), m.scattering.w)));
    for (var index: u32 = 0; index < 32; index = index + 1) {
        if (Convert<f32>(index) < m.environmentParameters.x) {
            const row: f32 = (Convert<f32>(index) + 0.5) / 32.0;
            const sphere: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.125, row));
            const color: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.375, row));
            const cone: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.625, row));
            const config: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.875, row));
            const delta: float3 = Sub3(float3(sphere.x, sphere.y, sphere.z), p);
            const squared: f32 = Dot3(delta, delta);
            if (squared < sphere.w * sphere.w) {
                const toLight: float3 = Unit(delta);
                const ratio: f32 = squared / (sphere.w * sphere.w);
                const fade: f32 = Max(1.0 - ratio * ratio, 0.0);
                var attenuation: f32 = fade * fade / Max(squared, 0.01) * ConeAttenuation(toLight, cone, config);
                if (config.z > 0.5 && config.z < 1.5) {
                    attenuation = attenuation * Visibility(resources.spotA, resources.spotASampler, p,
                        m.spotAX, m.spotAY, m.spotAZ, m.spotAW, 1.0, 0.0015, 1.0 / 512.0);
                } else if (config.z >= 1.5) {
                    attenuation = attenuation * Visibility(resources.spotB, resources.spotBSampler, p,
                        m.spotBX, m.spotBY, m.spotBZ, m.spotBW, 1.0, 0.0015, 1.0 / 512.0);
                }
                incoming = Add3(incoming, Scale3(float3(color.x, color.y, color.z),
                    color.w * attenuation * Phase(Dot3(ray, toLight), m.scattering.w)));
            }
        }
    }
    const bounded: float4 = LimitRadiance(float4(incoming.x, incoming.y, incoming.z, 1.0));
    const source: float3 = Scale3(Mul3(float3(bounded.x, bounded.y, bounded.z),
        float3(m.scattering.x, m.scattering.y, m.scattering.z)), density);
    return float4(source.x, source.y, source.z, density);
}

@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const m: PassMaterial = resources.material;
    const slice: f32 = Floor(input.uv.y * m.grid.z);
    const uv: float2 = float2(input.uv.x, input.uv.y * m.grid.z - slice);
    const eye: float3 = float3(m.eye.x, m.eye.y, m.eye.z);
    const ray: float3 = Unit(Sub3(WorldAt(uv, 0.5, m.inverseX, m.inverseY, m.inverseZ, m.inverseW), eye));
    const end: f32 = SliceEnd(slice, m.depth);
    var start: f32 = 0.0;
    if (slice > 0.0) { start = SliceEnd(slice - 1.0, m.depth); }
    var sum: float4 = float4(0.0, 0.0, 0.0, 0.0);
    // Four deterministic samples integrate thin shadow/density boundaries.
    // The shared TAA resolves camera jitter; no independent stochastic history.
    for (var sample: u32 = 0; sample < 4; sample = sample + 1) {
        const distance: f32 = start + (end - start) * (Convert<f32>(sample) + 0.5) / 4.0;
        const value: float4 = CellSource(resources, Add3(eye, Scale3(ray, distance)), ray, distance);
        sum = float4(sum.x + value.x, sum.y + value.y, sum.z + value.z, sum.w + value.w);
    }
    const divisor: f32 = Max(sum.w, 0.000001);
    return { color: float4(sum.x / divisor, sum.y / divisor, sum.z / divisor, sum.w / 4.0) };
}
