export function TemporalProjection(current: float4, previous: float4): float4 {
    const safeW: f32 = Max(previous.w, 0.00001);
    return float4(previous.x / safeW * 0.5 + 0.5, previous.y / safeW * 0.5 + 0.5,
        previous.z / safeW, current.z / Max(current.w, 0.00001));
}
