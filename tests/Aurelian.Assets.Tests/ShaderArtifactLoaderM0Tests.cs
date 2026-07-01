using System.Security.Cryptography;
using Aurelian.Assets.Shaders;
using Aurelian.Rendering.Contracts.Shaders;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class ShaderArtifactLoaderM0Tests
{
    [Fact]
    public void ShaderArtifactLoader_LoadsCompiledShaderProgramFromTomlAndSpv()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path);

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.NotNull(result.Program);
        Assert.Equal(CompiledShaderProgram.CurrentFormatVersion, result.Program.FormatVersion);
        Assert.Collection(
            result.Program.Stages,
            vertex =>
            {
                Assert.Equal(CompiledShaderStageKind.Vertex, vertex.Stage);
                Assert.Equal("VSMain", vertex.EntryPoint);
                Assert.Equal("vs_6_0", vertex.Profile);
            },
            fragment =>
            {
                Assert.Equal(CompiledShaderStageKind.Fragment, fragment.Stage);
                Assert.Equal("PSMain", fragment.EntryPoint);
                Assert.Equal("ps_6_0", fragment.Profile);
            });
    }


    [Fact]
    public void ShaderArtifactLoader_LoadsHexEncodedSpirvArtifact()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path, spirvEncoding: "hex");

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.All(result.Program!.Stages, stage => Assert.NotEmpty(stage.SpirvBytes));
    }

    [Fact]
    public void ShaderArtifactLoader_RejectsUnsupportedSpirvEncoding()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path, spirvEncoding: "decimal");

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.Equal(ShaderArtifactLoadStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ShaderArtifactDiagnosticCodes.UnsupportedSpirvEncoding);
    }

    [Fact]
    public void ShaderArtifactLoader_RejectsMalformedHexSpirv()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path, spirvEncoding: "hex");
        File.WriteAllText(Path.Combine(temp.Path, "VSMain.spv.hex"), "0302zz");

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.Equal(ShaderArtifactLoadStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ShaderArtifactDiagnosticCodes.HexSpirvParseFailed);
    }

    [Fact]
    public void ShaderArtifactLoader_HexEncodingHashChecksDecodedBytes()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path, spirvEncoding: "hex");
        File.WriteAllText(Path.Combine(temp.Path, "PSMain.spv.hex"), "0302230702\n000000");

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Contains(result.Program!.Stages, stage => stage.Stage == CompiledShaderStageKind.Fragment && stage.SpirvBytes.SequenceEqual(new byte[] { 0x03, 0x02, 0x23, 0x07, 0x02, 0x00, 0x00, 0x00 }));
    }

    [Fact]
    public void ShaderArtifactLoader_RejectsMissingManifest()
    {
        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "shader.toml"));

        Assert.Equal(ShaderArtifactLoadStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ShaderArtifactDiagnosticCodes.ManifestMissing);
    }

    [Fact]
    public void ShaderArtifactLoader_RejectsUnsupportedFormat()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path, format: "aurelian.shader-artifact/999");

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.Equal(ShaderArtifactLoadStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ShaderArtifactDiagnosticCodes.UnsupportedFormat);
    }

    [Fact]
    public void ShaderArtifactLoader_RejectsMissingSpvFile()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path);
        File.Delete(Path.Combine(temp.Path, "VSMain.spv"));

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.Equal(ShaderArtifactLoadStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ShaderArtifactDiagnosticCodes.SpirvFileMissing);
    }

    [Fact]
    public void ShaderArtifactLoader_RejectsHashMismatch()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path);
        File.WriteAllBytes(Path.Combine(temp.Path, "PSMain.spv"), [0x03, 0x02, 0x23, 0x07, 0xff]);

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.Equal(ShaderArtifactLoadStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ShaderArtifactDiagnosticCodes.SpirvHashMismatch);
    }

    [Fact]
    public void ShaderArtifactLoader_RejectsDuplicateStage()
    {
        using var temp = TempDirectory.Create();
        WriteArtifact(temp.Path, duplicateVertexStage: true);

        ShaderArtifactLoadResult result = ShaderArtifactLoader.LoadCompiledShaderProgram(Path.Combine(temp.Path, "shader.toml"));

        Assert.Equal(ShaderArtifactLoadStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ShaderArtifactDiagnosticCodes.DuplicateStage);
    }

    private static void WriteArtifact(
        string directory,
        string format = ShaderArtifactManifest.CurrentFormatVersion,
        bool duplicateVertexStage = false,
        string spirvEncoding = "binary")
    {
        byte[] vertex = [0x03, 0x02, 0x23, 0x07, 0x01, 0x00, 0x00, 0x00];
        byte[] fragment = [0x03, 0x02, 0x23, 0x07, 0x02, 0x00, 0x00, 0x00];
        bool hex = string.Equals(spirvEncoding, "hex", StringComparison.Ordinal) || string.Equals(spirvEncoding, "decimal", StringComparison.Ordinal);
        string vertexPath = hex ? "VSMain.spv.hex" : "VSMain.spv";
        string fragmentPath = hex ? "PSMain.spv.hex" : "PSMain.spv";
        if (hex)
        {
            File.WriteAllText(Path.Combine(directory, vertexPath), EncodeHex(vertex));
            File.WriteAllText(Path.Combine(directory, fragmentPath), EncodeHex(fragment));
        }
        else
        {
            File.WriteAllBytes(Path.Combine(directory, vertexPath), vertex);
            File.WriteAllBytes(Path.Combine(directory, fragmentPath), fragment);
        }

        string secondStageName = duplicateVertexStage ? "vertex" : "fragment";
        string vertexEncodingLine = spirvEncoding == "binary" ? string.Empty : $"spirv_encoding = \"{spirvEncoding}\"{Environment.NewLine}";
        string fragmentEncodingLine = spirvEncoding == "binary" ? string.Empty : $"spirv_encoding = \"{spirvEncoding}\"{Environment.NewLine}";
        File.WriteAllText(Path.Combine(directory, "shader.toml"), $$"""
            format = "{{format}}"
            source_language = "SDSL-V"
            source_name = "smoke_triangle.sdslv"
            source_sha256 = "{{new string('1', 64)}}"

            [[stages]]
            stage = "vertex"
            entry_point = "VSMain"
            profile = "vs_6_0"
            {{vertexEncodingLine}}spirv_path = "{{vertexPath}}"
            spirv_sha256 = "{{ComputeSha256(vertex)}}"
            source_name = "smoke_triangle.sdslv"

            [[stages]]
            stage = "{{secondStageName}}"
            entry_point = "PSMain"
            profile = "ps_6_0"
            {{fragmentEncodingLine}}spirv_path = "{{fragmentPath}}"
            spirv_sha256 = "{{ComputeSha256(fragment)}}"
            source_name = "smoke_triangle.sdslv"
            """);
    }

    private static string EncodeHex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2"))) + Environment.NewLine;

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FormatDiagnostics(ShaderArtifactLoadResult result) =>
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
