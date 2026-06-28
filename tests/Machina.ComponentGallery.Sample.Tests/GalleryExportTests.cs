using Xunit;

namespace Machina.ComponentGallery.Sample.Tests;

public sealed class GalleryExportTests
{
    [Fact]
    public void GalleryExportOptions_DefaultsAreStable()
    {
        var options = GalleryProgramOptions.Parse([]);

        Assert.False(options.ExportOnly);
        Assert.False(options.IncludeMsdfFontProof);
        Assert.Equal(Path.Combine("artifacts", "m7e"), options.ExportDirectory);
        Assert.Equal(GalleryExportContract.DefaultExportName, options.ExportName);
        Assert.Equal(GalleryState.Default, options.InitialState);
    }

    [Fact]
    public void GalleryExportOptions_ParsesDefaultExportCommand()
    {
        var options = GalleryProgramOptions.Parse(
        [
            "--export-only",
            "--export-dir",
            @"artifacts\m7e",
            "--export-name",
            "component-gallery-default",
        ]);

        Assert.True(options.ExportOnly);
        Assert.False(options.IncludeMsdfFontProof);
        Assert.Equal(@"artifacts\m7e", options.ExportDirectory);
        Assert.Equal("component-gallery-default", options.ExportName);
        Assert.Equal(GalleryState.Default, options.InitialState);
    }

    [Fact]
    public void GalleryProgramOptions_ParsesIncludeMsdfFontProof()
    {
        var options = GalleryProgramOptions.Parse(
        [
            "--export-only",
            "--include-msdf-font-proof",
        ]);

        Assert.True(options.ExportOnly);
        Assert.True(options.IncludeMsdfFontProof);
        Assert.Equal(GalleryExportContract.DefaultExportName, options.ExportName);
    }

    [Fact]
    public void GalleryExportContract_DefaultAndInteractiveNamesAreStable()
    {
        Assert.Equal("component-gallery-default", GalleryExportContract.DefaultExportName);
        Assert.Equal("component-gallery-interactive", GalleryExportContract.InteractiveExportName);
        Assert.Equal("component-gallery-msdf-proof", GalleryExportContract.MsdfProofExportName);
    }

    [Fact]
    public void GalleryExportContract_DefaultOutputPathsAreStable()
    {
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "component-gallery-default.png"),
            GalleryExportContract.GetDefaultOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "component-gallery-interactive.png"),
            GalleryExportContract.GetInteractiveOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "component-gallery-msdf-proof.png"),
            GalleryExportContract.GetMsdfProofOutputPath(Path.Combine("artifacts", "m7e")));
    }

    [Fact]
    public void GalleryExportOptions_ParsesInteractiveState()
    {
        var options = GalleryProgramOptions.Parse(
        [
            "--primary-clicks",
            "1",
            "--checkbox",
            "on",
            "--switch",
            "on",
        ]);

        Assert.Equal(
            GalleryState.Default with
            {
                PrimaryClicks = 1,
                LiveCheckboxChecked = true,
                LiveSwitchOn = true,
            },
            options.InitialState);
    }

    [Fact]
    public void GalleryExport_CreatesPngInRequestedDirectory()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "machina-gallery-export-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var result = GalleryExporter.Export(
                GalleryExportContract.InteractiveState,
                outputDirectory,
                GalleryExportContract.InteractiveExportName);

            Assert.Equal(
                GalleryExportContract.GetInteractiveOutputPath(outputDirectory),
                result.OutputPath);
            Assert.False(result.IncludeMsdfFontProof);
            Assert.Null(result.MsdfProofPlacement);
            Assert.True(File.Exists(result.OutputPath));

            var bytes = File.ReadAllBytes(result.OutputPath);
            Assert.True(bytes.Length > 8);
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'N', bytes[2]);
            Assert.Equal((byte)'G', bytes[3]);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void GalleryExport_Default_DoesNotIncludeMsdfProof()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "machina-gallery-export-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var result = GalleryExporter.Export(
                GalleryState.Default,
                outputDirectory,
                GalleryExportContract.DefaultExportName);

            Assert.False(result.IncludeMsdfFontProof);
            Assert.Null(result.MsdfProofPlacement);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void GalleryExport_WithMsdfProof_WritesArtifact()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "machina-gallery-export-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var result = GalleryExporter.Export(
                GalleryState.Default,
                outputDirectory,
                GalleryExportContract.MsdfProofExportName,
                includeMsdfFontProof: true);

            Assert.True(result.IncludeMsdfFontProof);
            Assert.NotNull(result.MsdfProofPlacement);
            Assert.Equal(
                GalleryExportContract.GetMsdfProofOutputPath(outputDirectory),
                result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));

            var bytes = File.ReadAllBytes(result.OutputPath);
            Assert.True(bytes.Length > 8);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}
