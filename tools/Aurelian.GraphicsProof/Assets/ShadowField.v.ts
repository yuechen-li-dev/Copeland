import { DistanceFieldShadowVisibility } from "./ShadowDistanceField";

export function ExperimentalVisibility(field: Texture2D<float4>, sampler: Sampler, uv: float2, depthVisibility: f32): f32 {
    return DistanceFieldShadowVisibility(Sample(field, sampler, uv));
}
