import { Add3, Sub3, Mul3, Scale3, Dot3, Unit, DirectLightResponseModel, DiffuseResponse } from "./Lighting3D";

// One covered dielectric interface over the substrate. The encoded profile
// uses the negative tag reserved for coat in the opaque surface payload.
export record CoatLayer {
    weight: f32;
    roughness: f32;
    f0: f32;
    f90: f32;
    albedo: f32;
}

export record CoatSubstrate {
    f0: float3;
    f90: f32;
    albedo: float3;
}

export function CoatLookup(nv: f32, roughness: f32): float2 {
    return float2(Sqrt(Clamp((nv - 0.001) / 0.999, 0.0, 1.0)),
        Clamp((roughness - 0.045) / 0.955, 0.0, 1.0));
}

export function EvaluateCoatAlbedo(coefficients: float2, f0: f32, f90: f32): f32 {
    return Clamp(coefficients.x * f0 + coefficients.y * f90, 0.0, 1.0);
}

export function OpenPbrCoat(profile: float4, coefficients: float2): CoatLayer {
    if (profile.w >= -1.0 || profile.x <= 0.0) {
        return { weight: 0.0, roughness: 1.0, f0: 0.0, f90: 0.0, albedo: 0.0 };
    }
    let ior: f32 = -profile.w;
    let normalAmplitude: f32 = (ior - 1.0) / (ior + 1.0);
    let f0: f32 = normalAmplitude * normalAmplitude;
    // Fade the Schlick grazing approximation as the interface vanishes.
    let f90: f32 = Min(1.0, (ior - 1.0) * 10.0);
    return { weight: profile.x, roughness: Max(profile.y, 0.045), f0: f0, f90: f90,
        albedo: EvaluateCoatAlbedo(coefficients, f0, f90) };
}

export function CoatTransmission(coat: CoatLayer): f32 {
    // OpenPBR directional albedo scaling, with a small reserve for LUT interpolation.
    return 1.0 - coat.weight * Min(1.0, coat.albedo + 0.006 * coat.f90);
}

export function CoatBase(base: float3, metallic: f32, dielectric: f32, coefficients: float2): CoatSubstrate {
    let normalReflectance: f32 = dielectric * (1.0 - metallic);
    let f0: float3 = Add3(Scale3(base, metallic), float3(normalReflectance, normalReflectance, normalReflectance));
    var f90: f32 = 1.0;
    if (dielectric == 0.0 && metallic == 0.0) { f90 = 0.0; }
    return { f0: f0, f90: f90, albedo: float3(EvaluateCoatAlbedo(coefficients, f0.x, f90),
        EvaluateCoatAlbedo(coefficients, f0.y, f90), EvaluateCoatAlbedo(coefficients, f0.z, f90)) };
}

function GgxFactors(roughness: f32, normal: float3, view: float3, light: float3): float2 {
    let nl: f32 = Dot3(normal, light);
    let nv: f32 = Max(Dot3(normal, view), 0.001);
    if (nl <= 0.0) { return float2(0.0, 0.0); }
    let half: float3 = Unit(Add3(view, light));
    let nh: f32 = Clamp(Dot3(normal, half), 0.0, 1.0);
    let vh: f32 = Clamp(Dot3(view, half), 0.0, 1.0);
    let alpha: f32 = roughness * roughness;
    let alphaSquared: f32 = alpha * alpha;
    let shape: f32 = (1.0 - nh * nh) + nh * nh * alphaSquared;
    let distribution: f32 = alphaSquared / (3.14159265359 * shape * shape);
    let maskingV: f32 = Sqrt(alphaSquared + (1.0 - alphaSquared) * nv * nv);
    let maskingL: f32 = Sqrt(alphaSquared + (1.0 - alphaSquared) * nl * nl);
    let grazing: f32 = 1.0 - vh;
    // GGX with height-correlated Smith visibility and the incident cosine.
    return float2(distribution * nl / (2.0 * (nl * maskingV + nv * maskingL)),
        grazing * grazing * grazing * grazing * grazing);
}

export function CoatSpecular(coat: CoatLayer, normal: float3, view: float3, light: float3): f32 {
    if (coat.weight <= 0.0) { return 0.0; }
    let factors: float2 = GgxFactors(coat.roughness, normal, view, light);
    return coat.weight * factors.x * (coat.f0 + (coat.f90 - coat.f0) * factors.y);
}

export function LayerCoat(substrate: float3, coatWeight: f32, transmission: f32): float3 {
    return Add3(Scale3(substrate, transmission), float3(coatWeight, coatWeight, coatWeight));
}

export function CoatedLightResponse(base: float3, normal: float3, view: float3, light: float3,
    metallic: f32, roughness: f32, dielectric: f32, model: f32, coat: CoatLayer, substrate: CoatSubstrate): float3 {
    if (coat.weight <= 0.0) {
        return DirectLightResponseModel(base, normal, view, light, metallic, roughness, dielectric, model);
    }
    let response: float3 = BoundedSubstrate(base, normal, view, light, metallic, roughness, model, substrate);
    return LayerCoat(response, CoatSpecular(coat, normal, view, light), CoatTransmission(coat));
}

export function BoundedSubstrate(base: float3, normal: float3, view: float3, light: float3,
    metallic: f32, roughness: f32, model: f32, substrate: CoatSubstrate): float3 {
    let factors: float2 = GgxFactors(roughness, normal, view, light);
    return BoundedSubstrateWithFactors(base, normal, view, light, metallic, roughness, model, substrate, factors, 0.006);
}

export function BoundedSubstrateWithFactors(base: float3, normal: float3, view: float3, light: float3,
    metallic: f32, roughness: f32, model: f32, substrate: CoatSubstrate, factors: float2, albedoReserve: f32): float3 {
    let fresnel: float3 = Add3(Scale3(substrate.f0, 1.0 - factors.y),
        float3(substrate.f90 * factors.y, substrate.f90 * factors.y, substrate.f90 * factors.y));
    let specular: float3 = Scale3(fresnel, factors.x);
    // Only admit normalized diffuse lobes inside the layered substrate.
    var diffuseModel: f32 = model;
    if (model > 0.5 && model < 2.5) { diffuseModel = 3.0; }
    let diffuse: float3 = DiffuseResponse(base, normal, view, light, roughness, diffuseModel);
    let margin: f32 = albedoReserve * substrate.f90;
    let remaining: float3 = float3(Max(0.0, 1.0 - substrate.albedo.x - margin),
        Max(0.0, 1.0 - substrate.albedo.y - margin), Max(0.0, 1.0 - substrate.albedo.z - margin));
    let cosine: f32 = Max(0.0, Dot3(normal, light));
    return Add3(specular, Scale3(Mul3(diffuse, remaining), cosine * (1.0 - metallic) / 3.14159265359));
}
