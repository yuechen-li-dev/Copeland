import { ResidentVertex, ResidentInstance, PointRow, DirectionRow } from "./ResidentGeometry3D";
import { Cross3, Dot3 } from "./Lighting3D";

export const ReflectionRayRange: f32 = 10000.0;

export function ScreenPixel(clip: float4, viewport: float2): uint2 {
    if (clip.w <= 0.00001 || clip.z < 0.0 || clip.z >= clip.w
        || Abs(clip.x) >= clip.w || Abs(clip.y) >= clip.w) {
        return uint2(0xffffffff, 0xffffffff);
    }
    let uv: float2 = float2(clip.x, clip.y) * (0.5 / clip.w) + float2(0.5, 0.5);
    return uint2(U32(Min(uv.x * viewport.x, viewport.x - 1.0)), U32(Min(uv.y * viewport.y, viewport.y - 1.0)));
}

function WorldPoint(instance: ResidentInstance, local: float3): float3 {
    return float3(PointRow(instance.worldX, local), PointRow(instance.worldY, local), PointRow(instance.worldZ, local));
}

export function ScreenTriangleLimit(candidate: uint2, origin: float3, direction: float3, instance: ResidentInstance,
    readonly vertices: StorageBuffer<ResidentVertex>, readonly indices: StorageBuffer<u32>): f32 {
    if (instance.primitiveKind != 0) { return ReflectionRayRange; }
    let primitive: u32 = candidate.y / 2;
    let address: u32 = instance.indexBase + primitive * 3;
    let a: float3 = vertices[instance.vertexBase + indices[address]].position;
    let ab: float3 = vertices[instance.vertexBase + indices[address + 1]].position - a;
    let ac: float3 = vertices[instance.vertexBase + indices[address + 2]].position - a;
    let worldAb: float3 = float3(DirectionRow(instance.worldX, ab), DirectionRow(instance.worldY, ab), DirectionRow(instance.worldZ, ab));
    let worldAc: float3 = float3(DirectionRow(instance.worldX, ac), DirectionRow(instance.worldY, ac), DirectionRow(instance.worldZ, ac));
    let p: float3 = Cross3(direction, worldAc);
    let determinant: f32 = Dot3(worldAb, p);
    if (Abs(determinant) <= 0.000001) { return ReflectionRayRange; }
    let relative: float3 = origin - WorldPoint(instance, a);
    let q: float3 = Cross3(relative, worldAb);
    let u: f32 = Dot3(relative, p) / determinant;
    let v: f32 = Dot3(direction, q) / determinant;
    let distance: f32 = Dot3(worldAc, q) / determinant;
    if (u <= 0.0001 || v <= 0.0001 || u + v >= 0.9999 || distance <= 0.002 || distance >= 16.0) {
        return ReflectionRayRange;
    }
    // Leave room for intersection roundoff and the retained ray tie rule.
    return distance + 0.002;
}
