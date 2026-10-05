using System.ComponentModel;
using System.Diagnostics;
using Aurelian.Rendering.Contracts.Shaders;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Graphics;

public sealed record WebGpuGraphicsProgram(
    CompiledGraphicsProgram Semantics,
    string VertexWgsl,
    string FragmentWgsl,
    double TranslationMilliseconds);

/// <summary>Browser artifact bridge. DXC remains the HLSL compiler; Naga translates its validated SPIR-V.</summary>
public static class WebGpuGraphicsBackend
{
    public static WebGpuGraphicsProgram Compile(VdMirGraphicsModule module, string? nagaExecutable = null)
        => Translate(module, VdMirGraphicsBackend.Compile(module, "vulkan1.1"), nagaExecutable);

    public static WebGpuGraphicsProgram Translate(
        VdMirGraphicsModule module,
        VdMirGraphicsBackendResult backend,
        string? nagaExecutable = null)
    {
        CompiledGraphicsProgram semantics = CompiledGraphicsProgramExporter.Export(module, backend);
        string executable = nagaExecutable ?? Environment.GetEnvironmentVariable("AURELIAN_NAGA") ?? "naga";
        string directory = Path.Combine(Path.GetTempPath(), "aurelian-webgpu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            string vertex = TranslateStage(executable, directory, "vertex", backend.Vertex.Spirv);
            string fragment = TranslateStage(executable, directory, "fragment", backend.Pixel.Spirv);
            return new WebGpuGraphicsProgram(semantics, vertex, fragment, stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A cleanup failure must not replace the original compiler diagnostic.
            }
        }
    }

    private static string TranslateStage(string executable, string directory, string stage, byte[] spirv)
    {
        string input = Path.Combine(directory, stage + ".spv");
        string output = Path.Combine(directory, stage + ".wgsl");
        File.WriteAllBytes(input, spirv);
        Run(executable, ["--keep-coordinate-space", input, output]);
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
        {
            throw new InvalidOperationException($"Naga did not produce {stage} WGSL.");
        }
        // Reparse the emitted WGSL, independently of the SPIR-V reader's validation.
        Run(executable, [output]);
        return File.ReadAllText(output);
    }

    private static void Run(string executable, IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("WebGPU translation requires Naga CLI. Set AURELIAN_NAGA to its executable path.", exception);
        }
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("Naga exceeded the 15 second translation limit.");
        }
        string diagnostic = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Naga failed ({process.ExitCode}): {diagnostic}");
        }
    }
}
