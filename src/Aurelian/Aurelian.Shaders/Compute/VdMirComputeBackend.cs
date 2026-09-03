using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Aurelian.Shaders.Language.External.Dxc;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Compute;

public sealed record VdMirComputeBackendResult(
    string Hlsl,
    string HlslSha256,
    byte[] Spirv,
    string? SpirvSha256,
    DxcSpirvStatus DxcStatus,
    string? DxcPath,
    IReadOnlyList<string> DxcArguments,
    bool SpirvValidated,
    string SpirvValidationOutput,
    string? SpirvDisassembly);

public static class VdMirComputeBackend
{
    public static VdMirComputeBackendResult Compile(VdMirComputeModule module)
    {
        string hlsl = VdMirComputeHlslEmitter.Emit(module);
        DxcExecutableResolution resolution = DxcExecutableResolver.Resolve();
        DxcSpirvCompileResult compilation = DxcSpirvCompiler.Compile(
            new DxcSpirvCompileRequest(
                hlsl,
                module.EntryPoint!.EmittedName,
                "cs_6_0",
                Path.GetFileNameWithoutExtension(module.EntryPoint.Source.File) + ".hlsl"),
            resolution);

        bool validated = false;
        string validationOutput = string.Empty;
        string? disassembly = null;
        if (compilation.Success)
        {
            (validated, validationOutput) = RunSpirvTool("spirv-val", compilation.SpirvBytes, ["--target-env", "vulkan1.3"]);
            (bool disassembled, string output) = RunSpirvTool("spirv-dis", compilation.SpirvBytes, []);
            if (disassembled) disassembly = output;
        }

        return new VdMirComputeBackendResult(
            hlsl,
            Hash(Encoding.UTF8.GetBytes(hlsl)),
            compilation.SpirvBytes,
            compilation.Success ? Hash(compilation.SpirvBytes) : null,
            compilation.Status,
            resolution.ExecutablePath,
            compilation.Arguments,
            validated,
            validationOutput,
            disassembly);
    }

    private static (bool Success, string Output) RunSpirvTool(
        string executable,
        byte[] spirv,
        IReadOnlyList<string> arguments)
    {
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"aurelian-vdmir-{Guid.NewGuid():N}.spv");
        try
        {
            File.WriteAllBytes(temporaryPath, spirv);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.ArgumentList.Add(temporaryPath);
            process.Start();
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            string output = string.Join(Environment.NewLine, new[] { standardOutput, standardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return (process.ExitCode == 0, output.Trim());
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, exception.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
