
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
    @binding(3) diffuse: Texture2D<float4>;
    @binding(4) diffuseSampler: Sampler;
    @binding(5) blurred: Texture2D<float4>;
    @binding(6) blurredSampler: Sampler;
    @binding(7) profile: Texture2D<float4>;
    @binding(8) profileSampler: Sampler;
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
    const original: float4 = Sample(resources.diffuse, resources.diffuseSampler, input.uv);
    const blurred: float4 = Sample(resources.blurred, resources.blurredSampler, input.uv);
    const profile: float4 = Sample(resources.profile, resources.profileSampler, input.uv);
    return { color: float4(Max(scene.x + (blurred.x - original.x) * profile.x, 0.0),
        Max(scene.y + (blurred.y - original.y) * profile.y, 0.0),
        Max(scene.z + (blurred.z - original.z) * profile.z, 0.0), scene.w) };
}
