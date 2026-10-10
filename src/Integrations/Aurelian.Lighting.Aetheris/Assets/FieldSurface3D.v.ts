import { FieldWorld } from "./AetherisField";
import { SurfaceAlbedo, SurfaceEmission } from "./LightingSurfaces";
import { TraceField, TraceFieldWithTolerance } from "./FieldTrace3D";
import { SurfaceHitTolerance } from "./FieldSurfacePrecision";
import { Add3, Scale3, Dot3, Unit } from "./Lighting3D";

export function TraceSurfaceField(origin: float3, direction: float3, maximum: f32, budget: u32): float4 {
    return TraceFieldWithTolerance(origin, direction, maximum, budget, SurfaceHitTolerance());
}

export function FieldNormal(p: float3): float3 {
    const e: f32 = 0.001;
    return Unit(float3(
        FieldWorld(float3(p.x + e, p.y, p.z)) - FieldWorld(float3(p.x - e, p.y, p.z)),
        FieldWorld(float3(p.x, p.y + e, p.z)) - FieldWorld(float3(p.x, p.y - e, p.z)),
        FieldWorld(float3(p.x, p.y, p.z + e)) - FieldWorld(float3(p.x, p.y, p.z - e))));
}

// Lambertian outgoing radiance for a unit white directional light plus authored emission.
// Alpha is resolution, never an implicit sky miss when tracing is exhausted.
export function SurfaceRadiance(p: float3, light: float3, budget: u32): float4 {
    const normal: float3 = FieldNormal(p);
    const visibility: float4 = TraceField(Add3(p, Scale3(normal, 0.004)), light, 48.0, budget);
    const albedo: float3 = SurfaceAlbedo(p);
    const emission: float3 = SurfaceEmission(p);
    var direct: f32 = 0.0;
    var resolved: f32 = 1.0;
    if (visibility.x == 0.0) {
        resolved = 0.0;
    }
    if (visibility.x == 2.0) {
        direct = Max(Dot3(normal, light), 0.0) / 3.14159265;
    }
    return float4(emission.x + albedo.x * direct, emission.y + albedo.y * direct,
        emission.z + albedo.z * direct, resolved);
}
