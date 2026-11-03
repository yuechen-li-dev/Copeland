import { BindlessMaterialRecord } from "./ResidentMaterial3D";
import { ResidentVertex, ResidentInstance, ResidentAttributes, TransformResidentVertex, ProjectPoint,
    Weighted2, Weighted3, Weighted4, Cross2 } from "./ResidentGeometry3D";
import { SurfaceData, EvaluateSurfaceMaterialGrad } from "./SurfaceMaterial3D";
import { TemporalProjection } from "./TemporalGeometry";
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
    const triangle: u32 = instance.indexBase + (hit.y / 2) * 3;
    const a: ResidentAttributes = TransformResidentVertex(tables.vertices[instance.vertexBase + tables.indices[triangle]], instance);
    const b: ResidentAttributes = TransformResidentVertex(tables.vertices[instance.vertexBase + tables.indices[triangle + 1]], instance);
    const c: ResidentAttributes = TransformResidentVertex(tables.vertices[instance.vertexBase + tables.indices[triangle + 2]], instance);
    const ca: float4 = ProjectPoint(a.world, uniforms.material.clipX, uniforms.material.clipY, uniforms.material.clipZ, uniforms.material.clipW);
    const cb: float4 = ProjectPoint(b.world, uniforms.material.clipX, uniforms.material.clipY, uniforms.material.clipZ, uniforms.material.clipW);
    const cc: float4 = ProjectPoint(c.world, uniforms.material.clipX, uniforms.material.clipY, uniforms.material.clipZ, uniforms.material.clipW);
    // Homogeneous barycentrics avoid division by a vertex w at near-plane crossings.
    const px: f32 = input.position.x * 2.0 / uniforms.material.baseColor.x - 1.0;
    const py: f32 = input.position.y * 2.0 / uniforms.material.baseColor.y - 1.0;
    const aa: float2 = float2(ca.x - px * ca.w, ca.y - py * ca.w);
    const bb: float2 = float2(cb.x - px * cb.w, cb.y - py * cb.w);
    const ccv: float2 = float2(cc.x - px * cc.w, cc.y - py * cc.w);
    const ra: f32 = Cross2(bb, ccv);
    const rb: f32 = Cross2(ccv, aa);
    const rc: f32 = Cross2(aa, bb);
    const sum: f32 = ra + rb + rc;
    if (sum == 0.0) { Discard(); }
    const weights: float3 = float3(ra / sum, rb / sum, rc / sum);
    const dx: f32 = 2.0 / uniforms.material.baseColor.x;
    const dy: f32 = 2.0 / uniforms.material.baseColor.y;
    const drax: f32 = dx * (cc.w * bb.y - cb.w * ccv.y);
    const drbx: f32 = dx * (ca.w * ccv.y - cc.w * aa.y);
    const drcx: f32 = dx * (cb.w * aa.y - ca.w * bb.y);
    const dray: f32 = dy * (cb.w * ccv.x - cc.w * bb.x);
    const drby: f32 = dy * (cc.w * aa.x - ca.w * ccv.x);
    const drcy: f32 = dy * (ca.w * bb.x - cb.w * aa.x);
    const uv: float2 = Weighted2(a.uv, b.uv, c.uv, weights);
    const dsumx: f32 = drax + drbx + drcx;
    const dsumy: f32 = dray + drby + drcy;
    const uvx: float2 = float2((a.uv.x * drax + b.uv.x * drbx + c.uv.x * drcx - uv.x * dsumx) / sum,
        (a.uv.y * drax + b.uv.y * drbx + c.uv.y * drcx - uv.y * dsumx) / sum);
    const uvy: float2 = float2((a.uv.x * dray + b.uv.x * drby + c.uv.x * drcy - uv.x * dsumy) / sum,
        (a.uv.y * dray + b.uv.y * drby + c.uv.y * drcy - uv.y * dsumy) / sum);
    // Reconstruct one complete quad from this triangle, including helper lanes across ID boundaries.
    // Finite pixel differences match raster texture footprints more closely than infinitesimal derivatives.
    const qx: f32 = Floor(input.position.x * 0.5) * 2.0 + 0.5 - input.position.x;
    const qy: f32 = Floor(input.position.y * 0.5) * 2.0 + 0.5 - input.position.y;
    const qa: f32 = ra + qx * drax + qy * dray;
    const qb: f32 = rb + qx * drbx + qy * drby;
    const qc: f32 = rc + qx * drcx + qy * drcy;
    const qs: f32 = qa + qb + qc;
    const qsx: f32 = qs + dsumx;
    const qsy: f32 = qs + dsumy;
    var sampleDx: float2 = uvx;
    var sampleDy: float2 = uvy;
    if (qs != 0.0 && qsx != 0.0 && qsy != 0.0) {
        const origin: float2 = Weighted2(a.uv, b.uv, c.uv, float3(qa / qs, qb / qs, qc / qs));
        const right: float2 = Weighted2(a.uv, b.uv, c.uv, float3((qa + drax) / qsx, (qb + drbx) / qsx, (qc + drcx) / qsx));
        const down: float2 = Weighted2(a.uv, b.uv, c.uv, float3((qa + dray) / qsy, (qb + drby) / qsy, (qc + drcy) / qsy));
        sampleDx = float2(right.x - origin.x, right.y - origin.y);
        sampleDy = float2(down.x - origin.x, down.y - origin.y);
    }
    const material: BindlessMaterialRecord = tables.materials[tables.instanceMaterials[instanceId]];
    const tangent: float4 = Weighted4(a.tangent, b.tangent, c.tangent, weights);
    const surface: SurfaceData = EvaluateSurfaceMaterialGrad(Weighted3(a.normal, b.normal, c.normal, weights),
        Weighted4(a.color, b.color, c.color, weights), uv, sampleDx, sampleDy, tangent, hit.y % 2 == 0, material.surface,
        tables.textures[NonUniform(material.baseTexture)], tables.samplers[NonUniform(material.baseSampler)],
        tables.textures[NonUniform(material.metalTexture)], tables.samplers[NonUniform(material.metalSampler)],
        tables.textures[NonUniform(material.normalTexture)], tables.samplers[NonUniform(material.normalSampler)],
        tables.textures[NonUniform(material.occlusionTexture)], tables.samplers[NonUniform(material.occlusionSampler)],
        tables.textures[NonUniform(material.emissiveTexture)], tables.samplers[NonUniform(material.emissiveSampler)]);
    const world: float3 = Weighted3(a.world, b.world, c.world, weights);
    const previous: float3 = Weighted3(a.previous, b.previous, c.previous, weights);
    const currentClip: float4 = ProjectPoint(world, uniforms.material.clipX, uniforms.material.clipY, uniforms.material.clipZ, uniforms.material.clipW);
    const previousClip: float4 = ProjectPoint(previous, uniforms.material.previousX, uniforms.material.previousY, uniforms.material.previousZ, uniforms.material.previousW);
    return { depth: currentClip.z / currentClip.w, color: surface.color, motion: TemporalProjection(currentClip, previousClip),
        normal: surface.normal, emission: surface.emission, subsurface: surface.subsurface };
}
