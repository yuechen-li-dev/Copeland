using System.Security.Cryptography;
using System.Text;
using Aurelian.Shaders.Language.Artifacts.SdslvSpirv;
using Aurelian.Shaders.Language.Artifacts.Spirv;

namespace Aurelian.Shaders.Language.Artifacts.Files;

public static class ShaderArtifactFileWriter
{
    public const string CurrentFormatVersion = "aurelian.shader-artifact/0";

    public static ShaderArtifactFileWriteResult WriteSdslvSpirvArtifact(
        SdslvSpirvShaderArtifact artifact,
        string outputDirectory) =>
        WriteSdslvSpirvArtifact(artifact, outputDirectory, new ShaderArtifactFileWriteOptions());

    public static ShaderArtifactFileWriteResult WriteSdslvSpirvArtifact(
        SdslvSpirvShaderArtifact artifact,
        string outputDirectory,
        ShaderArtifactFileWriteOptions? options)
    {
        var effectiveOptions = options ?? new ShaderArtifactFileWriteOptions();
        var diagnostics = new List<ShaderArtifactFileDiagnostic>();
        if (artifact is null)
        {
            diagnostics.Add(new ShaderArtifactFileDiagnostic(ShaderArtifactFileDiagnosticCodes.ArtifactMissing, "SDSL-V SPIR-V artifact is required."));
            return new ShaderArtifactFileWriteResult(ShaderArtifactFileWriteStatus.Rejected, null, diagnostics);
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            diagnostics.Add(new ShaderArtifactFileDiagnostic(ShaderArtifactFileDiagnosticCodes.OutputDirectoryMissing, "Output directory is required."));
            return new ShaderArtifactFileWriteResult(ShaderArtifactFileWriteStatus.Rejected, null, diagnostics);
        }

        if (!ShaderArtifactSpirvEncoding.IsSupported(effectiveOptions.SpirvEncoding))
        {
            diagnostics.Add(new ShaderArtifactFileDiagnostic(ShaderArtifactFileDiagnosticCodes.UnsupportedSpirvEncoding, $"SPIR-V encoding '{effectiveOptions.SpirvEncoding}' is not supported."));
            return new ShaderArtifactFileWriteResult(ShaderArtifactFileWriteStatus.Rejected, null, diagnostics);
        }

        if (!artifact.Success || artifact.SpirvArtifact is null)
        {
            diagnostics.Add(new ShaderArtifactFileDiagnostic(ShaderArtifactFileDiagnosticCodes.ArtifactFailed, "Only successful SDSL-V SPIR-V artifacts can be written as shader artifact files."));
            return new ShaderArtifactFileWriteResult(ShaderArtifactFileWriteStatus.Rejected, null, diagnostics);
        }

        foreach (SpirvShaderStageArtifact stage in artifact.SpirvArtifact.Stages)
        {
            if (stage.SpirvBytes is null || stage.SpirvBytes.Length == 0)
            {
                diagnostics.Add(new ShaderArtifactFileDiagnostic(ShaderArtifactFileDiagnosticCodes.StageMissingSpirv, $"Stage '{stage.Stage}' has no SPIR-V bytes."));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new ShaderArtifactFileWriteResult(ShaderArtifactFileWriteStatus.Rejected, null, diagnostics);
        }

        try
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            var stageFiles = new List<StageFile>();
            foreach (SpirvShaderStageArtifact stage in artifact.SpirvArtifact.Stages)
            {
                bool hexEncoding = string.Equals(effectiveOptions.SpirvEncoding, ShaderArtifactSpirvEncoding.Hex, StringComparison.Ordinal);
                string fileName = SanitizeFileName(stage.EntryPoint) + (hexEncoding ? ".spv.hex" : ".spv");
                string path = Path.Combine(fullOutputDirectory, fileName);
                if (hexEncoding)
                {
                    File.WriteAllText(path, ShaderArtifactSpirvEncoding.EncodeHex(stage.SpirvBytes), Encoding.UTF8);
                }
                else
                {
                    File.WriteAllBytes(path, stage.SpirvBytes);
                }

                string writtenHash = ComputeSha256(stage.SpirvBytes);
                if (!string.Equals(writtenHash, stage.SpirvSha256, StringComparison.Ordinal))
                {
                    diagnostics.Add(new ShaderArtifactFileDiagnostic(
                        ShaderArtifactFileDiagnosticCodes.HashMismatch,
                        $"Written SPIR-V hash '{writtenHash}' does not match artifact hash '{stage.SpirvSha256}' for stage '{stage.Stage}'.",
                        path));
                }

                stageFiles.Add(new StageFile(stage, effectiveOptions.SpirvEncoding, fileName, writtenHash, path));
            }

            string? generatedHlslPath = null;
            string? generatedHlslHash = null;
            if (!string.IsNullOrWhiteSpace(artifact.Hlsl))
            {
                generatedHlslPath = Path.Combine(fullOutputDirectory, "generated.hlsl");
                File.WriteAllText(generatedHlslPath, artifact.Hlsl, Encoding.UTF8);
                generatedHlslHash = ComputeSha256(Encoding.UTF8.GetBytes(artifact.Hlsl));
            }

            string manifestPath = Path.Combine(fullOutputDirectory, "shader.toml");
            File.WriteAllText(manifestPath, BuildManifest(artifact, stageFiles, generatedHlslHash), Encoding.UTF8);

            if (diagnostics.Count > 0)
            {
                return new ShaderArtifactFileWriteResult(ShaderArtifactFileWriteStatus.Failed, null, diagnostics);
            }

            return new ShaderArtifactFileWriteResult(
                ShaderArtifactFileWriteStatus.Written,
                new ShaderArtifactFileSet(fullOutputDirectory, manifestPath, stageFiles.Select(x => x.FullPath).ToArray(), generatedHlslPath),
                diagnostics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(new ShaderArtifactFileDiagnostic(ShaderArtifactFileDiagnosticCodes.FileWriteFailed, ex.Message, outputDirectory));
            return new ShaderArtifactFileWriteResult(ShaderArtifactFileWriteStatus.Failed, null, diagnostics);
        }
    }

    private static string BuildManifest(SdslvSpirvShaderArtifact artifact, IReadOnlyList<StageFile> stageFiles, string? generatedHlslHash)
    {
        var builder = new StringBuilder();
        builder.Append("format = ").Append(TomlString(CurrentFormatVersion)).AppendLine();
        builder.Append("source_language = ").Append(TomlString(artifact.Language)).AppendLine();
        builder.Append("source_name = ").Append(TomlString(artifact.SourceName)).AppendLine();
        builder.Append("source_sha256 = ").Append(TomlString(artifact.SourceSha256)).AppendLine();
        if (generatedHlslHash is not null)
        {
            builder.Append("generated_hlsl_path = \"generated.hlsl\"").AppendLine();
            builder.Append("generated_hlsl_sha256 = ").Append(TomlString(generatedHlslHash)).AppendLine();
        }

        foreach (StageFile file in stageFiles)
        {
            builder.AppendLine();
            builder.AppendLine("[[stages]]");
            builder.Append("stage = ").Append(TomlString(MapStage(file.Stage.Stage))).AppendLine();
            builder.Append("entry_point = ").Append(TomlString(file.Stage.EntryPoint)).AppendLine();
            builder.Append("profile = ").Append(TomlString(file.Stage.Profile)).AppendLine();
            if (!string.Equals(file.SpirvEncoding, ShaderArtifactSpirvEncoding.Binary, StringComparison.Ordinal))
            {
                builder.Append("spirv_encoding = ").Append(TomlString(file.SpirvEncoding)).AppendLine();
            }

            builder.Append("spirv_path = ").Append(TomlString(file.RelativePath)).AppendLine();
            builder.Append("spirv_sha256 = ").Append(TomlString(file.WrittenSha256)).AppendLine();
            builder.Append("source_name = ").Append(TomlString(file.Stage.SourceName)).AppendLine();
        }

        return builder.ToString();
    }

    private static string MapStage(HlslShaderStageKind stage) => stage switch
    {
        HlslShaderStageKind.Vertex => "vertex",
        HlslShaderStageKind.Fragment => "fragment",
        HlslShaderStageKind.Compute => "compute",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported shader stage."),
    };

    private static string SanitizeFileName(string value)
    {
        string effective = string.IsNullOrWhiteSpace(value) ? "stage" : value;
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            effective = effective.Replace(invalid, '_');
        }

        return effective;
    }

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string TomlString(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private sealed record StageFile(SpirvShaderStageArtifact Stage, string SpirvEncoding, string RelativePath, string WrittenSha256, string FullPath);
}
