import { ContinuousRadiance } from "./ContinuousExpertWeights";
import { Add3, Scale3 } from "./Lighting3D";

export function PredictLighting(p: float2, mode: f32, sun: f32, lamp: f32): float3 {
    let sunValue: float3 = ContinuousRadiance(p, 0);
    let lampValue: float3 = ContinuousRadiance(p, 3);
    let sunBasis: float3 = float3(Max(sunValue.x, 0.0), Max(sunValue.y, 0.0), Max(sunValue.z, 0.0));
    let lampBasis: float3 = float3(Max(lampValue.x, 0.0), Max(lampValue.y, 0.0), Max(lampValue.z, 0.0));
    return Add3(Scale3(sunBasis, sun), Scale3(lampBasis, lamp));
}
