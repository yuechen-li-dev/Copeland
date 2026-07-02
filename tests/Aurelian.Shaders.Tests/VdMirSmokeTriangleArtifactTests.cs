using System.Text.Json;
using Aurelian.Shaders.Language.Artifacts.Spirv;
using Aurelian.Shaders.Language.External.Dxc;
using Aurelian.Shaders.Language.VdMir.Artifacts;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VdMirSmokeTriangleArtifactTests
{
    [Fact]
    public void VdMirSmokeTriangle_CanCompileToSpirvThroughExistingDxcPath()
    {
        var proof = CompileSmokeTriangle();
        var resolution = DxcExecutableResolver.Resolve();

        if (!resolution.Success)
        {
            Assert.Equal("dxc-unavailable", proof.VdMirDxcSpirvProofStatus);
            return;
        }

        Assert.Equal("compiled", proof.VdMirDxcSpirvProofStatus);
        Assert.NotNull(proof.SpirvArtifact);
        Assert.True(proof.SpirvArtifact!.Success, FormatSpirvDiagnostics(proof.SpirvArtifact));
        Assert.Collection(
            proof.SpirvArtifact.Stages.OrderBy(stage => stage.Stage).ThenBy(stage => stage.EntryPoint, StringComparer.Ordinal),
            vertex => Assert.Equal(HlslShaderStageKind.Vertex, vertex.Stage),
            pixel => Assert.Equal(HlslShaderStageKind.Fragment, pixel.Stage));
    }

    [Fact]
    public void M14aManifest_RecordsVdMirM0Implementation()
    {
        using var temp = TempDirectory.Create();

        var result = VdMirSmokeTriangleArtifact.WriteArtifacts(CompileSmokeTriangle(), temp.Path);
        var manifest = File.ReadAllText(result.ManifestJsonPath);

        Assert.Contains("\"milestone\": \"M14a\"", manifest);
        Assert.Contains("\"vdMirImplemented\": true", manifest);
        Assert.Contains("\"vdMirScope\": \"M0 smoke triangle\"", manifest);
        Assert.Contains("\"implementationLocation\": \"src/Aurelian.Shaders/Language/VdMir\"", manifest);
    }

    [Fact]
    public void M14aManifest_RecordsNoVisibleTriangleWiring()
    {
        using var temp = TempDirectory.Create();

        var result = VdMirSmokeTriangleArtifact.WriteArtifacts(CompileSmokeTriangle(), temp.Path);
        var manifest = File.ReadAllText(result.ManifestJsonPath);

        Assert.Contains("\"visibleTriangleWiredToVdMir\": false", manifest);
        Assert.Contains("\"directHlslPathPreserved\": true", manifest);
        Assert.Contains("\"hlslBackendChangedDefaultBehavior\": false", manifest);
    }

    [Fact]
    public void M14aProofArtifacts_AreWrittenDeterministically()
    {
        using var first = TempDirectory.Create();
        using var second = TempDirectory.Create();

        VdMirSmokeTriangleArtifact.WriteArtifacts(CompileSmokeTriangle(), first.Path);
        VdMirSmokeTriangleArtifact.WriteArtifacts(CompileSmokeTriangle(), second.Path);

        var firstFiles = Directory.GetFiles(first.Path).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var secondFiles = Directory.GetFiles(second.Path).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(firstFiles, secondFiles);
        foreach (var file in firstFiles)
        {
            var firstText = File.ReadAllText(Path.Combine(first.Path, file!));
            var secondText = File.ReadAllText(Path.Combine(second.Path, file!));
            Assert.Equal(firstText, secondText);
        }
    }

    private static VdMirSmokeTriangleProof CompileSmokeTriangle() =>
        VdMirSmokeTriangleArtifact.CompileFromSource(
            ReadFixture("smoke_triangle.sdslv"),
            "Fixtures/Sdslv/smoke_triangle.sdslv");

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

    private static string FormatSpirvDiagnostics(SpirvShaderArtifact artifact) =>
        string.Join(Environment.NewLine, artifact.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
