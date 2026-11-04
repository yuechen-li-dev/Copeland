import { Unit, Dot3, Cross3, Add3, Sub3, Scale3 } from "./Lighting3D";
import { ResidentInstance, ResidentAttributes, PointRow, DirectionRow } from "./ResidentGeometry3D";

export record AnalyticRay { origin: float3; direction: float3; }
export record AnalyticHit { position: float3; normal: float3; distance: f32; valid: u32; }
record AnalyticSlab { near: f32; far: f32; nearNormal: float3; farNormal: float3; valid: u32; }

export function EmptyAnalyticHit(): AnalyticHit {
    return { position: float3(0.0, 0.0, 0.0), normal: float3(0.0, 0.0, 1.0), distance: 1.0e30, valid: 0 };
}

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
    const x: f32 = pixel.x * 2.0 / viewport.x - 1.0;
    const y: f32 = pixel.y * 2.0 / viewport.y - 1.0;
    const a: float3 = float3(clipX.x - x * clipW.x, clipX.y - x * clipW.y, clipX.z - x * clipW.z);
    const b: float3 = float3(clipY.x - y * clipW.x, clipY.y - y * clipW.y, clipY.z - y * clipW.z);
    const c: float3 = float3(clipZ.x, clipZ.y, clipZ.z);
    const direction: float3 = Cross3(a, b);
    const determinant: f32 = Dot3(c, direction);
    const origin: float3 = Scale3(Add3(Add3(Scale3(Cross3(b, c), -clipX.w + x * clipW.w),
        Scale3(Cross3(c, a), -clipY.w + y * clipW.w)), Scale3(direction, -clipZ.w)), 1.0 / determinant);
    const translation: float3 = float3(instance.worldX.w, instance.worldY.w, instance.worldZ.w);
    return { origin: InverseDirection(instance, Sub3(origin, translation)),
        direction: AnalyticUnit(InverseDirection(instance, Scale3(direction, 1.0 / determinant))) };
}

function SphereHit(ray: AnalyticRay, centerY: f32, radius: f32, hemisphere: u32): AnalyticHit {
    const relative: float3 = Sub3(ray.origin, float3(0.0, centerY, 0.0));
    const middle: f32 = -Dot3(relative, ray.direction);
    const closest: float3 = Add3(relative, Scale3(ray.direction, middle));
    const squared: f32 = radius * radius - Dot3(closest, closest);
    var result: AnalyticHit = EmptyAnalyticHit();
    if (squared >= 0.0) {
        const delta: f32 = Sqrt(squared);
        static for (let root of Indices<2>()) {
            var offset: f32 = -delta;
            if (root == 1) { offset = delta; }
            const distance: f32 = middle + offset;
            const relativeHit: float3 = Add3(closest, Scale3(ray.direction, offset));
            var allowed: bool = distance >= 0.0;
            if (hemisphere == 1) { allowed = allowed && relativeHit.y <= 0.0; }
            if (hemisphere == 2) { allowed = allowed && relativeHit.y >= 0.0; }
            if (allowed && distance < result.distance) {
                result = { position: Add3(relativeHit, float3(0.0, centerY, 0.0)),
                    normal: Unit(relativeHit), distance: distance, valid: 1 };
            }
        }
    }
    return result;
}

function Slab(origin: f32, direction: f32, axis: float3): AnalyticSlab {
    if (Abs(direction) < 1.0e-20) {
        var valid: u32 = 1;
        if (Abs(origin) > 1.0) { valid = 0; }
        return { near: -1.0e30, far: 1.0e30, nearNormal: axis, farNormal: axis, valid: valid };
    }
    const first: f32 = (-1.0 - origin) / direction;
    const second: f32 = (1.0 - origin) / direction;
    var sign: f32 = -1.0;
    if (direction < 0.0) { sign = 1.0; }
    return { near: Min(first, second), far: Max(first, second), nearNormal: Scale3(axis, sign),
        farNormal: Scale3(axis, -sign), valid: 1 };
}

function BoxHit(ray: AnalyticRay): AnalyticHit {
    const x: AnalyticSlab = Slab(ray.origin.x, ray.direction.x, float3(1.0, 0.0, 0.0));
    const y: AnalyticSlab = Slab(ray.origin.y, ray.direction.y, float3(0.0, 1.0, 0.0));
    const z: AnalyticSlab = Slab(ray.origin.z, ray.direction.z, float3(0.0, 0.0, 1.0));
    var near: f32 = x.near;
    var nearNormal: float3 = x.nearNormal;
    if (y.near > near) { near = y.near; nearNormal = y.nearNormal; }
    if (z.near > near) { near = z.near; nearNormal = z.nearNormal; }
    var far: f32 = x.far;
    var farNormal: float3 = x.farNormal;
    if (y.far < far) { far = y.far; farNormal = y.farNormal; }
    if (z.far < far) { far = z.far; farNormal = z.farNormal; }
    if (x.valid == 0 || y.valid == 0 || z.valid == 0 || far < Max(near, 0.0)) { return EmptyAnalyticHit(); }
    var distance: f32 = near;
    var normal: float3 = nearNormal;
    if (near < 0.0) { distance = far; normal = farNormal; }
    return { position: Add3(ray.origin, Scale3(ray.direction, distance)), normal: normal, distance: distance, valid: 1 };
}

export function IntersectAnalytic(ray: AnalyticRay, kind: u32): AnalyticHit {
    if (kind == 1) { return SphereHit(ray, 0.0, 1.0, 0); }
    if (kind == 2) { return BoxHit(ray); }
    if (kind != 3) { return EmptyAnalyticHit(); }
    var result: AnalyticHit = SphereHit(ray, -0.5, 0.5, 1);
    const upper: AnalyticHit = SphereHit(ray, 0.5, 0.5, 2);
    if (upper.valid != 0 && upper.distance < result.distance) { result = upper; }
    const a: f32 = ray.direction.x * ray.direction.x + ray.direction.z * ray.direction.z;
    const b: f32 = ray.origin.x * ray.direction.x + ray.origin.z * ray.direction.z;
    if (a > 1.0e-20) {
        const middle: f32 = -b / a;
        const closestX: f32 = ray.origin.x + ray.direction.x * middle;
        const closestZ: f32 = ray.origin.z + ray.direction.z * middle;
        const squared: f32 = 0.25 - closestX * closestX - closestZ * closestZ;
        if (squared >= 0.0) {
            static for (let root of Indices<2>()) {
                var sign: f32 = -1.0;
                if (root == 1) { sign = 1.0; }
                const offset: f32 = sign * Sqrt(squared / a);
                const distance: f32 = middle + offset;
                const position: float3 = float3(closestX + ray.direction.x * offset, ray.origin.y + ray.direction.y * distance,
                    closestZ + ray.direction.z * offset);
                if (distance >= 0.0 && distance < result.distance && Abs(position.y) <= 0.5) {
                    result = { position: position, normal: Unit(float3(position.x, 0.0, position.z)), distance: distance, valid: 1 };
                }
            }
        }
    }
    return result;
}

export function AnalyticUv(position: float3, normal: float3, kind: u32): float2 {
    var uv: float2 = float2(position.x, position.y);
    if (kind == 2 && Abs(normal.x) > 0.5) { uv = float2(-position.z, position.y); }
    if (kind == 2 && Abs(normal.y) > 0.5) { uv = float2(position.x, -position.z); }
    return float2(uv.x * 0.5 + 0.5, uv.y * 0.5 + 0.5);
}

function SampleAnalyticUv(ray: AnalyticRay, current: AnalyticHit, kind: u32): float2 {
    if (kind != 2) {
        const hit: AnalyticHit = IntersectAnalytic(ray, kind);
        if (hit.valid != 0) { return AnalyticUv(hit.position, current.normal, kind); }
    }
    const denominator: f32 = Dot3(current.normal, ray.direction);
    if (Abs(denominator) < 1.0e-10) { return AnalyticUv(current.position, current.normal, kind); }
    const distance: f32 = Dot3(Sub3(current.position, ray.origin), current.normal) / denominator;
    return AnalyticUv(Add3(ray.origin, Scale3(ray.direction, distance)), current.normal, kind);
}

export function AnalyticGradients(pixel: float2, viewport: float2, current: AnalyticHit, instance: ResidentInstance,
    clipX: float4, clipY: float4, clipZ: float4, clipW: float4): float4 {
    const origin: float2 = float2(Floor(pixel.x * 0.5) * 2.0 + 0.5, Floor(pixel.y * 0.5) * 2.0 + 0.5);
    const a: float2 = SampleAnalyticUv(AnalyticLocalRay(origin, viewport, instance, clipX, clipY, clipZ, clipW), current, instance.primitiveKind);
    const b: float2 = SampleAnalyticUv(AnalyticLocalRay(float2(origin.x + 1.0, origin.y), viewport, instance, clipX, clipY, clipZ, clipW), current, instance.primitiveKind);
    const c: float2 = SampleAnalyticUv(AnalyticLocalRay(float2(origin.x, origin.y + 1.0), viewport, instance, clipX, clipY, clipZ, clipW), current, instance.primitiveKind);
    return float4(b.x - a.x, b.y - a.y, c.x - a.x, c.y - a.y);
}

export function AnalyticAttributes(hit: AnalyticHit, instance: ResidentInstance): ResidentAttributes {
    const n: float3 = hit.normal;
    var tangent: float3 = Cross3(float3(0.0, 1.0, 0.0), n);
    var sign: f32 = 1.0;
    if (n.z < 0.0) { tangent = Scale3(tangent, -1.0); sign = -1.0; }
    if (instance.primitiveKind == 2) {
        tangent = float3(1.0, 0.0, 0.0);
        if (Abs(n.x) > 0.5) { tangent = float3(0.0, 0.0, -1.0); sign = n.x; }
        if (Abs(n.y) > 0.5) { sign = n.y; }
    }
    if (Dot3(tangent, tangent) < 1.0e-12) { tangent = float3(1.0, 0.0, 0.0); }
    const worldNormal: float3 = AnalyticUnit(float3(DirectionRow(instance.normalX, n), DirectionRow(instance.normalY, n), DirectionRow(instance.normalZ, n)));
    const worldTangent: float3 = float3(DirectionRow(instance.worldX, tangent), DirectionRow(instance.worldY, tangent), DirectionRow(instance.worldZ, tangent));
    const projectedTangent: float3 = AnalyticUnit(Sub3(worldTangent, Scale3(worldNormal, Dot3(worldNormal, worldTangent))));
    return { world: float3(PointRow(instance.worldX, hit.position), PointRow(instance.worldY, hit.position), PointRow(instance.worldZ, hit.position)),
        previous: float3(PointRow(instance.previousX, hit.position), PointRow(instance.previousY, hit.position), PointRow(instance.previousZ, hit.position)),
        normal: worldNormal, tangent: float4(projectedTangent.x, projectedTangent.y, projectedTangent.z, sign),
        color: float4(1.0, 1.0, 1.0, 1.0), uv: AnalyticUv(hit.position, n, instance.primitiveKind) };
}
