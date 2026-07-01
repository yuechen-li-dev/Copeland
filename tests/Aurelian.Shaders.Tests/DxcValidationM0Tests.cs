using Aurelian.Shaders.Language.Artifacts;
using Aurelian.Shaders.Language.External.Dxc;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class DxcValidationM0Tests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void DxcDiscovery_FindDxc_DoesNotThrow()
    {
        var exception = Record.Exception(() => DxcDiscovery.FindDxc());

        Assert.Null(exception);
    }

    [Fact]
    public void DxcValidator_ValidateHlsl_WithNullExecutable_ReturnsAvailabilitySafeStatus()
    {
        var request = new DxcValidationRequest(
            "float4 VSMain(float4 position : POSITION) : SV_Position { return position; }",
            "VSMain",
            "vs_6_0",
            "inline.hlsl");

        var result = WithoutPathOrEnvironmentDxc(() => DxcValidator.ValidateHlsl(request, executable: null));

        Assert.True(
            result.Status is DxcValidationStatus.SkippedToolUnavailable or DxcValidationStatus.Succeeded or DxcValidationStatus.Failed,
            $"Unexpected DXC validation status: {result.Status}");

        if (result.Status == DxcValidationStatus.SkippedToolUnavailable)
        {
            Assert.False(result.Success);
            Assert.Null(result.ExitCode);
            Assert.Empty(result.Arguments);
        }
        else
        {
            Assert.NotEmpty(result.Arguments);
        }
    }

    [Fact]
    public void DxcValidator_ValidateArtifact_WithEmptyHlsl_SkipsNoHlsl()
    {
        var artifact = CreateArtifact(hlsl: string.Empty, stages: [new SdslvShaderArtifactStage("VSMain", SdslvShaderStageKind.Vertex, "vs_6_0")]);

        var result = DxcValidator.ValidateArtifact(artifact, executable: null);

        var validation = Assert.Single(result.Results);
        Assert.Equal(DxcValidationStatus.SkippedNoHlsl, validation.Status);
        Assert.True(result.Success);
    }

    [Fact]
    public void DxcValidator_ValidateArtifact_WithNoEntryPoints_SkipsNoEntryPoints()
    {
        var artifact = CreateArtifact(hlsl: "float4 VSMain(float4 position : POSITION) : SV_Position { return position; }", stages: []);

        var result = DxcValidator.ValidateArtifact(artifact, executable: null);

        var validation = Assert.Single(result.Results);
        Assert.Equal(DxcValidationStatus.SkippedNoEntryPoints, validation.Status);
        Assert.True(result.Success);
    }

    [Fact]
    public void DxcCommandLineBuilder_BuildsExpectedProfileAndEntryArguments()
    {
        var request = new DxcValidationRequest("hlsl", "PSMain", "ps_6_0", "shader.hlsl");

        var arguments = DxcCommandLineBuilder.Build(request, "/tmp/input.hlsl", "/tmp/output.bin");

        Assert.Contains("-T", arguments);
        Assert.Contains("ps_6_0", arguments);
        Assert.Contains("-E", arguments);
        Assert.Contains("PSMain", arguments);
        Assert.Contains("-nHV", arguments);
        Assert.Contains("2021", arguments);
        Assert.Contains("-Ges", arguments);
        Assert.Contains("-Fo", arguments);
        Assert.Equal("/tmp/input.hlsl", arguments[^3]);
        Assert.Equal("/tmp/output.bin", arguments[^1]);
    }

    [Fact]
    public void DxcValidator_ValidateSmokeTriangleArtifact_WhenDxcAvailable_SucceedsOrReportsFailure()
    {
        var dxc = DxcDiscovery.FindDxc();
        if (dxc is null)
        {
            return;
        }

        var artifact = EmitSmokeTriangle();
        var result = DxcValidator.ValidateArtifact(artifact, dxc);

        Assert.NotEmpty(result.Results);
        Assert.All(result.Results, validation => Assert.True(
            validation.Status is DxcValidationStatus.Succeeded or DxcValidationStatus.Failed,
            $"Unexpected DXC validation status: {validation.Status}"));
    }

    [Fact]
    public void DxcValidator_ValidateArtifact_WithNullExecutable_ReturnsAvailabilitySafeStatus()
    {
        var artifact = EmitSmokeTriangle();

        var result = WithoutPathOrEnvironmentDxc(() => DxcValidator.ValidateArtifact(artifact, executable: null));

        Assert.NotEmpty(result.Results);
        Assert.All(result.Results, validation => Assert.True(
            validation.Status is DxcValidationStatus.SkippedToolUnavailable or DxcValidationStatus.Succeeded or DxcValidationStatus.Failed,
            $"Unexpected DXC validation status: {validation.Status}"));
    }

    private static T WithoutPathOrEnvironmentDxc<T>(Func<T> action)
    {
        lock (EnvironmentLock)
        {
            var originalAurelianDxc = Environment.GetEnvironmentVariable("AURELIAN_DXC");
            var originalPath = Environment.GetEnvironmentVariable("PATH");

            try
            {
                Environment.SetEnvironmentVariable("AURELIAN_DXC", null);
                Environment.SetEnvironmentVariable("PATH", string.Empty);
                return action();
            }
            finally
            {
                Environment.SetEnvironmentVariable("AURELIAN_DXC", originalAurelianDxc);
                Environment.SetEnvironmentVariable("PATH", originalPath);
            }
        }
    }

    private static SdslvShaderArtifact EmitSmokeTriangle() => SdslvShaderArtifactEmitter.Emit(
        new SdslvShaderSource("Fixtures/Sdslv/smoke_triangle.sdslv", ReadFixture("smoke_triangle.sdslv")));

    private static SdslvShaderArtifact CreateArtifact(string hlsl, IReadOnlyList<SdslvShaderArtifactStage> stages) => new(
        "aurelian.sdslv.artifact/0",
        SdslvShaderArtifact.LanguageName,
        "inline.sdslv",
        SdslvShaderSourceHash.ComputeSha256("inline"),
        hlsl,
        stages,
        []);

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
}
