import { AnalyticRay, AnalyticHit, IntersectAnalyticRange } from "./AnalyticIntersections3D";
import { Dot3 } from "./Lighting3D";

export record RayQueryCandidate {
    normal: float3;
    distance: f32;
    layer: u32;
    mask: u32;
}

// Sixteen words per primitive: three inverse affine rows, kind/layer/mask/reserved.
// The callback sees immutable ray values, never hardware traversal state.
export function AffineAnalyticIntersection(readonly primitives: StorageBuffer<f32>, primitive: u32,
    origin: float3, direction: float3, minimum: f32): RayQueryCandidate {
    let offset: u32 = primitive * 16;
    let inverseX: float3 = float3(primitives[offset], primitives[offset + 1], primitives[offset + 2]);
    let inverseY: float3 = float3(primitives[offset + 4], primitives[offset + 5], primitives[offset + 6]);
    let inverseZ: float3 = float3(primitives[offset + 8], primitives[offset + 9], primitives[offset + 10]);
    let localOrigin: float3 = float3(Dot3(inverseX, origin) + primitives[offset + 3],
        Dot3(inverseY, origin) + primitives[offset + 7], Dot3(inverseZ, origin) + primitives[offset + 11]);
    let localDirection: float3 = float3(Dot3(inverseX, direction), Dot3(inverseY, direction), Dot3(inverseZ, direction));
    let scale: f32 = Sqrt(Dot3(localDirection, localDirection));
    let ray: AnalyticRay = { origin: localOrigin, direction: localDirection / scale };
    let kind: u32 = Bitcast<u32>(primitives[offset + 12]);
    let layer: u32 = Bitcast<u32>(primitives[offset + 13]);
    let mask: u32 = Bitcast<u32>(primitives[offset + 14]);
    let hit: AnalyticHit = IntersectAnalyticRange(ray, kind, minimum * scale);
    if (hit.valid == 0) {
        return { normal: float3(0.0, 0.0, 0.0), distance: -1.0, layer: layer, mask: mask };
    }
    // Inverse transpose, including mirrored and sheared transforms. Unit-normal
    // clamping from lighting must not shorten normals on large affine shapes.
    let normal: float3 = float3(inverseX.x * hit.normal.x + inverseY.x * hit.normal.y + inverseZ.x * hit.normal.z,
        inverseX.y * hit.normal.x + inverseY.y * hit.normal.y + inverseZ.y * hit.normal.z,
        inverseX.z * hit.normal.x + inverseY.z * hit.normal.y + inverseZ.z * hit.normal.z);
    let unit: float3 = normal / Sqrt(Max(Dot3(normal, normal), 1.0e-30));
    return { normal: unit, distance: hit.distance / scale, layer: layer, mask: mask };
}
