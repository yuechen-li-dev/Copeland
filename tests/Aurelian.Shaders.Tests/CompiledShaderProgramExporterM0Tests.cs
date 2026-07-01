using System.Reflection;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Language.Artifacts.Compiled;
using Aurelian.Shaders.Language.Artifacts.SdslvSpirv;
using Aurelian.Shaders.Language.Artifacts.Spirv;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class CompiledShaderProgramExporterM0Tests
{
    [Fact]
    public void CompiledShaderProgramExporter_FromSpirvArtifact_RejectsFailedArtifact()
    {
        var artifact = new SpirvShaderArtifact(
            SpirvShaderArtifact.CurrentFormatVersion,
            SpirvShaderArtifact.LanguageName,
            [],
            [new SpirvShaderArtifactDiagnostic(SpirvShaderArtifactDiagnosticCodes.DxcUnavailable, SpirvShaderArtifactDiagnosticSeverity.Error, "DXC unavailable.")]);

        CompiledShaderProgramExportResult result = CompiledShaderProgramExporter.FromSpirvArtifact(artifact);

        Assert.False(result.Success);
        Assert.Equal(CompiledShaderStatus.Failed, result.Status);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, x => x.Code == CompiledShaderProgramExportDiagnosticCodes.ArtifactFailed);
    }

    [Fact]
    public void CompiledShaderProgramExporter_FromSpirvArtifact_ExportsVertexAndFragmentStages()
    {
        SpirvShaderArtifact artifact = SuccessfulSpirvArtifact();

        CompiledShaderProgramExportResult result = CompiledShaderProgramExporter.FromSpirvArtifact(artifact);

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Program);
        Assert.Equal(CompiledShaderProgram.CurrentFormatVersion, result.Program.FormatVersion);
        Assert.Collection(
            result.Program.Stages,
            vertex =>
            {
                Assert.Equal(CompiledShaderStageKind.Vertex, vertex.Stage);
                Assert.Equal("VSMain", vertex.EntryPoint);
                Assert.Equal("vs_6_0", vertex.Profile);
                Assert.Equal(new string('a', 64), vertex.SpirvSha256);
            },
            fragment =>
            {
                Assert.Equal(CompiledShaderStageKind.Fragment, fragment.Stage);
                Assert.Equal("PSMain", fragment.EntryPoint);
                Assert.Equal("ps_6_0", fragment.Profile);
                Assert.Equal(new string('b', 64), fragment.SpirvSha256);
            });
    }

    [Fact]
    public void CompiledShaderProgramExporter_FromSdslvSpirvArtifact_WhenDxcUnavailable_ReportsFailureOrUnavailable()
    {
        var artifact = new SdslvSpirvShaderArtifact(
            SdslvSpirvShaderArtifact.CurrentFormatVersion,
            SdslvSpirvShaderArtifact.LanguageName,
            "shader.sdslv",
            new string('c', 64),
            "",
            null,
            [new SdslvSpirvShaderArtifactDiagnostic(SdslvSpirvShaderArtifactDiagnosticCodes.SpirvCompilationUnavailable, SdslvSpirvShaderArtifactDiagnosticSeverity.Error, "DXC unavailable.")]);

        CompiledShaderProgramExportResult result = CompiledShaderProgramExporter.FromSdslvSpirvArtifact(artifact);

        Assert.False(result.Success);
        Assert.Equal(CompiledShaderStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == CompiledShaderProgramExportDiagnosticCodes.MissingArtifact);
    }

    [Fact]
    public void CompiledShaderProgramExporter_FromSdslvSpirvArtifact_WhenDxcAvailable_ExportsCompiledProgram()
    {
        var artifact = new SdslvSpirvShaderArtifact(
            SdslvSpirvShaderArtifact.CurrentFormatVersion,
            SdslvSpirvShaderArtifact.LanguageName,
            "shader.sdslv",
            new string('c', 64),
            "generated hlsl",
            SuccessfulSpirvArtifact(),
            []);

        CompiledShaderProgramExportResult result = CompiledShaderProgramExporter.FromSdslvSpirvArtifact(artifact);

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Program);
        Assert.Equal([CompiledShaderStageKind.Vertex, CompiledShaderStageKind.Fragment], result.Program.Stages.Select(x => x.Stage));
    }

    [Fact]
    public void CompiledShaderProgramExporter_DoesNotRequireGraphicsRuntime()
    {
        Assembly assembly = typeof(CompiledShaderProgramExporter).Assembly;

        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), x => x.Name == "Aurelian.Graphics");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), x => x.Name is "Silk.NET.Vulkan" or "Silk.NET.Windowing");
    }

    private static SpirvShaderArtifact SuccessfulSpirvArtifact()
        => new(
            SpirvShaderArtifact.CurrentFormatVersion,
            SpirvShaderArtifact.LanguageName,
            [
                new SpirvShaderStageArtifact(
                    HlslShaderStageKind.Vertex,
                    "VSMain",
                    "vs_6_0",
                    "shader.vert.hlsl",
                    new string('1', 64),
                    new string('a', 64),
                    [0x03, 0x02, 0x23, 0x07, 0x00, 0x00, 0x01, 0x00],
                    ["dxc", "-T", "vs_6_0"]),
                new SpirvShaderStageArtifact(
                    HlslShaderStageKind.Fragment,
                    "PSMain",
                    "ps_6_0",
                    "shader.frag.hlsl",
                    new string('2', 64),
                    new string('b', 64),
                    [0x03, 0x02, 0x23, 0x07, 0x00, 0x00, 0x01, 0x00],
                    ["dxc", "-T", "ps_6_0"]),
            ],
            []);

    private static string FormatDiagnostics(IReadOnlyList<CompiledShaderProgramExportDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Code}: {x.Message}"));
}
