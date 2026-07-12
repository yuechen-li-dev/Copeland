using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public sealed record FontDiagnosticScene(
    string Id,
    string Text,
    int SizePx,
    int Width,
    int Height,
    int BaselineY,
    Rgba32 Background,
    Rgba32 Foreground,
    MachinaTextRenderStrategy StaticTextRenderStrategy,
    MachinaTextRenderStrategy MsdfRenderStrategy,
    RgbaImage? BrowserImage,
    string? BrowserImagePath,
    RgbaImage DirectImage,
    string DirectImagePath,
    RgbaImage MsdfImage,
    string MsdfImagePath,
    RgbaImage WireframeImage,
    string WireframeImagePath,
    InkMask? BrowserMask,
    InkMask DirectMask,
    InkMask MsdfMask,
    FontDiagnosticBounds? BrowserBounds,
    FontDiagnosticBounds? DirectBounds,
    FontDiagnosticBounds? MsdfBounds,
    IReadOnlyList<FontDiagnosticBounds> GlyphWireframes);

public sealed record DiagnosticPresetDefinition(
    string Name,
    string Description,
    FontDiagnosticPresetRequirements Requirements,
    Func<FontDiagnosticScene, FontDiagnosticExportOptions, DiagnosticLayerComposition> CreateComposition);

public static class LayerPresets
{
    public static IReadOnlyDictionary<string, DiagnosticPresetDefinition> GetRequiredPresets()
    {
        return Presets;
    }

    public static DiagnosticPresetDefinition GetPreset(string name)
    {
        if (!Presets.TryGetValue(name, out DiagnosticPresetDefinition? preset))
        {
            throw new InvalidOperationException($"Unknown diagnostic preset '{name}'.");
        }

        return preset;
    }

    private static IReadOnlyDictionary<string, DiagnosticPresetDefinition> Presets { get; } =
        new Dictionary<string, DiagnosticPresetDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["browser-vs-direct"] = new(
                "browser-vs-direct",
                "Browser image, direct outline image, bounds, baseline, and optional grid.",
                new FontDiagnosticPresetRequirements(
                [
                    FontDiagnosticSourceKind.BrowserReference,
                    FontDiagnosticSourceKind.DirectOutline,
                ]),
                static (scene, options) => CreateBrowserVsDirect(scene, options)),
            ["direct-vs-msdf"] = new(
                "direct-vs-msdf",
                "Direct-outline static reference and MSDF scalable/experimental comparison with bounds and baseline.",
                new FontDiagnosticPresetRequirements(
                [
                    FontDiagnosticSourceKind.DirectOutline,
                    FontDiagnosticSourceKind.Msdf,
                ]),
                static (scene, options) => CreateDirectVsMsdf(scene, options)),
            ["browser-vs-msdf"] = new(
                "browser-vs-msdf",
                "Browser image and MSDF scalable/experimental image comparison with bounds and baseline.",
                new FontDiagnosticPresetRequirements(
                [
                    FontDiagnosticSourceKind.BrowserReference,
                    FontDiagnosticSourceKind.Msdf,
                ]),
                static (scene, options) => CreateBrowserVsMsdf(scene, options)),
            ["three-way"] = new(
                "three-way",
                "Three-way mask overlay for browser, direct-outline static, and MSDF scalable/experimental where available.",
                new FontDiagnosticPresetRequirements(
                [
                    FontDiagnosticSourceKind.BrowserReference,
                    FontDiagnosticSourceKind.DirectOutline,
                    FontDiagnosticSourceKind.Msdf,
                ]),
                static (scene, options) => CreateThreeWay(scene, options)),
            ["grid-only"] = new(
                "grid-only",
                "Grid and baseline measurement surface.",
                new FontDiagnosticPresetRequirements(Array.Empty<FontDiagnosticSourceKind>()),
                static (scene, options) => CreateGridOnly(scene, options)),
            ["bounds-only"] = new(
                "bounds-only",
                "Direct-outline static bounds and wireframe overlay without image comparison.",
                new FontDiagnosticPresetRequirements(
                [
                    FontDiagnosticSourceKind.DirectOutline,
                ]),
                static (scene, options) => CreateBoundsOnly(scene, options)),
            ["cad-debug"] = new(
                "cad-debug",
                "CAD-style debug view with direct-outline static reference, grid, axes, baseline, bounds, labels, and wireframes.",
                new FontDiagnosticPresetRequirements(
                [
                    FontDiagnosticSourceKind.DirectOutline,
                ]),
                static (scene, options) => CreateCadDebug(scene, options)),
            ["msdf-debug"] = new(
                "msdf-debug",
                "MSDF scalable/experimental debug view with MSDF image, mask, bounds, and wireframes.",
                new FontDiagnosticPresetRequirements(
                [
                    FontDiagnosticSourceKind.Msdf,
                ]),
                static (scene, options) => CreateMsdfDebug(scene, options)),
        };

    private static DiagnosticLayerComposition CreateBrowserVsDirect(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticLayer> layers =
        [
            CreateGridLayer(scene, options, 0),
            CreateAxisLayer(scene, options, 10),
            CreateBaselineLayer(scene, options, 20),
            new DiagnosticImageLayer("browser-image", "Browser image", scene.BrowserImage is not null, 0.75d, 30, scene.BrowserImage, SourcePath: scene.BrowserImagePath, MissingReason: "Browser source unavailable."),
            new DiagnosticImageLayer("direct-image", "Direct-outline static image", true, scene.BrowserImage is not null ? 0.65d : 1d, 40, scene.DirectImage, SourcePath: scene.DirectImagePath),
            CreateBoundsLayer(scene, options, includeBrowser: true, includeDirect: true, includeMsdf: false, zIndex: 60),
            CreateBrowserUnavailableLabel(scene, 70),
        ];

        return new DiagnosticLayerComposition(scene.Width, scene.Height, scene.Background, layers);
    }

    private static DiagnosticLayerComposition CreateDirectVsMsdf(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticLayer> layers =
        [
            CreateGridLayer(scene, options, 0),
            CreateAxisLayer(scene, options, 10),
            CreateBaselineLayer(scene, options, 20),
            new DiagnosticImageLayer("direct-image", "Direct-outline static image", true, 0.70d, 30, scene.DirectImage, SourcePath: scene.DirectImagePath),
            new DiagnosticImageLayer("msdf-image", "MSDF scalable/experimental image", true, 0.70d, 40, scene.MsdfImage, SourcePath: scene.MsdfImagePath),
            CreateDifferenceLayer(scene, options, "difference", "Direct-outline static vs MSDF scalable/experimental difference", scene.DirectMask, scene.MsdfMask, null, zIndex: 50),
            CreateBoundsLayer(scene, options, includeBrowser: false, includeDirect: true, includeMsdf: true, zIndex: 60),
        ];

        return new DiagnosticLayerComposition(scene.Width, scene.Height, scene.Background, layers);
    }

    private static DiagnosticLayerComposition CreateBrowserVsMsdf(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticLayer> layers =
        [
            CreateGridLayer(scene, options, 0),
            CreateAxisLayer(scene, options, 10),
            CreateBaselineLayer(scene, options, 20),
            new DiagnosticImageLayer("browser-image", "Browser image", scene.BrowserImage is not null, 0.75d, 30, scene.BrowserImage, SourcePath: scene.BrowserImagePath, MissingReason: "Browser source unavailable."),
            new DiagnosticImageLayer("msdf-image", "MSDF scalable/experimental image", true, scene.BrowserImage is not null ? 0.65d : 1d, 40, scene.MsdfImage, SourcePath: scene.MsdfImagePath),
            CreateBoundsLayer(scene, options, includeBrowser: true, includeDirect: false, includeMsdf: true, zIndex: 60),
            CreateBrowserUnavailableLabel(scene, 70),
        ];

        return new DiagnosticLayerComposition(scene.Width, scene.Height, scene.Background, layers);
    }

    private static DiagnosticLayerComposition CreateThreeWay(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticLayer> layers =
        [
            CreateGridLayer(scene, options, 0),
            CreateBaselineLayer(scene, options, 10),
            new DiagnosticDifferenceLayer(
                "three-way-diff",
                "Three-way difference",
                true,
                0.85d,
                20,
                scene.BrowserMask is not null ? DiagnosticDifferenceMode.ThreeWayMaskOverlay : DiagnosticDifferenceMode.PairwiseMaskOverlay,
                scene.BrowserMask ?? scene.DirectMask,
                scene.DirectMask,
                scene.BrowserMask is not null ? scene.MsdfMask : null,
                scene.Background,
                options.BoundsOptions.BrowserBoundsColor,
                options.BoundsOptions.DirectOutlineBoundsColor,
                scene.Foreground,
                scene.BrowserMask is not null ? options.BoundsOptions.MsdfBoundsColor : null,
                options.GridOptions.BaselineColor,
                scene.BaselineY,
                scene.BrowserMask is null ? "Browser source unavailable; composition fell back to direct-vs-msdf." : null),
            CreateBoundsLayer(scene, options, includeBrowser: true, includeDirect: true, includeMsdf: true, zIndex: 30),
            CreateBrowserUnavailableLabel(scene, 40),
        ];

        return new DiagnosticLayerComposition(scene.Width, scene.Height, scene.Background, layers);
    }

    private static DiagnosticLayerComposition CreateGridOnly(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticLayer> layers =
        [
            CreateGridLayer(scene, options, 0),
            CreateAxisLayer(scene, options, 10),
            CreateBaselineLayer(scene, options, 20),
        ];

        return new DiagnosticLayerComposition(scene.Width, scene.Height, scene.Background, layers);
    }

    private static DiagnosticLayerComposition CreateBoundsOnly(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticLayer> layers =
        [
            new DiagnosticImageLayer("direct-image", "Direct-outline static image", true, 0.35d, 0, scene.DirectImage, SourcePath: scene.DirectImagePath),
            CreateBoundsLayer(scene, options, includeBrowser: true, includeDirect: true, includeMsdf: false, zIndex: 10),
            CreateGlyphWireframeLayer(scene, options, 20),
            CreateBrowserUnavailableLabel(scene, 30),
        ];

        return new DiagnosticLayerComposition(scene.Width, scene.Height, scene.Background, layers);
    }

    private static DiagnosticLayerComposition CreateCadDebug(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticLayer> layers =
        [
            new DiagnosticImageLayer("direct-image", "Direct-outline static image", true, 0.40d, 0, scene.DirectImage, SourcePath: scene.DirectImagePath),
            CreateGridLayer(scene, options, 10),
            CreateAxisLayer(scene, options, 20),
            CreateBaselineLayer(scene, options, 30),
            CreateBoundsLayer(scene, options, includeBrowser: true, includeDirect: true, includeMsdf: false, zIndex: 40),
            CreateGlyphWireframeLayer(scene, options, 50),
            new DiagnosticTextLabelLayer(
                "cad-labels",
                "CAD labels",
                true,
                1d,
                60,
                BuildCadLabels(scene, options)),
            CreateBrowserUnavailableLabel(scene, 70),
        ];

        return new DiagnosticLayerComposition(scene.Width, scene.Height, scene.Background, layers);
    }

    private static DiagnosticLayerComposition CreateMsdfDebug(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticLayer> layers =
        [
            CreateGridLayer(scene, options, 0),
            CreateAxisLayer(scene, options, 10),
            CreateBaselineLayer(scene, options, 20),
            new DiagnosticImageLayer("msdf-image", "MSDF scalable/experimental image", true, 0.80d, 30, scene.MsdfImage, SourcePath: scene.MsdfImagePath),
            new DiagnosticMaskLayer("msdf-mask", "MSDF scalable/experimental mask", true, 0.30d, 40, scene.MsdfMask, options.BoundsOptions.MsdfBoundsColor, SourcePath: scene.MsdfImagePath),
            CreateBoundsLayer(scene, options, includeBrowser: false, includeDirect: false, includeMsdf: true, zIndex: 50),
            CreateGlyphWireframeLayer(scene, options, 60),
        ];

        return new DiagnosticLayerComposition(scene.Width, scene.Height, scene.Background, layers);
    }

    private static DiagnosticGridLayer CreateGridLayer(FontDiagnosticScene scene, FontDiagnosticExportOptions options, int zIndex)
    {
        return new DiagnosticGridLayer(
            "grid",
            "Grid",
            options.GridOptions.ShowGrid,
            1d,
            zIndex,
            options.GridOptions.GridStep,
            options.GridOptions.AxisStep,
            options.GridOptions.ShowUnitLabels,
            options.GridOptions.GridColor,
            options.GridOptions.MajorGridColor,
            options.GridOptions.LabelColor);
    }

    private static DiagnosticAxisLayer CreateAxisLayer(FontDiagnosticScene scene, FontDiagnosticExportOptions options, int zIndex)
    {
        return new DiagnosticAxisLayer(
            "axes",
            "Axes",
            options.GridOptions.ShowAxes,
            1d,
            zIndex,
            ShowXAxis: true,
            ShowYAxis: true,
            options.GridOptions.ShowOriginMarker,
            options.GridOptions.AxisStep,
            options.GridOptions.AxisColor);
    }

    private static DiagnosticBaselineLayer CreateBaselineLayer(FontDiagnosticScene scene, FontDiagnosticExportOptions options, int zIndex)
    {
        return new DiagnosticBaselineLayer(
            "baseline",
            "Baseline",
            options.GridOptions.ShowBaseline,
            1d,
            zIndex,
            scene.BaselineY,
            options.GridOptions.BaselineColor);
    }

    private static DiagnosticBoundsLayer CreateBoundsLayer(
        FontDiagnosticScene scene,
        FontDiagnosticExportOptions options,
        bool includeBrowser,
        bool includeDirect,
        bool includeMsdf,
        int zIndex)
    {
        List<DiagnosticBoundsItem> items = [];
        if (includeBrowser)
        {
            items.Add(new DiagnosticBoundsItem("browser-bounds", "browser", scene.BrowserBounds, options.BoundsOptions.BrowserBoundsColor));
        }

        if (includeDirect)
        {
            items.Add(new DiagnosticBoundsItem("direct-bounds", "direct", scene.DirectBounds, options.BoundsOptions.DirectOutlineBoundsColor));
        }

        if (includeMsdf)
        {
            items.Add(new DiagnosticBoundsItem("msdf-bounds", "msdf", scene.MsdfBounds, options.BoundsOptions.MsdfBoundsColor));
        }

        return new DiagnosticBoundsLayer(
            "bounds",
            "Bounds",
            options.BoundsOptions.ShowBounds,
            1d,
            zIndex,
            items);
    }

    private static DiagnosticDifferenceLayer CreateDifferenceLayer(
        FontDiagnosticScene scene,
        FontDiagnosticExportOptions options,
        string id,
        string label,
        InkMask leftMask,
        InkMask rightMask,
        InkMask? thirdMask,
        int zIndex)
    {
        return new DiagnosticDifferenceLayer(
            id,
            label,
            true,
            0.70d,
            zIndex,
            thirdMask is null ? DiagnosticDifferenceMode.PairwiseMaskOverlay : DiagnosticDifferenceMode.ThreeWayMaskOverlay,
            leftMask,
            rightMask,
            thirdMask,
            scene.Background,
            options.BoundsOptions.DirectOutlineBoundsColor,
            options.BoundsOptions.MsdfBoundsColor,
            scene.Foreground,
            thirdMask is null ? null : options.BoundsOptions.BrowserBoundsColor,
            options.GridOptions.BaselineColor,
            scene.BaselineY);
    }

    private static DiagnosticGlyphWireframeLayer CreateGlyphWireframeLayer(FontDiagnosticScene scene, FontDiagnosticExportOptions options, int zIndex)
    {
        return new DiagnosticGlyphWireframeLayer(
            "glyph-wireframes",
            "Glyph wireframes",
            options.BoundsOptions.ShowWireframes,
            1d,
            zIndex,
            scene.GlyphWireframes,
            options.BoundsOptions.WireframeColor);
    }

    private static DiagnosticTextLabelLayer CreateBrowserUnavailableLabel(FontDiagnosticScene scene, int zIndex)
    {
        bool browserMissing = scene.BrowserImage is null;
        return new DiagnosticTextLabelLayer(
            "browser-status-label",
            "Browser status label",
            browserMissing,
            1d,
            zIndex,
            browserMissing
                ? [new DiagnosticTextLabel("browser unavailable", 8, 8, new Rgba32(255, 220, 96, 255))]
                : Array.Empty<DiagnosticTextLabel>());
    }

    private static IReadOnlyList<DiagnosticTextLabel> BuildCadLabels(FontDiagnosticScene scene, FontDiagnosticExportOptions options)
    {
        List<DiagnosticTextLabel> labels =
        [
            new($"{scene.Text} @ {scene.SizePx}px", 8, Math.Max(8, scene.Height - 14), options.GridOptions.LabelColor),
            new($"baseline {scene.BaselineY}", 8, Math.Max(1, scene.BaselineY - 8), options.GridOptions.BaselineColor),
        ];

        return labels;
    }
}
