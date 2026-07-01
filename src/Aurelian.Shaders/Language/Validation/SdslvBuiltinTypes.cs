namespace Aurelian.Shaders.Language.Validation;

public static class SdslvBuiltinTypes
{
    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "void",
        "bool",
        "int",
        "uint",
        "float",
        "float2",
        "float3",
        "float4",
        "float4x4",
        "string",
    };
}
