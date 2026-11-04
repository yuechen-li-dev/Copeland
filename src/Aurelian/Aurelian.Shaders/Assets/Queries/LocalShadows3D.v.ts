import { AffineAnalyticIntersection } from "./AffineAnalyticQuery3D";
import { WorldAt, ConeAttenuation } from "./SurfaceLighting";
import { Dot3 } from "./Lighting3D";

import { Read4, HalfToFloat } from "./LocalLightSamplingMath3D";

// Four RGBA32F texels per pixel. Each numeric float holds sixteen visibility
// bits exactly; no NaN payloads or filtered bit reinterpretation cross graphics.
@compute
@numthreads(64, 1, 1)
function LocalShadowMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Scene: acceleration_structure,
    @binding(1) readonly Procedurals: StorageBuffer<f32>,
    @binding(2) readwrite Rays: StorageBuffer<f32>,
    @binding(3) readwrite Hits: StorageBuffer<f32>,
    @binding(4) readonly Instances: StorageBuffer<f32>,
    @binding(5) readonly Parameters: StorageBuffer<f32>,
    @binding(6) readonly Motion: StorageBuffer<f32>,
    @binding(7) readwrite Visibility: StorageBuffer<f32>,
    @binding(8) readonly Normals: StorageBuffer<u32>): void {
    const index: u32 = thread.x;
    const width: u32 = U32(Parameters[16]);
    const height: u32 = U32(Parameters[17]);
    if (index >= width * height) { return; }
    const depth: f32 = Motion[index * 4 + 3];
    const uv: float2 = float2((Convert<f32>(index % width) + 0.5) / Parameters[16],
        (Convert<f32>(index / width) + 0.5) / Parameters[17]);
    const position: float3 = WorldAt(uv, depth, Read4(Parameters, 0), Read4(Parameters, 4),
        Read4(Parameters, 8), Read4(Parameters, 12));
    const packedXY: u32 = Normals[index * 2];
    const packedZW: u32 = Normals[index * 2 + 1];
    const normal: float3 = float3(HalfToFloat(packedXY & 65535), HalfToFloat(packedXY >> 16), HalfToFloat(packedZW & 65535));
    const origin: float3 = position + normal * Parameters[22];
    const ray: u32 = 4 + index * 12;
    Rays[ray] = origin.x;
    Rays[ray + 1] = origin.y;
    Rays[ray + 2] = origin.z;
    Rays[ray + 3] = Parameters[19];
    Rays[ray + 8] = Parameters[20];
    Rays[ray + 9] = Parameters[21];
    for (let word of BoundedIndices<16>(16)) {
        var mask: u32 = 0;
        for (let bit of BoundedIndices<16>(16)) {
            const light: u32 = word * 16 + bit;
            if (depth < 1.0 && light < U32(Parameters[18])) {
                const offset: u32 = 64 + light * 16;
                const sphere: float4 = Read4(Parameters, offset);
                const config: float4 = Read4(Parameters, offset + 12);
                const delta: float3 = float3(sphere.x, sphere.y, sphere.z) - origin;
                const squared: f32 = Dot3(delta, delta);
                const distance: f32 = Sqrt(squared);
                if (config.z > 0.5 && distance > Parameters[19] * 2.0 && squared < sphere.w * sphere.w) {
                    const direction: float3 = delta / distance;
                    if (Dot3(normal, direction) > 0.0 && ConeAttenuation(direction, Read4(Parameters, offset + 8), config) > 0.0) {
                        Rays[ray + 4] = direction.x;
                        Rays[ray + 5] = direction.y;
                        Rays[ray + 6] = direction.z;
                        Rays[ray + 7] = distance - Parameters[19];
                        RayQueryTraceOccluded(Scene, Procedurals, Rays, Hits, Instances, index, AffineAnalyticIntersection);
                        if (Hits[index] > 0.5) { mask = mask | (1 << bit); }
                    }
                }
            }
        }
        Visibility[index * 16 + word] = Convert<f32>(mask);
    }
}
