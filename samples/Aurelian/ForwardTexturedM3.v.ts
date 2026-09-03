@space(object.position)
type ObjectPosition3 = float3;

@space(world.position)
type WorldPosition3 = float3;

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(2)
record SurfaceMaterial {
    tint: float4;
    roughness: f32;
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

stream ForwardVaryings {
    @builtin(position)
    position: ClipPosition4;
    @location(0)
    uv: float2;
    @location(1)
    worldPosition: WorldPosition3;
}

stream PixelBuiltins {
    @builtin(front_face)
    frontFace: bool;
}

stream ForwardOutput {
    @target(0)
    color: float4;
}

stream ForwardResources {
    @binding(0)
    albedo: Texture2D<float4>;
    @binding(1)
    linearSampler: Sampler;
    @binding(2)
    material: SurfaceMaterial;
}

function EstablishWorld(value: ObjectPosition3): WorldPosition3 {
    return float3(value.x, value.y, value.z);
}

function EstablishClip(value: ObjectPosition3): ClipPosition4 {
    return float4(value.x, value.y, value.z, 1.0);
}

@vertex
function VertexMain(input: VertexInput, builtins: VertexBuiltins): ForwardVaryings {
    const vertexBias: f32 = Convert<f32>(builtins.vertexId + builtins.instanceId) * 0.000001;
    return {
        position: float4(input.position.x + vertexBias, input.position.y, input.position.z, 1.0),
        uv: input.uv,
        worldPosition: EstablishWorld(input.position),
    };
}

@pixel
function PixelMain(input: ForwardVaryings, resources: ForwardResources, builtins: PixelBuiltins): ForwardOutput {
    const texel: float4 = Sample(resources.albedo, resources.linearSampler, input.uv);
    if (builtins.frontFace) {
        return {
            color: texel * resources.material.tint,
        };
    }
    return {
        color: resources.material.tint,
    };
}
