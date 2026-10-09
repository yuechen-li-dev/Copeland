// Generated from canonical VD-MIR graphics.m4. Do not edit.

// semantic space clip.position physically lowers to float4
// semantic space world.position physically lowers to float3

struct StaticModelMaterial
{
    float4 clipX; // offset 0, size 16, align 16
    float4 clipY; // offset 16, size 16, align 16
    float4 clipZ; // offset 32, size 16, align 16
    float4 clipW; // offset 48, size 16, align 16
    float4 eye; // offset 64, size 16, align 16
    float4 baseColor; // offset 80, size 16, align 16
    float4 factors; // offset 96, size 16, align 16
    float4 emissiveAlpha; // offset 112, size 16, align 16
    float4 flags; // offset 128, size 16, align 16
    float4 light; // offset 144, size 16, align 16
    float4 sun; // offset 160, size 16, align 16
    float4 sky; // offset 176, size 16, align 16
    float4 ground; // offset 192, size 16, align 16
    float4 shadowX; // offset 208, size 16, align 16
    float4 shadowY; // offset 224, size 16, align 16
    float4 shadowZ; // offset 240, size 16, align 16
    float4 shadowW; // offset 256, size 16, align 16
    float4 shadowParameters; // offset 272, size 16, align 16
};

struct ModelOutput
{
    float4 color : SV_Target0;
};

struct ModelVaryings
{
    float4 position : SV_Position;
    float3 world : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float4 color : TEXCOORD2;
    float2 uv : TEXCOORD3;
    float4 tangent : TEXCOORD4;
};

struct PixelBuiltins
{
    bool frontFace : SV_IsFrontFace;
};

struct VertexInput
{
    float3 position : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float4 color : TEXCOORD2;
    float2 uv : TEXCOORD3;
    float4 tangent : TEXCOORD4;
};

[[vk::binding(0, 0)]] ConstantBuffer<StaticModelMaterial> material;
[[vk::binding(1, 0)]] Texture2D<float4> baseMap;
[[vk::binding(2, 0)]] SamplerState baseSampler;
[[vk::binding(3, 0)]] Texture2D<float4> metalMap;
[[vk::binding(4, 0)]] SamplerState metalSampler;
[[vk::binding(5, 0)]] Texture2D<float4> normalMap;
[[vk::binding(6, 0)]] SamplerState normalSampler;
[[vk::binding(7, 0)]] Texture2D<float4> occlusionMap;
[[vk::binding(8, 0)]] SamplerState occlusionSampler;
[[vk::binding(9, 0)]] Texture2D<float4> emissiveMap;
[[vk::binding(10, 0)]] SamplerState emissiveSampler;
[[vk::binding(11, 0)]] Texture2D<float4> shadowMap;
[[vk::binding(12, 0)]] SamplerState shadowSampler;

float Row(float4 row, float3 p);
float3 Vts_66B62DB77D76A731_Add3(float3 a, float3 b);
float Vts_66B62DB77D76A731_Channel(float base, float metallic, float vh, float specular);
float3 Vts_66B62DB77D76A731_Cross3(float3 a, float3 b);
float3 Vts_66B62DB77D76A731_DirectLight(float3 base, float3 normal, float3 view, float3 light, float metallic, float roughness);
float Vts_66B62DB77D76A731_Dot3(float3 a, float3 b);
float Vts_66B62DB77D76A731_Fresnel(float f0, float vh);
float Vts_66B62DB77D76A731_Geometry(float n, float k);
float3 Vts_66B62DB77D76A731_HemisphereLight(float3 base, float3 normal, float3 view, float3 sky, float3 ground, float metallic, float roughness);
float3 Vts_66B62DB77D76A731_Mul3(float3 a, float3 b);
float3 Vts_66B62DB77D76A731_Scale3(float3 a, float s);
float Vts_66B62DB77D76A731_ShadowTap(Texture2D<float4> map, SamplerState sampler, float2 uv, float depth);
float Vts_66B62DB77D76A731_ShadowVisibility(Texture2D<float4> map, SamplerState sampler, float3 projected, float4 parameters, float nl);
float3 Vts_66B62DB77D76A731_Sub3(float3 a, float3 b);
float3 Vts_66B62DB77D76A731_Unit(float3 a);
float Vts_F85A4B8D4C402A9E_ExperimentalVisibility(Texture2D<float4> field, SamplerState sampler, float2 uv, float depthVisibility);

float Row(float4 row, float3 p)
{
    return ((((row.x * p.x) + (row.y * p.y)) + (row.z * p.z)) + row.w);
}

float3 Vts_66B62DB77D76A731_Add3(float3 a, float3 b)
{
    return float3((a.x + b.x), (a.y + b.y), (a.z + b.z));
}

float Vts_66B62DB77D76A731_Channel(float base, float metallic, float vh, float specular)
{
    float f0 = ((0.04 * (1.0 - metallic)) + (base * metallic));
    float f = Vts_66B62DB77D76A731_Fresnel(f0, vh);
    return (((((1.0 - f) * (1.0 - metallic)) * base) / 3.1415927) + (f * specular));
}

float3 Vts_66B62DB77D76A731_Cross3(float3 a, float3 b)
{
    return float3(((a.y * b.z) - (a.z * b.y)), ((a.z * b.x) - (a.x * b.z)), ((a.x * b.y) - (a.y * b.x)));
}

float3 Vts_66B62DB77D76A731_DirectLight(float3 base, float3 normal, float3 view, float3 light, float metallic, float roughness)
{
    float3 half = Vts_66B62DB77D76A731_Unit(Vts_66B62DB77D76A731_Add3(light, view));
    float nl = max(Vts_66B62DB77D76A731_Dot3(normal, light), 0.0);
    float nv = max(Vts_66B62DB77D76A731_Dot3(normal, view), 0.0001);
    float nh = max(Vts_66B62DB77D76A731_Dot3(normal, half), 0.0);
    float vh = max(Vts_66B62DB77D76A731_Dot3(view, half), 0.0);
    float a = (roughness * roughness);
    float a2 = (a * a);
    float denominator = (((nh * nh) * (a2 - 1.0)) + 1.0);
    float distribution = (a2 / max(((3.1415927 * denominator) * denominator), 1E-06));
    float k = (((roughness + 1.0) * (roughness + 1.0)) / 8.0);
    float specular = (((distribution * Vts_66B62DB77D76A731_Geometry(nv, k)) * Vts_66B62DB77D76A731_Geometry(nl, k)) / max(((4.0 * nv) * nl), 0.0001));
    return Vts_66B62DB77D76A731_Scale3(float3(Vts_66B62DB77D76A731_Channel(base.x, metallic, vh, specular), Vts_66B62DB77D76A731_Channel(base.y, metallic, vh, specular), Vts_66B62DB77D76A731_Channel(base.z, metallic, vh, specular)), nl);
}

float Vts_66B62DB77D76A731_Dot3(float3 a, float3 b)
{
    return (((a.x * b.x) + (a.y * b.y)) + (a.z * b.z));
}

float Vts_66B62DB77D76A731_Fresnel(float f0, float vh)
{
    float x = (1.0 - vh);
    return (f0 + ((((((1.0 - f0) * x) * x) * x) * x) * x));
}

float Vts_66B62DB77D76A731_Geometry(float n, float k)
{
    return (n / ((n * (1.0 - k)) + k));
}

float3 Vts_66B62DB77D76A731_HemisphereLight(float3 base, float3 normal, float3 view, float3 sky, float3 ground, float metallic, float roughness)
{
    float blend = clamp(((normal.y * 0.5) + 0.5), 0.0, 1.0);
    float3 ambient = Vts_66B62DB77D76A731_Add3(Vts_66B62DB77D76A731_Scale3(sky, blend), Vts_66B62DB77D76A731_Scale3(ground, (1.0 - blend)));
    float3 diffuse = Vts_66B62DB77D76A731_Scale3(Vts_66B62DB77D76A731_Mul3(base, ambient), (1.0 - metallic));
    float nv = max(Vts_66B62DB77D76A731_Dot3(normal, view), 0.0);
    float3 reflected = Vts_66B62DB77D76A731_Sub3(Vts_66B62DB77D76A731_Scale3(normal, (2.0 * nv)), view);
    float specularBlend = clamp(((((reflected.y * (1.0 - roughness)) + (normal.y * roughness)) * 0.5) + 0.5), 0.0, 1.0);
    float3 environment = Vts_66B62DB77D76A731_Add3(Vts_66B62DB77D76A731_Scale3(sky, specularBlend), Vts_66B62DB77D76A731_Scale3(ground, (1.0 - specularBlend)));
    float3 fresnel = float3(Vts_66B62DB77D76A731_Fresnel(((0.04 * (1.0 - metallic)) + (base.x * metallic)), nv), Vts_66B62DB77D76A731_Fresnel(((0.04 * (1.0 - metallic)) + (base.y * metallic)), nv), Vts_66B62DB77D76A731_Fresnel(((0.04 * (1.0 - metallic)) + (base.z * metallic)), nv));
    return Vts_66B62DB77D76A731_Add3(diffuse, Vts_66B62DB77D76A731_Mul3(environment, fresnel));
}

float3 Vts_66B62DB77D76A731_Mul3(float3 a, float3 b)
{
    return float3((a.x * b.x), (a.y * b.y), (a.z * b.z));
}

float3 Vts_66B62DB77D76A731_Scale3(float3 a, float s)
{
    return float3((a.x * s), (a.y * s), (a.z * s));
}

float Vts_66B62DB77D76A731_ShadowTap(Texture2D<float4> map, SamplerState sampler, float2 uv, float depth)
{
    float4 stored = map.Sample(sampler, uv);
    if ((depth <= stored.x))
    {
        return 1.0;
    }
    return 0.0;
}

float Vts_66B62DB77D76A731_ShadowVisibility(Texture2D<float4> map, SamplerState sampler, float3 projected, float4 parameters, float nl)
{
    if ((((((((parameters.x < 0.5) || (projected.x < (-1.0))) || (projected.x > 1.0)) || (projected.y < (-1.0))) || (projected.y > 1.0)) || (projected.z < 0.0)) || (projected.z > 1.0)))
    {
        return 1.0;
    }
    float u = ((projected.x * 0.5) + 0.5);
    float v = ((projected.y * 0.5) + 0.5);
    float t = parameters.z;
    float z = (projected.z - (parameters.y * (1.0 + (2.0 * (1.0 - nl)))));
    return (((((((((Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2((u - t), (v - t)), z) + Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2(u, (v - t)), z)) + Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2((u + t), (v - t)), z)) + Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2((u - t), v), z)) + Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2(u, v), z)) + Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2((u + t), v), z)) + Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2((u - t), (v + t)), z)) + Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2(u, (v + t)), z)) + Vts_66B62DB77D76A731_ShadowTap(map, sampler, float2((u + t), (v + t)), z)) / 9.0);
}

float3 Vts_66B62DB77D76A731_Sub3(float3 a, float3 b)
{
    return float3((a.x - b.x), (a.y - b.y), (a.z - b.z));
}

float3 Vts_66B62DB77D76A731_Unit(float3 a)
{
    return Vts_66B62DB77D76A731_Scale3(a, (1.0 / sqrt(max(Vts_66B62DB77D76A731_Dot3(a, a), 1E-06))));
}

float Vts_F85A4B8D4C402A9E_ExperimentalVisibility(Texture2D<float4> field, SamplerState sampler, float2 uv, float depthVisibility)
{
    float4 a = field.Sample(sampler, float2((uv.x - 0.015625), (uv.y - 0.015625)));
    float4 b = field.Sample(sampler, float2(uv.x, (uv.y - 0.015625)));
    float4 c = field.Sample(sampler, float2((uv.x + 0.015625), (uv.y - 0.015625)));
    float4 d = field.Sample(sampler, float2((uv.x - 0.015625), uv.y));
    float4 e = field.Sample(sampler, uv);
    float4 f = field.Sample(sampler, float2((uv.x + 0.015625), uv.y));
    float4 g = field.Sample(sampler, float2((uv.x - 0.015625), (uv.y + 0.015625)));
    float4 h = field.Sample(sampler, float2(uv.x, (uv.y + 0.015625)));
    float4 i = field.Sample(sampler, float2((uv.x + 0.015625), (uv.y + 0.015625)));
    return (1.0 - (((((((((a.x + b.x) + c.x) + d.x) + e.x) + f.x) + g.x) + h.x) + i.x) / 9.0));
}

ModelVaryings VertexMain(VertexInput input)
{
    ModelVaryings result = (ModelVaryings)0;
    result.position = float4(Row(material.clipX, input.position), Row(material.clipY, input.position), Row(material.clipZ, input.position), Row(material.clipW, input.position));
    result.world = float3(input.position.x, input.position.y, input.position.z);
    result.normal = input.normal;
    result.color = input.color;
    result.uv = input.uv;
    result.tangent = input.tangent;
    return result;
}

ModelOutput PixelMain(ModelVaryings input, PixelBuiltins builtins)
{
    if (((!builtins.frontFace) && (material.flags.z < 0.5)))
    {
        discard;
    }
    float4 base = ((baseMap.Sample(baseSampler, input.uv) * material.baseColor) * input.color);
    if (((material.flags.y > 0.5) && (base.w < material.emissiveAlpha.w)))
    {
        discard;
    }
    if ((material.flags.x > 0.5))
    {
        ModelOutput result = (ModelOutput)0;
        result.color = float4(base.x, base.y, base.z, 1.0);
        return result;
    }
    float4 nt = normalMap.Sample(normalSampler, input.uv);
    float3 tangent = Vts_66B62DB77D76A731_Unit(float3(input.tangent.x, input.tangent.y, input.tangent.z));
    float3 geometric = Vts_66B62DB77D76A731_Unit(input.normal);
    float3 bitangent = Vts_66B62DB77D76A731_Scale3(Vts_66B62DB77D76A731_Cross3(geometric, tangent), input.tangent.w);
    float3 normal = Vts_66B62DB77D76A731_Unit(Vts_66B62DB77D76A731_Add3(Vts_66B62DB77D76A731_Add3(Vts_66B62DB77D76A731_Scale3(tangent, (((nt.x * 2.0) - 1.0) * material.factors.z)), Vts_66B62DB77D76A731_Scale3(bitangent, (((nt.y * 2.0) - 1.0) * material.factors.z))), Vts_66B62DB77D76A731_Scale3(geometric, ((nt.z * 2.0) - 1.0))));
    if ((!builtins.frontFace))
    {
        normal = Vts_66B62DB77D76A731_Scale3(normal, (-1.0));
    }
    float3 light = Vts_66B62DB77D76A731_Unit(float3(material.light.x, material.light.y, material.light.z));
    float3 view = Vts_66B62DB77D76A731_Unit(Vts_66B62DB77D76A731_Sub3(float3(material.eye.x, material.eye.y, material.eye.z), input.world));
    float4 mr = metalMap.Sample(metalSampler, input.uv);
    float metallic = clamp((mr.z * material.factors.x), 0.0, 1.0);
    float roughness = clamp((mr.y * material.factors.y), 0.045, 1.0);
    float3 p = float3(input.world.x, input.world.y, input.world.z);
    float3 projected = float3(Row(material.shadowX, p), Row(material.shadowY, p), Row(material.shadowZ, p));
    float depthVisibility = Vts_66B62DB77D76A731_ShadowVisibility(shadowMap, shadowSampler, projected, material.shadowParameters, max(Vts_66B62DB77D76A731_Dot3(normal, light), 0.0));
    float visibility = Vts_F85A4B8D4C402A9E_ExperimentalVisibility(occlusionMap, occlusionSampler, input.uv, depthVisibility);
    float3 direct = Vts_66B62DB77D76A731_Scale3(Vts_66B62DB77D76A731_Mul3(Vts_66B62DB77D76A731_DirectLight(float3(base.x, base.y, base.z), normal, view, light, metallic, roughness), float3(material.sun.x, material.sun.y, material.sun.z)), (material.sun.w * visibility));
    float occlusion = 1.0;
    float3 ambient = Vts_66B62DB77D76A731_Scale3(Vts_66B62DB77D76A731_HemisphereLight(float3(base.x, base.y, base.z), normal, view, float3(material.sky.x, material.sky.y, material.sky.z), float3(material.ground.x, material.ground.y, material.ground.z), metallic, roughness), occlusion);
    float4 emission = emissiveMap.Sample(emissiveSampler, input.uv);
    float3 emitted = Vts_66B62DB77D76A731_Mul3(float3(emission.x, emission.y, emission.z), float3(material.emissiveAlpha.x, material.emissiveAlpha.y, material.emissiveAlpha.z));
    float3 lit = Vts_66B62DB77D76A731_Add3(Vts_66B62DB77D76A731_Add3(direct, ambient), emitted);
    ModelOutput result = (ModelOutput)0;
    result.color = float4(lit.x, lit.y, lit.z, 1.0);
    return result;
}
