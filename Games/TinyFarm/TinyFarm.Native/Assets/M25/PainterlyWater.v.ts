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
    const safeCoverage: f32 = wet * 0.57 + 0.43;
    const height: f32 = (field.x - 0.5) / safeCoverage + 0.5;
    const foam: f32 = field.y / safeCoverage;
    const charge: f32 = field.w / safeCoverage;
    const red: f32 = 0.10 + height * 0.08 + charge * 0.36 + foam * 0.35;
    const green: f32 = 0.34 + height * 0.10 + charge * 0.30 + foam * 0.48;
    const blue: f32 = 0.48 + height * 0.16 + charge * 0.45 + foam * 0.42;
    return {
        color: float4(red, green, blue, wet * 0.94) * resources.material.tint,
    };
}
