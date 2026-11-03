import { Unit, Mul3, Scale3, Add3, Cross3, LimitRadiance } from "./Lighting3D";

export record SurfaceMaterial {
    baseColor: float4;
    factors: float4;
    emissiveAlpha: float4;
    flags: float4;
    subsurfaceProfile: float4;
}
export record SurfaceData {
    color: float4;
    normal: float4;
    emission: float4;
    subsurface: float4;
}
export function EvaluateSurfaceMaterial(surfaceNormal: float3, vertexColor: float4, uv: float2,
    surfaceTangent: float4, frontFace: bool, material: SurfaceMaterial,
    baseMap: Texture2D<float4>, baseSampler: Sampler,
    metalMap: Texture2D<float4>, metalSampler: Sampler,
    normalMap: Texture2D<float4>, normalSampler: Sampler,
    occlusionMap: Texture2D<float4>, occlusionSampler: Sampler,
    emissiveMap: Texture2D<float4>, emissiveSampler: Sampler): SurfaceData {
    if (!frontFace && material.flags.z < 0.5) {
        Discard();
    }
    const base: float4 = Sample(baseMap, baseSampler, uv) * material.baseColor * vertexColor;
    if (material.flags.y > 0.5 && base.w < material.emissiveAlpha.w) {
        Discard();
    }
    const emission: float4 = Sample(emissiveMap, emissiveSampler, uv);
    const emitted: float3 = Mul3(float3(emission.x, emission.y, emission.z),
        float3(material.emissiveAlpha.x, material.emissiveAlpha.y, material.emissiveAlpha.z));
    const nt: float4 = Sample(normalMap, normalSampler, uv);
    const tangent: float3 = Unit(float3(surfaceTangent.x, surfaceTangent.y, surfaceTangent.z));
    const geometric: float3 = Unit(surfaceNormal);
    const bitangent: float3 = Scale3(Cross3(geometric, tangent), surfaceTangent.w);
    var normal: float3 = Unit(Add3(Add3(Scale3(tangent, (nt.x * 2.0 - 1.0) * material.factors.z),
        Scale3(bitangent, (nt.y * 2.0 - 1.0) * material.factors.z)), Scale3(geometric, nt.z * 2.0 - 1.0)));
    if (!frontFace) {
        normal = Scale3(normal, -1.0);
    }
    const mr: float4 = Sample(metalMap, metalSampler, uv);
    const metallic: f32 = Clamp(mr.z * material.factors.x, 0.0, 1.0);
    const roughness: f32 = Clamp(mr.y * material.factors.y, 0.045, 1.0);
    const ao: float4 = Sample(occlusionMap, occlusionSampler, uv);
    var occlusion: f32 = 1.0 - material.factors.w + ao.x * material.factors.w;
    if (material.flags.x > 0.5) {
        occlusion = -1.0;
    }
    return {
        color: float4(base.x, base.y, base.z, roughness),
        normal: float4(normal.x, normal.y, normal.z, metallic),
        emission: float4(emitted.x, emitted.y, emitted.z, occlusion),
        subsurface: material.subsurfaceProfile,
    };
}
