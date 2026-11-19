import { SurfaceData } from "./SurfaceMaterial3D";
import { Add3, Dot3, Mul3, Scale3, Sub3, Unit } from "./Lighting3D";

export function ResidentMaterialKey(readonly surfaces: StorageBuffer<f32>, surfaceKey: f32, range: u32): f32 {
    if (range != 0) { return surfaces[range]; }
    return surfaceKey;
}

function ReadAxis(readonly surfaces: StorageBuffer<f32>, offset: u32): float3 {
    return float3(surfaces[offset], surfaces[offset + 1], surfaces[offset + 2]);
}

function ProjectorWeight(readonly surfaces: StorageBuffer<f32>, offset: u32, world: float3,
    receiverNormal: float3, axis: float3): f32 {
    let facing: f32 = Clamp((Dot3(receiverNormal, axis) - 0.35) * 4.0, 0.0, 1.0);
    if (facing <= 0.0) { return 0.0; }
    let delta: float3 = Sub3(world, ReadAxis(surfaces, offset + 12));
    let depth: f32 = Abs(Dot3(axis, delta) * surfaces[offset + 11]);
    if (depth >= 1.0) { return 0.0; }
    let x: f32 = Dot3(ReadAxis(surfaces, offset), delta) * surfaces[offset + 3];
    let y: f32 = Dot3(ReadAxis(surfaces, offset + 4), delta) * surfaces[offset + 7];
    let radial: f32 = Clamp((1.0 - x * x - y * y) * 5.0, 0.0, 1.0);
    let depthWeight: f32 = Clamp((1.0 - depth) * 5.0, 0.0, 1.0);
    return surfaces[offset + 15] * radial * radial * (3.0 - 2.0 * radial) * depthWeight * facing;
}

export function ApplySurfaceDecals(readonly surfaces: StorageBuffer<f32>, range: u32, world: float3,
    geometric: float3, frontFace: bool, surface: SurfaceData): SurfaceData {
    if (range == 0) { return surface; }
    var receiverNormal: float3 = Unit(geometric);
    if (!frontFace) { receiverNormal = Scale3(receiverNormal, -1.0); }
    var rgba: float4 = surface.color;
    var normal: float3 = float3(surface.normal.x, surface.normal.y, surface.normal.z);
    let count: f32 = surfaces[range + 1];
    static for (let index of Indices<16>()) {
        if (Convert<f32>(index) < count) {
            let offset: u32 = range + 2 + index * 21;
            let axis: float3 = ReadAxis(surfaces, offset + 8);
            let weight: f32 = ProjectorWeight(surfaces, offset, world, receiverNormal, axis);
            if (weight > 0.0) {
                let tint: float3 = ReadAxis(surfaces, offset + 16);
                let multiplier: float3 = Add3(float3(1.0 - weight, 1.0 - weight, 1.0 - weight), Scale3(tint, weight));
                let rgb: float3 = Mul3(float3(rgba.x, rgba.y, rgba.z), multiplier);
                let targetRoughness: f32 = surfaces[offset + 19];
                var roughness: f32 = rgba.w;
                if (targetRoughness > 0.0) { roughness = roughness + (targetRoughness - roughness) * weight; }
                rgba = float4(rgb.x, rgb.y, rgb.z, roughness);
                let flattening: f32 = surfaces[offset + 20] * weight;
                if (flattening > 0.0) {
                    normal = Unit(Add3(Scale3(normal, 1.0 - flattening), Scale3(axis, flattening)));
                }
            }
        }
    }
    return { color: rgba, normal: float4(normal.x, normal.y, normal.z, surface.normal.w),
        emission: surface.emission, subsurface: surface.subsurface };
}
