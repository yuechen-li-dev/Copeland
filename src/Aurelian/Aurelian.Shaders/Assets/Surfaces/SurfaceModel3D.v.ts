import { TemporalProjection } from "./TemporalGeometry";
import { Unit, Sub3, ShadowVisibility, Dot3, Scale3, Mul3, DirectLight, HemisphereLight, Add3, Cross3, LimitRadiance } from "./Lighting3D";

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
    // RGB diffusion weight, world-space support radius. Shared lighting owns ambient radiance.
    subsurfaceProfile: float4;
    shadowX: float4;
    shadowY: float4;
    shadowZ: float4;
    shadowW: float4;
    shadowParameters: float4;
    previousX: float4;
    previousY: float4;
    previousZ: float4;
    previousW: float4;
}

stream VertexInput {
    @location(5) previousPosition: WorldPosition3;
    @location(0) position: WorldPosition3;
    @location(1) normal: float3;
    @location(2) color: float4;
    @location(3) uv: float2;
    @location(4) tangent: float4;
}

stream MaterialResources {
    @binding(0) material: StaticModelMaterial;
}
stream ModelResources {
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
}

stream ModelVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) world: float3;
    @location(1) normal: float3;
    @location(2) color: float4;
    @location(3) uv: float2;
    @location(4) tangent: float4;
    @location(5) currentClip: float4;
    @location(6) previousClip: float4;
}
stream PixelBuiltins { @builtin(front_face) frontFace: bool; }
stream ModelOutput {
    @target(0) color: float4;
    @target(1) motion: float4;
    @target(2) normal: float4;
    @target(3) emission: float4;
    @target(4) subsurface: float4;
}

function Row(row: float4, p: WorldPosition3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
@vertex
function VertexMain(input: VertexInput, uniforms: MaterialResources): ModelVaryings {
    return {
        position: float4(Row(uniforms.material.clipX, input.position), Row(uniforms.material.clipY, input.position),
            Row(uniforms.material.clipZ, input.position), Row(uniforms.material.clipW, input.position)),
        currentClip: float4(Row(uniforms.material.clipX, input.position), Row(uniforms.material.clipY, input.position),
            Row(uniforms.material.clipZ, input.position), Row(uniforms.material.clipW, input.position)),
        previousClip: float4(Row(uniforms.material.previousX, input.previousPosition), Row(uniforms.material.previousY, input.previousPosition),
            Row(uniforms.material.previousZ, input.previousPosition), Row(uniforms.material.previousW, input.previousPosition)),
        world: float3(input.position.x, input.position.y, input.position.z),
        normal: input.normal,
        color: input.color,
        uv: input.uv,
        tangent: input.tangent,
    };
}

@pixel
function PixelMain(input: ModelVaryings, uniforms: MaterialResources, resources: ModelResources, builtins: PixelBuiltins): ModelOutput {
    if (!builtins.frontFace && uniforms.material.flags.z < 0.5) {
        Discard();
    }
    const base: float4 = Sample(resources.baseMap, resources.baseSampler, input.uv) * uniforms.material.baseColor * input.color;
    if (uniforms.material.flags.y > 0.5 && base.w < uniforms.material.emissiveAlpha.w) {
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
    return {
        color: float4(base.x, base.y, base.z, roughness),
        motion: TemporalProjection(input.currentClip, input.previousClip),
        normal: float4(normal.x, normal.y, normal.z, metallic),
        emission: float4(emitted.x, emitted.y, emitted.z, occlusion),
        subsurface: uniforms.material.subsurfaceProfile,
    };
}
