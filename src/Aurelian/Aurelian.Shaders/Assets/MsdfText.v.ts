@space(object.position)
type ObjectPosition3 = float3;

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(2)
record MsdfTextMaterial {
    tint: float4;
    pixelRange: f32;
    threshold: f32;
}

stream VertexInput {
    @location(0)
    position: ObjectPosition3;
    @location(1)
    uv: float2;
    @location(2)
    fieldScale: f32;
}

stream VertexBuiltins {
    @builtin(vertex_id)
    vertexId: u32;
    @builtin(instance_id)
    instanceId: u32;
}

stream TextVaryings {
    @builtin(position)
    position: ClipPosition4;
    @location(0)
    uv: float2;
    @location(1)
    fieldScale: f32;
}

stream TextOutput {
    @target(0)
    color: float4;
}

stream TextResources {
    @binding(0)
    atlas: Texture2D<float4>;
    @binding(1)
    linearSampler: Sampler;
    @binding(2)
    material: MsdfTextMaterial;
}

function Median3(a: f32, b: f32, c: f32): f32 {
    return Max(Min(a, b), Min(Max(a, b), c));
}

function SmoothCoverage(distance: f32, pixelRange: f32, fieldScale: f32, threshold: f32): f32 {
    const smoothing: f32 = 0.5 / Max(1.0, pixelRange * fieldScale);
    const t: f32 = Clamp((distance - (threshold - smoothing)) / (smoothing + smoothing), 0.0, 1.0);
    return t * t * (3.0 - (2.0 * t));
}

@vertex
function VertexMain(input: VertexInput, builtins: VertexBuiltins): TextVaryings {
    const vertexBias: f32 = Convert<f32>(builtins.vertexId + builtins.instanceId) * 0.000001;
    return {
        position: float4(input.position.x + vertexBias, input.position.y, input.position.z, 1.0),
        uv: input.uv,
        fieldScale: input.fieldScale,
    };
}

@pixel
function PixelMain(input: TextVaryings, resources: TextResources): TextOutput {
    const sample: float4 = Sample(resources.atlas, resources.linearSampler, input.uv);
    const distance: f32 = Median3(sample.x, sample.y, sample.z);
    const coverage: f32 = SmoothCoverage(
        distance,
        resources.material.pixelRange,
        input.fieldScale,
        resources.material.threshold);
    return {
        color: float4(
            resources.material.tint.x,
            resources.material.tint.y,
            resources.material.tint.z,
            resources.material.tint.w * coverage),
    };
}
