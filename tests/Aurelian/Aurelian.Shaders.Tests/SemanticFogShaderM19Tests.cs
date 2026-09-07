using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SemanticFogShaderM19Tests
{
    [Fact]
    public void VisualTsFogCompilesThroughVdMirToValidatedSpirv()
    {
        string root = RepositoryRoot();
        const string relativePath = "src/Aurelian/Aurelian.Shaders/Assets/SemanticFog.v.ts";
        string source = File.ReadAllText(Path.Combine(root, relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
            new GpuCompilationRequest([new GpuSourceFile(relativePath, source)]));

        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(static item => item.Message)));
        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput);
        Assert.Contains("visibilityField", backend.Hlsl, StringComparison.Ordinal);
        Assert.Contains("edgeSoftness", backend.Hlsl, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException();
    }
}
