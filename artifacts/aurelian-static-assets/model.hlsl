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

float3 Add3(float3 a, float3 b);
float Channel(float base, float metallic, float vh, float specular);
float3 Cross3(float3 a, float3 b);
float Dot3(float3 a, float3 b);
float Fresnel(float f0, float vh);
float Geometry(float n, float k);
float3 Mul3(float3 a, float3 b);
float Row(float4 row, float3 p);
float3 Scale3(float3 a, float s);
float3 Sub3(float3 a, float3 b);
float3 Unit(float3 a);

float3 Add3(float3 a, float3 b)
{
    return float3((a.x + b.x), (a.y + b.y), (a.z + b.z));
}

float Channel(float base, float metallic, float vh, float specular)
{
    float f0 = ((0.04 * (1.0 - metallic)) + (base * metallic));
    float f = Fresnel(f0, vh);
    return (((((1.0 - f) * (1.0 - metallic)) * base) / 3.14159265) + (f * specular));
}

float3 Cross3(float3 a, float3 b)
{
    return float3(((a.y * b.z) - (a.z * b.y)), ((a.z * b.x) - (a.x * b.z)), ((a.x * b.y) - (a.y * b.x)));
}

float Dot3(float3 a, float3 b)
{
    return (((a.x * b.x) + (a.y * b.y)) + (a.z * b.z));
}

float Fresnel(float f0, float vh)
{
    float x = (1.0 - vh);
    return (f0 + ((((((1.0 - f0) * x) * x) * x) * x) * x));
}

float Geometry(float n, float k)
{
    return (n / ((n * (1.0 - k)) + k));
}

float3 Mul3(float3 a, float3 b)
{
    return float3((a.x * b.x), (a.y * b.y), (a.z * b.z));
}

float Row(float4 row, float3 p)
{
    return ((((row.x * p.x) + (row.y * p.y)) + (row.z * p.z)) + row.w);
}

float3 Scale3(float3 a, float s)
{
    return float3((a.x * s), (a.y * s), (a.z * s));
}

float3 Sub3(float3 a, float3 b)
{
    return float3((a.x - b.x), (a.y - b.y), (a.z - b.z));
}

float3 Unit(float3 a)
{
    return Scale3(a, (1.0 / sqrt(max(Dot3(a, a), 0.000001))));
}

ModelVaryings VertexMain(VertexInput input)
{
    ModelVaryings result;
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
        ModelOutput result;
        result.color = float4(base.x, base.y, base.z, 1.0);
        return result;
    }
    float4 nt = normalMap.Sample(normalSampler, input.uv);
    float3 tangent = Unit(float3(input.tangent.x, input.tangent.y, input.tangent.z));
    float3 geometric = Unit(input.normal);
    float3 bitangent = Scale3(Cross3(geometric, tangent), input.tangent.w);
    float3 normal = Unit(Add3(Add3(Scale3(tangent, (((nt.x * 2.0) - 1.0) * material.factors.z)), Scale3(bitangent, (((nt.y * 2.0) - 1.0) * material.factors.z))), Scale3(geometric, ((nt.z * 2.0) - 1.0))));
    if ((!builtins.frontFace))
    {
        normal = Scale3(normal, (-1.0));
    }
    float3 light = Unit(float3(0.36, 0.80, 0.48));
    float3 view = Unit(Sub3(float3(material.eye.x, material.eye.y, material.eye.z), input.world));
    float3 half = Unit(Add3(light, view));
    float nl = max(Dot3(normal, light), 0.0);
    float nv = max(Dot3(normal, view), 0.0001);
    float nh = max(Dot3(normal, half), 0.0);
    float vh = max(Dot3(view, half), 0.0);
    float4 mr = metalMap.Sample(metalSampler, input.uv);
    float metallic = clamp((mr.z * material.factors.x), 0.0, 1.0);
    float roughness = clamp((mr.y * material.factors.y), 0.045, 1.0);
    float a = (roughness * roughness);
    float a2 = (a * a);
    float denominator = (((nh * nh) * (a2 - 1.0)) + 1.0);
    float distribution = (a2 / max(((3.14159265 * denominator) * denominator), 0.000001));
    float k = (((roughness + 1.0) * (roughness + 1.0)) / 8.0);
    float specular = (((distribution * Geometry(nv, k)) * Geometry(nl, k)) / max(((4.0 * nv) * nl), 0.0001));
    float3 direct = Scale3(float3(Channel(base.x, metallic, vh, specular), Channel(base.y, metallic, vh, specular), Channel(base.z, metallic, vh, specular)), (nl * 2.5));
    float4 ao = occlusionMap.Sample(occlusionSampler, input.uv);
    float ambient = (0.12 * ((1.0 - material.factors.w) + (ao.x * material.factors.w)));
    float4 emission = emissiveMap.Sample(emissiveSampler, input.uv);
    float3 emitted = Mul3(float3(emission.x, emission.y, emission.z), float3(material.emissiveAlpha.x, material.emissiveAlpha.y, material.emissiveAlpha.z));
    float3 lit = Add3(Add3(direct, Scale3(float3(base.x, base.y, base.z), ambient)), emitted);
    ModelOutput result;
    result.color = float4(clamp(lit.x, 0.0, 1.0), clamp(lit.y, 0.0, 1.0), clamp(lit.z, 0.0, 1.0), 1.0);
    return result;
}
