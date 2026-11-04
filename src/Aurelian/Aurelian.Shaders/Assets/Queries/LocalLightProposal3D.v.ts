import { LocalAttenuation } from "./SurfaceLighting";
import { Dot3, Unit } from "./Lighting3D";
import { Read4 } from "./LocalLightSamplingMath3D";
import { EmitterSolidAngle } from "./LocalEmitter3D";

export function RawTarget(readonly parameters: StorageBuffer<f32>, point: float3, normal: float3, light: u32): f32 {
    let offset: u32 = 64 + light * 16;
    let sphere: float4 = Read4(parameters, offset);
    let color: float4 = Read4(parameters, offset + 4);
    let delta: float3 = float3(sphere.x, sphere.y, sphere.z) - point;
    let direction: float3 = Unit(delta);
    var attenuation: f32 = LocalAttenuation(direction, Dot3(delta, delta), sphere.w,
        Read4(parameters, offset + 8), Read4(parameters, offset + 12));
    if (parameters[offset + 13] > 1.5) {
        let ratio: f32 = Dot3(delta, delta) / (sphere.w * sphere.w);
        let fade: f32 = Max(1.0 - ratio * ratio, 0.0);
        attenuation = EmitterSolidAngle(Dot3(delta, delta), parameters[offset + 15]) * fade * fade;
    }
    var cosine: f32 = Max(Dot3(normal, direction), 0.0);
    if (parameters[offset + 13] > 1.5) {
        cosine = Max(Dot3(normal, direction) + parameters[offset + 15] / Sqrt(Max(Dot3(delta, delta), 0.000001)), 0.0);
    }
    let power: f32 = color.w * (color.x * 0.2126 + color.y * 0.7152 + color.z * 0.0722);
    return Min(power * attenuation * cosine, 1.0e20);
}
export function Target(readonly parameters: StorageBuffer<f32>, point: float3, normal: float3, light: u32): f32 {
    // Positive support keeps temporal normalization well-defined for dark candidates.
    return Max(0.00000001, RawTarget(parameters, point, normal, light));
}
