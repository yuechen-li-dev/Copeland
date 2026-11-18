import { Cross3, Dot3, Unit } from "./Lighting3D";

// The high bit records use of the two-proposal mixture. The remaining bits retain the
// hardware hit distance in centimetres and the two reserved sentinel values.
export const ReflectionGuideBit: u32 = 0x80000000;
export const ReflectionDistanceMask: u32 = 0x7fffffff;

export function GlossyCoverage(roughness: f32): f32 {
    return Clamp((0.48 - roughness) / 0.12, 0.0, 1.0);
}

export function GlossyIsSharp(roughness: f32): bool {
    return roughness <= 0.075;
}

// Heitz, Sampling the GGX Distribution of Visible Normals (JCGT 2018).
export function SampleVisibleGgx(normal: float3, view: float3, alpha: f32, sample: float2): float3 {
    var axis: float3 = float3(0.0, 0.0, 1.0);
    if (Abs(normal.z) > 0.9) { axis = float3(1.0, 0.0, 0.0); }
    let tangent: float3 = Unit(Cross3(axis, normal));
    let bitangent: float3 = Cross3(normal, tangent);
    let stretched: float3 = Unit(float3(Dot3(view, tangent) * alpha, Dot3(view, bitangent) * alpha, Dot3(view, normal)));
    let xySquared: f32 = stretched.x * stretched.x + stretched.y * stretched.y;
    var diskX: float3 = float3(1.0, 0.0, 0.0);
    if (xySquared > 0.000001) {
        diskX = float3(-stretched.y, stretched.x, 0.0) / Sqrt(xySquared);
    }
    let diskY: float3 = Cross3(stretched, diskX);
    let radius: f32 = Sqrt(sample.x);
    let phi: f32 = 6.28318530718 * sample.y;
    let x: f32 = radius * Cos(phi);
    let y: f32 = radius * Sin(phi);
    let blend: f32 = 0.5 * (1.0 + stretched.z);
    let warpedY: f32 = (1.0 - blend) * Sqrt(Max(0.0, 1.0 - x * x)) + blend * y;
    let projected: float3 = diskX * x + diskY * warpedY
        + stretched * Sqrt(Max(0.0, 1.0 - x * x - warpedY * warpedY));
    return Unit(tangent * (alpha * projected.x) + bitangent * (alpha * projected.y)
        + normal * Max(0.0, projected.z));
}

function SmithLambda(cosine: f32, alpha: f32): f32 {
    let squared: f32 = Max(cosine * cosine, 0.000001);
    return 0.5 * (Sqrt(1.0 + alpha * alpha * (1.0 - squared) / squared) - 1.0);
}

// f_s * N.L / pdf_L for visible-normal sampling is F * G2 / G1(V).
export function GgxVisibilityWeight(nv: f32, nl: f32, alpha: f32): f32 {
    let visible: f32 = 1.0 + SmithLambda(nv, alpha);
    return visible / (visible + SmithLambda(nl, alpha));
}

// Visible-normal PDF after the reflection Jacobian, in solid angle at the receiver.
export function VisibleGgxReflectionPdf(nv: f32, nh: f32, alpha: f32): f32 {
    let squared: f32 = alpha * alpha;
    let denominator: f32 = (1.0 - nh * nh) + nh * nh * squared;
    let distribution: f32 = squared / (3.14159265359 * denominator * denominator);
    return distribution / (4.0 * Max(nv, 0.000001) * (1.0 + SmithLambda(nv, alpha)));
}

export function SampleReflectionCone(axis: float3, cosine: f32, sample: float2): float3 {
    var up: float3 = float3(0.0, 0.0, 1.0);
    if (Abs(axis.z) > 0.9) { up = float3(1.0, 0.0, 0.0); }
    let tangent: float3 = Unit(Cross3(up, axis));
    let bitangent: float3 = Cross3(axis, tangent);
    let z: f32 = 1.0 - sample.x * (1.0 - cosine);
    let radius: f32 = Sqrt(Max(1.0 - z * z, 0.0));
    let angle: f32 = sample.y * 6.28318530718;
    return Unit(axis * z + tangent * (Cos(angle) * radius) + bitangent * (Sin(angle) * radius));
}
