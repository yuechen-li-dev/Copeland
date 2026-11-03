using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class HzbShaderTests
{
    [Theory]
    [InlineData("HzbCull3D.v.ts")]
    [InlineData("HzbReduce3D.v.ts")]
    public void HzbKernelsUseTheOwnedStorageBackendAndValidate(string name)
    {
        var module = GpuComputeBinder.Compile(new(BuiltinShaderLibrary.LoadSources(name)));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
        if (name == "HzbCull3D.v.ts")
            Assert.Equal(80, module.StorageLayouts!.Single(layout => layout.Type.EndsWith("HzbView", StringComparison.Ordinal)).Stride);
        else
            Assert.Equal(24, module.StorageLayouts!.Single(layout => layout.Type.EndsWith("HzbReduction", StringComparison.Ordinal)).Stride);
    }
}
