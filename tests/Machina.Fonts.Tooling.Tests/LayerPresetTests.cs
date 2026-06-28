using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Tests;

public sealed class LayerPresetTests
{
    [Fact]
    public void LayerPresets_IncludeRequiredPresets()
    {
        IReadOnlyDictionary<string, DiagnosticPresetDefinition> presets = LayerPresets.GetRequiredPresets();

        Assert.Contains("browser-vs-direct", presets.Keys);
        Assert.Contains("direct-vs-msdf", presets.Keys);
        Assert.Contains("browser-vs-msdf", presets.Keys);
        Assert.Contains("three-way", presets.Keys);
        Assert.Contains("grid-only", presets.Keys);
        Assert.Contains("bounds-only", presets.Keys);
        Assert.Contains("cad-debug", presets.Keys);
        Assert.Contains("msdf-debug", presets.Keys);
    }

    [Fact]
    public void LayerPreset_DirectVsMsdf_HasExpectedLayers()
    {
        DiagnosticLayerComposition composition = LayerPresets
            .GetPreset("direct-vs-msdf")
            .CreateComposition(CreateScene(), CreateOptions());

        Assert.Contains(composition.Layers, layer => layer is DiagnosticImageLayer { Id: "direct-image" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticImageLayer { Id: "msdf-image" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticDifferenceLayer { Id: "difference" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticBoundsLayer { Id: "bounds" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticBaselineLayer { Id: "baseline" });
    }

    [Fact]
    public void LayerPreset_CadDebug_HasGridBoundsAxesBaseline()
    {
        DiagnosticLayerComposition composition = LayerPresets
            .GetPreset("cad-debug")
            .CreateComposition(CreateScene(), CreateOptions());

        Assert.Contains(composition.Layers, layer => layer is DiagnosticGridLayer { Id: "grid" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticAxisLayer { Id: "axes" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticBaselineLayer { Id: "baseline" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticBoundsLayer { Id: "bounds" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticGlyphWireframeLayer { Id: "glyph-wireframes" });
    }

    [Fact]
    public void LayerPreset_MsdfDebug_HasMsdfAndFieldBoundsLayers()
    {
        DiagnosticLayerComposition composition = LayerPresets
            .GetPreset("msdf-debug")
            .CreateComposition(CreateScene(), CreateOptions());

        Assert.Contains(composition.Layers, layer => layer is DiagnosticImageLayer { Id: "msdf-image" });
        Assert.Contains(composition.Layers, layer => layer is DiagnosticMaskLayer { Id: "msdf-mask" });

        DiagnosticBoundsLayer boundsLayer = Assert.Single(composition.Layers.OfType<DiagnosticBoundsLayer>());
        Assert.Contains(boundsLayer.Items, item => item.Id == "msdf-bounds");
    }

    private static FontDiagnosticScene CreateScene()
    {
        RgbaImage directImage = CreateFilledImage(32, 16, new Rgba32(10, 10, 10, 255));
        RgbaImage msdfImage = CreateFilledImage(32, 16, new Rgba32(20, 20, 20, 255));
        RgbaImage wireframeImage = CreateFilledImage(32, 16, Rgba32.Transparent);
        InkMask directMask = CreateMask(32, 16, new FontDiagnosticBounds(2, 4, 14, 10));
        InkMask msdfMask = CreateMask(32, 16, new FontDiagnosticBounds(3, 4, 15, 10));

        return new FontDiagnosticScene(
            "hello-machina",
            "Hello Machina",
            32,
            32,
            16,
            12,
            Rgba32.Black,
            Rgba32.White,
            BrowserImage: null,
            BrowserImagePath: null,
            DirectImage: directImage,
            DirectImagePath: "direct.png",
            MsdfImage: msdfImage,
            MsdfImagePath: "msdf.png",
            WireframeImage: wireframeImage,
            WireframeImagePath: "wireframe.png",
            BrowserMask: null,
            DirectMask: directMask,
            MsdfMask: msdfMask,
            BrowserBounds: null,
            DirectBounds: new FontDiagnosticBounds(2, 4, 14, 10),
            MsdfBounds: new FontDiagnosticBounds(3, 4, 15, 10),
            GlyphWireframes:
            [
                new FontDiagnosticBounds(2, 4, 8, 10),
                new FontDiagnosticBounds(9, 4, 15, 10),
            ]);
    }

    private static FontDiagnosticExportOptions CreateOptions()
    {
        return new FontDiagnosticExportOptions
        {
            OutputDirectory = ".",
            AtlasName = "test",
            FontPath = CrimsonTextFixtureFont.FontPath,
            FontFamilyName = "Crimson Text",
            FontStyleName = "Regular",
            LicenseIdentifier = "OFL-1.1",
            Face = CrimsonTextFixtureFont.Face,
            TextDefinitions = [new FontDiagnosticTextDefinition("hello-machina", "Hello Machina")],
            CanvasDefinitions = [new FontDiagnosticCanvasDefinition(32, 32, 16, 0d, 12d)],
        };
    }

    private static InkMask CreateMask(int width, int height, FontDiagnosticBounds bounds)
    {
        InkMask mask = new(width, height);
        for (int y = bounds.Top; y <= bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x <= bounds.Right; x++)
            {
                mask.SetCoverage(x, y, 1f);
            }
        }

        return mask;
    }

    private static RgbaImage CreateFilledImage(int width, int height, Rgba32 color)
    {
        return new RgbaImage(width, height, Enumerable.Repeat(color, width * height).ToArray());
    }
}
