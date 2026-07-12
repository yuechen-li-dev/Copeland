using Aurelian.Shaders.Language.Artifacts.Spirv;
using Aurelian.Shaders.Language.External.Dxc;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SpirvShaderArtifactM0Tests
{
    [Fact]
    public void SpirvShaderArtifactEmitter_RejectsEmptyStageList()
    {
        var artifact = SpirvShaderArtifactEmitter.EmitFromHlslStages([]);

        Assert.False(artifact.Success);
        Assert.Empty(artifact.Stages);
        Assert.Contains(artifact.Diagnostics, x => x.Code == SpirvShaderArtifactDiagnosticCodes.EmptyHlslSource);
    }

    [Fact]
    public void SpirvShaderArtifactEmitter_RejectsDuplicateStages()
    {
        var artifact = SpirvShaderArtifactEmitter.EmitFromHlslStages([
            VertexStage(),
            VertexStage("VSMain2"),
        ]);

        Assert.False(artifact.Success);
        Assert.Empty(artifact.Stages);
        Assert.Contains(artifact.Diagnostics, x => x.Code == SpirvShaderArtifactDiagnosticCodes.DuplicateStage);
    }

    [Fact]
    public void SpirvShaderArtifactEmitter_RejectsStageProfileMismatch()
    {
        var artifact = SpirvShaderArtifactEmitter.EmitFromHlslStages([
            VertexStage(profile: "ps_6_0"),
        ]);

        Assert.False(artifact.Success);
        Assert.Empty(artifact.Stages);
        Assert.Contains(artifact.Diagnostics, x => x.Code == SpirvShaderArtifactDiagnosticCodes.StageProfileMismatch);
    }

    [Fact]
    public void SpirvShaderArtifactEmitter_EmitTinyHlslStages_WhenDxcUnavailable_ReturnsUnavailableDiagnostic()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (resolution.Success)
        {
            return;
        }

        var artifact = EmitTinyTriangle();

        Assert.False(artifact.Success);
        Assert.Empty(artifact.Stages);
        Assert.Contains(artifact.Diagnostics, x => x.Code == SpirvShaderArtifactDiagnosticCodes.DxcUnavailable);
    }

    [Fact]
    public void SpirvShaderArtifactEmitter_EmitTinyHlslStages_WhenDxcAvailable_ProducesVertexAndFragmentArtifacts()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            Assert.NotEmpty(resolution.Diagnostics);
            return;
        }

        var artifact = EmitTinyTriangle();

        Assert.True(artifact.Success, FormatDiagnostics(artifact.Diagnostics));
        Assert.Empty(artifact.Diagnostics);
        Assert.Collection(
            artifact.Stages,
            vertex =>
            {
                Assert.Equal(HlslShaderStageKind.Vertex, vertex.Stage);
                Assert.Equal("VSMain", vertex.EntryPoint);
                Assert.Equal("vs_6_0", vertex.Profile);
                AssertCompiledSpirv(vertex);
            },
            fragment =>
            {
                Assert.Equal(HlslShaderStageKind.Fragment, fragment.Stage);
                Assert.Equal("PSMain", fragment.EntryPoint);
                Assert.Equal("ps_6_0", fragment.Profile);
                AssertCompiledSpirv(fragment);
            });
    }

    [Fact]
    public void SpirvShaderArtifactEmitter_EmitTinyHlslStages_WhenDxcAvailable_HashesAreStable()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            Assert.NotEmpty(resolution.Diagnostics);
            return;
        }

        var first = EmitTinyTriangle();
        var second = EmitTinyTriangle();

        Assert.True(first.Success, FormatDiagnostics(first.Diagnostics));
        Assert.True(second.Success, FormatDiagnostics(second.Diagnostics));
        Assert.Equal(first.Stages.Select(x => x.SourceSha256), second.Stages.Select(x => x.SourceSha256));
        Assert.Equal(first.Stages.Select(x => x.SpirvSha256), second.Stages.Select(x => x.SpirvSha256));
        Assert.All(first.Stages, stage =>
        {
            Assert.Equal(64, stage.SourceSha256.Length);
            Assert.Equal(64, stage.SpirvSha256.Length);
            Assert.Matches("^[0-9a-f]{64}$", stage.SourceSha256);
            Assert.Matches("^[0-9a-f]{64}$", stage.SpirvSha256);
        });
    }

    [Fact]
    public void SpirvShaderArtifactJsonWriter_WritesDeterministicJson()
    {
        var artifact = new SpirvShaderArtifact(
            SpirvShaderArtifact.CurrentFormatVersion,
            SpirvShaderArtifact.LanguageName,
            [new SpirvShaderStageArtifact(HlslShaderStageKind.Vertex, "VSMain", "vs_6_0", "tiny_triangle_vs.hlsl", new string('a', 64), new string('b', 64), [0x03, 0x02, 0x23, 0x07], ["-spirv", "-E", "VSMain"])],
            []);

        var first = SpirvShaderArtifactJsonWriter.Write(artifact);
        var second = SpirvShaderArtifactJsonWriter.Write(artifact);

        Assert.Equal(first, second);
        Assert.Contains("\"formatVersion\"", first);
        Assert.Contains("\"language\"", first);
        Assert.Contains("\"success\": true", first);
        Assert.Contains("\"stages\"", first);
        Assert.Contains("\"diagnostics\"", first);
    }

    [Fact]
    public void SpirvShaderArtifactJsonWriter_IncludesBase64SpirvWhenAvailable()
    {
        var artifact = new SpirvShaderArtifact(
            SpirvShaderArtifact.CurrentFormatVersion,
            SpirvShaderArtifact.LanguageName,
            [new SpirvShaderStageArtifact(HlslShaderStageKind.Fragment, "PSMain", "ps_6_0", "tiny_triangle_ps.hlsl", new string('a', 64), new string('b', 64), [1, 2, 3], ["-spirv"])],
            []);

        var json = SpirvShaderArtifactJsonWriter.Write(artifact);

        Assert.Contains("\"spirvBase64\": \"AQID\"", json);
        Assert.Contains("\"spirvSha256\"", json);
    }

    [Fact]
    public void SpirvShaderArtifactEmitter_DoesNotRequireGraphicsRuntime()
    {
        var references = typeof(SpirvShaderArtifactEmitter).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("Aurelian.Graphics", references);
        Assert.DoesNotContain("Silk.NET.Vulkan", references);
    }

    private static SpirvShaderArtifact EmitTinyTriangle() => SpirvShaderArtifactEmitter.EmitFromHlslStages([
        VertexStage(),
        FragmentStage(),
    ]);

    private static HlslShaderStageSource VertexStage(string entryPoint = "VSMain", string profile = "vs_6_0") => new(
        HlslShaderStageKind.Vertex,
        ReadHlslFixture("tiny_triangle_vs.hlsl"),
        entryPoint,
        profile,
        "tiny_triangle_vs.hlsl");

    private static HlslShaderStageSource FragmentStage() => new(
        HlslShaderStageKind.Fragment,
        ReadHlslFixture("tiny_triangle_ps.hlsl"),
        "PSMain",
        "ps_6_0",
        "tiny_triangle_ps.hlsl");

    private static void AssertCompiledSpirv(SpirvShaderStageArtifact stage)
    {
        Assert.True(stage.SpirvBytes.Length > 4);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(stage.SpirvBytes, 0));
        Assert.Equal(64, stage.SourceSha256.Length);
        Assert.Equal(64, stage.SpirvSha256.Length);
        Assert.Contains("-spirv", stage.DxcArguments);
        Assert.Contains("-fspv-target-env=vulkan1.3", stage.DxcArguments);
    }

    private static string ReadHlslFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Hlsl", name);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Hlsl", name));
        return File.ReadAllText(path);
    }

    private static string FormatDiagnostics(IEnumerable<SpirvShaderArtifactDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
