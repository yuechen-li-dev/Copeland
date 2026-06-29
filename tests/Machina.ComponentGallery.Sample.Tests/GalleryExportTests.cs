using Xunit;

namespace Machina.ComponentGallery.Sample.Tests;

public sealed class GalleryExportTests
{
    [Fact]
    public void GalleryExportOptions_DefaultsAreStable()
    {
        var options = GalleryProgramOptions.Parse([]);

        Assert.False(options.ExportOnly);
        Assert.False(options.IncludeDirectOutlineTextProof);
        Assert.False(options.IncludeDirectOutlineRenderBridgeProof);
        Assert.False(options.IncludeDirectOutlineTextLayoutProof);
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
        Assert.False(options.IncludeDirectOutlineTextProof);
        Assert.False(options.IncludeDirectOutlineRenderBridgeProof);
        Assert.False(options.IncludeDirectOutlineTextLayoutProof);
        Assert.False(options.IncludeMsdfFontProof);
        Assert.Equal(@"artifacts\m7e", options.ExportDirectory);
        Assert.Equal("component-gallery-default", options.ExportName);
        Assert.Equal(GalleryState.Default, options.InitialState);
    }

    [Fact]
    public void GalleryProgramOptions_ParsesIncludeDirectOutlineTextProof()
    {
        var options = GalleryProgramOptions.Parse(
        [
            "--export-only",
            "--include-direct-outline-text-proof",
        ]);

        Assert.True(options.ExportOnly);
        Assert.True(options.IncludeDirectOutlineTextProof);
        Assert.False(options.IncludeDirectOutlineRenderBridgeProof);
        Assert.False(options.IncludeDirectOutlineTextLayoutProof);
        Assert.False(options.IncludeMsdfFontProof);
        Assert.Equal(GalleryExportContract.DefaultExportName, options.ExportName);
    }

    [Fact]
    public void GalleryProgramOptions_ParsesIncludeDirectOutlineTextLayoutProof()
    {
        var options = GalleryProgramOptions.Parse(
        [
            "--export-only",
            "--include-direct-outline-text-layout-proof",
        ]);

        Assert.True(options.ExportOnly);
        Assert.False(options.IncludeDirectOutlineTextProof);
        Assert.False(options.IncludeDirectOutlineRenderBridgeProof);
        Assert.True(options.IncludeDirectOutlineTextLayoutProof);
        Assert.False(options.IncludeMsdfFontProof);
        Assert.Equal(GalleryExportContract.DefaultExportName, options.ExportName);
    }

    [Fact]
    public void GalleryProgramOptions_ParsesIncludeDirectOutlineRenderBridgeProof()
    {
        var options = GalleryProgramOptions.Parse(
        [
            "--export-only",
            "--include-direct-outline-render-bridge-proof",
        ]);

        Assert.True(options.ExportOnly);
        Assert.False(options.IncludeDirectOutlineTextProof);
        Assert.True(options.IncludeDirectOutlineRenderBridgeProof);
        Assert.False(options.IncludeDirectOutlineTextLayoutProof);
        Assert.False(options.IncludeMsdfFontProof);
        Assert.Equal(GalleryExportContract.DefaultExportName, options.ExportName);
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
        Assert.Equal("component-gallery-direct-outline-text-proof", GalleryExportContract.DirectOutlineProofExportName);
        Assert.Equal("component-gallery-direct-outline-render-bridge-proof", GalleryExportContract.DirectOutlineRenderBridgeProofExportName);
        Assert.Equal("component-gallery-direct-outline-text-layout-proof", GalleryExportContract.DirectOutlineTextLayoutProofExportName);
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
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "component-gallery-direct-outline-text-proof.png"),
            GalleryExportContract.GetDirectOutlineProofOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "component-gallery-direct-outline-render-bridge-proof.png"),
            GalleryExportContract.GetDirectOutlineRenderBridgeProofOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "component-gallery-direct-outline-text-layout-proof.png"),
            GalleryExportContract.GetDirectOutlineTextLayoutProofOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "component-gallery-text-backend-comparison.png"),
            GalleryExportContract.GetTextBackendComparisonOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "direct-outline-static-text-proof.png"),
            GalleryExportContract.GetDirectOutlineStandaloneOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "direct-outline-render-bridge-proof.png"),
            GalleryExportContract.GetDirectOutlineRenderBridgeOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "direct-outline-render-bridge-layout-grid.png"),
            GalleryExportContract.GetDirectOutlineRenderBridgeLayoutGridOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "direct-outline-text-box-layout-proof.png"),
            GalleryExportContract.GetDirectOutlineTextBoxLayoutOutputPath(Path.Combine("artifacts", "m7e")));
        Assert.Equal(
            Path.Combine("artifacts", "m7e", "direct-outline-text-alignment-grid.png"),
            GalleryExportContract.GetDirectOutlineTextAlignmentGridOutputPath(Path.Combine("artifacts", "m7e")));
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
            Assert.False(result.ProofOptions.IncludeDirectOutlineTextProof);
            Assert.False(result.ProofOptions.IncludeDirectOutlineRenderBridgeProof);
            Assert.False(result.ProofOptions.IncludeDirectOutlineTextLayoutProof);
            Assert.False(result.ProofOptions.IncludeMsdfFontProof);
            Assert.Null(result.DirectOutlineProofPlacement);
            Assert.Null(result.DirectOutlineRenderBridgeProofPlacement);
            Assert.Null(result.DirectOutlineTextLayoutProofPlacement);
            Assert.Null(result.MsdfProofPlacement);
            Assert.Null(result.DirectOutlineArtifacts);
            Assert.Null(result.DirectOutlineRenderBridgeArtifacts);
            Assert.Null(result.DirectOutlineTextLayoutArtifacts);
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

            Assert.False(result.ProofOptions.IncludeDirectOutlineTextProof);
            Assert.False(result.ProofOptions.IncludeDirectOutlineRenderBridgeProof);
            Assert.False(result.ProofOptions.IncludeDirectOutlineTextLayoutProof);
            Assert.False(result.ProofOptions.IncludeMsdfFontProof);
            Assert.Null(result.DirectOutlineProofPlacement);
            Assert.Null(result.DirectOutlineRenderBridgeProofPlacement);
            Assert.Null(result.DirectOutlineTextLayoutProofPlacement);
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
    public void ExportComponentGallery_WithDirectOutlineTextProof_WritesArtifact()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "machina-gallery-export-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var result = GalleryExporter.Export(
                GalleryState.Default,
                outputDirectory,
                GalleryExportContract.DirectOutlineProofExportName,
                new GalleryProofOptions(IncludeDirectOutlineTextProof: true));

            Assert.True(result.ProofOptions.IncludeDirectOutlineTextProof);
            Assert.False(result.ProofOptions.IncludeDirectOutlineTextLayoutProof);
            Assert.False(result.ProofOptions.IncludeMsdfFontProof);
            Assert.NotNull(result.DirectOutlineProofPlacement);
            Assert.Null(result.DirectOutlineTextLayoutProofPlacement);
            Assert.Equal(
                GalleryExportContract.GetDirectOutlineProofOutputPath(outputDirectory),
                result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.NotNull(result.DirectOutlineArtifacts);
            Assert.True(File.Exists(result.DirectOutlineArtifacts!.StandaloneProofPath));
            Assert.True(File.Exists(result.DirectOutlineArtifacts.ComparisonPath));
            Assert.Equal(
                GalleryExportContract.GetDirectOutlineStandaloneOutputPath(outputDirectory),
                result.DirectOutlineArtifacts.StandaloneProofPath);
            Assert.Equal(
                GalleryExportContract.GetTextBackendComparisonOutputPath(outputDirectory),
                result.DirectOutlineArtifacts.ComparisonPath);

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

    [Fact]
    public void ExportComponentGallery_DefaultBehaviorUnchanged()
    {
        var options = GalleryProgramOptions.Parse(["--export-only"]);

        Assert.False(options.IncludeDirectOutlineTextProof);
        Assert.False(options.IncludeDirectOutlineRenderBridgeProof);
        Assert.False(options.IncludeDirectOutlineTextLayoutProof);
        Assert.False(options.IncludeMsdfFontProof);
        Assert.Equal(GalleryExportContract.DefaultExportName, options.ExportName);
    }

    [Fact]
    public void ExportComponentGallery_WithRenderBridgeProof_WritesArtifact()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "machina-gallery-export-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var result = GalleryExporter.Export(
                GalleryState.Default,
                outputDirectory,
                GalleryExportContract.DirectOutlineRenderBridgeProofExportName,
                new GalleryProofOptions(IncludeDirectOutlineRenderBridgeProof: true));

            Assert.False(result.ProofOptions.IncludeDirectOutlineTextProof);
            Assert.True(result.ProofOptions.IncludeDirectOutlineRenderBridgeProof);
            Assert.False(result.ProofOptions.IncludeDirectOutlineTextLayoutProof);
            Assert.False(result.ProofOptions.IncludeMsdfFontProof);
            Assert.NotNull(result.DirectOutlineRenderBridgeProofPlacement);
            Assert.Equal(
                GalleryExportContract.GetDirectOutlineRenderBridgeProofOutputPath(outputDirectory),
                result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.NotNull(result.DirectOutlineRenderBridgeArtifacts);
            Assert.True(File.Exists(result.DirectOutlineRenderBridgeArtifacts!.StandaloneProofPath));
            Assert.True(File.Exists(result.DirectOutlineRenderBridgeArtifacts.AlignmentGridPath));
            Assert.Equal(
                GalleryExportContract.GetDirectOutlineRenderBridgeOutputPath(outputDirectory),
                result.DirectOutlineRenderBridgeArtifacts.StandaloneProofPath);
            Assert.Equal(
                GalleryExportContract.GetDirectOutlineRenderBridgeLayoutGridOutputPath(outputDirectory),
                result.DirectOutlineRenderBridgeArtifacts.AlignmentGridPath);
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
    public void ExportComponentGallery_WithDirectOutlineTextLayoutProof_WritesArtifact()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "machina-gallery-export-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var result = GalleryExporter.Export(
                GalleryState.Default,
                outputDirectory,
                GalleryExportContract.DirectOutlineTextLayoutProofExportName,
                new GalleryProofOptions(IncludeDirectOutlineTextLayoutProof: true));

            Assert.False(result.ProofOptions.IncludeDirectOutlineTextProof);
            Assert.True(result.ProofOptions.IncludeDirectOutlineTextLayoutProof);
            Assert.False(result.ProofOptions.IncludeMsdfFontProof);
            Assert.NotNull(result.DirectOutlineTextLayoutProofPlacement);
            Assert.Equal(
                GalleryExportContract.GetDirectOutlineTextLayoutProofOutputPath(outputDirectory),
                result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.NotNull(result.DirectOutlineTextLayoutArtifacts);
            Assert.True(File.Exists(result.DirectOutlineTextLayoutArtifacts!.StandaloneProofPath));
            Assert.True(File.Exists(result.DirectOutlineTextLayoutArtifacts.AlignmentGridPath));
            Assert.Equal(
                GalleryExportContract.GetDirectOutlineTextBoxLayoutOutputPath(outputDirectory),
                result.DirectOutlineTextLayoutArtifacts.StandaloneProofPath);
            Assert.Equal(
                GalleryExportContract.GetDirectOutlineTextAlignmentGridOutputPath(outputDirectory),
                result.DirectOutlineTextLayoutArtifacts.AlignmentGridPath);
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

            Assert.True(result.ProofOptions.IncludeMsdfFontProof);
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
