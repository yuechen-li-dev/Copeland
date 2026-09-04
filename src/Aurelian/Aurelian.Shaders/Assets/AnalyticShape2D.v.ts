@space(object.position)
type ObjectPosition3 = float3;

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record AnalyticShapeMaterial {
    fillColor: float4;
    borderColor: float4;
    halfSize: float2;
    radius: f32;
    borderWidth: f32;
    shapeKind: u32;
}

stream VertexInput {
    @location(0)
    position: ObjectPosition3;
    @location(1)
    local: float2;
}

stream VertexBuiltins {
    @builtin(vertex_id)
    vertexId: u32;
    @builtin(instance_id)
    instanceId: u32;
}

stream ShapeVaryings {
    @builtin(position)
    position: ClipPosition4;
    @location(0)
    local: float2;
}

stream ShapeOutput {
    @target(0)
    color: float4;
}

stream ShapeResources {
    @binding(0)
    material: AnalyticShapeMaterial;
}

function SignedDistanceRoundedRect(pX: f32, pY: f32, halfWidth: f32, halfHeight: f32, radius: f32): f32 {
    const qX: f32 = Abs(pX) - (halfWidth - radius);
    const qY: f32 = Abs(pY) - (halfHeight - radius);
    const outsideX: f32 = Max(qX, 0.0);
    const outsideY: f32 = Max(qY, 0.0);
    const outside: f32 = Sqrt((outsideX * outsideX) + (outsideY * outsideY));
    return outside + Min(Max(qX, qY), 0.0) - radius;
}

function SignedDistanceCircle(pX: f32, pY: f32, radius: f32): f32 {
    return Sqrt((pX * pX) + (pY * pY)) - radius;
}

function CoverageFromDistance(distance: f32): f32 {
    const coverageAmount: f32 = Clamp(0.5 - distance, 0.0, 1.0);
    return coverageAmount * coverageAmount * (3.0 - (2.0 * coverageAmount));
}

@vertex
function VertexMain(input: VertexInput, builtins: VertexBuiltins): ShapeVaryings {
    const vertexBias: f32 = Convert<f32>(builtins.vertexId + builtins.instanceId) * 0.000001;
    return {
        position: float4(input.position.x + vertexBias, input.position.y, input.position.z, 1.0),
        local: input.local,
    };
}

@pixel
function PixelMain(input: ShapeVaryings, resources: ShapeResources): ShapeOutput {
    const pX: f32 = (input.local.x - 0.5) * resources.material.halfSize.x * 2.0;
    const pY: f32 = (input.local.y - 0.5) * resources.material.halfSize.y * 2.0;
    const roundedDistance: f32 = SignedDistanceRoundedRect(
        pX,
        pY,
        resources.material.halfSize.x,
        resources.material.halfSize.y,
        resources.material.radius);
    const circleDistance: f32 = SignedDistanceCircle(pX, pY, resources.material.halfSize.x);
    const circleMix: f32 = Convert<f32>(resources.material.shapeKind);
    const distance: f32 = roundedDistance + (circleMix * (circleDistance - roundedDistance));
    const coverage: f32 = CoverageFromDistance(distance);
    const borderMix: f32 = Clamp(distance + resources.material.borderWidth + 0.5, 0.0, 1.0);
    const fillMix: f32 = 1.0 - borderMix;
    return {
        color: float4(
            (resources.material.fillColor.x * fillMix) + (resources.material.borderColor.x * borderMix),
            (resources.material.fillColor.y * fillMix) + (resources.material.borderColor.y * borderMix),
            (resources.material.fillColor.z * fillMix) + (resources.material.borderColor.z * borderMix),
            ((resources.material.fillColor.w * fillMix) + (resources.material.borderColor.w * borderMix)) * coverage),
    };
}
