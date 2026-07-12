using Aurelian.Shaders.Language.External.Dxc;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class DxcSubprocessM0Tests
{
    [Fact]
    public void DxcExecutableResolver_Resolve_DoesNotThrow()
    {
        var exception = Record.Exception(() => DxcExecutableResolver.Resolve());

        Assert.Null(exception);
    }

    [Fact]
    public void DxcExecutableResolver_Resolve_ReturnsAvailableOrUnavailable()
    {
        var resolution = DxcExecutableResolver.Resolve();

        Assert.True(resolution.Status is DxcToolStatus.Available or DxcToolStatus.Unavailable);
        if (resolution.Success)
        {
            Assert.True(File.Exists(resolution.ExecutablePath));
        }
        else
        {
            Assert.NotEmpty(resolution.Diagnostics);
        }
    }

    [Fact]
    public void DxcSpirvCompiler_CompileRejectsEmptySource()
    {
        var result = DxcSpirvCompiler.Compile(new DxcSpirvCompileRequest(string.Empty, "VSMain", "vs_6_0", "empty.hlsl"));

        Assert.Equal(DxcSpirvStatus.Rejected, result.Status);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Empty(result.Arguments);
    }

    [Fact]
    public void DxcSpirvCompiler_CompileRejectsMissingEntryPoint()
    {
        var result = DxcSpirvCompiler.Compile(new DxcSpirvCompileRequest("float4 Main() : SV_Target0 { return 1; }", string.Empty, "ps_6_0", "missing-entry.hlsl"));

        Assert.Equal(DxcSpirvStatus.Rejected, result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void DxcSpirvCompiler_CompileRejectsMissingProfile()
    {
        var result = DxcSpirvCompiler.Compile(new DxcSpirvCompileRequest("float4 Main() : SV_Target0 { return 1; }", "Main", string.Empty, "missing-profile.hlsl"));

        Assert.Equal(DxcSpirvStatus.Rejected, result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void DxcSpirvCompiler_CompileTinyVertexShader_WhenDxcAvailable_ProducesSpirvBytes()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            Assert.NotEmpty(resolution.Diagnostics);
            return;
        }

        var result = DxcSpirvCompiler.Compile(
            new DxcSpirvCompileRequest(ReadHlslFixture("tiny_triangle_vs.hlsl"), "VSMain", "vs_6_0", "tiny_triangle_vs.hlsl"),
            resolution);

        AssertCompiledSpirv(result);
    }

    [Fact]
    public void DxcSpirvCompiler_CompileTinyPixelShader_WhenDxcAvailable_ProducesSpirvBytes()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            Assert.NotEmpty(resolution.Diagnostics);
            return;
        }

        var result = DxcSpirvCompiler.Compile(
            new DxcSpirvCompileRequest(ReadHlslFixture("tiny_triangle_ps.hlsl"), "PSMain", "ps_6_0", "tiny_triangle_ps.hlsl"),
            resolution);

        AssertCompiledSpirv(result);
    }

    [Fact]
    public void DxcSpirvCompiler_InvalidHlsl_WhenDxcAvailable_ReturnsFailedDiagnostics()
    {
        var resolution = DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            Assert.NotEmpty(resolution.Diagnostics);
            return;
        }

        var result = DxcSpirvCompiler.Compile(
            new DxcSpirvCompileRequest("float4 PSMain( : SV_Target0 { return nope; }", "PSMain", "ps_6_0", "invalid.hlsl"),
            resolution);

        Assert.Equal(DxcSpirvStatus.Failed, result.Status);
        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(!string.IsNullOrWhiteSpace(result.StandardError) || !string.IsNullOrWhiteSpace(result.StandardOutput) || result.Diagnostics.Count > 0);
    }

    [Fact]
    public void DxcSpirvCompiler_DoesNotRequireGraphicsRuntime()
    {
        var references = typeof(DxcSpirvCompiler).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("Aurelian.Graphics", references);
        Assert.DoesNotContain("Silk.NET.Vulkan", references);
    }

    private static void AssertCompiledSpirv(DxcSpirvCompileResult result)
    {
        Assert.Equal(DxcSpirvStatus.Compiled, result.Status);
        Assert.True(result.Success, result.StandardError);
        Assert.True(result.SpirvBytes.Length > 4);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(result.SpirvBytes, 0));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("-spirv", result.Arguments);
        Assert.Contains("-fspv-target-env=vulkan1.3", result.Arguments);
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
}
