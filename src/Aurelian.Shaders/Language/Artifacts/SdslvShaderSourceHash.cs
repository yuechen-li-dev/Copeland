using System.Security.Cryptography;
using System.Text;

namespace Aurelian.Shaders.Language.Artifacts;

public sealed record SdslvShaderSourceHash(
    string Algorithm,
    string Value)
{
    public static SdslvShaderSourceHash ComputeSha256(string sourceText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceText));
        return new SdslvShaderSourceHash("SHA-256", Convert.ToHexString(bytes).ToLowerInvariant());
    }
}
