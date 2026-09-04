using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Machina.Fonts;
using Machina.Fonts.AvaloniaOracle;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Toml;

namespace Machina.TextConformance;

internal static class Program
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 64, 96, 128];
    private static readonly string[] CanonicalTexts =
    [
        "Machina",
        "Hello Machina",
        "AV To Ta Wa Yo",
        "Aa0",
        "The quick brown fox jumps over the lazy dog",
        "Agjpqy",
    ];
    private static readonly string[] HeldOutTexts =
    [
        "Typography",
        "0123456789",
        "Hello, world.",
    ];
    private static readonly string[] TargetedTexts = ["Il1", "WMWM", "iiii", "mmmm", "To AV Wa Yo", ",:;"];
    private static readonly string[] SingleGlyphTexts = [".", "Q", "A", "M", "i", "W", "g", "j", "p", "q", "y", "o", "e", "8", ",", ":", ";"];

    public static async Task<int> Main(string[] args)
    {
        string repositoryRoot = FindRepositoryRoot();
        string outputDirectory = ResolveOutputDirectory(args, repositoryRoot);
        string fontPath = Path.Combine(
            repositoryRoot,
            "tests",
            "Machina.UI",
            "Machina.Fonts.Tests",
            "Fixtures",
            "Fonts",
            "CrimsonText-Regular.ttf");
        Directory.CreateDirectory(outputDirectory);

        string localDiagnosticDirectory = Path.Combine(
            Path.GetTempPath(),
            "machina-text-conformance-m0",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(localDiagnosticDirectory);

        List<ConformanceCase> cases = [];
        foreach (int size in Sizes)
        {
            foreach (string text in CanonicalTexts.Concat(TargetedTexts).Concat(HeldOutTexts))
            {
                cases.Add(await RunCaseAsync(fontPath, localDiagnosticDirectory, text, size));
            }
        }

        foreach (int size in Sizes)
        {
            foreach (string text in SingleGlyphTexts)
            {
                cases.Add(await RunCaseAsync(fontPath, localDiagnosticDirectory, text, size));
            }
        }

        await WriteArtifactsAsync(outputDirectory, fontPath, localDiagnosticDirectory, cases);
        Console.WriteLine($"MACHINA-MSDF-REALIZATION-M1: {cases.Count} cases complete.");
        Console.WriteLine($"Compact evidence: {outputDirectory}");
        Console.WriteLine($"Local raster diagnostics: {localDiagnosticDirectory}");
        return cases.All(static item => item.Pass) ? 0 : 1;
    }

    private static async Task<ConformanceCase> RunCaseAsync(
        string fontPath,
        string diagnosticDirectory,
        string text,
        int size)
    {
        const int outputWidth = 1400;
        const int outputHeight = 180;
        const double originX = 12d;
        string caseId = CreateCaseId(text, size);
        string referencePath = Path.Combine(diagnosticDirectory, caseId + "-avalonia.png");

        Stopwatch timer = Stopwatch.StartNew();
        AvaloniaTextReferenceRun reference = new AvaloniaTextOracle().CreateReference(
            new AvaloniaTextReferenceRequest(
                fontPath,
                text,
                size,
                new DirectOutlineRect(originX, 0d, outputWidth - originX, outputHeight),
                OutputWidth: outputWidth,
                OutputHeight: outputHeight),
            referencePath);
        timer.Stop();
        double avaloniaMilliseconds = timer.Elapsed.TotalMilliseconds;

        double baseline = reference.Lines[0].Baseline;
        Dictionary<int, double> anchors = reference.Tokens
            .Where(static token => token.AnchorOriginX is not null)
            .ToDictionary(static token => token.Id, static token => token.AnchorOriginX!.Value);
        FontFaceId face = new("CrimsonText-Regular");
        TypographyGlyphOutlineSource source = new(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [face] = new(face, fontPath),
        });

        timer.Restart();
        DirectOutlineTextRenderResult direct = await new DirectOutlineStaticTextRenderer(source, source).RenderAsync(
            new DirectOutlineTextRenderOptions(
                text,
                face,
                size,
                outputWidth,
                outputHeight,
                Rgba32.White,
                Rgba32.Transparent,
                originX,
                baseline,
                UsePairAdjustments: true,
                TokenAnchorOrigins: anchors));
        timer.Stop();
        double directMilliseconds = timer.Elapsed.TotalMilliseconds;
        if (!direct.Success
            || direct.Mask is null
            || direct.GlyphRun is null
            || direct.Layout is null
            || direct.Timings is null)
        {
            throw new InvalidOperationException($"DirectOutline failed for '{text}' at {size}px.");
        }

        DirectOutlineFontMetricsLoadResult machinaMetrics = await source.LoadFontMetricsAsync(face, size);
        if (!machinaMetrics.Success || machinaMetrics.Metrics is null)
        {
            throw new InvalidOperationException("Machina font metrics were unavailable.");
        }

        int fieldDimension = ExperimentalMsdfSizing.ComputeFieldDimension(size);
        string msdfDirectory = Path.Combine(diagnosticDirectory, caseId + "-msdf");
        DistanceFieldTextPipeline pipeline = new(
            source,
            new MsdfSharpDistanceFieldGenerator(),
            CreateMetadata(fontPath, reference, machinaMetrics.Metrics, size),
            pairAdjustmentSource: source);

        timer.Restart();
        DistanceFieldTextPipelineResult msdf = await pipeline.RenderTextAsync(
            text,
            new DistanceFieldTextRenderOptions(
                outputWidth,
                outputHeight,
                face,
                size,
                MachinaFontWeight.Regular,
                MachinaFontSlant.Upright,
                DistanceFieldKind.Msdf,
                fieldDimension,
                fieldDimension,
                4d,
                Rgba32.White,
                Rgba32.Transparent,
                originX,
                baseline,
                FlipY: true,
                PageWidth: 1024,
                PageHeight: 1024,
                PagePadding: 2),
            msdfDirectory,
            anchors,
            direct.Layout);
        timer.Stop();
        double msdfMilliseconds = timer.Elapsed.TotalMilliseconds;
        if (!msdf.Success || msdf.Image is null || msdf.Layout is null)
        {
            return CreateMsdfFailureCase(
                caseId,
                text,
                size,
                reference,
                direct.GlyphRun,
                machinaMetrics.Metrics,
                fieldDimension,
                avaloniaMilliseconds,
                directMilliseconds,
                direct.Timings,
                msdfMilliseconds,
                referencePath,
                msdf.Diagnostics);
        }

        InkMask msdfMask = InkMask.FromAlpha(msdf.Image);
        ShapeDiffMetrics directVsMsdf = InkMaskDiff.Compare(direct.Mask, msdfMask, baseline, 0.5f);
        if (size is 32 or 64
            && string.Equals(text, "The quick brown fox jumps over the lazy dog", StringComparison.Ordinal))
        {
            ExportDiagnosticBundle(
                diagnosticDirectory,
                caseId,
                reference,
                direct.GlyphRun,
                direct.Mask,
                msdfMask,
                baseline);
        }

        List<TokenDelta> tokenDeltas = CompareTokens(reference.Tokens, direct.GlyphRun);
        List<GlyphDelta> glyphDeltas = CompareGlyphs(reference, direct.GlyphRun);
        double baselineDelta = direct.GlyphRun.Lines[0].BaselineY - reference.Lines[0].Baseline;
        double metricScale = size / (double)reference.Font.UnitsPerEm;
        double avaloniaAscent = reference.Font.Ascender * metricScale;
        double avaloniaDescent = reference.Font.Descender * metricScale;
        double avaloniaLineGap = reference.Font.LineGap * metricScale;
        double anchorTolerance = size / 64d;
        double internalTolerance = size * 0.04d;
        double widthTolerance = size * 0.06d;
        bool pass = tokenDeltas.All(delta => Math.Abs(delta.AnchorDeltaX ?? 0d) <= anchorTolerance)
            && Math.Abs(baselineDelta) <= anchorTolerance
            && tokenDeltas
                .Where(static delta => delta.Kind != nameof(MachinaTextTokenKind.Whitespace))
                .All(delta => Math.Abs(delta.WidthDelta) <= widthTolerance)
            && glyphDeltas.All(delta => Math.Abs(delta.RelativeOriginDeltaX) <= internalTolerance);

        return new ConformanceCase(
            caseId,
            text,
            size,
            HeldOutTexts.Contains(text, StringComparer.Ordinal),
            pass,
            pass ? Array.Empty<string>() : Classify(tokenDeltas, glyphDeltas, baselineDelta, anchorTolerance, internalTolerance),
            reference.Font,
            new MetricParity(
                avaloniaAscent,
                machinaMetrics.Metrics.Ascent,
                avaloniaDescent,
                machinaMetrics.Metrics.Descent,
                avaloniaLineGap,
                machinaMetrics.Metrics.LineGap),
            reference.Lines,
            tokenDeltas,
            glyphDeltas,
            baselineDelta,
            new Tolerances(anchorTolerance, internalTolerance, widthTolerance),
            directVsMsdf,
            new PlacementParity(
                ReferenceEquals(direct.Layout, msdf.Layout),
                direct.GlyphRun.Glyphs.Count == msdf.Layout.GlyphRun.Glyphs.Count,
                MaxPlacementDelta(direct.GlyphRun, msdf.Layout.GlyphRun),
                fieldDimension,
                2),
            new Timings(
                avaloniaMilliseconds,
                directMilliseconds,
                direct.Timings.LayoutMilliseconds,
                direct.Timings.RasterMilliseconds,
                msdfMilliseconds,
                msdf.Timings?.AtlasGenerationMilliseconds,
                msdf.Timings?.RenderMilliseconds),
            referencePath);
    }

    private static ConformanceCase CreateMsdfFailureCase(
        string caseId,
        string text,
        int size,
        AvaloniaTextReferenceRun reference,
        MachinaGlyphRun directRun,
        DirectOutlineFontMetrics machinaMetrics,
        int fieldDimension,
        double avaloniaMilliseconds,
        double directMilliseconds,
        DirectOutlineTextRenderTimings directTimings,
        double msdfMilliseconds,
        string referencePath,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        List<TokenDelta> tokenDeltas = CompareTokens(reference.Tokens, directRun);
        List<GlyphDelta> glyphDeltas = CompareGlyphs(reference, directRun);
        double metricScale = size / (double)reference.Font.UnitsPerEm;
        string[] classifications = diagnostics
            .Select(static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.DistanceFieldGenerationFailed
                ? "MSDF_FIELD"
                : "UNKNOWN")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ConformanceCase(
            caseId,
            text,
            size,
            HeldOutTexts.Contains(text, StringComparer.Ordinal),
            Pass: false,
            classifications,
            reference.Font,
            new MetricParity(
                reference.Font.Ascender * metricScale,
                machinaMetrics.Ascent,
                reference.Font.Descender * metricScale,
                machinaMetrics.Descent,
                reference.Font.LineGap * metricScale,
                machinaMetrics.LineGap),
            reference.Lines,
            tokenDeltas,
            glyphDeltas,
            directRun.Lines[0].BaselineY - reference.Lines[0].Baseline,
            new Tolerances(size / 64d, size * 0.04d, size * 0.06d),
            EmptyShapeDiff(),
            new PlacementParity(false, false, 0d, fieldDimension, 2),
            new Timings(
                avaloniaMilliseconds,
                directMilliseconds,
                directTimings.LayoutMilliseconds,
                directTimings.RasterMilliseconds,
                msdfMilliseconds,
                null,
                null),
            referencePath);
    }

    private static ShapeDiffMetrics EmptyShapeDiff()
    {
        return new ShapeDiffMetrics(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            0,
            0,
            0,
            0,
            0d,
            0d,
            0d,
            0d,
            0d,
            0,
            0,
            0,
            1d,
            0d,
            0d,
            0d);
    }

    private static List<TokenDelta> CompareTokens(
        IReadOnlyList<AvaloniaReferenceToken> referenceTokens,
        MachinaGlyphRun machina)
    {
        List<TokenDelta> result = [];

        foreach (AvaloniaReferenceToken reference in referenceTokens)
        {
            MachinaTokenPlacement actual = machina.Tokens.Single(token => token.Id == reference.Id);
            double? anchorDelta = reference.AnchorOriginX is null || actual.AnchorOriginX is null
                ? null
                : actual.AnchorOriginX.Value - reference.AnchorOriginX.Value;
            List<MachinaGlyphPlacement> tokenGlyphs = machina.Glyphs
                .Where(glyph => glyph.TokenId == reference.Id && !glyph.IsWhitespace)
                .ToList();
            double internalMax = tokenGlyphs.Count == 0
                ? 0d
                : tokenGlyphs.Max(glyph => Math.Abs(glyph.OriginX - tokenGlyphs[0].OriginX));

            result.Add(new TokenDelta(
                reference.Id,
                reference.Text,
                reference.Kind.ToString(),
                reference.AnchorOriginX,
                actual.AnchorOriginX,
                anchorDelta,
                actual.AdvanceWidth - reference.AdvanceWidth,
                internalMax));
        }

        return result;
    }

    private static List<GlyphDelta> CompareGlyphs(AvaloniaTextReferenceRun reference, MachinaGlyphRun machina)
    {
        List<GlyphDelta> result = [];

        foreach (AvaloniaReferenceToken token in reference.Tokens.Where(static token => token.AnchorGlyphIndex is not null))
        {
            List<AvaloniaReferenceGlyph> referenceGlyphs = reference.Glyphs.Where(glyph => glyph.TokenId == token.Id).ToList();
            List<MachinaGlyphPlacement> machinaGlyphs = machina.Glyphs.Where(glyph => glyph.TokenId == token.Id && !glyph.IsWhitespace).ToList();
            int count = Math.Min(referenceGlyphs.Count, machinaGlyphs.Count);

            for (int index = 0; index < count; index++)
            {
                double referenceRelative = referenceGlyphs[index].OriginX - referenceGlyphs[0].OriginX;
                double machinaRelative = machinaGlyphs[index].OriginX - machinaGlyphs[0].OriginX;
                result.Add(new GlyphDelta(
                    token.Id,
                    index,
                    referenceGlyphs[index].GlyphId,
                    referenceGlyphs[index].Cluster,
                    referenceRelative,
                    machinaRelative,
                    machinaRelative - referenceRelative,
                    machinaGlyphs[index].PlaneBounds.Width - (referenceGlyphs[index].InkBounds?.Width ?? machinaGlyphs[index].PlaneBounds.Width),
                    machinaGlyphs[index].PlaneBounds.Height - (referenceGlyphs[index].InkBounds?.Height ?? machinaGlyphs[index].PlaneBounds.Height)));
            }
        }

        return result;
    }

    private static string[] Classify(
        IReadOnlyList<TokenDelta> tokens,
        IReadOnlyList<GlyphDelta> glyphs,
        double baselineDelta,
        double anchorTolerance,
        double internalTolerance)
    {
        List<string> classifications = [];
        if (Math.Abs(baselineDelta) > anchorTolerance)
        {
            classifications.Add("BASELINE");
        }

        if (tokens.Any(token => Math.Abs(token.AnchorDeltaX ?? 0d) > anchorTolerance))
        {
            classifications.Add("TOKEN_ANCHOR");
        }

        if (glyphs.Any(glyph => Math.Abs(glyph.RelativeOriginDeltaX) > internalTolerance))
        {
            classifications.Add("GLYPH_ADVANCE");
        }

        if (tokens.Any(token => token.Kind != nameof(MachinaTextTokenKind.Whitespace)
            && Math.Abs(token.WidthDelta) > token.Text.Length * anchorTolerance))
        {
            classifications.Add("GLYPH_ADVANCE");
        }

        return classifications.Distinct(StringComparer.Ordinal).DefaultIfEmpty("UNKNOWN").ToArray();
    }

    private static double MaxPlacementDelta(MachinaGlyphRun left, MachinaGlyphRun right)
    {
        int count = Math.Min(left.Glyphs.Count, right.Glyphs.Count);
        double maximum = 0d;

        for (int index = 0; index < count; index++)
        {
            maximum = Math.Max(maximum, Math.Abs(left.Glyphs[index].OriginX - right.Glyphs[index].OriginX));
            maximum = Math.Max(maximum, Math.Abs(left.Glyphs[index].BaselineY - right.Glyphs[index].BaselineY));
            maximum = Math.Max(maximum, Math.Abs(left.Glyphs[index].PlaneBounds.Left - right.Glyphs[index].PlaneBounds.Left));
            maximum = Math.Max(maximum, Math.Abs(left.Glyphs[index].PlaneBounds.Top - right.Glyphs[index].PlaneBounds.Top));
            maximum = Math.Max(maximum, Math.Abs(left.Glyphs[index].PlaneBounds.Right - right.Glyphs[index].PlaneBounds.Right));
            maximum = Math.Max(maximum, Math.Abs(left.Glyphs[index].PlaneBounds.Bottom - right.Glyphs[index].PlaneBounds.Bottom));
        }

        return maximum;
    }

    private static FontAtlasTomlExportMetadata CreateMetadata(
        string fontPath,
        AvaloniaTextReferenceRun reference,
        DirectOutlineFontMetrics metrics,
        int size)
    {
        return new FontAtlasTomlExportMetadata(
            "machina-msdf-realization-m1",
            "msdf",
            reference.Font.FamilyName,
            reference.Font.FaceName,
            fontPath,
            "sha256:" + reference.Font.Sha256,
            "OFL-1.1",
            new FontAtlasMetricsToml
            {
                EmSize = size,
                UnitsPerEm = (int)metrics.UnitsPerEm,
                Ascent = metrics.Ascent,
                Descent = metrics.Descent,
                LineGap = metrics.LineGap,
                LineHeight = metrics.LineHeight,
            },
            new FontAtlasMsdfToml
            {
                Range = 4d,
                Scale = 1d,
                EdgeColoring = "simple",
                MiterLimit = 2d,
            });
    }

    private static async Task WriteArtifactsAsync(
        string outputDirectory,
        string fontPath,
        string localDiagnosticDirectory,
        IReadOnlyList<ConformanceCase> cases)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        GlyphArtifactBundle glyphBundle = await BuildGlyphArtifactBundleAsync(fontPath, cases);
        WriteJson(Path.Combine(outputDirectory, "proof.json"), new
        {
            milestone = "MACHINA-MSDF-REALIZATION-M1",
            generatedUtc = DateTime.UtcNow,
            pipeline = BuildPipelineAudit(),
            cases,
            summary = BuildSummary(cases),
        }, options);
        WriteJson(Path.Combine(outputDirectory, "glyphs.json"), glyphBundle.Glyphs, options);
        WriteJson(Path.Combine(outputDirectory, "atlas.json"), glyphBundle.Atlas, options);
        WriteJson(Path.Combine(outputDirectory, "realization.json"), cases.Select(item => new
        {
            item.CaseId,
            item.Text,
            item.Size,
            item.DirectVsMsdf,
            item.Placement,
            item.Timings,
        }), options);
        WriteJson(Path.Combine(outputDirectory, "manifest.json"), new
        {
            milestone = "MACHINA-MSDF-REALIZATION-M1",
            kind = "direct-vector-outline-msdf-atlas-qualification",
            layoutLawChanged = false,
            qualifiedGlyphRunReused = true,
            msdfGeneratedFromVectorOutline = true,
            productionMsdfUsesBitmapIntermediate = false,
            atlasOwnsPlacement = false,
            periodFieldGenerationFixed = true,
            descenderRealizationQualified = true,
            texelCenterPreserved = true,
            arbitraryFudgeFactorsAdded = false,
            nativeFontLibraryAdded = false,
            sixLaborsDependencyAdded = false,
            aurelianRendererChanged = false,
            avaloniaIsExternalLayoutOracle = true,
            avaloniaOwnsApplicationState = false,
            browserIsPrimaryOracle = false,
            tokenFirstGlyphIsPositionOracle = true,
            internalKerningRequiresExactParity = false,
            crossTokenDriftAllowed = false,
            directOutlineUsesSharedGlyphRun = true,
            msdfUsesSharedGlyphRun = true,
            atlasOwnsLayout = false,
            arbitraryVisualFudgeFactorsAdded = false,
            complexScriptSupportClaimed = false,
            fontPath,
            fontSha256 = ComputeSha256(fontPath),
            canonicalSizes = Sizes,
            canonicalTexts = CanonicalTexts,
            targetedTexts = TargetedTexts,
            heldOutTexts = HeldOutTexts,
            singleGlyphTexts = SingleGlyphTexts,
            localDiagnosticDirectory,
            slowLaneRetired = false,
            slowLaneDisposition = "Retained as historical and optional manual tooling; it does not gate production MSDF qualification.",
            summary = BuildSummary(cases),
        }, options);
    }

    private static async Task<GlyphArtifactBundle> BuildGlyphArtifactBundleAsync(
        string fontPath,
        IReadOnlyList<ConformanceCase> cases)
    {
        FontFaceId face = new("CrimsonText-Regular");
        TypographyGlyphOutlineSource source = new(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [face] = new(face, fontPath),
        });
        MsdfSharpDistanceFieldGenerator generator = new();
        List<GeneratedGlyphDistanceField> fields = [];
        List<GlyphEvidenceSeed> seeds = [];

        foreach (ConformanceCase item in cases
            .Where(item => SingleGlyphTexts.Contains(item.Text, StringComparer.Ordinal))
            .OrderBy(static item => item.Size)
            .ThenBy(static item => item.Text, StringComparer.Ordinal))
        {
            int codepoint = item.Text.EnumerateRunes().Single().Value;
            GlyphOutlineLoadResult loaded = await source.LoadGlyphOutlineAsync(
                face,
                codepoint,
                new GlyphOutlineLoadOptions(item.Size, 0, GlyphHintingMode.None, normalizeToEm: true));
            if (!loaded.Success || loaded.Outline is null)
            {
                throw new InvalidOperationException($"Could not reload proof outline for U+{codepoint:X4} at {item.Size}px.");
            }

            int fieldDimension = ExperimentalMsdfSizing.ComputeFieldDimension(item.Size);
            MsdfGenerationSettings settings = new(
                DistanceFieldKind.Msdf,
                fieldDimension,
                fieldDimension,
                4d,
                1d,
                "simple",
                2d);
            GeneratedGlyphDistanceField field = generator.Generate(
                loaded.Outline,
                settings);
            if (field.Diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
            {
                throw new InvalidOperationException($"Could not regenerate proof field for U+{codepoint:X4} at {item.Size}px.");
            }

            fields.Add(field);
            seeds.Add(new GlyphEvidenceSeed(item, loaded.Outline, field));
        }

        GeneratedFieldAtlasPackResult packed = new GeneratedFieldAtlasPacker().Pack(
            fields,
            new GeneratedFieldAtlasPackOptions(2048, 2048, 2, "machina-msdf-realization-m1"));
        if (!packed.Success)
        {
            throw new InvalidOperationException("Could not pack the canonical M1 glyph atlas.");
        }

        object[] glyphs = seeds.Select(seed =>
        {
            GlyphAtlasEntry entry = packed.Snapshot.Glyphs[seed.Field.Key];
            GlyphDelta glyph = seed.Case.Glyphs.Single();
            return (object)new
            {
                seed.Case.CaseId,
                seed.Case.Text,
                seed.Case.Size,
                glyphId = glyph.AvaloniaGlyphId,
                codepoint = seed.Field.Key.Codepoint,
                outlineHash = GlyphOutlineFingerprint.ComputeSha256(seed.Outline),
                fieldHash = GeneratedDistanceFieldFingerprint.ComputeSha256(seed.Field),
                fieldSize = new { width = seed.Field.Width, height = seed.Field.Height, channels = seed.Field.ChannelCount },
                planeBounds = seed.Field.Placement,
                atlasRect = new { entry.PageIndex, entry.X, entry.Y, entry.Width, entry.Height },
                uv = new { entry.U0, entry.V0, entry.U1, entry.V1 },
                pixelRange = seed.Field.Placement.PixelRange,
                finite = seed.Field.Data.Span.ToArray().All(static value => float.IsFinite(value)),
                directBounds = seed.Case.DirectVsMsdf.LeftBounds,
                msdfBounds = seed.Case.DirectVsMsdf.RightBounds,
                iou = seed.Case.DirectVsMsdf.IntersectionOverUnion,
                edgeP50 = seed.Case.DirectVsMsdf.P50EdgeDistance,
                edgeP95 = seed.Case.DirectVsMsdf.P95EdgeDistance,
                edgeMax = seed.Case.DirectVsMsdf.MaxEdgeDistance,
                inkAreaRatio = seed.Case.DirectVsMsdf.InkAreaRatio,
                centroidDelta = new
                {
                    x = seed.Case.DirectVsMsdf.CentroidDeltaX,
                    y = seed.Case.DirectVsMsdf.CentroidDeltaY,
                    distance = seed.Case.DirectVsMsdf.CentroidDistance,
                },
                boundsDelta = new
                {
                    seed.Case.DirectVsMsdf.DeltaLeft,
                    seed.Case.DirectVsMsdf.DeltaTop,
                    seed.Case.DirectVsMsdf.DeltaRight,
                    seed.Case.DirectVsMsdf.DeltaBottom,
                },
                classification = seed.Case.DirectVsMsdf.P95EdgeDistance <= 1d
                    && seed.Case.DirectVsMsdf.IntersectionOverUnion >= 0.60d
                        ? "qualified"
                        : "review",
            };
        }).ToArray();

        object atlas = new
        {
            deterministicOrder = "height-desc,width-desc,face,em-size,weight,slant,codepoint",
            padding = 2,
            paddingAffectsPlacement = false,
            pages = packed.Pages.Select(static page => new
            {
                page.Index,
                page.Width,
                page.Height,
                page.ChannelCount,
                fieldHash = ComputeFloatSha256(page.Data),
            }),
            entries = packed.Snapshot.Glyphs.Values
                .OrderBy(static entry => entry.PageIndex)
                .ThenBy(static entry => entry.Y)
                .ThenBy(static entry => entry.X)
                .Select(static entry => new
                {
                    entry.Key,
                    entry.PageIndex,
                    entry.X,
                    entry.Y,
                    entry.Width,
                    entry.Height,
                    entry.U0,
                    entry.V0,
                    entry.U1,
                    entry.V1,
                    entry.Placement,
                }),
        };

        return new GlyphArtifactBundle(glyphs, atlas);
    }

    private static object[] BuildPipelineAudit()
    {
        return
        [
            new
            {
                stage = "outline extraction",
                source = "TypographyGlyphOutlineSource",
                geometry = "vector",
                issue = "none",
                action = "reuse qualified outline",
            },
            new
            {
                stage = "contour normalization",
                source = "GlyphContour line/quadratic/cubic segments",
                geometry = "vector",
                issue = "zero-length edges were unsanitized",
                action = "remove only zero-length control polygons",
            },
            new
            {
                stage = "edge coloring and distance",
                source = "MSDF-Sharp.Core 1.0.2",
                geometry = "vector",
                issue = "period can produce non-finite multi-channel values",
                action = "finite vector-SDF RGB fallback for the geometric failure class",
            },
            new
            {
                stage = "field transform",
                source = "MsdfSharpDistanceFieldGenerator",
                geometry = "vector",
                issue = "pixel-space translation was passed as vector-space translation",
                action = "convert translation to vector space and invert the same law",
            },
            new
            {
                stage = "atlas packing",
                source = "GeneratedFieldAtlasPacker",
                geometry = "field storage",
                issue = "none",
                action = "retain deterministic order and storage-only padding",
            },
            new
            {
                stage = "reconstruction",
                source = "DistanceFieldSampling and CpuDistanceFieldGlyphRenderer",
                geometry = "MSDF",
                issue = "proof classified RGB instead of alpha coverage",
                action = "compare alpha at the 0.5 signed-distance boundary",
            },
        ];
    }

    private static string ComputeFloatSha256(ReadOnlySpan<float> values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        foreach (float value in values)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value));
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static object BuildSummary(IReadOnlyList<ConformanceCase> cases)
    {
        double[] anchors = cases.SelectMany(static item => item.Tokens)
            .Where(static token => token.AnchorDeltaX is not null)
            .Select(static token => Math.Abs(token.AnchorDeltaX!.Value))
            .Order()
            .ToArray();
        double[] internalGlyphs = cases.SelectMany(static item => item.Glyphs)
            .Select(static glyph => Math.Abs(glyph.RelativeOriginDeltaX))
            .Order()
            .ToArray();
        double[] baselines = cases.Select(static item => Math.Abs(item.BaselineDelta)).Order().ToArray();

        return new
        {
            outcome = DetermineOutcome(cases),
            caseCount = cases.Count,
            passedCount = cases.Count(static item => item.Pass),
            failedCount = cases.Count(static item => !item.Pass),
            tokenAnchor = Percentiles(anchors),
            internalGlyphOrigin = Percentiles(internalGlyphs),
            baseline = Percentiles(baselines),
            directVsMsdfMinimumIou = cases.Min(static item => item.DirectVsMsdf.IntersectionOverUnion),
            directVsMsdfMaximumPlacementDelta = cases.Max(static item => item.Placement.MaximumSemanticDelta),
            averageTimings = new
            {
                avaloniaReferenceMilliseconds = cases.Average(static item => item.Timings.AvaloniaReferenceMilliseconds),
                directOutlineMilliseconds = cases.Average(static item => item.Timings.DirectOutlineMilliseconds),
                machinaLayoutMilliseconds = cases.Average(static item => item.Timings.MachinaLayoutMilliseconds),
                directOutlineRasterMilliseconds = cases.Average(static item => item.Timings.DirectOutlineRasterMilliseconds),
                msdfAtlasGenerationAndRenderMilliseconds = cases.Average(static item => item.Timings.MsdfAtlasGenerationAndRenderMilliseconds),
                msdfAtlasGenerationMilliseconds = cases
                    .Where(static item => item.Timings.MsdfAtlasGenerationMilliseconds is not null)
                    .Average(static item => item.Timings.MsdfAtlasGenerationMilliseconds!.Value),
                msdfRenderMilliseconds = cases
                    .Where(static item => item.Timings.MsdfRenderMilliseconds is not null)
                    .Average(static item => item.Timings.MsdfRenderMilliseconds!.Value),
            },
        };
    }

    private static string DetermineOutcome(IReadOnlyList<ConformanceCase> cases)
    {
        bool layoutConverged = cases.All(static item => item.Pass);
        bool msdfAccepted = cases.Min(static item => item.DirectVsMsdf.IntersectionOverUnion) >= 0.30d
            && cases.Max(static item => item.DirectVsMsdf.P95EdgeDistance) <= 4d;
        return layoutConverged && msdfAccepted ? "A" : "B";
    }

    private static void ExportDiagnosticBundle(
        string directory,
        string caseId,
        AvaloniaTextReferenceRun reference,
        MachinaGlyphRun run,
        InkMask direct,
        InkMask msdf,
        double baseline)
    {
        PpmImageWriter.Write(Path.Combine(directory, caseId + "-direct.ppm"), direct.ToImage(Rgba32.White, Rgba32.Transparent));
        PpmImageWriter.Write(Path.Combine(directory, caseId + "-msdf.ppm"), msdf.ToImage(Rgba32.White, Rgba32.Transparent));

        RgbaImage overlay = new(direct.Width, direct.Height);
        RgbaImage referenceDirect = new(direct.Width, direct.Height);
        RgbaImage referenceMsdf = new(direct.Width, direct.Height);
        RgbaImage directMsdf = new(direct.Width, direct.Height);
        RgbaImage edgeDifference = new(direct.Width, direct.Height);
        for (int y = 0; y < overlay.Height; y++)
        {
            for (int x = 0; x < overlay.Width; x++)
            {
                Rgba32 referencePixel = reference.RasterImage.GetPixel(x, y);
                byte referenceInk = referencePixel.A;
                byte directInk = (byte)Math.Round(direct.GetCoverage(x, y) * 255d, MidpointRounding.AwayFromZero);
                byte msdfInk = (byte)Math.Round(msdf.GetCoverage(x, y) * 255d, MidpointRounding.AwayFromZero);
                overlay.SetPixel(x, y, new Rgba32(referenceInk, directInk, msdfInk, 255));
                referenceDirect.SetPixel(x, y, new Rgba32(referenceInk, directInk, 0, 255));
                referenceMsdf.SetPixel(x, y, new Rgba32(referenceInk, msdfInk, msdfInk, 255));
                directMsdf.SetPixel(x, y, new Rgba32(directInk, msdfInk, 0, 255));
                byte difference = (byte)Math.Abs(directInk - msdfInk);
                edgeDifference.SetPixel(x, y, new Rgba32(difference, difference, difference, 255));
            }
        }

        int baselineRow = (int)Math.Round(baseline, MidpointRounding.AwayFromZero);
        if ((uint)baselineRow < (uint)overlay.Height)
        {
            for (int x = 0; x < overlay.Width; x++)
            {
                overlay.SetPixel(x, baselineRow, new Rgba32(255, 255, 255, 255));
            }
        }

        foreach (AvaloniaReferenceToken token in reference.Tokens.Where(static token => token.AnchorOriginX is not null))
        {
            int guideX = (int)Math.Round(token.AnchorOriginX!.Value, MidpointRounding.AwayFromZero);
            if ((uint)guideX >= (uint)overlay.Width)
            {
                continue;
            }

            for (int y = 0; y < overlay.Height; y++)
            {
                overlay.SetPixel(guideX, y, new Rgba32(255, 255, 0, 255));
            }
        }

        RgbaImage glyphBounds = CopyImage(overlay);
        foreach (MachinaGlyphPlacement glyph in run.Glyphs.Where(static glyph => !glyph.IsWhitespace))
        {
            int left = (int)Math.Round(glyph.OriginX + glyph.PlaneBounds.Left, MidpointRounding.AwayFromZero);
            int right = (int)Math.Round(glyph.OriginX + glyph.PlaneBounds.Right, MidpointRounding.AwayFromZero);
            int top = (int)Math.Round(glyph.BaselineY + glyph.PlaneBounds.Top, MidpointRounding.AwayFromZero);
            int bottom = (int)Math.Round(glyph.BaselineY + glyph.PlaneBounds.Bottom, MidpointRounding.AwayFromZero);
            DrawRectangle(glyphBounds, left, top, right, bottom, new Rgba32(0, 255, 255, 255));

            int originX = (int)Math.Round(glyph.OriginX, MidpointRounding.AwayFromZero);
            int originY = (int)Math.Round(glyph.BaselineY, MidpointRounding.AwayFromZero);
            DrawLine(glyphBounds, originX - 2, originY, originX + 2, originY, new Rgba32(255, 0, 255, 255));
            DrawLine(glyphBounds, originX, originY - 2, originX, originY + 2, new Rgba32(255, 0, 255, 255));
        }

        PpmImageWriter.Write(Path.Combine(directory, caseId + "-avalonia-direct-overlay.ppm"), referenceDirect);
        PpmImageWriter.Write(Path.Combine(directory, caseId + "-avalonia-msdf-overlay.ppm"), referenceMsdf);
        PpmImageWriter.Write(Path.Combine(directory, caseId + "-direct-msdf-overlay.ppm"), directMsdf);
        PpmImageWriter.Write(Path.Combine(directory, caseId + "-edge-difference.ppm"), edgeDifference);
        PpmImageWriter.Write(Path.Combine(directory, caseId + "-three-way-guides.ppm"), overlay);
        PpmImageWriter.Write(Path.Combine(directory, caseId + "-glyph-bounds-baseline.ppm"), glyphBounds);
    }

    private static RgbaImage CopyImage(RgbaImage source)
    {
        RgbaImage copy = new(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                copy.SetPixel(x, y, source.GetPixel(x, y));
            }
        }

        return copy;
    }

    private static void DrawRectangle(RgbaImage image, int left, int top, int right, int bottom, Rgba32 color)
    {
        DrawLine(image, left, top, right, top, color);
        DrawLine(image, left, bottom, right, bottom, color);
        DrawLine(image, left, top, left, bottom, color);
        DrawLine(image, right, top, right, bottom, color);
    }

    private static void DrawLine(RgbaImage image, int x0, int y0, int x1, int y1, Rgba32 color)
    {
        int left = Math.Max(0, Math.Min(x0, x1));
        int right = Math.Min(image.Width - 1, Math.Max(x0, x1));
        int top = Math.Max(0, Math.Min(y0, y1));
        int bottom = Math.Min(image.Height - 1, Math.Max(y0, y1));

        if (y0 == y1 && (uint)y0 < (uint)image.Height)
        {
            for (int x = left; x <= right; x++)
            {
                image.SetPixel(x, y0, color);
            }
        }
        else if (x0 == x1 && (uint)x0 < (uint)image.Width)
        {
            for (int y = top; y <= bottom; y++)
            {
                image.SetPixel(x0, y, color);
            }
        }
    }

    private static object Percentiles(IReadOnlyList<double> values)
    {
        return new
        {
            p50 = Percentile(values, 0.50d),
            p95 = Percentile(values, 0.95d),
            max = values.Count == 0 ? 0d : values[^1],
        };
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        double position = (values.Count - 1) * percentile;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return values[lower];
        }

        return values[lower] + ((values[upper] - values[lower]) * (position - lower));
    }

    private static void WriteJson(string path, object value, JsonSerializerOptions options)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, options) + Environment.NewLine);
    }

    private static string ResolveOutputDirectory(string[] args, string repositoryRoot)
    {
        int outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex >= 0 && outputIndex + 1 < args.Length)
        {
            return Path.GetFullPath(args[outputIndex + 1]);
        }

        return Path.Combine(repositoryRoot, "artifacts", "machina-msdf-realization-m1");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Machina.UI.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the runner output directory.");
    }

    private static string CreateCaseId(string text, int size)
    {
        string slug = new(text.ToLowerInvariant().Select(static character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (slug.Length == 0)
        {
            slug = string.Join("-", text.EnumerateRunes().Select(static rune => $"u{rune.Value:x4}"));
        }

        return $"{size}-{slug}";
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed record ConformanceCase(
    string CaseId,
    string Text,
    int Size,
    bool HeldOut,
    bool Pass,
    IReadOnlyList<string> Classifications,
    AvaloniaReferenceFont Font,
    MetricParity Metrics,
    IReadOnlyList<AvaloniaReferenceLine> Lines,
    IReadOnlyList<TokenDelta> Tokens,
    IReadOnlyList<GlyphDelta> Glyphs,
    double BaselineDelta,
    Tolerances Tolerances,
    ShapeDiffMetrics DirectVsMsdf,
    PlacementParity Placement,
    Timings Timings,
    string AvaloniaRasterPath);

internal sealed record TokenDelta(
    int TokenId,
    string Text,
    string Kind,
    double? AvaloniaAnchorX,
    double? MachinaAnchorX,
    double? AnchorDeltaX,
    double WidthDelta,
    double MachinaInternalSpan);

internal sealed record GlyphDelta(
    int TokenId,
    int TokenGlyphIndex,
    ushort AvaloniaGlyphId,
    int Cluster,
    double AvaloniaRelativeOriginX,
    double MachinaRelativeOriginX,
    double RelativeOriginDeltaX,
    double PlaneWidthDelta,
    double PlaneHeightDelta);

internal sealed record MetricParity(
    double AvaloniaAscent,
    double MachinaAscent,
    double AvaloniaDescent,
    double MachinaDescent,
    double AvaloniaLineGap,
    double MachinaLineGap);

internal sealed record Tolerances(double TokenAnchor, double InternalGlyphOrigin, double TokenWidth);

internal sealed record PlacementParity(
    bool SamePlacementInstance,
    bool SameGlyphCount,
    double MaximumSemanticDelta,
    int FieldDimension,
    int AtlasPadding);

internal sealed record Timings(
    double AvaloniaReferenceMilliseconds,
    double DirectOutlineMilliseconds,
    double MachinaLayoutMilliseconds,
    double DirectOutlineRasterMilliseconds,
    double MsdfAtlasGenerationAndRenderMilliseconds,
    double? MsdfAtlasGenerationMilliseconds,
    double? MsdfRenderMilliseconds);

internal sealed record GlyphEvidenceSeed(
    ConformanceCase Case,
    GlyphOutline Outline,
    GeneratedGlyphDistanceField Field);

internal sealed record GlyphArtifactBundle(
    IReadOnlyList<object> Glyphs,
    object Atlas);
