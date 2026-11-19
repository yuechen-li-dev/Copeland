import { MapDisplay } from "./ToneMapColor3D";

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record OutputMaterial { parameters: float4; }
stream OutputResources {
    @binding(0) output: OutputMaterial;
    @binding(1) hdr: Texture2D<float4>;
    @binding(2) hdrSampler: Sampler;
    @binding(3) bloom: Texture2D<float4>;
    @binding(4) bloomSampler: Sampler;
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
    return { color: MapDisplay(sampledColor, glow, resources.output.parameters) };
}
