using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;

namespace Aurelian.Shaders.Library;

/// <summary>Compile/cache selected engine compute modules without game assets
/// or repository paths. Compiler diagnostics and SPIR-V validation remain owned
/// by the existing Visual TypeScript backend.</summary>
public sealed class BuiltinComputeShaderCache
{
    private readonly Dictionary<string, byte[]> programs = new(StringComparer.Ordinal);

    public byte[] Get(string name)
    {
        BuiltinShaderLibrary.RequireKind(name, ShaderSourceKind.Compute);
        if (programs.TryGetValue(name, out var cached))
        {
            return cached;
        }
        var module = GpuComputeBinder.Compile(new(BuiltinShaderLibrary.LoadSources(name)));
        if (!module.Success)
        {
            throw new InvalidDataException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        }
        var compiled = VdMirComputeBackend.Compile(module);
        if (!compiled.SpirvValidated || compiled.Spirv.Length == 0)
        {
            throw new InvalidDataException(compiled.DxcOutput + compiled.SpirvValidationOutput);
        }
        programs.Add(name, compiled.Spirv);
        return compiled.Spirv;
    }
}
