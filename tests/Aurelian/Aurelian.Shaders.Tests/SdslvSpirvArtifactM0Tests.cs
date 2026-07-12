using Aurelian.Shaders.Language.Artifacts.SdslvSpirv;
using Aurelian.Shaders.Language.Artifacts.Spirv;
using Aurelian.Shaders.Language.External.Dxc;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SdslvSpirvArtifactM0Tests
{
    [Fact]
    public void SdslvSpirvShaderArtifactEmitter_RejectsInvalidSdslv()
    {
        var artifact = SdslvSpirvShaderArtifactEmitter.EmitFromSource("shader {", "invalid.sdslv");

        Assert.False(artifact.Success);
        Assert.Null(artifact.SpirvArtifact);
        Assert.Empty(artifact.Hlsl);
        Assert.Contains(artifact.Diagnostics, x => x.Code == SdslvSpirvShaderArtifactDiagnosticCodes.ParseFailed);
    }

    [Fact]
    public void SdslvSpirvShaderArtifactEmitter_EmitSmokeTriangle_WhenDxcUnavailable_ReturnsUnavailableDiagnostic()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (resolution.Success)
        {
            return;
        }

        var artifact = EmitSmokeTriangle();

        Assert.False(artifact.Success);
        Assert.NotNull(artifact.SpirvArtifact);
        Assert.Contains("VSMain", artifact.Hlsl);
        Assert.Contains("PSMain", artifact.Hlsl);
        Assert.Contains(artifact.Diagnostics, x => x.Code == SdslvSpirvShaderArtifactDiagnosticCodes.SpirvCompilationUnavailable);
    }

    [Fact]
    public void SdslvSpirvShaderArtifactEmitter_EmitSmokeTriangle_WhenDxcAvailable_ProducesVertexAndFragmentSpirv()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            Assert.NotEmpty(resolution.Diagnostics);
            return;
        }

        var artifact = EmitSmokeTriangle();

        Assert.True(artifact.Success, FormatDiagnostics(artifact));
        Assert.Empty(artifact.Diagnostics);
        Assert.Contains("VSMain", artifact.Hlsl);
        Assert.Contains("PSMain", artifact.Hlsl);
        Assert.NotNull(artifact.SpirvArtifact);
        Assert.True(artifact.SpirvArtifact.Success, FormatSpirvDiagnostics(artifact.SpirvArtifact));
        Assert.Collection(
            artifact.SpirvArtifact.Stages,
            vertex =>
            {
                Assert.Equal(HlslShaderStageKind.Vertex, vertex.Stage);
                Assert.Equal("VSMain", vertex.EntryPoint);
                AssertCompiledSpirv(vertex);
            },
            fragment =>
            {
                Assert.Equal(HlslShaderStageKind.Fragment, fragment.Stage);
                Assert.Equal("PSMain", fragment.EntryPoint);
                AssertCompiledSpirv(fragment);
            });
    }

    [Fact]
    public void SdslvSpirvShaderArtifactEmitter_EmitSmokeTriangle_WhenDxcAvailable_HashesAreStable()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            Assert.NotEmpty(resolution.Diagnostics);
            return;
        }

        var first = EmitSmokeTriangle();
        var second = EmitSmokeTriangle();

        Assert.True(first.Success, FormatDiagnostics(first));
        Assert.True(second.Success, FormatDiagnostics(second));
        Assert.Equal(first.SourceSha256, second.SourceSha256);
        Assert.Equal(first.SpirvArtifact!.Stages.Select(x => x.SourceSha256), second.SpirvArtifact!.Stages.Select(x => x.SourceSha256));
        Assert.Equal(first.SpirvArtifact.Stages.Select(x => x.SpirvSha256), second.SpirvArtifact.Stages.Select(x => x.SpirvSha256));
        Assert.Matches("^[0-9a-f]{64}$", first.SourceSha256);
        Assert.All(first.SpirvArtifact.Stages, stage =>
        {
            Assert.Matches("^[0-9a-f]{64}$", stage.SourceSha256);
            Assert.Matches("^[0-9a-f]{64}$", stage.SpirvSha256);
        });
    }

    [Fact]
    public void SdslvSpirvShaderArtifactJsonWriter_WritesDeterministicJson()
    {
        var artifact = EmitSmokeTriangle();

        var first = SdslvSpirvShaderArtifactJsonWriter.Write(artifact);
        var second = SdslvSpirvShaderArtifactJsonWriter.Write(artifact);

        Assert.Equal(first, second);
        Assert.Contains("\"formatVersion\"", first);
        Assert.Contains("\"language\"", first);
        Assert.Contains("\"sourceSha256\"", first);
        Assert.Contains("\"hlsl\"", first);
        Assert.Contains("\"spirvArtifact\"", first);
        Assert.Contains("\"diagnostics\"", first);
    }

    [Fact]
    public void SdslvSpirvShaderArtifactEmitter_DoesNotRequireGraphicsRuntime()
    {
        var references = typeof(SdslvSpirvShaderArtifactEmitter).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("Aurelian.Graphics", references);
        Assert.DoesNotContain("Silk.NET.Vulkan", references);
    }

    private static SdslvSpirvShaderArtifact EmitSmokeTriangle() =>
        SdslvSpirvShaderArtifactEmitter.EmitFromSource(
            ReadFixture("smoke_triangle.sdslv"),
            "Fixtures/Sdslv/smoke_triangle.sdslv");

    private static void AssertCompiledSpirv(SpirvShaderStageArtifact stage)
    {
        Assert.True(stage.SpirvBytes.Length > 4);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(stage.SpirvBytes, 0));
        Assert.Equal(64, stage.SourceSha256.Length);
        Assert.Equal(64, stage.SpirvSha256.Length);
    }

    private static string ReadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sdslv", name);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Sdslv", name));
        return File.ReadAllText(path);
    }

    private static string FormatDiagnostics(SdslvSpirvShaderArtifact artifact) =>
        string.Join(Environment.NewLine, artifact.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
            .Concat(artifact.SpirvArtifact?.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}") ?? []));

    private static string FormatSpirvDiagnostics(SpirvShaderArtifact artifact) =>
        string.Join(Environment.NewLine, artifact.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
