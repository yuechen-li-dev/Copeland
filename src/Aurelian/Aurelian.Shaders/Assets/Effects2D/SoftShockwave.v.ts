@space(object.position)
type ObjectPosition3 = float3;

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record SoftShockwaveMaterial {
    color: float4;
    age: f32;
    lifetime: f32;
    radius: f32;
    thickness: f32;
    intensity: f32;
    seed: f32;
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

stream ShockwaveVaryings {
    @builtin(position)
    position: ClipPosition4;
    @location(0)
    local: float2;
}

stream ShockwaveOutput {
    @target(0)
    color: float4;
}

stream ShockwaveResources {
    @binding(0)
    material: SoftShockwaveMaterial;
}

function SoftRing(distance: f32, radius: f32, thickness: f32): f32 {
    const ringDistance: f32 = Abs(distance - radius);
    const normalized: f32 = 1.0 - (ringDistance / thickness);
    const coverage: f32 = Clamp(normalized, 0.0, 1.0);
    return coverage * coverage * (3.0 - (2.0 * coverage));
}

@vertex
function VertexMain(input: VertexInput, builtins: VertexBuiltins): ShockwaveVaryings {
    const vertexBias: f32 = Convert<f32>(builtins.vertexId + builtins.instanceId) * 0.000001;
    return {
        position: float4(input.position.x + vertexBias, input.position.y, input.position.z, 1.0),
        local: input.local,
    };
}

@pixel
function PixelMain(input: ShockwaveVaryings, resources: ShockwaveResources): ShockwaveOutput {
    const x: f32 = input.local.x - 0.5;
    const y: f32 = input.local.y - 0.5;
    const distance: f32 = Sqrt((x * x) + (y * y));
    const progress: f32 = Clamp(resources.material.age / resources.material.lifetime, 0.0, 1.0);
    const seedVariation: f32 = Clamp(resources.material.seed * 0.000001, 0.0, 0.08);
    const ring: f32 = SoftRing(
        distance,
        (resources.material.radius * progress) + seedVariation,
        resources.material.thickness);
    const fade: f32 = 1.0 - progress;
    const alpha: f32 = resources.material.color.w * ring * fade * resources.material.intensity;
    return {
        color: float4(
            resources.material.color.x,
            resources.material.color.y,
            resources.material.color.z,
            Clamp(alpha, 0.0, 1.0)),
    };
}
