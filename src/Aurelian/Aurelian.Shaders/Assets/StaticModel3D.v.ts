import { Unit, Sub3, ShadowVisibility, Dot3, Scale3, Mul3, DirectLight, HemisphereLight, Add3, Cross3 } from "./Lighting3D";

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
    @binding(11) shadowMap: Texture2D<float4>;
    @binding(12) shadowSampler: Sampler;
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
stream ModelOutput { @target(0) color: float4; }

function Row(row: float4, p: WorldPosition3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
@vertex
function VertexMain(input: VertexInput, uniforms: MaterialResources): ModelVaryings {
    return {
        position: float4(Row(uniforms.material.clipX, input.position), Row(uniforms.material.clipY, input.position),
            Row(uniforms.material.clipZ, input.position), Row(uniforms.material.clipW, input.position)),
        world: float3(input.position.x, input.position.y, input.position.z),
        normal: input.normal,
        color: input.color,
        uv: input.uv,
        tangent: input.tangent,
    };
}

@pixel
function PixelMain(input: ModelVaryings, uniforms: MaterialResources, resources: ModelResources, builtins: PixelBuiltins): ModelOutput {
    if (!builtins.frontFace && uniforms.material.flags.z < 0.5) { Discard(); }
    const base: float4 = Sample(resources.baseMap, resources.baseSampler, input.uv) * uniforms.material.baseColor * input.color;
    if (uniforms.material.flags.y > 0.5 && base.w < uniforms.material.emissiveAlpha.w) { Discard(); }
    if (uniforms.material.flags.x > 0.5) { return { color: float4(base.x, base.y, base.z, 1.0) }; }

    const nt: float4 = Sample(resources.normalMap, resources.normalSampler, input.uv);
    const tangent: float3 = Unit(float3(input.tangent.x, input.tangent.y, input.tangent.z));
    const geometric: float3 = Unit(input.normal);
    const bitangent: float3 = Scale3(Cross3(geometric, tangent), input.tangent.w);
    var normal: float3 = Unit(Add3(Add3(Scale3(tangent, (nt.x * 2.0 - 1.0) * uniforms.material.factors.z),
        Scale3(bitangent, (nt.y * 2.0 - 1.0) * uniforms.material.factors.z)), Scale3(geometric, nt.z * 2.0 - 1.0)));
    if (!builtins.frontFace) { normal = Scale3(normal, -1.0); }
    const light: float3 = Unit(float3(uniforms.material.light.x, uniforms.material.light.y, uniforms.material.light.z));
    const view: float3 = Unit(Sub3(float3(uniforms.material.eye.x, uniforms.material.eye.y, uniforms.material.eye.z), input.world));
    const mr: float4 = Sample(resources.metalMap, resources.metalSampler, input.uv);
    const metallic: f32 = Clamp(mr.z * uniforms.material.factors.x, 0.0, 1.0);
    const roughness: f32 = Clamp(mr.y * uniforms.material.factors.y, 0.045, 1.0);
    const p: WorldPosition3 = float3(input.world.x, input.world.y, input.world.z);
    const projected: float3 = float3(Row(uniforms.material.shadowX, p), Row(uniforms.material.shadowY, p), Row(uniforms.material.shadowZ, p));
    const visibility: f32 = ShadowVisibility(resources.shadowMap, resources.shadowSampler, projected,
        uniforms.material.shadowParameters, Max(Dot3(normal, light), 0.0));
    const direct: float3 = Scale3(Mul3(DirectLight(float3(base.x, base.y, base.z), normal, view, light, metallic, roughness),
        float3(uniforms.material.sun.x, uniforms.material.sun.y, uniforms.material.sun.z)), uniforms.material.sun.w * visibility);
    const ao: float4 = Sample(resources.occlusionMap, resources.occlusionSampler, input.uv);
    const occlusion: f32 = 1.0 - uniforms.material.factors.w + ao.x * uniforms.material.factors.w;
    const ambient: float3 = Scale3(HemisphereLight(float3(base.x, base.y, base.z), normal, view,
        float3(uniforms.material.sky.x, uniforms.material.sky.y, uniforms.material.sky.z),
        float3(uniforms.material.ground.x, uniforms.material.ground.y, uniforms.material.ground.z), metallic, roughness), occlusion);
    const emission: float4 = Sample(resources.emissiveMap, resources.emissiveSampler, input.uv);
    const emitted: float3 = Mul3(float3(emission.x, emission.y, emission.z),
        float3(uniforms.material.emissiveAlpha.x, uniforms.material.emissiveAlpha.y, uniforms.material.emissiveAlpha.z));
    const lit: float3 = Add3(Add3(direct, ambient), emitted);
    return { color: float4(lit.x, lit.y, lit.z, 1.0) };
}
