using System.Diagnostics;
using Aurelian.Shaders.Language.Artifacts;

namespace Aurelian.Shaders.Language.External.Dxc;

public static class DxcValidator
{
    public static DxcValidationResult ValidateHlsl(
        DxcValidationRequest request,
        DxcExecutable? executable = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Hlsl))
        {
            return Skipped(DxcValidationStatus.SkippedNoHlsl, request.EntryPoint, request.Profile);
        }

        if (string.IsNullOrWhiteSpace(request.EntryPoint) || string.IsNullOrWhiteSpace(request.Profile))
        {
            return Skipped(DxcValidationStatus.SkippedNoEntryPoints, request.EntryPoint, request.Profile);
        }

        executable ??= DxcDiscovery.FindDxc();
        if (executable is null)
        {
            return Skipped(DxcValidationStatus.SkippedToolUnavailable, request.EntryPoint, request.Profile);
        }

        var (inputPath, outputPath) = CreateTempPaths(request.SourceName);
        IReadOnlyList<string> arguments = [];

        try
        {
            File.WriteAllText(inputPath, request.Hlsl);
            arguments = DxcCommandLineBuilder.Build(request, inputPath, outputPath);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable.Path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new DxcValidationResult(
                process.ExitCode == 0 ? DxcValidationStatus.Succeeded : DxcValidationStatus.Failed,
                request.EntryPoint,
                request.Profile,
                process.ExitCode,
                standardOutput,
                standardError,
                arguments);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new DxcValidationResult(
                DxcValidationStatus.Failed,
                request.EntryPoint,
                request.Profile,
                null,
                string.Empty,
                ex.Message,
                arguments);
        }
        finally
        {
            DeleteTempFile(inputPath);
            DeleteTempFile(outputPath);
        }
    }

    public static DxcArtifactValidationResult ValidateArtifact(
        SdslvShaderArtifact artifact,
        DxcExecutable? executable = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (string.IsNullOrWhiteSpace(artifact.Hlsl))
        {
            return new DxcArtifactValidationResult([
                Skipped(DxcValidationStatus.SkippedNoHlsl, string.Empty, string.Empty),
            ]);
        }

        var requests = artifact.Stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.EntryPoint) && !string.IsNullOrWhiteSpace(stage.Profile))
            .Select(stage => new DxcValidationRequest(
                artifact.Hlsl,
                stage.EntryPoint,
                stage.Profile!,
                artifact.SourceName))
            .ToArray();

        if (requests.Length == 0)
        {
            return new DxcArtifactValidationResult([
                Skipped(DxcValidationStatus.SkippedNoEntryPoints, string.Empty, string.Empty),
            ]);
        }

        executable ??= DxcDiscovery.FindDxc();
        if (executable is null)
        {
            return new DxcArtifactValidationResult(requests
                .Select(request => Skipped(DxcValidationStatus.SkippedToolUnavailable, request.EntryPoint, request.Profile))
                .ToArray());
        }

        return new DxcArtifactValidationResult(requests
            .Select(request => ValidateHlsl(request, executable))
            .ToArray());
    }

    private static DxcValidationResult Skipped(DxcValidationStatus status, string entryPoint, string profile) =>
        new(status, entryPoint, profile, null, string.Empty, string.Empty, []);

    private static (string InputPath, string OutputPath) CreateTempPaths(string sourceName)
    {
        var safeName = Path.GetFileNameWithoutExtension(sourceName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "shader";
        }

        var basePath = Path.Combine(Path.GetTempPath(), $"aurelian_dxc_{Environment.ProcessId}_{Guid.NewGuid():N}_{safeName}");
        return ($"{basePath}.hlsl", $"{basePath}.bin");
    }

    private static void DeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
