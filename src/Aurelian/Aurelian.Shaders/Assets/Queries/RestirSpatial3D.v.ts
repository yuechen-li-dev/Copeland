import { EmitterSample3D, SampleLocalEmitter } from "./LocalEmitter3D";
import { AffineAnalyticIntersection } from "./AffineAnalyticQuery3D";
import { Target } from "./LocalLightProposal3D";
import { Dot3 } from "./Lighting3D";
import { Read4, LightRandom } from "./LocalLightSamplingMath3D";
import { ReservoirAddress, ReservoirLight, ReservoirMass, ReservoirQuota, ReservoirPoint, ReservoirNormal,
    WriteReservoir, WriteReservoirGeometry } from "./LightReservoir3D";

record SpatialDonor {
    surfacePosition: float3;
    surfaceNormal: float3;
    tag: u32;
    weight: f32;
}
function Donor(readonly parameters: StorageBuffer<f32>, readonly candidates: StorageBuffer<u32>,
    point: float3, normal: float3, pixel: u32, sample: u32, key: u32, neighbor: u32): SpatialDonor {
    let invalid: SpatialDonor = {
        surfacePosition: float3(0.0, 0.0, 0.0), surfaceNormal: float3(0.0, 0.0, 0.0), tag: 0, weight: 0.0
    };
    if (neighbor >= U32(parameters[36])) {
        return invalid;
    }
    let width: u32 = U32(parameters[16]);
    let angle: f32 = LightRandom(key + neighbor * 37 + 7) * 6.2831853;
    let radius: f32 = Sqrt(LightRandom(key + neighbor * 37 + 8)) * parameters[37];
    let x: f32 = Floor(Convert<f32>(pixel % width) + Cos(angle) * radius + 0.5);
    let y: f32 = Floor(Convert<f32>(pixel / width) + Sin(angle) * radius + 0.5);
    if (x < 0.0 || y < 0.0 || x >= parameters[16] || y >= parameters[17]) {
        return invalid;
    }
    let other: u32 = U32(y) * width + U32(x);
    if (other == pixel) {
        return invalid;
    }
    let source: u32 = ReservoirAddress(other);
    let sourceQuota: u32 = ReservoirQuota(candidates[source]);
    if (sourceQuota == 0) {
        return invalid;
    }
    let address: u32 = source + ((sample + neighbor) % sourceQuota) * 2;
    let tag: u32 = candidates[address];
    if (ReservoirMass(tag) <= 0.0) {
        return invalid;
    }
    let sourcePoint: float3 = ReservoirPoint(candidates, source);
    let sourceNormal: float3 = ReservoirNormal(candidates, source);
    if (Dot3(normal, sourceNormal) < parameters[39] || Abs(Dot3(sourcePoint - point, normal)) > parameters[38]) {
        return invalid;
    }
    return { surfacePosition: sourcePoint, surfaceNormal: sourceNormal, tag: tag, weight: Bitcast<f32>(candidates[address + 1]) };
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
    @binding(9) readonly Candidates: StorageBuffer<u32>,
    @binding(10) readwrite FinalHistory: StorageBuffer<u32>): void {
    let pixel: u32 = thread.x;
    let width: u32 = U32(Parameters[16]);
    let height: u32 = U32(Parameters[17]);
    if (pixel >= width * height) {
        return;
    }
    let base: u32 = ReservoirAddress(pixel);
    let output: u32 = pixel * 16;
    let frame: u32 = Bitcast<u32>(Parameters[28]);
    let seed: u32 = Bitcast<u32>(Parameters[35]);
    let quota: u32 = U32(Visibility[output + 9]);
    let temporal: f32 = Visibility[output + 10];
    let reprojected: f32 = Visibility[output + 11];
    let rejected: f32 = Visibility[output + 12];
    let emitterKey: u32 = U32(Visibility[output + 14]);
    let point: float3 = ReservoirPoint(Candidates, base);
    let normal: float3 = ReservoirNormal(Candidates, base);
    let storedQuota: u32 = ReservoirQuota(Candidates[base]);
    let origin: float3 = point + normal * Parameters[22];
    let packet: u32 = 4 + pixel * 12;
    Rays[packet] = origin.x;
    Rays[packet + 1] = origin.y;
    Rays[packet + 2] = origin.z;
    Rays[packet + 3] = Parameters[19];
    Rays[packet + 8] = Parameters[20];
    Rays[packet + 9] = Parameters[21];
    for (let word of BoundedIndices<16>(16)) {
        Visibility[output + word] = 0.0;
    }
    let storeHistory: bool = U32(Parameters[23]) == 8;
    if (storeHistory) {
        WriteReservoirGeometry(FinalHistory, base, point, normal);
    }
    var queries: u32 = 0;
    var spatial: u32 = 0;
    var spatialRejected: u32 = 0;
    for (let sample of BoundedIndices<4>(4)) {
        if (sample < storedQuota) {
            let key: u32 = pixel * 0x9e3779b9 + frame * 0x85ebca6b + seed + sample * 0xc2b2ae35;
            let address: u32 = base + sample * 2;
            let tag: u32 = Candidates[address];
            var selected: u32 = ReservoirLight(tag);
            var mass: f32 = Min(ReservoirMass(tag), Parameters[27]);
            var normalized: f32 = Bitcast<f32>(Candidates[address + 1]);
            var currentTarget: f32 = Target(Parameters, point, normal, selected);
            // Pairwise balance partitions sum to one for every light ID. The
            // composite keeps an unbiased contribution weight while donors are
            // streamed once, rather than evaluating every donor against every other.
            for (let sourceIndex of BoundedIndices<8>(8)) {
                let donor: SpatialDonor = Donor(Parameters, Candidates, point, normal, pixel, sample, key, sourceIndex);
                let donorMass: f32 = ReservoirMass(donor.tag);
                if (donorMass > 0.0) {
                    let count: f32 = Min(donorMass, Parameters[27]);
                    let light: u32 = ReservoirLight(donor.tag);
                    let donorTarget: f32 = Target(Parameters, donor.surfacePosition, donor.surfaceNormal, light);
                    let destinationTarget: f32 = Target(Parameters, point, normal, light);
                    var firstPartition: f32 = mass / (mass + count);
                    var secondPartition: f32 = count / (mass + count);
                    if (Parameters[56] > 0.5) {
                        firstPartition = mass * currentTarget / (mass * currentTarget
                            + count * Target(Parameters, donor.surfacePosition, donor.surfaceNormal, selected));
                        secondPartition = count * donorTarget / (mass * destinationTarget + count * donorTarget);
                    }
                    let first: f32 = currentTarget * normalized * firstPartition;
                    let second: f32 = destinationTarget * donor.weight * secondPartition;
                    let sum: f32 = first + second;
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
            if (storeHistory) {
                WriteReservoir(FinalHistory, base, sample, selected, mass, normalized, storedQuota);
            }
            var weight: f32 = normalized / Convert<f32>(quota);
            let offset: u32 = 64 + selected * 16;
            let sphere: float4 = Read4(Parameters, offset);
            let emitter: EmitterSample3D = SampleLocalEmitter(point, sphere, Read4(Parameters, offset + 8), Read4(Parameters, offset + 12), emitterKey + sample * 137);
            let delta: float3 = emitter.endpoint - origin;
            let distance: f32 = Sqrt(Dot3(delta, delta));
            if (Parameters[offset + 14] > 0.5 && distance > Parameters[19] * 2.0
                && distance < sphere.w && Dot3(normal, delta) > 0.0
                && (Parameters[offset + 13] < 1.5 || emitter.attenuation > 0.0)) {
                let direction: float3 = delta / distance;
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
        } else {
            if (storeHistory) {
                WriteReservoir(FinalHistory, base, sample, 0, 0.0, 0.0, storedQuota);
            }
        }
    }
    Visibility[output + 8] = Convert<f32>(queries);
    Visibility[output + 9] = Convert<f32>(quota);
    Visibility[output + 10] = temporal;
    Visibility[output + 11] = reprojected;
    Visibility[output + 12] = rejected;
    Visibility[output + 13] = Convert<f32>(spatial);
    Visibility[output + 14] = Convert<f32>(emitterKey);
    Visibility[output + 15] = Convert<f32>(spatialRejected);
}
