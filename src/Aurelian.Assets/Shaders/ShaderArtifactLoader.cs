using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Aurelian.Rendering.Contracts.Shaders;
using Tomlyn;
using Tomlyn.Model;

namespace Aurelian.Assets.Shaders;

public static class ShaderArtifactLoader
{
    private static readonly Regex Sha256Regex = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ShaderArtifactLoadResult LoadCompiledShaderProgram(string manifestPath)
    {
        var diagnostics = new List<ShaderArtifactDiagnostic>();
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.ManifestMissing, "Shader artifact manifest was not found.", manifestPath));
            return new ShaderArtifactLoadResult(ShaderArtifactLoadStatus.Rejected, null, diagnostics);
        }

        TomlTable table;
        try
        {
            string toml = File.ReadAllText(manifestPath);
            if (!Toml.TryToModel<TomlTable>(toml, out TomlTable? parsed, out var parseDiagnostics) || parsed is null)
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.ManifestParseFailed, parseDiagnostics?.ToString() ?? "TOML parse failed.", manifestPath));
                return new ShaderArtifactLoadResult(ShaderArtifactLoadStatus.Rejected, null, diagnostics);
            }

            table = parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.ManifestParseFailed, ex.Message, manifestPath));
            return new ShaderArtifactLoadResult(ShaderArtifactLoadStatus.Failed, null, diagnostics);
        }

        ShaderArtifactManifest manifest = ParseManifest(table);
        if (!string.Equals(manifest.Format, ShaderArtifactManifest.CurrentFormatVersion, StringComparison.Ordinal))
        {
            diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.UnsupportedFormat, $"Unsupported shader artifact format '{manifest.Format}'.", manifestPath));
        }

        if (manifest.Stages.Count == 0)
        {
            diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.StageMissing, "Shader artifact manifest must contain at least one stage.", manifestPath));
        }

        foreach (IGrouping<string, ShaderArtifactStageManifest> duplicate in manifest.Stages.GroupBy(x => x.Stage, StringComparer.Ordinal).Where(x => x.Count() > 1))
        {
            diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.DuplicateStage, $"Duplicate shader artifact stage '{duplicate.Key}'.", manifestPath, duplicate.Key));
        }

        string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
        var compiledStages = new List<CompiledShaderStage>();
        foreach (ShaderArtifactStageManifest stage in manifest.Stages)
        {
            if (!TryMapStage(stage.Stage, out CompiledShaderStageKind stageKind))
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.UnsupportedStage, $"Unsupported shader artifact stage '{stage.Stage}'.", manifestPath, stage.Stage));
                continue;
            }

            if (!IsValidHash(stage.SpirvSha256))
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.InvalidHash, $"Stage '{stage.Stage}' SPIR-V SHA-256 must be 64 lowercase hexadecimal characters.", manifestPath, stage.Stage));
                continue;
            }

            if (!ShaderArtifactSpirvEncoding.IsSupported(stage.SpirvEncoding))
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.UnsupportedSpirvEncoding, $"Stage '{stage.Stage}' SPIR-V encoding '{stage.SpirvEncoding}' is not supported.", manifestPath, stage.Stage));
                continue;
            }

            if (Path.IsPathRooted(stage.SpirvPath))
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.SpirvFileMissing, $"Stage '{stage.Stage}' SPIR-V path must be relative to the manifest directory.", stage.SpirvPath, stage.Stage));
                continue;
            }

            string spirvPath = ResolveRelativePath(manifestDirectory, stage.SpirvPath);
            if (!File.Exists(spirvPath))
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.SpirvFileMissing, $"Stage '{stage.Stage}' SPIR-V file was not found.", spirvPath, stage.Stage));
                continue;
            }

            byte[] bytes;
            try
            {
                if (string.Equals(stage.SpirvEncoding, ShaderArtifactSpirvEncoding.Hex, StringComparison.Ordinal))
                {
                    string hexText = File.ReadAllText(spirvPath);
                    if (!ShaderArtifactSpirvEncoding.TryDecodeHex(hexText, out bytes, out string error))
                    {
                        diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.HexSpirvParseFailed, $"Stage '{stage.Stage}' hex SPIR-V parse failed: {error}", spirvPath, stage.Stage));
                        continue;
                    }
                }
                else
                {
                    bytes = File.ReadAllBytes(spirvPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.SpirvFileMissing, ex.Message, spirvPath, stage.Stage));
                continue;
            }

            if (bytes.Length == 0)
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.EmptySpirv, $"Stage '{stage.Stage}' SPIR-V file is empty.", spirvPath, stage.Stage));
                continue;
            }

            string actualHash = ComputeSha256(bytes);
            if (!string.Equals(actualHash, stage.SpirvSha256, StringComparison.Ordinal))
            {
                diagnostics.Add(new ShaderArtifactDiagnostic(ShaderArtifactDiagnosticCodes.SpirvHashMismatch, $"Stage '{stage.Stage}' SPIR-V hash '{actualHash}' does not match manifest hash '{stage.SpirvSha256}'.", spirvPath, stage.Stage));
                continue;
            }

            compiledStages.Add(new CompiledShaderStage(stageKind, stage.EntryPoint, stage.Profile, bytes, actualHash, stage.SourceName));
        }

        if (diagnostics.Count > 0)
        {
            return new ShaderArtifactLoadResult(ShaderArtifactLoadStatus.Rejected, null, diagnostics);
        }

        return new ShaderArtifactLoadResult(
            ShaderArtifactLoadStatus.Loaded,
            new CompiledShaderProgram(CompiledShaderProgram.CurrentFormatVersion, compiledStages),
            diagnostics);
    }

    private static ShaderArtifactManifest ParseManifest(TomlTable table)
    {
        var stages = new List<ShaderArtifactStageManifest>();
        if (table.TryGetValue("stages", out object? stagesValue) && stagesValue is TomlTableArray stageTables)
        {
            foreach (TomlTable stageTable in stageTables.OfType<TomlTable>())
            {
                stages.Add(new ShaderArtifactStageManifest(
                    ReadString(stageTable, "stage"),
                    ReadString(stageTable, "entry_point"),
                    ReadString(stageTable, "profile"),
                    ReadNullableString(stageTable, "spirv_encoding") ?? ShaderArtifactSpirvEncoding.Binary,
                    ReadString(stageTable, "spirv_path"),
                    ReadString(stageTable, "spirv_sha256"),
                    ReadString(stageTable, "source_name")));
            }
        }

        return new ShaderArtifactManifest(
            ReadString(table, "format"),
            ReadString(table, "source_language"),
            ReadString(table, "source_name"),
            ReadString(table, "source_sha256"),
            ReadNullableString(table, "generated_hlsl_path"),
            ReadNullableString(table, "generated_hlsl_sha256"),
            stages);
    }

    private static string ReadString(TomlTable table, string key) =>
        table.TryGetValue(key, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static string? ReadNullableString(TomlTable table, string key) =>
        table.TryGetValue(key, out object? value) ? value?.ToString() : null;

    private static bool TryMapStage(string stage, out CompiledShaderStageKind kind)
    {
        switch (stage)
        {
            case "vertex":
                kind = CompiledShaderStageKind.Vertex;
                return true;
            case "fragment":
                kind = CompiledShaderStageKind.Fragment;
                return true;
            case "compute":
                kind = CompiledShaderStageKind.Compute;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string ResolveRelativePath(string manifestDirectory, string relativePath) =>
        Path.GetFullPath(Path.Combine(manifestDirectory, relativePath));

    private static bool IsValidHash(string hash) => Sha256Regex.IsMatch(hash);

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
