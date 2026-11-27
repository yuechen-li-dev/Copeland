import { EmitterSample3D, SampleLocalEmitter } from "./LocalEmitter3D";
import { WorldAt, Band, EncodeOcta, PrefilteredEnvironment, EnvironmentSurfaceResponseWeighted, EnvironmentDiffuse, EnvironmentDiffuseWeighted, MaskAllows, BoxReflection, LocalAttenuation } from "./SurfaceLighting";
import { Unit, Sub3, Add3, Scale3, Mul3, Dot3, DirectDiffuseModel, HemisphereLight, SpecularFresnel, ShadowVisibility, PlaneShadowVisibility, LimitRadiance } from "./Lighting3D";
import { CoatLayer, CoatSubstrate, CoatLookup, CoatBase, OpenPbrCoat, CoatSpecular, CoatTransmission } from "./OpenPbrCoat3D";
import { SheenLayer, OpenPbrSheen, SheenAxis, SheenPdf, LayeredLightResponse } from "./OpenPbrSheen3D";
import { AnisotropyLayer, DecodeAnisotropy, AnisotropyLookup } from "./OpenPbrAnisotropy3D";
import { CompiledDiffuse } from "./CompiledDiffuseLighting";

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
    light: float4;
    sun: float4;
    sky: float4;
    ground: float4;
    shadowX: float4;
    shadowY: float4;
    shadowZ: float4;
    shadowW: float4;
    shadowParameters: float4;
    parameters: float4;
    clear: float4;
    aoParameters: float4;
    surfaceTexels: float4;
    probePosition: float4;
    probeBounds: float4;
    spotAX: float4;
    spotAY: float4;
    spotAZ: float4;
    spotAW: float4;
    spotBX: float4;
    spotBY: float4;
    spotBZ: float4;
    spotBW: float4;
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
    reflectionParameters: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) base: Texture2D<float4>;
    @binding(2) baseSampler: Sampler;
    @binding(3) motion: Texture2D<float4>;
    @binding(4) motionSampler: Sampler;
    @binding(5) normal: Texture2D<float4>;
    @binding(6) normalSampler: Sampler;
    @binding(7) emission: Texture2D<float4>;
    @binding(8) emissionSampler: Sampler;
    @binding(9) environment: Texture2D<float4>;
    @binding(10) environmentSampler: Sampler;
    @binding(11) lights: Texture2D<float4>;
    @binding(12) lightsSampler: Sampler;
    @binding(13) tiles: Texture2D<float4>;
    @binding(14) tilesSampler: Sampler;
    @binding(15) shadow: Texture2D<float4>;
    @binding(16) shadowSampler: Sampler;
    @binding(17) diffuse: Texture2D<float4>;
    @binding(18) diffuseSampler: Sampler;
    @binding(19) ao: Texture2D<float4>;
    @binding(20) aoSampler: Sampler;
    @binding(21) spotA: Texture2D<float4>;
    @binding(22) spotASampler: Sampler;
    @binding(23) spotB: Texture2D<float4>;
    @binding(24) spotBSampler: Sampler;
    @binding(25) cascadeMid: Texture2D<float4>;
    @binding(26) cascadeMidSampler: Sampler;
    @binding(27) cascadeFar: Texture2D<float4>;
    @binding(28) cascadeFarSampler: Sampler;
    @binding(29) subsurface: Texture2D<float4>;
    @binding(30) subsurfaceSampler: Sampler;
    @binding(31) reflection: Texture2D<float4>;
    @binding(32) coatAlbedo: Texture2D<float4>;
    @binding(33) sheen: Texture2D<float4>;
    @binding(34) anisotropy: Texture2D<u32>;
    @binding(35) anisotropyAlbedo: Texture2D<float4>;
    @binding(36) rayVisibility: StorageBuffer<f32>;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output {
    @target(0) color: float4;
    @target(1) diffuse: float4;
    @target(2) tail: float4;
}
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}

function Position(resources: Resources, uv: float2, depth: f32): float3 {
    return WorldAt(uv, depth, resources.material.inverseX, resources.material.inverseY,
        resources.material.inverseZ, resources.material.inverseW);
}
function Row(row: float4, p: float3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
function Occlusion(resources: Resources, uv: float2, p: float3): f32 {
    let parameters: float4 = resources.material.aoParameters;
    if (parameters.z <= 0.0) {
        return 1.0;
    }
    let pixel: float2 = float2(uv.x / parameters.x - 0.5, uv.y / parameters.y - 0.5);
    let origin: float2 = float2(Floor(pixel.x), Floor(pixel.y));
    let fraction: float2 = float2(pixel.x - origin.x, pixel.y - origin.y);
    var sum: f32 = 0.0;
    var total: f32 = 0.0;
    for (var y: u32 = 0; y < 2; y = y + 1) {
        for (var x: u32 = 0; x < 2; x = x + 1) {
            let fx: f32 = Convert<f32>(x);
            let fy: f32 = Convert<f32>(y);
            let tapUv: float2 = float2((origin.x + fx + 0.5) * parameters.x, (origin.y + fy + 0.5) * parameters.y);
            let tap: float4 = Sample(resources.ao, resources.aoSampler, tapUv);
            if (tap.y < 1.0) {
                let texel: float4 = resources.material.surfaceTexels;
                let snapped: float2 = float2((Floor(tapUv.x / texel.x) + 0.5) * texel.x,
                    (Floor(tapUv.y / texel.y) + 0.5) * texel.y);
                let delta: float3 = Sub3(Position(resources, snapped, tap.y), p);
                let weight: f32 = (1.0 - Abs(fx - fraction.x)) * (1.0 - Abs(fy - fraction.y))
                    / (1.0 + Dot3(delta, delta) / Max(parameters.z * parameters.z * 0.02, 0.000001));
                sum = sum + tap.x * weight;
                total = total + weight;
            }
        }
    }
    if (total < 0.00001) {
        return 1.0;
    }
    return sum / total;
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    let motion: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    if (motion.w >= 1.0) {
        return { color: resources.material.clear, diffuse: float4(0.0, 0.0, 0.0, 0.0), tail: float4(0.0, 0.0, 0.0, 0.0) };
    }
    let material: float4 = Sample(resources.base, resources.baseSampler, input.uv);
    let n: float4 = Sample(resources.normal, resources.normalSampler, input.uv);
    let emission: float4 = Sample(resources.emission, resources.emissionSampler, input.uv);
    let base: float3 = float3(material.x, material.y, material.z);
    let emitted: float3 = float3(emission.x, emission.y, emission.z);
    let p: float3 = Position(resources, input.uv, motion.w);
    if (resources.material.surfaceTexels.z > 0.5) {
        let visibility: f32 = Occlusion(resources, input.uv, p);
        return { color: float4(visibility, visibility, visibility, 1.0), diffuse: float4(0.0, 0.0, 0.0, 0.0), tail: float4(0.0, 0.0, 0.0, 0.0) };
    }
    if (emission.w < 0.0) {
        let unlit: float3 = Add3(base, emitted);
        return { color: LimitRadiance(float4(unlit.x, unlit.y, unlit.z, 1.0)), diffuse: float4(0.0, 0.0, 0.0, 0.0), tail: float4(0.0, 0.0, 0.0, 0.0) };
    }
    let normal: float3 = Unit(float3(n.x, n.y, n.z));
    var view: float3 = Unit(Sub3(float3(resources.material.eye.x, resources.material.eye.y, resources.material.eye.z), p));
    if (resources.material.eye.w < 0.5) {
        view = Unit(Sub3(Position(resources, input.uv, 0.0), Position(resources, input.uv, 1.0)));
    }
    let light: float3 = Unit(float3(resources.material.light.x, resources.material.light.y, resources.material.light.z));
    let sun: float3 = float3(resources.material.sun.x, resources.material.sun.y, resources.material.sun.z);
    let visibility: f32 = DirectionalVisibility(resources, p, normal);
    let profile: float4 = Sample(resources.subsurface, resources.subsurfaceSampler, input.uv);
    let packedAnisotropy: u32 = Load(resources.anisotropy, uint2(Convert<u32>(input.position.x), Convert<u32>(input.position.y)));
    let anisotropy: AnisotropyLayer = DecodeAnisotropy(normal, material.w, packedAnisotropy);
    var coat: CoatLayer = OpenPbrCoat(profile, float2(0.0, 0.0));
    var substrate: CoatSubstrate = CoatBase(base, n.w, 0.04, float2(0.0, 0.0));
    var sheen: SheenLayer = OpenPbrSheen(0.0, float4(0.0, 0.0, 0.0, 0.0), normal, view);
    if (profile.w < -1.0 || anisotropy.active) {
        let nv: f32 = Dot3(normal, view);
        if (coat.weight > 0.0) { coat = OpenPbrCoat(profile, CoatCoefficients(resources, nv, profile.y)); }
        if (profile.w < -1.0 && profile.z > 0.0) {
            sheen = OpenPbrSheen(profile.z, Sample(resources.sheen, resources.subsurfaceSampler, input.uv), normal, view);
        }
        if (anisotropy.active) {
            substrate = CoatBase(base, n.w, 0.04, AnisotropyCoefficients(resources, AnisotropyLookup(anisotropy, normal, view)));
        } else {
            substrate = CoatBase(base, n.w, 0.04, CoatCoefficients(resources, nv, material.w));
        }
    }
    let transmission: f32 = CoatTransmission(coat);
    let stackTransmission: f32 = transmission * (1.0 - sheen.budget);
    var direct: float3 = Scale3(Mul3(LayeredLightResponse(base, normal, view, light, n.w, material.w,
        0.04, resources.material.ground.w, coat, substrate, sheen, anisotropy), sun), resources.material.sun.w * visibility);
    var diffuseDirect: float3 = float3(0.0, 0.0, 0.0);
    var diffuseAmbient: float3 = float3(0.0, 0.0, 0.0);
    if (profile.w > 0.0) {
        diffuseDirect = Scale3(Mul3(DirectDiffuseModel(base, normal, view, light, n.w, material.w, resources.material.ground.w), sun), resources.material.sun.w * visibility);
        let blend: f32 = Clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
        diffuseAmbient = Mul3(base, Add3(Scale3(float3(resources.material.sky.x, resources.material.sky.y, resources.material.sky.z), blend),
            Scale3(float3(resources.material.ground.x, resources.material.ground.y, resources.material.ground.z), 1.0 - blend)));
    }
    let sky: float3 = float3(resources.material.sky.x, resources.material.sky.y, resources.material.sky.z);
    let ground: float3 = float3(resources.material.ground.x, resources.material.ground.y, resources.material.ground.z);
    var reflection: float4 = float4(0.0, 0.0, 0.0, 0.0);
    if (resources.material.reflectionParameters.x > 0.5 && !anisotropy.active) {
        reflection = Sample(resources.reflection, resources.baseSampler, input.uv);
    }
    let reflectionWeight: f32 = Abs(reflection.w);
    var ambient: float3 = HemisphereLight(base, normal, view, sky, ground, n.w, material.w);
    if (reflectionWeight > 0.0) {
        let blend: f32 = Clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
        let diffuseHemisphere: float3 = Scale3(Mul3(base, Add3(Scale3(sky, blend), Scale3(ground, 1.0 - blend))), 1.0 - n.w);
        ambient = Add3(Scale3(ambient, 1.0 - reflectionWeight), Scale3(diffuseHemisphere, reflectionWeight));
    }
    if ((coat.weight > 0.0 || sheen.active || anisotropy.active) && resources.material.parameters.y < 0.5) {
        let mirror: float3 = Sub3(Scale3(normal, 2.0 * Max(Dot3(normal, view), 0.0)), view);
        let spread: float3 = Add3(Scale3(mirror, 1.0 - material.w), Scale3(normal, material.w));
        ambient = CoatedAmbientBase(base, n.w, substrate.albedo, HemisphereGradient(normal, sky, ground),
            HemisphereGradient(spread, sky, ground), 1.0, 1.0 - reflectionWeight);
    }
    if (resources.material.parameters.y > 0.5) {
        let reflected: float3 = BoxReflection(p, Sub3(Scale3(normal, 2.0 * Max(Dot3(normal, view), 0.0)), view),
            resources.material.probePosition, resources.material.probeBounds);
        var diffuseWeight: f32 = 1.0;
        if (resources.material.probePosition.w > 0.5) {
            let estimate: float4 = Sample(resources.diffuse, resources.diffuseSampler, input.uv);
            // No valid connection means no diffuse estimate, rather than a black sky.
            if (estimate.w > 0.000001) { diffuseWeight = 0.0; }
        }
        let nv: f32 = Max(Dot3(normal, view), 0.0001);
        ambient = float3(0.0, 0.0, 0.0);
        if (coat.weight > 0.0 || sheen.active || anisotropy.active) {
            let diffuseRadiance: float3 = Band(resources.environment, resources.environmentSampler, EncodeOcta(normal), 6.0);
            let specularRadiance: float3 = SubstrateEnvironment(resources, reflected, material.w, anisotropy);
            ambient = Scale3(CoatedAmbientBase(base, n.w, substrate.albedo, diffuseRadiance, specularRadiance,
                diffuseWeight, 1.0 - reflectionWeight), resources.material.parameters.z);
        } else if (reflectionWeight < 1.0) {
            ambient = Scale3(EnvironmentSurfaceResponseWeighted(resources.environment, resources.environmentSampler, base, normal, reflected,
                nv, n.w, material.w, 0.04, diffuseWeight), resources.material.parameters.z * (1.0 - reflectionWeight));
        }
        if (coat.weight <= 0.0 && !sheen.active && !anisotropy.active && reflectionWeight > 0.0) {
            ambient = Add3(ambient, Scale3(EnvironmentDiffuseWeighted(resources.environment, resources.environmentSampler, base, normal,
                nv, n.w, material.w, diffuseWeight), resources.material.parameters.z * reflectionWeight));
        }
    }
    if (profile.w > 0.0 && resources.material.parameters.y > 0.5 && resources.material.probePosition.w < 0.5) {
        diffuseAmbient = Scale3(EnvironmentDiffuse(resources.environment, resources.environmentSampler, base, normal,
            Max(Dot3(normal, view), 0.0001), material.w), resources.material.parameters.z);
    }
    if (resources.material.parameters.w > 0.5 && n.w == 0.0 && !anisotropy.active) {
        let response: float4 = CompiledDiffuse(resources.diffuse, resources.diffuseSampler, p, normal,
            resources.material.sun.w, resources.material.sky.w, sun);
        if (response.w > 0.5) {
            direct = Scale3(Mul3(base, sun), Max(Dot3(normal, light), 0.0) * resources.material.sun.w * visibility / 3.14159265);
            ambient = Mul3(base, float3(response.x, response.y, response.z));
            diffuseDirect = direct;
            diffuseAmbient = ambient;
            if (coat.weight > 0.0) {
                let coatSun: f32 = CoatSpecular(coat, normal, view, light) * resources.material.sun.w * visibility;
                direct = Add3(Scale3(direct, transmission), Scale3(sun, coatSun));
            }
            if (sheen.active) {
                let fuzzSun: f32 = sheen.budget * SheenPdf(sheen, normal, light) * resources.material.sun.w * visibility;
                direct = Add3(Scale3(direct, 1.0 - sheen.budget), Scale3(Mul3(sun, sheen.tint), fuzzSun));
            }
        }
    }
    var stochasticTail: float3 = float3(0.0, 0.0, 0.0);
    var tailVisible: f32 = 0.0;
    var tailSlots: f32 = 0.0;
    let rayBase: u32 = (Convert<u32>(input.position.y) * Convert<u32>(resources.material.reflectionParameters.y)
        + Convert<u32>(input.position.x)) * 16;
    if (resources.material.surfaceTexels.w > 1.5) {
        let emitterKey: u32 = Convert<u32>(resources.rayVisibility[rayBase + 14]);
        let quota: f32 = resources.rayVisibility[rayBase + 9];
        var exactCount: u32 = 0;
        if ((Convert<u32>(resources.material.surfaceTexels.w) & 1) != 0) {
            exactCount = Convert<u32>(resources.rayVisibility[rayBase + 15]);
        }
        for (var sample: u32 = 0; sample < 4; sample = sample + 1) {
            let address: u32 = rayBase + sample * 2;
            let index: u32 = Convert<u32>(resources.rayVisibility[address]);
            let weight: f32 = resources.rayVisibility[address + 1];
            if (sample >= exactCount && Convert<f32>(sample) < quota) {
                tailSlots = tailSlots + 1.0;
                if (weight > 0.0) { tailVisible = tailVisible + 1.0; }
            }
            if (weight > 0.0) {
                let row: f32 = (Convert<f32>(index) + 0.5) / 256.0;
                let sphere: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.125, row));
                let color: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.375, row));
                let cone: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.625, row));
                let config: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.875, row));
                let emitter: EmitterSample3D = SampleLocalEmitter(p, sphere, cone, config, emitterKey + sample * 137);
                let direction: float3 = emitter.direction;
                if (emitter.attenuation > 0.0) {
                    var attenuation: f32 = emitter.attenuation * weight;
                    if (profile.w > 0.0) {
                        diffuseDirect = Add3(diffuseDirect, Scale3(Mul3(DirectDiffuseModel(base, normal, view, direction, n.w, material.w, resources.material.ground.w),
                            float3(color.x, color.y, color.z)), color.w * attenuation));
                    }
                    let contribution: float3 = Scale3(Mul3(LayeredLightResponse(base, normal, view, direction, n.w, material.w, 0.04, resources.material.ground.w, coat, substrate, sheen, anisotropy),
                        float3(color.x, color.y, color.z)), color.w * attenuation);
                    if (sample >= exactCount) { stochasticTail = Add3(stochasticTail, contribution); }
                    if (sample < exactCount || resources.material.surfaceTexels.w < 5.5) {
                        direct = Add3(direct, contribution);
                    }
                }
            }
        }
    } else if (resources.material.parameters.x > 0.0) {
        for (var group: u32 = 0; group < 8; group = group + 1) {
            if (Convert<f32>(group * 32) < resources.material.parameters.x) {
                let mask: float4 = Sample(resources.tiles, resources.tilesSampler,
                    float2((input.uv.x + Convert<f32>(group)) / 8.0, input.uv.y));
                for (var lane: u32 = 0; lane < 32; lane = lane + 1) {
                    let index: u32 = group * 32 + lane;
                    if (Convert<f32>(index) < resources.material.parameters.x
                        && (MaskAllows(mask, lane) || resources.material.aoParameters.w > 0.5)) {
                        let row: f32 = (Convert<f32>(index) + 0.5) / 256.0;
                        let sphere: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.125, row));
                        let color: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.375, row));
                        let cone: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.625, row));
                        let config: float4 = Sample(resources.lights, resources.lightsSampler, float2(0.875, row));
                        let delta: float3 = Sub3(float3(sphere.x, sphere.y, sphere.z), p);
                        let squared: f32 = Dot3(delta, delta);
                        if (squared < sphere.w * sphere.w) {
                            let direction: float3 = Unit(delta);
                            var attenuation: f32 = LocalAttenuation(direction, squared, sphere.w, cone, config);
                            if (config.z > 0.5) {
                                if (resources.material.surfaceTexels.w > 0.5) {
                                    attenuation = attenuation * RayVisibility(resources, rayBase, index);
                                } else {
                                    attenuation = attenuation * LocalShadow(resources, config.z, p, Max(Dot3(normal, direction), 0.0));
                                }
                            }
                            if (profile.w > 0.0) {
                                diffuseDirect = Add3(diffuseDirect, Scale3(Mul3(DirectDiffuseModel(base, normal, view, direction, n.w, material.w, resources.material.ground.w),
                                    float3(color.x, color.y, color.z)), color.w * attenuation));
                            }
                            direct = Add3(direct, Scale3(Mul3(LayeredLightResponse(base, normal, view, direction, n.w, material.w, 0.04, resources.material.ground.w, coat, substrate, sheen, anisotropy),
                                float3(color.x, color.y, color.z)), color.w * attenuation));
                        }
                    }
                }
            }
        }
    }
    if (coat.weight > 0.0) {
        ambient = Add3(Scale3(ambient, transmission), CoatEnvironment(resources, p, normal, view, sky, ground, coat));
    }
    if (sheen.active) {
        let axis: float3 = SheenAxis(sheen, normal);
        var radiance: float3 = HemisphereGradient(axis, sky, ground);
        // The constant environment is exact. Directional HDR uses the existing
        // prefiltered atlas as an approximation of the warped-cosine integral.
        if (resources.material.parameters.y > 0.5) {
            let reflected: float3 = BoxReflection(p, axis, resources.material.probePosition, resources.material.probeBounds);
            radiance = Scale3(PrefilteredEnvironment(resources.environment, resources.environmentSampler, EncodeOcta(reflected), sheen.roughness),
                resources.material.parameters.z);
        }
        ambient = Add3(Scale3(ambient, 1.0 - sheen.budget), Scale3(Mul3(sheen.tint, radiance), sheen.budget));
    }
    let indirect: float3 = Scale3(ambient, emission.w * Occlusion(resources, input.uv, p));
    var bounce: float3 = float3(0.0, 0.0, 0.0);
    // The legacy floor field and dynamic field are mutually exclusive. Share
    // their texture binding; probePosition.w selects the current-pixel field.
    if (resources.material.probePosition.w > 0.5) {
        let gi: float4 = Sample(resources.diffuse, resources.diffuseSampler, input.uv);
        bounce = Scale3(Mul3(base, float3(gi.x, gi.y, gi.z)), (1.0 - n.w) * 0.96);
        if (anisotropy.active) { bounce = Mul3(bounce, Sub3(float3(1.0, 1.0, 1.0), substrate.albedo)); }
        if (coat.weight > 0.0 || sheen.active) { bounce = Scale3(bounce, stackTransmission); }
    }
    var lit: float3 = Add3(Add3(Add3(direct, indirect), emitted), bounce);
    if (reflection.w > 0.0) {
        let fresnel: float3 = SpecularFresnel(base, n.w, Max(Dot3(normal, view), 0.0));
        lit = Add3(lit, Scale3(Mul3(float3(reflection.x, reflection.y, reflection.z), fresnel), reflection.w * stackTransmission));
    }
    if (reflection.w < 0.0) {
        // The GGX estimator already includes the sample's Fresnel and visibility.
        lit = Add3(lit, Scale3(float3(reflection.x, reflection.y, reflection.z), reflectionWeight * stackTransmission));
    }
    let diffuse: float3 = Add3(bounce, Add3(diffuseDirect, Scale3(diffuseAmbient, emission.w * Occlusion(resources, input.uv, p))));
    return {
        tail: LimitRadiance(float4(stochasticTail.x, stochasticTail.y, stochasticTail.z, tailVisible / Max(tailSlots, 1.0))),
        color: LimitRadiance(float4(lit.x, lit.y, lit.z, 1.0)),
        diffuse: LimitRadiance(float4(diffuse.x, diffuse.y, diffuse.z, 1.0)),
    };
}

function CoatCoefficients(resources: Resources, nv: f32, roughness: f32): float2 {
    let uv: float2 = CoatLookup(nv, roughness);
    let sample: float4 = Sample(resources.coatAlbedo, resources.environmentSampler,
        float2((uv.x * 63.0 + 0.5) / 64.0, (uv.y * 63.0 + 0.5) / 64.0));
    return float2(sample.x, sample.y);
}

function AnisotropyTile(resources: Resources, coordinates: float4, azimuth: f32, minor: f32): float2 {
    let uv: float2 = float2((azimuth * 41.0 + coordinates.x + 0.5) / 369.0,
        (minor * 25.0 + coordinates.y + 0.5) / 625.0);
    let sample: float4 = Sample(resources.anisotropyAlbedo, resources.environmentSampler, uv);
    return float2(sample.x, sample.y);
}

function BlendCoefficients(first: float2, second: float2, weight: f32): float2 {
    return float2(first.x + (second.x - first.x) * weight, first.y + (second.y - first.y) * weight);
}

function AnisotropyCoefficients(resources: Resources, coordinates: float4): float2 {
    let phi: f32 = Floor(coordinates.z);
    let minor: f32 = Floor(coordinates.w);
    let azimuthBlend: f32 = coordinates.z - phi;
    let minorBlend: f32 = coordinates.w - minor;
    let nextPhi: f32 = Min(phi + 1.0, 8.0);
    let nextMinor: f32 = Min(minor + 1.0, 24.0);
    let first: float2 = BlendCoefficients(AnisotropyTile(resources, coordinates, phi, minor),
        AnisotropyTile(resources, coordinates, nextPhi, minor), azimuthBlend);
    let second: float2 = BlendCoefficients(AnisotropyTile(resources, coordinates, phi, nextMinor),
        AnisotropyTile(resources, coordinates, nextPhi, nextMinor), azimuthBlend);
    return BlendCoefficients(first, second, minorBlend);
}

function SubstrateEnvironment(resources: Resources, reflected: float3, roughness: f32, anisotropy: AnisotropyLayer): float3 {
    if (!anisotropy.active) {
        return PrefilteredEnvironment(resources.environment, resources.environmentSampler, EncodeOcta(reflected), roughness);
    }
    // Approximate the principal-axis footprint with three normalized taps in
    // the existing isotropically filtered atlas.
    let width: f32 = Sqrt(anisotropy.widths.y);
    let major: f32 = Sqrt(Max(0.0, anisotropy.widths.x * anisotropy.widths.x - anisotropy.widths.y * anisotropy.widths.y));
    let tangent: float3 = Sub3(anisotropy.tangent, Scale3(reflected, Dot3(reflected, anisotropy.tangent)));
    let offset: float3 = Scale3(Unit(tangent), major);
    let center: float3 = PrefilteredEnvironment(resources.environment, resources.environmentSampler, EncodeOcta(reflected), width);
    let first: float3 = PrefilteredEnvironment(resources.environment, resources.environmentSampler, EncodeOcta(Unit(Add3(reflected, offset))), width);
    let second: float3 = PrefilteredEnvironment(resources.environment, resources.environmentSampler, EncodeOcta(Unit(Sub3(reflected, offset))), width);
    return Add3(Scale3(center, 0.5), Scale3(Add3(first, second), 0.25));
}

function HemisphereGradient(direction: float3, sky: float3, ground: float3): float3 {
    let blend: f32 = Clamp(direction.y * 0.5 + 0.5, 0.0, 1.0);
    return Add3(Scale3(sky, blend), Scale3(ground, 1.0 - blend));
}

function CoatedAmbientBase(base: float3, metallic: f32, specular: float3, diffuseRadiance: float3,
    specularRadiance: float3, diffuseWeight: f32, specularWeight: f32): float3 {
    let diffuse: float3 = Scale3(Mul3(base, Sub3(float3(1.0, 1.0, 1.0), specular)), (1.0 - metallic) * diffuseWeight);
    return Add3(Mul3(diffuse, diffuseRadiance), Scale3(Mul3(specular, specularRadiance), specularWeight));
}

function CoatEnvironment(resources: Resources, position: float3, normal: float3, view: float3,
    sky: float3, ground: float3, coat: CoatLayer): float3 {
    let mirror: float3 = Sub3(Scale3(normal, 2.0 * Max(Dot3(normal, view), 0.0)), view);
    if (resources.material.parameters.y > 0.5) {
        let direction: float3 = BoxReflection(position, mirror, resources.material.probePosition, resources.material.probeBounds);
        return Scale3(PrefilteredEnvironment(resources.environment, resources.environmentSampler, EncodeOcta(direction), coat.roughness),
            coat.weight * coat.albedo * resources.material.parameters.z);
    }
    let scattered: float3 = Add3(Scale3(mirror, 1.0 - coat.roughness), Scale3(normal, coat.roughness));
    return Scale3(HemisphereGradient(scattered, sky, ground), coat.weight * coat.albedo);
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
function FirstShadow(resources: Resources, p: float3, nl: f32): f32 {
    let w: f32 = Row(resources.material.spotAW, p);
    if (w <= 0.00001) {
        return 1.0;
    }
    let projected: float3 = float3(Row(resources.material.spotAX, p) / w,
        Row(resources.material.spotAY, p) / w, Row(resources.material.spotAZ, p) / w);
    return ShadowVisibility(resources.spotA, resources.spotASampler, projected, float4(1.0, 0.0015, 1.0 / 512.0, 0.0), nl);
}
function SecondShadow(resources: Resources, p: float3, nl: f32): f32 {
    let w: f32 = Row(resources.material.spotBW, p);
    if (w <= 0.00001) {
        return 1.0;
    }
    let projected: float3 = float3(Row(resources.material.spotBX, p) / w,
        Row(resources.material.spotBY, p) / w, Row(resources.material.spotBZ, p) / w);
    return ShadowVisibility(resources.spotB, resources.spotBSampler, projected, float4(1.0, 0.0015, 1.0 / 512.0, 0.0), nl);
}
function LocalShadow(resources: Resources, index: f32, p: float3, nl: f32): f32 {
    return match ShadowSourceFor(index) {
        ShadowSource.First => FirstShadow(resources, p, nl),
        ShadowSource.Second => SecondShadow(resources, p, nl),
    };
}

function NearVisibility(resources: Resources, p: float3, normal: float3): f32 {
    let projected: float3 = float3(Row(resources.material.shadowX, p), Row(resources.material.shadowY, p), Row(resources.material.shadowZ, p));
    return PlaneShadowVisibility(resources.shadow, resources.shadowSampler, projected,
        float4(resources.material.shadowParameters.x, resources.material.cascadeBias.x, resources.material.shadowParameters.z, 0.0), normal, resources.material.shadowX, resources.material.shadowY, resources.material.shadowZ);
}
function MidVisibility(resources: Resources, p: float3, normal: float3): f32 {
    let projected: float3 = float3(Row(resources.material.cascadeMidX, p), Row(resources.material.cascadeMidY, p), Row(resources.material.cascadeMidZ, p));
    return PlaneShadowVisibility(resources.cascadeMid, resources.cascadeMidSampler, projected,
        float4(resources.material.shadowParameters.x, resources.material.cascadeBias.y, resources.material.shadowParameters.z, 0.0), normal, resources.material.cascadeMidX, resources.material.cascadeMidY, resources.material.cascadeMidZ);
}
function FarVisibility(resources: Resources, p: float3, normal: float3): f32 {
    let projected: float3 = float3(Row(resources.material.cascadeFarX, p), Row(resources.material.cascadeFarY, p), Row(resources.material.cascadeFarZ, p));
    return PlaneShadowVisibility(resources.cascadeFar, resources.cascadeFarSampler, projected,
        float4(resources.material.shadowParameters.x, resources.material.cascadeBias.z, resources.material.shadowParameters.z, 0.0), normal, resources.material.cascadeFarX, resources.material.cascadeFarY, resources.material.cascadeFarZ);
}
function DirectionalVisibility(resources: Resources, p: float3, normal: float3): f32 {
    let forward: float4 = resources.material.cascadeForward;
    let eye: float4 = resources.material.eye;
    let depth: f32 = (p.x - eye.x) * forward.x + (p.y - eye.y) * forward.y + (p.z - eye.z) * forward.z;
    let ends: float4 = resources.material.cascadeEnds;
    if (depth < ends.x * 0.9) {
        return NearVisibility(resources, p, normal);
    }
    if (depth < ends.x) {
        let blend: f32 = (depth - ends.x * 0.9) / (ends.x * 0.1);
        return NearVisibility(resources, p, normal) * (1.0 - blend) + MidVisibility(resources, p, normal) * blend;
    }
    if (depth < ends.y * 0.9) {
        return MidVisibility(resources, p, normal);
    }
    if (depth < ends.y) {
        let blend: f32 = (depth - ends.y * 0.9) / (ends.y * 0.1);
        return MidVisibility(resources, p, normal) * (1.0 - blend) + FarVisibility(resources, p, normal) * blend;
    }
    let fade: f32 = Clamp((depth - ends.z * 0.9) / (ends.z * 0.1), 0.0, 1.0);
    return FarVisibility(resources, p, normal) * (1.0 - fade) + fade;
}

function RayVisibility(resources: Resources, rayBase: u32, light: u32): f32 {
    let word: u32 = Convert<u32>(resources.rayVisibility[rayBase + light / 16]);
    if (((word >> (light % 16)) & 1) != 0) {
        return 0.0;
    }
    return 1.0;
}
