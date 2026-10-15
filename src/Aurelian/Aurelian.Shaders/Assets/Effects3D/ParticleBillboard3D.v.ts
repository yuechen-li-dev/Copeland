import { Fogged } from "./HeightFog";
import { VolumeAt, Phase } from "./VolumeLighting";
import { WorldAt } from "./SurfaceLighting";
import { Sub3, Add3, Scale3, Mul3, Dot3, Unit } from "./Lighting3D";

// Particle shading for the weighted transparency pass.
//
// Fire, sparks and embers are emitters. Their colour is blackbody radiation at a per-pixel temperature (as in Nguyen,
// Fedkiw and Jensen 2002): hue from Planck's law sampled at three wavelengths, brightness from the Stefan-Boltzmann
// T^4 law. Emission goes to its own additive channel, so flames add light instead of being averaged with smoke.
//
// Smoke uses spherical billboards (Umenhoffer, Szirmay-Kalos and Szijarto 2006): each pixel integrates a smooth density
// along the view ray's chord through the particle's sphere, clipped against the opaque depth. That replaces flat cards
// and soft-particle fades with real thickness. Light uses Beer-Lambert transmittance toward the sun through the
// sphere, a Henyey-Greenstein phase and the "powder" term from Schneider's Horizon Zero Dawn clouds (2015).

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
    sunDirection: float4;
    sunColor: float4;
    sky: float4;
    ground: float4;
}
stream Input {
    @location(0) position: float3;
    @location(1) uvKind: float4;
    @location(2) color: float4;
    @location(3) centre: float4;
    @location(4) detail: float4;
    @location(5) glow: float4;
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
    @location(3) centre: float4;
    @location(4) detail: float4;
    @location(5) glow: float4;
}
stream Output {
    @target(0) color: float4;
    @target(1) optical: float4;
}
function Row(row: float4, p: float3): f32 {
    return Dot3(row.xyz, p) + row.w;
}
function Fract(value: f32): f32 {
    return value - Floor(value);
}
function Smooth(value: f32): f32 {
    return value * value * (3.0 - 2.0 * value);
}
function Hash(x: f32, y: f32, z: f32): f32 {
    return Fract(Sin(x * 127.1 + y * 311.7 + z * 74.7) * 43758.5453);
}
function Lerp(a: f32, b: f32, t: f32): f32 {
    return a + (b - a) * t;
}
// Trilinearly interpolated lattice noise in [0, 1].
function Noise(p: float3): f32 {
    const ix: f32 = Floor(p.x);
    const iy: f32 = Floor(p.y);
    const iz: f32 = Floor(p.z);
    const fx: f32 = Smooth(p.x - ix);
    const fy: f32 = Smooth(p.y - iy);
    const fz: f32 = Smooth(p.z - iz);
    const a: f32 = Lerp(Hash(ix, iy, iz), Hash(ix + 1.0, iy, iz), fx);
    const b: f32 = Lerp(Hash(ix, iy + 1.0, iz), Hash(ix + 1.0, iy + 1.0, iz), fx);
    const c: f32 = Lerp(Hash(ix, iy, iz + 1.0), Hash(ix + 1.0, iy, iz + 1.0), fx);
    const d: f32 = Lerp(Hash(ix, iy + 1.0, iz + 1.0), Hash(ix + 1.0, iy + 1.0, iz + 1.0), fx);
    return Lerp(Lerp(a, b, fy), Lerp(c, d, fy), fz);
}
// Three octaves of fractal noise, normalised to [0, 1].
function Fbm(p: float3): f32 {
    const first: f32 = Noise(p);
    const second: f32 = Noise(float3(p.x * 2.03 + 17.1, p.y * 2.03 - 3.7, p.z * 2.03 + 9.2));
    const third: f32 = Noise(float3(p.x * 4.11 - 5.3, p.y * 4.11 + 11.9, p.z * 4.11 - 13.4));
    return (first + second * 0.5 + third * 0.25) / 1.75;
}
// Blackbody chromaticity with unit luminance. Planck's law ratios at 610, 550 and 465 nm, in the Wien regime:
// red/green = (0.55/0.61)^5 exp(c2/T (1/0.55 - 1/0.61)), with c2 = 14388 um K; blue likewise.
function Blackbody(kelvin: f32): float3 {
    const t: f32 = Max(kelvin, 500.0);
    const red: f32 = Min(0.5963 * Exp(2573.2 / t), 60.0);
    const blue: f32 = 2.3138 * Exp(-4781.6 / t);
    const luminance: f32 = 0.2126 * red + 0.7152 + 0.0722 * blue;
    return float3(red / luminance, 1.0 / luminance, blue / luminance);
}
// Radiance of a blackbody relative to one at 1800 K: chromaticity times the Stefan-Boltzmann ratio (T/1800)^4.
function Glowing(kelvin: f32): float3 {
    const ratio: f32 = Max(kelvin, 0.0) / 1800.0;
    return Scale3(Blackbody(kelvin), ratio * ratio * ratio * ratio);
}
function Fire(uv: float2, detail: float4): float2 {
    // Teardrop: rounded where the gas leaves the fuel, tapering to a tip. y runs from 0 at the base to 1 at the tip.
    const y: f32 = Clamp((uv.y + 1.0) * 0.5, 0.0, 1.0);
    const width: f32 = 1.05 * Pow(Max(1.0 - y, 0.0), 0.6) * Min(1.0, 0.3 + y * 4.0);
    // Noise scrolls downward through the flame, so its features rise; the warp grows toward the tip.
    const seed: f32 = detail.x;
    const age: f32 = detail.y;
    const warp: f32 = (Noise(float3(uv.y * 1.7 - age * 3.1, seed, age * 0.7)) - 0.5) * (0.25 + 0.75 * y);
    const x: f32 = uv.x + warp * 0.9;
    const body: f32 = Clamp(1.0 - Abs(x) / Max(width, 0.001), 0.0, 1.0);
    const turbulence: f32 = Fbm(float3(x * 1.6 + seed, uv.y * 1.3 - age * 2.6, seed * 0.37 + age * 0.4));
    const flame: f32 = Clamp(body * 1.55 - turbulence * (0.35 + 0.65 * y), 0.0, 1.0);
    return float2(flame, y);
}
function SphereNoise(q: float3, detail: float4): f32 {
    const seed: f32 = detail.x;
    const age: f32 = detail.y;
    // Slow billowing: the domain drifts and turns over with age.
    return Fbm(float3(q.x * 1.4 + seed, q.y * 1.4 - age * 0.18, q.z * 1.4 + seed * 0.61 + age * 0.11));
}
@vertex
function VertexMain(input: Input, uniforms: Uniforms): Varyings {
    let m: ParticleMaterial = uniforms.material;
    return {
        position: float4(Row(m.clipX, input.position), Row(m.clipY, input.position), Row(m.clipZ, input.position), Row(m.clipW, input.position)),
        world: input.position,
        uvKind: input.uvKind,
        color: input.color,
        centre: input.centre,
        detail: input.detail,
        glow: input.glow,
    };
}
@pixel
function PixelMain(input: Varyings, uniforms: Uniforms, resources: Resources): Output {
    let m: ParticleMaterial = uniforms.material;
    if (input.color.w <= 0.00001 || input.centre.w <= 0.0) { Discard(); }
    let uv: float2 = float2(input.position.x * m.texels.x, input.position.y * m.texels.y);
    let depth: f32 = Sample(resources.motion, resources.motionSampler, uv).w;
    if (input.position.z >= depth) { Discard(); }
    let opaque: float3 = WorldAt(uv, depth, m.inverseX, m.inverseY, m.inverseZ, m.inverseW);
    let delta: float3 = Sub3(input.world, m.eye.xyz);
    let opaqueDelta: float3 = Sub3(opaque, m.eye.xyz);
    let distance: f32 = Sqrt(Dot3(delta, delta));
    let opaqueDistance: f32 = Sqrt(Dot3(opaqueDelta, opaqueDelta));
    let soft: f32 = Clamp((opaqueDistance - distance) / input.uvKind.w, 0.0, 1.0);
    let kind: f32 = input.uvKind.z;
    let tint: float3 = float3(input.color.x, input.color.y, input.color.z);

    var alpha: f32 = 0.0;
    var scattered: float3 = float3(0.0, 0.0, 0.0);
    var emitted: float3 = float3(0.0, 0.0, 0.0);
    if (kind < 0.5) {
        let shape: float2 = Fire(float2(input.uvKind.x, input.uvKind.y), input.detail);
        let flame: f32 = shape.x;
        if (flame <= 0.0) { Discard(); }
        // The hottest gas is in the flame's core; the edges and the tip are cooler and redder.
        let kelvin: f32 = input.detail.z * (0.55 + 0.45 * flame) * (1.0 - 0.22 * shape.y);
        let strength: f32 = input.color.w * soft * Pow(flame, 1.4);
        emitted = Scale3(Mul3(Glowing(kelvin), tint), strength);
        // A little soot toward the tips of older flames.
        alpha = Clamp(strength * 0.1 * input.detail.w * shape.y, 0.0, 0.5);
    } else if (kind < 1.5) {
        // Spherical billboard: integrate density (1 - r^2/R^2) along the view ray's chord through the sphere.
        let radius: f32 = input.centre.w;
        let centre: float3 = float3(input.centre.x, input.centre.y, input.centre.z);
        let view: float3 = Unit(delta);
        let toCentre: float3 = Sub3(centre, m.eye.xyz);
        let along: f32 = Dot3(toCentre, view);
        let off: f32 = Max(Dot3(toCentre, toCentre) - along * along, 0.0);
        let r2: f32 = radius * radius;
        if (off >= r2) { Discard(); }
        let half: f32 = Sqrt(r2 - off);
        let near: f32 = Max(along - half, 0.0);
        let far: f32 = Min(along + half, opaqueDistance);
        if (far <= near) { Discard(); }
        let s0: f32 = near - along;
        let s1: f32 = far - along;
        let chord: f32 = (1.0 - off / r2) * (s1 - s0) - (s1 * s1 * s1 - s0 * s0 * s0) / (3.0 * r2);
        let middle: float3 = Add3(m.eye.xyz, Scale3(view, (near + far) * 0.5));
        let local: float3 = Scale3(Sub3(middle, centre), 1.0 / radius);
        let billow: f32 = SphereNoise(local, input.detail);
        let erosion: f32 = Clamp((billow - 0.3) * 2.4 + 0.45 * (1.0 - Dot3(local, local)), 0.0, 1.6);
        // Extinction per metre scales with 1/R, so a puff's central optical depth follows the command's opacity.
        let sigma: f32 = input.color.w * 2.6 / radius;
        let depthView: f32 = sigma * Max(chord, 0.0) * erosion;
        alpha = 1.0 - Exp(-depthView);
        if (alpha <= 0.002) { Discard(); }

        // Sun: Beer-Lambert toward the light from the chord's midpoint, through the rest of the sphere.
        let sun: float3 = Unit(m.sunDirection.xyz);
        let offset: float3 = Sub3(middle, centre);
        let b: f32 = Dot3(offset, sun);
        let exit: f32 = -b + Sqrt(Max(b * b - (Dot3(offset, offset) - r2), 0.0));
        let toward: f32 = Exp(-sigma * exit * 0.65 * erosion);
        let phase: f32 = Phase(Dot3(view, sun), 0.35) * 12.566370614;
        let powder: f32 = 1.0 - Exp(-depthView * 2.0);
        let sunlight: float3 = Scale3(m.sunColor.xyz, m.sunDirection.w * toward * phase * (0.35 + 0.65 * powder));
        // Ambient from the sky above and the ground below, darkened by the puff's own thickness.
        let outward: float3 = Unit(Add3(offset, Scale3(view, -0.35 * radius)));
        let up: f32 = outward.y;
        let ambient: float3 = Scale3(Add3(Scale3(m.sky.xyz, 0.5 + 0.5 * up), Scale3(m.ground.xyz, 0.5 - 0.5 * up)),
            0.45 + 0.55 * Exp(-sigma * radius * 0.5));
        // Light from the fire beneath: brightest on the underside.
        let underglow: float3 = Scale3(input.glow.xyz, 0.55 - 0.45 * up);
        scattered = Mul3(tint, Add3(Add3(sunlight, ambient), underglow));
    } else {
        // Sparks and embers: a streak along the motion with a bright head and a fading tail.
        let across: f32 = Max(1.0 - input.uvKind.x * input.uvKind.x, 0.0);
        let along: f32 = (input.uvKind.y + 1.0) * 0.5;
        let caps: f32 = 1.0 - Pow(Abs(input.uvKind.y), 6.0);
        let shape: f32 = across * across * (0.3 + 0.7 * along) * caps;
        if (shape <= 0.0) { Discard(); }
        emitted = Scale3(Mul3(Glowing(input.detail.z), tint), input.color.w * soft * shape);
    }

    // Distance attenuation: fog or the integrated volume dims emission and blends scattered light toward the medium.
    var transmission: f32 = 1.0;
    var lit: float3 = scattered;
    if (m.volumeGrid.w > 0.5) {
        let volume: float4 = VolumeAt(resources.volume, resources.volumeSampler, uv, distance, m.volumeGrid, m.volumeDepth);
        transmission = volume.w;
        lit = Add3(Scale3(scattered, volume.w), float3(volume.x, volume.y, volume.z));
    } else {
        let fogged: float3 = Fogged(float3(1.0, 1.0, 1.0), input.world, m.eye.xyz, m.fogParameters, float4(0.0, 0.0, 0.0, m.fogColor.w));
        transmission = fogged.x;
        lit = Fogged(scattered, input.world, m.eye.xyz, m.fogParameters, m.fogColor);
    }
    let light: float3 = Scale3(emitted, transmission);
    let weight: f32 = Max(alpha, 0.001) / (1.0 + distance * distance * 0.02);
    return {
        color: float4(lit.x * alpha * weight, lit.y * alpha * weight, lit.z * alpha * weight, alpha * weight),
        optical: float4(-Log(Max(1.0 - alpha, 0.000001)), light.x, light.y, light.z),
    };
}
