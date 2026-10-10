import { WorldAt } from "./SurfaceLighting";
import { Fogged } from "./HeightFog";
import { Unit, Sub3, Add3, Scale3 } from "./Lighting3D";
@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    inverseX: float4;
    inverseY: float4;
    inverseZ: float4;
    inverseW: float4;
    eye: float4;
    fogParameters: float4;
    fogColor: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) scene: Texture2D<float4>;
    @binding(2) sceneSampler: Sampler;
    @binding(3) motion: Texture2D<float4>;
    @binding(4) motionSampler: Sampler;
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
    const source: float4 = Sample(resources.scene, resources.sceneSampler, input.uv);
    const motion: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    const depth: f32 = Min(motion.w, 0.9999);
    var point: float3 = WorldAt(input.uv, depth, resources.material.inverseX,
        resources.material.inverseY, resources.material.inverseZ, resources.material.inverseW);
    const eye: float3 = float3(resources.material.eye.x, resources.material.eye.y, resources.material.eye.z);
    if (motion.w >= 1.0) {
        point = Add3(eye, Scale3(Unit(Sub3(point, eye)), resources.material.fogColor.w));
    }
    const lit: float3 = Fogged(float3(source.x, source.y, source.z), point, eye,
        resources.material.fogParameters, resources.material.fogColor);
    return { color: float4(lit.x, lit.y, lit.z, source.w) };
}
