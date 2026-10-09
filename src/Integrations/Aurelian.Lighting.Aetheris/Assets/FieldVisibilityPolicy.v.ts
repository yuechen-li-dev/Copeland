import { FieldShadow } from "./FieldTrace3D";

export function ApplyFieldVisibility(world: float3, normal: float3, light: float3, raster: f32): f32 {
    return FieldShadow(world, normal, light, raster);
}
