import { EmitterSample3D, SampleLocalEmitter } from "./LocalEmitter3D";
import { LocalAttenuation } from "./SurfaceLighting";
import { DirectLightResponseModel, Dot3, Cross3, Unit } from "./Lighting3D";
import { AffineAnalyticIntersection } from "./AffineAnalyticQuery3D";
import { ReferenceEnvironment, ReferenceEnvironmentPdf, SampleReferenceEnvironment } from "./ReferenceEnvironment3D";

// Counter-based integer randomness: sample identity is independent of submission batches.
function Random(key: u32): f32 {
    var value: u32 = key;
    value = (value ^ (value >> 16)) * 0x7feb352d;
    value = (value ^ (value >> 15)) * 0x846ca68b;
    value = value ^ (value >> 16);
    return (Convert<f32>(value >> 8) + 0.5) / 16777216.0;
}
function PowerWeight(first: f32, second: f32): f32 {
    return first * first / Max(first * first + second * second, 0.000000000001);
}
function Read3(readonly data: StorageBuffer<f32>, offset: u32): float3 {
    return float3(data[offset], data[offset + 1], data[offset + 2]);
}
function SetRay(readwrite rays: StorageBuffer<f32>, offset: u32, origin: float3, direction: float3, range: f32): void {
    rays[offset] = origin.x;
    rays[offset + 1] = origin.y;
    rays[offset + 2] = origin.z;
    rays[offset + 3] = 0.0001;
    rays[offset + 4] = direction.x;
    rays[offset + 5] = direction.y;
    rays[offset + 6] = direction.z;
    rays[offset + 7] = range;
    rays[offset + 8] = Bitcast<f32>(0xffffffff);
    rays[offset + 9] = Bitcast<f32>(0xffffffff);
}
export record ReferenceEmitter {
    position: float3;
    normal: float3;
    emission: float3;
    twoSided: f32;
}
function SampleEmitter(readonly triangles: StorageBuffer<f32>, readonly parameters: StorageBuffer<f32>, key: u32): ReferenceEmitter {
    let target: f32 = Random(key) * parameters[39];
    let emitterTable: u32 = U32(parameters[41]);
    let count: u32 = U32(parameters[40]);
    var lower: u32 = 0;
    var upper: u32 = count;
    static for (let stage of Indices<9>()) {
        if (lower < upper) {
            let middle: u32 = (lower + upper) / 2;
            let primitive: u32 = U32(parameters[emitterTable + middle]);
            let cumulative: f32 = parameters[64 + primitive * 16 + 10];
            if (target < cumulative) {
                upper = middle;
            } else {
                lower = middle + 1;
            }
        }
    }
    if (lower >= count) { lower = count - 1; }
    let selected: u32 = U32(parameters[emitterTable + lower]);
    let vertex: u32 = selected * 12;
    let a: float3 = Read3(triangles, vertex);
    let b: float3 = Read3(triangles, vertex + 4);
    let c: float3 = Read3(triangles, vertex + 8);
    let radius: f32 = Sqrt(Random(key + 1));
    let v: f32 = Random(key + 2);
    let material: u32 = 64 + selected * 16;
    return { position: a * (1.0 - radius) + b * (radius * (1.0 - v)) + c * (radius * v),
        normal: Unit(Cross3(b - a, c - a)), emission: Read3(parameters, material + 4),
        twoSided: parameters[material + 12] };
}

// One invocation owns one pixel, including its ray/hit packets and float4 sum/count.
// Cosine sampling has support over the entire reflective hemisphere, including GGX.
// It uses the matching cosine PDF for both throughput and power-heuristic MIS.
@compute
@numthreads(64, 1, 1)
function ReferenceMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Scene: acceleration_structure,
    @binding(1) readonly Procedurals: StorageBuffer<f32>,
    @binding(2) readwrite Rays: StorageBuffer<f32>,
    @binding(3) readwrite Hits: StorageBuffer<f32>,
    @binding(4) readonly Triangles: StorageBuffer<f32>,
    @binding(5) readonly Parameters: StorageBuffer<f32>,
    @binding(6) readwrite Accumulation: StorageBuffer<f32>,
    @binding(7) readonly Environment: StorageBuffer<f32>): void {
    let width: u32 = U32(Parameters[0]);
    let height: u32 = U32(Parameters[1]);
    let pixel: u32 = thread.x;
    if (pixel < width * height) {
        let sum: u32 = pixel * 4;
        let sample: u32 = U32(Accumulation[sum + 3]);
        let key: u32 = pixel * 0x9e3779b9 + sample * 0x85ebca6b + Bitcast<u32>(Parameters[3]);
        var jitterX: f32 = 0.5;
        var jitterY: f32 = 0.5;
        if (Parameters[38] > 0.5) {
            jitterX = Random(key);
            jitterY = Random(key + 1);
        }
        let x: f32 = (Convert<f32>(pixel % width) + jitterX) / Convert<f32>(width) * 2.0 - 1.0;
        let y: f32 = 1.0 - (Convert<f32>(pixel / width) + jitterY) / Convert<f32>(height) * 2.0;
        var origin: float3 = Read3(Parameters, 4);
        var direction: float3 = Unit(Read3(Parameters, 8) + Read3(Parameters, 12) * x + Read3(Parameters, 16) * y);
        if (Parameters[37] > 0.5) {
            origin = origin + Read3(Parameters, 12) * x + Read3(Parameters, 16) * y;
            direction = Read3(Parameters, 8);
        }
        var throughput: float3 = float3(1.0, 1.0, 1.0);
        var radiance: float3 = float3(0.0, 0.0, 0.0);
        var active: bool = true;
        var previousPdf: f32 = 0.0;
        let lightArea: f32 = Parameters[39];
        let ray: u32 = 4 + pixel * 12;
        let hit: u32 = pixel * 16;
        for (let bounce of BoundedIndices<17>(17)) {
            if (active && bounce <= U32(Parameters[2])) {
                SetRay(Rays, ray, origin, direction, 100000.0);
                RayQueryTraceClosest(Scene, Procedurals, Rays, Hits, Triangles, pixel, AffineAnalyticIntersection);
                let kind: u32 = Bitcast<u32>(Hits[hit]);
                if (kind == 0) {
                    var environmentWeight: f32 = 1.0;
                    if (bounce > 0 && Parameters[36] > 0.5 && Parameters[48] > 0.5) {
                        environmentWeight = PowerWeight(previousPdf, ReferenceEnvironmentPdf(Parameters, direction));
                    }
                    radiance = radiance + throughput * ReferenceEnvironment(Environment, Parameters, direction) * environmentWeight;
                    active = false;
                } else {
                    let primitive: u32 = Bitcast<u32>(Hits[hit + 1]);
                    var material: u32 = 64 + primitive * 16;
                    if (kind == 2) {
                        material = material + U32(Parameters[35]) * 16;
                    }
                    let base: float3 = Read3(Parameters, material);
                    let roughness: f32 = Parameters[material + 3];
                    let emission: float3 = Read3(Parameters, material + 4);
                    let metallic: f32 = Parameters[material + 7];
                    let dielectric: f32 = Parameters[material + 8];
                    let model: f32 = Parameters[material + 9];
                    let point: float3 = float3(Hits[hit + 8], Hits[hit + 9], Hits[hit + 10]);
                    let normal: float3 = float3(Hits[hit + 12], Hits[hit + 13], Hits[hit + 14]);
                    let distance: f32 = Hits[hit + 4];
                    var lightCosine: f32 = Max(Dot3(normal, direction * -1.0) * Hits[hit + 15], 0.0);
                    if (Parameters[material + 12] > 0.5) {
                        lightCosine = Abs(Dot3(normal, direction));
                    }
                    if (Dot3(emission, emission) > 0.0) {
                        var weight: f32 = 1.0;
                        if (bounce > 0 && Parameters[36] > 0.5) {
                            weight = PowerWeight(previousPdf, distance * distance / Max(lightArea * lightCosine, 0.00000001));
                        }
                        if (lightCosine > 0.0) {
                            radiance = radiance + throughput * emission * weight;
                        }
                    }
                    if (bounce < U32(Parameters[2])) {
                        let sun: float3 = Read3(Parameters, 24);
                        let sunDirection: float3 = Read3(Parameters, 20);
                        if (Dot3(sun, sun) > 0.0 && Dot3(normal, sunDirection) > 0.0 && (bounce > 0 || Parameters[46] < 0.5)) {
                            SetRay(Rays, ray, point, sunDirection, 100000.0);
                            RayQueryTraceClosest(Scene, Procedurals, Rays, Hits, Triangles, pixel, AffineAnalyticIntersection);
                            if (Bitcast<u32>(Hits[hit]) == 0) {
                                let response: float3 = DirectLightResponseModel(base, normal, direction * -1.0,
                                    sunDirection, metallic, roughness, dielectric, model);
                                radiance = radiance + throughput * response * sun;
                            }
                        }
                        // Delta point/spot sources are evaluated exactly at every scattering vertex.
                        // The capture keeps its independent world-space closest-hit acceleration path.
                        if (Parameters[49] > 0.0) {
                            for (let local of BoundedIndices<256>(256)) {
                                if (local < U32(Parameters[49])) {
                                    let offset: u32 = U32(Parameters[50]) + local * 16;
                                    let position: float3 = Read3(Parameters, offset);
                                    let delta: float3 = position - point;
                                    let squared: f32 = Dot3(delta, delta);
                                    let centerDistance: f32 = Sqrt(squared);
                                    if (centerDistance > 0.0004 && centerDistance < Parameters[offset + 3]) {
                                        let cone: float4 = float4(Parameters[offset + 8], Parameters[offset + 9], Parameters[offset + 10], Parameters[offset + 11]);
                                        let config: float4 = float4(Parameters[offset + 12], Parameters[offset + 13], Parameters[offset + 14], Parameters[offset + 15]);
                                        let emitter: EmitterSample3D = SampleLocalEmitter(point, float4(position.x, position.y, position.z, Parameters[offset + 3]), cone, config,
                                            key + local * 101 + bounce * 937);
                                        let incoming: float3 = emitter.direction;
                                        let distance: f32 = Sqrt(Dot3(emitter.endpoint - point, emitter.endpoint - point));
                                        let attenuation: f32 = emitter.attenuation;
                                        if (attenuation > 0.0 && Dot3(normal, incoming) > 0.0) {
                                            var visible: bool = true;
                                            if (config.z > 0.5) {
                                                SetRay(Rays, ray, point, incoming, distance - 0.0002);
                                                Rays[ray + 3] = 0.0002;
                                                RayQueryTraceClosest(Scene, Procedurals, Rays, Hits, Triangles, pixel, AffineAnalyticIntersection);
                                                visible = Hits[hit + 2] == 0.0;
                                            }
                                            if (visible) {
                                                let response: float3 = DirectLightResponseModel(base, normal, direction * -1.0,
                                                    incoming, metallic, roughness, dielectric, model);
                                                radiance = radiance + throughput * response * Read3(Parameters, offset + 4) * (Parameters[offset + 7] * attenuation);
                                            }
                                        }
                                    }
                            }
                            }
                        }
                        let randomBase: u32 = key + 2 + bounce * 7;
                        if (Parameters[36] > 0.5 && Parameters[40] > 0.5) {
                            let emitter: ReferenceEmitter = SampleEmitter(Triangles, Parameters, randomBase);
                            let delta: float3 = emitter.position - point;
                            let range: f32 = Sqrt(Dot3(delta, delta));
                            let incoming: float3 = delta / range;
                            var cosLight: f32 = Max(Dot3(emitter.normal, incoming * -1.0), 0.0);
                            if (emitter.twoSided > 0.5) {
                                cosLight = Abs(Dot3(emitter.normal, incoming));
                            }
                            let cosSurface: f32 = Max(Dot3(normal, incoming), 0.0);
                            if (cosLight > 0.0 && cosSurface > 0.0) {
                                SetRay(Rays, ray, point, incoming, Max(range - 0.0002, 0.0001));
                                RayQueryTraceClosest(Scene, Procedurals, Rays, Hits, Triangles, pixel, AffineAnalyticIntersection);
                                if (Bitcast<u32>(Hits[hit]) == 0) {
                                    let lightPdf: f32 = range * range / (lightArea * cosLight);
                                    let bsdfPdf: f32 = cosSurface / 3.14159265;
                                    let response: float3 = DirectLightResponseModel(base, normal, direction * -1.0,
                                        incoming, metallic, roughness, dielectric, model);
                                    let neeWeight: f32 = PowerWeight(lightPdf, bsdfPdf);
                                    radiance = radiance + throughput * response * emitter.emission * (neeWeight / lightPdf);
                                }
                            }
                        }
                        if (Parameters[36] > 0.5 && Parameters[48] > 0.5) {
                            let environmentKey: u32 = key + 0x51ed270b + bounce * 7;
                            let incoming: float3 = SampleReferenceEnvironment(Parameters, Random(environmentKey),
                                Random(environmentKey + 1), Random(environmentKey + 2));
                            if (Dot3(normal, incoming) > 0.0) {
                                SetRay(Rays, ray, point, incoming, 100000.0);
                                RayQueryTraceClosest(Scene, Procedurals, Rays, Hits, Triangles, pixel, AffineAnalyticIntersection);
                                if (Bitcast<u32>(Hits[hit]) == 0) {
                                    let lightPdf: f32 = ReferenceEnvironmentPdf(Parameters, incoming);
                                    let bsdfPdf: f32 = Max(Dot3(normal, incoming), 0.0) / 3.14159265;
                                    let response: float3 = DirectLightResponseModel(base, normal, direction * -1.0,
                                        incoming, metallic, roughness, dielectric, model);
                                    radiance = radiance + throughput * response * ReferenceEnvironment(Environment, Parameters, incoming)
                                        * (PowerWeight(lightPdf, bsdfPdf) / lightPdf);
                                }
                            }
                        }
                        let radius: f32 = Sqrt(Random(randomBase + 3));
                        let angle: f32 = 6.2831853 * Random(randomBase + 4);
                        var axis: float3 = float3(0.0, 1.0, 0.0);
                        if (Abs(normal.y) > 0.9) {
                            axis = float3(1.0, 0.0, 0.0);
                        }
                        let tangent: float3 = Unit(Cross3(axis, normal));
                        let bitangent: float3 = Cross3(normal, tangent);
                        let outgoing: float3 = Unit(tangent * (radius * Cos(angle)) + bitangent * (radius * Sin(angle))
                            + normal * Sqrt(Max(1.0 - radius * radius, 0.0)));
                        previousPdf = Max(Dot3(normal, outgoing), 0.0) / 3.14159265;
                        let response: float3 = DirectLightResponseModel(base, normal, direction * -1.0,
                            outgoing, metallic, roughness, dielectric, model);
                        throughput = throughput * response / Max(previousPdf, 0.00000001);
                        if (Dot3(throughput, throughput) == 0.0) {
                            active = false;
                        }
                        direction = outgoing;
                        origin = point;
                    } else {
                        active = false;
                    }
                }
            }
        }
        Accumulation[sum] = Accumulation[sum] + radiance.x;
        Accumulation[sum + 1] = Accumulation[sum + 1] + radiance.y;
        Accumulation[sum + 2] = Accumulation[sum + 2] + radiance.z;
        Accumulation[sum + 3] = Accumulation[sum + 3] + 1.0;
    }
}
