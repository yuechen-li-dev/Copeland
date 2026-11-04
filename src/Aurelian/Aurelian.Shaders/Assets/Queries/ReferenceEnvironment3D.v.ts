import { ReadEnvironmentRadiance, EnvironmentSamplePdf, SampleEnvironmentDirection } from "./EnvironmentSampling3D";

export function ReferenceEnvironment(readonly image: StorageBuffer<f32>, readonly parameters: StorageBuffer<f32>, direction: float3): float3 {
    return ReadEnvironmentRadiance(image, parameters, direction);
}
export function ReferenceEnvironmentPdf(readonly parameters: StorageBuffer<f32>, direction: float3): f32 {
    return EnvironmentSamplePdf(parameters, direction);
}
export function SampleReferenceEnvironment(readonly parameters: StorageBuffer<f32>, choice: f32, u: f32, v: f32): float3 {
    return SampleEnvironmentDirection(parameters, choice, u, v);
}
