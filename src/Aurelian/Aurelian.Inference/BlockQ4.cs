namespace Aurelian.Inference;

/// <summary>Aurelian Q4B32: one FP32 scale and four nibble words per 32 values.
/// This is a versioned local PTQ representation, not GGUF Q4_0 or a QAT claim.</summary>
public static class BlockQ4
{
    public const int ValuesPerBlock = 32;
    public const int WordsPerBlock = 5;

    public static uint[] Pack(ReadOnlySpan<float> values)
    {
        if (values.Length == 0 || values.Length % ValuesPerBlock != 0)
        {
            throw new ArgumentException("Q4B32 requires complete 32-value blocks.", nameof(values));
        }
        var packed = new uint[values.Length / ValuesPerBlock * WordsPerBlock];
        for (int first = 0; first < values.Length; first += ValuesPerBlock)
        {
            float extreme = 0;
            for (int index = 0; index < ValuesPerBlock; index++)
            {
                float value = values[first + index];
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("AUR-Q4-001: Nonfinite source weight.");
                }
                if (MathF.Abs(value) > MathF.Abs(extreme))
                {
                    extreme = value;
                }
            }
            float scale = extreme / -8;
            int offset = first / ValuesPerBlock * WordsPerBlock;
            packed[offset] = BitConverter.SingleToUInt32Bits(scale);
            for (int index = 0; index < ValuesPerBlock; index++)
            {
                int quantized = scale == 0 ? 8 : Math.Clamp((int)MathF.Floor(values[first + index] / scale + 8.5f), 0, 15);
                packed[offset + 1 + index / 8] |= (uint)quantized << (index % 8 * 4);
            }
        }
        return packed;
    }

    public static float Decode(ReadOnlySpan<uint> words, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int offset = index / ValuesPerBlock * WordsPerBlock;
        float scale = BitConverter.UInt32BitsToSingle(words[offset]);
        int nibble = (int)(words[offset + 1 + index % ValuesPerBlock / 8] >> (index % 8 * 4) & 15);
        return scale * (nibble - 8);
    }
}
