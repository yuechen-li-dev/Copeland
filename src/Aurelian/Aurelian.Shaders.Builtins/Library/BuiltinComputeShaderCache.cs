using System.Buffers.Binary;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;

namespace Aurelian.Shaders.Library;

/// <summary>Compile/cache selected engine compute modules without game assets
/// or repository paths. Compiler diagnostics and SPIR-V validation remain owned
/// by the existing Visual TypeScript backend.</summary>
public sealed class BuiltinComputeShaderCache
{
    private readonly Dictionary<(string Name, bool PushParameters), byte[]> programs = [];
    private static readonly string[] PrecompiledNames =
    [
        "FusedGroupedAttention.SplitDecode", "MergeAttentionPartials.v.ts",
        "ResidentSgemm.v.ts", "FusedGroupedAttention.Keys8", "FusedGroupedAttention.v.ts", "ArgMax.v.ts",
        "RowReduction.v.ts",
    ];

    public bool UsePrecompiledShaders { get; init; }
    public static ReadOnlySpan<string> PrecompiledShaderNames => PrecompiledNames;

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
        ["QuantizedGemv.InterleavedHalfScale"] = new("QuantizedGemv4.v.ts", "GemvLanes32.v.ts", "Q4Rows8HalfScale.v.ts"),
        ["QuantizedQ6KWord.Interleaved8"] = new("QuantizedQ6KWord.v.ts", "Q6Rows1.v.ts", "Q6Rows8.v.ts"),
        ["PackQ4WeightsHalf.InterleavedHalfScale"] = new("PackQ4WeightsHalf.v.ts", "Q4Rows1.v.ts", "Q4Rows8HalfScale.v.ts"),
        ["ResidentSgemm.InterleavedHalfScale"] = new("ResidentSgemm.v.ts", "Q4Rows1.v.ts", "Q4Rows8HalfScale.v.ts"),
        ["FusedGroupedAttention.Keys8"] = new("FusedGroupedAttention.v.ts", "AttentionStripe32.v.ts", "AttentionStripe16.v.ts"),
        ["FusedGroupedAttention.SplitDecode"] = new("FusedGroupedAttention.v.ts", "AttentionStripe32.v.ts", "AttentionSplitDecode.v.ts"),
        ["NormalizeRotateCacheKey.Query"] = new("NormalizeRotateCacheKey.v.ts", "NormalizeRopeKey.v.ts", "NormalizeRopeQuery.v.ts"),
        ["DecoderNormalize1536.SplitProjection"] = new("DecoderNormalize1536.v.ts", "NormalizeSingleProjection.v.ts", "NormalizeSplitProjection.v.ts"),
        ["FusedAttention64.Packed"] = new("FusedAttention64.v.ts", "AttentionOperand.v.ts", "PackedAttentionOperand.v.ts"),
    };

    public byte[] Get(string name) => Get(name, pushParameters: false);

    public byte[] Get(string name, bool pushParameters)
    {
        lock (programs)
        {
            return GetOrCompile(name, pushParameters);
        }
    }

    private byte[] GetOrCompile(string name, bool pushParameters)
    {
        var key = (name, pushParameters);
        if (programs.TryGetValue(key, out var cached))
        {
            return cached;
        }
        if (UsePrecompiledShaders && Array.IndexOf(PrecompiledNames, name) >= 0)
        {
            byte[] spirv = LoadPrecompiled(name, pushParameters);
            programs.Add(key, spirv);
            return spirv;
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
        var compiled = VdMirComputeBackend.Compile(module, pushParameters ? (0, 3) : null);
        if (!compiled.SpirvValidated || compiled.Spirv.Length == 0)
        {
            throw new InvalidDataException(compiled.DxcOutput + compiled.SpirvValidationOutput);
        }
        programs.Add(key, compiled.Spirv);
        return compiled.Spirv;
    }

    public static string PrecompiledFileName(string name, bool pushParameters)
    {
        string abi = pushParameters ? "push" : "storage";
        return $"{name}.{abi}.spv";
    }

    private static byte[] LoadPrecompiled(string name, bool pushParameters)
    {
        string file = PrecompiledFileName(name, pushParameters);
        using Stream stream = typeof(BuiltinComputeShaderCache).Assembly.GetManifestResourceStream(
            "Aurelian.Shaders.Precompiled." + file)
            ?? throw new InvalidDataException($"The built-in shader pack is missing {file}.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (bytes.Length < 20 || bytes.Length % sizeof(uint) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x07230203)
        {
            throw new InvalidDataException($"The built-in shader {file} has an invalid SPIR-V header.");
        }
        return bytes;
    }
}
