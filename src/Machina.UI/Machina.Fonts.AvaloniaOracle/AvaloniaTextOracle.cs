using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Skia;
using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.AvaloniaOracle;

public enum AvaloniaReferenceAlignment
{
    Left,
    Center,
    Right,
}

public sealed record AvaloniaTextReferenceRequest(
    string FontPath,
    string Text,
    double FontSize,
    DirectOutlineRect ContentRect,
    AvaloniaReferenceAlignment Alignment = AvaloniaReferenceAlignment.Left,
    double? ExplicitLineHeight = null,
    int OutputWidth = 800,
    int OutputHeight = 180);

public sealed record AvaloniaReferenceAvailability(
    bool GlyphIds,
    bool GlyphClusters,
    bool GlyphOrigins,
    bool GlyphAdvances,
    bool GlyphOffsets,
    bool GlyphInkBounds,
    string TokenAnchorSource);

public sealed record AvaloniaReferenceFont(
    string Sha256,
    string FamilyName,
    string FaceName,
    int UnitsPerEm,
    int Ascender,
    int Descender,
    int LineGap);

public sealed record AvaloniaReferenceGlyph(
    ushort GlyphId,
    int Cluster,
    double OriginX,
    double OriginY,
    double Advance,
    double OffsetX,
    double OffsetY,
    MachinaPlaneBounds? InkBounds,
    int TokenId);

public sealed record AvaloniaReferenceToken(
    int Id,
    MachinaTextTokenKind Kind,
    string Text,
    MachinaTextSpan SourceSpan,
    int? AnchorGlyphIndex,
    double? AnchorOriginX,
    double? AnchorOriginY,
    double AdvanceWidth,
    MachinaPlaneBounds? InkBounds);

public sealed record AvaloniaReferenceLine(
    int Index,
    MachinaTextSpan SourceSpan,
    double Baseline,
    double AdvanceWidth,
    double Height,
    MachinaPlaneBounds LayoutBounds,
    MachinaPlaneBounds? InkBounds);

public sealed record AvaloniaTextReferenceRun(
    AvaloniaReferenceFont Font,
    AvaloniaReferenceAvailability Availability,
    IReadOnlyList<AvaloniaReferenceLine> Lines,
    IReadOnlyList<AvaloniaReferenceToken> Tokens,
    IReadOnlyList<AvaloniaReferenceGlyph> Glyphs,
    string RasterPath,
    double LayoutWidth,
    double LayoutHeight,
    [property: JsonIgnore] RgbaImage RasterImage);

public static class AvaloniaTextOraclePlatform
{
    private static readonly object Gate = new();
    private static bool initialized;

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (initialized)
            {
                return;
            }

            AppBuilder.Configure<Application>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false,
                })
                .SetupWithoutStarting();

            initialized = true;
        }
    }
}

public sealed class AvaloniaTextOracle
{
    public AvaloniaTextReferenceRun CreateReference(AvaloniaTextReferenceRequest request, string rasterPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(rasterPath);

        if (!File.Exists(request.FontPath))
        {
            throw new FileNotFoundException("The exact oracle font file was not found.", request.FontPath);
        }

        AvaloniaTextOraclePlatform.EnsureInitialized();

        string fontHash = ComputeSha256(request.FontPath);
        Uri collectionKey = new($"fonts:machina-text-conformance-{fontHash[..16]}");
        Uri embeddedFontUri = new("avares://Machina.Fonts.AvaloniaOracle/Assets/CrimsonText-Regular.ttf");
        VerifyEmbeddedFontIdentity(embeddedFontUri, fontHash);
        EmbeddedFontCollection collection = new(collectionKey, embeddedFontUri);
        FontManager.Current.AddFontCollection(collection);

        try
        {
            if (collection.Count != 1)
            {
                throw new InvalidOperationException($"Expected one exact font face, but Avalonia loaded {collection.Count}.");
            }

            Typeface typeface = new(
                new FontFamily($"avares://Machina.Fonts.AvaloniaOracle/Assets#{collection[0].Name}"),
                FontStyle.Normal,
                FontWeight.Normal,
                FontStretch.Normal);
            if (!FontManager.Current.TryGetGlyphTypeface(typeface, out IGlyphTypeface? resolvedTypeface))
            {
                throw new InvalidOperationException(
                    $"Avalonia could not resolve embedded family '{collection[0].Name}' from the exact font resource.");
            }

            using TextLayout layout = CreateLayout(request, typeface);
            IReadOnlyList<MachinaTokenPlacement> sourceTokens = MachinaTextTokenizer.Tokenize(request.Text);
            List<AvaloniaReferenceGlyph> glyphs = ExtractGlyphs(layout, request, sourceTokens);
            List<AvaloniaReferenceToken> tokens = ExtractTokens(layout, request, sourceTokens, glyphs);
            List<AvaloniaReferenceLine> lines = ExtractLines(layout, request);

            RgbaImage rasterImage = Render(layout, request, rasterPath);

            FontMetrics metrics = resolvedTypeface.Metrics;
            AvaloniaReferenceFont font = new(
                fontHash,
                resolvedTypeface.FamilyName,
                resolvedTypeface.Style.ToString(),
                metrics.DesignEmHeight,
                -metrics.Ascent,
                metrics.Descent,
                metrics.LineGap);

            return new AvaloniaTextReferenceRun(
                font,
                new AvaloniaReferenceAvailability(
                    GlyphIds: true,
                    GlyphClusters: true,
                    GlyphOrigins: true,
                    GlyphAdvances: true,
                    GlyphOffsets: true,
                    GlyphInkBounds: true,
                    TokenAnchorSource: "first-visible-shaped-glyph-origin"),
                lines,
                tokens,
                glyphs,
                Path.GetFullPath(rasterPath),
                layout.WidthIncludingTrailingWhitespace,
                layout.Height,
                rasterImage);
        }
        finally
        {
            FontManager.Current.RemoveFontCollection(collection.Key);
        }
    }

    private static TextLayout CreateLayout(AvaloniaTextReferenceRequest request, Typeface typeface)
    {
        return new TextLayout(
            request.Text,
            typeface,
            new FontFeatureCollection
            {
                FontFeature.Parse("liga=0"),
                FontFeature.Parse("clig=0"),
            },
            request.FontSize,
            Brushes.White,
            request.Alignment switch
            {
                AvaloniaReferenceAlignment.Left => TextAlignment.Left,
                AvaloniaReferenceAlignment.Center => TextAlignment.Center,
                AvaloniaReferenceAlignment.Right => TextAlignment.Right,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            },
            TextWrapping.NoWrap,
            TextTrimming.None,
            textDecorations: null,
            FlowDirection.LeftToRight,
            request.ContentRect.Width,
            request.ContentRect.Height,
            request.ExplicitLineHeight ?? double.NaN,
            letterSpacing: 0d,
            maxLines: 0,
            textStyleOverrides: null);
    }

    private static List<AvaloniaReferenceGlyph> ExtractGlyphs(
        TextLayout layout,
        AvaloniaTextReferenceRequest request,
        IReadOnlyList<MachinaTokenPlacement> tokens)
    {
        List<AvaloniaReferenceGlyph> glyphs = [];
        double lineTop = request.ContentRect.Y;

        foreach (TextLine line in layout.TextLines)
        {
            foreach (TextRun textRun in line.TextRuns)
            {
                if (textRun is not ShapedTextRun shapedRun)
                {
                    continue;
                }

                GlyphRun glyphRun = shapedRun.GlyphRun;
                double penX = request.ContentRect.X + line.Start + glyphRun.BaselineOrigin.X;
                double baselineY = lineTop + glyphRun.BaselineOrigin.Y;
                double metricScale = request.FontSize / glyphRun.GlyphTypeface.Metrics.DesignEmHeight;

                foreach (GlyphInfo glyphInfo in glyphRun.GlyphInfos)
                {
                    int cluster = NormalizeCluster(glyphInfo.GlyphCluster, line.FirstTextSourceIndex, request.Text.Length);
                    int tokenId = FindTokenId(tokens, cluster);
                    double originX = penX + glyphInfo.GlyphOffset.X;
                    double originY = baselineY + glyphInfo.GlyphOffset.Y;
                    MachinaPlaneBounds? inkBounds = null;

                    if (glyphRun.GlyphTypeface.TryGetGlyphMetrics(glyphInfo.GlyphIndex, out Avalonia.Media.GlyphMetrics glyphMetrics)
                        && glyphMetrics.Width > 0
                        && glyphMetrics.Height > 0)
                    {
                        double left = glyphMetrics.XBearing * metricScale;
                        double top = -glyphMetrics.YBearing * metricScale;
                        inkBounds = new MachinaPlaneBounds(
                            left,
                            top,
                            left + (glyphMetrics.Width * metricScale),
                            top + (glyphMetrics.Height * metricScale));
                    }

                    glyphs.Add(new AvaloniaReferenceGlyph(
                        glyphInfo.GlyphIndex,
                        cluster,
                        originX,
                        originY,
                        glyphInfo.GlyphAdvance,
                        glyphInfo.GlyphOffset.X,
                        glyphInfo.GlyphOffset.Y,
                        inkBounds,
                        tokenId));

                    penX += glyphInfo.GlyphAdvance;
                }
            }

            lineTop += line.Height;
        }

        return glyphs;
    }

    private static List<AvaloniaReferenceToken> ExtractTokens(
        TextLayout layout,
        AvaloniaTextReferenceRequest request,
        IReadOnlyList<MachinaTokenPlacement> sourceTokens,
        IReadOnlyList<AvaloniaReferenceGlyph> glyphs)
    {
        List<AvaloniaReferenceToken> result = [];

        foreach (MachinaTokenPlacement token in sourceTokens)
        {
            List<(AvaloniaReferenceGlyph Glyph, int Index)> tokenGlyphs = glyphs
                .Select(static (glyph, index) => (Glyph: glyph, Index: index))
                .Where(item => item.Glyph.TokenId == token.Id)
                .ToList();
            (AvaloniaReferenceGlyph Glyph, int Index)? anchor = token.Kind == MachinaTextTokenKind.Whitespace || tokenGlyphs.Count == 0
                ? null
                : tokenGlyphs[0];
            double startX = GetTextPositionX(layout, token.SourceSpan.Start, request.ContentRect.X);
            double endX = GetTextPositionX(layout, token.SourceSpan.End, request.ContentRect.X);

            result.Add(new AvaloniaReferenceToken(
                token.Id,
                token.Kind,
                token.Text,
                token.SourceSpan,
                anchor?.Index,
                anchor?.Glyph.OriginX,
                anchor?.Glyph.OriginY,
                Math.Abs(endX - startX),
                UnionInkBounds(tokenGlyphs.Select(static item => item.Glyph))));
        }

        return result;
    }

    private static List<AvaloniaReferenceLine> ExtractLines(TextLayout layout, AvaloniaTextReferenceRequest request)
    {
        List<AvaloniaReferenceLine> lines = [];
        double lineTop = request.ContentRect.Y;

        for (int index = 0; index < layout.TextLines.Count; index++)
        {
            TextLine line = layout.TextLines[index];
            double baseline = lineTop + line.Baseline;
            MachinaPlaneBounds layoutBounds = new(
                request.ContentRect.X + line.Start,
                lineTop,
                request.ContentRect.X + line.Start + line.WidthIncludingTrailingWhitespace,
                lineTop + line.Height);
            MachinaPlaneBounds? inkBounds = line.Extent <= 0d
                ? null
                : new MachinaPlaneBounds(
                    layoutBounds.Left - line.OverhangLeading,
                    lineTop + line.Baseline - line.Extent,
                    layoutBounds.Right + line.OverhangTrailing,
                    lineTop + line.Baseline + line.OverhangAfter);

            lines.Add(new AvaloniaReferenceLine(
                index,
                new MachinaTextSpan(line.FirstTextSourceIndex, line.Length),
                baseline,
                line.WidthIncludingTrailingWhitespace,
                line.Height,
                layoutBounds,
                inkBounds));

            lineTop += line.Height;
        }

        return lines;
    }

    private static RgbaImage Render(TextLayout layout, AvaloniaTextReferenceRequest request, string outputPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using RenderTargetBitmap bitmap = new(
            new PixelSize(request.OutputWidth, request.OutputHeight),
            new Vector(96d, 96d));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            layout.Draw(context, new Point(request.ContentRect.X, request.ContentRect.Y));
        }

        bitmap.Save(outputPath);
        return CopyPixels(bitmap, request.OutputWidth, request.OutputHeight);
    }

    private static RgbaImage CopyPixels(RenderTargetBitmap bitmap, int width, int height)
    {
        int stride = checked(width * 4);
        int byteCount = checked(stride * height);
        IntPtr buffer = Marshal.AllocHGlobal(byteCount);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), buffer, byteCount, stride);
            byte[] bytes = new byte[byteCount];
            Marshal.Copy(buffer, bytes, 0, byteCount);

            RgbaImage image = new(width, height);
            int byteIndex = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte blue = bytes[byteIndex++];
                    byte green = bytes[byteIndex++];
                    byte red = bytes[byteIndex++];
                    byte alpha = bytes[byteIndex++];
                    image.SetPixel(x, y, new Rgba32(red, green, blue, alpha));
                }
            }

            return image;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static double GetTextPositionX(TextLayout layout, int sourceIndex, double originX)
    {
        int clampedIndex = Math.Clamp(sourceIndex, 0, int.MaxValue);
        return originX + layout.HitTestTextPosition(clampedIndex).X;
    }

    private static int NormalizeCluster(int cluster, int lineStart, int textLength)
    {
        if (cluster >= lineStart && cluster < textLength)
        {
            return cluster;
        }

        return Math.Clamp(lineStart + cluster, 0, Math.Max(0, textLength - 1));
    }

    private static int FindTokenId(IReadOnlyList<MachinaTokenPlacement> tokens, int cluster)
    {
        for (int index = 0; index < tokens.Count; index++)
        {
            if (cluster >= tokens[index].SourceSpan.Start && cluster < tokens[index].SourceSpan.End)
            {
                return tokens[index].Id;
            }
        }

        return tokens.Count == 0 ? -1 : tokens[^1].Id;
    }

    private static MachinaPlaneBounds? UnionInkBounds(IEnumerable<AvaloniaReferenceGlyph> glyphs)
    {
        MachinaPlaneBounds? result = null;

        foreach (AvaloniaReferenceGlyph glyph in glyphs)
        {
            if (glyph.InkBounds is null)
            {
                continue;
            }

            MachinaPlaneBounds absolute = new(
                glyph.OriginX + glyph.InkBounds.Left,
                glyph.OriginY + glyph.InkBounds.Top,
                glyph.OriginX + glyph.InkBounds.Right,
                glyph.OriginY + glyph.InkBounds.Bottom);

            result = result is null
                ? absolute
                : new MachinaPlaneBounds(
                    Math.Min(result.Left, absolute.Left),
                    Math.Min(result.Top, absolute.Top),
                    Math.Max(result.Right, absolute.Right),
                    Math.Max(result.Bottom, absolute.Bottom));
        }

        return result;
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void VerifyEmbeddedFontIdentity(Uri embeddedFontUri, string expectedHash)
    {
        using Stream stream = AssetLoader.Open(embeddedFontUri);
        string embeddedHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(embeddedHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The requested font bytes do not match the bounded font embedded in the Avalonia M0 oracle.");
        }
    }
}
