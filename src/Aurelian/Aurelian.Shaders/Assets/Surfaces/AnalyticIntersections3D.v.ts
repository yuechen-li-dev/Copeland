import { Unit, Dot3, Add3, Sub3, Scale3 } from "./Lighting3D";

// Canonical unit sphere, [-1,1] box and radius-.5, segment-Y[-.5,.5] capsule.
// Directions are unit length; distances and the minimum use the same local metric.
export record AnalyticRay { origin: float3; direction: float3; }
export record AnalyticHit { position: float3; normal: float3; distance: f32; valid: u32; }
record AnalyticSlab { near: f32; far: f32; nearNormal: float3; farNormal: float3; valid: u32; }

export function EmptyAnalyticHit(): AnalyticHit {
    return { position: float3(0.0, 0.0, 0.0), normal: float3(0.0, 0.0, 1.0), distance: 1.0e30, valid: 0 };
}

function SphereHit(ray: AnalyticRay, centerY: f32, radius: f32, hemisphere: u32, minimum: f32): AnalyticHit {
    let relative: float3 = Sub3(ray.origin, float3(0.0, centerY, 0.0));
    let middle: f32 = -Dot3(relative, ray.direction);
    let closest: float3 = Add3(relative, Scale3(ray.direction, middle));
    let squared: f32 = radius * radius - Dot3(closest, closest);
    var result: AnalyticHit = EmptyAnalyticHit();
    if (squared >= 0.0) {
        let delta: f32 = Sqrt(squared);
        static for (let root of Indices<2>()) {
            var offset: f32 = -delta;
            if (root == 1) { offset = delta; }
            let distance: f32 = middle + offset;
            let relativeHit: float3 = Add3(closest, Scale3(ray.direction, offset));
            var allowed: bool = distance >= minimum;
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
    let first: f32 = (-1.0 - origin) / direction;
    let second: f32 = (1.0 - origin) / direction;
    var sign: f32 = -1.0;
    if (direction < 0.0) { sign = 1.0; }
    return { near: Min(first, second), far: Max(first, second), nearNormal: Scale3(axis, sign),
        farNormal: Scale3(axis, -sign), valid: 1 };
}

function BoxHit(ray: AnalyticRay, minimum: f32): AnalyticHit {
    let x: AnalyticSlab = Slab(ray.origin.x, ray.direction.x, float3(1.0, 0.0, 0.0));
    let y: AnalyticSlab = Slab(ray.origin.y, ray.direction.y, float3(0.0, 1.0, 0.0));
    let z: AnalyticSlab = Slab(ray.origin.z, ray.direction.z, float3(0.0, 0.0, 1.0));
    var near: f32 = x.near;
    var nearNormal: float3 = x.nearNormal;
    if (y.near > near) { near = y.near; nearNormal = y.nearNormal; }
    if (z.near > near) { near = z.near; nearNormal = z.nearNormal; }
    var far: f32 = x.far;
    var farNormal: float3 = x.farNormal;
    if (y.far < far) { far = y.far; farNormal = y.farNormal; }
    if (z.far < far) { far = z.far; farNormal = z.farNormal; }
    if (x.valid == 0 || y.valid == 0 || z.valid == 0 || far < Max(near, minimum)) { return EmptyAnalyticHit(); }
    var distance: f32 = near;
    var normal: float3 = nearNormal;
    if (near < minimum) { distance = far; normal = farNormal; }
    return { position: Add3(ray.origin, Scale3(ray.direction, distance)), normal: normal, distance: distance, valid: 1 };
}

export function IntersectAnalyticRange(ray: AnalyticRay, kind: u32, minimum: f32): AnalyticHit {
    if (kind == 1) { return SphereHit(ray, 0.0, 1.0, 0, minimum); }
    if (kind == 2) { return BoxHit(ray, minimum); }
    if (kind != 3) { return EmptyAnalyticHit(); }
    var result: AnalyticHit = SphereHit(ray, -0.5, 0.5, 1, minimum);
    let upper: AnalyticHit = SphereHit(ray, 0.5, 0.5, 2, minimum);
    if (upper.valid != 0 && upper.distance < result.distance) { result = upper; }
    let a: f32 = ray.direction.x * ray.direction.x + ray.direction.z * ray.direction.z;
    let b: f32 = ray.origin.x * ray.direction.x + ray.origin.z * ray.direction.z;
    if (a > 1.0e-20) {
        let middle: f32 = -b / a;
        let closestX: f32 = ray.origin.x + ray.direction.x * middle;
        let closestZ: f32 = ray.origin.z + ray.direction.z * middle;
        let squared: f32 = 0.25 - closestX * closestX - closestZ * closestZ;
        if (squared >= 0.0) {
            static for (let root of Indices<2>()) {
                var sign: f32 = -1.0;
                if (root == 1) { sign = 1.0; }
                let offset: f32 = sign * Sqrt(squared / a);
                let distance: f32 = middle + offset;
                let position: float3 = float3(closestX + ray.direction.x * offset, ray.origin.y + ray.direction.y * distance,
                    closestZ + ray.direction.z * offset);
                if (distance >= minimum && distance < result.distance && Abs(position.y) <= 0.5) {
                    result = { position: position, normal: Unit(float3(position.x, 0.0, position.z)), distance: distance, valid: 1 };
                }
            }
        }
    }
    return result;
}

export function IntersectAnalytic(ray: AnalyticRay, kind: u32): AnalyticHit {
    return IntersectAnalyticRange(ray, kind, 0.0);
}
