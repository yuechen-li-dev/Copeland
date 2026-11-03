import { SurfaceMaterial, SurfaceData, EvaluateSurfaceMaterial } from "./SurfaceMaterial3D";
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
    let material: SurfaceMaterial = { baseColor: uniforms.material.baseColor, factors: uniforms.material.factors,
        emissiveAlpha: uniforms.material.emissiveAlpha, flags: uniforms.material.flags,
        subsurfaceProfile: uniforms.material.subsurfaceProfile };
    let surface: SurfaceData = EvaluateSurfaceMaterial(input.normal, input.color, input.uv, input.tangent, builtins.frontFace, material,
        resources.baseMap, resources.baseSampler, resources.metalMap, resources.metalSampler,
        resources.normalMap, resources.normalSampler, resources.occlusionMap, resources.occlusionSampler,
        resources.emissiveMap, resources.emissiveSampler);
    return { color: surface.color, motion: TemporalProjection(input.currentClip, input.previousClip),
        normal: surface.normal, emission: surface.emission, subsurface: surface.subsurface };
}
