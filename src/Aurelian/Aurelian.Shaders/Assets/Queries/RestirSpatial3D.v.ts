import { AffineAnalyticIntersection } from "./AffineAnalyticQuery3D";
import { Target } from "./LocalLightProposal3D";
import { Dot3 } from "./Lighting3D";
import { Read4, LightRandom } from "./LocalLightSamplingMath3D";

export record SpatialDonor {
    pixel: u32;
    slot: u32;
    valid: u32;
}
function Donor(readonly parameters: StorageBuffer<f32>, readonly candidates: StorageBuffer<f32>,
    pixel: u32, sample: u32, key: u32, sourceIndex: u32): SpatialDonor {
    if (sourceIndex == 0) { return {
        pixel: pixel, slot: sample, valid: 1 };
    }
    if (sourceIndex > U32(parameters[36])) { return {
        pixel: 0, slot: 0, valid: 0 };
    }
    const neighbor: u32 = sourceIndex - 1;
    const width: u32 = U32(parameters[16]);
    const angle: f32 = LightRandom(key + neighbor * 37 + 7) * 6.2831853;
    const radius: f32 = Sqrt(LightRandom(key + neighbor * 37 + 8)) * parameters[37];
    const x: f32 = Floor(Convert<f32>(pixel % width) + Cos(angle) * radius + 0.5);
    const y: f32 = Floor(Convert<f32>(pixel / width) + Sin(angle) * radius + 0.5);
    if (x < 0.0 || y < 0.0 || x >= parameters[16] || y >= parameters[17]) { return {
        pixel: 0, slot: 0, valid: 0 };
    }
    const other: u32 = U32(y) * width + U32(x);
    const base: u32 = pixel * 24;
    const source: u32 = other * 24;
    const sourceQuota: u32 = U32(candidates[source + 3]);
    if (sourceQuota == 0) { return {
        pixel: 0, slot: 0, valid: 0 };
    }
    const slot: u32 = (sample + neighbor) % sourceQuota;
    if (other == pixel || candidates[source + 23] < 0.5 || candidates[source + slot * 4 + 2] <= 0.0) {
        return { pixel: 0, slot: 0, valid: 0 };
    }
    const point: float3 = float3(candidates[base + 16], candidates[base + 17], candidates[base + 18]);
    const normal: float3 = float3(candidates[base + 20], candidates[base + 21], candidates[base + 22]);
    const sourcePoint: float3 = float3(candidates[source + 16], candidates[source + 17], candidates[source + 18]);
    const sourceNormal: float3 = float3(candidates[source + 20], candidates[source + 21], candidates[source + 22]);
    if (Dot3(normal, sourceNormal) < parameters[39] || Abs(Dot3(sourcePoint - point, normal)) > parameters[38]) {
        return { pixel: 0, slot: 0, valid: 0 };
    }
    return { pixel: other, slot: slot, valid: 1 };
}
function DonorTarget(readonly parameters: StorageBuffer<f32>, readonly candidates: StorageBuffer<f32>, source: u32, light: u32): f32 {
    const point: float3 = float3(candidates[source + 16], candidates[source + 17], candidates[source + 18]);
    const normal: float3 = float3(candidates[source + 20], candidates[source + 21], candidates[source + 22]);
    return Target(parameters, point, normal, light);
}

// The candidate buffer is immutable for this whole dispatch. Final history and
// shading packets have separate storage; no workgroup-local ordering is assumed.
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
    @binding(8) readonly Normals: StorageBuffer<u32>,
    @binding(9) readonly Candidates: StorageBuffer<f32>,
    @binding(10) readwrite FinalHistory: StorageBuffer<f32>): void {
    const pixel: u32 = thread.x;
    const width: u32 = U32(Parameters[16]);
    const height: u32 = U32(Parameters[17]);
    if (pixel >= width * height) {
        return;
    }
    const base: u32 = pixel * 24;
    const output: u32 = pixel * 16;
    const frame: u32 = Bitcast<u32>(Parameters[28]);
    const seed: u32 = Bitcast<u32>(Parameters[35]);
    const quota: u32 = U32(Visibility[output + 9]);
    const temporal: f32 = Visibility[output + 10];
    const reprojected: f32 = Visibility[output + 11];
    const rejected: f32 = Visibility[output + 12];
    const point: float3 = float3(Candidates[base + 16], Candidates[base + 17], Candidates[base + 18]);
    const normal: float3 = float3(Candidates[base + 20], Candidates[base + 21], Candidates[base + 22]);
    const origin: float3 = point + normal * Parameters[22];
    const packet: u32 = 4 + pixel * 12;
    Rays[packet] = origin.x;
    Rays[packet + 1] = origin.y;
    Rays[packet + 2] = origin.z;
    Rays[packet + 3] = Parameters[19];
    Rays[packet + 8] = Parameters[20];
    Rays[packet + 9] = Parameters[21];
    for (let word of BoundedIndices<16>(16)) {
        Visibility[output + word] = 0.0;
    }
    for (let word of BoundedIndices<24>(24)) {
        FinalHistory[base + word] = Candidates[base + word];
    }
    var queries: u32 = 0;
    var spatial: u32 = 0;
    var spatialRejected: u32 = 0;
    for (let sample of BoundedIndices<4>(4)) {
        if (sample < quota && Candidates[base + 23] > 0.5) {
            const key: u32 = pixel * 0x9e3779b9 + frame * 0x85ebca6b + seed + sample * 0xc2b2ae35;
            var selected: u32 = U32(Candidates[base + sample * 4]);
            var mass: f32 = Min(Candidates[base + sample * 4 + 2], Parameters[27]);
            var normalized: f32 = Candidates[base + sample * 4 + 1];
            var currentTarget: f32 = Target(Parameters, point, normal, selected);
            // Pairwise balance partitions sum to one for every light ID. The
            // composite keeps an unbiased contribution weight while donors are
            // streamed once, rather than evaluating every donor against every other.
            for (let sourceIndex of BoundedIndices<8>(8)) {
                const donor: SpatialDonor = Donor(Parameters, Candidates, pixel, sample, key, sourceIndex + 1);
                if (donor.valid != 0) {
                    const source: u32 = donor.pixel * 24;
                    const count: f32 = Min(Candidates[source + donor.slot * 4 + 2], Parameters[27]);
                    const light: u32 = U32(Candidates[source + donor.slot * 4]);
                    const donorWeight: f32 = Candidates[source + donor.slot * 4 + 1];
                    const donorTarget: f32 = DonorTarget(Parameters, Candidates, source, light);
                    const destinationTarget: f32 = Target(Parameters, point, normal, light);
                    var firstPartition: f32 = mass / (mass + count);
                    var secondPartition: f32 = count / (mass + count);
                    if (Parameters[56] > 0.5) {
                        firstPartition = mass * currentTarget / (mass * currentTarget
                            + count * DonorTarget(Parameters, Candidates, source, selected));
                        secondPartition = count * donorTarget / (mass * destinationTarget + count * donorTarget);
                    }
                    const first: f32 = currentTarget * normalized * firstPartition;
                    const second: f32 = destinationTarget * donorWeight * secondPartition;
                    const sum: f32 = first + second;
                    if (LightRandom(key + sourceIndex * 37 + 9) * sum < second) {
                        selected = light;
                        currentTarget = destinationTarget;
                    }
                    normalized = sum / currentTarget;
                    mass = mass + count;
                    spatial = spatial + 1;
                } else {
                    if (sourceIndex < U32(Parameters[36])) {
                        spatialRejected = spatialRejected + 1;
                    }
                }
            }
            FinalHistory[base + sample * 4] = Convert<f32>(selected);
            FinalHistory[base + sample * 4 + 1] = normalized;
            FinalHistory[base + sample * 4 + 2] = mass;
            var weight: f32 = normalized / Convert<f32>(quota);
            const offset: u32 = 64 + selected * 16;
            const sphere: float4 = Read4(Parameters, offset);
            const delta: float3 = float3(sphere.x, sphere.y, sphere.z) - origin;
            const distance: f32 = Sqrt(Dot3(delta, delta));
            if (Parameters[offset + 14] > 0.5 && distance > Parameters[19] * 2.0
                && distance < sphere.w && Dot3(normal, delta) > 0.0) {
                const direction: float3 = delta / distance;
                Rays[packet + 4] = direction.x;
                Rays[packet + 5] = direction.y;
                Rays[packet + 6] = direction.z;
                Rays[packet + 7] = distance - Parameters[19];
                RayQueryTraceOccluded(Scene, Procedurals, Rays, Hits, Instances, pixel, AffineAnalyticIntersection);
                queries = queries + 1;
                if (Hits[pixel] > 0.5) {
                    weight = 0.0;
                }
            }
            Visibility[output + sample * 2] = Convert<f32>(selected);
            Visibility[output + sample * 2 + 1] = weight;
        }
    }
    Visibility[output + 8] = Convert<f32>(queries);
    Visibility[output + 9] = Convert<f32>(quota);
    Visibility[output + 10] = temporal;
    Visibility[output + 11] = reprojected;
    Visibility[output + 12] = rejected;
    Visibility[output + 13] = Convert<f32>(spatial);
    Visibility[output + 15] = Convert<f32>(spatialRejected);
}
