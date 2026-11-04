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
    @binding(9) history: Texture2D<float4>;
    @binding(10) historySampler: Sampler;
    @binding(11) geometry: Texture2D<float4>;
    @binding(12) geometrySampler: Sampler;
    @binding(13) previousSurface: Texture2D<float4>;
    @binding(14) previousSurfaceSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output {
    @target(0) color: float4;
    @target(1) geometry: float4;
    @target(2) surface: float4;
}
@vertex
function VertexMain(input: Input): Varyings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}

function MinColor(a: float3, b: float3): float3 {
    return float3(Min(a.x, b.x), Min(a.y, b.y), Min(a.z, b.z));
}
function MaxColor(a: float3, b: float3): float3 {
    return float3(Max(a.x, b.x), Max(a.y, b.y), Max(a.z, b.z));
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    let motion: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    let normal: float4 = Sample(resources.normal, resources.normalSampler, input.uv);
    let surface: float4 = Sample(resources.surface, resources.surfaceSampler, input.uv);
    let current: float4 = Sample(resources.tail, resources.tailSampler, input.uv);
    var color: float3 = float3(current.x, current.y, current.z);
    var count: f32 = 1.0;
    if (motion.w >= 1.0) {
        return { color: float4(0.0, 0.0, 0.0, 0.0), geometry: float4(0.0, 0.0, 0.0, 1.0), surface: surface };
    }
    let previousUv: float2 = float2(motion.x, motion.y);
    if (resources.material.settings.z > 0.5 && previousUv.x >= 0.0 && previousUv.y >= 0.0
        && previousUv.x < 1.0 && previousUv.y < 1.0 && motion.z >= 0.0 && motion.z < 1.0) {
        let geometry: float4 = Sample(resources.geometry, resources.geometrySampler, previousUv);
        let old: float4 = Sample(resources.history, resources.historySampler, previousUv);
        let oldSurface: float4 = Sample(resources.previousSurface, resources.previousSurfaceSampler, previousUv);
        let expected: float3 = WorldAt(previousUv, motion.z, resources.material.previousX,
            resources.material.previousY, resources.material.previousZ, resources.material.previousW);
        let oldPoint: float3 = WorldAt(previousUv, geometry.w, resources.material.previousX,
            resources.material.previousY, resources.material.previousZ, resources.material.previousW);
        let oldNormal: float3 = float3(geometry.x, geometry.y, geometry.z);
        let difference: float3 = Sub3(expected, oldPoint);
        let materialDifference: float3 = float3(surface.x - oldSurface.x, surface.y - oldSurface.y, surface.z - oldSurface.z);
        let oldCount: f32 = Floor(old.w);
        let oldVisibility: f32 = (old.w - oldCount) * 2.0;
        if (oldCount > 0.0 && geometry.w < 1.0
            && Dot3(float3(normal.x, normal.y, normal.z), oldNormal) >= 0.95
            && Abs(Dot3(difference, oldNormal)) < resources.material.control.x
            && Dot3(materialDifference, materialDifference) < 0.01 && Abs(oldSurface.w - normal.w) < 0.05
            && Abs(oldVisibility - current.w) < 0.49) {
            var low: float3 = color;
            var high: float3 = color;
            static for (let y of [-1.0, 0.0, 1.0]) {
                static for (let x of [-1.0, 0.0, 1.0]) {
                    let uv: float2 = float2(input.uv.x + x * resources.material.settings.x, input.uv.y + y * resources.material.settings.y);
                    let tap: float4 = Sample(resources.tail, resources.tailSampler, uv);
                    low = MinColor(low, float3(tap.x, tap.y, tap.z));
                    high = MaxColor(high, float3(tap.x, tap.y, tap.z));
                }
            }
            let history: float3 = MinColor(high, MaxColor(low, float3(old.x, old.y, old.z)));
            count = Min(oldCount + 1.0, resources.material.settings.w);
            color = Scale3(Add3(color, Scale3(history, count - 1.0)), 1.0 / count);
        }
    }
    return { color: float4(color.x, color.y, color.z, count + current.w * 0.5),
        geometry: float4(normal.x, normal.y, normal.z, motion.w),
        surface: float4(surface.x, surface.y, surface.z, normal.w) };
}
