import { EnvironmentUv } from "./EnvironmentCoordinates3D";
import { SecondaryAttributes } from "./SecondarySurface3D";
import { ResidentVertex, ResidentInstance, ResidentAttributes, DirectionRow, PointRow } from "./ResidentGeometry3D";
import { BindlessMaterialRecord } from "./ResidentMaterial3D";
import { SurfaceData, EvaluateSurfaceSamples } from "./SurfaceMaterial3D";
import { AnalyticLocalRay } from "./AnalyticGeometry3D";
import { AnalyticRay, AnalyticHit, IntersectAnalytic } from "./AnalyticIntersections3D";
import { AffineAnalyticIntersection } from "./AffineAnalyticQuery3D";
import { WorldAt, EncodeOcta, EnvironmentBandUv } from "./SurfaceLighting";
import { Cross3, Unit, Dot3, DirectLightModel, HemisphereLight, SpecularFresnel } from "./Lighting3D";
import { GlossyCoverage, GlossyIsSharp, SampleVisibleGgx, GgxVisibilityWeight, VisibleGgxReflectionPdf, SampleReflectionCone, ReflectionGuideBit } from "./GlossyReflection3D";
import { ScreenCandidateBit, ScreenRetryBit } from "./GlossyReflection3D";
import { ReflectionRayRange, ScreenPixel, ScreenTriangleLimit } from "./ScreenReflection3D";
import { Read4 } from "./LocalLightSamplingMath3D";
import { EmitterSample3D, SampleLocalEmitter, EmitterRandom } from "./LocalEmitter3D";

function SetReflectionRay(readwrite rays: StorageBuffer<f32>, index: u32,
    origin: float3, direction: float3, maximum: f32): void {
    let offset: u32 = 4 + index * 12;
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

function GeometricNormal(hit: uint2, pixel: float2, viewport: float2, instance: ResidentInstance,
    readonly vertices: StorageBuffer<ResidentVertex>, readonly indices: StorageBuffer<u32>,
    readonly parameters: StorageBuffer<f32>): float3 {
    if (instance.primitiveKind != 0) {
        let ray: AnalyticRay = AnalyticLocalRay(pixel, viewport, instance,
            Read4(parameters, 32), Read4(parameters, 36), Read4(parameters, 40), Read4(parameters, 44));
        let analytic: AnalyticHit = IntersectAnalytic(ray, instance.primitiveKind);
        let n: float3 = analytic.normal;
        return Unit(float3(DirectionRow(instance.normalX, n), DirectionRow(instance.normalY, n), DirectionRow(instance.normalZ, n)));
    }
    let triangle: u32 = instance.indexBase + hit.y / 2 * 3;
    let a: float3 = vertices[instance.vertexBase + indices[triangle]].position;
    let b: float3 = vertices[instance.vertexBase + indices[triangle + 1]].position;
    let c: float3 = vertices[instance.vertexBase + indices[triangle + 2]].position;
    let n: float3 = Cross3(b - a, c - a);
    return Unit(float3(DirectionRow(instance.normalX, n), DirectionRow(instance.normalY, n), DirectionRow(instance.normalZ, n)));
}

function HemisphereRadiance(direction: float3, sky: float3, ground: float3): float3 {
    let blend: f32 = Clamp(direction.y * 0.5 + 0.5, 0.0, 1.0);
    return sky * blend + ground * (1.0 - blend);
}

@compute
@numthreads(64, 1, 1)
function SharpReflectionMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Scene: acceleration_structure,
    @binding(1) readonly Procedurals: StorageBuffer<f32>,
    @binding(2) readonly Instances: StorageBuffer<f32>,
    @binding(3) readonly Shading: StorageBuffer<f32>,
    @binding(4) readonly Parameters: StorageBuffer<f32>,
    @binding(5) readwrite Rays: StorageBuffer<f32>,
    @binding(6) readwrite Hits: StorageBuffer<f32>,
    @binding(7) readwrite Occlusion: StorageBuffer<f32>,
    @binding(8) readwrite Output: StorageBuffer<f32>,
    @binding(9) readonly Materials: StorageBuffer<BindlessMaterialRecord>,
    @binding(10) readonly InstanceMaterials: StorageBuffer<u32>,
    @binding(11) readonly Vertices: StorageBuffer<ResidentVertex>,
    @binding(12) readonly Indices: StorageBuffer<u32>,
    @binding(13) readonly Geometry: StorageBuffer<ResidentInstance>,
    @binding(14) readonly Visibility: Texture2D<uint2>,
    @binding(15) readonly Motion: Texture2D<float4>,
    @binding(16) readonly BaseColor: Texture2D<float4>,
    @binding(17) readonly Normals: Texture2D<float4>,
    @binding(18) readonly Emission: Texture2D<float4>,
    @binding(19) readonly Environment: Texture2D<float4>,
    @binding(20) readonly SkySource: Texture2D<float4>,
    @binding(21) readonly LinearClamp: Sampler,
    @binding(22) readonly Textures: DescriptorArray<Texture2D<float4>, 4096>,
    @binding(23) readonly Samplers: DescriptorArray<Sampler, 64>,
    @binding(24) readwrite ReflectionHits: StorageBuffer<u32>,
    @binding(25) readonly ProbeGuide: StorageBuffer<f32>): void {
    let raySlot: u32 = thread.x;
    let workIndex: u32 = raySlot + thread.y * U32(Parameters[27]);
    let width: u32 = U32(Parameters[20]);
    let height: u32 = U32(Parameters[21]);
    let tracing: u32 = U32(Parameters[59]);
    let quarterRate: bool = tracing % 2 != 0;
    let useScreenBounds: bool = tracing >= 2;
    let compact: bool = quarterRate && thread.z != 0;
    var texelX: u32 = workIndex % width;
    var texelY: u32 = workIndex / width;
    if (compact) {
        let tileWidth: u32 = (width + 1) / 2;
        let phase: u32 = U32(Parameters[63]) % 4;
        texelX = workIndex % tileWidth * 2 + phase % 2;
        texelY = workIndex / tileWidth * 2 + phase / 2;
    }
    if (texelX >= width || texelY >= height) { return; }
    let index: u32 = texelY * width + texelX;
    let target: u32 = index * 4;
    if (!compact) { Output[target + 3] = 0.0; }
    let coordinate: uint2 = uint2(texelX, texelY);
    let identity: uint2 = Load(Visibility, coordinate);
    if (identity.x == 0) { return; }
    let receiver: float4 = Load(BaseColor, coordinate);
    let roughness: f32 = receiver.w;
    let glossy: bool = Parameters[28] > 0.5;
    var coverage: f32 = Clamp((0.18 - roughness) / 0.08, 0.0, 1.0);
    if (glossy) { coverage = GlossyCoverage(roughness); }
    if (coverage <= 0.0 || Load(Emission, coordinate).w < 0.0) { return; }
    let sampledGloss: bool = glossy && !GlossyIsSharp(roughness);
    if (quarterRate) {
        if (compact) {
            if (!sampledGloss) { return; }
            Output[target + 3] = 0.0;
        } else if (sampledGloss) {
            // Positive coverage marks an eligible receiver with no ray this frame.
            Output[target + 3] = coverage;
            return;
        }
    }
    let depth: f32 = Load(Motion, coordinate).w;
    if (depth >= 1.0) { return; }

    let pixel: float2 = float2(Convert<f32>(coordinate.x) + 0.5, Convert<f32>(coordinate.y) + 0.5);
    let viewport: float2 = float2(Parameters[20], Parameters[21]);
    let uv: float2 = float2(pixel.x / viewport.x, pixel.y / viewport.y);
    let sx: float4 = Read4(Parameters, 0);
    let sy: float4 = Read4(Parameters, 4);
    let sz: float4 = Read4(Parameters, 8);
    let sw: float4 = Read4(Parameters, 12);
    let position: float3 = WorldAt(uv, depth, sx, sy, sz, sw);
    var view: float3 = Unit(float3(Parameters[16], Parameters[17], Parameters[18]) - position);
    if (Parameters[19] < 0.5) {
        view = Unit(WorldAt(uv, 0.0, sx, sy, sz, sw) - WorldAt(uv, 1.0, sx, sy, sz, sw));
    }
    let encoded: float4 = Load(Normals, coordinate);
    let normal: float3 = Unit(float3(encoded.x, encoded.y, encoded.z));
    let nv: f32 = Dot3(normal, view);
    if (nv <= 0.0) { return; }
    var direction: float3 = normal * (2.0 * nv) - view;
    var throughput: float3 = float3(1.0, 1.0, 1.0);
    var usedGuide: bool = false;
    if (glossy) {
        let base: float3 = float3(receiver.x, receiver.y, receiver.z);
        throughput = SpecularFresnel(base, encoded.w, nv);
        if (sampledGloss) {
            let key: u32 = index * 0x9e3779b9;
            var step: f32 = Parameters[63];
            // The interleave selects which pixel is traced. Each pixel still
            // advances its own low-discrepancy sequence by one per actual ray.
            if (quarterRate) { step = Floor(step * 0.25); }
            let x: f32 = EmitterRandom(key) + step * 0.7548776662;
            let y: f32 = EmitterRandom(key + 1) + step * 0.5698402910;
            let alpha: f32 = roughness * roughness;
            let random: float2 = float2(x - Floor(x), y - Floor(y));
            var guide: float3 = direction;
            var mixWeight: f32 = 0.0;
            var coneCosine: f32 = 0.988;
            if (Parameters[28] > 1.5 && roughness >= 0.28) {
                let hint: float4 = Read4(ProbeGuide, 0);
                let displacement: float3 = float3(hint.x, hint.y, hint.z) - position;
                let distanceSquared: f32 = Dot3(displacement, displacement);
                if (hint.w > 0.0 && distanceSquared > 0.25) {
                    guide = Unit(displacement);
                    if (Dot3(guide, direction) > 0.89 && Dot3(guide, normal) > 0.12) {
                        coneCosine = Clamp(Sqrt(distanceSquared / (distanceSquared + hint.w * hint.w)), 0.96, 0.9998);
                        mixWeight = 0.48;
                    }
                }
            }
            var half: float3 = float3(0.0, 0.0, 0.0);
            if (random.x < mixWeight) {
                direction = SampleReflectionCone(guide, coneCosine, float2(random.x / mixWeight, random.y));
                half = Unit(view + direction);
            } else {
                half = SampleVisibleGgx(normal, view, alpha, float2((random.x - mixWeight) / (1.0 - mixWeight), random.y));
                direction = half * (2.0 * Max(0.0, Dot3(view, half))) - view;
            }
            let vh: f32 = Max(0.0, Dot3(view, half));
            throughput = SpecularFresnel(base, encoded.w, vh) * GgxVisibilityWeight(nv, Dot3(normal, direction), alpha);
            if (mixWeight > 0.0) {
                let pdf: f32 = VisibleGgxReflectionPdf(nv, Clamp(Dot3(normal, half), 0.0, 1.0), alpha);
                var guidePdf: f32 = 0.0;
                if (Dot3(guide, direction) >= coneCosine) { guidePdf = 1.0 / (6.28318530718 * (1.0 - coneCosine)); }
                // Both proposals trace hardware rays. Divide by the mixture PDF to preserve the GGX integral.
                throughput = throughput * (pdf / (pdf * (1.0 - mixWeight) + guidePdf * mixWeight));
                usedGuide = true;
            }
        }
    }
    var geometric: float3 = GeometricNormal(identity, pixel, viewport, Geometry[identity.x - 1], Vertices, Indices, Parameters);
    if (Dot3(geometric, view) < 0.0) { geometric = geometric * -1.0; }
    let writeHit: bool = Parameters[31] > 0.5;
    if (Dot3(direction, geometric) <= 0.0 || (sampledGloss && Dot3(normal, direction) <= 0.0)) {
        if (sampledGloss) {
            // A rejected GGX direction contributes zero to the estimate.
            Output[target] = 0.0;
            Output[target + 1] = 0.0;
            Output[target + 2] = 0.0;
            Output[target + 3] = -coverage;
            if (writeHit) {
                ReflectionHits[index * 2] = 0;
                ReflectionHits[index * 2 + 1] = 1;
            }
        }
        return;
    }
    let rayOrigin: float3 = position + geometric * 0.0005;
    let hit: u32 = raySlot * 16;
    var limit: f32 = ReflectionRayRange;
    var flags: u32 = 0;
    if (useScreenBounds) {
        let cx: float4 = Read4(Parameters, 32);
        let cy: float4 = Read4(Parameters, 36);
        let cz: float4 = Read4(Parameters, 40);
        let cw: float4 = Read4(Parameters, 44);
        // The resolved receiver already supplies clip position up to the homogeneous scale.
        let start: float4 = float4(uv.x * 2.0 - 1.0, uv.y * 2.0 - 1.0, depth, 1.0) * PointRow(cw, position);
        let delta: float4 = float4(DirectionRow(cx, direction), DirectionRow(cy, direction), DirectionRow(cz, direction), DirectionRow(cw, direction));
        var previous: uint2 = uint2(identity.x, 0);
        var step: f32 = 0.55;
        for (let tap of BoundedIndices<4>(4)) {
            if (limit == ReflectionRayRange) {
                let pixel: uint2 = ScreenPixel(start + delta * step, viewport);
                step = step * 2.4;
                if (pixel.x != 0xffffffff) {
                    let observed: uint2 = Load(Visibility, pixel);
                    if (observed.x != 0 && observed.x != identity.x && (observed.x != previous.x || observed.y != previous.y)) {
                        previous = observed;
                        limit = ScreenTriangleLimit(observed, rayOrigin, direction, Geometry[observed.x - 1], Vertices, Indices);
                    }
                }
            }
        }
        if (limit < ReflectionRayRange) { flags = ScreenCandidateBit; }
    }
    var trace: bool = true;
    for (let attempt of BoundedIndices<2>(2)) {
        if (trace) {
            SetReflectionRay(Rays, raySlot, rayOrigin, direction, limit);
            RayQueryTraceRetained(Scene, Procedurals, Rays, Hits, Shading, raySlot, AffineAnalyticIntersection);
            // A screen candidate can be filtered by the TLAS. A miss or a hit at
            // the clipping boundary needs the original range and tie behavior.
            trace = limit < ReflectionRayRange && (Hits[hit + 2] < 0.5 || Hits[hit + 4] > limit - 0.0001);
            if (trace) {
                flags = flags | ScreenRetryBit;
                limit = ReflectionRayRange;
            }
        }
    }

    let sky: float3 = float3(Parameters[56], Parameters[57], Parameters[58]);
    let ground: float3 = float3(Parameters[60], Parameters[61], Parameters[62]);
    if (writeHit) {
        ReflectionHits[index * 2] = U32(Hits[hit + 3] + 1.0);
        // Zero identifies a sky miss. Hits store distance in centimetres plus two.
        var distance: u32 = 0;
        if (Hits[hit + 2] > 0.5) { distance = U32(Hits[hit + 4] * 100.0 + 2.5); }
        if (usedGuide) { distance = distance | ReflectionGuideBit; }
        ReflectionHits[index * 2 + 1] = distance | flags;
    }
    var radiance: float3 = float3(0.0, 0.0, 0.0);
    if (Hits[hit + 2] < 0.5) {
        if (Parameters[29] > 0.5) {
            let sample: float4 = SampleLevel(SkySource, LinearClamp, EnvironmentUv(direction), 0.0);
            radiance = float3(sample.x, sample.y, sample.z) * Parameters[25];
        } else if (Parameters[24] > 0.5 && Parameters[30] < 0.5) {
            let sample: float4 = SampleLevel(Environment, LinearClamp, EnvironmentBandUv(EncodeOcta(direction), 0.0), 0.0);
            radiance = float3(sample.x, sample.y, sample.z) * Parameters[25];
        } else {
            radiance = HemisphereRadiance(direction, sky, ground);
        }
    } else {
        let instanceId: u32 = U32(Hits[hit + 3]);
        let entry: BindlessMaterialRecord = Materials[InstanceMaterials[instanceId]];
        let front: bool = Hits[hit + 15] > 0.0;
        if (front || entry.surface.flags.z > 0.5) {
            let attributes: ResidentAttributes = SecondaryAttributes(Hits, Procedurals, raySlot, Geometry[instanceId], Vertices, Indices);
            let uv: float2 = attributes.uv;
            let baseSample: float4 = SampleLevel(Textures[NonUniform(entry.baseTexture)], Samplers[NonUniform(entry.baseSampler)], uv, 0.0);
            let emissiveSample: float4 = SampleLevel(Textures[NonUniform(entry.emissiveTexture)], Samplers[NonUniform(entry.emissiveSampler)], uv, 0.0);
            let normalSample: float4 = SampleLevel(Textures[NonUniform(entry.normalTexture)], Samplers[NonUniform(entry.normalSampler)], uv, 0.0);
            let metalSample: float4 = SampleLevel(Textures[NonUniform(entry.metalTexture)], Samplers[NonUniform(entry.metalSampler)], uv, 0.0);
            let occlusionSample: float4 = SampleLevel(Textures[NonUniform(entry.occlusionTexture)], Samplers[NonUniform(entry.occlusionSampler)], uv, 0.0);
            let surface: SurfaceData = EvaluateSurfaceSamples(attributes.normal, attributes.tangent, front,
                entry.surface, baseSample * entry.surface.baseColor * attributes.color, emissiveSample, normalSample, metalSample, occlusionSample);
            let base: float3 = float3(surface.color.x, surface.color.y, surface.color.z);
            radiance = float3(surface.emission.x, surface.emission.y, surface.emission.z) * Parameters[23];
            if (surface.emission.w < 0.0) {
                radiance = radiance + base;
            } else {
                let point: float3 = float3(Hits[hit + 8], Hits[hit + 9], Hits[hit + 10]);
                let face: float3 = float3(Hits[hit + 12], Hits[hit + 13], Hits[hit + 14]);
                var shadingNormal: float3 = float3(surface.normal.x, surface.normal.y, surface.normal.z);
                if (Dot3(shadingNormal, face) <= 0.0 || Dot3(shadingNormal, direction) >= 0.0) { shadingNormal = face; }
                let metallic: f32 = surface.normal.w;
                let hitRoughness: f32 = surface.color.w;
                let response: f32 = Parameters[22];
                let outgoing: float3 = direction * -1.0;
                let origin: float3 = point + face * 0.0005;
                var ambient: float3 = HemisphereLight(base, shadingNormal, outgoing, sky, ground, metallic, hitRoughness);
                if (Parameters[24] > 0.5) {
                    let diffuse: float4 = SampleLevel(Environment, LinearClamp, EnvironmentBandUv(EncodeOcta(shadingNormal), 6.0), 0.0);
                    ambient = base * float3(diffuse.x, diffuse.y, diffuse.z) * (0.96 * (1.0 - metallic) * Parameters[25]);
                }
                radiance = radiance + ambient * surface.emission.w;
                let sun: float3 = float3(Parameters[48], Parameters[49], Parameters[50]);
                let sunEnergy: float4 = Read4(Parameters, 52);
                if (sunEnergy.w > 0.0 && Dot3(face, sun) > 0.0 && Dot3(shadingNormal, sun) > 0.0) {
                    var visible: bool = true;
                    if (Parameters[51] > 0.5) {
                        SetReflectionRay(Rays, raySlot, origin, sun, 10000.0);
                        RayQueryTraceOccluded(Scene, Procedurals, Rays, Occlusion, Instances, raySlot, AffineAnalyticIntersection);
                        visible = Occlusion[raySlot] < 0.5;
                    }
                    if (visible) {
                        radiance = radiance + DirectLightModel(base, shadingNormal, outgoing, sun, metallic, hitRoughness, response)
                            * float3(sunEnergy.x, sunEnergy.y, sunEnergy.z) * sunEnergy.w;
                    }
                }
                for (let light of BoundedIndices<256>(U32(Parameters[26]))) {
                    let offset: u32 = 64 + light * 16;
                    let sphere: float4 = Read4(Parameters, offset);
                    let delta: float3 = float3(sphere.x, sphere.y, sphere.z) - point;
                    if (Dot3(delta, delta) < sphere.w * sphere.w) {
                        let config: float4 = Read4(Parameters, offset + 12);
                        let emitter: EmitterSample3D = SampleLocalEmitter(point, sphere, Read4(Parameters, offset + 8), config, light * 137 + 113);
                        if (emitter.attenuation > 0.0 && Dot3(face, emitter.direction) > 0.0 && Dot3(shadingNormal, emitter.direction) > 0.0) {
                            var visible: bool = true;
                            if (config.z > 0.5) {
                                let segment: float3 = emitter.endpoint - origin;
                                let distance: f32 = Sqrt(Dot3(segment, segment));
                                SetReflectionRay(Rays, raySlot, origin, Unit(segment), Max(distance - 0.0005, 0.0002));
                                RayQueryTraceOccluded(Scene, Procedurals, Rays, Occlusion, Instances, raySlot, AffineAnalyticIntersection);
                                visible = Occlusion[raySlot] < 0.5;
                            }
                            if (visible) {
                                let color: float4 = Read4(Parameters, offset + 4);
                                radiance = radiance + DirectLightModel(base, shadingNormal, outgoing, emitter.direction, metallic, hitRoughness, response)
                                    * float3(color.x, color.y, color.z) * (color.w * emitter.attenuation);
                            }
                        }
                    }
                }
            }
        }
    }
    if (glossy) {
        radiance = radiance * throughput;
        coverage = -coverage;
    }
    Output[target] = radiance.x;
    Output[target + 1] = radiance.y;
    Output[target + 2] = radiance.z;
    Output[target + 3] = coverage;
}
