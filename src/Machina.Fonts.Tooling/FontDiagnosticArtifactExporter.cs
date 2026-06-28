using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Tooling;

public sealed class FontDiagnosticArtifactExporter
{
    private readonly IGlyphDistanceFieldGenerator distanceFieldGenerator;
    private readonly IGlyphOutlineSource outlineSource;
    private readonly GeneratedFieldAtlasPacker packer;
    private readonly IGlyphPairAdjustmentSource? pairAdjustmentSource;

    public FontDiagnosticArtifactExporter(
        IGlyphOutlineSource outlineSource,
        IGlyphDistanceFieldGenerator distanceFieldGenerator,
        GeneratedFieldAtlasPacker? packer = null,
        IGlyphPairAdjustmentSource? pairAdjustmentSource = null)
    {
        this.outlineSource = outlineSource ?? throw new ArgumentNullException(nameof(outlineSource));
        this.distanceFieldGenerator = distanceFieldGenerator ?? throw new ArgumentNullException(nameof(distanceFieldGenerator));
        this.packer = packer ?? new GeneratedFieldAtlasPacker();
        this.pairAdjustmentSource = pairAdjustmentSource ?? outlineSource as IGlyphPairAdjustmentSource;
    }

    public async Task<FontDiagnosticExportResult> ExportAsync(
        FontDiagnosticExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        FontDiagnosticExportOptions validated = options.Validate();

        string outputDirectory = Path.GetFullPath(validated.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        List<FontShapeDiffSizeReport> shapeDiffSizeReports = [];
        List<LayerCompositionArtifactReport> compositionArtifacts = [];

        foreach (FontDiagnosticCanvasDefinition canvas in validated.CanvasDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string sizeDirectory = Path.Combine(outputDirectory, canvas.SizeDirectoryName);
            Directory.CreateDirectory(sizeDirectory);

            GeneratedDiagnosticSceneSet sceneSet = await GenerateSceneSetAsync(validated, canvas, sizeDirectory, cancellationToken);
            shapeDiffSizeReports.Add(BuildShapeDiffSizeReport(canvas, sceneSet));

            foreach (GeneratedDiagnosticScene generatedScene in sceneSet.Scenes)
            {
                foreach (string presetName in validated.PresetNames)
                {
                    DiagnosticPresetDefinition preset = LayerPresets.GetPreset(presetName);
                    DiagnosticLayerComposition composition = preset.CreateComposition(generatedScene.Scene, validated);
                    RgbaImage composedImage = LayerCompositor.Compose(composition);
                    string artifactPath = Path.Combine(
                        sizeDirectory,
                        sceneSet.TextDefinitionsById[generatedScene.Scene.Id].GetPresetArtifactFileName(presetName));
                    RgbaPngWriter.Write(artifactPath, composedImage);

                    compositionArtifacts.Add(BuildCompositionArtifactReport(
                        presetName,
                        generatedScene.Scene,
                        artifactPath,
                        composition,
                        validated));
                }
            }
        }

        FontShapeDiffReport shapeDiffReport = new(
            validated.FontPath,
            validated.Face.Value,
            shapeDiffSizeReports.Select(static report => report.SizePx).ToArray(),
            validated.TextDefinitions.Select(static definition => definition.Text).ToArray(),
            "Direct outline rasterization with Machina's own kerning is the current geometry reference for M9 diagnostics.",
            "Browser capture may still be useful for context, but browser horizontal kerning is not the target oracle in this workflow.",
            "Diagnostic layers make measurements, baseline placement, bounds, and diffs easier to inspect for humans and LLMs without changing production text behavior.",
            shapeDiffSizeReports);

        LayerCompositionReport compositionReport = new(
            outputDirectory,
            validated.PresetNames.ToArray(),
            compositionArtifacts);

        string shapeDiffReportJsonPath = Path.Combine(outputDirectory, "shape-diff-report.json");
        string shapeDiffReportTextPath = Path.Combine(outputDirectory, "shape-diff-report.txt");
        File.WriteAllText(shapeDiffReportJsonPath, JsonSerializer.Serialize(shapeDiffReport, JsonOptions));
        File.WriteAllText(shapeDiffReportTextPath, BuildShapeDiffTextReport(shapeDiffReport));

        string layerCompositionReportJsonPath = Path.Combine(outputDirectory, "layer-composition-report.json");
        string layerCompositionReportTextPath = Path.Combine(outputDirectory, "layer-composition-report.txt");
        File.WriteAllText(layerCompositionReportJsonPath, JsonSerializer.Serialize(compositionReport, JsonOptions));
        File.WriteAllText(layerCompositionReportTextPath, BuildLayerCompositionTextReport(compositionReport));

        return new FontDiagnosticExportResult(
            outputDirectory,
            shapeDiffReportJsonPath,
            shapeDiffReportTextPath,
            shapeDiffReport,
            layerCompositionReportJsonPath,
            layerCompositionReportTextPath,
            compositionReport);
    }

    private async Task<GeneratedDiagnosticSceneSet> GenerateSceneSetAsync(
        FontDiagnosticExportOptions options,
        FontDiagnosticCanvasDefinition canvas,
        string sizeDirectory,
        CancellationToken cancellationToken)
    {
        FontProofExporter proofExporter = new(
            outlineSource,
            distanceFieldGenerator,
            CreateMetadata(options, canvas.SizePx),
            packer,
            pairAdjustmentSource);

        FontProofArtifactDefinition[] proofDefinitions = options.TextDefinitions
            .Select(static definition => new FontProofArtifactDefinition(definition.MsdfPpmFileName, definition.Text))
            .ToArray();

        FontProofExportResult proofExport = await proofExporter.ExportAsync(
            proofDefinitions,
            CreateProofOptions(options, sizeDirectory, canvas),
            cancellationToken);

        if (!proofExport.Success || proofExport.Snapshot is null)
        {
            string diagnostics = string.Join(
                " | ",
                proofExport.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
            throw new InvalidOperationException($"Font diagnostics export failed for {canvas.SizePx}px. {diagnostics}");
        }

        Dictionary<GlyphKey, GlyphOutline> outlinesByGlyph = await LoadOutlinesAsync(options, canvas, cancellationToken);
        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = outlinesByGlyph.ToDictionary(static item => item.Key, static item => item.Value.Metrics);
        List<GeneratedDiagnosticScene> scenes = [];
        Dictionary<string, FontDiagnosticTextDefinition> definitionsById = options.TextDefinitions.ToDictionary(static item => item.Id, StringComparer.Ordinal);

        foreach (FontDiagnosticTextDefinition definition in options.TextDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DistanceFieldTextRun run = DistanceFieldTextRun.Create(
                definition.Text,
                options.Face,
                canvas.SizePx,
                options.Weight,
                options.Slant);

            Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = await CollectPairAdjustmentsAsync(run, cancellationToken);
            DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(
                run,
                metricsByGlyph,
                CreateRenderOptions(options, canvas),
                pairAdjustments: pairAdjustments);

            FontProofArtifact msdfArtifact = AssertArtifact(proofExport.Artifacts, definition.MsdfPpmFileName);
            string msdfPngPath = Path.Combine(sizeDirectory, definition.MsdfPngFileName);
            RgbaPngWriter.Write(msdfPngPath, msdfArtifact.Image);

            DirectOutlineMaskRenderOptions directOptions = CreateDirectOptions(options, canvas);
            InkMask directMask = DirectOutlineMaskRenderer.RenderMask(outlinesByGlyph, layout, directOptions);
            RgbaImage directImage = directMask.ToImage(
                options.Foreground,
                options.Background,
                showBaselineGuide: true,
                baselineY: canvas.BaselineY,
                baselineGuideColor: options.GridOptions.BaselineColor);
            string directPngPath = Path.Combine(sizeDirectory, definition.DirectOutlinePngFileName);
            RgbaPngWriter.Write(directPngPath, directImage);

            RgbaImage wireframeImage = DirectOutlineMaskRenderer.RenderWireframe(
                outlinesByGlyph,
                layout,
                directOptions,
                options.BoundsOptions.WireframeColor,
                options.Background);
            string wireframePngPath = Path.Combine(sizeDirectory, definition.WireframePngFileName);
            RgbaPngWriter.Write(wireframePngPath, wireframeImage);

            InkMask msdfMask = InkMask.FromImage(
                msdfArtifact.Image,
                new InkMaskExtractionOptions(options.Background, options.GridOptions.BaselineColor));
            FontDiagnosticBounds? directBounds = ConvertBounds(directMask.ComputeBounds());
            FontDiagnosticBounds? msdfBounds = ConvertBounds(msdfMask.ComputeBounds());
            IReadOnlyList<FontDiagnosticBounds> wireframeBounds = ComputeWireframeBounds(layout, proofExport.Snapshot);
            ShapeDiffMetrics diffMetrics = InkMaskDiff.Compare(directMask, msdfMask, canvas.BaselineY);

            scenes.Add(new GeneratedDiagnosticScene(
                new FontDiagnosticScene(
                    definition.Id,
                    definition.Text,
                    canvas.SizePx,
                    canvas.Width,
                    canvas.Height,
                    RoundToInt(canvas.BaselineY),
                    options.Background,
                    options.Foreground,
                    BrowserImage: null,
                    BrowserImagePath: null,
                    DirectImage: directImage,
                    DirectImagePath: directPngPath,
                    MsdfImage: msdfArtifact.Image,
                    MsdfImagePath: msdfPngPath,
                    WireframeImage: wireframeImage,
                    WireframeImagePath: wireframePngPath,
                    BrowserMask: null,
                    DirectMask: directMask,
                    MsdfMask: msdfMask,
                    BrowserBounds: null,
                    DirectBounds: directBounds,
                    MsdfBounds: msdfBounds,
                    GlyphWireframes: wireframeBounds),
                new FontShapeDiff(
                    diffMetrics.IntersectionOverUnion,
                    diffMetrics.MeanEdgeDistance,
                    diffMetrics.P95EdgeDistance,
                    diffMetrics.MaxEdgeDistance,
                    diffMetrics.LeftOnlyArea,
                    diffMetrics.RightOnlyArea,
                    diffMetrics.DeltaLeft,
                    diffMetrics.DeltaTop,
                    diffMetrics.DeltaRight,
                    diffMetrics.DeltaBottom,
                    diffMetrics.DeltaWidth,
                    diffMetrics.DeltaHeight)));
        }

        return new GeneratedDiagnosticSceneSet(scenes, definitionsById);
    }

    private static FontShapeDiffSizeReport BuildShapeDiffSizeReport(
        FontDiagnosticCanvasDefinition canvas,
        GeneratedDiagnosticSceneSet sceneSet)
    {
        return new FontShapeDiffSizeReport(
            canvas.SizePx,
            sceneSet.Scenes[0].Scene.DirectImagePath is null
                ? string.Empty
                : Path.GetDirectoryName(sceneSet.Scenes[0].Scene.DirectImagePath)!,
            canvas.Width,
            canvas.Height,
            canvas.OriginX,
            canvas.BaselineY,
            sceneSet.Scenes.Select(static item => new FontShapeDiffFixtureReport(
                item.Scene.Id,
                item.Scene.Text,
                item.Scene.DirectImagePath,
                item.Scene.MsdfImagePath,
                item.Scene.WireframeImagePath,
                item.Scene.DirectBounds,
                item.Scene.MsdfBounds,
                item.Scene.GlyphWireframes,
                item.Diff)).ToArray());
    }

    private static LayerCompositionArtifactReport BuildCompositionArtifactReport(
        string presetName,
        FontDiagnosticScene scene,
        string artifactPath,
        DiagnosticLayerComposition composition,
        FontDiagnosticExportOptions options)
    {
        IReadOnlyList<LayerCompositionLayerReport> layers = composition.GetOrderedLayers()
            .Select(static layer => new LayerCompositionLayerReport(
                layer.Id,
                layer.Label,
                layer.Visible,
                layer.Opacity,
                layer.ZIndex,
                layer.GetType().Name))
            .ToArray();

        DiagnosticGridLayer? gridLayer = composition.Layers.OfType<DiagnosticGridLayer>().FirstOrDefault();

        return new LayerCompositionArtifactReport(
            presetName,
            scene.SizePx,
            scene.Id,
            scene.Text,
            artifactPath,
            layers,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["browser"] = scene.BrowserImagePath,
                ["direct"] = scene.DirectImagePath,
                ["msdf"] = scene.MsdfImagePath,
                ["wireframe"] = scene.WireframeImagePath,
            },
            gridLayer is null
                ? null
                : new LayerCompositionGridReport(gridLayer.Visible, gridLayer.GridStep, gridLayer.MajorStep, gridLayer.ShowUnitLabels),
            new LayerCompositionBoundsReport(options.BoundsOptions.ShowBounds, options.BoundsOptions.ShowWireframes),
            scene.BrowserImage is null && presetName.Contains("browser", StringComparison.OrdinalIgnoreCase)
                ? "Browser source unavailable. Preset still generated with browser layers hidden and a status label."
                : null);
    }

    private async Task<Dictionary<GlyphKey, GlyphOutline>> LoadOutlinesAsync(
        FontDiagnosticExportOptions options,
        FontDiagnosticCanvasDefinition canvas,
        CancellationToken cancellationToken)
    {
        Dictionary<GlyphKey, GlyphOutline> outlines = [];
        GlyphOutlineLoadOptions loadOptions = new(
            canvas.SizePx,
            0,
            GlyphHintingMode.None,
            normalizeToEm: true);

        IEnumerable<GlyphKey> glyphKeys = options.TextDefinitions
            .SelectMany(definition => DistanceFieldTextRun.Create(
                definition.Text,
                options.Face,
                canvas.SizePx,
                options.Weight,
                options.Slant).GlyphKeys)
            .Distinct();

        foreach (GlyphKey glyphKey in glyphKeys)
        {
            GlyphOutlineLoadResult result = await outlineSource.LoadGlyphOutlineAsync(
                glyphKey.Face,
                glyphKey.Codepoint,
                loadOptions,
                cancellationToken);

            if (!result.Success || result.Outline is null)
            {
                throw new InvalidOperationException($"Failed to load outline for U+{glyphKey.Codepoint:X4} at {canvas.SizePx}px.");
            }

            outlines[glyphKey] = result.Outline;
        }

        return outlines;
    }

    private async Task<Dictionary<GlyphPairKey, GlyphPairAdjustment>> CollectPairAdjustmentsAsync(
        DistanceFieldTextRun run,
        CancellationToken cancellationToken)
    {
        Dictionary<GlyphPairKey, GlyphPairAdjustment> adjustments = [];
        if (pairAdjustmentSource is null)
        {
            return adjustments;
        }

        GlyphKey? previousKey = null;
        bool previousWasWhitespace = true;

        foreach (GlyphKey glyphKey in run.GlyphKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool isWhitespace = Rune.IsWhiteSpace(new Rune(glyphKey.Codepoint));
            if (previousKey is GlyphKey previous && !previousWasWhitespace && !isWhitespace)
            {
                GlyphPairAdjustment? adjustment = await pairAdjustmentSource.GetPairAdjustmentAsync(previous, glyphKey, cancellationToken);
                if (adjustment is not null)
                {
                    adjustments[new GlyphPairKey(previous, glyphKey)] = adjustment;
                }
            }

            previousKey = glyphKey;
            previousWasWhitespace = isWhitespace;
        }

        return adjustments;
    }

    private static IReadOnlyList<FontDiagnosticBounds> ComputeWireframeBounds(
        DistanceFieldTextLayoutResult layout,
        FontAtlasSnapshot snapshot)
    {
        List<FontDiagnosticBounds> bounds = [];
        foreach (DistanceFieldGlyphPlacement placement in layout.Placements)
        {
            if (placement.IsWhitespace || !snapshot.Glyphs.TryGetValue(placement.Key, out GlyphAtlasEntry? entry))
            {
                continue;
            }

            bounds.Add(ComputeDrawBounds(placement, entry));
        }

        return bounds;
    }

    private static FontDiagnosticBounds ComputeDrawBounds(
        DistanceFieldGlyphPlacement placement,
        GlyphAtlasEntry entry)
    {
        double drawX = placement.X + (entry.Placement.PlaneLeft * placement.Scale);
        int outputWidth = Math.Max(1, RoundToInt(entry.Placement.Width * placement.Scale));
        int outputHeight = Math.Max(1, RoundToInt(entry.Placement.Height * placement.Scale));
        int baselineInOutput = ComputeBaselineOffsetInOutput(entry.Placement, outputHeight);
        int drawY = RoundToInt(placement.BaselineY) - baselineInOutput;

        return new FontDiagnosticBounds(
            RoundToInt(drawX),
            drawY,
            RoundToInt(drawX) + outputWidth - 1,
            drawY + outputHeight - 1);
    }

    private static int ComputeBaselineOffsetInOutput(GlyphFieldPlacement placement, int outputHeight)
    {
        double baselineFraction = -placement.PlaneTop / placement.Height;
        return RoundToInt(baselineFraction * outputHeight);
    }

    private static int RoundToInt(double value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static FontDiagnosticBounds? ConvertBounds(InkMaskBounds? bounds)
    {
        return bounds is null
            ? null
            : new FontDiagnosticBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    private static FontProofExportOptions CreateProofOptions(
        FontDiagnosticExportOptions options,
        string outputDirectory,
        FontDiagnosticCanvasDefinition canvas)
    {
        return new FontProofExportOptions(
            outputDirectory,
            $"{options.AtlasName}-{canvas.SizePx}",
            options.Face,
            canvas.SizePx,
            options.Weight,
            options.Slant,
            options.Kind,
            canvas.Width,
            canvas.Height,
            options.FieldWidth,
            options.FieldHeight,
            options.PixelRange,
            options.Foreground,
            options.Background,
            canvas.OriginX,
            canvas.BaselineY,
            ShowBaselineGuide: true,
            BaselineGuideColor: options.GridOptions.BaselineColor,
            FlipY: options.FlipY,
            PageWidth: options.PageWidth,
            PageHeight: options.PageHeight,
            PagePadding: options.PagePadding,
            EdgeColoring: options.EdgeColoring,
            MiterLimit: options.MiterLimit);
    }

    private static DistanceFieldTextRenderOptions CreateRenderOptions(
        FontDiagnosticExportOptions options,
        FontDiagnosticCanvasDefinition canvas)
    {
        return new DistanceFieldTextRenderOptions(
            canvas.Width,
            canvas.Height,
            options.Face,
            canvas.SizePx,
            options.Weight,
            options.Slant,
            options.Kind,
            options.FieldWidth,
            options.FieldHeight,
            options.PixelRange,
            options.Foreground,
            options.Background,
            canvas.OriginX,
            canvas.BaselineY,
            ShowBaselineGuide: true,
            BaselineGuideColor: options.GridOptions.BaselineColor,
            FlipY: options.FlipY,
            PageWidth: options.PageWidth,
            PageHeight: options.PageHeight,
            PagePadding: options.PagePadding,
            EdgeColoring: options.EdgeColoring,
            MiterLimit: options.MiterLimit).Validate();
    }

    private static DirectOutlineMaskRenderOptions CreateDirectOptions(
        FontDiagnosticExportOptions options,
        FontDiagnosticCanvasDefinition canvas)
    {
        return new DirectOutlineMaskRenderOptions(
            canvas.Width,
            canvas.Height,
            options.Foreground,
            options.Background,
            canvas.OriginX,
            canvas.BaselineY,
            Supersample: 4,
            FillRule: OutlineFillRule.EvenOdd,
            CurveSubdivisionCount: 24,
            ShowBaselineGuide: true,
            BaselineGuideColor: options.GridOptions.BaselineColor);
    }

    private static FontAtlasTomlExportMetadata CreateMetadata(FontDiagnosticExportOptions options, double emSize)
    {
        return new FontAtlasTomlExportMetadata(
            options.AtlasName,
            options.Kind.ToString().ToLowerInvariant(),
            options.FontFamilyName,
            options.FontStyleName,
            options.FontPath,
            ComputeFileSha256(options.FontPath),
            options.LicenseIdentifier,
            new FontAtlasMetricsToml
            {
                EmSize = emSize,
                UnitsPerEm = 1000,
                Ascent = emSize * 0.8d,
                Descent = emSize * -0.2d,
                LineGap = 0,
                LineHeight = emSize,
            },
            new FontAtlasMsdfToml
            {
                Range = options.PixelRange,
                Scale = 1,
                EdgeColoring = options.EdgeColoring,
                MiterLimit = options.MiterLimit,
            });
    }

    private static FontProofArtifact AssertArtifact(IReadOnlyList<FontProofArtifact> artifacts, string fileName)
    {
        FontProofArtifact? artifact = artifacts.SingleOrDefault(
            item => string.Equals(Path.GetFileName(item.PpmPath), fileName, StringComparison.OrdinalIgnoreCase));

        return artifact ?? throw new InvalidOperationException($"Expected diagnostic artifact '{fileName}' was not exported.");
    }

    private static string BuildShapeDiffTextReport(FontShapeDiffReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("Machina Font Toolkit M9b shape diff report");
        builder.AppendLine($"fontPath: {report.FontPath}");
        builder.AppendLine($"fontFace: {report.FontFace}");
        builder.AppendLine($"sizes: {string.Join(", ", report.FontSizes.Select(static value => value + "px"))}");
        builder.AppendLine($"texts: {string.Join(" | ", report.Texts)}");
        builder.AppendLine($"geometryReferencePolicy: {report.GeometryReferencePolicy}");
        builder.AppendLine($"browserKerningPolicy: {report.BrowserKerningPolicy}");
        builder.AppendLine($"diagnosticGridPolicy: {report.DiagnosticGridPolicy}");
        builder.AppendLine();

        foreach (FontShapeDiffSizeReport size in report.Sizes)
        {
            builder.AppendLine($"[{size.SizePx}px] canvas={size.CanvasWidth}x{size.CanvasHeight}, x={size.OriginX:0.###}, baselineY={size.BaselineY:0.###}");

            foreach (FontShapeDiffFixtureReport fixture in size.Fixtures)
            {
                builder.AppendLine($"  - {fixture.Text}");
                builder.AppendLine($"    directOutlinePngPath: {fixture.DirectOutlinePngPath}");
                builder.AppendLine($"    msdfPngPath: {fixture.MsdfPngPath}");
                builder.AppendLine($"    wireframePngPath: {fixture.WireframePngPath}");
                builder.AppendLine($"    directBounds: {FormatBounds(fixture.DirectOutlineBounds)}");
                builder.AppendLine($"    msdfBounds: {FormatBounds(fixture.MsdfBounds)}");
                builder.AppendLine($"    wireframes: {fixture.WireframeBounds.Count}");
                builder.AppendLine($"    directVsMsdfIoU: {fixture.DirectVsMsdf.IntersectionOverUnion:0.0000}");
                builder.AppendLine($"    directVsMsdfMeanEdge: {fixture.DirectVsMsdf.MeanEdgeDistance:0.0000}");
                builder.AppendLine($"    directVsMsdfP95Edge: {fixture.DirectVsMsdf.P95EdgeDistance:0.0000}");
                builder.AppendLine($"    directVsMsdfMaxEdge: {fixture.DirectVsMsdf.MaxEdgeDistance:0.0000}");
                builder.AppendLine($"    deltaLeft: {FormatNullable(fixture.DirectVsMsdf.DeltaLeft)}");
                builder.AppendLine($"    deltaTop: {FormatNullable(fixture.DirectVsMsdf.DeltaTop)}");
                builder.AppendLine($"    deltaRight: {FormatNullable(fixture.DirectVsMsdf.DeltaRight)}");
                builder.AppendLine($"    deltaBottom: {FormatNullable(fixture.DirectVsMsdf.DeltaBottom)}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildLayerCompositionTextReport(LayerCompositionReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("Machina Font Toolkit M9b layer composition report");
        builder.AppendLine($"outputDirectory: {report.OutputDirectory}");
        builder.AppendLine($"presetsGenerated: {string.Join(", ", report.PresetsGenerated)}");
        builder.AppendLine();

        foreach (LayerCompositionArtifactReport artifact in report.Artifacts)
        {
            builder.AppendLine($"[{artifact.PresetName}] {artifact.Text} @ {artifact.SizePx}px");
            builder.AppendLine($"artifactPath: {artifact.ArtifactPath}");
            builder.AppendLine($"notes: {artifact.Notes ?? "none"}");
            builder.AppendLine("sourceImagePaths:");
            foreach ((string key, string? value) in artifact.SourceImagePaths.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                builder.AppendLine($"  {key}: {value ?? "not available"}");
            }

            if (artifact.Grid is not null)
            {
                builder.AppendLine($"grid: show={artifact.Grid.ShowGrid.ToString().ToLowerInvariant()}, step={artifact.Grid.GridStep}, majorStep={artifact.Grid.MajorStep}, unitLabels={artifact.Grid.ShowUnitLabels.ToString().ToLowerInvariant()}");
            }
            else
            {
                builder.AppendLine("grid: not included");
            }

            builder.AppendLine($"bounds: show={artifact.Bounds.ShowBounds.ToString().ToLowerInvariant()}, wireframes={artifact.Bounds.ShowWireframes.ToString().ToLowerInvariant()}");
            builder.AppendLine("layers:");
            foreach (LayerCompositionLayerReport layer in artifact.Layers)
            {
                builder.AppendLine($"  - {layer.Id} ({layer.LayerType}) visible={layer.Visible.ToString().ToLowerInvariant()} opacity={layer.Opacity:0.###} zIndex={layer.ZIndex}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatBounds(FontDiagnosticBounds? bounds)
    {
        return bounds is null
            ? "<none>"
            : $"left={bounds.Left}, top={bounds.Top}, right={bounds.Right}, bottom={bounds.Bottom}, width={bounds.Width}, height={bounds.Height}";
    }

    private static string FormatNullable(int? value)
    {
        return value?.ToString() ?? "not available";
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private sealed record GeneratedDiagnosticScene(FontDiagnosticScene Scene, FontShapeDiff Diff);

    private sealed record GeneratedDiagnosticSceneSet(
        IReadOnlyList<GeneratedDiagnosticScene> Scenes,
        IReadOnlyDictionary<string, FontDiagnosticTextDefinition> TextDefinitionsById);
}
