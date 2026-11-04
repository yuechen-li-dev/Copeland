// Shared source orientation. Atan2 avoids a separate compute-only acos intrinsic.
export function EnvironmentUv(direction: float3): float2 {
    return float2(Clamp(Atan2(direction.z, direction.x) / 6.2831853 + 0.5, 0.0, 1.0),
        Clamp(Atan2(Sqrt(Max(1.0 - direction.y * direction.y, 0.0)), Clamp(direction.y, -1.0, 1.0)) / 3.14159265, 0.0, 1.0));
}
