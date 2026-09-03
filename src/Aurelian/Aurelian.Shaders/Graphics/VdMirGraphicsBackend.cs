using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Aurelian.Shaders.Language.External.Dxc;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Graphics;

public sealed record VdMirGraphicsStageResult(
    VdMirGraphicsStage Stage,
    string EntryPoint,
    string Profile,
    byte[] Spirv,
    string? SpirvSha256,
    DxcSpirvStatus DxcStatus,
    string DxcOutput,
    IReadOnlyList<string> DxcArguments,
    bool SpirvValidated,
    string SpirvValidationOutput,
    string? SpirvDisassembly,
    double DxcMilliseconds,
    double SpirvValidationMilliseconds,
    double SpirvDisassemblyMilliseconds);

public sealed record VdMirGraphicsBackendResult(
    string Hlsl,
    string HlslSha256,
    string? DxcPath,
    VdMirGraphicsStageResult Vertex,
    VdMirGraphicsStageResult Pixel);

public static class VdMirGraphicsBackend
{
    public static VdMirGraphicsBackendResult Compile(VdMirGraphicsModule module)
    {
        string hlsl = VdMirGraphicsHlslEmitter.Emit(module);
        string hlslHash = Hash(Encoding.UTF8.GetBytes(hlsl));
        DxcExecutableResolution resolution = DxcExecutableResolver.Resolve();
        VdMirGraphicsEntryPoint vertex = module.EntryPoints.Single(entry => entry.Stage == VdMirGraphicsStage.Vertex);
        VdMirGraphicsEntryPoint pixel = module.EntryPoints.Single(entry => entry.Stage == VdMirGraphicsStage.Pixel);
        VdMirGraphicsStageResult vertexResult = CompileStage(hlsl, vertex, "vs_6_0", resolution);
        VdMirGraphicsStageResult pixelResult = CompileStage(hlsl, pixel, "ps_6_0", resolution);
        return new VdMirGraphicsBackendResult(hlsl, hlslHash, resolution.ExecutablePath, vertexResult, pixelResult);
    }

    private static VdMirGraphicsStageResult CompileStage(string hlsl, VdMirGraphicsEntryPoint entry, string profile, DxcExecutableResolution resolution)
    {
        Stopwatch dxcStopwatch = Stopwatch.StartNew();
        DxcSpirvCompileResult compilation = DxcSpirvCompiler.Compile(
            new DxcSpirvCompileRequest(hlsl, entry.EmittedName, profile, $"{entry.EmittedName}.hlsl"),
            resolution);
        dxcStopwatch.Stop();
        bool validated = false;
        string validationOutput = string.Empty;
        string? disassembly = null;
        double validationMilliseconds = 0;
        double disassemblyMilliseconds = 0;
        if (compilation.Success)
        {
            Stopwatch validationStopwatch = Stopwatch.StartNew();
            (validated, validationOutput) = RunSpirvTool("spirv-val", compilation.SpirvBytes, ["--target-env", "vulkan1.3"]);
            validationStopwatch.Stop();
            validationMilliseconds = validationStopwatch.Elapsed.TotalMilliseconds;
            Stopwatch disassemblyStopwatch = Stopwatch.StartNew();
            (bool disassembled, string output) = RunSpirvTool("spirv-dis", compilation.SpirvBytes, []);
            disassemblyStopwatch.Stop();
            disassemblyMilliseconds = disassemblyStopwatch.Elapsed.TotalMilliseconds;
            if (disassembled)
            {
                disassembly = output;
            }
        }
        string dxcOutput = string.Join(Environment.NewLine, new[] { compilation.StandardOutput, compilation.StandardError }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        return new VdMirGraphicsStageResult(
            entry.Stage,
            entry.EmittedName,
            profile,
            compilation.SpirvBytes,
            compilation.Success ? Hash(compilation.SpirvBytes) : null,
            compilation.Status,
            dxcOutput,
            compilation.Arguments,
            validated,
            validationOutput,
            disassembly,
            Math.Round(dxcStopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Round(validationMilliseconds, 3),
            Math.Round(disassemblyMilliseconds, 3));
    }

    private static (bool Success, string Output) RunSpirvTool(string executable, byte[] spirv, IReadOnlyList<string> arguments)
    {
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"aurelian-vdmir-graphics-{Guid.NewGuid():N}.spv");
        try
        {
            File.WriteAllBytes(temporaryPath, spirv);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ResolveSpirvExecutable(executable),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            process.StartInfo.ArgumentList.Add(temporaryPath);
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode == 0, string.Join(Environment.NewLine, new[] { output, error }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim());
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, exception.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ResolveSpirvExecutable(string executable)
    {
        string? sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        string candidate = Path.Combine(sdk ?? string.Empty, "Bin", executable + ".exe");
        return sdk is not null && File.Exists(candidate) ? candidate : executable;
    }
}
