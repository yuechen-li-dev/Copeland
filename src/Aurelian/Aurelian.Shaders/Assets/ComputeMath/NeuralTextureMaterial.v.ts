import { DirectLightModel, Unit } from "./Lighting3D";

export function TextureDemandUv(pixel: u32, width: u32, height: u32, oblique: bool): float2 {
    let x: f32 = (Convert<f32>(pixel % width) + 0.5) / Convert<f32>(width);
    let y: f32 = (Convert<f32>(pixel / width) + 0.5) / Convert<f32>(height);
    if (oblique) {
        return float2(1.31 * x + 0.25 * y - 0.22, 0.17 * x + (0.57 + 0.46 * y) * y + 0.09);
    }
    return float2(x, y);
}

function Byte(value: f32): u32 {
    return U32(Clamp(value, 0.0, 1.0) * 255.0 + 0.5);
}

function Linear(value: f32): f32 {
    let color: f32 = Clamp(value, 0.0, 1.0);
    if (color <= 0.04045) { return color / 12.92; }
    return Pow((color + 0.055) / 1.055, 2.4);
}

function Display(value: f32): f32 {
    let color: f32 = Max(value, 0.0);
    let exposed: f32 = color / (1.0 + color);
    if (exposed <= 0.0031308) { return exposed * 12.92; }
    return 1.055 * Pow(exposed, 1.0 / 2.4) - 0.055;
}

export function WriteDecodedMaterial(readwrite output: StorageBuffer<u32>, pixel: u32,
    color: float3, xy: float2, roughness: f32, shade: bool): void {
    let offset: u32 = pixel * 3;
    output[offset] = Byte(color.x) | (Byte(color.y) << 8) | (Byte(color.z) << 16) | (Byte(roughness) << 24);
    output[offset + 1] = Byte(xy.x) | (Byte(xy.y) << 8);
    if (!shade) { return; }
    let nx: f32 = Clamp(xy.x, 0.0, 1.0) * 2.0 - 1.0;
    let ny: f32 = Clamp(xy.y, 0.0, 1.0) * 2.0 - 1.0;
    let normal: float3 = Unit(float3(nx, ny, Sqrt(Max(1.0 - nx * nx - ny * ny, 0.0))));
    let albedo: float3 = float3(Linear(color.x), Linear(color.y), Linear(color.z));
    let radiance: float3 = DirectLightModel(albedo, normal, float3(0.0, 0.0, 1.0),
        float3(0.6, 0.0, 0.8), 0.0, Clamp(roughness, 0.045, 1.0), 0.0) * 2.7 + albedo * 0.08;
    output[offset + 2] = Byte(Display(radiance.x)) | (Byte(Display(radiance.y)) << 8) | (Byte(Display(radiance.z)) << 16);
}
