using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Machina.Fonts;
using Machina.Fonts.AvaloniaOracle;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;

namespace Machina.OutlineConformance;

internal static class Program
{
    private const string Fox = "The quick brown fox jumps over the lazy dog";
    private static readonly int[] PrimarySizes = [64, 96, 128];
    private static readonly string[] HeldOut = ["Machina", "Hello Machina", "AV To Ta Wa Yo", "Agjpqy"];

    public static async Task<int> Main(string[] args)
    {
        string root = FindRepositoryRoot();
        string output = ResolveOutputDirectory(args, root);
        string local = Path.Combine(Path.GetTempPath(), "machina-outline-conformance-m1");
        string fontPath = Path.Combine(root, "tests", "Machina.UI", "Machina.Fonts.Tests", "Fixtures", "Fonts", "CrimsonText-Regular.ttf");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(local);

        List<CaseReport> primary = [];
        foreach (int size in PrimarySizes)
        {
            CaseReport report = await RunCaseAsync(fontPath, Fox, size, local, exportVisuals: true);
            primary.Add(report);
            WriteJson(Path.Combine(output, $"fox-{size}.json"), report);
        }

        File.Copy(
            Path.Combine(local, "fox-96-overlay.svg"),
            Path.Combine(output, "fox-96-outline-overlay.svg"),
            overwrite: true);

        bool primaryPassed = primary.All(static report => report.Pass);
        List<CaseReport> heldOut = [];
        if (primaryPassed)
        {
            foreach (string text in HeldOut)
            {
                heldOut.Add(await RunCaseAsync(fontPath, text, 96, local, exportVisuals: false));
            }
        }

        string outcome = primaryPassed && heldOut.All(static report => report.Pass) ? "A" : "B";
        object proof = new
        {
            milestone = "MACHINA-OUTLINE-CONFORMANCE-M1",
            outcome,
            comparisonSpace = new
            {
                x = "right-positive",
                y = "down-positive",
                origin = "requested line box top-left",
                units = "Avalonia DIPs at 96 DPI; one DIP equals one output pixel",
            },
            featureSettings = new { direction = "LTR", script = "Latin", language = "invariant", kerning = true, liga = false, clig = false },
            primary,
            heldOut,
            summary = BuildSummary(primary.SelectMany(static report => report.Glyphs).ToArray()),
            localDiagnostics = Path.GetFullPath(local),
        };
        WriteJson(Path.Combine(output, "proof.json"), proof);
        WriteJson(Path.Combine(output, "manifest.json"), new
        {
            milestone = "MACHINA-OUTLINE-CONFORMANCE-M1",
            kind = "avalonia-skia-vs-typography-positioned-outline-parity",
            primaryText = Fox,
            minimumSizePx = 64,
            rasterizationInScope = false,
            msdfInScope = false,
            atlasInScope = false,
            shaderWorkInScope = false,
            avaloniaIsExternalReference = true,
            skiaOutlineIsReferenceGeometry = true,
            typographyOutlineIsMachinaGeometry = true,
            naturalLayoutCompared = true,
            tokenResetUsedOnlyForDiagnostics = true,
            arbitraryFudgeFactorsAllowed = false,
            outcome,
        });

        Console.WriteLine($"MACHINA-OUTLINE-CONFORMANCE-M1: Outcome {outcome}");
        Console.WriteLine($"Compact evidence: {Path.GetFullPath(output)}");
        Console.WriteLine($"Local vector diagnostics: {Path.GetFullPath(local)}");
        return outcome == "A" ? 0 : 1;
    }

    private static async Task<CaseReport> RunCaseAsync(
        string fontPath,
        string text,
        int size,
        string localDirectory,
        bool exportVisuals)
    {
        const double originX = 12d;
        AvaloniaTextReferenceRun reference = new AvaloniaTextOracle().CreateGeometryReference(
            new AvaloniaTextReferenceRequest(
                fontPath,
                text,
                size,
                new DirectOutlineRect(originX, 0d, 1800d, 240d),
                OutputWidth: 1900,
                OutputHeight: 260));

        FontFaceId face = new("CrimsonText-Regular");
        TypographyGlyphOutlineSource source = new(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [face] = new(face, fontPath, reference.Font.FaceIndex),
        });
        MachinaRun machina = await CreateMachinaRunAsync(source, face, text, size, originX, reference.Lines[0].Baseline);
        List<GlyphReport> glyphReports = CompareGlyphs(reference, machina, text, size);
        List<TokenReport> tokenReports = CompareTokens(reference, machina.Run);
        Tolerances tolerances = Tolerances.ForSize(size);
        bool pass = glyphReports.All(glyph => glyph.Correspondence == "SAME_GLYPH_ID"
            && Math.Abs(glyph.DeltaOriginX) <= tolerances.Origin
            && Math.Abs(glyph.DeltaOriginY) <= tolerances.Origin
            && Math.Abs(glyph.DeltaAdvance) <= tolerances.Advance
            && glyph.Bounds.MaximumAbsoluteDelta <= tolerances.Bounds
            && glyph.Comparison.HausdorffDistance <= tolerances.Vector);

        if (exportVisuals)
        {
            ExportVisuals(localDirectory, text, size, reference, machina, glyphReports);
        }

        TypographyFontFaceFacts typography = source.GetFaceFacts(face);
        return new CaseReport(
            text,
            size,
            pass,
            new FontReport(
                reference.Font.Sha256,
                reference.Font.FaceIndex,
                reference.Font.FamilyName,
                reference.Font.Subfamily,
                reference.Font.UnitsPerEm,
                reference.Font.Ascender,
                reference.Font.Descender,
                reference.Font.LineGap,
                typography),
            new ScaleReport(size / (double)reference.Font.UnitsPerEm, size / (double)typography.UnitsPerEm, 96d, 1d),
            reference.Lines[0].Baseline,
            machina.Run.Lines[0].BaselineY,
            glyphReports,
            tokenReports,
            BuildSummary(glyphReports),
            tolerances);
    }

    private static async Task<MachinaRun> CreateMachinaRunAsync(
        TypographyGlyphOutlineSource source,
        FontFaceId face,
        string text,
        int size,
        double originX,
        double baseline)
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create(
            text,
            face,
            size,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright);
        GlyphOutlineLoadOptions loadOptions = new(size, 0, GlyphHintingMode.None, normalizeToEm: true);
        Dictionary<GlyphKey, GlyphOutline> outlines = [];
        Dictionary<GlyphKey, GlyphMetrics> metrics = [];
        Dictionary<GlyphPairKey, GlyphPairAdjustment> pairs = [];

        foreach (GlyphKey key in run.GlyphKeys.Distinct())
        {
            GlyphOutlineLoadResult loaded = await source.LoadGlyphOutlineAsync(face, key.Codepoint, loadOptions);
            if (!loaded.Success || loaded.Outline is null)
            {
                throw new InvalidOperationException($"Typography outline extraction failed for U+{key.Codepoint:X4}.");
            }

            outlines[key] = loaded.Outline;
            metrics[key] = loaded.Outline.Metrics;
        }

        GlyphKey? previous = null;
        bool previousWhitespace = true;
        foreach (GlyphKey key in run.GlyphKeys)
        {
            bool whitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
            if (previous is GlyphKey left && !previousWhitespace && !whitespace)
            {
                GlyphPairAdjustment? pair = await source.GetPairAdjustmentAsync(left, key);
                if (pair is not null)
                {
                    pairs[new GlyphPairKey(left, key)] = pair;
                }
            }

            previous = key;
            previousWhitespace = whitespace;
        }

        DistanceFieldTextRenderOptions options = new(
            1900,
            260,
            face,
            size,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            DistanceFieldKind.Msdf,
            1,
            1,
            1d,
            Rgba32.White,
            Rgba32.Transparent,
            originX,
            baseline);
        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(run, metrics, options, pairAdjustments: pairs);
        List<PositionedGlyphOutline> positioned = [];
        foreach (MachinaGlyphPlacement glyph in layout.GlyphRun.Glyphs)
        {
            GlyphOutline outline = outlines[glyph.Key];
            ushort glyphId = source.GetGlyphId(face, glyph.Key.Codepoint);
            positioned.Add(PositionedOutlineGeometry.FromTypography(
                glyphId,
                glyph.SourceSpan,
                outline,
                glyph.OriginX,
                glyph.BaselineY,
                size / (double)source.GetFaceFacts(face).UnitsPerEm));
        }

        return new MachinaRun(layout.GlyphRun, positioned);
    }

    private static List<GlyphReport> CompareGlyphs(
        AvaloniaTextReferenceRun reference,
        MachinaRun machina,
        string text,
        int size)
    {
        List<GlyphReport> reports = [];
        int count = Math.Min(reference.Glyphs.Count, machina.Run.Glyphs.Count);
        for (int index = 0; index < count; index++)
        {
            AvaloniaReferenceGlyph expected = reference.Glyphs[index];
            MachinaGlyphPlacement actual = machina.Run.Glyphs[index];
            PositionedGlyphOutline referenceOutline = reference.Outlines[index];
            PositionedGlyphOutline machinaOutline = machina.Outlines[index];
            OutlineComparisonResult comparison = PositionedOutlineGeometry.Compare(referenceOutline, machinaOutline);
            BoundsDelta bounds = BoundsDelta.Create(referenceOutline.Bounds, machinaOutline.Bounds);
            string correspondence = expected.GlyphId == machinaOutline.GlyphId
                ? "SAME_GLYPH_ID"
                : expected.Cluster == actual.SourceSpan.Start ? "SHAPING_MISMATCH" : "UNMATCHED";
            string classification = Classify(expected, actual, bounds, comparison, size, correspondence);
            string value = expected.Cluster >= 0 && expected.Cluster < text.Length
                ? char.ConvertFromUtf32(char.ConvertToUtf32(text, expected.Cluster))
                : string.Empty;

            reports.Add(new GlyphReport(
                index,
                value,
                expected.Cluster,
                correspondence,
                expected.GlyphId,
                machinaOutline.GlyphId,
                expected.OriginX,
                actual.OriginX,
                actual.OriginX - expected.OriginX,
                expected.OriginY,
                actual.BaselineY,
                actual.BaselineY - expected.OriginY,
                expected.Advance,
                actual.Advance,
                actual.Advance - expected.Advance,
                referenceOutline.Transform.FontUnitsScale,
                machinaOutline.Transform.FontUnitsScale,
                machinaOutline.Transform.FontUnitsScale / referenceOutline.Transform.FontUnitsScale,
                bounds,
                comparison,
                classification));
        }

        return reports;
    }

    private static List<TokenReport> CompareTokens(AvaloniaTextReferenceRun reference, MachinaGlyphRun machina)
    {
        return reference.Tokens.Select(token =>
        {
            MachinaTokenPlacement actual = machina.Tokens.Single(item => item.Id == token.Id);
            return new TokenReport(
                token.Text,
                token.AnchorOriginX,
                actual.AnchorOriginX,
                actual.AnchorOriginX - token.AnchorOriginX,
                token.AdvanceWidth,
                actual.AdvanceWidth,
                actual.AdvanceWidth - token.AdvanceWidth);
        }).ToList();
    }

    private static string Classify(
        AvaloniaReferenceGlyph reference,
        MachinaGlyphPlacement actual,
        BoundsDelta bounds,
        OutlineComparisonResult comparison,
        int size,
        string correspondence)
    {
        Tolerances tolerance = Tolerances.ForSize(size);
        if (correspondence != "SAME_GLYPH_ID") return "SHAPING";
        if (Math.Abs(actual.BaselineY - reference.OriginY) > tolerance.Origin) return "BASELINE";
        if (Math.Abs(actual.OriginX - reference.OriginX) > tolerance.Origin) return "ORIGIN";
        if (Math.Abs(actual.Advance - reference.Advance) > tolerance.Advance) return "ADVANCE";
        if (Math.Abs(comparison.TranslationAndUniformScale.ScaleX - 1d) > 0.0001d
            && comparison.TranslationAndUniformScale.RootMeanSquareResidual < comparison.TranslationOnly.RootMeanSquareResidual * 0.25d) return "FONT_SCALE";
        if (comparison.TranslationOnly.RootMeanSquareResidual < tolerance.Vector && comparison.HausdorffDistance > tolerance.Vector) return "ORIGIN";
        if (bounds.MaximumAbsoluteDelta > tolerance.Bounds) return "COORDINATE_TRANSFORM";
        if (comparison.HausdorffDistance > tolerance.Vector) return "OUTLINE_SOURCE";
        return "MATCH";
    }

    private static object BuildSummary(IReadOnlyList<GlyphReport> glyphs)
    {
        return new
        {
            origin = Distribution(glyphs.Select(static glyph => Math.Abs(glyph.DeltaOriginX)).ToArray()),
            advance = Distribution(glyphs.Select(static glyph => Math.Abs(glyph.DeltaAdvance)).ToArray()),
            bounds = Distribution(glyphs.Select(static glyph => glyph.Bounds.MaximumAbsoluteDelta).ToArray()),
            vectorResidual = Distribution(glyphs.Select(static glyph => glyph.Comparison.HausdorffDistance).ToArray()),
            classifications = glyphs.GroupBy(static glyph => glyph.Classification).ToDictionary(static group => group.Key, static group => group.Count()),
        };
    }

    private static DistributionReport Distribution(double[] values)
    {
        if (values.Length == 0) return new DistributionReport(0d, 0d, 0d);
        Array.Sort(values);
        return new DistributionReport(Percentile(values, 0.50d), Percentile(values, 0.95d), values[^1]);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static void ExportVisuals(
        string directory,
        string text,
        int size,
        AvaloniaTextReferenceRun reference,
        MachinaRun machina,
        IReadOnlyList<GlyphReport> reports)
    {
        string prefix = Path.Combine(directory, $"fox-{size}");
        SvgWriter.Write(prefix + "-overlay.svg", reference.Outlines, machina.Outlines, reference.Lines[0].Baseline, reference.Tokens, null);
        int worst = reports.Select(static (report, index) => (report, index))
            .OrderByDescending(static item => item.report.Comparison.HausdorffDistance)
            .First().index;
        SvgWriter.Write(prefix + "-worst-crop.svg", [reference.Outlines[worst]], [machina.Outlines[worst]], reference.Lines[0].Baseline, [], reports[worst].Value);
        SvgWriter.Write(
            prefix + "-translation-fit.svg",
            [reference.Outlines[worst]],
            [SvgWriter.Transform(machina.Outlines[worst], reports[worst].Comparison.TranslationOnly)],
            reference.Lines[0].Baseline,
            [],
            reports[worst].Value + " translation fit");
        SvgWriter.Write(
            prefix + "-translation-scale-fit.svg",
            [reference.Outlines[worst]],
            [SvgWriter.Transform(machina.Outlines[worst], reports[worst].Comparison.TranslationAndUniformScale)],
            reference.Lines[0].Baseline,
            [],
            reports[worst].Value + " translation + scale fit");

        HashSet<char> cropCharacters = ['T', 'q', 'u', 'i', 'c', 'k', 'b', 'r', 'o', 'w', 'n', 'f', 'x', 'g', 'y'];
        foreach ((GlyphReport report, int index) in reports.Select(static (report, index) => (report, index)))
        {
            if (report.Value.Length == 1 && cropCharacters.Contains(report.Value[0]))
            {
                SvgWriter.Write(
                    Path.Combine(directory, $"fox-{size}-glyph-{report.Index:D2}-{Slug(report.Value)}.svg"),
                    [reference.Outlines[index]],
                    [machina.Outlines[index]],
                    reference.Lines[0].Baseline,
                    [],
                    report.Value);
            }
        }
    }

    private static string ResolveOutputDirectory(string[] args, string root)
    {
        string? option = args.FirstOrDefault(static arg => arg.StartsWith("--output=", StringComparison.Ordinal));
        return option is null
            ? Path.Combine(root, "artifacts", "machina-outline-conformance-m1")
            : Path.GetFullPath(option["--output=".Length..]);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Machina.UI.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string Slug(string value)
    {
        string slug = new(value.Where(char.IsLetterOrDigit).ToArray());
        return slug.Length == 0 ? "text" : slug.ToLowerInvariant();
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }) + Environment.NewLine);
    }

    private sealed record MachinaRun(MachinaGlyphRun Run, IReadOnlyList<PositionedGlyphOutline> Outlines);
    private sealed record FontReport(string Sha256, int FaceIndex, string Family, string Subfamily, int UnitsPerEm, int Ascender, int Descender, int LineGap, TypographyFontFaceFacts Typography);
    private sealed record ScaleReport(double Avalonia, double Machina, double Dpi, double PixelsPerDip);
    private sealed record CaseReport(string Text, int SizePx, bool Pass, FontReport Font, ScaleReport Scale, double ReferenceBaseline, double MachinaBaseline, IReadOnlyList<GlyphReport> Glyphs, IReadOnlyList<TokenReport> Tokens, object Summary, Tolerances Tolerances);
    private sealed record GlyphReport(int Index, string Value, int Cluster, string Correspondence, ushort ReferenceGlyphId, ushort MachinaGlyphId, double ReferenceOriginX, double MachinaOriginX, double DeltaOriginX, double ReferenceOriginY, double MachinaOriginY, double DeltaOriginY, double ReferenceAdvance, double MachinaAdvance, double DeltaAdvance, double ReferenceScale, double MachinaScale, double ScaleRatio, BoundsDelta Bounds, OutlineComparisonResult Comparison, string Classification);
    private sealed record TokenReport(string Token, double? ReferenceAnchorX, double? MachinaAnchorX, double? DeltaX, double ReferenceWidth, double MachinaWidth, double DeltaWidth);
    private sealed record DistributionReport(double P50, double P95, double Max);
    private sealed record Tolerances(double Origin, double Advance, double Bounds, double Vector)
    {
        public static Tolerances ForSize(int size) => new(Math.Max(0.002d, size * 0.00005d), Math.Max(0.002d, size * 0.00005d), Math.Max(0.004d, size * 0.0001d), Math.Max(0.006d, size * 0.00015d));
    }

    private sealed record BoundsDelta(double Left, double Top, double Right, double Bottom, double Width, double Height, double MaximumAbsoluteDelta)
    {
        public static BoundsDelta Create(MachinaPlaneBounds reference, MachinaPlaneBounds actual)
        {
            double left = actual.Left - reference.Left;
            double top = actual.Top - reference.Top;
            double right = actual.Right - reference.Right;
            double bottom = actual.Bottom - reference.Bottom;
            double width = actual.Width - reference.Width;
            double height = actual.Height - reference.Height;
            return new BoundsDelta(left, top, right, bottom, width, height, new[] { left, top, right, bottom, width, height }.Max(Math.Abs));
        }
    }
}

internal static class SvgWriter
{
    public static PositionedGlyphOutline Transform(PositionedGlyphOutline outline, OutlineFit fit)
    {
        IReadOnlyList<GlyphContour> contours = outline.Contours.Select(contour => new GlyphContour(
            contour.Segments.Select(segment => TransformSegment(segment, fit)).ToArray())).ToArray();
        return PositionedOutlineGeometry.FromComparisonSpace(
            outline.GlyphId,
            outline.SourceSpan,
            contours,
            outline.Transform.FontUnitsScale * fit.ScaleX,
            outline.Transform.LocalOffsetX,
            outline.Transform.LocalOffsetY,
            (outline.Transform.GlyphOriginX * fit.ScaleX) + fit.TranslateX,
            (outline.Transform.GlyphOriginY * fit.ScaleY) + fit.TranslateY,
            (outline.Transform.BaselineY * fit.ScaleY) + fit.TranslateY,
            outline.Transform.YAxisLaw + "; diagnostic fit applied");
    }

    public static void Write(
        string path,
        IReadOnlyList<PositionedGlyphOutline> reference,
        IReadOnlyList<PositionedGlyphOutline> machina,
        double baseline,
        IReadOnlyList<AvaloniaReferenceToken> tokens,
        string? label)
    {
        MachinaPlaneBounds bounds = Union(reference.Concat(machina));
        double margin = Math.Max(8d, bounds.Height * 0.15d);
        double left = bounds.Left - margin;
        double top = bounds.Top - margin;
        double width = Math.Max(1d, bounds.Width + (2d * margin));
        double height = Math.Max(1d, bounds.Height + (2d * margin));
        StringBuilder svg = new();
        svg.AppendLine(FormattableString.Invariant($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{left:F4} {top:F4} {width:F4} {height:F4}\">"));
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#101418\"/>");
        svg.AppendLine(FormattableString.Invariant($"<line x1=\"{left:F4}\" y1=\"{baseline:F4}\" x2=\"{left + width:F4}\" y2=\"{baseline:F4}\" stroke=\"#d8d8d8\" stroke-width=\"0.35\" opacity=\"0.7\"/>"));
        foreach (AvaloniaReferenceToken token in tokens.Where(static token => token.AnchorOriginX is not null))
        {
            svg.AppendLine(FormattableString.Invariant($"<line x1=\"{token.AnchorOriginX:F4}\" y1=\"{top:F4}\" x2=\"{token.AnchorOriginX:F4}\" y2=\"{top + height:F4}\" stroke=\"#ffe66d\" stroke-width=\"0.25\" opacity=\"0.45\"/>"));
        }

        WriteOutlines(svg, reference, "#00e5ff", 0.9d);
        WriteOutlines(svg, machina, "#ff365e", 0.35d);
        foreach (PositionedGlyphOutline outline in reference)
        {
            svg.AppendLine(FormattableString.Invariant($"<path d=\"M {outline.Transform.GlyphOriginX - 1:F4} {outline.Transform.BaselineY:F4} H {outline.Transform.GlyphOriginX + 1:F4} M {outline.Transform.GlyphOriginX:F4} {outline.Transform.BaselineY - 1:F4} V {outline.Transform.BaselineY + 1:F4}\" stroke=\"#ffffff\" stroke-width=\"0.3\"/>"));
        }

        if (!string.IsNullOrEmpty(label))
        {
            svg.AppendLine(FormattableString.Invariant($"<text x=\"{left + 2:F4}\" y=\"{top + 5:F4}\" fill=\"#ffffff\" font-size=\"4\">{System.Security.SecurityElement.Escape(label)}</text>"));
        }

        svg.AppendLine("</svg>");
        File.WriteAllText(path, svg.ToString());
    }

    private static void WriteOutlines(
        StringBuilder svg,
        IReadOnlyList<PositionedGlyphOutline> outlines,
        string color,
        double strokeWidth)
    {
        foreach (PositionedGlyphOutline outline in outlines)
        {
            svg.Append("<path d=\"");
            foreach (GlyphContour contour in outline.Contours)
            {
                if (contour.Segments.Count == 0) continue;
                GlyphPoint start = Start(contour.Segments[0]);
                svg.Append(FormattableString.Invariant($"M {start.X:F4} {start.Y:F4} "));
                foreach (GlyphOutlineSegment segment in contour.Segments)
                {
                    switch (segment)
                    {
                        case GlyphLineSegment line:
                            svg.Append(FormattableString.Invariant($"L {line.P1.X:F4} {line.P1.Y:F4} "));
                            break;
                        case GlyphQuadraticSegment quadratic:
                            svg.Append(FormattableString.Invariant($"Q {quadratic.P1.X:F4} {quadratic.P1.Y:F4} {quadratic.P2.X:F4} {quadratic.P2.Y:F4} "));
                            break;
                        case GlyphCubicSegment cubic:
                            svg.Append(FormattableString.Invariant($"C {cubic.P1.X:F4} {cubic.P1.Y:F4} {cubic.P2.X:F4} {cubic.P2.Y:F4} {cubic.P3.X:F4} {cubic.P3.Y:F4} "));
                            break;
                    }
                }
                svg.Append("Z ");
            }
            svg.AppendLine(FormattableString.Invariant(
                $"\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{strokeWidth:F2}\" opacity=\"0.95\"/>"));
        }
    }

    private static GlyphPoint Start(GlyphOutlineSegment segment) => segment switch
    {
        GlyphLineSegment line => line.P0,
        GlyphQuadraticSegment quadratic => quadratic.P0,
        GlyphCubicSegment cubic => cubic.P0,
        _ => throw new InvalidOperationException(),
    };

    private static GlyphOutlineSegment TransformSegment(GlyphOutlineSegment segment, OutlineFit fit)
    {
        GlyphPoint Apply(GlyphPoint point) => new(
            (point.X * fit.ScaleX) + fit.TranslateX,
            (point.Y * fit.ScaleY) + fit.TranslateY);

        return segment switch
        {
            GlyphLineSegment line => new GlyphLineSegment(Apply(line.P0), Apply(line.P1)),
            GlyphQuadraticSegment quadratic => new GlyphQuadraticSegment(Apply(quadratic.P0), Apply(quadratic.P1), Apply(quadratic.P2)),
            GlyphCubicSegment cubic => new GlyphCubicSegment(Apply(cubic.P0), Apply(cubic.P1), Apply(cubic.P2), Apply(cubic.P3)),
            _ => throw new InvalidOperationException(),
        };
    }

    private static MachinaPlaneBounds Union(IEnumerable<PositionedGlyphOutline> outlines)
    {
        PositionedGlyphOutline[] values = outlines.Where(static outline => outline.Contours.Count > 0).ToArray();
        return values.Length == 0
            ? new MachinaPlaneBounds(0d, 0d, 1d, 1d)
            : new MachinaPlaneBounds(
                values.Min(static item => item.Bounds.Left),
                values.Min(static item => item.Bounds.Top),
                values.Max(static item => item.Bounds.Right),
                values.Max(static item => item.Bounds.Bottom));
    }
}
