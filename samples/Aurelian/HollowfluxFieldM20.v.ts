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
    const reconstructedHeight: f32 = (field.x - 0.5) / safeCoverage + 0.5;
    const foam: f32 = field.y / safeCoverage;
    const charge: f32 = field.w / safeCoverage;
    return {
        color: float4(reconstructedHeight * wet, foam * wet, charge * wet, wet),
    };
}
