using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Cloth3D.Vulkan;

public static class ClothCompute3D
{
    public static byte[] Compile(string source)
    {
        var program = GpuComputeBinder.Compile(new([new GpuSourceFile("Cloth3D.v.ts", source)]));
        if (!program.Success)
        {
            throw new InvalidDataException(string.Join("; ", program.Diagnostics.Select(item => item.Message)));
        }
        var backend = VdMirComputeBackend.Compile(program);
        if (!backend.SpirvValidated || backend.Spirv.Length == 0)
        {
            throw new InvalidDataException(backend.DxcOutput + backend.SpirvValidationOutput);
        }
        return backend.Spirv!;
    }
}
