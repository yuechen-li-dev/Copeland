using System.Text;

namespace Aurelian.Assets.Shaders;

internal static class ShaderArtifactSpirvEncoding
{
    public const string Binary = "binary";
    public const string Hex = "hex";

    public static bool IsSupported(string encoding) =>
        string.Equals(encoding, Binary, StringComparison.Ordinal) || string.Equals(encoding, Hex, StringComparison.Ordinal);

    public static bool TryDecodeHex(string text, out byte[] bytes, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);

        var compact = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (!IsHex(c))
            {
                bytes = [];
                error = $"Hex SPIR-V text contains non-hex character U+{(int)c:X4}.";
                return false;
            }

            compact.Append(c);
        }

        if (compact.Length % 2 != 0)
        {
            bytes = [];
            error = "Hex SPIR-V text contains an odd number of hexadecimal digits.";
            return false;
        }

        bytes = new byte[compact.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            int high = FromHex(compact[i * 2]);
            int low = FromHex(compact[(i * 2) + 1]);
            bytes[i] = (byte)((high << 4) | low);
        }

        error = string.Empty;
        return true;
    }

    private static bool IsHex(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int FromHex(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "Character is not hexadecimal."),
    };
}
