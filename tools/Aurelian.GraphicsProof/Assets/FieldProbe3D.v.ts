import { TraceField } from "./FieldTrace3D";
import { TraceBudget } from "./FieldProbeBudget";
import { Unit, Sub3, ShadowVisibility, Dot3, Scale3, Mul3, DirectLight, HemisphereLight, Add3 } from "./Lighting3D";

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
}
stream VertexInput {
    @location(0) position: WorldPosition3;
    @location(1) normal: float3;
    @location(2) color: float4;
}
stream CameraResources {
    @binding(0) camera: CameraMaterial;
}
stream SolidShadowResources {
    @binding(1) shadowMap: Texture2D<float4>;
    @binding(2) shadowSampler: Sampler;
}
stream SolidVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) color: float4;
    @location(1) world: float3;
    @location(2) normal: float3;
}
stream SolidOutput { @target(0) color: float4; }
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
        color: color, world: float3(input.position.x, input.position.y, input.position.z), normal: input.normal };
}
enum LightingChoice { Basic(color: float4), Pbr }
function ChooseLighting(enabled: f32, color: float4): LightingChoice {
    if (enabled < 0.5) {
        return LightingChoice.Basic(color);
    }
    return LightingChoice.Pbr;
}
function ShadeSolid(input: SolidVaryings, resources: CameraResources, shadows: SolidShadowResources): float4 {
    const normal: float3 = Unit(input.normal);
    const light: float3 = Unit(float3(resources.camera.light.x, resources.camera.light.y, resources.camera.light.z));
    const trace: float4 = TraceField(Add3(input.world, Scale3(normal, 0.004)), light, 48.0, TraceBudget());
    if (trace.x == 1.0) {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    if (trace.x == 2.0) {
        return float4(1.0, 1.0, 1.0, 1.0);
    }
    const p: WorldPosition3 = float3(input.world.x, input.world.y, input.world.z);
    const projected: float3 = float3(ProjectRow(resources.camera.shadowX, p), ProjectRow(resources.camera.shadowY, p), ProjectRow(resources.camera.shadowZ, p));
    const fallback: f32 = ShadowVisibility(shadows.shadowMap, shadows.shadowSampler, projected, resources.camera.shadowParameters, 1.0);
    return float4(1.0, fallback * 0.5, 1.0, 1.0);
}
@pixel
function PixelMain(input: SolidVaryings, resources: CameraResources, shadows: SolidShadowResources): SolidOutput {
    return { color: match ChooseLighting(resources.camera.surface.z, input.color) {
        LightingChoice.Basic(payload) => payload.color,
        LightingChoice.Pbr => ShadeSolid(input, resources, shadows),
    } };
}
