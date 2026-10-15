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
export function ChannelResponse(base: f32, metallic: f32, vh: f32, specular: f32, dielectric: f32): f32 {
    if (dielectric == 0.0 && metallic == 0.0) {
        return base / 3.14159265;
    }
    const f0: f32 = dielectric * (1.0 - metallic) + base * metallic;
    const f: f32 = Fresnel(f0, vh);
    return (1.0 - f) * (1.0 - metallic) * base / 3.14159265 + f * specular;
}
export function Channel(base: f32, metallic: f32, vh: f32, specular: f32): f32 {
    return ChannelResponse(base, metallic, vh, specular, 0.04);
}
export function DirectLightResponse(base: float3, normal: float3, view: float3, light: float3, metallic: f32, roughness: f32, dielectric: f32): float3 {
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
    return Scale3(float3(ChannelResponse(base.x, metallic, vh, specular, dielectric), ChannelResponse(base.y, metallic, vh, specular, dielectric),
        ChannelResponse(base.z, metallic, vh, specular, dielectric)), nl);
}
export function DirectLight(base: float3, normal: float3, view: float3, light: float3, metallic: f32, roughness: f32): float3 {
    return DirectLightResponse(base, normal, view, light, metallic, roughness, 0.04);
}
// The diffuse term is kept separate so subsurface diffusion cannot blur specular highlights.
export function DirectDiffuse(base: float3, normal: float3, view: float3, light: float3, metallic: f32): float3 {
    const half: float3 = Unit(Add3(light, view));
    const vh: f32 = Max(Dot3(view, half), 0.0);
    const nl: f32 = Max(Dot3(normal, light), 0.0);
    return Scale3(float3(Channel(base.x, metallic, vh, 0.0), Channel(base.y, metallic, vh, 0.0),
        Channel(base.z, metallic, vh, 0.0)), nl);
}
export function HemisphereResponse(base: float3, normal: float3, view: float3, sky: float3, ground: float3, metallic: f32, roughness: f32, dielectric: f32): float3 {
    const blend: f32 = Clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    const ambient: float3 = Add3(Scale3(sky, blend), Scale3(ground, 1.0 - blend));
    const diffuse: float3 = Scale3(Mul3(base, ambient), 1.0 - metallic);
    if (dielectric == 0.0 && metallic == 0.0) {
        return diffuse;
    }
    // A bounded procedural environment approximation. Textured, prefiltered IBL can replace this later.
    const nv: f32 = Max(Dot3(normal, view), 0.0);
    const reflected: float3 = Sub3(Scale3(normal, 2.0 * nv), view);
    const specularBlend: f32 = Clamp((reflected.y * (1.0 - roughness) + normal.y * roughness) * 0.5 + 0.5, 0.0, 1.0);
    const environment: float3 = Add3(Scale3(sky, specularBlend), Scale3(ground, 1.0 - specularBlend));
    const fresnel: float3 = float3(Fresnel(dielectric * (1.0 - metallic) + base.x * metallic, nv),
        Fresnel(dielectric * (1.0 - metallic) + base.y * metallic, nv), Fresnel(dielectric * (1.0 - metallic) + base.z * metallic, nv));
    return Add3(diffuse, Mul3(environment, fresnel));
}
export function HemisphereLight(base: float3, normal: float3, view: float3, sky: float3, ground: float3, metallic: f32, roughness: f32): float3 {
    return HemisphereResponse(base, normal, view, sky, ground, metallic, roughness, 0.04);
}
export function ShadowTap(map: Texture2D<float4>, sampler: Sampler, uv: float2, depth: f32): f32 {
    const stored: float4 = Sample(map, sampler, uv);
    if (depth <= stored.x)
    {
        return 1.0;
    }
    return 0.0;
}
// One row of the bilinear-weighted 3x3 PCF footprint: four texel centres with weights (1 - f, 1, 1, f).
function ShadowRow(map: Texture2D<float4>, sampler: Sampler, row: f32, across: f32, fraction: f32,
    texel: f32, depth: f32): f32 {
    const v: f32 = (row + 0.5) * texel;
    return ShadowTap(map, sampler, float2((across - 0.5) * texel, v), depth) * (1.0 - fraction)
        + ShadowTap(map, sampler, float2((across + 0.5) * texel, v), depth)
        + ShadowTap(map, sampler, float2((across + 1.5) * texel, v), depth)
        + ShadowTap(map, sampler, float2((across + 2.5) * texel, v), depth) * fraction;
}
// Bilinear-weighted 3x3 percentage-closer filtering. A plain 3x3 box of nearest texels moves in whole-texel steps,
// which draws stair-stepped rings around every shadow edge. Blending the box filters at the four texels around the
// sample point by its sub-texel position gives the same footprint with weights that change continuously:
// 16 comparisons with separable weights (1 - f, 1, 1, f) / 3 per axis.
export function ShadowVisibility(map: Texture2D<float4>, sampler: Sampler, projected: float3, parameters: float4, nl: f32): f32 {
    if (parameters.x < 0.5 || projected.x < -1.0 || projected.x > 1.0 || projected.y < -1.0 || projected.y > 1.0
        || projected.z < 0.0 || projected.z > 1.0) {
        return 1.0;
    }
    const t: f32 = parameters.z;
    const z: f32 = projected.z - parameters.y * (1.0 + 2.0 * (1.0 - nl));
    const sx: f32 = (projected.x * 0.5 + 0.5) / t - 0.5;
    const sy: f32 = (projected.y * 0.5 + 0.5) / t - 0.5;
    const across: f32 = Floor(sx);
    const row: f32 = Floor(sy);
    const fx: f32 = sx - across;
    const fy: f32 = sy - row;
    return (ShadowRow(map, sampler, row - 1.0, across, fx, t, z) * (1.0 - fy)
        + ShadowRow(map, sampler, row, across, fx, t, z)
        + ShadowRow(map, sampler, row + 1.0, across, fx, t, z)
        + ShadowRow(map, sampler, row + 2.0, across, fx, t, z) * fy) / 9.0;
}

function PlaneShadowTap(map: Texture2D<float4>, sampler: Sampler, uv: float2,
    origin: float2, gradient: float2, depth: f32, texel: f32): f32 {
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
        return 1.0;
    }
    // Nearest shadow texels contain depth at their centers, not at the
    // receiver's arbitrary UV. Compare the extrapolated receiver plane there.
    const center: float2 = float2((Floor(uv.x / texel) + 0.5) * texel,
        (Floor(uv.y / texel) + 0.5) * texel);
    const expected: f32 = depth + (center.x - origin.x) * gradient.x * 2.0
        + (center.y - origin.y) * gradient.y * 2.0;
    return ShadowTap(map, sampler, uv, expected);
}

export function PlaneShadowVisibility(map: Texture2D<float4>, sampler: Sampler,
    projected: float3, parameters: float4, normal: float3, x: float4, y: float4, z: float4): f32 {
    if (parameters.x < 0.5 || projected.x < -1.0 || projected.x > 1.0 || projected.y < -1.0 || projected.y > 1.0
        || projected.z < 0.0 || projected.z > 1.0) {
        return 1.0;
    }
    const rowX: float3 = float3(x.x, x.y, x.z);
    const rowY: float3 = float3(y.x, y.y, y.z);
    const rowZ: float3 = float3(z.x, z.y, z.z);
    const denominator: f32 = Dot3(normal, rowZ);
    if (Abs(denominator) < 0.000001) {
        return ShadowVisibility(map, sampler, projected, parameters, 0.0);
    }
    const gradient: float2 = float2(-Dot3(normal, rowX) * Dot3(rowZ, rowZ) / (denominator * Dot3(rowX, rowX)),
        -Dot3(normal, rowY) * Dot3(rowZ, rowZ) / (denominator * Dot3(rowY, rowY)));
    const origin: float2 = float2(projected.x * 0.5 + 0.5, projected.y * 0.5 + 0.5);
    const t: f32 = parameters.z;
    const depth: f32 = projected.z - parameters.y;
    // Same bilinear-weighted 3x3 footprint as ShadowVisibility, comparing against the receiver plane at each texel.
    const sx: f32 = origin.x / t - 0.5;
    const sy: f32 = origin.y / t - 0.5;
    const across: f32 = Floor(sx);
    const row: f32 = Floor(sy);
    const fx: f32 = sx - across;
    const fy: f32 = sy - row;
    return (PlaneShadowRow(map, sampler, row - 1.0, across, fx, origin, gradient, depth, t) * (1.0 - fy)
        + PlaneShadowRow(map, sampler, row, across, fx, origin, gradient, depth, t)
        + PlaneShadowRow(map, sampler, row + 1.0, across, fx, origin, gradient, depth, t)
        + PlaneShadowRow(map, sampler, row + 2.0, across, fx, origin, gradient, depth, t) * fy) / 9.0;
}

function PlaneShadowRow(map: Texture2D<float4>, sampler: Sampler, row: f32, across: f32, fraction: f32,
    origin: float2, gradient: float2, depth: f32, texel: f32): f32 {
    const v: f32 = (row + 0.5) * texel;
    return PlaneShadowTap(map, sampler, float2((across - 0.5) * texel, v), origin, gradient, depth, texel) * (1.0 - fraction)
        + PlaneShadowTap(map, sampler, float2((across + 0.5) * texel, v), origin, gradient, depth, texel)
        + PlaneShadowTap(map, sampler, float2((across + 1.5) * texel, v), origin, gradient, depth, texel)
        + PlaneShadowTap(map, sampler, float2((across + 2.5) * texel, v), origin, gradient, depth, texel) * fraction;
}

// Keep the linear FP16 attachment finite even with very bright authored lights.
export function LimitRadiance(color: float4): float4 {
    return float4(Clamp(color.x, 0.0, 60000.0), Clamp(color.y, 0.0, 60000.0),
        Clamp(color.z, 0.0, 60000.0), color.w);
}
