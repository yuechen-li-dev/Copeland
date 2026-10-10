import { WorldAt, EnvironmentResponse, MaskAllows, BoxReflection, ConeAttenuation } from "./SurfaceLighting";
import { Unit, Sub3, Add3, Scale3, Mul3, Dot3, DirectLight, HemisphereLight, ShadowVisibility, LimitRadiance } from "./Lighting3D";
import { CompiledDiffuse } from "./CompiledDiffuseLighting";

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
    light: float4;
    sun: float4;
    sky: float4;
    ground: float4;
    shadowX: float4;
    shadowY: float4;
    shadowZ: float4;
    shadowW: float4;
    shadowParameters: float4;
    parameters: float4;
    clear: float4;
    aoParameters: float4;
    surfaceTexels: float4;
    probePosition: float4;
    probeBounds: float4;
    spotAX: float4;
    spotAY: float4;
    spotAZ: float4;
    spotAW: float4;
    spotBX: float4;
    spotBY: float4;
    spotBZ: float4;
    spotBW: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) base: Texture2D<float4>;
    @binding(2) baseSampler: Sampler;
    @binding(3) motion: Texture2D<float4>;
    @binding(4) motionSampler: Sampler;
    @binding(5) normal: Texture2D<float4>;
    @binding(6) normalSampler: Sampler;
    @binding(7) emission: Texture2D<float4>;
    @binding(8) emissionSampler: Sampler;
    @binding(9) environment: Texture2D<float4>;
    @binding(10) environmentSampler: Sampler;
    @binding(11) lights: Texture2D<float4>;
    @binding(12) lightsSampler: Sampler;
    @binding(13) tiles: Texture2D<float4>;
    @binding(14) tilesSampler: Sampler;
    @binding(15) shadow: Texture2D<float4>;
    @binding(16) shadowSampler: Sampler;
    @binding(17) diffuse: Texture2D<float4>;
    @binding(18) diffuseSampler: Sampler;
    @binding(19) ao: Texture2D<float4>;
    @binding(20) aoSampler: Sampler;
    @binding(21) spotA: Texture2D<float4>;
    @binding(22) spotASampler: Sampler;
    @binding(23) spotB: Texture2D<float4>;
    @binding(24) spotBSampler: Sampler;
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

function Position(resources: Resources, uv: float2, depth: f32): float3 {
    return WorldAt(uv, depth, resources.material.inverseX, resources.material.inverseY,
        resources.material.inverseZ, resources.material.inverseW);
}
function Row(row: float4, p: float3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
function Occlusion(resources: Resources, uv: float2, p: float3): f32 {
    const parameters: float4 = resources.material.aoParameters;
    if (parameters.z <= 0.0) {
        return 1.0;
    }
    const pixel: float2 = float2(uv.x / parameters.x - 0.5, uv.y / parameters.y - 0.5);
    const origin: float2 = float2(Floor(pixel.x), Floor(pixel.y));
    const fraction: float2 = float2(pixel.x - origin.x, pixel.y - origin.y);
    var sum: f32 = 0.0;
    var total: f32 = 0.0;
    for (var y: u32 = 0; y < 2; y = y + 1) {
        for (var x: u32 = 0; x < 2; x = x + 1) {
            const fx: f32 = Convert<f32>(x);
            const fy: f32 = Convert<f32>(y);
            const tapUv: float2 = float2((origin.x + fx + 0.5) * parameters.x, (origin.y + fy + 0.5) * parameters.y);
            const tap: float4 = Sample(resources.ao, resources.aoSampler, tapUv);
            if (tap.y < 1.0) {
                const texel: float4 = resources.material.surfaceTexels;
                const snapped: float2 = float2((Floor(tapUv.x / texel.x) + 0.5) * texel.x,
                    (Floor(tapUv.y / texel.y) + 0.5) * texel.y);
                const delta: float3 = Sub3(Position(resources, snapped, tap.y), p);
                const weight: f32 = (1.0 - Abs(fx - fraction.x)) * (1.0 - Abs(fy - fraction.y))
                    / (1.0 + Dot3(delta, delta) / Max(parameters.z * parameters.z * 0.02, 0.000001));
                sum = sum + tap.x * weight;
                total = total + weight;
            }
        }
    }
    if (total < 0.00001) {
        return 1.0;
    }
    return sum / total;
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const motion: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    if (motion.w >= 1.0) {
        return { color: resources.material.clear };
    }
    const material: float4 = Sample(resources.base, resources.baseSampler, input.uv);
    const n: float4 = Sample(resources.normal, resources.normalSampler, input.uv);
    const emission: float4 = Sample(resources.emission, resources.emissionSampler, input.uv);
    const base: float3 = float3(material.x, material.y, material.z);
    const emitted: float3 = float3(emission.x, emission.y, emission.z);
    if (emission.w < 0.0) {
        const unlit: float3 = Add3(base, emitted);
        return { color: LimitRadiance(float4(unlit.x, unlit.y, unlit.z, 1.0)) };
    }
    const normal: float3 = Unit(float3(n.x, n.y, n.z));
    const p: float3 = Position(resources, input.uv, motion.w);
    const view: float3 = Unit(Sub3(float3(resources.material.eye.x, resources.material.eye.y, resources.material.eye.z), p));
    const light: float3 = Unit(float3(resources.material.light.x, resources.material.light.y, resources.material.light.z));
    const sun: float3 = float3(resources.material.sun.x, resources.material.sun.y, resources.material.sun.z);
    const projected: float3 = float3(Row(resources.material.shadowX, p), Row(resources.material.shadowY, p), Row(resources.material.shadowZ, p));
    const visibility: f32 = ShadowVisibility(resources.shadow, resources.shadowSampler, projected,
        resources.material.shadowParameters, Max(Dot3(normal, light), 0.0));
    var direct: float3 = Scale3(Mul3(DirectLight(base, normal, view, light, n.w, material.w), sun), resources.material.sun.w * visibility);
    var ambient: float3 = HemisphereLight(base, normal, view, float3(resources.material.sky.x, resources.material.sky.y, resources.material.sky.z),
        float3(resources.material.ground.x, resources.material.ground.y, resources.material.ground.z), n.w, material.w);
    if (resources.material.parameters.y > 0.5) {
        const reflected: float3 = BoxReflection(p, Sub3(Scale3(normal, 2.0 * Max(Dot3(normal, view), 0.0)), view),
            resources.material.probePosition, resources.material.probeBounds);
        ambient = Scale3(EnvironmentResponse(resources.environment, resources.environmentSampler, base, normal, reflected,
            Max(Dot3(normal, view), 0.0001), n.w, material.w), resources.material.parameters.z);
    }
    if (resources.material.parameters.w > 0.5 && n.w == 0.0) {
        const response: float4 = CompiledDiffuse(resources.diffuse, resources.diffuseSampler, p, normal,
            resources.material.sun.w, resources.material.sky.w, sun);
        if (response.w > 0.5) {
            direct = Scale3(Mul3(base, sun), Max(Dot3(normal, light), 0.0) * resources.material.sun.w * visibility / 3.14159265);
            ambient = Mul3(base, float3(response.x, response.y, response.z));
        }
    }
    const mask: float4 = Sample(resources.tiles, resources.tilesSampler, input.uv);
    for (var index: u32 = 0; index < 32; index = index + 1) {
        if (Convert<f32>(index) < resources.material.parameters.x && (MaskAllows(mask, index) || resources.material.aoParameters.w > 0.5)) {
            const row: f32 = (Convert<f32>(index) + 0.5) / 32.0;
            const sphere: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.125, row));
            const color: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.375, row));
            const cone: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.625, row));
            const config: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.875, row));
            const delta: float3 = Sub3(float3(sphere.x, sphere.y, sphere.z), p);
            const squared: f32 = Dot3(delta, delta);
            if (squared < sphere.w * sphere.w) {
                const direction: float3 = Unit(delta);
                const ratio: f32 = squared / (sphere.w * sphere.w);
                const fade: f32 = Max(1.0 - ratio * ratio, 0.0);
                var attenuation: f32 = fade * fade / Max(squared, 0.01);
                attenuation = attenuation * ConeAttenuation(direction, cone, config);
                if (config.z > 0.5) {
                    attenuation = attenuation * LocalShadow(resources, config.z, p, Max(Dot3(normal, direction), 0.0));
                }
                direct = Add3(direct, Scale3(Mul3(DirectLight(base, normal, view, direction, n.w, material.w),
                    float3(color.x, color.y, color.z)), color.w * attenuation));
            }
        }
    }
    const indirect: float3 = Scale3(ambient, emission.w * Occlusion(resources, input.uv, p));
    const lit: float3 = Add3(Add3(direct, indirect), emitted);
    return { color: LimitRadiance(float4(lit.x, lit.y, lit.z, 1.0)) };
}

enum ShadowSource {
    First,
    Second,
}
function ShadowSourceFor(index: f32): ShadowSource {
    if (index < 1.5) {
        return ShadowSource.First;
    }
    return ShadowSource.Second;
}
function FirstShadow(resources: Resources, p: float3, nl: f32): f32 {
    const w: f32 = Row(resources.material.spotAW, p);
    if (w <= 0.00001) {
        return 1.0;
    }
    const projected: float3 = float3(Row(resources.material.spotAX, p) / w,
        Row(resources.material.spotAY, p) / w, Row(resources.material.spotAZ, p) / w);
    return ShadowVisibility(resources.spotA, resources.spotASampler, projected, float4(1.0, 0.0015, 1.0 / 512.0, 0.0), nl);
}
function SecondShadow(resources: Resources, p: float3, nl: f32): f32 {
    const w: f32 = Row(resources.material.spotBW, p);
    if (w <= 0.00001) {
        return 1.0;
    }
    const projected: float3 = float3(Row(resources.material.spotBX, p) / w,
        Row(resources.material.spotBY, p) / w, Row(resources.material.spotBZ, p) / w);
    return ShadowVisibility(resources.spotB, resources.spotBSampler, projected, float4(1.0, 0.0015, 1.0 / 512.0, 0.0), nl);
}
function LocalShadow(resources: Resources, index: f32, p: float3, nl: f32): f32 {
    return match ShadowSourceFor(index) {
        ShadowSource.First => FirstShadow(resources, p, nl),
        ShadowSource.Second => SecondShadow(resources, p, nl),
    };
}
