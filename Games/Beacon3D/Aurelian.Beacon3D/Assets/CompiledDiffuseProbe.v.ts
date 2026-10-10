import { CompiledDiffuse } from "./CompiledDiffuseLighting";

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
    @location(0) position: float3;
    @location(1) normal: float3;
    @location(2) color: float4;
}
stream Resources { @binding(0) camera: CameraMaterial; }
stream Textures {
    @binding(1) shadowMap: Texture2D<float4>;
    @binding(2) shadowSampler: Sampler;
    @binding(3) diffuseData: Texture2D<float4>;
    @binding(4) diffuseSampler: Sampler;
}
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) coordinate: float2;
    @location(1) scale: f32;
}
stream Output { @target(0) color: float4; }
@vertex
function VertexMain(input: Input, resources: Resources): Varyings {
    let p: float3 = float3(input.position.x, input.position.z, 0.0);
    return { position: float4(ProjectRow(resources.camera.clipX, p), ProjectRow(resources.camera.clipY, p),
        ProjectRow(resources.camera.clipZ, p), ProjectRow(resources.camera.clipW, p)),
        coordinate: float2(input.position.x, input.position.z), scale: input.color.x };
}
function ProjectRow(row: float4, p: float3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
@pixel
function PixelMain(input: Varyings, resources: Resources, textures: Textures): Output {
    let domain: float4 = Sample(textures.diffuseData, textures.diffuseSampler, float2(0.5 / 32.0, 0.0));
    let header: float4 = Sample(textures.diffuseData, textures.diffuseSampler, float2(1.5 / 32.0, 0.0));
    let world: float3 = float3(domain.x + (input.coordinate.x + 1.0) / domain.z,
        header.x, domain.y + (input.coordinate.y + 1.0) / domain.w);
    let response: float4 = CompiledDiffuse(textures.diffuseData, textures.diffuseSampler, world,
        float3(0.0, 1.0, 0.0), resources.camera.sun.w, resources.camera.sky.w,
        float3(resources.camera.sun.x, resources.camera.sun.y, resources.camera.sun.z));
    return { color: float4(response.x * input.scale, response.y * input.scale, response.z * input.scale, response.w) };
}
