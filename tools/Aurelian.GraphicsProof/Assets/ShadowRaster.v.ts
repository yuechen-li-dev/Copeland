const Texel: f32 = static (1.0 / 64.0);

export function ExperimentalVisibility(field: Texture2D<float4>, sampler: Sampler, uv: float2, depthVisibility: f32): f32 {
    const a: float4 = Sample(field, sampler, float2(uv.x - Texel, uv.y - Texel));
    const b: float4 = Sample(field, sampler, float2(uv.x, uv.y - Texel));
    const c: float4 = Sample(field, sampler, float2(uv.x + Texel, uv.y - Texel));
    const d: float4 = Sample(field, sampler, float2(uv.x - Texel, uv.y));
    const e: float4 = Sample(field, sampler, uv);
    const f: float4 = Sample(field, sampler, float2(uv.x + Texel, uv.y));
    const g: float4 = Sample(field, sampler, float2(uv.x - Texel, uv.y + Texel));
    const h: float4 = Sample(field, sampler, float2(uv.x, uv.y + Texel));
    const i: float4 = Sample(field, sampler, float2(uv.x + Texel, uv.y + Texel));
    return 1.0 - (a.x + b.x + c.x + d.x + e.x + f.x + g.x + h.x + i.x) / 9.0;
}
