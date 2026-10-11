@space(object.position)
type ObjectPosition3 = float3;

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(2)
record SemanticFogMaterial {
    tint: float4;
    unexploredOpacity: f32;
    exploredOpacity: f32;
    edgeSoftness: f32;
    noiseAmount: f32;
    temporalPhase: f32;
}

stream VertexInput {
    @location(0)
    position: ObjectPosition3;
    @location(1)
    uv: float2;
}

stream VertexBuiltins {
    @builtin(vertex_id)
    vertexId: u32;
    @builtin(instance_id)
    instanceId: u32;
}

stream FogVaryings {
    @builtin(position)
    position: ClipPosition4;
    @location(0)
    uv: float2;
}

stream FogOutput {
    @target(0)
    color: float4;
}

stream FogResources {
    @binding(0)
    visibilityField: Texture2D<float4>;
    @binding(1)
    linearSampler: Sampler;
    @binding(2)
    material: SemanticFogMaterial;
}

function SmoothUnit(value: f32): f32 {
    const bounded: f32 = Clamp(value, 0.0, 1.0);
    return bounded * bounded * (3.0 - (2.0 * bounded));
}

@vertex
function VertexMain(input: VertexInput, builtins: VertexBuiltins): FogVaryings {
    const vertexBias: f32 = Convert<f32>(builtins.vertexId + builtins.instanceId) * 0.000001;
    return {
        position: float4(input.position.x + vertexBias, input.position.y, input.position.z, 1.0),
        uv: input.uv,
    };
}

@pixel
function PixelMain(input: FogVaryings, resources: FogResources): FogOutput {
    const sampleValue: f32 = Sample(resources.visibilityField, resources.linearSampler, input.uv).x;
    const shapedField: f32 = Clamp(
        ((sampleValue - 0.5) * resources.material.edgeSoftness) + 0.5,
        0.0,
        1.0);
    const exploredMix: f32 = SmoothUnit(shapedField * 2.0);
    const visibleMix: f32 = SmoothUnit((shapedField - 0.5) * 2.0);
    const exploredOpacity: f32 = resources.material.unexploredOpacity
        + (exploredMix * (resources.material.exploredOpacity - resources.material.unexploredOpacity));
    const wave: f32 = (input.uv.x * 17.0) + (input.uv.y * 29.0) + resources.material.temporalPhase;
    const triangleWave: f32 = Abs((wave - Floor(wave)) - 0.5) * 2.0;
    const noise: f32 = (triangleWave - 0.5) * resources.material.noiseAmount;
    const opacity: f32 = Clamp((exploredOpacity * (1.0 - visibleMix)) + noise, 0.0, 1.0);
    return {
        color: float4(
            resources.material.tint.x,
            resources.material.tint.y,
            resources.material.tint.z,
            opacity * resources.material.tint.w),
    };
}
