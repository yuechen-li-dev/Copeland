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
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) diffuse: Texture2D<float4>;
    @binding(2) diffuseSampler: Sampler;
    @binding(3) motion: Texture2D<float4>;
    @binding(4) motionSampler: Sampler;
    @binding(5) normal: Texture2D<float4>;
    @binding(6) normalSampler: Sampler;
    @binding(7) profile: Texture2D<float4>;
    @binding(8) profileSampler: Sampler;
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
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const profile: float4 = Sample(resources.profile, resources.profileSampler, input.uv);
    const center: float4 = Sample(resources.diffuse, resources.diffuseSampler, input.uv);
    if (profile.w <= 0.0) {
        return { color: center };
    }
    const depth: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    const normal: float4 = Sample(resources.normal, resources.normalSampler, input.uv);
    const p: float3 = WorldAt(input.uv, depth.w, resources.material.inverseX,
        resources.material.inverseY, resources.material.inverseZ, resources.material.inverseW);
    const step: float2 = float2(resources.material.parameters.x, resources.material.parameters.y);
    const next: float3 = WorldAt(float2(input.uv.x + step.x, input.uv.y + step.y), depth.w,
        resources.material.inverseX, resources.material.inverseY, resources.material.inverseZ, resources.material.inverseW);
    const spacing: float3 = Sub3(next, p);
    const pixels: f32 = Min(profile.w / Sqrt(Max(Dot3(spacing, spacing), 0.000001)), 32.0);
    var sum: float4 = float4(0.0, 0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    for (var index: u32 = 0; index < 9; index = index + 1) {
        const offset: f32 = (Convert<f32>(index) - 4.0) / 4.0;
        const uv: float2 = float2(input.uv.x + step.x * pixels * offset, input.uv.y + step.y * pixels * offset);
        if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) {
            const neighborProfile: float4 = Sample(resources.profile, resources.profileSampler, uv);
            const neighborDepth: float4 = Sample(resources.motion, resources.motionSampler, uv);
            const neighborNormal: float4 = Sample(resources.normal, resources.normalSampler, uv);
            const q: float3 = WorldAt(uv, neighborDepth.w, resources.material.inverseX,
                resources.material.inverseY, resources.material.inverseZ, resources.material.inverseW);
            const delta: float3 = Sub3(q, p);
            const agreement: f32 = normal.x * neighborNormal.x + normal.y * neighborNormal.y + normal.z * neighborNormal.z;
            if (neighborProfile.w > 0.0 && neighborDepth.w < 1.0 && agreement > 0.8
                && Abs(neighborProfile.w - profile.w) < profile.w * 0.1
                && Dot3(delta, delta) < profile.w * profile.w * 2.0) {
                const weight: f32 = Exp(-4.5 * offset * offset);
                const value: float4 = Sample(resources.diffuse, resources.diffuseSampler, uv);
                sum = float4(sum.x + value.x * weight, sum.y + value.y * weight,
                    sum.z + value.z * weight, sum.w + value.w * weight);
                total = total + weight;
            }
        }
    }
    if (total <= 0.00001) {
        return { color: center };
    }
    return { color: sum * float4(1.0 / total, 1.0 / total, 1.0 / total, 1.0 / total) };
}
