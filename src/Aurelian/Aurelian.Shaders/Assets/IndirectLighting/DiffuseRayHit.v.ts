// VulkanRayQueryScene's retained graphics packet: four float4 lanes, four packets per image row.
export function ReadRayHit(map: Texture2D<float4>, sampler: Sampler, index: f32, lane: f32, capacity: f32): float4 {
    const row: f32 = Floor(index / 4.0);
    const packetColumn: f32 = index - row * 4.0;
    return Sample(map, sampler, float2((packetColumn * 4.0 + lane + 0.5) / 16.0,
        (row + 0.5) / Floor((capacity + 3.0) / 4.0)));
}
