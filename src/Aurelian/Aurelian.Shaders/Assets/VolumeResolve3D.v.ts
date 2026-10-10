import { WorldAt } from "./SurfaceLighting";
import { Sub3, Dot3 } from "./Lighting3D";
import { VolumeAt, ComposeVolume } from "./VolumeLighting";
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
    grid: float4;
    depth: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) scene: Texture2D<float4>;
    @binding(2) sceneSampler: Sampler;
    @binding(3) motion: Texture2D<float4>;
    @binding(4) motionSampler: Sampler;
    @binding(5) volume: Texture2D<float4>;
    @binding(6) volumeSampler: Sampler;
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
    const m: PassMaterial = resources.material;
    const scene: float4 = Sample(resources.scene, resources.sceneSampler, input.uv);
    const motion: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    const delta: float3 = Sub3(WorldAt(input.uv, Min(motion.w, 0.9999), m.inverseX, m.inverseY, m.inverseZ, m.inverseW),
        float3(m.eye.x, m.eye.y, m.eye.z));
    var distance: f32 = Sqrt(Dot3(delta, delta));
    if (motion.w >= 1.0) { distance = m.depth.y; }
    const volume: float4 = VolumeAt(resources.volume, resources.volumeSampler, input.uv, distance, m.grid, m.depth);
    const color: float3 = ComposeVolume(float3(scene.x, scene.y, scene.z), volume);
    return { color: float4(color.x, color.y, color.z, scene.w) };
}
