import { WorldAt } from "./SurfaceLighting";
import { Unit, Sub3, Dot3 } from "./Lighting3D";

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
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) depth: Texture2D<float4>;
    @binding(2) depthSampler: Sampler;
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

function PixelUv(resources: Resources, uv: float2): float2 {
    const texel: float4 = resources.material.parameters;
    return float2((Floor(uv.x / texel.x) + 0.5) * texel.x,
        (Floor(uv.y / texel.y) + 0.5) * texel.y);
}
function Position(resources: Resources, uv: float2, depth: f32): float3 {
    return WorldAt(PixelUv(resources, uv), depth, resources.material.inverseX, resources.material.inverseY,
        resources.material.inverseZ, resources.material.inverseW);
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const depth: float4 = Sample(resources.depth, resources.depthSampler, PixelUv(resources, input.uv));
    if (depth.w >= 1.0 || resources.material.parameters.w <= 0.0) {
        return { color: float4(1.0, depth.w, 0.0, 1.0) };
    }
    const n: float4 = Sample(resources.normal, resources.normalSampler, PixelUv(resources, input.uv));
    const normal: float3 = Unit(float3(n.x, n.y, n.z));
    const position: float3 = Position(resources, input.uv, depth.w);
    const adjacent: float3 = Position(resources, float2(input.uv.x + resources.material.parameters.x, input.uv.y), depth.w);
    const spacing: float3 = Sub3(adjacent, position);
    const radius: f32 = resources.material.parameters.z;
    const pixels: f32 = Clamp(radius / Sqrt(Max(Dot3(spacing, spacing), 0.0000001)), 2.0, 96.0);
    var occlusion: f32 = 0.0;
    for (var ray: u32 = 0; ray < 8; ray = ray + 1) {
        const angle: f32 = (Convert<f32>(ray) + 0.5) * 0.78539816;
        var horizon: f32 = 0.0;
        for (var step: u32 = 0; step < 4; step = step + 1) {
            const distance: f32 = (Convert<f32>(step) + 1.0) / 4.0;
            const uv: float2 = float2(input.uv.x + Cos(angle) * pixels * distance * distance * resources.material.parameters.x,
                input.uv.y + Sin(angle) * pixels * distance * distance * resources.material.parameters.y);
            if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) {
                const sample: float4 = Sample(resources.depth, resources.depthSampler, PixelUv(resources, uv));
                if (sample.w < 1.0) {
                    const delta: float3 = Sub3(Position(resources, uv, sample.w), position);
                    const squared: f32 = Dot3(delta, delta);
                    const falloff: f32 = Max(1.0 - squared / (radius * radius), 0.0);
                    const elevation: f32 = Max(Dot3(normal, delta) / Sqrt(Max(squared, 0.000001)) - 0.08, 0.0);
                    // Cosine-weighted blocked energy scales with sin(horizon)^2.
                    // A linear elevation over-darkens shallow, distant contacts.
                    horizon = Max(horizon, elevation * elevation * falloff);
                }
            }
        }
        occlusion = occlusion + horizon;
    }
    // Average all eight azimuth samples once. Dividing by four doubled AO.
    const visibility: f32 = Clamp(1.0 - occlusion * resources.material.parameters.w / 8.0, 0.0, 1.0);
    return { color: float4(visibility, depth.w, 0.0, 1.0) };
}
