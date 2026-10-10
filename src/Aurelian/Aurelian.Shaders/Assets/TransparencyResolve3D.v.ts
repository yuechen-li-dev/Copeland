
@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    parameters: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) scene: Texture2D<float4>;
    @binding(2) sceneSampler: Sampler;
    @binding(3) accumulation: Texture2D<float4>;
    @binding(4) accumulationSampler: Sampler;
    @binding(5) optical: Texture2D<float4>;
    @binding(6) opticalSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output {
    @target(0) color: float4;
}
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const scene: float4 = Sample(resources.scene, resources.sceneSampler, input.uv);
    const sum: float4 = Sample(resources.accumulation, resources.accumulationSampler, input.uv);
    const optical: float4 = Sample(resources.optical, resources.opticalSampler, input.uv);
    const transmission: f32 = Exp(-optical.x);
    const scale: f32 = (1.0 - transmission) / Max(sum.w, 0.000001);
    return { color: float4(scene.x * transmission + sum.x * scale,
        scene.y * transmission + sum.y * scale, scene.z * transmission + sum.z * scale, transmission) };
}
