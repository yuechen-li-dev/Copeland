using Aurelian.AssetTool;
using Aurelian.Assets;
using Xunit;

namespace Aurelian.AssetTool.Tests;

public sealed class AssetToolIdentityTests
{
    [Fact]
    public void FormatterAssemblyName_IsAurelianAssetTool()
    {
        var assemblyName = typeof(CliDiagnosticFormatter).Assembly.GetName().Name;

        Assert.Equal("Aurelian.AssetTool", assemblyName);
    }

    [Fact]
    public void TextDiagnosticFormatter_FormatsAssetDiagnostic()
    {
        var diagnostic = new AssetDiagnostic("AM001", "error", "Manifest missing.", "assets.toml", 2, 3);

        var formatted = CliDiagnosticFormatter.FormatDiagnostic(diagnostic, DiagnosticFormat.Text);

        Assert.Contains("ERROR AM001", formatted);
        Assert.Contains("assets.toml:2:3", formatted);
        Assert.Contains("Manifest missing.", formatted);
    }
}
