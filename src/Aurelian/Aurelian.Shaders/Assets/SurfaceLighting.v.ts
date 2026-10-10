import { Unit, Add3, Scale3, Dot3, Mul3 } from "./Lighting3D";

function Sign(value: f32): f32 {
    if (value < 0.0) {
        return -1.0;
    }
    return 1.0;
}
export function DecodeOcta(uv: float2): float3 {
    var x: f32 = uv.x * 2.0 - 1.0;
    var z: f32 = uv.y * 2.0 - 1.0;
    const y: f32 = 1.0 - Abs(x) - Abs(z);
    if (y < 0.0) {
        const old: f32 = x;
        x = (1.0 - Abs(z)) * Sign(x);
        z = (1.0 - Abs(old)) * Sign(z);
    }
    return Unit(float3(x, y, z));
}
export function EncodeOcta(direction: float3): float2 {
    const divisor: f32 = Max(Abs(direction.x) + Abs(direction.y) + Abs(direction.z), 0.00001);
    var x: f32 = direction.x / divisor;
    var z: f32 = direction.z / divisor;
    if (direction.y < 0.0) {
        const old: f32 = x;
        x = (1.0 - Abs(z)) * Sign(x);
        z = (1.0 - Abs(old)) * Sign(z);
    }
    return float2(x * 0.5 + 0.5, z * 0.5 + 0.5);
}
export function Band(map: Texture2D<float4>, sampler: Sampler, uv: float2, band: f32): float3 {
    const coordinate: float2 = float2((Clamp(uv.x, 0.0, 1.0) * 63.0 + 0.5) / 64.0,
        (band * 64.0 + Clamp(uv.y, 0.0, 1.0) * 63.0 + 0.5) / 512.0);
    const value: float4 = Sample(map, sampler, coordinate);
    return float3(value.x, value.y, value.z);
}
export function PrefilteredEnvironment(map: Texture2D<float4>, sampler: Sampler, uv: float2, roughness: f32): float3 {
    const lod: f32 = Clamp(roughness, 0.0, 1.0) * 5.0;
    const low: f32 = Floor(lod);
    const fraction: f32 = lod - low;
    return Add3(Scale3(Band(map, sampler, uv, low), 1.0 - fraction),
        Scale3(Band(map, sampler, uv, Min(low + 1.0, 5.0)), fraction));
}
export function EnvironmentSurfaceResponse(map: Texture2D<float4>, sampler: Sampler, base: float3,
    normal: float3, reflected: float3, nv: f32, metallic: f32, roughness: f32, dielectric: f32): float3 {
    const diffuse: float3 = Band(map, sampler, EncodeOcta(normal), 6.0);
    if (dielectric == 0.0 && metallic == 0.0) {
        return Mul3(base, diffuse);
    }
    const uv: float2 = EncodeOcta(reflected);
    const specular: float3 = PrefilteredEnvironment(map, sampler, uv, roughness);
    const dfg: float3 = Band(map, sampler, float2(nv, roughness), 7.0);
    const f0: float3 = Add3(Scale3(base, metallic), float3(dielectric * (1.0 - metallic),
        dielectric * (1.0 - metallic), dielectric * (1.0 - metallic)));
    const energy: float3 = Add3(Scale3(f0, dfg.x), float3(dfg.y, dfg.y, dfg.y));
    const kd: float3 = float3((1.0 - energy.x) * (1.0 - metallic),
        (1.0 - energy.y) * (1.0 - metallic), (1.0 - energy.z) * (1.0 - metallic));
    return Add3(Mul3(Mul3(base, diffuse), kd), Mul3(specular, energy));
}
export function EnvironmentResponse(map: Texture2D<float4>, sampler: Sampler, base: float3,
    normal: float3, reflected: float3, nv: f32, metallic: f32, roughness: f32): float3 {
    return EnvironmentSurfaceResponse(map, sampler, base, normal, reflected, nv, metallic, roughness, 0.04);
}
export function EnvironmentDiffuse(map: Texture2D<float4>, sampler: Sampler, base: float3,
    normal: float3, nv: f32, roughness: f32): float3 {
    const diffuse: float3 = Band(map, sampler, EncodeOcta(normal), 6.0);
    const dfg: float3 = Band(map, sampler, float2(nv, roughness), 7.0);
    const energy: f32 = 0.04 * dfg.x + dfg.y;
    return Scale3(Mul3(base, diffuse), 1.0 - energy);
}
export function WorldAt(uv: float2, depth: f32, x: float4, y: float4, z: float4, w: float4): float3 {
    const p: float4 = float4(uv.x * 2.0 - 1.0, uv.y * 2.0 - 1.0, depth, 1.0);
    const divisor: f32 = w.x * p.x + w.y * p.y + w.z * p.z + w.w;
    return float3((x.x * p.x + x.y * p.y + x.z * p.z + x.w) / divisor,
        (y.x * p.x + y.y * p.y + y.z * p.z + y.w) / divisor,
        (z.x * p.x + z.y * p.y + z.z * p.z + z.w) / divisor);
}
enum MaskWord {
    X,
    Y,
    Z,
    W,
}
function WordFor(index: u32): MaskWord {
    if (index < 8) {
        return MaskWord.X;
    }
    if (index < 16) {
        return MaskWord.Y;
    }
    if (index < 24) {
        return MaskWord.Z;
    }
    return MaskWord.W;
}
export function AddMaskBit(mask: float4, index: u32, value: f32): float4 {
    return match WordFor(index) {
        MaskWord.X => float4(mask.x + value, mask.y, mask.z, mask.w),
        MaskWord.Y => float4(mask.x, mask.y + value, mask.z, mask.w),
        MaskWord.Z => float4(mask.x, mask.y, mask.z + value, mask.w),
        MaskWord.W => float4(mask.x, mask.y, mask.z, mask.w + value),
    };
}
export function MaskAllows(mask: float4, index: u32): bool {
    const value: f32 = match WordFor(index) {
        MaskWord.X => mask.x,
        MaskWord.Y => mask.y,
        MaskWord.Z => mask.z,
        MaskWord.W => mask.w,
    };
    const shifted: f32 = Floor(value / Pow(2.0, (Convert<f32>(index) - Floor(Convert<f32>(index) / 8.0) * 8.0)));
    return shifted - Floor(shifted / 2.0) * 2.0 > 0.5;
}

function BoxDistance(position: f32, direction: f32, origin: f32, halfSize: f32): f32 {
    if (Abs(direction) < 0.00001) {
        return 100000.0;
    }
    return (origin + Sign(direction) * halfSize - position) / direction;
}
export function BoxReflection(position: float3, reflected: float3, origin: float4, bounds: float4): float3 {
    if (bounds.w < 0.5 || Abs(position.x - origin.x) > bounds.x
        || Abs(position.y - origin.y) > bounds.y || Abs(position.z - origin.z) > bounds.z) {
        return reflected;
    }
    const distance: f32 = Min(BoxDistance(position.x, reflected.x, origin.x, bounds.x),
        Min(BoxDistance(position.y, reflected.y, origin.y, bounds.y), BoxDistance(position.z, reflected.z, origin.z, bounds.z)));
    const corrected: float3 = Unit(float3(position.x + reflected.x * distance - origin.x,
        position.y + reflected.y * distance - origin.y, position.z + reflected.z * distance - origin.z));
    const edgeDistance: f32 = Min(bounds.x - Abs(position.x - origin.x),
        Min(bounds.y - Abs(position.y - origin.y), bounds.z - Abs(position.z - origin.z)));
    const feather: f32 = Min(bounds.x, Min(bounds.y, bounds.z)) * 0.2;
    const blend: f32 = Clamp(edgeDistance / feather, 0.0, 1.0);
    return Unit(Add3(Scale3(reflected, 1.0 - blend), Scale3(corrected, blend)));
}

enum LocalSource {
    Point,
    Spot(cosine: f32, inner: f32, outer: f32),
}
function LocalSourceFor(direction: float3, cone: float4, config: float4): LocalSource {
    if (config.y > 0.5) {
        return LocalSource.Spot(-Dot3(direction, float3(cone.x, cone.y, cone.z)), config.x, cone.w);
    }
    return LocalSource.Point;
}
function SpotFade(cosine: f32, inner: f32, outer: f32): f32 {
    const blend: f32 = Clamp((cosine - outer) / Max(inner - outer, 0.00001), 0.0, 1.0);
    return blend * blend * (3.0 - 2.0 * blend);
}
export function ConeAttenuation(direction: float3, cone: float4, config: float4): f32 {
    return match LocalSourceFor(direction, cone, config) {
        LocalSource.Point => 1.0,
        LocalSource.Spot(payload) => SpotFade(payload.cosine, payload.inner, payload.outer),
    };
}
