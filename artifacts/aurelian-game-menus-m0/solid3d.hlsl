// Generated from canonical VD-MIR graphics.m3. Do not edit.

// semantic space clip.position physically lowers to float4
// semantic space world.position physically lowers to float3

struct CameraMaterial
{
    float4 clipX; // offset 0, size 16, align 16
    float4 clipY; // offset 16, size 16, align 16
    float4 clipZ; // offset 32, size 16, align 16
    float4 clipW; // offset 48, size 16, align 16
};

struct SolidOutput
{
    float4 color : SV_Target0;
};

struct SolidVaryings
{
    float4 position : SV_Position;
    float4 color : TEXCOORD0;
};

struct VertexInput
{
    float3 position : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float4 color : TEXCOORD2;
};

[[vk::binding(0, 0)]] ConstantBuffer<CameraMaterial> camera;

float ProjectRow(float4 row, float3 p)
{
    return ((((row.x * p.x) + (row.y * p.y)) + (row.z * p.z)) + row.w);
}

SolidVaryings VertexMain(VertexInput input)
{
    float light = (0.28 + (0.72 * max((((input.normal.x * 0.36) + (input.normal.y * 0.80)) + (input.normal.z * 0.48)), 0.0)));
    SolidVaryings result;
    result.position = float4(ProjectRow(camera.clipX, input.position), ProjectRow(camera.clipY, input.position), ProjectRow(camera.clipZ, input.position), ProjectRow(camera.clipW, input.position));
    result.color = (input.color * float4(light, light, light, 1.0));
    return result;
}

SolidOutput PixelMain(SolidVaryings input)
{
    SolidOutput result;
    result.color = input.color;
    return result;
}
