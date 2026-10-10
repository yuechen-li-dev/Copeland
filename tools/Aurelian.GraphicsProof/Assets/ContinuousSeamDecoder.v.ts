import { ContinuousSeamDifference } from "./ContinuousExpertWeights";

export function PredictLighting(p: float2, mode: f32, sun: f32, lamp: f32): float3 {
    return ContinuousSeamDifference(p);
}
