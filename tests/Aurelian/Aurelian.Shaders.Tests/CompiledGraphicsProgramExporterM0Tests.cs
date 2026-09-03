using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class CompiledGraphicsProgramExporterM0Tests
{
    [Fact]
    public void ForwardTextured_ExportsCompleteRendererNeutralMetadataAndSpirv()
    {
        string sourceName = "samples/Aurelian/ForwardTexturedM3.v.ts";
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "samples", "Aurelian", "ForwardTexturedM3.v.ts"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
        Assert.True(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));

        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
        CompiledGraphicsProgram program = CompiledGraphicsProgramExporter.Export(module, backend);

        Assert.Equal(CompiledGraphicsProgram.CurrentFormatVersion, program.FormatVersion);
        Assert.Equal(VdMirGraphicsModule.GraphicsM3FeatureLevel, program.FeatureLevel);
        Assert.Equal(2, program.Shaders.Stages.Count);
        Assert.All(program.Shaders.Stages, stage => Assert.NotEmpty(stage.SpirvBytes));
        Assert.Equal([0, 1], program.VertexInputs.Select(input => input.Location));
        Assert.Equal([0, 1, 2], program.Resources.Select(resource => resource.Binding));
        Assert.Equal(
            [CompiledGraphicsResourceKind.Texture2D, CompiledGraphicsResourceKind.Sampler, CompiledGraphicsResourceKind.UniformBuffer],
            program.Resources.Select(resource => resource.Kind));
        Assert.Equal(32, program.Material!.Size);
        Assert.Equal([0, 16], program.Material.Fields.Select(field => field.Offset));
    }

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
