import { BindlessMaterialRecord } from "./ResidentMaterial3D";
import { ResidentVertex, ResidentInstance, ResidentAttributes, PointRow } from "./ResidentGeometry3D";
import { TestSurfaceCoverage } from "./SurfaceMaterial3D";
import { AnalyticRay, AnalyticHit, IntersectAnalytic } from "./AnalyticIntersections3D";
import { AnalyticLocalRay, AnalyticAttributes, AnalyticGradients } from "./AnalyticGeometry3D";
import { Dot3 } from "./Lighting3D";
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
stream VisibilityOutput {
    @builtin(frag_depth) depth: f32;
    @target(0) identity: uint2;
}
@vertex
function VertexMain(input: VertexInput, uniforms: MaterialResources, tables: TableResources, builtins: VertexBuiltins): VisibilityVaryings {
    let instance: ResidentInstance = tables.instances[builtins.instance];
    var minimum: float2 = float2(1.0, 1.0);
    var maximum: float2 = float2(-1.0, -1.0);
    var clipped: bool = false;
    var radial: f32 = 1.0;
    if (instance.primitiveKind == 3) { radial = 0.5; }
    static for (let corner of Indices<8>()) {
        var x: f32 = -radial;
        var y: f32 = -1.0;
        var z: f32 = -radial;
        if ((corner & 1) != 0) { x = radial; }
        if ((corner & 2) != 0) { y = 1.0; }
        if ((corner & 4) != 0) { z = radial; }
        let point: float3 = float3(x, y, z);
        let world: float3 = float3(PointRow(input.worldX, point), PointRow(input.worldY, point), PointRow(input.worldZ, point));
        let clip: float4 = float4(PointRow(uniforms.material.clipX, world), PointRow(uniforms.material.clipY, world),
            PointRow(uniforms.material.clipZ, world), PointRow(uniforms.material.clipW, world));
        if (clip.w <= 0.00001 || clip.z <= 0.0) { clipped = true; }
        if (clip.w > 0.00001) {
            minimum = float2(Min(minimum.x, clip.x / clip.w), Min(minimum.y, clip.y / clip.w));
            maximum = float2(Max(maximum.x, clip.x / clip.w), Max(maximum.y, clip.y / clip.w));
        }
    }
    if (clipped) { minimum = float2(-1.0, -1.0); maximum = float2(1.0, 1.0); }
    minimum = float2(Max(-1.0, minimum.x - 4.0 / uniforms.material.baseColor.x), Max(-1.0, minimum.y - 4.0 / uniforms.material.baseColor.y));
    maximum = float2(Min(1.0, maximum.x + 4.0 / uniforms.material.baseColor.x), Min(1.0, maximum.y + 4.0 / uniforms.material.baseColor.y));
    let x: f32 = minimum.x + (maximum.x - minimum.x) * (input.position.x * 0.5 + 0.5);
    let y: f32 = minimum.y + (maximum.y - minimum.y) * (input.position.y * 0.5 + 0.5);
    return { position: float4(x, y, 0.0, 1.0), uv: float2(0.0, 0.0), color: float4(1.0, 1.0, 1.0, 1.0),
        instance: builtins.instance, firstTriangle: 0 };
}

@pixel
function PixelMain(input: VisibilityVaryings, uniforms: MaterialResources, tables: TableResources): VisibilityOutput {
    let instance: ResidentInstance = tables.instances[input.instance];
    let viewport: float2 = float2(uniforms.material.baseColor.x, uniforms.material.baseColor.y);
    let pixel: float2 = float2(input.position.x, input.position.y);
    let ray: AnalyticRay = AnalyticLocalRay(pixel, viewport, instance, uniforms.material.clipX, uniforms.material.clipY,
        uniforms.material.clipZ, uniforms.material.clipW);
    let hit: AnalyticHit = IntersectAnalytic(ray, instance.primitiveKind);
    if (hit.valid == 0) { Discard(); }
    let attributes: ResidentAttributes = AnalyticAttributes(hit, instance);
    let clip: float4 = float4(PointRow(uniforms.material.clipX, attributes.world), PointRow(uniforms.material.clipY, attributes.world),
        PointRow(uniforms.material.clipZ, attributes.world), PointRow(uniforms.material.clipW, attributes.world));
    let depth: f32 = clip.z / clip.w;
    if (clip.w <= 0.0 || depth < 0.0 || depth > 1.0) { Discard(); }
    let material: BindlessMaterialRecord = tables.materials[tables.instanceMaterials[input.instance]];
    let front: bool = Dot3(hit.normal, ray.direction) < 0.0;
    var baseSample: float4 = float4(1.0, 1.0, 1.0, 1.0);
    if (material.surface.flags.y > 0.5) {
        let gradient: float4 = AnalyticGradients(pixel, viewport, hit, instance, uniforms.material.clipX,
            uniforms.material.clipY, uniforms.material.clipZ, uniforms.material.clipW);
        baseSample = SampleGrad(tables.textures[NonUniform(material.baseTexture)], tables.samplers[NonUniform(material.baseSampler)],
            attributes.uv, float2(gradient.x, gradient.y), float2(gradient.z, gradient.w));
    }
    let covered: float4 = TestSurfaceCoverage(front, material.surface, baseSample, attributes.color);
    var face: u32 = 0;
    if (!front) { face = 1; }
    return { depth: depth, identity: uint2(input.instance + 1, 0x80000000 + face) };
}
