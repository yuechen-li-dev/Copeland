import { DecodeOcta } from "./SurfaceLighting";
import { Unit, Cross3, Add3, Scale3, Sub3, Dot3, Geometry } from "./Lighting3D";

@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record PassMaterial {
    parameters: float4;
}
stream Resources {
    @binding(0) material: PassMaterial;
    @binding(1) source: Texture2D<float4>;
    @binding(2) sourceSampler: Sampler;
}
stream Input { @location(0) position: float2; }
stream Varyings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream Output { @target(0) color: float4; }
@vertex
function VertexMain(input: Input): Varyings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5),
    };
}

function Radical(index: u32): f32 {
    var result: f32 = 0.0;
    var value: f32 = Convert<f32>(index);
    var weight: f32 = 0.5;
    for (var bit: u32 = 0; bit < 16; bit = bit + 1) {
        result = result + (value - Floor(value / 2.0) * 2.0) * weight;
        value = Floor(value / 2.0);
        weight = weight * 0.5;
    }
    return result;
}
function Source(resources: Resources, direction: float3): float3 {
    const uv: float2 = float2(Atan2(direction.z, direction.x) / 6.2831853 + 0.5,
        Acos(Clamp(direction.y, -1.0, 1.0)) / 3.14159265);
    const sample: float4 = Sample(resources.source, resources.sourceSampler, uv);
    return float3(sample.x, sample.y, sample.z);
}
enum BakeJob {
    Mirror,
    Specular(roughness: f32),
    Diffuse,
    Brdf(nv: f32, roughness: f32),
}
function JobFor(band: f32, uv: float2): BakeJob {
    if (band > 6.5) {
        return BakeJob.Brdf(Max(uv.x, 0.0001), Max(uv.y, 0.045));
    }
    if (band > 5.5) {
        return BakeJob.Diffuse;
    }
    if (band < 0.5) {
        return BakeJob.Mirror;
    }
    return BakeJob.Specular(band / 5.0);
}
function HalfVector(x: f32, y: f32, roughness: f32): float3 {
    const a: f32 = roughness * roughness;
    const cosine: f32 = Sqrt((1.0 - y) / (1.0 + (a * a - 1.0) * y));
    const sine: f32 = Sqrt(Max(1.0 - cosine * cosine, 0.0));
    return float3(Cos(x * 6.2831853) * sine, Sin(x * 6.2831853) * sine, cosine);
}
function Tangent(normal: float3): float3 {
    var up: float3 = float3(0.0, 1.0, 0.0);
    if (Abs(normal.y) > 0.99) {
        up = float3(1.0, 0.0, 0.0);
    }
    return Unit(Cross3(up, normal));
}
function ConvolveDiffuse(resources: Resources, normal: float3): float3 {
    const tangent: float3 = Tangent(normal);
    const bitangent: float3 = Cross3(normal, tangent);
    var accumulated: float3 = float3(0.0, 0.0, 0.0);
    for (var sample: u32 = 0; sample < 1024; sample = sample + 1) {
        const phi: f32 = (Convert<f32>(sample) + 0.5) / 1024.0 * 6.2831853;
        const y: f32 = Radical(sample);
        const direction: float3 = Add3(Add3(Scale3(tangent, Cos(phi) * Sqrt(y)),
            Scale3(bitangent, Sin(phi) * Sqrt(y))), Scale3(normal, Sqrt(1.0 - y)));
        accumulated = Add3(accumulated, Source(resources, direction));
    }
    return Scale3(accumulated, 1.0 / 1024.0);
}
function ConvolveSpecular(resources: Resources, normal: float3, roughness: f32): float3 {
    const tangent: float3 = Tangent(normal);
    const bitangent: float3 = Cross3(normal, tangent);
    var accumulated: float3 = float3(0.0, 0.0, 0.0);
    var weight: f32 = 0.0;
    for (var sample: u32 = 0; sample < 1024; sample = sample + 1) {
        const local: float3 = HalfVector((Convert<f32>(sample) + 0.5) / 1024.0, Radical(sample), roughness);
        const half: float3 = Add3(Add3(Scale3(tangent, local.x), Scale3(bitangent, local.y)), Scale3(normal, local.z));
        const direction: float3 = Sub3(Scale3(half, 2.0 * Dot3(normal, half)), normal);
        const nl: f32 = Max(Dot3(normal, direction), 0.0);
        accumulated = Add3(accumulated, Scale3(Source(resources, direction), nl));
        weight = weight + nl;
    }
    return Scale3(accumulated, 1.0 / Max(weight, 0.0001));
}
function IntegrateBrdf(nv: f32, roughness: f32): float3 {
    const view: float3 = float3(Sqrt(1.0 - nv * nv), 0.0, nv);
    var accumulated: float3 = float3(0.0, 0.0, 0.0);
    for (var sample: u32 = 0; sample < 1024; sample = sample + 1) {
        const half: float3 = HalfVector((Convert<f32>(sample) + 0.5) / 1024.0, Radical(sample), roughness);
        const vh: f32 = Max(Dot3(view, half), 0.0);
        const light: float3 = Sub3(Scale3(half, 2.0 * vh), view);
        const nl: f32 = Max(light.z, 0.0);
        if (nl > 0.0) {
            const k: f32 = roughness * roughness / 2.0;
            const visibility: f32 = Geometry(nv, k) * Geometry(nl, k) * vh / Max(half.z * nv, 0.00001);
            const fresnel: f32 = Pow(1.0 - vh, 5.0);
            accumulated = Add3(accumulated, float3((1.0 - fresnel) * visibility, fresnel * visibility, 0.0));
        }
    }
    return Scale3(accumulated, 1.0 / 1024.0);
}
@pixel
function PixelMain(input: Varyings, resources: Resources): Output {
    const band: f32 = Floor(input.uv.y * 8.0);
    // The atlas decoder addresses the endpoints at texel centres. Compile the
    // same endpoint grid so encode/decode does not subtly shrink the directions.
    const uv: float2 = float2(Clamp((input.uv.x * 64.0 - 0.5) / 63.0, 0.0, 1.0),
        Clamp(((input.uv.y * 8.0 - band) * 64.0 - 0.5) / 63.0, 0.0, 1.0));
    const normal: float3 = DecodeOcta(uv);
    const result: float3 = match JobFor(band, uv) {
        BakeJob.Mirror => Source(resources, normal),
        BakeJob.Specular(payload) => ConvolveSpecular(resources, normal, payload.roughness),
        BakeJob.Diffuse => ConvolveDiffuse(resources, normal),
        BakeJob.Brdf(payload) => IntegrateBrdf(payload.nv, payload.roughness),
    };
    return { color: float4(result.x, result.y, result.z, 1.0) };
}
