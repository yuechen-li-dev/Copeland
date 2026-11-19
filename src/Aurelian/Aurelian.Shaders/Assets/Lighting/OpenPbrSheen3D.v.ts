import { Add3, Cross3, Dot3, Scale3, Sub3, Unit } from "./Lighting3D";
import { CoatLayer, CoatSubstrate, CoatedLightResponse, BoundedSubstrate, BoundedSubstrateWithFactors,
    LayerCoat, CoatSpecular, CoatTransmission } from "./OpenPbrCoat3D";
import { AnisotropyLayer, AnisotropicGgxFactors, AnisotropyAlbedoReserve } from "./OpenPbrAnisotropy3D";

export record SheenLayer {
    active: bool;
    budget: f32;
    tint: float3;
    a: f32;
    b: f32;
    tangent: float3;
    roughness: f32;
}

// Zeltner, Burley and Chiang (SIGGRAPH 2022). The closed form coefficients
// are the MaterialX PBR specification's fit to volumetric sheen transport.
export function OpenPbrSheen(weight: f32, profile: float4, normal: float3, view: float3): SheenLayer {
    if (weight <= 0.0) {
        return { active: false, budget: 0.0, tint: float3(0.0, 0.0, 0.0), a: 1.0, b: 0.0,
            tangent: float3(0.0, 0.0, 0.0), roughness: 1.0 };
    }
    let viewCosine: f32 = Dot3(normal, view);
    let x: f32 = Clamp(viewCosine, 0.0, 1.0);
    let y: f32 = Clamp(profile.w, 0.01, 1.0);
    let a: f32 = ((2.58126 * x + 0.813703 * y) * y) / (1.0 + 0.310327 * x * x + 2.60994 * x * y);
    let b: f32 = Sqrt(1.0 - x) * (y - 1.0) * y * y * y
        / (0.0000254053 + x * (1.71228 - 1.71506 * y) + 1.34174 * y * y);
    let sigma: f32 = y * (0.0206607 + 1.58491 * y) / (0.0379424 + y * (1.32227 + y));
    let mean: f32 = y * (-0.193854 + y * (-1.14885 + y * (1.7932 - 0.95943 * y * y))) / (0.046391 + y);
    let offset: f32 = y * (0.000654023 + (-0.0207818 + 0.119681 * y) * y) / (1.26264 + y * (-1.92021 + y));
    let z: f32 = (x - mean) / sigma;
    let budget: f32 = weight * Clamp(0.3989422804 * Exp(-0.5 * z * z) / sigma + offset, 0.0, 1.0);
    var tangent: float3 = Sub3(view, Scale3(normal, viewCosine));
    if (Dot3(tangent, tangent) < 0.000001) {
        var axis: float3 = float3(0.0, 0.0, 1.0);
        if (Abs(normal.z) > 0.9) { axis = float3(1.0, 0.0, 0.0); }
        tangent = Cross3(axis, normal);
    }
    return { active: true, budget: budget, tint: float3(profile.x, profile.y, profile.z),
        a: a, b: b, tangent: Unit(tangent), roughness: y };
}

// The inverse transform preserves the hemisphere. Its Jacobian is
// a^2 / |M^-1 light|^3, so the cosine-weighted lobe integrates to 1.
export function SheenPdf(sheen: SheenLayer, normal: float3, light: float3): f32 {
    let nl: f32 = Dot3(normal, light);
    if (nl <= 0.0) { return 0.0; }
    let x: f32 = sheen.a * Dot3(sheen.tangent, light) + sheen.b * nl;
    let y: f32 = sheen.a * Dot3(Cross3(normal, sheen.tangent), light);
    let lengthSquared: f32 = x * x + y * y + nl * nl;
    return sheen.a * sheen.a * nl / (3.14159265359 * lengthSquared * lengthSquared);
}

export function SampleSheen(sheen: SheenLayer, normal: float3, sample: float2): float3 {
    let radius: f32 = Sqrt(sample.x);
    let angle: f32 = 6.28318530718 * sample.y;
    let z: f32 = Sqrt(1.0 - sample.x);
    let direction: float3 = Add3(Scale3(normal, sheen.a * z),
        Add3(Scale3(sheen.tangent, radius * Cos(angle) - sheen.b * z),
            Scale3(Cross3(normal, sheen.tangent), radius * Sin(angle))));
    return Scale3(direction, 1.0 / Sqrt(Dot3(direction, direction)));
}

export function SheenAxis(sheen: SheenLayer, normal: float3): float3 {
    return Unit(Add3(Scale3(normal, sheen.a), Scale3(sheen.tangent, -sheen.b)));
}

export function LayeredLightResponse(base: float3, normal: float3, view: float3, light: float3,
    metallic: f32, roughness: f32, dielectric: f32, model: f32, coat: CoatLayer, substrate: CoatSubstrate,
    sheen: SheenLayer, anisotropy: AnisotropyLayer): float3 {
    if (!sheen.active && !anisotropy.active) {
        return CoatedLightResponse(base, normal, view, light, metallic, roughness, dielectric, model, coat, substrate);
    }
    var lower: float3 = float3(0.0, 0.0, 0.0);
    if (anisotropy.active) {
        lower = BoundedSubstrateWithFactors(base, normal, view, light, metallic, roughness, model, substrate,
            AnisotropicGgxFactors(anisotropy, normal, view, light), AnisotropyAlbedoReserve(anisotropy, normal, view));
    } else {
        lower = BoundedSubstrate(base, normal, view, light, metallic, roughness, model, substrate);
    }
    if (coat.weight > 0.0) {
        lower = LayerCoat(lower, CoatSpecular(coat, normal, view, light), CoatTransmission(coat));
    }
    if (!sheen.active) {
        return lower;
    }
    // Gray transmission. Tint only the portion reflected by the fibers.
    return Add3(Scale3(lower, 1.0 - sheen.budget), Scale3(sheen.tint, sheen.budget * SheenPdf(sheen, normal, light)));
}
