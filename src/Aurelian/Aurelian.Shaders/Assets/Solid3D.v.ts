@space(world.position)
type WorldPosition3 = float3;

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record CameraMaterial {
    clipX: float4;
    clipY: float4;
    clipZ: float4;
    clipW: float4;
    light: float4;
}

stream VertexInput {
    @location(0)
    position: WorldPosition3;
    @location(1)
    normal: float3;
    @location(2)
    color: float4;
}

stream CameraResources {
    @binding(0)
    camera: CameraMaterial;
}

stream SolidVaryings {
    @builtin(position)
    position: ClipPosition4;
    @location(0)
    color: float4;
}

stream SolidOutput {
    @target(0)
    color: float4;
}

function ProjectRow(row: float4, p: WorldPosition3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}

@vertex
function VertexMain(input: VertexInput, resources: CameraResources): SolidVaryings {
    const light: f32 = resources.camera.light.w + (1.0 - resources.camera.light.w) * Max(
        input.normal.x * resources.camera.light.x + input.normal.y * resources.camera.light.y + input.normal.z * resources.camera.light.z,
        0.0);
    return {
        position: float4(
            ProjectRow(resources.camera.clipX, input.position),
            ProjectRow(resources.camera.clipY, input.position),
            ProjectRow(resources.camera.clipZ, input.position),
            ProjectRow(resources.camera.clipW, input.position)),
        color: input.color * float4(light, light, light, 1.0),
    };
}

@pixel
function PixelMain(input: SolidVaryings): SolidOutput {
    return { color: input.color };
}
