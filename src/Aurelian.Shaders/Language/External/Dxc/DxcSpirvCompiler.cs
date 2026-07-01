using System.ComponentModel;
using System.Diagnostics;

namespace Aurelian.Shaders.Language.External.Dxc;

public static class DxcSpirvCompiler
{
    public static DxcSpirvCompileResult Compile(
        DxcSpirvCompileRequest request,
        DxcExecutableResolution? resolution = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rejectionDiagnostics = ValidateRequest(request);
        if (rejectionDiagnostics.Count > 0)
        {
            return new DxcSpirvCompileResult(
                DxcSpirvStatus.Rejected,
                [],
                null,
                string.Empty,
                string.Empty,
                [],
                rejectionDiagnostics);
        }

        resolution ??= DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            return new DxcSpirvCompileResult(
                DxcSpirvStatus.Unavailable,
                [],
                null,
                string.Empty,
                string.Empty,
                [],
                resolution.Diagnostics);
        }

        var tempDirectory = CreateTempDirectory();
        var inputPath = Path.Combine(tempDirectory, SanitizeFileName(request.SourceName, "shader.hlsl"));
        var outputPath = Path.Combine(tempDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".spv");
        IReadOnlyList<string> arguments = [];

        try
        {
            File.WriteAllText(inputPath, request.SourceText);
            arguments = BuildArguments(request, inputPath, outputPath);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = resolution.ExecutablePath!,
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

            if (process.ExitCode != 0)
            {
                return new DxcSpirvCompileResult(
                    DxcSpirvStatus.Failed,
                    [],
                    process.ExitCode,
                    standardOutput,
                    standardError,
                    arguments,
                    []);
            }

            if (!File.Exists(outputPath))
            {
                return FailedOutputMissing(process.ExitCode, standardOutput, standardError, arguments, outputPath);
            }

            var bytes = File.ReadAllBytes(outputPath);
            if (bytes.Length == 0)
            {
                return FailedOutputMissing(process.ExitCode, standardOutput, standardError, arguments, outputPath);
            }

            return new DxcSpirvCompileResult(
                DxcSpirvStatus.Compiled,
                bytes,
                process.ExitCode,
                standardOutput,
                standardError,
                arguments,
                []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return new DxcSpirvCompileResult(
                DxcSpirvStatus.Failed,
                [],
                null,
                string.Empty,
                ex.Message,
                arguments,
                [new DxcToolDiagnostic(DxcToolDiagnosticCodes.PathProbeFailed, $"DXC subprocess failed: {ex.Message}", resolution.ExecutablePath)]);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    private static List<DxcToolDiagnostic> ValidateRequest(DxcSpirvCompileRequest request)
    {
        var diagnostics = new List<DxcToolDiagnostic>();
        if (string.IsNullOrWhiteSpace(request.SourceText))
        {
            diagnostics.Add(new DxcToolDiagnostic("ASD1101", "HLSL source text is required."));
        }

        if (string.IsNullOrWhiteSpace(request.EntryPoint))
        {
            diagnostics.Add(new DxcToolDiagnostic("ASD1102", "DXC entry point is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Profile))
        {
            diagnostics.Add(new DxcToolDiagnostic("ASD1103", "DXC shader profile is required."));
        }

        if (string.IsNullOrWhiteSpace(request.SourceName))
        {
            diagnostics.Add(new DxcToolDiagnostic("ASD1104", "DXC source name is required."));
        }

        return diagnostics;
    }

    private static IReadOnlyList<string> BuildArguments(DxcSpirvCompileRequest request, string inputPath, string outputPath)
    {
        var arguments = new List<string>
        {
            "-spirv",
            "-fspv-target-env=vulkan1.3",
            "-HV",
            "2021",
            "-E",
            request.EntryPoint,
            "-T",
            request.Profile,
            inputPath,
            "-Fo",
            outputPath,
        };

        if (request.AdditionalArguments is not null)
        {
            arguments.AddRange(request.AdditionalArguments.Where(argument => !string.IsNullOrWhiteSpace(argument)));
        }

        return arguments;
    }

    private static DxcSpirvCompileResult FailedOutputMissing(
        int exitCode,
        string standardOutput,
        string standardError,
        IReadOnlyList<string> arguments,
        string outputPath) => new(
            DxcSpirvStatus.Failed,
            [],
            exitCode,
            standardOutput,
            standardError,
            arguments,
            [new DxcToolDiagnostic("ASD1105", "DXC exited successfully but did not write non-empty SPIR-V output.", outputPath)]);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "aurelian-dxc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizeFileName(string sourceName, string fallback)
    {
        var fileName = Path.GetFileName(sourceName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fallback;
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".hlsl";
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary-file cleanup must not turn a completed subprocess result into a test failure.
        }
    }
}
