using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;

namespace Aurelian.Shaders.Library;

/// <summary>Compile/cache selected engine compute modules without game assets
/// or repository paths. Compiler diagnostics and SPIR-V validation remain owned
/// by the existing Visual TypeScript backend.</summary>
public sealed class BuiltinComputeShaderCache
{
    private readonly Dictionary<string, byte[]> programs = new(StringComparer.Ordinal);

    // Variants bind a typed implementation of an imported module before compilation.
    // The cache key identifies the closed program. The shared entry retains its own name.
    private sealed record Variant(string Entry, string Import, string Implementation);

    private static readonly Dictionary<string, Variant> Variants = new(StringComparer.Ordinal)
    {
        ["WideCooperativeAttention64.Keys64"] = new("WideCooperativeAttention64.v.ts", "KeyTile32.v.ts", "KeyTile64.v.ts"),
        ["LayerNorm384"] = new("LayerNorm768.v.ts", "NormWidth768.v.ts", "NormWidth384.v.ts"),
        ["FusedAttention64.Causal"] = new("FusedAttention64.v.ts", "AttentionLayout.v.ts", "CausalAttentionLayout.v.ts"),
        ["DenseGemv.Packed"] = new("DenseGemv.v.ts", "AttentionOperand.v.ts", "PackedAttentionOperand.v.ts"),
        ["QuantizedGemv.Lanes16"] = new("QuantizedGemv4.v.ts", "GemvLanes32.v.ts", "GemvLanes16.v.ts"),
        ["QuantizedGemv.Lanes8"] = new("QuantizedGemv4.v.ts", "GemvLanes32.v.ts", "GemvLanes8.v.ts"),
        ["FusedGroupedAttention.Keys8"] = new("FusedGroupedAttention.v.ts", "AttentionStripe32.v.ts", "AttentionStripe16.v.ts"),
        ["FusedAttention64.Packed"] = new("FusedAttention64.v.ts", "AttentionOperand.v.ts", "PackedAttentionOperand.v.ts"),
    };

    public byte[] Get(string name)
    {
        lock (programs)
        {
            return GetOrCompile(name);
        }
    }

    private byte[] GetOrCompile(string name)
    {
        if (programs.TryGetValue(name, out var cached))
        {
            return cached;
        }
        Variants.TryGetValue(name, out var variant);
        string entry = variant?.Entry ?? name;
        BuiltinShaderLibrary.RequireKind(entry, ShaderSourceKind.Compute);
        var sources = GpuSourceLoader.Load(entry, path => BuiltinShaderLibrary.TryRead(
            variant is not null && path == variant.Import ? variant.Implementation : path));
        var module = GpuComputeBinder.Compile(new(sources));
        if (!module.Success)
        {
            throw new InvalidDataException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        }
        var compiled = VdMirComputeBackend.Compile(module);
        if (!compiled.SpirvValidated || compiled.Spirv.Length == 0)
        {
            throw new InvalidDataException(compiled.DxcOutput + compiled.SpirvValidationOutput);
        }
        programs.Add(name, compiled.Spirv);
        return compiled.Spirv;
    }
}
