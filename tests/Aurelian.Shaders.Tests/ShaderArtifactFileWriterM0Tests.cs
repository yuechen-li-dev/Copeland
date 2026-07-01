using System.Security.Cryptography;
using Aurelian.Shaders.Language.Artifacts.Files;
using Aurelian.Shaders.Language.Artifacts.SdslvSpirv;
using Aurelian.Shaders.Language.Artifacts.Spirv;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ShaderArtifactFileWriterM0Tests
{
    [Fact]
    public void ShaderArtifactFileWriter_WritesTomlSpvAndHlslFiles()
    {
        using var temp = TempDirectory.Create();

        ShaderArtifactFileWriteResult result = ShaderArtifactFileWriter.WriteSdslvSpirvArtifact(SuccessfulArtifact(), temp.Path);

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.True(File.Exists(Path.Combine(temp.Path, "shader.toml")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "VSMain.spv")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "PSMain.spv")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "generated.hlsl")));
        string manifest = File.ReadAllText(Path.Combine(temp.Path, "shader.toml"));
        Assert.Contains("format = \"aurelian.shader-artifact/0\"", manifest);
        Assert.Contains("spirv_path = \"VSMain.spv\"", manifest);
        Assert.DoesNotContain("spirv_bytes", manifest);
    }

    [Fact]
    public void ShaderArtifactFileWriter_WrittenSpvHashesMatchManifest()
    {
        using var temp = TempDirectory.Create();

        ShaderArtifactFileWriteResult result = ShaderArtifactFileWriter.WriteSdslvSpirvArtifact(SuccessfulArtifact(), temp.Path);

        Assert.True(result.Success, FormatDiagnostics(result));
        string vertexHash = ComputeSha256(File.ReadAllBytes(Path.Combine(temp.Path, "VSMain.spv")));
        string fragmentHash = ComputeSha256(File.ReadAllBytes(Path.Combine(temp.Path, "PSMain.spv")));
        string manifest = File.ReadAllText(Path.Combine(temp.Path, "shader.toml"));
        Assert.Contains($"spirv_sha256 = \"{vertexHash}\"", manifest);
        Assert.Contains($"spirv_sha256 = \"{fragmentHash}\"", manifest);
    }


    [Fact]
    public void ShaderArtifactFileWriter_CanWriteHexEncodedSpirvFiles()
    {
        using var temp = TempDirectory.Create();

        ShaderArtifactFileWriteResult result = ShaderArtifactFileWriter.WriteSdslvSpirvArtifact(
            SuccessfulArtifact(),
            temp.Path,
            new ShaderArtifactFileWriteOptions("hex"));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.True(File.Exists(Path.Combine(temp.Path, "VSMain.spv.hex")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "PSMain.spv.hex")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "VSMain.spv")));
        Assert.Matches("^[0-9a-f\n]+$", File.ReadAllText(Path.Combine(temp.Path, "VSMain.spv.hex")));
    }

    [Fact]
    public void ShaderArtifactFileWriter_HexManifestUsesSpirvEncodingHex()
    {
        using var temp = TempDirectory.Create();

        ShaderArtifactFileWriteResult result = ShaderArtifactFileWriter.WriteSdslvSpirvArtifact(
            SuccessfulArtifact(),
            temp.Path,
            new ShaderArtifactFileWriteOptions("hex"));

        Assert.True(result.Success, FormatDiagnostics(result));
        string manifest = File.ReadAllText(Path.Combine(temp.Path, "shader.toml"));
        Assert.Contains("spirv_encoding = \"hex\"", manifest);
        Assert.Contains("spirv_path = \"VSMain.spv.hex\"", manifest);
        Assert.Contains("spirv_path = \"PSMain.spv.hex\"", manifest);
    }

    [Fact]
    public void ShaderArtifactFileWriter_HexHashesMatchRawBytes()
    {
        using var temp = TempDirectory.Create();

        ShaderArtifactFileWriteResult result = ShaderArtifactFileWriter.WriteSdslvSpirvArtifact(
            SuccessfulArtifact(),
            temp.Path,
            new ShaderArtifactFileWriteOptions("hex"));

        Assert.True(result.Success, FormatDiagnostics(result));
        string manifest = File.ReadAllText(Path.Combine(temp.Path, "shader.toml"));
        Assert.Contains($"spirv_sha256 = \"{ComputeSha256([0x03, 0x02, 0x23, 0x07, 0x01, 0x00, 0x00, 0x00])}\"", manifest);
        Assert.Contains($"spirv_sha256 = \"{ComputeSha256([0x03, 0x02, 0x23, 0x07, 0x02, 0x00, 0x00, 0x00])}\"", manifest);
    }

    [Fact]
    public void ShaderArtifactFileWriter_RejectsFailedArtifact()
    {
        var artifact = new SdslvSpirvShaderArtifact(
            SdslvSpirvShaderArtifact.CurrentFormatVersion,
            SdslvSpirvShaderArtifact.LanguageName,
            "shader.sdslv",
            new string('0', 64),
            string.Empty,
            null,
            [new SdslvSpirvShaderArtifactDiagnostic(SdslvSpirvShaderArtifactDiagnosticCodes.SpirvCompilationFailed, SdslvSpirvShaderArtifactDiagnosticSeverity.Error, "failed")]);

        ShaderArtifactFileWriteResult result = ShaderArtifactFileWriter.WriteSdslvSpirvArtifact(artifact, Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

        Assert.Equal(ShaderArtifactFileWriteStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ShaderArtifactFileDiagnosticCodes.ArtifactFailed);
    }

    private static SdslvSpirvShaderArtifact SuccessfulArtifact()
    {
        byte[] vertex = [0x03, 0x02, 0x23, 0x07, 0x01, 0x00, 0x00, 0x00];
        byte[] fragment = [0x03, 0x02, 0x23, 0x07, 0x02, 0x00, 0x00, 0x00];
        var spirv = new SpirvShaderArtifact(
            SpirvShaderArtifact.CurrentFormatVersion,
            SpirvShaderArtifact.LanguageName,
            [
                new SpirvShaderStageArtifact(HlslShaderStageKind.Vertex, "VSMain", "vs_6_0", "smoke_triangle.sdslv", new string('1', 64), ComputeSha256(vertex), vertex, []),
                new SpirvShaderStageArtifact(HlslShaderStageKind.Fragment, "PSMain", "ps_6_0", "smoke_triangle.sdslv", new string('1', 64), ComputeSha256(fragment), fragment, []),
            ],
            []);

        return new SdslvSpirvShaderArtifact(
            SdslvSpirvShaderArtifact.CurrentFormatVersion,
            SdslvSpirvShaderArtifact.LanguageName,
            "smoke_triangle.sdslv",
            new string('1', 64),
            "float4 VSMain() : SV_Position { return 0; }",
            spirv,
            []);
    }

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FormatDiagnostics(ShaderArtifactFileWriteResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;
        public string Path { get; }
        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
