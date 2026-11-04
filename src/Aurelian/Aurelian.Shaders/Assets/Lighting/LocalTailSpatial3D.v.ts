import { Dot3, Add3, Sub3, Scale3 } from "./Lighting3D";
import { WorldAt } from "./SurfaceLighting";
@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    inverseX: float4;
    inverseY: float4;
    inverseZ: float4;
    inverseW: float4;
    previousX: float4;
    previousY: float4;
    previousZ: float4;
    previousW: float4;
    settings: float4;
    control: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) tail: Texture2D<float4>;
    @binding(2) tailSampler: Sampler;
    @binding(3) normal: Texture2D<float4>;
    @binding(4) normalSampler: Sampler;
    @binding(5) motion: Texture2D<float4>;
    @binding(6) motionSampler: Sampler;
    @binding(7) surface: Texture2D<float4>;
    @binding(8) surfaceSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output {
    @target(0) color: float4;
}
@vertex
function VertexMain(input: Input): Varyings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}

@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const depth: f32 = Sample(resources.motion, resources.motionSampler, input.uv).w;
    const center: float4 = Sample(resources.tail, resources.tailSampler, input.uv);
    if (depth >= 1.0) {
        return { color: center };
    }
    const normal: float4 = Sample(resources.normal, resources.normalSampler, input.uv);
    const surface: float4 = Sample(resources.surface, resources.surfaceSampler, input.uv);
    const point: float3 = WorldAt(input.uv, depth, resources.material.inverseX, resources.material.inverseY,
        resources.material.inverseZ, resources.material.inverseW);
    var sum: float3 = float3(0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    static for (let y of [-1.0, 0.0, 1.0]) {
        static for (let x of [-1.0, 0.0, 1.0]) {
            const uv: float2 = float2(input.uv.x + x * resources.material.settings.x * resources.material.control.y,
                input.uv.y + y * resources.material.settings.y * resources.material.control.y);
            if (uv.x >= 0.0 && uv.y >= 0.0 && uv.x < 1.0 && uv.y < 1.0) {
                const tapDepth: f32 = Sample(resources.motion, resources.motionSampler, uv).w;
                const tapNormal: float4 = Sample(resources.normal, resources.normalSampler, uv);
                const tapSurface: float4 = Sample(resources.surface, resources.surfaceSampler, uv);
                const tapPoint: float3 = WorldAt(uv, tapDepth, resources.material.inverseX, resources.material.inverseY,
                    resources.material.inverseZ, resources.material.inverseW);
                const delta: float3 = Sub3(tapPoint, point);
                const materialDelta: float3 = float3(surface.x - tapSurface.x, surface.y - tapSurface.y, surface.z - tapSurface.z);
                const alignment: f32 = Dot3(float3(normal.x, normal.y, normal.z), float3(tapNormal.x, tapNormal.y, tapNormal.z));
                if (tapDepth < 1.0 && alignment > 0.95 && Abs(normal.w - tapNormal.w) < 0.05
                    && Abs(Dot3(delta, float3(normal.x, normal.y, normal.z))) < resources.material.control.x
                    && Dot3(materialDelta, materialDelta) < 0.01) {
                    const tap: float4 = Sample(resources.tail, resources.tailSampler, uv);
                    const difference: f32 = Abs(tap.x - center.x) + Abs(tap.y - center.y) + Abs(tap.z - center.z);
                    const scale: f32 = 0.25 + (center.x + center.y + center.z + tap.x + tap.y + tap.z) * 0.25;
                    const kernel: f32 = (2.0 - Abs(x)) * (2.0 - Abs(y));
                    const weight: f32 = kernel * Pow(alignment, 32.0) / (1.0 + difference / scale);
                    sum = Add3(sum, Scale3(float3(tap.x, tap.y, tap.z), weight));
                    total = total + weight;
                }
            }
        }
    }
    const result: float3 = Scale3(sum, 1.0 / Max(total, 0.00001));
    return { color: float4(result.x, result.y, result.z, center.w) };
}
