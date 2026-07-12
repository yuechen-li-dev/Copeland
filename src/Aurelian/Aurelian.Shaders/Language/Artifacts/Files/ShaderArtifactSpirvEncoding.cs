using System.Text;

namespace Aurelian.Shaders.Language.Artifacts.Files;

internal static class ShaderArtifactSpirvEncoding
{
    public const string Binary = "binary";
    public const string Hex = "hex";

    public static bool IsSupported(string encoding) =>
        string.Equals(encoding, Binary, StringComparison.Ordinal) || string.Equals(encoding, Hex, StringComparison.Ordinal);

    public static string EncodeHex(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var builder = new StringBuilder(bytes.Length * 2 + (bytes.Length / 32) + 1);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0 && i % 32 == 0)
            {
                builder.AppendLine();
            }

            builder.Append(bytes[i].ToString("x2"));
        }

        builder.AppendLine();
        return builder.ToString();
    }
}
