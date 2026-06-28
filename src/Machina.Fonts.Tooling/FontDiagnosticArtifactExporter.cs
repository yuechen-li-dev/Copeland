using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        List<string> warnings = PrepareOutputDirectory(validated, outputDirectory);
        List<string> errors = [];

        FontDiagnosticSourceAvailability initialSourceAvailability = CreateSourceAvailability(
            warnings,
            errors,
            placementReportAvailable: false,
            shapeDiffReportAvailable: false);

        IReadOnlyList<FontDiagnosticPresetAvailabilityReport> initialPresetReports =
            EvaluatePresetAvailability(validated.PresetNames, initialSourceAvailability, validated.AllowPartial);

        List<string> strictErrors = initialPresetReports
            .SelectMany(static report => report.Errors)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToList();

        if (strictErrors.Count > 0)
        {
            errors.AddRange(strictErrors);
            FontDiagnosticExportManifest failedManifest = CreateManifest(
                validated,
                outputDirectory,
                initialSourceAvailability with { Errors = errors.ToArray() },
                initialPresetReports,
                artifacts: Array.Empty<string>(),
                warnings,
                errors);

            string failedManifestJsonPath = Path.Combine(outputDirectory, "font-diagnostic-export-manifest.json");
            string failedManifestTextPath = Path.Combine(outputDirectory, "font-diagnostic-export-manifest.txt");
            WriteTextFile(failedManifestJsonPath, JsonSerializer.Serialize(failedManifest, JsonOptions));
            WriteTextFile(failedManifestTextPath, BuildManifestTextReport(failedManifest));

            throw new InvalidOperationException(
                $"Font diagnostics export failed source validation. {string.Join(" | ", strictErrors)} Manifest: {failedManifestJsonPath}");
        }

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
                    WritePngFile(artifactPath, composedImage);

                    FontDiagnosticPresetAvailabilityReport presetAvailability = initialPresetReports.Single(
                        report => string.Equals(report.PresetName, presetName, StringComparison.OrdinalIgnoreCase));

                    compositionArtifacts.Add(BuildCompositionArtifactReport(
                        presetName,
                        generatedScene.Scene,
                        artifactPath,
                        presetAvailability,
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
        WriteTextFile(shapeDiffReportJsonPath, JsonSerializer.Serialize(shapeDiffReport, JsonOptions));
        WriteTextFile(shapeDiffReportTextPath, BuildShapeDiffTextReport(shapeDiffReport));

        string layerCompositionReportJsonPath = Path.Combine(outputDirectory, "layer-composition-report.json");
        string layerCompositionReportTextPath = Path.Combine(outputDirectory, "layer-composition-report.txt");
        WriteTextFile(layerCompositionReportJsonPath, JsonSerializer.Serialize(compositionReport, JsonOptions));
        WriteTextFile(layerCompositionReportTextPath, BuildLayerCompositionTextReport(compositionReport));

        FontDiagnosticSourceAvailability finalSourceAvailability = CreateSourceAvailability(
            warnings,
            errors,
            placementReportAvailable: true,
            shapeDiffReportAvailable: true);

        string manifestJsonPath = Path.Combine(outputDirectory, "font-diagnostic-export-manifest.json");
        string manifestTextPath = Path.Combine(outputDirectory, "font-diagnostic-export-manifest.txt");
        IReadOnlyList<string> generatedArtifacts = EnumerateGeneratedArtifacts(outputDirectory)
            .Concat(
            [
                Path.GetRelativePath(outputDirectory, manifestJsonPath),
                Path.GetRelativePath(outputDirectory, manifestTextPath),
            ])
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        FontDiagnosticExportManifest manifest = CreateManifest(
            validated,
            outputDirectory,
            finalSourceAvailability,
            EvaluatePresetAvailability(validated.PresetNames, finalSourceAvailability, validated.AllowPartial),
            generatedArtifacts,
            warnings,
            errors);

        WriteTextFile(manifestJsonPath, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteTextFile(manifestTextPath, BuildManifestTextReport(manifest));

        return new FontDiagnosticExportResult(
            outputDirectory,
            shapeDiffReportJsonPath,
            shapeDiffReportTextPath,
            shapeDiffReport,
            layerCompositionReportJsonPath,
            layerCompositionReportTextPath,
            compositionReport,
            manifestJsonPath,
            manifestTextPath,
            manifest);
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
            WritePngFile(msdfPngPath, msdfArtifact.Image);

            DirectOutlineMaskRenderOptions directOptions = CreateDirectOptions(options, canvas);
            InkMask directMask = DirectOutlineMaskRenderer.RenderMask(outlinesByGlyph, layout, directOptions);
            RgbaImage directImage = directMask.ToImage(
                options.Foreground,
                options.Background,
                showBaselineGuide: true,
                baselineY: canvas.BaselineY,
                baselineGuideColor: options.GridOptions.BaselineColor);
            string directPngPath = Path.Combine(sizeDirectory, definition.DirectOutlinePngFileName);
            WritePngFile(directPngPath, directImage);

            RgbaImage wireframeImage = DirectOutlineMaskRenderer.RenderWireframe(
                outlinesByGlyph,
                layout,
                directOptions,
                options.BoundsOptions.WireframeColor,
                options.Background);
            string wireframePngPath = Path.Combine(sizeDirectory, definition.WireframePngFileName);
            WritePngFile(wireframePngPath, wireframeImage);

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
        FontDiagnosticPresetAvailabilityReport presetAvailability,
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
            presetAvailability,
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
        builder.AppendLine("Machina Font Toolkit M9c shape diff report");
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
        builder.AppendLine("Machina Font Toolkit M9c layer composition report");
        builder.AppendLine($"outputDirectory: {report.OutputDirectory}");
        builder.AppendLine($"presetsGenerated: {string.Join(", ", report.PresetsGenerated)}");
        builder.AppendLine();

        foreach (LayerCompositionArtifactReport artifact in report.Artifacts)
        {
            builder.AppendLine($"[{artifact.PresetName}] {artifact.Text} @ {artifact.SizePx}px");
            builder.AppendLine($"artifactPath: {artifact.ArtifactPath}");
            builder.AppendLine($"notes: {artifact.Notes ?? "none"}");
            builder.AppendLine($"presetComplete: {artifact.PresetAvailability.Complete.ToString().ToLowerInvariant()}");
            builder.AppendLine($"missingRequiredSources: {FormatJoinedValues(artifact.PresetAvailability.MissingRequiredSources)}");
            builder.AppendLine($"degradedSources: {FormatJoinedValues(artifact.PresetAvailability.DegradedSources)}");
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

    private static string BuildManifestTextReport(FontDiagnosticExportManifest manifest)
    {
        StringBuilder builder = new();
        builder.AppendLine("Machina Font Toolkit M9c export manifest");
        builder.AppendLine($"format: {manifest.Format}");
        builder.AppendLine($"kind: {manifest.Kind}");
        builder.AppendLine($"milestone: {manifest.Milestone}");
        builder.AppendLine($"outputDirectory: {manifest.OutputDirectory}");
        builder.AppendLine($"presets: {string.Join(", ", manifest.Presets)}");
        builder.AppendLine($"complete: {manifest.Complete.ToString().ToLowerInvariant()}");
        builder.AppendLine($"generatedAtUtc: {manifest.GeneratedAtUtc ?? "<omitted>"}");
        builder.AppendLine("options:");
        builder.AppendLine($"  cleanOutputDirectory: {manifest.Options.CleanOutputDirectory.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  allowPartial: {manifest.Options.AllowPartial.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  gridStep: {manifest.Options.GridStep}");
        builder.AppendLine($"  showGrid: {manifest.Options.ShowGrid.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  showUnitLabels: {manifest.Options.ShowUnitLabels.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  showAxes: {manifest.Options.ShowAxes.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  axisStep: {manifest.Options.AxisStep}");
        builder.AppendLine($"  showBounds: {manifest.Options.ShowBounds.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  showWireframes: {manifest.Options.ShowWireframes.ToString().ToLowerInvariant()}");
        builder.AppendLine("sources:");
        builder.AppendLine($"  browserReferenceAvailable: {manifest.Sources.BrowserReferenceAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  directOutlineAvailable: {manifest.Sources.DirectOutlineAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  msdfAvailable: {manifest.Sources.MsdfAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  browserMaskAvailable: {manifest.Sources.BrowserMaskAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  directMaskAvailable: {manifest.Sources.DirectMaskAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  msdfMaskAvailable: {manifest.Sources.MsdfMaskAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  placementReportAvailable: {manifest.Sources.PlacementReportAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  shapeDiffReportAvailable: {manifest.Sources.ShapeDiffReportAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"warnings: {FormatJoinedValues(manifest.Warnings)}");
        builder.AppendLine($"errors: {FormatJoinedValues(manifest.Errors)}");
        builder.AppendLine("presetReports:");

        foreach (FontDiagnosticPresetAvailabilityReport presetReport in manifest.PresetReports)
        {
            builder.AppendLine($"  - {presetReport.PresetName}: complete={presetReport.Complete.ToString().ToLowerInvariant()} required={FormatJoinedValues(presetReport.RequiredSources)} available={FormatJoinedValues(presetReport.AvailableSources)} missing={FormatJoinedValues(presetReport.MissingRequiredSources)} degraded={FormatJoinedValues(presetReport.DegradedSources)} warnings={FormatJoinedValues(presetReport.Warnings)} errors={FormatJoinedValues(presetReport.Errors)}");
        }

        builder.AppendLine("artifacts:");
        foreach (string artifact in manifest.Artifacts)
        {
            builder.AppendLine($"  - {artifact}");
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

    private static string FormatJoinedValues(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "none"
            : string.Join(", ", values);
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static List<string> PrepareOutputDirectory(FontDiagnosticExportOptions options, string outputDirectory)
    {
        if (options.CleanOutputDirectory)
        {
            ValidateCleanOutputDirectory(options, outputDirectory);
        }

        if (Directory.Exists(outputDirectory))
        {
            if (options.CleanOutputDirectory)
            {
                try
                {
                    Directory.Delete(outputDirectory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    string? lockedPath = TryFindLockedPath(outputDirectory);
                    string target = lockedPath ?? outputDirectory;
                    throw new InvalidOperationException(
                        $"Unable to clean diagnostic output directory '{outputDirectory}'. Locked or inaccessible path: '{target}'.",
                        ex);
                }
            }
            else if (Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                return
                [
                    $"Output directory '{outputDirectory}' already contains files. Existing artifacts may be overwritten and stale files may remain."
                ];
            }
        }

        Directory.CreateDirectory(outputDirectory);
        return [];
    }

    private static void ValidateCleanOutputDirectory(FontDiagnosticExportOptions options, string outputDirectory)
    {
        string normalizedOutputDirectory = NormalizeDirectoryPath(outputDirectory);
        if (string.IsNullOrWhiteSpace(normalizedOutputDirectory))
        {
            throw new InvalidOperationException("Clean export requires a non-empty output directory.");
        }

        string root = NormalizeDirectoryPath(Path.GetPathRoot(normalizedOutputDirectory)!);
        if (string.Equals(normalizedOutputDirectory, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean root directory '{normalizedOutputDirectory}'.");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile)
            && string.Equals(normalizedOutputDirectory, NormalizeDirectoryPath(userProfile), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean user profile root '{normalizedOutputDirectory}'.");
        }

        if (!string.IsNullOrWhiteSpace(options.RepositoryRootDirectory)
            && string.Equals(
                normalizedOutputDirectory,
                NormalizeDirectoryPath(options.RepositoryRootDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean repository root '{normalizedOutputDirectory}'.");
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string? TryFindLockedPath(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return null;
        }

        foreach (string path in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(static item => item, StringComparer.Ordinal))
        {
            try
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return path;
            }
        }

        return null;
    }

    private static FontDiagnosticSourceAvailability CreateSourceAvailability(
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        bool placementReportAvailable,
        bool shapeDiffReportAvailable)
    {
        return new FontDiagnosticSourceAvailability(
            BrowserReferenceAvailable: false,
            DirectOutlineAvailable: true,
            MsdfAvailable: true,
            BrowserMaskAvailable: false,
            DirectMaskAvailable: true,
            MsdfMaskAvailable: true,
            PlacementReportAvailable: placementReportAvailable,
            ShapeDiffReportAvailable: shapeDiffReportAvailable,
            warnings.ToArray(),
            errors.ToArray());
    }

    private static IReadOnlyList<FontDiagnosticPresetAvailabilityReport> EvaluatePresetAvailability(
        IReadOnlyList<string> presetNames,
        FontDiagnosticSourceAvailability sourceAvailability,
        bool allowPartial)
    {
        List<FontDiagnosticPresetAvailabilityReport> reports = [];

        foreach (string presetName in presetNames
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            DiagnosticPresetDefinition preset = LayerPresets.GetPreset(presetName);
            List<string> requiredSources = preset.Requirements.RequiredSources
                .Select(FontDiagnosticSourceCatalog.GetName)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToList();
            List<string> availableSources = preset.Requirements.RequiredSources
                .Where(sourceAvailability.IsAvailable)
                .Select(FontDiagnosticSourceCatalog.GetName)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToList();
            List<string> missingRequiredSources = preset.Requirements.RequiredSources
                .Where(sourceKind => !sourceAvailability.IsAvailable(sourceKind))
                .Select(FontDiagnosticSourceCatalog.GetName)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToList();

            List<string> warnings = [];
            List<string> errors = [];
            List<string> degradedSources = [];

            if (missingRequiredSources.Count > 0)
            {
                if (allowPartial)
                {
                    degradedSources.AddRange(missingRequiredSources);
                    warnings.Add($"Preset '{preset.Name}' degraded because required sources are missing: {string.Join(", ", missingRequiredSources)}.");
                }
                else
                {
                    errors.Add($"Preset '{preset.Name}' requires sources that are unavailable: {string.Join(", ", missingRequiredSources)}.");
                }
            }

            reports.Add(new FontDiagnosticPresetAvailabilityReport(
                preset.Name,
                requiredSources,
                availableSources,
                missingRequiredSources,
                degradedSources
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                warnings
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                errors
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                Complete: missingRequiredSources.Count == 0));
        }

        return reports;
    }

    private static FontDiagnosticExportManifest CreateManifest(
        FontDiagnosticExportOptions options,
        string outputDirectory,
        FontDiagnosticSourceAvailability sourceAvailability,
        IReadOnlyList<FontDiagnosticPresetAvailabilityReport> presetReports,
        IReadOnlyList<string> artifacts,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        List<string> manifestWarnings = warnings
            .Concat(sourceAvailability.Warnings)
            .Concat(presetReports.SelectMany(static report => report.Warnings))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToList();

        List<string> manifestErrors = errors
            .Concat(sourceAvailability.Errors)
            .Concat(presetReports.SelectMany(static report => report.Errors))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToList();

        return new FontDiagnosticExportManifest(
            Format: 1,
            Kind: "machina-font-diagnostic-export",
            Milestone: "M9c",
            OutputDirectory: outputDirectory,
            Presets: options.PresetNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Options: new FontDiagnosticExportManifestOptions(
                options.CleanOutputDirectory,
                options.AllowPartial,
                options.GridOptions.GridStep,
                options.GridOptions.ShowGrid,
                options.GridOptions.ShowUnitLabels,
                options.GridOptions.ShowAxes,
                options.GridOptions.AxisStep,
                options.BoundsOptions.ShowBounds,
                options.BoundsOptions.ShowWireframes),
            Sources: sourceAvailability with
            {
                Warnings = manifestWarnings.ToArray(),
                Errors = manifestErrors.ToArray(),
            },
            PresetReports: presetReports,
            Artifacts: artifacts
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray(),
            Warnings: manifestWarnings.ToArray(),
            Errors: manifestErrors.ToArray(),
            Complete: manifestErrors.Count == 0 && presetReports.All(static report => report.Complete),
            GeneratedAtUtc: options.IncludeTimestamp
                ? DateTimeOffset.UtcNow.ToString("O")
                : null);
    }

    private static IReadOnlyList<string> EnumerateGeneratedArtifacts(string outputDirectory)
    {
        return Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(outputDirectory, path))
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteTextFile(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Unable to write diagnostic artifact '{path}'.", ex);
        }
    }

    private static void WritePngFile(string path, RgbaImage image)
    {
        try
        {
            RgbaPngWriter.Write(path, image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Unable to write diagnostic artifact '{path}'.", ex);
        }
    }

    private sealed record GeneratedDiagnosticScene(FontDiagnosticScene Scene, FontShapeDiff Diff);

    private sealed record GeneratedDiagnosticSceneSet(
        IReadOnlyList<GeneratedDiagnosticScene> Scenes,
        IReadOnlyDictionary<string, FontDiagnosticTextDefinition> TextDefinitionsById);
}
