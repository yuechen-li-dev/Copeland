import { ReadRayHit } from "./DiffuseRayHit";
@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record ProbeMaterial {
    parameters: float4;
    batch: float4;
    lightPosition: float4;
    lightColor: float4;
    gridMinimum: float4;
    gridMaximum: float4;
    gridSize: float4;
    sky: float4;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}
stream Resources {
    @binding(0) material: ProbeMaterial;
    @binding(1) hits: Texture2D<float4>;
    @binding(2) hitsSampler: Sampler;
}
stream Output { @target(0) color: float4; }
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const site: f32 = Floor(input.uv.y * resources.material.batch.y);
    var mask: f32 = 0.0;
    for (var corner: u32 = 0; corner < 8; corner = corner + 1) {
        const index: f32 = site * 8.0 + Convert<f32>(corner);
        const metadata: float4 = ReadRayHit(resources.hits, resources.hitsSampler, index, 0.0, resources.material.parameters.z);
        if (metadata.z < 0.5) {
            mask = mask + Pow(2.0, Convert<f32>(corner));
        }
    }
    return { color: float4(mask, 0.0, 0.0, resources.material.parameters.w) };
}
