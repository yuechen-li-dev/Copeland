import { SliceEnd } from "./VolumeLighting";
@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    grid: float4;
    depth: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) source: Texture2D<float4>;
    @binding(2) sourceSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output { @target(0) color: float4; }
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}

@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    let grid: float4 = resources.material.grid;
    let depth: float4 = resources.material.depth;
    let slice: f32 = Floor(input.uv.y * grid.z);
    let uv: float2 = float2(input.uv.x, input.uv.y * grid.z - slice);
    var r: f32 = 0.0;
    var g: f32 = 0.0;
    var b: f32 = 0.0;
    var transmission: f32 = 1.0;
    var start: f32 = 0.0;
    for (var index: u32 = 0; index < 64; index = index + 1) {
        let i: f32 = Convert<f32>(index);
        if (i <= slice && i < grid.z) {
            let end: f32 = SliceEnd(i, depth);
            let source: float4 = Sample(resources.source, resources.sourceSampler, float2(uv.x, (i + uv.y) / grid.z));
            let segment: f32 = Exp(-Min(source.w * (end - start), 80.0));
            let weight: f32 = transmission * (1.0 - segment);
            r = r + source.x * weight;
            g = g + source.y * weight;
            b = b + source.z * weight;
            transmission = transmission * segment;
            start = end;
        }
    }
    return { color: float4(r, g, b, transmission) };
}
