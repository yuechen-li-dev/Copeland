import { BindlessMaterialRecord } from "./ResidentMaterial3D";
import { TemporalProjection } from "./TemporalGeometry";
import { SurfaceMaterial, SurfaceData, EvaluateSurfaceMaterial } from "./SurfaceMaterial3D";
import { Unit, Dot3, Scale3, Sub3 } from "./Lighting3D";

@space(clip.position)
type ClipPosition4 = float4;

// Matches SurfaceModel3D's material ABI. Its existing fragment stage is linked unchanged.
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
    @location(0) position: float3;
    @location(1) normal: float3;
    @location(2) color: float4;
    @location(3) uv: float2;
    @location(4) tangent: float4;
    @location(5) worldX: float4;
    @location(6) worldY: float4;
    @location(7) worldZ: float4;
    @location(8) previousX: float4;
    @location(9) previousY: float4;
    @location(10) previousZ: float4;
    @location(11) normalX: float4;
    @location(12) normalY: float4;
    @location(13) normalZ: float4;
}
stream MaterialResources { @binding(0) material: StaticModelMaterial; }
stream TableResources {
    @binding(1) materials: StorageBuffer<BindlessMaterialRecord>;
    @binding(2) instanceMaterials: StorageBuffer<u32>;
    @binding(3) textures: DescriptorArray<Texture2D<float4>, 4096>;
    @binding(4) samplers: DescriptorArray<Sampler, 64>;
}
stream VertexBuiltins { @builtin(instance_id) instance: u32; }
stream PixelBuiltins { @builtin(front_face) frontFace: bool; }
stream ModelOutput {
    @target(0) color: float4;
    @target(1) motion: float4;
    @target(2) normal: float4;
    @target(3) emission: float4;
    @target(4) subsurface: float4;
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
    @location(7) @interpolation(flat) materialId: u32;
}

function PointRow(row: float4, point: float3): f32 {
    return row.x * point.x + row.y * point.y + row.z * point.z + row.w;
}
function DirectionRow(row: float4, direction: float3): f32 {
    return row.x * direction.x + row.y * direction.y + row.z * direction.z;
}
@vertex
function VertexMain(input: VertexInput, uniforms: MaterialResources, tables: TableResources, builtins: VertexBuiltins): ModelVaryings {
    let world: float3 = float3(PointRow(input.worldX, input.position), PointRow(input.worldY, input.position),
        PointRow(input.worldZ, input.position));
    let previous: float3 = float3(PointRow(input.previousX, input.position), PointRow(input.previousY, input.position),
        PointRow(input.previousZ, input.position));
    let normal: float3 = Unit(float3(DirectionRow(input.normalX, input.normal),
        DirectionRow(input.normalY, input.normal), DirectionRow(input.normalZ, input.normal)));
    let localTangent: float3 = float3(input.tangent.x, input.tangent.y, input.tangent.z);
    let transformedTangent: float3 = float3(DirectionRow(input.worldX, localTangent),
        DirectionRow(input.worldY, localTangent), DirectionRow(input.worldZ, localTangent));
    let tangent: float3 = Unit(Sub3(transformedTangent, Scale3(normal, Dot3(normal, transformedTangent))));
    return {
        materialId: tables.instanceMaterials[builtins.instance],
        position: float4(PointRow(uniforms.material.clipX, world), PointRow(uniforms.material.clipY, world),
            PointRow(uniforms.material.clipZ, world), PointRow(uniforms.material.clipW, world)),
        currentClip: float4(PointRow(uniforms.material.clipX, world), PointRow(uniforms.material.clipY, world),
            PointRow(uniforms.material.clipZ, world), PointRow(uniforms.material.clipW, world)),
        previousClip: float4(PointRow(uniforms.material.previousX, previous), PointRow(uniforms.material.previousY, previous),
            PointRow(uniforms.material.previousZ, previous), PointRow(uniforms.material.previousW, previous)),
        world: world,
        normal: normal,
        color: input.color,
        uv: input.uv,
        tangent: float4(tangent.x, tangent.y, tangent.z, input.tangent.w),
    };
}
@pixel
function PixelMain(input: ModelVaryings, tables: TableResources, builtins: PixelBuiltins): ModelOutput {
    let material: BindlessMaterialRecord = tables.materials[input.materialId];
    let surface: SurfaceData = EvaluateSurfaceMaterial(input.normal, input.color, input.uv, input.tangent, builtins.frontFace, material.surface,
        tables.textures[NonUniform(material.baseTexture)], tables.samplers[NonUniform(material.baseSampler)],
        tables.textures[NonUniform(material.metalTexture)], tables.samplers[NonUniform(material.metalSampler)],
        tables.textures[NonUniform(material.normalTexture)], tables.samplers[NonUniform(material.normalSampler)],
        tables.textures[NonUniform(material.occlusionTexture)], tables.samplers[NonUniform(material.occlusionSampler)],
        tables.textures[NonUniform(material.emissiveTexture)], tables.samplers[NonUniform(material.emissiveSampler)]);
    return { color: surface.color, motion: TemporalProjection(input.currentClip, input.previousClip),
        normal: surface.normal, emission: surface.emission, subsurface: surface.subsurface };
}
