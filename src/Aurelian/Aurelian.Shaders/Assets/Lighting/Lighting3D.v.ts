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
// BRDF / (1 / pi), with both directions pointing away from the surface.
// Burley 2012 section 5.3; Lagarde/de Rousiers 2014 listing 1.
// EON: Portsmouth/Kutz/Hill 2025, unit-albedo lobe scaled by authored base color.
// The EON fit is adapted from MIT-licensed EON-diffuse; see Assets/Licenses/EON-LICENSE.txt.
function Pow5(x: f32): f32 {
    return x * x * x * x * x;
}
function FonAlbedo(mu: f32, roughness: f32): f32 {
    const x: f32 = 1.0 - mu;
    const integral: f32 = x * (0.0571085289 + x * (0.491881867 + x * (-0.332181442 + x * 0.0714429953)));
    return (1.0 + roughness * integral) / (1.0 + (0.5 - 2.0 / (3.0 * Pi)) * roughness);
}
// Engine experiment: separable first bounce plus an analytic colored path closure.
// The cosine-weighted integral of (1 - mu)^5 is 1/21. No texture or angular fit is needed.
// Returns single-bounce shape, unit-albedo multiple-bounce shape, and mean single-bounce energy.
function PathWeights(nl: f32, nv: f32, roughness: f32): float3 {
    const attenuation: f32 = 1.0 + (1.0 / 1.51 - 1.0) * roughness;
    const edge: f32 = 0.5 * roughness;
    const incident: f32 = 1.0 - edge * Pow5(1.0 - nl);
    const outgoing: f32 = 1.0 - edge * Pow5(1.0 - nv);
    const integral: f32 = 1.0 - edge / 21.0;
    const average: f32 = attenuation * integral * integral;
    const single: f32 = attenuation * incident * outgoing;
    const missingIncident: f32 = 1.0 - attenuation * integral * incident;
    const missingOutgoing: f32 = 1.0 - attenuation * integral * outgoing;
    const multiple: f32 = missingIncident * missingOutgoing / Max(1.0 - average, 0.0000001);
    return float3(single, multiple, average);
}
function PathColor(base: f32, weights: float3): f32 {
    const albedo: f32 = Clamp(base, 0.0, 1.0);
    const bounce: f32 = albedo * albedo * weights.z / (1.0 - albedo * (1.0 - weights.z));
    return albedo * weights.x + bounce * weights.y;
}
export function DiffuseFactor(normal: float3, view: float3, light: float3, roughness: f32, model: f32): f32 {
    if (model < 0.5) {
        return 1.0;
    }
    const nl: f32 = Clamp(Dot3(normal, light), 0.0, 1.0);
    const nv: f32 = Clamp(Dot3(normal, view), 0.0, 1.0);
    const r: f32 = Clamp(roughness, 0.0, 1.0);
    if (model > 3.5) {
        const weights: float3 = PathWeights(nl, nv, r);
        return weights.x + weights.y;
    }
    if (model > 2.5) {
        if (r < 0.00001) {
            return 1.0;
        }
        const tangentDot: f32 = Dot3(light, view) - nl * nv;
        var projected: f32 = tangentDot;
        if (tangentDot > 0.0) {
            projected = tangentDot / Max(Max(nl, nv), 0.000001);
        }
        const normalization: f32 = 1.0 / (1.0 + (0.5 - 2.0 / (3.0 * Pi)) * r);
        const single: f32 = normalization * (1.0 + r * projected);
        const average: f32 = normalization * (1.0 + (2.0 / 3.0 - 28.0 / (15.0 * Pi)) * r);
        const multiple: f32 = (1.0 - FonAlbedo(nl, r)) * (1.0 - FonAlbedo(nv, r)) / Max(1.0 - average, 0.0000001);
        return single + multiple;
    }
    const half: float3 = Unit(Add3(light, view));
    const lh: f32 = Clamp(Dot3(light, half), 0.0, 1.0);
    var grazing: f32 = 0.5 + 2.0 * r * lh * lh;
    var energy: f32 = 1.0;
    if (model > 1.5) {
        grazing = 0.5 * r + 2.0 * r * lh * lh;
        energy = 1.0 + (1.0 / 1.51 - 1.0) * r;
    }
    return (1.0 + (grazing - 1.0) * Pow5(1.0 - nl))
        * (1.0 + (grazing - 1.0) * Pow5(1.0 - nv)) * energy;
}
// Diffuse BRDF times pi, including color. PathDiffuse treats base as single-bounce
// reflectance; legacy models retain their authored-albedo multiplier semantics.
export function DiffuseResponse(base: float3, normal: float3, view: float3, light: float3, roughness: f32, model: f32): float3 {
    if (model > 3.5) {
        const weights: float3 = PathWeights(Clamp(Dot3(normal, light), 0.0, 1.0),
            Clamp(Dot3(normal, view), 0.0, 1.0), Clamp(roughness, 0.0, 1.0));
        return float3(PathColor(base.x, weights), PathColor(base.y, weights), PathColor(base.z, weights));
    }
    return Scale3(base, DiffuseFactor(normal, view, light, roughness, model));
}
function ChannelWithDiffuse(base: f32, metallic: f32, vh: f32, specular: f32, dielectric: f32, diffuse: f32): f32 {
    if (dielectric == 0.0 && metallic == 0.0) {
        return diffuse / Pi;
    }
    const f0: f32 = dielectric * (1.0 - metallic) + base * metallic;
    const f: f32 = Fresnel(f0, vh);
    return (1.0 - f) * (1.0 - metallic) * diffuse / Pi + f * specular;
}
export function DirectLightResponseModel(base: float3, normal: float3, view: float3, light: float3, metallic: f32, roughness: f32, dielectric: f32, model: f32): float3 {
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
    const diffuse: float3 = DiffuseResponse(base, normal, view, light, roughness, model);
    return Scale3(float3(ChannelWithDiffuse(base.x, metallic, vh, specular, dielectric, diffuse.x),
        ChannelWithDiffuse(base.y, metallic, vh, specular, dielectric, diffuse.y),
        ChannelWithDiffuse(base.z, metallic, vh, specular, dielectric, diffuse.z)), nl);
}
export function DirectLightResponse(base: float3, normal: float3, view: float3, light: float3, metallic: f32, roughness: f32, dielectric: f32): float3 {
    return DirectLightResponseModel(base, normal, view, light, metallic, roughness, dielectric, 0.0);
}
export function DirectLightModel(base: float3, normal: float3, view: float3, light: float3, metallic: f32, roughness: f32, model: f32): float3 {
    return DirectLightResponseModel(base, normal, view, light, metallic, roughness, 0.04, model);
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
export function DirectDiffuseModel(base: float3, normal: float3, view: float3, light: float3, metallic: f32, roughness: f32, model: f32): float3 {
    const half: float3 = Unit(Add3(light, view));
    const vh: f32 = Max(Dot3(view, half), 0.0);
    const nl: f32 = Max(Dot3(normal, light), 0.0);
    const diffuse: float3 = DiffuseResponse(base, normal, view, light, roughness, model);
    return Scale3(float3(ChannelWithDiffuse(base.x, metallic, vh, 0.0, 0.04, diffuse.x),
        ChannelWithDiffuse(base.y, metallic, vh, 0.0, 0.04, diffuse.y),
        ChannelWithDiffuse(base.z, metallic, vh, 0.0, 0.04, diffuse.z)), nl);
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
