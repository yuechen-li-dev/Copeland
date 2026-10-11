@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    unused: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) scene: Texture2D<float4>;
    @binding(2) sceneSampler: Sampler;
    @binding(3) glass: Texture2D<float4>;
    @binding(4) glassSampler: Sampler;
    @binding(5) coverage: Texture2D<float4>;
    @binding(6) coverageSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output { @target(0) color: float4; }
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
    const glass: float4 = Sample(resources.glass, resources.glassSampler, input.uv);
    const coverage: float4 = Sample(resources.coverage, resources.coverageSampler, input.uv);
    if (coverage.x < 0.5) { return { color: scene }; }
    // Alpha marks refracted pixels reactive to the existing temporal resolver.
    return { color: float4(glass.x, glass.y, glass.z, 0.0) };
}
