import { TemporalProjection } from "./TemporalGeometry";
import { Unit, Sub3, ShadowVisibility, Dot3, Scale3, Mul3, DirectLight, HemisphereLight, Add3, LimitRadiance } from "./Lighting3D";

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
    eye: float4;
    sun: float4;
    sky: float4;
    ground: float4;
    surface: float4;
    shadowX: float4;
    shadowY: float4;
    shadowZ: float4;
    shadowW: float4;
    shadowParameters: float4;
    previousX: float4;
    previousY: float4;
    previousZ: float4;
    previousW: float4;
}
stream VertexInput {
    @location(3) previousPosition: WorldPosition3;
    @location(0) position: WorldPosition3;
    @location(1) normal: float3;
    @location(2) color: float4;
}
stream CameraResources {
    @binding(0) camera: CameraMaterial;
}
stream SolidVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) color: float4;
    @location(1) world: float3;
    @location(2) normal: float3;
    @location(3) currentClip: float4;
    @location(4) previousClip: float4;
}
stream SolidOutput {
    @target(0) color: float4;
    @target(1) motion: float4;
    @target(2) normal: float4;
    @target(3) emission: float4;
}
function ProjectRow(row: float4, p: WorldPosition3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
@vertex
function VertexMain(input: VertexInput, resources: CameraResources): SolidVaryings {
    var color: float4 = input.color;
    if (resources.camera.surface.z < 0.5) {
        const light: f32 = resources.camera.light.w + (1.0 - resources.camera.light.w) * Max(
            input.normal.x * resources.camera.light.x + input.normal.y * resources.camera.light.y + input.normal.z * resources.camera.light.z, 0.0);
        color = input.color * float4(light, light, light, 1.0);
    }
    return { position: float4(ProjectRow(resources.camera.clipX, input.position), ProjectRow(resources.camera.clipY, input.position),
        ProjectRow(resources.camera.clipZ, input.position), ProjectRow(resources.camera.clipW, input.position)),
        currentClip: float4(ProjectRow(resources.camera.clipX, input.position), ProjectRow(resources.camera.clipY, input.position),
            ProjectRow(resources.camera.clipZ, input.position), ProjectRow(resources.camera.clipW, input.position)),
        previousClip: float4(ProjectRow(resources.camera.previousX, input.previousPosition), ProjectRow(resources.camera.previousY, input.previousPosition),
            ProjectRow(resources.camera.previousZ, input.previousPosition), ProjectRow(resources.camera.previousW, input.previousPosition)),
        color: color, world: float3(input.position.x, input.position.y, input.position.z), normal: input.normal };
}
@pixel
function PixelMain(input: SolidVaryings, resources: CameraResources): SolidOutput {
    const n: float3 = Unit(input.normal);
    var flag: f32 = 1.0;
    if (resources.camera.surface.z < 0.5) {
        flag = -1.0;
    }
    return {
        color: float4(input.color.x, input.color.y, input.color.z, resources.camera.surface.x),
        motion: TemporalProjection(input.currentClip, input.previousClip),
        normal: float4(n.x, n.y, n.z, resources.camera.surface.y),
        emission: float4(0.0, 0.0, 0.0, flag),
    };
}
