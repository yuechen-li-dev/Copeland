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
stream ModelResources {
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
}

stream ModelVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) world: float3;
    @location(1) normal: float3;
    @location(2) color: float4;
    @location(3) uv: float2;
    @location(4) tangent: float4;
}
stream PixelBuiltins { @builtin(front_face) frontFace: bool; }
stream ModelOutput { @target(0) color: float4; }

function Row(row: float4, p: WorldPosition3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
function Dot3(a: float3, b: float3): f32 {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}
function Scale3(a: float3, s: f32): float3 {
    return float3(a.x * s, a.y * s, a.z * s);
}
function Add3(a: float3, b: float3): float3 {
    return float3(a.x + b.x, a.y + b.y, a.z + b.z);
}
function Sub3(a: float3, b: float3): float3 {
    return float3(a.x - b.x, a.y - b.y, a.z - b.z);
}
function Mul3(a: float3, b: float3): float3 {
    return float3(a.x * b.x, a.y * b.y, a.z * b.z);
}
function Unit(a: float3): float3 {
    return Scale3(a, 1.0 / Sqrt(Max(Dot3(a, a), 0.000001)));
}
function Cross3(a: float3, b: float3): float3 {
    return float3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}
function Fresnel(f0: f32, vh: f32): f32 {
    const x: f32 = 1.0 - vh;
    return f0 + (1.0 - f0) * x * x * x * x * x;
}
function Geometry(n: f32, k: f32): f32 {
    return n / (n * (1.0 - k) + k);
}
function Channel(base: f32, metallic: f32, vh: f32, specular: f32): f32 {
    const f0: f32 = 0.04 * (1.0 - metallic) + base * metallic;
    const f: f32 = Fresnel(f0, vh);
    return (1.0 - f) * (1.0 - metallic) * base / 3.14159265 + f * specular;
}

@vertex
function VertexMain(input: VertexInput, uniforms: MaterialResources): ModelVaryings {
    return {
        position: float4(Row(uniforms.material.clipX, input.position), Row(uniforms.material.clipY, input.position),
            Row(uniforms.material.clipZ, input.position), Row(uniforms.material.clipW, input.position)),
        world: float3(input.position.x, input.position.y, input.position.z),
        normal: input.normal,
        color: input.color,
        uv: input.uv,
        tangent: input.tangent,
    };
}

@pixel
function PixelMain(input: ModelVaryings, uniforms: MaterialResources, resources: ModelResources, builtins: PixelBuiltins): ModelOutput {
    if (!builtins.frontFace && uniforms.material.flags.z < 0.5) { Discard(); }
    const base: float4 = Sample(resources.baseMap, resources.baseSampler, input.uv) * uniforms.material.baseColor * input.color;
    if (uniforms.material.flags.y > 0.5 && base.w < uniforms.material.emissiveAlpha.w) { Discard(); }
    if (uniforms.material.flags.x > 0.5) { return { color: float4(base.x, base.y, base.z, 1.0) }; }

    const nt: float4 = Sample(resources.normalMap, resources.normalSampler, input.uv);
    const tangent: float3 = Unit(float3(input.tangent.x, input.tangent.y, input.tangent.z));
    const geometric: float3 = Unit(input.normal);
    const bitangent: float3 = Scale3(Cross3(geometric, tangent), input.tangent.w);
    var normal: float3 = Unit(Add3(Add3(Scale3(tangent, (nt.x * 2.0 - 1.0) * uniforms.material.factors.z),
        Scale3(bitangent, (nt.y * 2.0 - 1.0) * uniforms.material.factors.z)), Scale3(geometric, nt.z * 2.0 - 1.0)));
    if (!builtins.frontFace) { normal = Scale3(normal, -1.0); }
    const light: float3 = Unit(float3(0.36, 0.80, 0.48));
    const view: float3 = Unit(Sub3(float3(uniforms.material.eye.x, uniforms.material.eye.y, uniforms.material.eye.z), input.world));
    const half: float3 = Unit(Add3(light, view));
    const nl: f32 = Max(Dot3(normal, light), 0.0);
    const nv: f32 = Max(Dot3(normal, view), 0.0001);
    const nh: f32 = Max(Dot3(normal, half), 0.0);
    const vh: f32 = Max(Dot3(view, half), 0.0);
    const mr: float4 = Sample(resources.metalMap, resources.metalSampler, input.uv);
    const metallic: f32 = Clamp(mr.z * uniforms.material.factors.x, 0.0, 1.0);
    const roughness: f32 = Clamp(mr.y * uniforms.material.factors.y, 0.045, 1.0);
    const a: f32 = roughness * roughness;
    const a2: f32 = a * a;
    const denominator: f32 = nh * nh * (a2 - 1.0) + 1.0;
    const distribution: f32 = a2 / Max(3.14159265 * denominator * denominator, 0.000001);
    const k: f32 = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    const specular: f32 = distribution * Geometry(nv, k) * Geometry(nl, k) / Max(4.0 * nv * nl, 0.0001);
    const direct: float3 = Scale3(float3(Channel(base.x, metallic, vh, specular), Channel(base.y, metallic, vh, specular),
        Channel(base.z, metallic, vh, specular)), nl * 2.5);
    const ao: float4 = Sample(resources.occlusionMap, resources.occlusionSampler, input.uv);
    const ambient: f32 = 0.12 * (1.0 - uniforms.material.factors.w + ao.x * uniforms.material.factors.w);
    const emission: float4 = Sample(resources.emissiveMap, resources.emissiveSampler, input.uv);
    const emitted: float3 = Mul3(float3(emission.x, emission.y, emission.z),
        float3(uniforms.material.emissiveAlpha.x, uniforms.material.emissiveAlpha.y, uniforms.material.emissiveAlpha.z));
    const lit: float3 = Add3(Add3(direct, Scale3(float3(base.x, base.y, base.z), ambient)), emitted);
    return { color: float4(Clamp(lit.x, 0.0, 1.0), Clamp(lit.y, 0.0, 1.0), Clamp(lit.z, 0.0, 1.0), 1.0) };
}
