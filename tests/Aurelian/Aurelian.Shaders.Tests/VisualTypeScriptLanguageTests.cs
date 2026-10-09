using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VisualTypeScriptLanguageTests
{
    [Fact]
    public void Compute_imports_static_helpers_and_payload_matches_compile_to_validated_spirv()
    {
        var module = GpuComputeBinder.Compile(new(Sources("LanguagePortCompute.v.ts")));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        Assert.DoesNotContain(module.Functions, function => function.Name.EndsWith("_Square", StringComparison.Ordinal));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
        var repeated = VdMirComputeBackend.Compile(module);
        Assert.Equal(backend.HlslSha256, repeated.HlslSha256);
        Assert.Equal(backend.SpirvSha256, repeated.SpirvSha256);
    }

    [Fact]
    public void Graphics_imports_static_helpers_and_payload_matches_compile_to_validated_spirv()
    {
        var module = GpuGraphicsBinder.Compile(new(Sources("LanguagePortGraphics.v.ts")));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
    }

    private static IReadOnlyList<GpuSourceFile> Sources(string root)
    {
        return GpuSourceLoader.Load(root, name =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        });
    }
}
