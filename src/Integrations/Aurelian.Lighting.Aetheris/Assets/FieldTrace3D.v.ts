import { FieldWorld } from "./AetherisField";
import { Add3, Scale3 } from "./Lighting3D";
import { TraceBudget } from "./FieldTraceBudget";

// Contract: normalized direction; <=48m ray; query path within +/-64m.
// x: 0 unresolved, 1 hit, 2 miss; y: travel; z: heuristic visibility; w: evaluations.
// 0.5mm hit tolerance and 0.05mm numerical margin are qualified by the proof,
// not a general guarantee for all f32 geometry and grazing rays.
export function TraceField(origin: float3, direction: float3, maximum: f32, budget: u32): float4 {
    return TraceFieldWithTolerance(origin, direction, maximum, budget, 0.0005);
}

// Radiance hits can demand tighter contact than binary shadow visibility. The step
// margin stays below both admitted tolerances; exhaustion still remains unresolved.
export function TraceFieldWithTolerance(origin: float3, direction: float3, maximum: f32, budget: u32, tolerance: f32): float4 {
    var status: f32 = 0.0;
    var travel: f32 = 0.0;
    var visibility: f32 = 1.0;
    var evaluations: f32 = 0.0;
    const directionLengthSquared: f32 = direction.x * direction.x + direction.y * direction.y + direction.z * direction.z;
    if (!(Abs(directionLengthSquared - 1.0) <= 0.0001)
        || !(tolerance >= 0.0001 && tolerance <= 0.0005)
        || !(maximum > 0.0 && maximum <= 48.0)) {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    for (var step: u32 = 0; step < 256; step = step + 1) {
        if (status == 0.0 && step < budget) {
            const samplePosition: float3 = Add3(origin, Scale3(direction, travel));
            if (!(Abs(samplePosition.x) <= 64.0 && Abs(samplePosition.y) <= 64.0 && Abs(samplePosition.z) <= 64.0)) {
                return float4(0.0, travel, 0.0, evaluations);
            }
            const distance: f32 = FieldWorld(samplePosition);
            evaluations = evaluations + 1.0;
            if (distance <= tolerance) {
                status = 1.0;
                visibility = 0.0;
            } else {
                visibility = Min(visibility, 12.0 * distance / Max(travel, 0.004));
                const advance: f32 = (distance - 0.00005) * 0.9;
                if (travel + advance >= maximum) {
                    status = 2.0;
                } else {
                    travel = travel + advance;
                }
            }
        }
    }
    return float4(status, travel, Clamp(visibility, 0.0, 1.0), evaluations);
}

export function FieldShadow(world: float3, normal: float3, light: float3, rasterFallback: f32): f32 {
    const trace: float4 = TraceField(Add3(world, Scale3(normal, 0.004)), light, 48.0, TraceBudget());
    if (trace.x == 0.0) {
        return rasterFallback;
    }
    if (trace.x == 1.0) {
        return 0.0;
    }
    return 1.0;
}

export function SoftFieldShadow(world: float3, normal: float3, light: float3, rasterFallback: f32): f32 {
    const trace: float4 = TraceField(Add3(world, Scale3(normal, 0.004)), light, 48.0, TraceBudget());
    if (trace.x == 0.0) {
        return rasterFallback;
    }
    return trace.z;
}
