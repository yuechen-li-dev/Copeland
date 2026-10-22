@material
@binding(2)
record SurfaceMaterial {
    tint: float4;
    roughness: f32;
}

stream VertexInput {
    @location(0)
    position: float3;
    @location(1)
    uv: float2;
}

stream FieldVaryings {
    @builtin(position)
    position: float4;
    @location(0)
    uv: float2;
}

stream FieldPixelInput {
    @location(0)
    uv: float2;
}

stream FieldOutput {
    @target(0)
    color: float4;
}

stream FieldResources {
    @binding(0)
    semanticField: Texture2D<float4>;
    @binding(1)
    linearSampler: Sampler;
    @binding(2)
    material: SurfaceMaterial;
}

@vertex
function VertexMain(input: VertexInput): FieldVaryings {
    return {
        position: float4(input.position, 1.0),
        uv: input.uv,
    };
}

@pixel
function PixelMain(input: FieldPixelInput, resources: FieldResources): FieldOutput {
    const field: float4 = Sample(resources.semanticField, resources.linearSampler, input.uv);
    const wet: f32 = field.z;
    // Blue stores signed bank distance, rasterized from the same authored geometry as gameplay.
    const edge: f32 = Clamp((wet - 0.48) / 0.04, 0.0, 1.0);
    const coverage: f32 = edge * edge * (3.0 - 2.0 * edge);
    const safeCoverage: f32 = wet * 0.57 + 0.43;
    const height: f32 = (field.x - 0.5) / safeCoverage + 0.5;
    const foam: f32 = field.y / safeCoverage;
    const charge: f32 = field.w / safeCoverage;
    const depth: f32 = Clamp((wet - 0.5) * 2.0, 0.0, 1.0);
    const shallow: f32 = 1.0 - depth;
    const ripple: f32 = Sin(input.uv.y * 197.0 + Sin(input.uv.x * 71.0) * 1.6 + height * 12.0)
        * Sin(input.uv.x * 127.0 + input.uv.y * 33.0);
    const brokenEdge: f32 = Clamp((Sin(input.uv.y * 151.0 + input.uv.x * 83.0) - 0.3) * 2.0, 0.0, 1.0);
    const edgeFoam: f32 = Clamp(1.0 - depth * 10.0, 0.0, 1.0) * brokenEdge * 0.22;
    const glint: f32 = Clamp((ripple - 0.90) * 4.0, 0.0, 1.0) * 0.025;
    const red: f32 = 0.018 + height * 0.025 + shallow * 0.14 + charge * 0.36 + foam * 0.35 + edgeFoam + glint;
    const green: f32 = 0.13 + height * 0.06 + shallow * 0.13 + charge * 0.30 + foam * 0.48 + edgeFoam + glint;
    const blue: f32 = 0.18 + height * 0.07 + shallow * 0.10 + charge * 0.45 + foam * 0.42 + edgeFoam + glint;
    const brush: f32 = 0.96 + 0.025 * Sin(input.uv.y * 137.0 + Sin(input.uv.x * 83.0) * 1.8)
        + 0.015 * Sin(input.uv.y * 57.0 + input.uv.x * 13.0);
    return {
        color: float4(red * brush, green * brush, blue * brush, coverage * 0.94) * resources.material.tint,
    };
}
