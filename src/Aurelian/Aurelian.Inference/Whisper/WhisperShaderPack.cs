using System.Buffers.Binary;

namespace Aurelian.Inference.Whisper;

internal static class WhisperShaderPack
{
    private static readonly string[] ModuleNames =
    [
        "Activation.v.ts", "CooperativeProjection.v.ts", "CrossAttentionScores.v.ts", "CrossAttentionValues.v.ts",
        "DecoderScale.v.ts", "DenseGemv.Packed", "DenseGemv.v.ts", "GatherPackedHalf.v.ts", "GroupedAttention.v.ts",
        "Im2Col.v.ts", "LayerNorm384", "PackMatrixHalf.v.ts", "ProjectionEpilogue.v.ts", "RowReduction.v.ts",
        "TensorMap.v.ts", "WideCooperativeAttention64.v.ts",
    ];

    internal static ReadOnlySpan<string> Names => ModuleNames;

    internal static byte[] Load(string name)
    {
        using Stream stream = typeof(WhisperShaderPack).Assembly.GetManifestResourceStream(
            "Aurelian.Inference.Whisper.Shaders." + name + ".spv")
            ?? throw new InvalidOperationException($"Aurelian Whisper is missing the embedded shader {name}.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (bytes.Length < 20 || bytes.Length % sizeof(uint) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x07230203)
        {
            throw new InvalidDataException($"Embedded shader {name} has an invalid SPIR-V header.");
        }
        return bytes;
    }
}
