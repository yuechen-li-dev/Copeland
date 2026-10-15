import { WaterDifferential, WindWave, AddWave, ImpactWave, WaterNormal, DetailNormal } from "./WaterGeometry";
import { EnvironmentSurfaceResponse, BoxReflection, ConeAttenuation, WorldAt, PrefilteredEnvironment, EncodeOcta } from "./SurfaceLighting";
import { VolumeAt, ComposeVolume } from "./VolumeLighting";
import { Fogged } from "./HeightFog";
import { Unit, Sub3, ShadowVisibility, PlaneShadowVisibility, Dot3, Scale3, Mul3, DirectLightResponse, HemisphereResponse, Add3, Cross3, LimitRadiance } from "./Lighting3D";

@space(world.position)
type WorldPosition3 = float3;
@space(clip.position)
type ClipPosition4 = float4;

@material
@binding(0)
record StaticModelMaterial {
    clipX: float4;
    clipY: float4;
    clipZ: float4;
    clipW: float4;
    eye: float4;
    baseColor: float4;
    factors: float4;
    emissiveAlpha: float4;
    flags: float4;
    light: float4;
    sun: float4;
    sky: float4;
    ground: float4;
    shadowX: float4;
    shadowY: float4;
    shadowZ: float4;
    shadowW: float4;
    shadowParameters: float4;
    cascadeMidX: float4;
    cascadeMidY: float4;
    cascadeMidZ: float4;
    cascadeMidW: float4;
    cascadeFarX: float4;
    cascadeFarY: float4;
    cascadeFarZ: float4;
    cascadeFarW: float4;
    cascadeEnds: float4;
    cascadeForward: float4;
    cascadeBias: float4;
    environmentParameters: float4;
    probePosition: float4;
    probeBounds: float4;
    fogParameters: float4;
    fogColor: float4;
    surfaceTexels: float4;
    spotAX: float4;
    spotAY: float4;
    spotAZ: float4;
    spotAW: float4;
    spotBX: float4;
    spotBY: float4;
    spotBZ: float4;
    spotBW: float4;
    volumeGrid: float4;
    volumeDepth: float4;
    refraction: float4;
    absorption: float4;
    inverseX: float4;
    inverseY: float4;
    inverseZ: float4;
    inverseW: float4;
    waterProfile: float4;
    waterFoam: float4;
    waterDeep: float4;
    waterTime: float4;
    waterPreviousX: float4;
    waterPreviousY: float4;
    waterPreviousZ: float4;
    waterPreviousW: float4;
    waterReflectionX: float4;
    waterReflectionY: float4;
    waterReflectionZ: float4;
    waterReflectionW: float4;
    wave0: float4;
    wave0Behavior: float4;
    wave1: float4;
    wave1Behavior: float4;
    wave2: float4;
    wave2Behavior: float4;
    wave3: float4;
    wave3Behavior: float4;
    impulse0: float4;
    impulse0Behavior: float4;
    impulse1: float4;
    impulse1Behavior: float4;
    impulse2: float4;
    impulse2Behavior: float4;
    impulse3: float4;
    impulse3Behavior: float4;
    impulse4: float4;
    impulse4Behavior: float4;
    impulse5: float4;
    impulse5Behavior: float4;
    impulse6: float4;
    impulse6Behavior: float4;
    impulse7: float4;
    impulse7Behavior: float4;
}

stream VertexInput {
    @location(0) position: WorldPosition3;
    @location(1) normal: float3;
    @location(2) color: float4;
    @location(3) uv: float2;
    @location(4) tangent: float4;
}

stream MaterialResources {
    @binding(0) material: StaticModelMaterial;
}
stream Resources {
    @binding(1) baseMap: Texture2D<float4>;
    @binding(2) baseSampler: Sampler;
    @binding(3) metalMap: Texture2D<float4>;
    @binding(4) metalSampler: Sampler;
    @binding(5) normalMap: Texture2D<float4>;
    @binding(6) normalSampler: Sampler;
    @binding(7) occlusionMap: Texture2D<float4>;
    @binding(8) occlusionSampler: Sampler;
    @binding(9) emissiveMap: Texture2D<float4>;
    @binding(10) emissiveSampler: Sampler;
    @binding(11) shadow: Texture2D<float4>;
    @binding(12) shadowSampler: Sampler;
    @binding(13) environment: Texture2D<float4>;
    @binding(14) environmentSampler: Sampler;
    @binding(15) motion: Texture2D<float4>;
    @binding(16) motionSampler: Sampler;
    @binding(17) lights: Texture2D<float4>;
    @binding(18) lightsSampler: Sampler;
    @binding(19) cascadeMid: Texture2D<float4>;
    @binding(20) cascadeMidSampler: Sampler;
    @binding(21) cascadeFar: Texture2D<float4>;
    @binding(22) cascadeFarSampler: Sampler;
    @binding(23) spotA: Texture2D<float4>;
    @binding(24) spotASampler: Sampler;
    @binding(25) spotB: Texture2D<float4>;
    @binding(26) spotBSampler: Sampler;
    @binding(27) volume: Texture2D<float4>;
    @binding(28) volumeSampler: Sampler;
    @binding(29) scene: Texture2D<float4>;
    @binding(30) sceneSampler: Sampler;
    @binding(31) planar: Texture2D<float4>;
    @binding(32) planarSampler: Sampler;
}

stream ModelVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) world: float3;
    @location(1) normal: float3;
    @location(2) color: float4;
    @location(3) uv: float2;
    @location(4) tangent: float4;
    @location(5) previous: float4;
    @location(6) rest: float3;
}
stream PixelBuiltins { @builtin(front_face) frontFace: bool; }
stream ModelOutput {
    @target(0) color: float4;
    @target(1) optical: float4;
    @target(2) motion: float4;
}

function ProjectRow(row: float4, p: WorldPosition3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
function Row(row: float4, p: float3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
function WaterAt(p: float3, time: f32, material: StaticModelMaterial): WaterDifferential {
    var result: WaterDifferential = { offset: float3(0.0, 0.0, 0.0), dx: float3(0.0, 0.0, 0.0), dz: float3(0.0, 0.0, 0.0) };
    result = AddWave(result, WindWave(p, time, material.wave0, material.wave0Behavior));
    result = AddWave(result, WindWave(p, time, material.wave1, material.wave1Behavior));
    result = AddWave(result, WindWave(p, time, material.wave2, material.wave2Behavior));
    result = AddWave(result, WindWave(p, time, material.wave3, material.wave3Behavior));
    result = AddWave(result, ImpactWave(p, time, material.impulse0, material.impulse0Behavior));
    result = AddWave(result, ImpactWave(p, time, material.impulse1, material.impulse1Behavior));
    result = AddWave(result, ImpactWave(p, time, material.impulse2, material.impulse2Behavior));
    result = AddWave(result, ImpactWave(p, time, material.impulse3, material.impulse3Behavior));
    result = AddWave(result, ImpactWave(p, time, material.impulse4, material.impulse4Behavior));
    result = AddWave(result, ImpactWave(p, time, material.impulse5, material.impulse5Behavior));
    result = AddWave(result, ImpactWave(p, time, material.impulse6, material.impulse6Behavior));
    result = AddWave(result, ImpactWave(p, time, material.impulse7, material.impulse7Behavior));
    return result;
}
@vertex
function VertexMain(input: VertexInput, uniforms: MaterialResources): ModelVaryings {
    const rest: float3 = float3(input.position.x, input.position.y, input.position.z);
    var p: float3 = rest;
    var previous: float3 = rest;
    var normal: float3 = input.normal;
    if (uniforms.material.waterProfile.x > 0.5) {
        const water: WaterDifferential = WaterAt(rest, uniforms.material.waterTime.x, uniforms.material);
        p = Add3(rest, water.offset);
        normal = WaterNormal(water);
        previous = Add3(rest, WaterAt(rest, uniforms.material.waterTime.y, uniforms.material).offset);
    }
    return {
        position: float4(Row(uniforms.material.clipX, p), Row(uniforms.material.clipY, p),
            Row(uniforms.material.clipZ, p), Row(uniforms.material.clipW, p)),
        world: p,
        normal: normal,
        color: input.color,
        uv: input.uv,
        tangent: input.tangent,
        previous: float4(Row(uniforms.material.waterPreviousX, previous), Row(uniforms.material.waterPreviousY, previous),
            Row(uniforms.material.waterPreviousZ, previous), Row(uniforms.material.waterPreviousW, previous)),
        rest: rest,
    };
}

@pixel
function PixelMain(input: ModelVaryings, uniforms: MaterialResources, resources: Resources, builtins: PixelBuiltins): ModelOutput {
    if (!builtins.frontFace && uniforms.material.flags.z < 0.5) {
        Discard();
    }
    const base: float4 = Sample(resources.baseMap, resources.baseSampler, input.uv) * uniforms.material.baseColor * input.color;
    if (uniforms.material.flags.y > 0.5 && base.w < uniforms.material.emissiveAlpha.w) {
        Discard();
    }
    const uv: float2 = float2(input.position.x * uniforms.material.surfaceTexels.x,
        input.position.y * uniforms.material.surfaceTexels.y);
    const opaque: float4 = Sample(resources.motion, resources.motionSampler, uv);
    if (input.position.z >= opaque.w) {
        Discard();
    }
    const emission: float4 = Sample(resources.emissiveMap, resources.emissiveSampler, input.uv);
    const emitted: float3 = Mul3(float3(emission.x, emission.y, emission.z),
        float3(uniforms.material.emissiveAlpha.x, uniforms.material.emissiveAlpha.y, uniforms.material.emissiveAlpha.z));
    const nt: float4 = Sample(resources.normalMap, resources.normalSampler, input.uv);
    const tangent: float3 = Unit(float3(input.tangent.x, input.tangent.y, input.tangent.z));
    const geometric: float3 = Unit(input.normal);
    const bitangent: float3 = Scale3(Cross3(geometric, tangent), input.tangent.w);
    var normal: float3 = Unit(Add3(Add3(Scale3(tangent, (nt.x * 2.0 - 1.0) * uniforms.material.factors.z),
        Scale3(bitangent, (nt.y * 2.0 - 1.0) * uniforms.material.factors.z)), Scale3(geometric, nt.z * 2.0 - 1.0)));
    if (!builtins.frontFace) {
        normal = Scale3(normal, -1.0);
    }
    const mr: float4 = Sample(resources.metalMap, resources.metalSampler, input.uv);
    const metallic: f32 = Clamp(mr.z * uniforms.material.factors.x, 0.0, 1.0);
    const roughness: f32 = Clamp(mr.y * uniforms.material.factors.y, 0.045, 1.0);
    const ao: float4 = Sample(resources.occlusionMap, resources.occlusionSampler, input.uv);
    var occlusion: f32 = 1.0 - uniforms.material.factors.w + ao.x * uniforms.material.factors.w;
    if (uniforms.material.flags.x > 0.5) {
        occlusion = -1.0;
    }
    const p: float3 = input.world;
    const water: bool = uniforms.material.waterProfile.x > 0.5;
    if (water) {
        normal = DetailNormal(normal, input.rest, uniforms.material.waterTime.x,
            uniforms.material.waterProfile.y, uniforms.material.waterProfile.z);
    }
    const eye: float3 = float3(uniforms.material.eye.x, uniforms.material.eye.y, uniforms.material.eye.z);
    const view: float3 = Unit(Sub3(eye, p));
    const light: float3 = Unit(float3(uniforms.material.light.x, uniforms.material.light.y, uniforms.material.light.z));
    const sun: float3 = float3(uniforms.material.sun.x, uniforms.material.sun.y, uniforms.material.sun.z);
    const ior: f32 = uniforms.material.refraction.y;
    const iorRatio: f32 = (ior - 1.0) / (ior + 1.0);
    const f0: f32 = iorRatio * iorRatio;
    const transmissionRoughness: f32 = roughness * Clamp(ior * 2.0 - 2.0, 0.0, 1.0);
    const transmissionFactor: f32 = uniforms.material.refraction.x;
    const rgb: float3 = Scale3(float3(base.x, base.y, base.z), 1.0 - transmissionFactor);
    var lit: float3 = Add3(rgb, emitted);
    if (uniforms.material.flags.x < 0.5) {
        const visibility: f32 = DirectionalVisibility(resources, uniforms, p, normal);
        var direct: float3 = Scale3(Mul3(DirectLightResponse(rgb, normal, view, light, metallic, roughness, f0), sun),
            uniforms.material.sun.w * visibility);
        var ambient: float3 = HemisphereResponse(rgb, normal, view,
            float3(uniforms.material.sky.x, uniforms.material.sky.y, uniforms.material.sky.z),
            float3(uniforms.material.ground.x, uniforms.material.ground.y, uniforms.material.ground.z), metallic, roughness, f0);
        if (uniforms.material.environmentParameters.y > 0.5) {
            const reflected: float3 = BoxReflection(p, Sub3(Scale3(normal, 2.0 * Max(Dot3(normal, view), 0.0)), view),
                uniforms.material.probePosition, uniforms.material.probeBounds);
            ambient = Scale3(EnvironmentSurfaceResponse(resources.environment, resources.environmentSampler, rgb, normal, reflected,
                Max(Dot3(normal, view), 0.0001), metallic, roughness, f0), uniforms.material.environmentParameters.z);
        }
        for (var index: u32 = 0; index < 32; index = index + 1) {
            if (Convert<f32>(index) < uniforms.material.environmentParameters.x) {
                const row: f32 = (Convert<f32>(index) + 0.5) / 32.0;
                const sphere: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.125, row));
                const color: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.375, row));
                const cone: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.625, row));
                const config: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.875, row));
                const delta: float3 = Sub3(float3(sphere.x, sphere.y, sphere.z), p);
                const squared: f32 = Dot3(delta, delta);
                if (squared < sphere.w * sphere.w) {
                    const direction: float3 = Unit(delta);
                    const ratio: f32 = squared / (sphere.w * sphere.w);
                    const fade: f32 = Max(1.0 - ratio * ratio, 0.0);
                    var attenuation: f32 = fade * fade / Max(squared, 0.01) * ConeAttenuation(direction, cone, config);
                    if (config.z > 0.5) {
                        attenuation = attenuation * LocalShadow(resources, uniforms, config.z, p, Max(Dot3(normal, direction), 0.0));
                    }
                    direct = Add3(direct, Scale3(Mul3(DirectLightResponse(rgb, normal, view, direction, metallic, roughness, f0),
                        float3(color.x, color.y, color.z)), color.w * attenuation));
                }
            }
        }
        lit = Add3(Add3(direct, Scale3(ambient, occlusion)), emitted);
    }
    const nv: f32 = Max(Dot3(normal, view), 0.00001);
    const eta: f32 = 1.0 / ior;
    const cosine: f32 = Sqrt(Max(1.0 - eta * eta * (1.0 - nv * nv), 0.0));
    const incident: float3 = Scale3(view, -1.0);
    const refracted: float3 = Unit(Add3(Scale3(incident, eta), Scale3(normal, eta * nv - cosine)));
    // Authored thickness models a parallel slab. Thin sheets have zero offset.
    var path: f32 = uniforms.material.refraction.z / Max(cosine, 0.05);
    var waterDepth: f32 = uniforms.material.waterProfile.w;
    if (water && opaque.w < 1.0) {
        const bottom: float3 = WorldAt(uv, opaque.w, uniforms.material.inverseX,
            uniforms.material.inverseY, uniforms.material.inverseZ, uniforms.material.inverseW);
        waterDepth = Clamp(p.y - bottom.y, 0.0, uniforms.material.waterProfile.w);
        path = Min(Sqrt(Dot3(Sub3(bottom, p), Sub3(bottom, p))), uniforms.material.waterProfile.w);
    }
    var exitPoint: float3 = Add3(p, Scale3(refracted, path));
    var traceDirection: float3 = incident;
    if (water) {
        exitPoint = p;
        traceDirection = refracted;
    }
    var transmitted: float3 = float3(0.0, 0.0, 0.0);
    if (uniforms.material.environmentParameters.y > 0.5) {
        transmitted = Scale3(PrefilteredEnvironment(resources.environment, resources.environmentSampler,
            EncodeOcta(incident), transmissionRoughness), uniforms.material.environmentParameters.z);
    }
    var found: bool = false;
    var hitUv: float2 = uv;
    var hitPoint: float3 = Add3(exitPoint, Scale3(incident, uniforms.material.fogColor.w));
    var previousDistance: f32 = 0.0;
    var traceDistance: f32 = uniforms.material.surfaceTexels.z;
    var steps: f32 = 64.0;
    if (water) { traceDistance = Min(traceDistance, uniforms.material.waterProfile.w * 1.8); }
    if (water) { steps = 32.0; }
    // Fixed world-length march; no unbounded loops or CPU/GPU readback.
    for (var step: u32 = 0; step < 64; step = step + 1) {
        if (Convert<f32>(step) < steps && !found) {
            const fraction: f32 = (Convert<f32>(step) + 1.0) / steps;
            const distance: f32 = fraction * fraction * traceDistance;
            const samplePoint: float3 = Add3(exitPoint, Scale3(traceDirection, distance));
            const divisor: f32 = Row(uniforms.material.clipW, samplePoint);
            if (divisor > 0.00001) {
                const coordinate: float2 = float2(Row(uniforms.material.clipX, samplePoint) / divisor * 0.5 + 0.5,
                    Row(uniforms.material.clipY, samplePoint) / divisor * 0.5 + 0.5);
                if (coordinate.x >= 0.0 && coordinate.x <= 1.0 && coordinate.y >= 0.0 && coordinate.y <= 1.0) {
                    const background: float4 = Sample(resources.motion, resources.motionSampler, coordinate);
                    if (background.w < 1.0) {
                        const world: float3 = WorldAt(coordinate, background.w, uniforms.material.inverseX,
                            uniforms.material.inverseY, uniforms.material.inverseZ, uniforms.material.inverseW);
                        const separation: f32 = Dot3(Sub3(samplePoint, world), traceDirection);
                        const tolerance: f32 = Max(distance - previousDistance, 0.02);
                        var minimumBehind: f32 = path * 0.5;
                        if (water) { minimumBehind = 0.0001; }
                        // Reject a foreground occluder instead of pulling its color through glass.
                        if (separation >= 0.0 && separation <= tolerance * 1.5
                            && Dot3(Sub3(world, p), incident) > minimumBehind
                            && (!water || world.y <= p.y)) {
                            found = true;
                            hitUv = coordinate;
                            hitPoint = world;
                            if (water) {
                                // Refine the first crossed depth interval rather than
                                // exposing the coarse march as bands in the water.
                                var near: f32 = previousDistance;
                                var far: f32 = distance;
                                for (var refine: u32 = 0; refine < 6; refine = refine + 1) {
                                    const middle: f32 = (near + far) * 0.5;
                                    const candidate: float3 = Add3(p, Scale3(traceDirection, middle));
                                    const refinementDivisor: f32 = Row(uniforms.material.clipW, candidate);
                                    const refinementUv: float2 = float2(Row(uniforms.material.clipX, candidate) / refinementDivisor * 0.5 + 0.5,
                                        Row(uniforms.material.clipY, candidate) / refinementDivisor * 0.5 + 0.5);
                                    const refinementDepth: float4 = Sample(resources.motion, resources.motionSampler, refinementUv);
                                    if (Row(uniforms.material.clipZ, candidate) / refinementDivisor >= refinementDepth.w) {
                                        far = middle;
                                        hitUv = refinementUv;
                                        hitPoint = WorldAt(refinementUv, refinementDepth.w, uniforms.material.inverseX,
                                            uniforms.material.inverseY, uniforms.material.inverseZ, uniforms.material.inverseW);
                                    } else {
                                        near = middle;
                                    }
                                }
                                path = Min(Sqrt(Dot3(Sub3(hitPoint, p), Sub3(hitPoint, p))), uniforms.material.waterProfile.w);
                            }
                        }
                    }
                }
            }
            previousDistance = distance;
        }
    }
    const traced: bool = found;
    if (water && !found) {
        transmitted = float3(uniforms.material.waterDeep.x, uniforms.material.waterDeep.y, uniforms.material.waterDeep.z);
        // Screen-space rays cannot recover a floor hidden behind a foreground
        // rim. Prefer a depth-qualified undistorted background over a hard hole.
        if (opaque.w < 1.0) {
            const world: float3 = WorldAt(uv, opaque.w, uniforms.material.inverseX,
                uniforms.material.inverseY, uniforms.material.inverseZ, uniforms.material.inverseW);
            if (world.y < p.y && Dot3(Sub3(world, p), incident) > 0.0) {
                const color: float4 = Sample(resources.scene, resources.sceneSampler, uv);
                transmitted = float3(color.x, color.y, color.z);
                hitPoint = world;
                hitUv = uv;
                found = true;
            }
        }
    }
    var radius: f32 = transmissionRoughness * transmissionRoughness * 24.0;
    if (water) {
        const footprint: f32 = Max(Fwidth(hitUv.x) / uniforms.material.surfaceTexels.x,
            Fwidth(hitUv.y) / uniforms.material.surfaceTexels.y);
        radius = Max(radius, Clamp(footprint * 0.5, 0.75, 3.0));
    }
    if (found) {
        const color: float4 = Sample(resources.scene, resources.sceneSampler, hitUv);
        transmitted = float3(color.x, color.y, color.z);
        var sum: float3 = transmitted;
        var count: f32 = 1.0;
        for (var tap: u32 = 0; tap < 4; tap = tap + 1) {
            var offset: float2 = float2(radius * uniforms.material.surfaceTexels.x, 0.0);
            if (tap == 1) { offset = float2(-radius * uniforms.material.surfaceTexels.x, 0.0); }
            else if (tap == 2) { offset = float2(0.0, radius * uniforms.material.surfaceTexels.y); }
            else if (tap == 3) { offset = float2(0.0, -radius * uniforms.material.surfaceTexels.y); }
            const coordinate: float2 = float2(hitUv.x + offset.x, hitUv.y + offset.y);
            const depth: float4 = Sample(resources.motion, resources.motionSampler, coordinate);
            if (coordinate.x >= 0.0 && coordinate.x <= 1.0 && coordinate.y >= 0.0 && coordinate.y <= 1.0 && depth.w < 1.0) {
                const world: float3 = WorldAt(coordinate, depth.w, uniforms.material.inverseX,
                    uniforms.material.inverseY, uniforms.material.inverseZ, uniforms.material.inverseW);
                if (Dot3(Sub3(world, p), incident) > path * 0.5 && Abs(Dot3(Sub3(world, hitPoint), incident)) < 0.5) {
                    const sampleColor: float4 = Sample(resources.scene, resources.sceneSampler, coordinate);
                    sum = Add3(sum, float3(sampleColor.x, sampleColor.y, sampleColor.z));
                    count = count + 1.0;
                }
            }
        }
        transmitted = Scale3(sum, 1.0 / count);
    }
    if (uniforms.material.volumeGrid.w > 0.5) {
        const backDelta: float3 = Sub3(hitPoint, eye);
        const exitDelta: float3 = Sub3(exitPoint, eye);
        const back: float4 = VolumeAt(resources.volume, resources.volumeSampler, hitUv, Sqrt(Dot3(backDelta, backDelta)),
            uniforms.material.volumeGrid, uniforms.material.volumeDepth);
        const front: float4 = VolumeAt(resources.volume, resources.volumeSampler, hitUv, Sqrt(Dot3(exitDelta, exitDelta)),
            uniforms.material.volumeGrid, uniforms.material.volumeDepth);
        const scale: f32 = 1.0 / Max(front.w, 0.00001);
        transmitted = ComposeVolume(transmitted, float4(Max(back.x - front.x, 0.0) * scale,
            Max(back.y - front.y, 0.0) * scale, Max(back.z - front.z, 0.0) * scale, Clamp(back.w * scale, 0.0, 1.0)));
    } else {
        transmitted = Fogged(transmitted, hitPoint, exitPoint, uniforms.material.fogParameters, uniforms.material.fogColor);
    }
    const absorption: float4 = uniforms.material.absorption;
    if (absorption.w > 0.0) {
        transmitted = Mul3(transmitted, float3(Pow(Max(absorption.x, 0.000001), path / absorption.w),
            Pow(Max(absorption.y, 0.000001), path / absorption.w), Pow(Max(absorption.z, 0.000001), path / absorption.w)));
    }
    if (water) {
        const absorptionColor: float3 = float3(uniforms.material.waterDeep.x, uniforms.material.waterDeep.y, uniforms.material.waterDeep.z);
        const scatter: f32 = 1.0 - Exp(-path * 0.18);
        transmitted = Add3(Scale3(transmitted, 1.0 - scatter), Scale3(absorptionColor, scatter));
    }
    transmitted = Mul3(transmitted, float3(base.x, base.y, base.z));
    var fresnel: f32 = f0 + (1.0 - f0) * Pow(1.0 - nv, 5.0);
    if (ior == 1.0) { fresnel = 0.0; }
    lit = Add3(lit, Scale3(transmitted, transmissionFactor * (1.0 - fresnel)));
    if (water && uniforms.material.waterTime.w > 0.5) {
        const reflected: float3 = Unit(Sub3(Scale3(normal, 2.0 * Dot3(normal, view)), view));
        const planarPoint: float3 = Add3(input.rest, Scale3(Sub3(normal, float3(0.0, 1.0, 0.0)), 0.12));
        const divisor: f32 = Row(uniforms.material.waterReflectionW, planarPoint);
        const coordinate: float2 = float2(Row(uniforms.material.waterReflectionX, planarPoint) / Max(divisor, 0.00001) * 0.5 + 0.5,
            Row(uniforms.material.waterReflectionY, planarPoint) / Max(divisor, 0.00001) * 0.5 + 0.5);
        if (divisor > 0.0 && coordinate.x > 0.0 && coordinate.x < 1.0 && coordinate.y > 0.0 && coordinate.y < 1.0) {
            const planar: float4 = Sample(resources.planar, resources.planarSampler, coordinate);
            var fallback: float3 = float3(0.0, 0.0, 0.0);
            if (uniforms.material.environmentParameters.y > 0.5) {
                fallback = Scale3(PrefilteredEnvironment(resources.environment, resources.environmentSampler,
                    EncodeOcta(reflected), roughness), uniforms.material.environmentParameters.z);
            }
            // Replace the environment's specular term, retaining direct light.
            lit = Add3(lit, Scale3(Sub3(float3(planar.x, planar.y, planar.z), fallback), fresnel * Clamp(planar.w, 0.0, 1.0)));
        }
    }
    if (water) {
        const contact: f32 = 1.0 - Clamp(waterDepth / uniforms.material.waterFoam.x, 0.0, 1.0);
        const pattern: f32 = 0.6 + 0.4 * Sin(input.rest.x * 23.0 + Sin(input.rest.z * 19.0) + uniforms.material.waterTime.x * 1.7);
        const foam: f32 = contact * contact * pattern * uniforms.material.waterFoam.y;
        lit = Add3(Scale3(lit, 1.0 - foam), float3(foam * 0.8, foam * 0.85, foam * 0.8));
    }
    if (water) {
        const debug: f32 = uniforms.material.waterFoam.z;
        if (debug == 1.0) { lit = float3(waterDepth / uniforms.material.waterProfile.w, waterDepth / uniforms.material.waterProfile.w, waterDepth / uniforms.material.waterProfile.w); }
        if (debug == 2.0) { lit = float3(normal.x * 0.5 + 0.5, normal.y * 0.5 + 0.5, normal.z * 0.5 + 0.5); }
        if (debug == 3.0) {
            const contact: f32 = 1.0 - Clamp(waterDepth / uniforms.material.waterFoam.x, 0.0, 1.0);
            lit = float3(contact, contact, contact);
        }
        if (debug == 4.0) {
            lit = float3(1.0, 0.0, 0.0);
            if (traced) { lit = float3(0.0, 1.0, 0.0); }
        }
        if (debug == 5.0) {
            const divisor: f32 = Row(uniforms.material.waterReflectionW, input.rest);
            const coordinate: float2 = float2(Row(uniforms.material.waterReflectionX, input.rest) / Max(divisor, 0.00001) * 0.5 + 0.5,
                Row(uniforms.material.waterReflectionY, input.rest) / Max(divisor, 0.00001) * 0.5 + 0.5);
            const color: float4 = Sample(resources.planar, resources.planarSampler, coordinate);
            lit = float3(color.x, color.y, color.z);
        }
    }
    if (uniforms.material.volumeGrid.w > 0.5) {
        const frontDelta: float3 = Sub3(p, eye);
        lit = ComposeVolume(lit, VolumeAt(resources.volume, resources.volumeSampler, uv, Sqrt(Dot3(frontDelta, frontDelta)),
            uniforms.material.volumeGrid, uniforms.material.volumeDepth));
    } else {
        lit = Fogged(lit, p, eye, uniforms.material.fogParameters, uniforms.material.fogColor);
    }
    const bounded: float4 = LimitRadiance(float4(lit.x, lit.y, lit.z, 1.0));
    const divisor: f32 = Max(input.previous.w, 0.00001);
    var history: f32 = 0.0;
    if (water) { history = 1.0; }
    return {
        color: float4(bounded.x, bounded.y, bounded.z, history),
        optical: float4(1.0, 0.0, 0.0, 0.0),
        motion: float4(input.previous.x / divisor * 0.5 + 0.5, input.previous.y / divisor * 0.5 + 0.5,
            input.previous.z / divisor, input.position.z),
    };

}

function NearVisibility(resources: Resources, uniforms: MaterialResources, p: float3, normal: float3): f32 {
    const projected: float3 = float3(Row(uniforms.material.shadowX, p), Row(uniforms.material.shadowY, p), Row(uniforms.material.shadowZ, p));
    return PlaneShadowVisibility(resources.shadow, resources.shadowSampler, projected,
        float4(uniforms.material.shadowParameters.x, uniforms.material.cascadeBias.x, uniforms.material.shadowParameters.z, 0.0), normal, uniforms.material.shadowX, uniforms.material.shadowY, uniforms.material.shadowZ);
}
function MidVisibility(resources: Resources, uniforms: MaterialResources, p: float3, normal: float3): f32 {
    const projected: float3 = float3(Row(uniforms.material.cascadeMidX, p), Row(uniforms.material.cascadeMidY, p), Row(uniforms.material.cascadeMidZ, p));
    return PlaneShadowVisibility(resources.cascadeMid, resources.cascadeMidSampler, projected,
        float4(uniforms.material.shadowParameters.x, uniforms.material.cascadeBias.y, uniforms.material.shadowParameters.z, 0.0), normal, uniforms.material.cascadeMidX, uniforms.material.cascadeMidY, uniforms.material.cascadeMidZ);
}
function FarVisibility(resources: Resources, uniforms: MaterialResources, p: float3, normal: float3): f32 {
    const projected: float3 = float3(Row(uniforms.material.cascadeFarX, p), Row(uniforms.material.cascadeFarY, p), Row(uniforms.material.cascadeFarZ, p));
    return PlaneShadowVisibility(resources.cascadeFar, resources.cascadeFarSampler, projected,
        float4(uniforms.material.shadowParameters.x, uniforms.material.cascadeBias.z, uniforms.material.shadowParameters.z, 0.0), normal, uniforms.material.cascadeFarX, uniforms.material.cascadeFarY, uniforms.material.cascadeFarZ);
}
function DirectionalVisibility(resources: Resources, uniforms: MaterialResources, p: float3, normal: float3): f32 {
    const forward: float4 = uniforms.material.cascadeForward;
    const eye: float4 = uniforms.material.eye;
    const depth: f32 = (p.x - eye.x) * forward.x + (p.y - eye.y) * forward.y + (p.z - eye.z) * forward.z;
    const ends: float4 = uniforms.material.cascadeEnds;
    if (depth < ends.x * 0.9) {
        return NearVisibility(resources, uniforms, p, normal);
    }
    if (depth < ends.x) {
        const blend: f32 = (depth - ends.x * 0.9) / (ends.x * 0.1);
        return NearVisibility(resources, uniforms, p, normal) * (1.0 - blend) + MidVisibility(resources, uniforms, p, normal) * blend;
    }
    if (depth < ends.y * 0.9) {
        return MidVisibility(resources, uniforms, p, normal);
    }
    if (depth < ends.y) {
        const blend: f32 = (depth - ends.y * 0.9) / (ends.y * 0.1);
        return MidVisibility(resources, uniforms, p, normal) * (1.0 - blend) + FarVisibility(resources, uniforms, p, normal) * blend;
    }
    const fade: f32 = Clamp((depth - ends.z * 0.9) / (ends.z * 0.1), 0.0, 1.0);
    return FarVisibility(resources, uniforms, p, normal) * (1.0 - fade) + fade;
}
enum ShadowSource {
    First,
    Second,
}
function ShadowSourceFor(index: f32): ShadowSource {
    if (index < 1.5) {
        return ShadowSource.First;
    }
    return ShadowSource.Second;
}
function FirstShadow(resources: Resources, uniforms: MaterialResources, p: float3, nl: f32): f32 {
    const w: f32 = Row(uniforms.material.spotAW, p);
    if (w <= 0.00001) {
        return 1.0;
    }
    const projected: float3 = float3(Row(uniforms.material.spotAX, p) / w,
        Row(uniforms.material.spotAY, p) / w, Row(uniforms.material.spotAZ, p) / w);
    return ShadowVisibility(resources.spotA, resources.spotASampler, projected, float4(1.0, 0.0015, 1.0 / 512.0, 0.0), nl);
}
function SecondShadow(resources: Resources, uniforms: MaterialResources, p: float3, nl: f32): f32 {
    const w: f32 = Row(uniforms.material.spotBW, p);
    if (w <= 0.00001) {
        return 1.0;
    }
    const projected: float3 = float3(Row(uniforms.material.spotBX, p) / w,
        Row(uniforms.material.spotBY, p) / w, Row(uniforms.material.spotBZ, p) / w);
    return ShadowVisibility(resources.spotB, resources.spotBSampler, projected, float4(1.0, 0.0015, 1.0 / 512.0, 0.0), nl);
}
function LocalShadow(resources: Resources, uniforms: MaterialResources, index: f32, p: float3, nl: f32): f32 {
    return match ShadowSourceFor(index) {
        ShadowSource.First => FirstShadow(resources, uniforms, p, nl),
        ShadowSource.Second => SecondShadow(resources, uniforms, p, nl),
    };
}

