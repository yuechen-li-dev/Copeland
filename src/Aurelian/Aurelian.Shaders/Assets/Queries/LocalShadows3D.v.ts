import { AffineAnalyticIntersection } from "./AffineAnalyticQuery3D";
import { WorldAt, ConeAttenuation } from "./SurfaceLighting";
import { Dot3 } from "./Lighting3D";

import { Read4, ReadSurfaceNormal } from "./LocalLightSamplingMath3D";

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
    let index: u32 = thread.x;
    let width: u32 = U32(Parameters[16]);
    let height: u32 = U32(Parameters[17]);
    if (index >= width * height) { return; }
    let depth: f32 = Motion[index * 4 + 3];
    let uv: float2 = float2((Convert<f32>(index % width) + 0.5) / Parameters[16],
        (Convert<f32>(index / width) + 0.5) / Parameters[17]);
    let position: float3 = WorldAt(uv, depth, Read4(Parameters, 0), Read4(Parameters, 4),
        Read4(Parameters, 8), Read4(Parameters, 12));
    let normal: float3 = ReadSurfaceNormal(Normals, index);
    let origin: float3 = position + normal * Parameters[22];
    let ray: u32 = 4 + index * 12;
    Rays[ray] = origin.x;
    Rays[ray + 1] = origin.y;
    Rays[ray + 2] = origin.z;
    Rays[ray + 3] = Parameters[19];
    Rays[ray + 8] = Parameters[20];
    Rays[ray + 9] = Parameters[21];
    for (let word of BoundedIndices<16>(16)) {
        var mask: u32 = 0;
        for (let bit of BoundedIndices<16>(16)) {
            let light: u32 = word * 16 + bit;
            if (depth < 1.0 && light < U32(Parameters[18])) {
                let offset: u32 = 64 + light * 16;
                let sphere: float4 = Read4(Parameters, offset);
                let config: float4 = Read4(Parameters, offset + 12);
                let delta: float3 = float3(sphere.x, sphere.y, sphere.z) - origin;
                let squared: f32 = Dot3(delta, delta);
                let distance: f32 = Sqrt(squared);
                if (config.z > 0.5 && distance > Parameters[19] * 2.0 && squared < sphere.w * sphere.w) {
                    let direction: float3 = delta / distance;
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
