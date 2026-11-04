const Texel: f32 = static (1.0 / 64.0);

export function ExperimentalVisibility(field: Texture2D<float4>, sampler: Sampler, uv: float2, depthVisibility: f32): f32 {
    let a: float4 = Sample(field, sampler, float2(uv.x - Texel, uv.y - Texel));
    let b: float4 = Sample(field, sampler, float2(uv.x, uv.y - Texel));
    let c: float4 = Sample(field, sampler, float2(uv.x + Texel, uv.y - Texel));
    let d: float4 = Sample(field, sampler, float2(uv.x - Texel, uv.y));
    let e: float4 = Sample(field, sampler, uv);
    let f: float4 = Sample(field, sampler, float2(uv.x + Texel, uv.y));
    let g: float4 = Sample(field, sampler, float2(uv.x - Texel, uv.y + Texel));
    let h: float4 = Sample(field, sampler, float2(uv.x, uv.y + Texel));
    let i: float4 = Sample(field, sampler, float2(uv.x + Texel, uv.y + Texel));
    return 1.0 - (a.x + b.x + c.x + d.x + e.x + f.x + g.x + h.x + i.x) / 9.0;
}
