namespace TinyFarm.Native;

internal static class TinyFarmBankMask
{
    /// <summary>One dry texel border permits linear wet coverage at the finite field boundary.
    /// Interior semantic channels are copied byte-for-byte. No gameplay mask is mutated.</summary>
    public static byte[] Pad(byte[] source, int width, int height)
    {
        if (source.Length != checked(width * height * 4))
        {
            throw new ArgumentException("Field extent and RGBA payload disagree.", nameof(source));
        }
        int paddedWidth = width + 2;
        byte[] result = new byte[checked(paddedWidth * (height + 2) * 4)];
        for (int y = 0; y < height + 2; y++)
        {
            for (int x = 0; x < paddedWidth; x++)
            {
                int sourceX = Math.Clamp(x - 1, 0, width - 1);
                int sourceY = Math.Clamp(y - 1, 0, height - 1);
                int from = (sourceY * width + sourceX) * 4;
                int to = (y * paddedWidth + x) * 4;
                Array.Copy(source, from, result, to, 4);
                if (x == 0 || y == 0 || x == width + 1 || y == height + 1)
                {
                    result[to + 2] = 0;
                }
            }
        }
        return result;
    }
}
