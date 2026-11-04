@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial { parameters: float4; }
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) head: Texture2D<float4>;
    @binding(2) headSampler: Sampler;
    @binding(3) tail: Texture2D<float4>;
    @binding(4) tailSampler: Sampler;
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
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}

@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    let head: float4 = Sample(resources.head, resources.headSampler, input.uv);
    let tail: float4 = Sample(resources.tail, resources.tailSampler, input.uv);
    return { color: float4(Min(head.x + tail.x, 65504.0), Min(head.y + tail.y, 65504.0), Min(head.z + tail.z, 65504.0), head.w) };
}
