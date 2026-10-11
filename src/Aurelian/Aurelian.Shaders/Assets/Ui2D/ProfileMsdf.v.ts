@space(object.position)
type ObjectPosition3 = float3;

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(2)
record ProfileMsdfMaterial {
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

stream ProfileVaryings {
    @builtin(position)
    position: ClipPosition4;
    @location(0)
    uv: float2;
    @location(1)
    fieldScale: f32;
}

stream ProfileOutput {
    @target(0)
    color: float4;
}

stream ProfileResources {
    @binding(0)
    atlas: Texture2D<float4>;
    @binding(1)
    linearSampler: Sampler;
    @binding(2)
    material: ProfileMsdfMaterial;
}

function Median3(a: f32, b: f32, c: f32): f32 {
    return Max(Min(a, b), Min(Max(a, b), c));
}

function ScreenSpaceCoverage(distance: f32, threshold: f32): f32 {
    // The field stores normalized signed distance with its contour at 0.5.
    // Fwidth therefore reports normalized field distance per screen pixel.
    // A ramp of that complete width realizes approximately one pixel of AA
    // independently of field resolution or projected Profile scale.
    const width: f32 = Max(Fwidth(distance), 0.000001);
    const t: f32 = Clamp((distance - (threshold - (width * 0.5))) / width, 0.0, 1.0);
    return t * t * (3.0 - (2.0 * t));
}

@vertex
function VertexMain(input: VertexInput, builtins: VertexBuiltins): ProfileVaryings {
    const vertexBias: f32 = Convert<f32>(builtins.vertexId + builtins.instanceId) * 0.000001;
    return {
        position: float4(input.position.x + vertexBias, input.position.y, input.position.z, 1.0),
        uv: input.uv,
        fieldScale: input.fieldScale,
    };
}

@pixel
function PixelMain(input: ProfileVaryings, resources: ProfileResources): ProfileOutput {
    const sample: float4 = Sample(resources.atlas, resources.linearSampler, input.uv);
    const distance: f32 = Median3(sample.x, sample.y, sample.z);
    const coverage: f32 = ScreenSpaceCoverage(distance, resources.material.threshold);
    return {
        color: float4(
            resources.material.tint.x,
            resources.material.tint.y,
            resources.material.tint.z,
            resources.material.tint.w * coverage),
    };
}
