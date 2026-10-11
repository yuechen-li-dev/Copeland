import { Fogged } from "./HeightFog";
import { VolumeAt, ComposeVolume } from "./VolumeLighting";
import { WorldAt } from "./SurfaceLighting";
import { Sub3, Dot3 } from "./Lighting3D";

@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record ParticleMaterial {
    clipX: float4;
    clipY: float4;
    clipZ: float4;
    clipW: float4;
    inverseX: float4;
    inverseY: float4;
    inverseZ: float4;
    inverseW: float4;
    eye: float4;
    texels: float4;
    fogParameters: float4;
    fogColor: float4;
    volumeGrid: float4;
    volumeDepth: float4;
}
stream Input {
    @location(0) position: float3;
    @location(1) uvKind: float4;
    @location(2) color: float4;
}
stream Uniforms { @binding(0) material: ParticleMaterial; }
stream Resources {
    @binding(1) motion: Texture2D<float4>;
    @binding(2) motionSampler: Sampler;
    @binding(3) volume: Texture2D<float4>;
    @binding(4) volumeSampler: Sampler;
}
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) world: float3;
    @location(1) uvKind: float4;
    @location(2) color: float4;
}
stream Output {
    @target(0) color: float4;
    @target(1) optical: float4;
}
function Row(row: float4, p: float3): f32 {
    return Dot3(row.xyz, p) + row.w;
}
@vertex
function VertexMain(input: Input, uniforms: Uniforms): Varyings {
    let m: ParticleMaterial = uniforms.material;
    return {
        position: float4(Row(m.clipX, input.position), Row(m.clipY, input.position), Row(m.clipZ, input.position), Row(m.clipW, input.position)),
        world: input.position,
        uvKind: input.uvKind,
        color: input.color,
    };
}
@pixel
function PixelMain(input: Varyings, uniforms: Uniforms, resources: Resources): Output {
    let m: ParticleMaterial = uniforms.material;
    let uv: float2 = float2(input.position.x * m.texels.x, input.position.y * m.texels.y);
    let depth: f32 = Sample(resources.motion, resources.motionSampler, uv).w;
    if (input.position.z >= depth) { Discard(); }
    let radius: f32 = input.uvKind.x * input.uvKind.x + input.uvKind.y * input.uvKind.y;
    if (radius >= 1.0 || input.color.w <= 0.00001) { Discard(); }
    let opaque: float3 = WorldAt(uv, depth, m.inverseX, m.inverseY, m.inverseZ, m.inverseW);
    let delta: float3 = Sub3(input.world, m.eye.xyz);
    let opaqueDelta: float3 = Sub3(opaque, m.eye.xyz);
    let distance: f32 = Sqrt(Dot3(delta, delta));
    let soft: f32 = Clamp((Sqrt(Dot3(opaqueDelta, opaqueDelta)) - distance) / input.uvKind.w, 0.0, 1.0);
    var shape: f32 = Pow(1.0 - radius, 2.0);
    if (input.uvKind.z > 0.5 && input.uvKind.z < 1.5) {
        // Smooth low-frequency lobes avoid requiring an imported flipbook for the starter.
        let lobes: f32 = 0.7 + 0.3 * Sin(input.uvKind.x * 8.0 + Sin(input.uvKind.y * 7.0));
        shape = (1.0 - radius) * lobes;
    }
    let alpha: f32 = Clamp(input.color.w * shape * soft, 0.0, 0.98);
    var lit: float3 = input.color.xyz;
    if (m.volumeGrid.w > 0.5) {
        lit = ComposeVolume(lit, VolumeAt(resources.volume, resources.volumeSampler, uv, distance, m.volumeGrid, m.volumeDepth));
    } else {
        lit = Fogged(lit, input.world, m.eye.xyz, m.fogParameters, m.fogColor);
    }
    let weight: f32 = Max(alpha, 0.001) / (1.0 + distance * distance * 0.02);
    return {
        color: float4(lit.x * alpha * weight, lit.y * alpha * weight, lit.z * alpha * weight, alpha * weight),
        optical: float4(-Log(Max(1.0 - alpha, 0.000001)), 0.0, 0.0, 0.0),
    };
}
