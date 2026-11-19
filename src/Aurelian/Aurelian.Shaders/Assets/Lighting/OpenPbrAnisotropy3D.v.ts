import { Add3, Sub3, Scale3, Dot3, Cross3, Unit } from "./Lighting3D";
import { EncodeOcta, DecodeOcta } from "./SurfaceLighting";

export record AnisotropyLayer {
    active: bool;
    tangent: float3;
    bitangent: float3;
    widths: float2;
}

function TangentOnNormal(normal: float3, direction: float3): float3 {
    var tangent: float3 = Sub3(direction, Scale3(normal, Dot3(normal, direction)));
    if (Dot3(tangent, tangent) < 0.000001) {
        var axis: float3 = float3(0.0, 0.0, 1.0);
        if (Abs(normal.z) > 0.9) { axis = float3(1.0, 0.0, 0.0); }
        tangent = Cross3(axis, normal);
    }
    return Unit(tangent);
}

export function AnisotropyDirection(normal: float3, meshTangent: float4, rotation: float2): float3 {
    let tangent: float3 = TangentOnNormal(normal, float3(meshTangent.x, meshTangent.y, meshTangent.z));
    let bitangent: float3 = Scale3(Cross3(normal, tangent), meshTangent.w);
    return Unit(Add3(Scale3(tangent, rotation.x), Scale3(bitangent, rotation.y)));
}

// One integer attachment carries octahedral world direction, strength and
// convention. The tangent is reprojected against the decoded shading normal.
export function PackAnisotropy(normal: float4, meshTangent: float4, profile: float4): u32 {
    if (profile.z <= 0.0) { return 0; }
    let direction: float3 = AnisotropyDirection(float3(normal.x, normal.y, normal.z), meshTangent, float2(profile.x, profile.y));
    let oct: float2 = EncodeOcta(direction);
    let x: u32 = Convert<u32>(Clamp(oct.x, 0.0, 1.0) * 1023.0 + 0.5);
    let y: u32 = Convert<u32>(Clamp(oct.y, 0.0, 1.0) * 1023.0 + 0.5);
    let weight: u32 = Convert<u32>(profile.z * 2047.0 + 0.5);
    let convention: u32 = Convert<u32>(profile.w);
    return x | (y << 10) | (weight << 20) | (convention << 31);
}

export function OpenPbrAnisotropy(normal: float3, direction: float3, roughness: f32,
    strength: f32, gltf: bool): AnisotropyLayer {
    let alpha: f32 = roughness * roughness;
    if (strength <= 0.0) {
        return { active: false, tangent: float3(0.0, 0.0, 0.0), bitangent: float3(0.0, 0.0, 0.0), widths: float2(alpha, alpha) };
    }
    let tangent: float3 = TangentOnNormal(normal, direction);
    let compression: f32 = 1.0 - strength;
    var major: f32 = alpha * Sqrt(2.0 / (1.0 + compression * compression));
    var minor: f32 = Max(major * compression, 0.002025);
    if (gltf) {
        major = alpha + (1.0 - alpha) * strength * strength;
        minor = alpha;
    }
    return { active: true, tangent: tangent, bitangent: Cross3(normal, tangent), widths: float2(major, minor) };
}

export function DecodeAnisotropy(normal: float3, roughness: f32, packed: u32): AnisotropyLayer {
    let strength: f32 = Convert<f32>((packed >> 20) & 2047) / 2047.0;
    var direction: float3 = float3(0.0, 0.0, 0.0);
    if (strength > 0.0) {
        direction = DecodeOcta(float2(Convert<f32>(packed & 1023) / 1023.0, Convert<f32>((packed >> 10) & 1023) / 1023.0));
    }
    return OpenPbrAnisotropy(normal, direction, roughness, strength, (packed >> 31) != 0);
}

// Sample densely near the horizon and the narrow axis, where albedo changes
// fastest. The two GGX symmetry planes fold azimuth to the first quadrant.
export function AnisotropyLookup(layer: AnisotropyLayer, normal: float3, view: float3): float4 {
    let x: f32 = Abs(Dot3(layer.tangent, view));
    let y: f32 = Abs(Dot3(layer.bitangent, view));
    var angle: f32 = 0.0;
    if (x + y > 0.000001) { angle = Atan2(y, x); }
    let viewCosine: f32 = Clamp((Dot3(normal, view) - 0.001) / 0.999, 0.0, 1.0);
    let widthRange: f32 = Log(1.41421356237 / 0.002025);
    return float4(Sqrt(viewCosine) * 40.0,
        24.0 * Clamp(Log(layer.widths.x / 0.002025) / widthRange, 0.0, 1.0),
        (1.0 - Sqrt(Max(0.0, 1.0 - angle * (2.0 / 3.14159265359)))) * 8.0,
        24.0 * Clamp(Log(layer.widths.y / 0.002025) / widthRange, 0.0, 1.0));
}

export function AnisotropyAlbedoReserve(layer: AnisotropyLayer, normal: float3, view: float3): f32 {
    // Allow for LUT interpolation and integration error. The horizon of a
    // very narrow lobe needs a larger reserve than other viewing angles.
    let horizon: f32 = Clamp(1.0 - Dot3(normal, view) / 0.055, 0.0, 1.0);
    let narrow: f32 = Max(1.0 - 18.0 * layer.widths.y / layer.widths.x, 0.0);
    return 0.025 + 0.14 * horizon * narrow;
}

function StretchedLength(layer: AnisotropyLayer, normalCosine: f32, direction: float3): f32 {
    let t: f32 = Dot3(layer.tangent, direction) * layer.widths.x;
    let b: f32 = Dot3(layer.bitangent, direction) * layer.widths.y;
    return Sqrt(t * t + b * b + normalCosine * normalCosine);
}

function Distribution(layer: AnisotropyLayer, nh: f32, half: float3): f32 {
    let t: f32 = Dot3(layer.tangent, half) / layer.widths.x;
    let b: f32 = Dot3(layer.bitangent, half) / layer.widths.y;
    let shape: f32 = t * t + b * b + nh * nh;
    return 1.0 / (3.14159265359 * layer.widths.x * layer.widths.y * shape * shape);
}

export function AnisotropicGgxFactors(layer: AnisotropyLayer, normal: float3, view: float3, light: float3): float2 {
    let nl: f32 = Dot3(normal, light);
    if (nl <= 0.0) { return float2(0.0, 0.0); }
    let nv: f32 = Max(Dot3(normal, view), 0.001);
    let half: float3 = Unit(Add3(view, light));
    let nh: f32 = Max(Dot3(normal, half), 0.0);
    let g: f32 = 1.0 - Clamp(Dot3(view, half), 0.0, 1.0);
    let masking: f32 = nl * StretchedLength(layer, nv, view) + nv * StretchedLength(layer, nl, light);
    return float2(Distribution(layer, nh, half) * nl / (2.0 * masking), g * g * g * g * g);
}

export function AnisotropicReflectionPdf(layer: AnisotropyLayer, normal: float3, view: float3, light: float3): f32 {
    let nv: f32 = Max(Dot3(normal, view), 0.001);
    let half: float3 = Unit(Add3(view, light));
    return Distribution(layer, Max(Dot3(normal, half), 0.0), half) / (2.0 * (nv + StretchedLength(layer, nv, view)));
}

// Heitz visible-normal sampling in the principal frame. Matches the reference
// PDF and the directional albedo bake's half-vector distribution.
export function SampleAnisotropicGgx(layer: AnisotropyLayer, normal: float3, view: float3, sample: float2): float3 {
    let nv: f32 = Max(Dot3(normal, view), 0.001);
    let stretched: float3 = Unit(float3(Dot3(view, layer.tangent) * layer.widths.x, Dot3(view, layer.bitangent) * layer.widths.y, nv));
    let xy: f32 = stretched.x * stretched.x + stretched.y * stretched.y;
    var diskX: float3 = float3(1.0, 0.0, 0.0);
    if (xy > 0.00000001) {
        diskX = Scale3(float3(-stretched.y, stretched.x, 0.0), 1.0 / Sqrt(xy));
    }
    let diskY: float3 = Cross3(stretched, diskX);
    let radius: f32 = Sqrt(sample.x);
    let turn: f32 = 6.28318530718 * sample.y;
    let x: f32 = radius * Cos(turn);
    let blend: f32 = 0.5 * (1.0 + stretched.z);
    let y: f32 = (1.0 - blend) * Sqrt(Max(0.0, 1.0 - x * x)) + blend * radius * Sin(turn);
    let z: f32 = Sqrt(Max(0.0, 1.0 - x * x - y * y));
    let h: float3 = Add3(Scale3(diskX, x), Add3(Scale3(diskY, y), Scale3(stretched, z)));
    return Unit(Add3(Scale3(layer.tangent, layer.widths.x * h.x),
        Add3(Scale3(layer.bitangent, layer.widths.y * h.y), Scale3(normal, Max(0.0, h.z)))));
}
