import { LocalAttenuation } from "./SurfaceLighting";
import { Dot3, Unit } from "./Lighting3D";
import { Read4 } from "./LocalLightSamplingMath3D";

export function RawTarget(readonly parameters: StorageBuffer<f32>, point: float3, normal: float3, light: u32): f32 {
    const offset: u32 = 64 + light * 16;
    const sphere: float4 = Read4(parameters, offset);
    const color: float4 = Read4(parameters, offset + 4);
    const delta: float3 = float3(sphere.x, sphere.y, sphere.z) - point;
    const direction: float3 = Unit(delta);
    const attenuation: f32 = LocalAttenuation(direction, Dot3(delta, delta), sphere.w,
        Read4(parameters, offset + 8), Read4(parameters, offset + 12));
    const power: f32 = color.w * (color.x * 0.2126 + color.y * 0.7152 + color.z * 0.0722);
    return Min(power * attenuation * Max(Dot3(normal, direction), 0.0), 1.0e20);
}
export function Target(readonly parameters: StorageBuffer<f32>, point: float3, normal: float3, light: u32): f32 {
    // Positive support keeps temporal normalization well-defined for dark candidates.
    return Max(0.00000001, RawTarget(parameters, point, normal, light));
}
