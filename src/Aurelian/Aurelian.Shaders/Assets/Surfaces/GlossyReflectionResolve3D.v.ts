import { GlossyIsSharp } from "./GlossyReflection3D";
import { Add3, Dot3, Scale3, Sub3, Unit } from "./Lighting3D";
import { WorldAt, EncodeOcta, DecodeOcta } from "./SurfaceLighting";

@space(clip.position)
type ClipPosition4 = float4;
@material @binding(0)
record PassMaterial {
    inverseX: float4;
    inverseY: float4;
    inverseZ: float4;
    inverseW: float4;
    previousX: float4;
    previousY: float4;
    previousZ: float4;
    previousW: float4;
    eye: float4;
    previousEye: float4;
    control: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) raw: Texture2D<float4>;
    @binding(2) rawSampler: Sampler;
    @binding(3) normal: Texture2D<float4>;
    @binding(4) normalSampler: Sampler;
    @binding(5) motion: Texture2D<float4>;
    @binding(6) motionSampler: Sampler;
    @binding(7) surface: Texture2D<float4>;
    @binding(8) surfaceSampler: Sampler;
    @binding(9) history: Texture2D<float4>;
    @binding(10) historySampler: Sampler;
    @binding(11) previousGuide: Texture2D<float4>;
    @binding(12) previousGuideSampler: Sampler;
    @binding(13) hits: Texture2D<uint2>;
    @binding(14) unusedHitSampler: Sampler;
    @binding(15) identity: Texture2D<uint2>;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output {
    @target(0) radiance: float4;
    @target(1) guide: float4;
}
@vertex
function VertexMain(input: Input): Varyings {
    return { position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5) };
}

function PackGuide(normal: float3, roughness: f32): f32 {
    let oct: float2 = EncodeOcta(normal);
    return Floor(oct.x * 255.0 + 0.5) + Floor(oct.y * 255.0 + 0.5) * 256.0
        + Floor(roughness * 255.0 + 0.5) * 65536.0;
}
function UnpackNormal(word: f32): float3 {
    let row: f32 = Floor(word / 256.0);
    return DecodeOcta(float2((word - row * 256.0) / 255.0,
        (row - Floor(row / 256.0) * 256.0) / 255.0));
}
function Position(material: PassMaterial, uv: float2, depth: f32): float3 {
    return WorldAt(uv, depth, material.inverseX, material.inverseY, material.inverseZ, material.inverseW);
}
function PreviousPosition(material: PassMaterial, uv: float2, depth: f32): float3 {
    return WorldAt(uv, depth, material.previousX, material.previousY, material.previousZ, material.previousW);
}
function InBounds(uv: float2): bool {
    return uv.x >= 0.0 && uv.y >= 0.0 && uv.x < 1.0 && uv.y < 1.0;
}

// Hit information reduces spatial bleed across large parallax changes. Different
// stochastic hits can be valid samples of the same glossy lobe, so this is soft.
function HitWeight(center: uint2, neighbor: uint2): f32 {
    if (center.y == 1 || neighbor.y == 1) { return 1.0; }
    if (center.x == neighbor.x) { return 1.0; }
    if (center.x == 0 || neighbor.x == 0) { return 0.5; }
    let a: f32 = Convert<f32>(center.y);
    let b: f32 = Convert<f32>(neighbor.y);
    return 0.7 / (1.0 + Abs(a - b) / Max(Min(a, b), 1.0));
}

@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    let raw: float4 = Sample(resources.raw, resources.rawSampler, input.uv);
    let empty: float4 = float4(0.0, 0.0, 0.0, 0.0);
    if (raw.w == 0.0) { return { radiance: empty, guide: empty }; }
    let appearance: float4 = Sample(resources.surface, resources.surfaceSampler, input.uv);
    if (GlossyIsSharp(appearance.w)) { return { radiance: raw, guide: empty }; }
    let material: PassMaterial = resources.material;
    let pixel: float2 = float2(Floor(input.position.x), Floor(input.position.y));
    let coordinate: uint2 = uint2(Convert<u32>(pixel.x), Convert<u32>(pixel.y));
    let identity: uint2 = Load(resources.identity, coordinate);
    let motion: float4 = Sample(resources.motion, resources.motionSampler, input.uv);
    let n: float4 = Sample(resources.normal, resources.normalSampler, input.uv);
    let normal: float3 = Unit(float3(n.x, n.y, n.z));
    let position: float3 = Position(material, input.uv, motion.w);
    let previousUv: float2 = float2(motion.x, motion.y);
    let hasSample: bool = raw.w < 0.0;
    var color: float3 = float3(raw.x, raw.y, raw.z);
    var count: f32 = 1.0;
    var accepted: bool = false;
    if (material.control.z > 0.5 && InBounds(previousUv) && motion.z >= 0.0 && motion.z < 1.0) {
        let guide: float4 = Sample(resources.previousGuide, resources.previousGuideSampler, previousUv);
        let previousNormal: float3 = UnpackNormal(guide.z);
        let oldRoughness: f32 = Floor(guide.z / 65536.0) / 255.0;
        if (guide.w >= 1.0 && guide.x == Convert<f32>(identity.x) && Abs(appearance.w - oldRoughness) < 0.035
            && Dot3(normal, previousNormal) > 0.97) {
            let previousPoint: float3 = PreviousPosition(material, previousUv, motion.z);
            let oldPoint: float3 = PreviousPosition(material, previousUv, guide.y);
            var view: float3 = Unit(Sub3(float3(material.eye.x, material.eye.y, material.eye.z), position));
            var oldView: float3 = Unit(Sub3(float3(material.previousEye.x, material.previousEye.y, material.previousEye.z), previousPoint));
            if (material.eye.w < 0.5) {
                view = Unit(Sub3(Position(material, input.uv, 0.0), Position(material, input.uv, 1.0)));
                oldView = Unit(Sub3(PreviousPosition(material, previousUv, 0.0), PreviousPosition(material, previousUv, 1.0)));
            }
            let angularTolerance: f32 = Max(0.012, appearance.w * appearance.w * 0.55);
            if (Abs(Dot3(Sub3(previousPoint, oldPoint), previousNormal)) < 0.035
                && Dot3(view, oldView) > 1.0 - 0.5 * angularTolerance * angularTolerance) {
                let old: float4 = Sample(resources.history, resources.historySampler, previousUv);
                accepted = old.w < 0.0;
                if (accepted) {
                    let prior: float3 = float3(old.x, old.y, old.z);
                    count = Min(guide.w, 24.0);
                    color = prior;
                    if (hasSample) {
                        count = Min(count + 1.0, 24.0);
                        color = Add3(Scale3(prior, 1.0 - 1.0 / count), Scale3(float3(raw.x, raw.y, raw.z), 1.0 / count));
                    }
                }
            }
        }
    }
    if (!accepted) {
        var anchor: float2 = pixel;
        var stride: f32 = 1.0;
        if (material.control.w >= 0.0) {
            let row: f32 = Floor(material.control.w / 2.0);
            let laneX: f32 = material.control.w - row * 2.0;
            anchor = float2(Floor(pixel.x / 2.0) * 2.0 + laneX, Floor(pixel.y / 2.0) * 2.0 + row);
            stride = 2.0;
        }
        var sum: float3 = float3(0.0, 0.0, 0.0);
        var total: f32 = 0.0;
        var hit: uint2 = uint2(0, 0);
        if (hasSample) { hit = Load(resources.hits, coordinate); }
        static for (let y of [-1.0, 0.0, 1.0]) {
            static for (let x of [-1.0, 0.0, 1.0]) {
                let tapPixel: float2 = float2(anchor.x + x * stride, anchor.y + y * stride);
                let uv: float2 = float2((tapPixel.x + 0.5) * material.control.x, (tapPixel.y + 0.5) * material.control.y);
                if (InBounds(uv)) {
                    let tap: float4 = Sample(resources.raw, resources.rawSampler, uv);
                    if (tap.w < 0.0) {
                        let address: uint2 = uint2(Convert<u32>(tapPixel.x), Convert<u32>(tapPixel.y));
                        let id: uint2 = Load(resources.identity, address);
                        if (id.x == identity.x) {
                            let surface: float4 = Sample(resources.surface, resources.surfaceSampler, uv);
                            let nTap: float4 = Sample(resources.normal, resources.normalSampler, uv);
                            let nDot: f32 = Dot3(normal, Unit(float3(nTap.x, nTap.y, nTap.z)));
                            let materialDelta: float3 = float3(surface.x - appearance.x, surface.y - appearance.y, surface.z - appearance.z);
                            if (nDot > 0.97 && Abs(surface.w - appearance.w) < 0.04 && Abs(nTap.w - n.w) < 0.05
                                && Dot3(materialDelta, materialDelta) < 0.02) {
                                let tapDepth: f32 = Sample(resources.motion, resources.motionSampler, uv).w;
                                let delta: float3 = Sub3(Position(material, uv, tapDepth), position);
                                if (tapDepth < 1.0 && Abs(Dot3(delta, normal)) < 0.035) {
                                    let dx: f32 = tapPixel.x - pixel.x;
                                    let dy: f32 = tapPixel.y - pixel.y;
                                    var weight: f32 = 1.0 / (1.0 + (dx * dx + dy * dy) / (stride * stride));
                                    if (hasSample) { weight = weight * HitWeight(hit, Load(resources.hits, address)); }
                                    sum = Add3(sum, Scale3(float3(tap.x, tap.y, tap.z), weight));
                                    total = total + weight;
                                }
                            }
                        }
                    }
                }
            }
        }
        if (total <= 0.0 && !hasSample) { return { radiance: empty, guide: empty }; }
        if (total > 0.0) { color = Scale3(sum, 1.0 / total); }
        // An upsampled neighbor colors a newly visible pixel but carries no
        // independent samples into that receiver's history.
        if (!hasSample) { count = 0.0; }
    }
    return { radiance: float4(Min(color.x, 60000.0), Min(color.y, 60000.0), Min(color.z, 60000.0), -Abs(raw.w)),
        guide: float4(Convert<f32>(identity.x), motion.w, PackGuide(normal, appearance.w), count) };
}
