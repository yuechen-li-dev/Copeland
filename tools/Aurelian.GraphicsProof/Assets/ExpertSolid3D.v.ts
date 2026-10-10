import { Unit, ShadowVisibility, Dot3, Add3, Scale3 } from "./Lighting3D";
import { PredictLighting } from "./ExpertDecoder";
import { ExpertChoice } from "./ExpertChoice";

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
stream Input {
    @location(0) position: WorldPosition3;
    @location(1) normal: float3;
    @location(2) color: float4;
}
stream Resources {
    @binding(0) camera: CameraMaterial;
}
stream Shadows {
    @binding(1) shadowMap: Texture2D<float4>;
    @binding(2) shadowSampler: Sampler;
}
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) color: float4;
    @location(1) world: float3;
    @location(2) normal: float3;
}
stream Output {
    @target(0) color: float4;
}
function Project(row: float4, p: WorldPosition3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
@vertex
function VertexMain(input: Input, resources: Resources): Varyings {
    return {
        position: float4(Project(resources.camera.clipX, input.position), Project(resources.camera.clipY, input.position),
            Project(resources.camera.clipZ, input.position), Project(resources.camera.clipW, input.position)),
        color: input.color,
        world: float3(input.position.x, input.position.y, input.position.z),
        normal: input.normal
    };
}
@pixel
function PixelMain(input: Varyings, resources: Resources, shadows: Shadows): Output {
    let normal: float3 = Unit(input.normal);
    let light: float3 = Unit(float3(resources.camera.light.x, resources.camera.light.y, resources.camera.light.z));
    let p: WorldPosition3 = float3(input.world.x, input.world.y, input.world.z);
    let projected: float3 = float3(Project(resources.camera.shadowX, p), Project(resources.camera.shadowY, p), Project(resources.camera.shadowZ, p));
    let cosine: f32 = Max(Dot3(normal, light), 0.0);
    let visibility: f32 = ShadowVisibility(shadows.shadowMap, shadows.shadowSampler, projected, resources.camera.shadowParameters, cosine);
    let base: float3 = float3(input.color.x, input.color.y, input.color.z);
    var radiance: float3 = Scale3(base, cosine * visibility * resources.camera.sun.w / 3.14159265);
    // The experimental host supplies lamp's linear coefficient in camera.sky.x.
    // Only the declared visible receiver uses the expert; other surfaces retain direct lighting.
    if (normal.y > 0.9 && Abs(input.world.y) < 0.001 && Abs(input.world.x) <= 2.7 && Abs(input.world.z) <= 2.7) {
        radiance = Add3(radiance, PredictLighting(float2(input.world.x / 2.7, input.world.z / 2.7),
            ExpertChoice(), resources.camera.sun.w, resources.camera.sky.x));
    }
    return { color: float4(radiance.x, radiance.y, radiance.z, 1.0) };
}
