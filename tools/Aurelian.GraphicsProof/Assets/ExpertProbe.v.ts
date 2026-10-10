import { SceneLighting } from "./SceneExpert";

@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record BakeMaterial {
    origin: float4;
    axisU: float4;
    axisV: float4;
    parameters: float4;
    light: float4;
    eye: float4;
}
stream Resources {
    @binding(0) bake: BakeMaterial;
}
stream Input {
    @location(0) position: float2;
}
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output {
    @target(0) color: float4;
}
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5)
    };
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    let normalized: float2 = float2(input.uv.x * 2.0 - 1.0, input.uv.y * 2.0 - 1.0);
    let radiance: float3 = SceneLighting(normalized, resources.bake.parameters.x, resources.bake.light.x, resources.bake.light.y);
    return { color: float4(radiance.x, radiance.y, radiance.z, 1.0) };
}
