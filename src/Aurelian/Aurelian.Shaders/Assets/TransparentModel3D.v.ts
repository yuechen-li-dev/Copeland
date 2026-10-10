import { EnvironmentResponse, BoxReflection, ConeAttenuation } from "./SurfaceLighting";
import { Fogged } from "./HeightFog";
import { Unit, Sub3, ShadowVisibility, PlaneShadowVisibility, Dot3, Scale3, Mul3, DirectLight, HemisphereLight, Add3, Cross3, LimitRadiance } from "./Lighting3D";

@space(world.position)
type WorldPosition3 = float3;
@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record StaticModelMaterial {
    clipX: float4;
    clipY: float4;
    clipZ: float4;
    clipW: float4;
    eye: float4;
    baseColor: float4;
    factors: float4;
    emissiveAlpha: float4;
    flags: float4;
    light: float4;
    sun: float4;
    sky: float4;
    ground: float4;
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
    environmentParameters: float4;
    probePosition: float4;
    probeBounds: float4;
    fogParameters: float4;
    fogColor: float4;
    surfaceTexels: float4;
    spotAX: float4;
    spotAY: float4;
    spotAZ: float4;
    spotAW: float4;
    spotBX: float4;
    spotBY: float4;
    spotBZ: float4;
    spotBW: float4;
}

stream VertexInput {
    @location(0) position: WorldPosition3;
    @location(1) normal: float3;
    @location(2) color: float4;
    @location(3) uv: float2;
    @location(4) tangent: float4;
}

stream MaterialResources {
    @binding(0) material: StaticModelMaterial;
}
stream Resources {
    @binding(1) baseMap: Texture2D<float4>;
    @binding(2) baseSampler: Sampler;
    @binding(3) metalMap: Texture2D<float4>;
    @binding(4) metalSampler: Sampler;
    @binding(5) normalMap: Texture2D<float4>;
    @binding(6) normalSampler: Sampler;
    @binding(7) occlusionMap: Texture2D<float4>;
    @binding(8) occlusionSampler: Sampler;
    @binding(9) emissiveMap: Texture2D<float4>;
    @binding(10) emissiveSampler: Sampler;
    @binding(11) shadow: Texture2D<float4>;
    @binding(12) shadowSampler: Sampler;
    @binding(13) environment: Texture2D<float4>;
    @binding(14) environmentSampler: Sampler;
    @binding(15) motion: Texture2D<float4>;
    @binding(16) motionSampler: Sampler;
    @binding(17) lights: Texture2D<float4>;
    @binding(18) lightsSampler: Sampler;
    @binding(19) cascadeMid: Texture2D<float4>;
    @binding(20) cascadeMidSampler: Sampler;
    @binding(21) cascadeFar: Texture2D<float4>;
    @binding(22) cascadeFarSampler: Sampler;
    @binding(23) spotA: Texture2D<float4>;
    @binding(24) spotASampler: Sampler;
    @binding(25) spotB: Texture2D<float4>;
    @binding(26) spotBSampler: Sampler;
}

stream ModelVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) world: float3;
    @location(1) normal: float3;
    @location(2) color: float4;
    @location(3) uv: float2;
    @location(4) tangent: float4;
}
stream PixelBuiltins { @builtin(front_face) frontFace: bool; }
stream ModelOutput {
    @target(0) color: float4;
    @target(1) optical: float4;
}

function ProjectRow(row: float4, p: WorldPosition3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
function Row(row: float4, p: float3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
@vertex
function VertexMain(input: VertexInput, uniforms: MaterialResources): ModelVaryings {
    return {
        position: float4(ProjectRow(uniforms.material.clipX, input.position), ProjectRow(uniforms.material.clipY, input.position),
            ProjectRow(uniforms.material.clipZ, input.position), ProjectRow(uniforms.material.clipW, input.position)),
        world: float3(input.position.x, input.position.y, input.position.z),
        normal: input.normal,
        color: input.color,
        uv: input.uv,
        tangent: input.tangent,
    };
}

@pixel
function PixelMain(input: ModelVaryings, uniforms: MaterialResources, resources: Resources, builtins: PixelBuiltins): ModelOutput {
    if (!builtins.frontFace && uniforms.material.flags.z < 0.5) {
        Discard();
    }
    const base: float4 = Sample(resources.baseMap, resources.baseSampler, input.uv) * uniforms.material.baseColor * input.color;
    if (uniforms.material.flags.y > 0.5 && base.w < uniforms.material.emissiveAlpha.w) {
        Discard();
    }
    const uv: float2 = float2(input.position.x * uniforms.material.surfaceTexels.x,
        input.position.y * uniforms.material.surfaceTexels.y);
    const opaque: float4 = Sample(resources.motion, resources.motionSampler, uv);
    if (input.position.z >= opaque.w) {
        Discard();
    }
    const emission: float4 = Sample(resources.emissiveMap, resources.emissiveSampler, input.uv);
    const emitted: float3 = Mul3(float3(emission.x, emission.y, emission.z),
        float3(uniforms.material.emissiveAlpha.x, uniforms.material.emissiveAlpha.y, uniforms.material.emissiveAlpha.z));
    const nt: float4 = Sample(resources.normalMap, resources.normalSampler, input.uv);
    const tangent: float3 = Unit(float3(input.tangent.x, input.tangent.y, input.tangent.z));
    const geometric: float3 = Unit(input.normal);
    const bitangent: float3 = Scale3(Cross3(geometric, tangent), input.tangent.w);
    var normal: float3 = Unit(Add3(Add3(Scale3(tangent, (nt.x * 2.0 - 1.0) * uniforms.material.factors.z),
        Scale3(bitangent, (nt.y * 2.0 - 1.0) * uniforms.material.factors.z)), Scale3(geometric, nt.z * 2.0 - 1.0)));
    if (!builtins.frontFace) {
        normal = Scale3(normal, -1.0);
    }
    const mr: float4 = Sample(resources.metalMap, resources.metalSampler, input.uv);
    const metallic: f32 = Clamp(mr.z * uniforms.material.factors.x, 0.0, 1.0);
    const roughness: f32 = Clamp(mr.y * uniforms.material.factors.y, 0.045, 1.0);
    const ao: float4 = Sample(resources.occlusionMap, resources.occlusionSampler, input.uv);
    var occlusion: f32 = 1.0 - uniforms.material.factors.w + ao.x * uniforms.material.factors.w;
    if (uniforms.material.flags.x > 0.5) {
        occlusion = -1.0;
    }
    const p: float3 = input.world;
    const eye: float3 = float3(uniforms.material.eye.x, uniforms.material.eye.y, uniforms.material.eye.z);
    const view: float3 = Unit(Sub3(eye, p));
    const light: float3 = Unit(float3(uniforms.material.light.x, uniforms.material.light.y, uniforms.material.light.z));
    const sun: float3 = float3(uniforms.material.sun.x, uniforms.material.sun.y, uniforms.material.sun.z);
    const rgb: float3 = float3(base.x, base.y, base.z);
    var lit: float3 = Add3(rgb, emitted);
    if (uniforms.material.flags.x < 0.5) {
        const visibility: f32 = DirectionalVisibility(resources, uniforms, p, normal);
        var direct: float3 = Scale3(Mul3(DirectLight(rgb, normal, view, light, metallic, roughness), sun),
            uniforms.material.sun.w * visibility);
        var ambient: float3 = HemisphereLight(rgb, normal, view,
            float3(uniforms.material.sky.x, uniforms.material.sky.y, uniforms.material.sky.z),
            float3(uniforms.material.ground.x, uniforms.material.ground.y, uniforms.material.ground.z), metallic, roughness);
        if (uniforms.material.environmentParameters.y > 0.5) {
            const reflected: float3 = BoxReflection(p, Sub3(Scale3(normal, 2.0 * Max(Dot3(normal, view), 0.0)), view),
                uniforms.material.probePosition, uniforms.material.probeBounds);
            ambient = Scale3(EnvironmentResponse(resources.environment, resources.environmentSampler, rgb, normal, reflected,
                Max(Dot3(normal, view), 0.0001), metallic, roughness), uniforms.material.environmentParameters.z);
        }
        for (var index: u32 = 0; index < 32; index = index + 1) {
            if (Convert<f32>(index) < uniforms.material.environmentParameters.x) {
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
                    var attenuation: f32 = fade * fade / Max(squared, 0.01) * ConeAttenuation(direction, cone, config);
                    if (config.z > 0.5) {
                        attenuation = attenuation * LocalShadow(resources, uniforms, config.z, p, Max(Dot3(normal, direction), 0.0));
                    }
                    direct = Add3(direct, Scale3(Mul3(DirectLight(rgb, normal, view, direction, metallic, roughness),
                        float3(color.x, color.y, color.z)), color.w * attenuation));
                }
            }
        }
        lit = Add3(Add3(direct, Scale3(ambient, occlusion)), emitted);
    }
    lit = Fogged(lit, p, eye, uniforms.material.fogParameters, uniforms.material.fogColor);
    const distance: float3 = Sub3(p, eye);
    const alpha: f32 = Clamp(base.w, 0.0, 1.0);
    const weight: f32 = Max(alpha, 0.001) / (1.0 + Dot3(distance, distance) * 0.02);
    const bounded: float4 = LimitRadiance(float4(lit.x, lit.y, lit.z, 1.0));
    return {
        color: float4(bounded.x * alpha * weight, bounded.y * alpha * weight, bounded.z * alpha * weight, alpha * weight),
        optical: float4(-Log(Max(1.0 - alpha, 0.000001)), 0.0, 0.0, 0.0),
    };
}

function NearVisibility(resources: Resources, uniforms: MaterialResources, p: float3, normal: float3): f32 {
    const projected: float3 = float3(Row(uniforms.material.shadowX, p), Row(uniforms.material.shadowY, p), Row(uniforms.material.shadowZ, p));
    return PlaneShadowVisibility(resources.shadow, resources.shadowSampler, projected,
        float4(uniforms.material.shadowParameters.x, uniforms.material.cascadeBias.x, 1.0 / 1024.0, 0.0), normal, uniforms.material.shadowX, uniforms.material.shadowY, uniforms.material.shadowZ);
}
function MidVisibility(resources: Resources, uniforms: MaterialResources, p: float3, normal: float3): f32 {
    const projected: float3 = float3(Row(uniforms.material.cascadeMidX, p), Row(uniforms.material.cascadeMidY, p), Row(uniforms.material.cascadeMidZ, p));
    return PlaneShadowVisibility(resources.cascadeMid, resources.cascadeMidSampler, projected,
        float4(uniforms.material.shadowParameters.x, uniforms.material.cascadeBias.y, 1.0 / 1024.0, 0.0), normal, uniforms.material.cascadeMidX, uniforms.material.cascadeMidY, uniforms.material.cascadeMidZ);
}
function FarVisibility(resources: Resources, uniforms: MaterialResources, p: float3, normal: float3): f32 {
    const projected: float3 = float3(Row(uniforms.material.cascadeFarX, p), Row(uniforms.material.cascadeFarY, p), Row(uniforms.material.cascadeFarZ, p));
    return PlaneShadowVisibility(resources.cascadeFar, resources.cascadeFarSampler, projected,
        float4(uniforms.material.shadowParameters.x, uniforms.material.cascadeBias.z, 1.0 / 1024.0, 0.0), normal, uniforms.material.cascadeFarX, uniforms.material.cascadeFarY, uniforms.material.cascadeFarZ);
}
function DirectionalVisibility(resources: Resources, uniforms: MaterialResources, p: float3, normal: float3): f32 {
    const forward: float4 = uniforms.material.cascadeForward;
    const eye: float4 = uniforms.material.eye;
    const depth: f32 = (p.x - eye.x) * forward.x + (p.y - eye.y) * forward.y + (p.z - eye.z) * forward.z;
    const ends: float4 = uniforms.material.cascadeEnds;
    if (depth < ends.x * 0.9) {
        return NearVisibility(resources, uniforms, p, normal);
    }
    if (depth < ends.x) {
        const blend: f32 = (depth - ends.x * 0.9) / (ends.x * 0.1);
        return NearVisibility(resources, uniforms, p, normal) * (1.0 - blend) + MidVisibility(resources, uniforms, p, normal) * blend;
    }
    if (depth < ends.y * 0.9) {
        return MidVisibility(resources, uniforms, p, normal);
    }
    if (depth < ends.y) {
        const blend: f32 = (depth - ends.y * 0.9) / (ends.y * 0.1);
        return MidVisibility(resources, uniforms, p, normal) * (1.0 - blend) + FarVisibility(resources, uniforms, p, normal) * blend;
    }
    const fade: f32 = Clamp((depth - ends.z * 0.9) / (ends.z * 0.1), 0.0, 1.0);
    return FarVisibility(resources, uniforms, p, normal) * (1.0 - fade) + fade;
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
function FirstShadow(resources: Resources, uniforms: MaterialResources, p: float3, nl: f32): f32 {
    const w: f32 = Row(uniforms.material.spotAW, p);
    if (w <= 0.00001) {
        return 1.0;
    }
    const projected: float3 = float3(Row(uniforms.material.spotAX, p) / w,
        Row(uniforms.material.spotAY, p) / w, Row(uniforms.material.spotAZ, p) / w);
    return ShadowVisibility(resources.spotA, resources.spotASampler, projected, float4(1.0, 0.0015, 1.0 / 512.0, 0.0), nl);
}
function SecondShadow(resources: Resources, uniforms: MaterialResources, p: float3, nl: f32): f32 {
    const w: f32 = Row(uniforms.material.spotBW, p);
    if (w <= 0.00001) {
        return 1.0;
    }
    const projected: float3 = float3(Row(uniforms.material.spotBX, p) / w,
        Row(uniforms.material.spotBY, p) / w, Row(uniforms.material.spotBZ, p) / w);
    return ShadowVisibility(resources.spotB, resources.spotBSampler, projected, float4(1.0, 0.0015, 1.0 / 512.0, 0.0), nl);
}
function LocalShadow(resources: Resources, uniforms: MaterialResources, index: f32, p: float3, nl: f32): f32 {
    return match ShadowSourceFor(index) {
        ShadowSource.First => FirstShadow(resources, uniforms, p, nl),
        ShadowSource.Second => SecondShadow(resources, uniforms, p, nl),
    };
}

