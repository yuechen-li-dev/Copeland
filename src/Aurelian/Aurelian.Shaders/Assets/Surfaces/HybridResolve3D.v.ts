import { BindlessMaterialRecord } from "./ResidentMaterial3D";
import { ResidentVertex, ResidentInstance, ResidentAttributes, TransformResidentVertex, ProjectPoint,
    Weighted2, Weighted3, Weighted4, Cross2 } from "./ResidentGeometry3D";
import { SurfaceData, EvaluateSurfaceMaterialGrad } from "./SurfaceMaterial3D";
import { VisibilityHit, EmptyVisibilityHit, ResolveTriangleHit } from "./VisibilityGeometry3D";
import { TemporalProjection } from "./TemporalGeometry";
import { AnalyticRay, AnalyticHit, IntersectAnalytic } from "./AnalyticIntersections3D";
import { AnalyticLocalRay, AnalyticAttributes, AnalyticGradients } from "./AnalyticGeometry3D";
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

stream VertexInput { @location(0) position: float2; }
stream Varyings { @builtin(position) position: ClipPosition4; }
stream ModelOutput {
    @builtin(frag_depth) depth: f32;
    @target(0) color: float4;
    @target(1) motion: float4;
    @target(2) normal: float4;
    @target(3) emission: float4;
    @target(4) subsurface: float4;
}
@vertex
function VertexMain(input: VertexInput): Varyings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0) };
}
@pixel
function PixelMain(input: Varyings, uniforms: MaterialResources, tables: TableResources): ModelOutput {
    const hit: uint2 = Load(tables.visibility, uint2(Convert<u32>(input.position.x), Convert<u32>(input.position.y)));
    if (hit.x == 0) { Discard(); }
    const instanceId: u32 = hit.x - 1;
    const instance: ResidentInstance = tables.instances[instanceId];
    var resolved: VisibilityHit = EmptyVisibilityHit();
    if ((hit.y & 0x80000000) == 0) {
        resolved = ResolveTriangleHit(hit, float2(input.position.x, input.position.y),
            float2(uniforms.material.baseColor.x, uniforms.material.baseColor.y), instance,
            uniforms.material.clipX, uniforms.material.clipY, uniforms.material.clipZ, uniforms.material.clipW,
            tables.vertices, tables.indices);
    } else {
        const viewport: float2 = float2(uniforms.material.baseColor.x, uniforms.material.baseColor.y);
        const pixel: float2 = float2(input.position.x, input.position.y);
        const ray: AnalyticRay = AnalyticLocalRay(pixel, viewport, instance, uniforms.material.clipX, uniforms.material.clipY,
            uniforms.material.clipZ, uniforms.material.clipW);
        const analytic: AnalyticHit = IntersectAnalytic(ray, instance.primitiveKind);
        if (analytic.valid == 0) { Discard(); }
        const gradient: float4 = AnalyticGradients(pixel, viewport, analytic, instance, uniforms.material.clipX,
            uniforms.material.clipY, uniforms.material.clipZ, uniforms.material.clipW);
        resolved = { attributes: AnalyticAttributes(analytic, instance), uvDx: float2(gradient.x, gradient.y),
            uvDy: float2(gradient.z, gradient.w), valid: 1 };
    }
    if (resolved.valid == 0) { Discard(); }
    const material: BindlessMaterialRecord = tables.materials[tables.instanceMaterials[instanceId]];
    const tangent: float4 = resolved.attributes.tangent;
    const surface: SurfaceData = EvaluateSurfaceMaterialGrad(resolved.attributes.normal,
        resolved.attributes.color, resolved.attributes.uv, resolved.uvDx, resolved.uvDy, tangent, hit.y % 2 == 0, material.surface,
        tables.textures[NonUniform(material.baseTexture)], tables.samplers[NonUniform(material.baseSampler)],
        tables.textures[NonUniform(material.metalTexture)], tables.samplers[NonUniform(material.metalSampler)],
        tables.textures[NonUniform(material.normalTexture)], tables.samplers[NonUniform(material.normalSampler)],
        tables.textures[NonUniform(material.occlusionTexture)], tables.samplers[NonUniform(material.occlusionSampler)],
        tables.textures[NonUniform(material.emissiveTexture)], tables.samplers[NonUniform(material.emissiveSampler)]);
    const world: float3 = resolved.attributes.world;
    const previous: float3 = resolved.attributes.previous;
    const currentClip: float4 = ProjectPoint(world, uniforms.material.clipX, uniforms.material.clipY, uniforms.material.clipZ, uniforms.material.clipW);
    const previousClip: float4 = ProjectPoint(previous, uniforms.material.previousX, uniforms.material.previousY, uniforms.material.previousZ, uniforms.material.previousW);
    return { depth: currentClip.z / currentClip.w, color: surface.color, motion: TemporalProjection(currentClip, previousClip),
        normal: surface.normal, emission: surface.emission, subsurface: surface.subsurface };
}
