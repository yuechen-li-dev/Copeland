import { WorldAt } from "./SurfaceLighting";
import { Sub3, Dot3 } from "./Lighting3D";

@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    inverseX: float4;
    inverseY: float4;
    inverseZ: float4;
    inverseW: float4;
    parameters: float4;
    sourceTexels: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) ao: Texture2D<float4>;
    @binding(2) aoSampler: Sampler;
    @binding(3) normal: Texture2D<float4>;
    @binding(4) normalSampler: Sampler;
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

function Position(resources: Resources, uv: float2, depth: f32): float3 {
    const texel: float4 = resources.material.sourceTexels;
    const snapped: float2 = float2((Floor(uv.x / texel.x) + 0.5) * texel.x,
        (Floor(uv.y / texel.y) + 0.5) * texel.y);
    return WorldAt(snapped, depth, resources.material.inverseX, resources.material.inverseY,
        resources.material.inverseZ, resources.material.inverseW);
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const center: float4 = Sample(resources.ao, resources.aoSampler, input.uv);
    if (center.y >= 1.0 || resources.material.parameters.w <= 0.0) {
        return { color: center };
    }
    const p: float3 = Position(resources, input.uv, center.y);
    const n: float4 = Sample(resources.normal, resources.normalSampler, input.uv);
    var total: f32 = 0.0;
    var sum: f32 = 0.0;
    for (var y: u32 = 0; y < 3; y = y + 1) {
        for (var x: u32 = 0; x < 3; x = x + 1) {
            const uv: float2 = float2(input.uv.x + (Convert<f32>(x) - 1.0) * resources.material.parameters.x,
                input.uv.y + (Convert<f32>(y) - 1.0) * resources.material.parameters.y);
            const tap: float4 = Sample(resources.ao, resources.aoSampler, uv);
            if (tap.y < 1.0) {
                const neighbor: float4 = Sample(resources.normal, resources.normalSampler, uv);
                const delta: float3 = Sub3(Position(resources, uv, tap.y), p);
                const alignment: f32 = Dot3(float3(n.x, n.y, n.z), float3(neighbor.x, neighbor.y, neighbor.z));
                const weight: f32 = Max(alignment, 0.0) / (1.0 + Dot3(delta, delta) / Max(resources.material.parameters.z * resources.material.parameters.z * 0.02, 0.000001));
                sum = sum + tap.x * weight;
                total = total + weight;
            }
        }
    }
    return { color: float4(sum / Max(total, 0.0001), center.y, 0.0, 1.0) };
}
