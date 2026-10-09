import { SoftFieldShadow } from "./FieldTrace3D";

export function ApplyFieldVisibility(world: float3, normal: float3, light: float3, raster: f32): f32 {
    return SoftFieldShadow(world, normal, light, raster);
}
