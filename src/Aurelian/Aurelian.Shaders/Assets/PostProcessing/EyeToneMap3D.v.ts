import { MapDisplay } from "./ToneMapColor3D";
import { ApplyRodCone } from "./RodCone3D";

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record OutputMaterial { parameters: float4; lowLight: float4; }
stream OutputResources {
    @binding(0) output: OutputMaterial;
    @binding(1) hdr: Texture2D<float4>;
    @binding(2) hdrSampler: Sampler;
    @binding(3) bloom: Texture2D<float4>;
    @binding(4) bloomSampler: Sampler;
    @binding(5) eye: StorageBuffer<float4>;
}
stream OutputInput { @location(0) position: float2; }
stream OutputVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream OutputColor { @target(0) color: float4; }
@vertex
function VertexMain(input: OutputInput): OutputVaryings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}
@pixel
function PixelMain(input: OutputVaryings, resources: OutputResources): OutputColor {
    let sampledColor: float4 = Sample(resources.hdr, resources.hdrSampler, input.uv);
    let glow: float4 = Sample(resources.bloom, resources.bloomSampler, input.uv);
    let parameters: float4 = resources.output.parameters;
    let gain: f32 = parameters.x * Pow(2.0, resources.eye[0].x);
    let lowLight: float4 = resources.output.lowLight;
    if (lowLight.x > 0.0) {
        let inputColor: float3 = float3(sampledColor.x + glow.x * parameters.w,
            sampledColor.y + glow.y * parameters.w, sampledColor.z + glow.z * parameters.w);
        let adapted: float3 = ApplyRodCone(inputColor, Pow(2.0, resources.eye[1].x), lowLight.x, lowLight.y);
        return { color: MapDisplay(float4(adapted.x, adapted.y, adapted.z, 1.0), glow,
            float4(gain, parameters.y, parameters.z, 0.0)) };
    }
    return { color: MapDisplay(sampledColor, glow, float4(gain, parameters.y, parameters.z, parameters.w)) };
}
