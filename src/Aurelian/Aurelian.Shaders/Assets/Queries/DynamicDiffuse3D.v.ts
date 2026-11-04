import { AffineAnalyticIntersection } from "./AffineAnalyticQuery3D";
import { WorldAt, DecodeOcta, EncodeOcta } from "./SurfaceLighting";
import { Dot3, Unit, DirectLightModel } from "./Lighting3D";
import { Read4, HalfToFloat } from "./LocalLightSamplingMath3D";
import { EmitterSample3D, SampleLocalEmitter, EmitterRandom } from "./LocalEmitter3D";

function MinIndex(first: u32, second: u32): u32 {
    if (first < second) { return first; }
    return second;
}

function Direction(index: u32, count: u32): float3 {
    const y: f32 = 1.0 - 2.0 * (Convert<f32>(index) + 0.5) / Convert<f32>(count);
    const radius: f32 = Sqrt(Max(1.0 - y * y, 0.0));
    const angle: f32 = Convert<f32>(index) * 2.39996323;
    return float3(Cos(angle) * radius, y, Sin(angle) * radius);
}

function Probe(readonly parameters: StorageBuffer<f32>, index: u32): float3 {
    const x: u32 = U32(parameters[24]);
    const y: u32 = U32(parameters[25]);
    const coordinate: float3 = float3(Convert<f32>(index % x), Convert<f32>((index / x) % y), Convert<f32>(index / (x * y)));
    return float3(parameters[32], parameters[33], parameters[34]) + coordinate * float3(parameters[36], parameters[37], parameters[38]);
}

function SetRay(readwrite rays: StorageBuffer<f32>, index: u32, origin: float3, direction: float3, maximum: f32): void {
    const offset: u32 = 4 + index * 12;
    rays[offset] = origin.x;
    rays[offset + 1] = origin.y;
    rays[offset + 2] = origin.z;
    rays[offset + 3] = 0.0002;
    rays[offset + 4] = direction.x;
    rays[offset + 5] = direction.y;
    rays[offset + 6] = direction.z;
    rays[offset + 7] = maximum;
    rays[offset + 8] = Bitcast<f32>(0xffffffff);
    rays[offset + 9] = Bitcast<f32>(0xffffffff);
}

function Band(readwrite cache: StorageBuffer<f32>, readonly baseline: StorageBuffer<f32>, offset: u32, compiledOnly: bool, normal: float3): float3 {
    const uv: float2 = EncodeOcta(normal) * 7.0;
    const x: u32 = U32(Floor(uv.x));
    const y: u32 = U32(Floor(uv.y));
    const fraction: float2 = uv - float2(Convert<f32>(x), Convert<f32>(y));
    var sum: float3 = float3(0.0, 0.0, 0.0);
    for (let corner of BoundedIndices<4>(4)) {
        const dx: u32 = corner % 2;
        const dy: u32 = corner / 2;
        const sx: u32 = MinIndex(x + dx, 7);
        const sy: u32 = MinIndex(y + dy, 7);
        const at: u32 = offset + (sx + sy * 8) * 4;
        var value: float3 = float3(cache[at], cache[at + 1], cache[at + 2]);
        if (compiledOnly) { value = float3(baseline[at], baseline[at + 1], baseline[at + 2]); }
        var wx: f32 = 1.0 - fraction.x;
        var wy: f32 = 1.0 - fraction.y;
        if (dx == 1) { wx = fraction.x; }
        if (dy == 1) { wy = fraction.y; }
        sum = sum + value * (wx * wy);
    }
    return sum;
}

// The same entry records three ordered phases: update directional radiance,
// convolve E/pi, and resolve with current receiver-to-probe visibility.
@compute
@numthreads(64, 1, 1)
function DynamicDiffuseMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Scene: acceleration_structure,
    @binding(1) readonly Procedurals: StorageBuffer<f32>,
    @binding(2) readwrite Rays: StorageBuffer<f32>,
    @binding(3) readwrite Hits: StorageBuffer<f32>,
    @binding(4) readonly Instances: StorageBuffer<f32>,
    @binding(5) readonly Parameters: StorageBuffer<f32>,
    @binding(6) readonly Motion: StorageBuffer<f32>,
    @binding(7) readwrite Output: StorageBuffer<f32>,
    @binding(8) readonly Normals: StorageBuffer<u32>,
    @binding(9) readonly History: StorageBuffer<f32>,
    @binding(10) readwrite Next: StorageBuffer<f32>,
    @binding(11) readonly Baseline: StorageBuffer<f32>,
    @binding(12) readonly Shading: StorageBuffer<f32>,
    @binding(13) readwrite Occlusion: StorageBuffer<f32>): void {
    const index: u32 = thread.x;
    const count: u32 = U32(Parameters[27]);
    const directions: u32 = U32(Parameters[28]);
    const budget: u32 = U32(Parameters[29]);
    const frame: u32 = Bitcast<u32>(Parameters[30]);
    const mode: u32 = U32(Parameters[31]);
    const phase: u32 = U32(Parameters[39]);
    const coefficients: u32 = count * directions * 4;
    if (phase == 0) {
        if (index >= count * directions || mode == 3) { return; }
        const probe: u32 = index / directions;
        const directionIndex: u32 = index % directions;
        var value: float3 = float3(0.0, 0.0, 0.0);
        var backface: f32 = 0.0;
        if (mode == 2) { backface = Baseline[index * 4 + 3]; }
        if (frame > 0) {
            value = float3(History[index * 4], History[index * 4 + 1], History[index * 4 + 2]);
            backface = History[index * 4 + 3];
        }
        // Cyclic intervals cover every direction even when N is not divisible by P.
        const rank: u32 = (directionIndex * U32(Parameters[43]) + probe * 17) % directions;
        const relative: u32 = (rank + directions - (frame * budget) % directions) % directions;
        if (relative < budget) {
            const origin: float3 = Probe(Parameters, probe);
            const direction: float3 = Direction(directionIndex, directions);
            SetRay(Rays, index, origin, direction, 10000.0);
            RayQueryTraceRetained(Scene, Procedurals, Rays, Hits, Shading, index, AffineAnalyticIntersection);
            const hit: u32 = index * 16;
            value = float3(0.0, 0.0, 0.0);
            backface = 0.0;
            if (Hits[hit + 2] > 0.5) {
                if (Hits[hit + 15] < 0.0) { backface = 1.0; }
                const material: u32 = U32(Hits[hit + 3]) * 32;
                const position: float3 = float3(Hits[hit + 8], Hits[hit + 9], Hits[hit + 10]);
                const normal: float3 = float3(Hits[hit + 12], Hits[hit + 13], Hits[hit + 14]);
                const base: float3 = float3(Shading[material + 16], Shading[material + 17], Shading[material + 18]);
                if (backface < 0.5) { value = float3(Shading[material + 20], Shading[material + 21], Shading[material + 22]) * Parameters[45]; }
                const lights: u32 = U32(Parameters[18]);
                if (lights > 0 && backface < 0.5) {
                    const light: u32 = MinIndex(U32(EmitterRandom(index + 71) * Convert<f32>(lights)), lights - 1);
                    const offset: u32 = 64 + light * 16;
                    const emission: float4 = Read4(Parameters, offset + 4);
                    const config: float4 = Read4(Parameters, offset + 12);
                    const sample: EmitterSample3D = SampleLocalEmitter(position, Read4(Parameters, offset), Read4(Parameters, offset + 8), config, index + 113);
                    const delta: float3 = sample.endpoint - position;
                    const distance: f32 = Sqrt(Dot3(delta, delta));
                    var visible: bool = true;
                    if (config.z > 0.5 && sample.attenuation > 0.0 && Dot3(normal, sample.direction) > 0.0) {
                        SetRay(Rays, index, position + normal * 0.0005, sample.direction, Max(distance - 0.001, 0.0002));
                        RayQueryTraceOccluded(Scene, Procedurals, Rays, Occlusion, Instances, index, AffineAnalyticIntersection);
                        visible = Occlusion[index] < 0.5;
                    }
                    if (visible) {
                        value = value + DirectLightModel(base, normal, direction * -1.0, sample.direction,
                            Shading[material + 23], Shading[material + 19], Parameters[44])
                            * float3(emission.x, emission.y, emission.z) * (emission.w * sample.attenuation * Convert<f32>(lights));
                    }
                }
            }
            if (mode == 2) {
                // Signed until the final receiver result. Negative changes remove stale light.
                value = value - float3(Baseline[index * 4], Baseline[index * 4 + 1], Baseline[index * 4 + 2]);
            }
        }
        Next[index * 4] = value.x;
        Next[index * 4 + 1] = value.y;
        Next[index * 4 + 2] = value.z;
        Next[index * 4 + 3] = backface;
        return;
    }
    if (phase == 1) {
        if (index >= count * 64 || mode == 3) { return; }
        const probe: u32 = index / 64;
        const texel: u32 = index % 64;
        const normal: float3 = DecodeOcta(float2(Convert<f32>(texel % 8) / 7.0, Convert<f32>(texel / 8) / 7.0));
        var sum: float3 = float3(0.0, 0.0, 0.0);
        var backs: f32 = 0.0;
        var normalization: u32 = directions;
        if (mode == 1 && frame < directions) { normalization = MinIndex((frame + 1) * budget, directions); }
        for (let ray of BoundedIndices<128>(128)) {
            if (ray < directions) {
                const offset: u32 = (probe * directions + ray) * 4;
                var radiance: float3 = float3(Next[offset], Next[offset + 1], Next[offset + 2]);
                if (mode == 2) { radiance = radiance + float3(Baseline[offset], Baseline[offset + 1], Baseline[offset + 2]); }
                sum = sum + radiance * (4.0 * Max(Dot3(normal, Direction(ray, directions)), 0.0) / Convert<f32>(normalization));
                backs = backs + Next[offset + 3];
            }
        }
        const offset: u32 = coefficients + index * 4;
        Next[offset] = sum.x;
        Next[offset + 1] = sum.y;
        Next[offset + 2] = sum.z;
        Next[offset + 3] = backs / Convert<f32>(normalization);
        return;
    }
    const width: u32 = U32(Parameters[16]);
    const height: u32 = U32(Parameters[17]);
    if (index >= width * height) { return; }
    var sum: float3 = float3(0.0, 0.0, 0.0);
    var total: f32 = 0.0;
    const depth: f32 = Motion[index * 4 + 3];
    if (depth < 1.0) {
        const xy: u32 = Normals[index * 2];
        const zw: u32 = Normals[index * 2 + 1];
        const normal: float3 = float3(HalfToFloat(xy & 65535), HalfToFloat(xy >> 16), HalfToFloat(zw & 65535));
        const uv: float2 = float2((Convert<f32>(index % width) + 0.5) / Parameters[16], (Convert<f32>(index / width) + 0.5) / Parameters[17]);
        const position: float3 = WorldAt(uv, depth, Read4(Parameters, 0), Read4(Parameters, 4), Read4(Parameters, 8), Read4(Parameters, 12)) + normal * 0.002;
        const coordinate: float3 = (position - float3(Parameters[32], Parameters[33], Parameters[34])) / float3(Parameters[36], Parameters[37], Parameters[38]);
        const size: float3 = float3(Parameters[24], Parameters[25], Parameters[26]);
        if (coordinate.x >= -1.0 && coordinate.y >= -1.0 && coordinate.z >= -1.0 && coordinate.x <= size.x && coordinate.y <= size.y && coordinate.z <= size.z) {
            const grid: float3 = float3(Clamp(coordinate.x, 0.0, size.x - 1.001), Clamp(coordinate.y, 0.0, size.y - 1.001), Clamp(coordinate.z, 0.0, size.z - 1.001));
            const cell: float3 = float3(Floor(grid.x), Floor(grid.y), Floor(grid.z));
            const fraction: float3 = grid - cell;
            for (let corner of BoundedIndices<8>(8)) {
                const x: u32 = corner % 2;
                const y: u32 = (corner / 2) % 2;
                const z: u32 = corner / 4;
                const probe: u32 = U32(cell.x) + x + (U32(cell.y) + y) * U32(size.x) + (U32(cell.z) + z) * U32(size.x * size.y);
                const delta: float3 = Probe(Parameters, probe) - position;
                const distance: f32 = Sqrt(Max(Dot3(delta, delta), 0.000001));
                const direction: float3 = delta / distance;
                var visible: bool = true;
                if (Parameters[40] > 0.5) {
                    SetRay(Rays, index, position, direction, Max(distance - 0.001, 0.0002));
                    RayQueryTraceOccluded(Scene, Procedurals, Rays, Occlusion, Instances, index, AffineAnalyticIntersection);
                    visible = Occlusion[index] < 0.5;
                }
                const offset: u32 = coefficients + probe * 64 * 4;
                var backs: f32 = Next[offset + 3];
                if (mode == 3) { backs = Baseline[offset + 3]; }
                if (visible && backs < 0.25) {
                    var weight: f32 = Max(Dot3(normal, direction), 0.0) * 0.8 + 0.2;
                    var wx: f32 = 1.0 - fraction.x;
                    var wy: f32 = 1.0 - fraction.y;
                    var wz: f32 = 1.0 - fraction.z;
                    if (x == 1) { wx = fraction.x; }
                    if (y == 1) { wy = fraction.y; }
                    if (z == 1) { wz = fraction.z; }
                    weight = weight * wx * wy * wz;
                    sum = sum + Band(Next, Baseline, offset, mode == 3, normal) * weight;
                    total = total + weight;
                }
            }
        }
    }
    if (total > 0.000001) { sum = sum * (Parameters[41] / total); }
    Output[index * 4] = Max(sum.x, 0.0);
    Output[index * 4 + 1] = Max(sum.y, 0.0);
    Output[index * 4 + 2] = Max(sum.z, 0.0);
    Output[index * 4 + 3] = total;
}
