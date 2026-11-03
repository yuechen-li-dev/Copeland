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
export function TestSurfaceCoverage(frontFace: bool, material: SurfaceMaterial, sample: float4, vertexColor: float4): float4 {
    if (!frontFace && material.flags.z < 0.5) { Discard(); }
    const base: float4 = sample * material.baseColor * vertexColor;
    if (material.flags.y > 0.5 && base.w < material.emissiveAlpha.w) { Discard(); }
    return base;
}
function EvaluateSurfaceSamples(surfaceNormal: float3, surfaceTangent: float4, frontFace: bool, material: SurfaceMaterial,
    base: float4, emission: float4, nt: float4, mr: float4, ao: float4): SurfaceData {
    const emitted: float3 = Mul3(float3(emission.x, emission.y, emission.z),
        float3(material.emissiveAlpha.x, material.emissiveAlpha.y, material.emissiveAlpha.z));
    const tangent: float3 = Unit(float3(surfaceTangent.x, surfaceTangent.y, surfaceTangent.z));
    const geometric: float3 = Unit(surfaceNormal);
    const bitangent: float3 = Scale3(Cross3(geometric, tangent), surfaceTangent.w);
    var normal: float3 = Unit(Add3(Add3(Scale3(tangent, (nt.x * 2.0 - 1.0) * material.factors.z),
        Scale3(bitangent, (nt.y * 2.0 - 1.0) * material.factors.z)), Scale3(geometric, nt.z * 2.0 - 1.0)));
    if (!frontFace) {
        normal = Scale3(normal, -1.0);
    }
    const metallic: f32 = Clamp(mr.z * material.factors.x, 0.0, 1.0);
    const roughness: f32 = Clamp(mr.y * material.factors.y, 0.045, 1.0);
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
export function EvaluateSurfaceMaterial(surfaceNormal: float3, vertexColor: float4, uv: float2,
    surfaceTangent: float4, frontFace: bool, material: SurfaceMaterial,
    baseMap: Texture2D<float4>, baseSampler: Sampler,
    metalMap: Texture2D<float4>, metalSampler: Sampler,
    normalMap: Texture2D<float4>, normalSampler: Sampler,
    occlusionMap: Texture2D<float4>, occlusionSampler: Sampler,
    emissiveMap: Texture2D<float4>, emissiveSampler: Sampler): SurfaceData {
    const base: float4 = TestSurfaceCoverage(frontFace, material, Sample(baseMap, baseSampler, uv), vertexColor);
    return EvaluateSurfaceSamples(surfaceNormal, surfaceTangent, frontFace, material, base,
        Sample(emissiveMap, emissiveSampler, uv), Sample(normalMap, normalSampler, uv),
        Sample(metalMap, metalSampler, uv), Sample(occlusionMap, occlusionSampler, uv));
}
export function EvaluateSurfaceMaterialGrad(surfaceNormal: float3, vertexColor: float4, uv: float2, uvx: float2, uvy: float2,
    surfaceTangent: float4, frontFace: bool, material: SurfaceMaterial,
    baseMap: Texture2D<float4>, baseSampler: Sampler,
    metalMap: Texture2D<float4>, metalSampler: Sampler,
    normalMap: Texture2D<float4>, normalSampler: Sampler,
    occlusionMap: Texture2D<float4>, occlusionSampler: Sampler,
    emissiveMap: Texture2D<float4>, emissiveSampler: Sampler): SurfaceData {
    // Rasterization already tested coverage using its interpolated UV and implicit derivatives.
    const base: float4 = SampleGrad(baseMap, baseSampler, uv, uvx, uvy) * material.baseColor * vertexColor;
    return EvaluateSurfaceSamples(surfaceNormal, surfaceTangent, frontFace, material, base,
        SampleGrad(emissiveMap, emissiveSampler, uv, uvx, uvy), SampleGrad(normalMap, normalSampler, uv, uvx, uvy),
        SampleGrad(metalMap, metalSampler, uv, uvx, uvy), SampleGrad(occlusionMap, occlusionSampler, uv, uvx, uvy));
}
