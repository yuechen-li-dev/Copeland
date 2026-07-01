// Prebuilt visible-triangle debug HLSL checked in with the A69 TOML + SPIR-V artifact.
// The sample loads VSMain.spv and PSMain.spv at runtime and does not compile this file.
struct VSOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
};

VSOutput VSMain(float2 position : POSITION0, float4 color : COLOR0)
{
    VSOutput output;
    output.Position = float4(position, 0.0, 1.0);
    output.Color = color;
    return output;
}

float4 PSMain(VSOutput input) : SV_Target0
{
    return input.Color;
}
