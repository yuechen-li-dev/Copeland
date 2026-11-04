import { ResidentVertex, ResidentInstance, ResidentAttributes, TransformResidentVertex, Weighted2, Weighted3, Weighted4 } from "./ResidentGeometry3D";
import { AnalyticAttributes } from "./AnalyticGeometry3D";
import { AnalyticHit } from "./AnalyticIntersections3D";
import { Unit, Dot3 } from "./Lighting3D";

export function SecondaryAttributes(readonly hits: StorageBuffer<f32>, readonly procedurals: StorageBuffer<f32>, ray: u32,
    instance: ResidentInstance, readonly vertices: StorageBuffer<ResidentVertex>, readonly indices: StorageBuffer<u32>): ResidentAttributes {
    let hit: u32 = ray * 16;
    if (instance.primitiveKind != 0) {
        let position: float3 = float3(hits[hit + 8], hits[hit + 9], hits[hit + 10]);
        let outward: float3 = float3(hits[hit + 12], hits[hit + 13], hits[hit + 14]) * hits[hit + 15];
        let columnX: float3 = float3(instance.worldX.x, instance.worldY.x, instance.worldZ.x);
        let columnY: float3 = float3(instance.worldX.y, instance.worldY.y, instance.worldZ.y);
        let columnZ: float3 = float3(instance.worldX.z, instance.worldY.z, instance.worldZ.z);
        let normal: float3 = Unit(float3(Dot3(columnX, outward), Dot3(columnY, outward), Dot3(columnZ, outward)));
        let offset: u32 = U32(hits[hit + 3]) * 16;
        let local: float3 = float3(
            procedurals[offset] * position.x + procedurals[offset + 1] * position.y + procedurals[offset + 2] * position.z + procedurals[offset + 3],
            procedurals[offset + 4] * position.x + procedurals[offset + 5] * position.y + procedurals[offset + 6] * position.z + procedurals[offset + 7],
            procedurals[offset + 8] * position.x + procedurals[offset + 9] * position.y + procedurals[offset + 10] * position.z + procedurals[offset + 11]);
        let analytic: AnalyticHit = { position: local, normal: normal, distance: hits[hit + 4], valid: 1 };
        return AnalyticAttributes(analytic, instance);
    }
    let triangle: u32 = instance.indexBase + U32(hits[hit + 7]) * 3;
    let a: ResidentAttributes = TransformResidentVertex(vertices[instance.vertexBase + indices[triangle]], instance);
    let b: ResidentAttributes = TransformResidentVertex(vertices[instance.vertexBase + indices[triangle + 1]], instance);
    let c: ResidentAttributes = TransformResidentVertex(vertices[instance.vertexBase + indices[triangle + 2]], instance);
    let weights: float3 = float3(1.0 - hits[hit + 5] - hits[hit + 6], hits[hit + 5], hits[hit + 6]);
    return { world: Weighted3(a.world, b.world, c.world, weights), previous: Weighted3(a.previous, b.previous, c.previous, weights),
        normal: Weighted3(a.normal, b.normal, c.normal, weights), tangent: Weighted4(a.tangent, b.tangent, c.tangent, weights),
        color: Weighted4(a.color, b.color, c.color, weights), uv: Weighted2(a.uv, b.uv, c.uv, weights) };
}
