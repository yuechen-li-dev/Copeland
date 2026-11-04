import { ResidentVertex, ResidentInstance, ResidentAttributes, TransformResidentVertex, ProjectPoint,
    Weighted2, Weighted3, Weighted4, Cross2 } from "./ResidentGeometry3D";
export record VisibilityHit {
    attributes: ResidentAttributes;
    uvDx: float2;
    uvDy: float2;
    valid: u32;
}
export function EmptyVisibilityHit(): VisibilityHit {
    return { attributes: { world: float3(0.0, 0.0, 0.0), previous: float3(0.0, 0.0, 0.0),
        normal: float3(0.0, 0.0, 1.0), tangent: float4(1.0, 0.0, 0.0, 1.0),
        color: float4(1.0, 1.0, 1.0, 1.0), uv: float2(0.0, 0.0) },
        uvDx: float2(0.0, 0.0), uvDy: float2(0.0, 0.0), valid: 0 };
}
export function ResolveTriangleHit(hit: uint2, pixel: float2, viewport: float2, instance: ResidentInstance,
    clipX: float4, clipY: float4, clipZ: float4, clipW: float4,
    vertices: StorageBuffer<ResidentVertex>, indices: StorageBuffer<u32>): VisibilityHit {
    let triangle: u32 = instance.indexBase + (hit.y / 2) * 3;
    let a: ResidentAttributes = TransformResidentVertex(vertices[instance.vertexBase + indices[triangle]], instance);
    let b: ResidentAttributes = TransformResidentVertex(vertices[instance.vertexBase + indices[triangle + 1]], instance);
    let c: ResidentAttributes = TransformResidentVertex(vertices[instance.vertexBase + indices[triangle + 2]], instance);
    let ca: float4 = ProjectPoint(a.world, clipX, clipY, clipZ, clipW);
    let cb: float4 = ProjectPoint(b.world, clipX, clipY, clipZ, clipW);
    let cc: float4 = ProjectPoint(c.world, clipX, clipY, clipZ, clipW);
    // Homogeneous barycentrics avoid division by a vertex w at near-plane crossings.
    let px: f32 = pixel.x * 2.0 / viewport.x - 1.0;
    let py: f32 = pixel.y * 2.0 / viewport.y - 1.0;
    let aa: float2 = float2(ca.x - px * ca.w, ca.y - py * ca.w);
    let bb: float2 = float2(cb.x - px * cb.w, cb.y - py * cb.w);
    let ccv: float2 = float2(cc.x - px * cc.w, cc.y - py * cc.w);
    let ra: f32 = Cross2(bb, ccv);
    let rb: f32 = Cross2(ccv, aa);
    let rc: f32 = Cross2(aa, bb);
    let sum: f32 = ra + rb + rc;
    if (sum == 0.0) { return EmptyVisibilityHit(); }
    let weights: float3 = float3(ra / sum, rb / sum, rc / sum);
    let dx: f32 = 2.0 / viewport.x;
    let dy: f32 = 2.0 / viewport.y;
    let drax: f32 = dx * (cc.w * bb.y - cb.w * ccv.y);
    let drbx: f32 = dx * (ca.w * ccv.y - cc.w * aa.y);
    let drcx: f32 = dx * (cb.w * aa.y - ca.w * bb.y);
    let dray: f32 = dy * (cb.w * ccv.x - cc.w * bb.x);
    let drby: f32 = dy * (cc.w * aa.x - ca.w * ccv.x);
    let drcy: f32 = dy * (ca.w * bb.x - cb.w * aa.x);
    let uv: float2 = Weighted2(a.uv, b.uv, c.uv, weights);
    let dsumx: f32 = drax + drbx + drcx;
    let dsumy: f32 = dray + drby + drcy;
    let uvx: float2 = float2((a.uv.x * drax + b.uv.x * drbx + c.uv.x * drcx - uv.x * dsumx) / sum,
        (a.uv.y * drax + b.uv.y * drbx + c.uv.y * drcx - uv.y * dsumx) / sum);
    let uvy: float2 = float2((a.uv.x * dray + b.uv.x * drby + c.uv.x * drcy - uv.x * dsumy) / sum,
        (a.uv.y * dray + b.uv.y * drby + c.uv.y * drcy - uv.y * dsumy) / sum);
    // Reconstruct one complete quad from this triangle, including helper lanes across ID boundaries.
    // Finite pixel differences match raster texture footprints more closely than infinitesimal derivatives.
    let qx: f32 = Floor(pixel.x * 0.5) * 2.0 + 0.5 - pixel.x;
    let qy: f32 = Floor(pixel.y * 0.5) * 2.0 + 0.5 - pixel.y;
    let qa: f32 = ra + qx * drax + qy * dray;
    let qb: f32 = rb + qx * drbx + qy * drby;
    let qc: f32 = rc + qx * drcx + qy * drcy;
    let qs: f32 = qa + qb + qc;
    let qsx: f32 = qs + dsumx;
    let qsy: f32 = qs + dsumy;
    var sampleDx: float2 = uvx;
    var sampleDy: float2 = uvy;
    if (qs != 0.0 && qsx != 0.0 && qsy != 0.0) {
        let origin: float2 = Weighted2(a.uv, b.uv, c.uv, float3(qa / qs, qb / qs, qc / qs));
        let right: float2 = Weighted2(a.uv, b.uv, c.uv, float3((qa + drax) / qsx, (qb + drbx) / qsx, (qc + drcx) / qsx));
        let down: float2 = Weighted2(a.uv, b.uv, c.uv, float3((qa + dray) / qsy, (qb + drby) / qsy, (qc + drcy) / qsy));
        sampleDx = float2(right.x - origin.x, right.y - origin.y);
        sampleDy = float2(down.x - origin.x, down.y - origin.y);
    }
    return { attributes: { world: Weighted3(a.world, b.world, c.world, weights),
        previous: Weighted3(a.previous, b.previous, c.previous, weights), normal: Weighted3(a.normal, b.normal, c.normal, weights),
        tangent: Weighted4(a.tangent, b.tangent, c.tangent, weights), color: Weighted4(a.color, b.color, c.color, weights), uv: uv },
        uvDx: sampleDx, uvDy: sampleDy, valid: 1 };
}
