const Pi: f32 = static (3.14159265);
// Shared linear-space lighting for solid, skinned and textured surfaces.
export function Dot3(a: float3, b: float3): f32 {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}
export function Scale3(a: float3, s: f32): float3 {
    return float3(a.x * s, a.y * s, a.z * s);
}
export function Add3(a: float3, b: float3): float3 {
    return float3(a.x + b.x, a.y + b.y, a.z + b.z);
}
export function Sub3(a: float3, b: float3): float3 {
    return float3(a.x - b.x, a.y - b.y, a.z - b.z);
}
export function Mul3(a: float3, b: float3): float3 {
    return float3(a.x * b.x, a.y * b.y, a.z * b.z);
}
export function Unit(a: float3): float3 {
    return Scale3(a, 1.0 / Sqrt(Max(Dot3(a, a), 0.000001)));
}
export function Cross3(a: float3, b: float3): float3 {
    return float3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}
export function Fresnel(f0: f32, vh: f32): f32 {
    const x: f32 = 1.0 - vh;
    return f0 + (1.0 - f0) * x * x * x * x * x;
}
export function Geometry(n: f32, k: f32): f32 {
    return n / (n * (1.0 - k) + k);
}
export function Channel(base: f32, metallic: f32, vh: f32, specular: f32): f32 {
    const f0: f32 = 0.04 * (1.0 - metallic) + base * metallic;
    const f: f32 = Fresnel(f0, vh);
    return (1.0 - f) * (1.0 - metallic) * base / 3.14159265 + f * specular;
}
export function DirectLight(base: float3, normal: float3, view: float3, light: float3, metallic: f32, roughness: f32): float3 {
    const half: float3 = Unit(Add3(light, view));
    const nl: f32 = Max(Dot3(normal, light), 0.0);
    const nv: f32 = Max(Dot3(normal, view), 0.0001);
    const nh: f32 = Max(Dot3(normal, half), 0.0);
    const vh: f32 = Max(Dot3(view, half), 0.0);
    const a: f32 = roughness * roughness;
    const a2: f32 = a * a;
    const denominator: f32 = nh * nh * (a2 - 1.0) + 1.0;
    const distribution: f32 = a2 / Max(Pi * denominator * denominator, 0.000001);
    const k: f32 = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    const specular: f32 = distribution * Geometry(nv, k) * Geometry(nl, k) / Max(4.0 * nv * nl, 0.0001);
    return Scale3(float3(Channel(base.x, metallic, vh, specular), Channel(base.y, metallic, vh, specular),
        Channel(base.z, metallic, vh, specular)), nl);
}
export function HemisphereLight(base: float3, normal: float3, view: float3, sky: float3, ground: float3, metallic: f32, roughness: f32): float3 {
    const blend: f32 = Clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    const ambient: float3 = Add3(Scale3(sky, blend), Scale3(ground, 1.0 - blend));
    const diffuse: float3 = Scale3(Mul3(base, ambient), 1.0 - metallic);
    // A bounded procedural environment approximation. Textured, prefiltered IBL can replace this later.
    const nv: f32 = Max(Dot3(normal, view), 0.0);
    const reflected: float3 = Sub3(Scale3(normal, 2.0 * nv), view);
    const specularBlend: f32 = Clamp((reflected.y * (1.0 - roughness) + normal.y * roughness) * 0.5 + 0.5, 0.0, 1.0);
    const environment: float3 = Add3(Scale3(sky, specularBlend), Scale3(ground, 1.0 - specularBlend));
    const fresnel: float3 = float3(Fresnel(0.04 * (1.0 - metallic) + base.x * metallic, nv),
        Fresnel(0.04 * (1.0 - metallic) + base.y * metallic, nv), Fresnel(0.04 * (1.0 - metallic) + base.z * metallic, nv));
    return Add3(diffuse, Mul3(environment, fresnel));
}
export function ShadowTap(map: Texture2D<float4>, sampler: Sampler, uv: float2, depth: f32): f32 {
    const stored: float4 = Sample(map, sampler, uv);
    if (depth <= stored.x)
    {
        return 1.0;
    }
    return 0.0;
}
export function ShadowVisibility(map: Texture2D<float4>, sampler: Sampler, projected: float3, parameters: float4, nl: f32): f32 {
    if (parameters.x < 0.5 || projected.x < -1.0 || projected.x > 1.0 || projected.y < -1.0 || projected.y > 1.0
        || projected.z < 0.0 || projected.z > 1.0) {
        return 1.0;
    }
    const u: f32 = projected.x * 0.5 + 0.5;
    const v: f32 = projected.y * 0.5 + 0.5;
    const t: f32 = parameters.z;
    const z: f32 = projected.z - parameters.y * (1.0 + 2.0 * (1.0 - nl));
    return (ShadowTap(map, sampler, float2(u - t, v - t), z)
        + ShadowTap(map, sampler, float2(u, v - t), z)
        + ShadowTap(map, sampler, float2(u + t, v - t), z)
        + ShadowTap(map, sampler, float2(u - t, v), z)
        + ShadowTap(map, sampler, float2(u, v), z)
        + ShadowTap(map, sampler, float2(u + t, v), z)
        + ShadowTap(map, sampler, float2(u - t, v + t), z)
        + ShadowTap(map, sampler, float2(u, v + t), z)
        + ShadowTap(map, sampler, float2(u + t, v + t), z)) / 9.0;
}
