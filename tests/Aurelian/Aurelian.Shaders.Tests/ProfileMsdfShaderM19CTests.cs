using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ProfileMsdfShaderM19CTests
{
    [Fact]
    public void Profile_shader_lowers_screen_derivative_coverage_to_valid_spirv()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string sourceName = "src/Aurelian/Aurelian.Shaders/Assets/ProfileMsdf.v.ts";
        string source = File.ReadAllText(Path.Combine(root, sourceName.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
            new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));

        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput);
        Assert.Contains("fwidth(distance)", backend.Hlsl, StringComparison.Ordinal);
        Assert.Contains("ScreenSpaceCoverage", backend.Hlsl, StringComparison.Ordinal);
    }
}
