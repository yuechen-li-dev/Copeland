import { RefitProbeReflection } from "./DiffuseProbeLighting";
@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record ProbeMaterial {
    parameters: float4;
    batch: float4;
    lightPosition: float4;
    lightColor: float4;
    gridMinimum: float4;
    gridMaximum: float4;
    gridSize: float4;
    sky: float4;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}
stream Resources {
    @binding(0) material: ProbeMaterial;
    @binding(1) captured: Texture2D<float4>;
    @binding(2) capturedSampler: Sampler;
    @binding(3) lighting: Texture2D<float4>;
    @binding(4) lightingSampler: Sampler;
}
stream Output { @target(0) color: float4; }
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const radiance: float4 = Sample(resources.captured, resources.capturedSampler, float2(0.25, input.uv.y));
    const reference: float4 = Sample(resources.captured, resources.capturedSampler, float2(0.75, input.uv.y));
    const current: float4 = Sample(resources.lighting, resources.lightingSampler, float2(0.5, input.uv.y));
    const result: float3 = RefitProbeReflection(float3(radiance.x, radiance.y, radiance.z),
        float3(reference.x, reference.y, reference.z), float3(current.x, current.y, current.z));
    return { color: float4(result.x, result.y, result.z, 1.0) };
}
