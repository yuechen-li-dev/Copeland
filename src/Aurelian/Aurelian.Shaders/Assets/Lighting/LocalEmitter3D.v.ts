import { Dot3, Unit, Add3, Sub3, Scale3 } from "./Lighting3D";
import { LocalAttenuation } from "./SurfaceLighting";

export record EmitterSample3D {
    endpoint: float3;
    direction: float3;
    attenuation: f32;
}

export function EmitterRandom(key: u32): f32 {
    var value: u32 = key;
    value = (value ^ (value >> 16)) * 0x7feb352d;
    value = (value ^ (value >> 15)) * 0x846ca68b;
    value = value ^ (value >> 16);
    return (Convert<f32>(value >> 8) + 0.5) / 16777216.0;
}

function EmitterCross(a: float3, b: float3): float3 {
    return float3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}

export function EmitterSolidAngle(squared: f32, radius: f32): f32 {
    if (squared <= radius * radius) {
        return 0.0;
    }
    const ratio: f32 = radius * radius / squared;
    return 6.2831853 * ratio / (1.0 + Sqrt(Max(1.0 - ratio, 0.0)));
}

export function SampleLocalEmitter(point: float3, sphere: float4, cone: float4, config: float4, key: u32): EmitterSample3D {
    const center: float3 = float3(sphere.x, sphere.y, sphere.z);
    const delta: float3 = Sub3(center, point);
    const squared: f32 = Dot3(delta, delta);
    const axis: float3 = Unit(delta);
    if (config.y < 1.5) {
        return { endpoint: center, direction: axis, attenuation: LocalAttenuation(axis, squared, sphere.w, cone, config) };
    }
    const omega: f32 = EmitterSolidAngle(squared, config.w);
    if (omega <= 0.0 || squared >= sphere.w * sphere.w) {
        return { endpoint: center, direction: axis, attenuation: 0.0 };
    }
    var up: float3 = float3(0.0, 1.0, 0.0);
    if (Abs(axis.y) > 0.99) {
        up = float3(1.0, 0.0, 0.0);
    }
    const tangent: float3 = Unit(EmitterCross(axis, up));
    const side: float3 = EmitterCross(axis, tangent);
    const cosine: f32 = 1.0 - EmitterRandom(key + 17) * omega / 6.2831853;
    const sine: f32 = Sqrt(Max(1.0 - cosine * cosine, 0.0));
    const angle: f32 = EmitterRandom(key + 29) * 6.2831853;
    const direction: float3 = Unit(Add3(Add3(Scale3(axis, cosine), Scale3(tangent, Cos(angle) * sine)), Scale3(side, Sin(angle) * sine)));
    const projection: f32 = Dot3(delta, direction);
    const distance: f32 = projection - Sqrt(Max(config.w * config.w - squared + projection * projection, 0.0));
    const ratio: f32 = squared / (sphere.w * sphere.w);
    const fade: f32 = Max(1.0 - ratio * ratio, 0.0);
    return { endpoint: Add3(point, Scale3(direction, distance)), direction: direction, attenuation: omega * fade * fade };
}
