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
    // Reconstruct the same binary wet/dry boundary with a narrow presentation edge.
    const edge: f32 = Clamp((wet - 0.48) / 0.04, 0.0, 1.0);
    const coverage: f32 = edge * edge * (3.0 - 2.0 * edge);
    const safeCoverage: f32 = wet * 0.57 + 0.43;
    const height: f32 = (field.x - 0.5) / safeCoverage + 0.5;
    const foam: f32 = field.y / safeCoverage;
    const charge: f32 = field.w / safeCoverage;
    const red: f32 = 0.10 + height * 0.08 + charge * 0.36 + foam * 0.35;
    const green: f32 = 0.34 + height * 0.10 + charge * 0.30 + foam * 0.48;
    const blue: f32 = 0.48 + height * 0.16 + charge * 0.45 + foam * 0.42;
    const brush: f32 = 0.96 + 0.025 * Sin(input.uv.y * 137.0 + Sin(input.uv.x * 83.0) * 1.8)
        + 0.015 * Sin(input.uv.y * 57.0 + input.uv.x * 13.0);
    return {
        color: float4(red * brush, green * brush, blue * brush, coverage * 0.94) * resources.material.tint,
    };
}
