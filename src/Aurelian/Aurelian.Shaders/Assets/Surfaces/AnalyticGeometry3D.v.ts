import { Unit, Dot3, Cross3, Add3, Sub3, Scale3 } from "./Lighting3D";
import { ResidentInstance, ResidentAttributes, PointRow, DirectionRow } from "./ResidentGeometry3D";

import { AnalyticRay, AnalyticHit, IntersectAnalytic } from "./AnalyticIntersections3D";

function AnalyticUnit(value: float3): float3 {
    return Scale3(value, 1.0 / Sqrt(Max(Dot3(value, value), 1.0e-30)));
}

function InverseDirection(instance: ResidentInstance, direction: float3): float3 {
    return float3(instance.normalX.x * direction.x + instance.normalY.x * direction.y + instance.normalZ.x * direction.z,
        instance.normalX.y * direction.x + instance.normalY.y * direction.y + instance.normalZ.y * direction.z,
        instance.normalX.z * direction.x + instance.normalY.z * direction.y + instance.normalZ.z * direction.z);
}

// Intersect the two pixel planes with clip Z=0. No camera inverse or perspective-only eye ray assumption.
export function AnalyticLocalRay(pixel: float2, viewport: float2, instance: ResidentInstance,
    clipX: float4, clipY: float4, clipZ: float4, clipW: float4): AnalyticRay {
    let x: f32 = pixel.x * 2.0 / viewport.x - 1.0;
    let y: f32 = pixel.y * 2.0 / viewport.y - 1.0;
    let a: float3 = float3(clipX.x - x * clipW.x, clipX.y - x * clipW.y, clipX.z - x * clipW.z);
    let b: float3 = float3(clipY.x - y * clipW.x, clipY.y - y * clipW.y, clipY.z - y * clipW.z);
    let c: float3 = float3(clipZ.x, clipZ.y, clipZ.z);
    let direction: float3 = Cross3(a, b);
    let determinant: f32 = Dot3(c, direction);
    let origin: float3 = Scale3(Add3(Add3(Scale3(Cross3(b, c), -clipX.w + x * clipW.w),
        Scale3(Cross3(c, a), -clipY.w + y * clipW.w)), Scale3(direction, -clipZ.w)), 1.0 / determinant);
    let translation: float3 = float3(instance.worldX.w, instance.worldY.w, instance.worldZ.w);
    return { origin: InverseDirection(instance, Sub3(origin, translation)),
        direction: AnalyticUnit(InverseDirection(instance, Scale3(direction, 1.0 / determinant))) };
}

export function AnalyticUv(position: float3, normal: float3, kind: u32): float2 {
    var uv: float2 = float2(position.x, position.y);
    if (kind == 2 && Abs(normal.x) > 0.5) { uv = float2(-position.z, position.y); }
    if (kind == 2 && Abs(normal.y) > 0.5) { uv = float2(position.x, -position.z); }
    return float2(uv.x * 0.5 + 0.5, uv.y * 0.5 + 0.5);
}

function SampleAnalyticUv(ray: AnalyticRay, current: AnalyticHit, kind: u32): float2 {
    if (kind != 2) {
        let hit: AnalyticHit = IntersectAnalytic(ray, kind);
        if (hit.valid != 0) { return AnalyticUv(hit.position, current.normal, kind); }
    }
    let denominator: f32 = Dot3(current.normal, ray.direction);
    if (Abs(denominator) < 1.0e-10) { return AnalyticUv(current.position, current.normal, kind); }
    let distance: f32 = Dot3(Sub3(current.position, ray.origin), current.normal) / denominator;
    return AnalyticUv(Add3(ray.origin, Scale3(ray.direction, distance)), current.normal, kind);
}

export function AnalyticGradients(pixel: float2, viewport: float2, current: AnalyticHit, instance: ResidentInstance,
    clipX: float4, clipY: float4, clipZ: float4, clipW: float4): float4 {
    let origin: float2 = float2(Floor(pixel.x * 0.5) * 2.0 + 0.5, Floor(pixel.y * 0.5) * 2.0 + 0.5);
    let a: float2 = SampleAnalyticUv(AnalyticLocalRay(origin, viewport, instance, clipX, clipY, clipZ, clipW), current, instance.primitiveKind);
    let b: float2 = SampleAnalyticUv(AnalyticLocalRay(float2(origin.x + 1.0, origin.y), viewport, instance, clipX, clipY, clipZ, clipW), current, instance.primitiveKind);
    let c: float2 = SampleAnalyticUv(AnalyticLocalRay(float2(origin.x, origin.y + 1.0), viewport, instance, clipX, clipY, clipZ, clipW), current, instance.primitiveKind);
    return float4(b.x - a.x, b.y - a.y, c.x - a.x, c.y - a.y);
}

export function AnalyticAttributes(hit: AnalyticHit, instance: ResidentInstance): ResidentAttributes {
    let n: float3 = hit.normal;
    var tangent: float3 = Cross3(float3(0.0, 1.0, 0.0), n);
    var sign: f32 = 1.0;
    if (n.z < 0.0) { tangent = Scale3(tangent, -1.0); sign = -1.0; }
    if (instance.primitiveKind == 2) {
        tangent = float3(1.0, 0.0, 0.0);
        if (Abs(n.x) > 0.5) { tangent = float3(0.0, 0.0, -1.0); sign = n.x; }
        if (Abs(n.y) > 0.5) { sign = n.y; }
    }
    if (Dot3(tangent, tangent) < 1.0e-12) { tangent = float3(1.0, 0.0, 0.0); }
    let worldNormal: float3 = AnalyticUnit(float3(DirectionRow(instance.normalX, n), DirectionRow(instance.normalY, n), DirectionRow(instance.normalZ, n)));
    let worldTangent: float3 = float3(DirectionRow(instance.worldX, tangent), DirectionRow(instance.worldY, tangent), DirectionRow(instance.worldZ, tangent));
    let projectedTangent: float3 = AnalyticUnit(Sub3(worldTangent, Scale3(worldNormal, Dot3(worldNormal, worldTangent))));
    let columnX: float3 = float3(instance.worldX.x, instance.worldY.x, instance.worldZ.x);
    let columnY: float3 = float3(instance.worldX.y, instance.worldY.y, instance.worldZ.y);
    let columnZ: float3 = float3(instance.worldX.z, instance.worldY.z, instance.worldZ.z);
    if (Dot3(Cross3(columnX, columnY), columnZ) < 0.0) { sign = -sign; }
    return { world: float3(PointRow(instance.worldX, hit.position), PointRow(instance.worldY, hit.position), PointRow(instance.worldZ, hit.position)),
        previous: float3(PointRow(instance.previousX, hit.position), PointRow(instance.previousY, hit.position), PointRow(instance.previousZ, hit.position)),
        normal: worldNormal, tangent: float4(projectedTangent.x, projectedTangent.y, projectedTangent.z, sign),
        color: float4(1.0, 1.0, 1.0, 1.0), uv: AnalyticUv(hit.position, n, instance.primitiveKind) };
}
