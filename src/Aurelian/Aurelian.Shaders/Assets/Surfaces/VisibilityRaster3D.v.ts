import { BindlessMaterialRecord } from "./ResidentMaterial3D";
import { ResidentVertex, ResidentInstance, PointRow } from "./ResidentGeometry3D";
import { TestSurfaceCoverage } from "./SurfaceMaterial3D";
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
    @binding(5) vertices: StorageBuffer<ResidentVertex>;
    @binding(6) indices: StorageBuffer<u32>;
    @binding(7) instances: StorageBuffer<ResidentInstance>;
    @binding(8) commands: StorageBuffer<u32>;
    @binding(9) visibility: Texture2D<uint2>;
}

stream VertexBuiltins {
    @builtin(instance_id) instance: u32;
    @builtin(draw_id) draw: u32;
}
stream PixelBuiltins {
    @builtin(front_face) frontFace: bool;
    @builtin(primitive_id) primitive: u32;
}
stream VisibilityVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
    @location(1) color: float4;
    @location(2) @interpolation(flat) instance: u32;
    @location(3) @interpolation(flat) firstTriangle: u32;
}
stream VisibilityOutput { @target(0) identity: uint2; }
@vertex
function VertexMain(input: VertexInput, uniforms: MaterialResources, tables: TableResources, builtins: VertexBuiltins): VisibilityVaryings {
    const world: float3 = float3(PointRow(input.worldX, input.position), PointRow(input.worldY, input.position), PointRow(input.worldZ, input.position));
    const instance: ResidentInstance = tables.instances[builtins.instance];
    const firstIndex: u32 = tables.commands[instance.commandBase + builtins.draw * 5 + 2];
    return { position: float4(PointRow(uniforms.material.clipX, world), PointRow(uniforms.material.clipY, world),
            PointRow(uniforms.material.clipZ, world), PointRow(uniforms.material.clipW, world)),
        uv: input.uv, color: input.color, instance: builtins.instance, firstTriangle: firstIndex / 3 };
}
@pixel
function PixelMain(input: VisibilityVaryings, tables: TableResources, builtins: PixelBuiltins): VisibilityOutput {
    const material: BindlessMaterialRecord = tables.materials[tables.instanceMaterials[input.instance]];
    // Coverage is the only material work in visibility rasterization.
    var baseSample: float4 = float4(1.0, 1.0, 1.0, 1.0);
    if (material.surface.flags.y > 0.5) {
        baseSample = Sample(tables.textures[NonUniform(material.baseTexture)],
            tables.samplers[NonUniform(material.baseSampler)], input.uv);
    }
    const covered: float4 = TestSurfaceCoverage(builtins.frontFace, material.surface, baseSample, input.color);
    var face: u32 = 0;
    if (!builtins.frontFace) { face = 1; }
    return { identity: uint2(input.instance + 1, (input.firstTriangle + builtins.primitive) * 2 + face) };
}
