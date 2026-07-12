namespace Machina.Standard.Text;

public static class MachinaTextLayoutEngine
{
    private const string BulletMarkerText = "\u2022 ";
    private const double BulletIndentStep = 16;

    public static MachinaTextLayoutResult Layout(
        MachinaTextSpec spec,
        MachinaTextBox box,
        IMachinaTextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var parseResult = MachinaTextParser.Parse(spec.Source);
        return Layout(parseResult.Document, MachinaTextPolicy.FromSpec(spec), box, measurer, parseResult.Diagnostics);
    }

    public static MachinaTextLayoutResult Layout(
        MachinaTextSpec spec,
        MachinaTextBox box,
        Machina.Core.Measurement.ITextMeasurer textMeasurer)
    {
        ArgumentNullException.ThrowIfNull(textMeasurer);
        return Layout(spec, box, MachinaTextMeasurers.FromCore(textMeasurer));
    }

    public static MachinaTextLayoutResult Layout(
        MachinaTextDocument document,
        MachinaTextPolicy policy,
        MachinaTextBox box,
        IMachinaTextMeasurer? measurer = null)
    {
        return Layout(document, policy, box, measurer, []);
    }

    public static MachinaTextLayoutResult Layout(
        MachinaTextDocument document,
        MachinaTextPolicy policy,
        MachinaTextBox box,
        Machina.Core.Measurement.ITextMeasurer textMeasurer)
    {
        ArgumentNullException.ThrowIfNull(textMeasurer);
        return Layout(document, policy, box, MachinaTextMeasurers.FromCore(textMeasurer), []);
    }

    private static MachinaTextLayoutResult Layout(
        MachinaTextDocument document,
        MachinaTextPolicy policy,
        MachinaTextBox box,
        IMachinaTextMeasurer? measurer,
        IReadOnlyList<MachinaTextDiagnostic> parseDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(parseDiagnostics);

        measurer ??= MachinaTextMeasurers.Deterministic;

        var diagnostics = new List<MachinaTextLayoutDiagnostic>();
        var variantMetrics = ResolveVariantMetrics(policy.Variant);
        var lineHeight = variantMetrics.FontSize * ResolveLeading(policy.Leading, variantMetrics.DefaultLeading);

        if (policy.Overflow is MachinaTextOverflow.Ellipsis or MachinaTextOverflow.Scroll)
        {
            diagnostics.Add(new MachinaTextLayoutDiagnostic(
                MachinaTextLayoutDiagnosticCode.UnsupportedOverflow,
                $"Overflow mode '{policy.Overflow}' is not implemented in M6c; layout reports clip-style overflow only."));
        }

        var hasOverflow = false;

        if (box.Width <= 0 || box.Height <= 0)
        {
            hasOverflow = true;
            diagnostics.Add(new MachinaTextLayoutDiagnostic(
                MachinaTextLayoutDiagnosticCode.BoxTooSmall,
                "Text box must have positive width and height."));
        }

        var provisionalLines = new List<ProvisionalLine>();
        var blockLineIndices = new Dictionary<int, int>();

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            if (block is ParagraphBlock paragraph)
            {
                var lines = LayoutParagraph(blockIndex, paragraph.Inline, policy, measurer, box.Width, 0, 0, diagnostics, ref hasOverflow);
                provisionalLines.AddRange(AssignLineIndices(lines, blockIndex, blockLineIndices));
            }
            else if (block is BulletListBlock bulletList)
            {
                LayoutBulletList(
                    blockIndex,
                    bulletList,
                    0,
                    policy,
                    measurer,
                    box.Width,
                    lineHeight,
                    provisionalLines,
                    blockLineIndices,
                    diagnostics,
                    ref hasOverflow);
            }
            else
            {
                diagnostics.Add(new MachinaTextLayoutDiagnostic(
                    MachinaTextLayoutDiagnosticCode.UnsupportedInline,
                    $"Unsupported text block type '{block.GetType().Name}' was skipped."));
            }

            if (blockIndex < document.Blocks.Count - 1 && provisionalLines.Count > 0)
            {
                provisionalLines[^1].GapAfter += policy.BlockGap;
            }
        }

        var contentHeight = CalculateContentHeight(provisionalLines, lineHeight);
        var startY = ResolveStartY(box, policy.VerticalAlign, contentHeight);

        if (contentHeight > box.Height)
        {
            hasOverflow = true;
        }

        var finalizedLines = FinalizeLines(box, policy.Align, provisionalLines, lineHeight, startY, ref hasOverflow);
        var allRuns = finalizedLines.SelectMany(line => line.Runs).ToArray();
        var contentBounds = CalculateContentBounds(box, finalizedLines);

        if (hasOverflow && diagnostics.All(diagnostic => diagnostic.Code != MachinaTextLayoutDiagnosticCode.ContentOverflow))
        {
            diagnostics.Add(new MachinaTextLayoutDiagnostic(
                MachinaTextLayoutDiagnosticCode.ContentOverflow,
                "Text content exceeds the assigned text box."));
        }

        return new MachinaTextLayoutResult(
            box,
            contentBounds,
            finalizedLines,
            allRuns,
            hasOverflow,
            diagnostics.ToArray(),
            parseDiagnostics.ToArray());
    }

    private static IReadOnlyList<ProvisionalLine> LayoutParagraph(
        int blockIndex,
        IReadOnlyList<MachinaInline> inline,
        MachinaTextPolicy policy,
        IMachinaTextMeasurer measurer,
        double boxWidth,
        double firstLineOffsetX,
        double continuationOffsetX,
        List<MachinaTextLayoutDiagnostic> diagnostics,
        ref bool hasOverflow)
    {
        var fragments = FlattenInline(inline, new InlineStyleState(policy.Variant), diagnostics);
        return LayoutFragments(blockIndex, fragments, policy, measurer, boxWidth, firstLineOffsetX, continuationOffsetX, ref hasOverflow);
    }

    private static void LayoutBulletList(
        int blockIndex,
        BulletListBlock bulletList,
        int depth,
        MachinaTextPolicy policy,
        IMachinaTextMeasurer measurer,
        double boxWidth,
        double lineHeight,
        List<ProvisionalLine> lines,
        Dictionary<int, int> blockLineIndices,
        List<MachinaTextLayoutDiagnostic> diagnostics,
        ref bool hasOverflow)
    {
        for (var itemIndex = 0; itemIndex < bulletList.Items.Count; itemIndex++)
        {
            var item = bulletList.Items[itemIndex];
            var markerStyle = new MachinaTextRunStyle(policy.Variant, false, false, false, null);
            var markerWidth = measurer.Measure(BulletMarkerText, policy.Variant, markerStyle).Width;
            var baseOffset = depth * BulletIndentStep;
            var continuationOffset = baseOffset + markerWidth;

            var itemFragments = FlattenInline(item.Inline, new InlineStyleState(policy.Variant), diagnostics);
            itemFragments.Insert(0, new FlattenedFragment(new TextRun(BulletMarkerText), BulletMarkerText, markerStyle, policy.Variant));

            var itemLines = LayoutFragments(
                blockIndex,
                itemFragments,
                policy,
                measurer,
                boxWidth,
                baseOffset,
                continuationOffset,
                ref hasOverflow);

            lines.AddRange(AssignLineIndices(itemLines, blockIndex, blockLineIndices));

            if (item.Children is { Count: > 0 })
            {
                LayoutBulletItems(
                    blockIndex,
                    item.Children,
                    depth + 1,
                    policy,
                    measurer,
                    boxWidth,
                    lineHeight,
                    lines,
                    blockLineIndices,
                    diagnostics,
                    ref hasOverflow);
            }

            if (itemIndex < bulletList.Items.Count - 1 && lines.Count > 0)
            {
                lines[^1].GapAfter += policy.ListGap;
            }
        }
    }

    private static void LayoutBulletItems(
        int blockIndex,
        IReadOnlyList<MachinaBulletItem> items,
        int depth,
        MachinaTextPolicy policy,
        IMachinaTextMeasurer measurer,
        double boxWidth,
        double lineHeight,
        List<ProvisionalLine> lines,
        Dictionary<int, int> blockLineIndices,
        List<MachinaTextLayoutDiagnostic> diagnostics,
        ref bool hasOverflow)
    {
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            var markerStyle = new MachinaTextRunStyle(policy.Variant, false, false, false, null);
            var markerWidth = measurer.Measure(BulletMarkerText, policy.Variant, markerStyle).Width;
            var baseOffset = depth * BulletIndentStep;
            var continuationOffset = baseOffset + markerWidth;

            var itemFragments = FlattenInline(item.Inline, new InlineStyleState(policy.Variant), diagnostics);
            itemFragments.Insert(0, new FlattenedFragment(new TextRun(BulletMarkerText), BulletMarkerText, markerStyle, policy.Variant));

            var itemLines = LayoutFragments(
                blockIndex,
                itemFragments,
                policy,
                measurer,
                boxWidth,
                baseOffset,
                continuationOffset,
                ref hasOverflow);

            lines.AddRange(AssignLineIndices(itemLines, blockIndex, blockLineIndices));

            if (item.Children is { Count: > 0 })
            {
                LayoutBulletItems(
                    blockIndex,
                    item.Children,
                    depth + 1,
                    policy,
                    measurer,
                    boxWidth,
                    lineHeight,
                    lines,
                    blockLineIndices,
                    diagnostics,
                    ref hasOverflow);
            }

            if (itemIndex < items.Count - 1 && lines.Count > 0)
            {
                lines[^1].GapAfter += policy.ListGap;
            }
        }
    }

    private static IReadOnlyList<ProvisionalLine> AssignLineIndices(
        IReadOnlyList<ProvisionalLine> lines,
        int blockIndex,
        IDictionary<int, int> blockLineIndices)
    {
        if (!blockLineIndices.TryGetValue(blockIndex, out var nextIndex))
        {
            nextIndex = 0;
        }

        foreach (var line in lines)
        {
            line.LineIndex = nextIndex;
            nextIndex++;
        }

        blockLineIndices[blockIndex] = nextIndex;
        return lines;
    }

    private static List<FlattenedFragment> FlattenInline(
        IReadOnlyList<MachinaInline> inline,
        InlineStyleState state,
        List<MachinaTextLayoutDiagnostic> diagnostics)
    {
        var fragments = new List<FlattenedFragment>();

        foreach (var node in inline)
        {
            switch (node)
            {
                case TextRun textRun:
                    fragments.Add(new FlattenedFragment(
                        textRun,
                        textRun.Text,
                        new MachinaTextRunStyle(state.Variant, state.Strong, state.Emphasis, state.Code, state.LinkHref),
                        state.Variant));
                    break;

                case CodeRun codeRun:
                    fragments.Add(new FlattenedFragment(
                        codeRun,
                        codeRun.Text,
                        new MachinaTextRunStyle(MachinaTextVariant.Mono, state.Strong, state.Emphasis, true, state.LinkHref),
                        MachinaTextVariant.Mono));
                    break;

                case StrongRun strongRun:
                    fragments.AddRange(FlattenInline(
                        strongRun.Children,
                        state with { Strong = true },
                        diagnostics));
                    break;

                case EmphasisRun emphasisRun:
                    fragments.AddRange(FlattenInline(
                        emphasisRun.Children,
                        state with { Emphasis = true },
                        diagnostics));
                    break;

                case LinkRun linkRun:
                    fragments.AddRange(FlattenInline(
                        linkRun.Children,
                        state with { LinkHref = linkRun.Href },
                        diagnostics));
                    break;

                default:
                    diagnostics.Add(new MachinaTextLayoutDiagnostic(
                        MachinaTextLayoutDiagnosticCode.UnsupportedInline,
                        $"Unsupported inline type '{node.GetType().Name}' was skipped."));
                    break;
            }
        }

        return fragments;
    }

    private static IReadOnlyList<ProvisionalLine> LayoutFragments(
        int blockIndex,
        IReadOnlyList<FlattenedFragment> fragments,
        MachinaTextPolicy policy,
        IMachinaTextMeasurer measurer,
        double boxWidth,
        double firstLineOffsetX,
        double continuationOffsetX,
        ref bool hasOverflow)
    {
        var lines = new List<ProvisionalLine>();

        if (fragments.Count == 0)
        {
            lines.Add(new ProvisionalLine(blockIndex, firstLineOffsetX, Math.Max(0, boxWidth - firstLineOffsetX)));
            return lines;
        }

        if (policy.Wrap == MachinaTextWrap.None)
        {
            lines.Add(BuildNoWrapLine(blockIndex, fragments, measurer, boxWidth, firstLineOffsetX));
            return lines;
        }

        var tokens = TokenizeFragments(fragments, measurer);
        var currentLine = new ProvisionalLine(blockIndex, firstLineOffsetX, Math.Max(0, boxWidth - firstLineOffsetX));

        foreach (var token in tokens)
        {
            if (token.IsWhitespace && currentLine.Runs.Count == 0)
            {
                continue;
            }

            var tokenWidth = token.Width;
            if (token.IsWhitespace && currentLine.Runs.Count > 0)
            {
                if (currentLine.Width + tokenWidth <= currentLine.AvailableWidth)
                {
                    AddTokenRuns(currentLine, token);
                }

                continue;
            }

            if (currentLine.Width + tokenWidth <= currentLine.AvailableWidth || currentLine.Runs.Count == 0)
            {
                AddTokenRuns(currentLine, token);

                if (currentLine.Width > currentLine.AvailableWidth)
                {
                    hasOverflow = true;
                }

                continue;
            }

            TrimTrailingWhitespace(currentLine);
            lines.Add(currentLine);

            currentLine = new ProvisionalLine(blockIndex, continuationOffsetX, Math.Max(0, boxWidth - continuationOffsetX));

            if (token.IsWhitespace)
            {
                continue;
            }

            AddTokenRuns(currentLine, token);

            if (currentLine.Width > currentLine.AvailableWidth)
            {
                hasOverflow = true;
            }

            continue;
        }

        TrimTrailingWhitespace(currentLine);
        lines.Add(currentLine);

        foreach (var line in lines)
        {
            if (line.Width > line.AvailableWidth)
            {
                hasOverflow = true;
            }
        }

        return lines;
    }

    private static ProvisionalLine BuildNoWrapLine(
        int blockIndex,
        IReadOnlyList<FlattenedFragment> fragments,
        IMachinaTextMeasurer measurer,
        double boxWidth,
        double baseOffsetX)
    {
        var line = new ProvisionalLine(blockIndex, baseOffsetX, Math.Max(0, boxWidth - baseOffsetX));

        foreach (var fragment in fragments)
        {
            var width = measurer.Measure(fragment.Text, fragment.Variant, fragment.Style).Width;
            line.AddRun(new ProvisionalRun(fragment.Source, fragment.Text, fragment.Style, width));
        }

        return line;
    }

    private static IReadOnlyList<LineToken> TokenizeFragments(
        IReadOnlyList<FlattenedFragment> fragments,
        IMachinaTextMeasurer measurer)
    {
        var tokens = new List<LineToken>();
        var currentSegments = new List<TokenSegment>();
        var currentWidth = 0d;
        var currentWhitespace = false;

        foreach (var fragment in fragments)
        {
            if (fragment.Text.Length == 0)
            {
                continue;
            }

            var buffer = new System.Text.StringBuilder();
            bool? bufferWhitespace = null;

            foreach (var character in fragment.Text)
            {
                var isWhitespace = char.IsWhiteSpace(character);

                if (bufferWhitespace is null)
                {
                    bufferWhitespace = isWhitespace;
                }
                else if (bufferWhitespace.Value != isWhitespace)
                {
                    AppendTokenSegment(fragment, buffer.ToString(), bufferWhitespace.Value, measurer, currentSegments, ref currentWidth, ref currentWhitespace, tokens);
                    buffer.Clear();
                    bufferWhitespace = isWhitespace;
                }

                buffer.Append(character);
            }

            if (buffer.Length > 0 && bufferWhitespace is not null)
            {
                AppendTokenSegment(fragment, buffer.ToString(), bufferWhitespace.Value, measurer, currentSegments, ref currentWidth, ref currentWhitespace, tokens);
            }
        }

        if (currentSegments.Count > 0)
        {
            tokens.Add(new LineToken(currentWhitespace, currentSegments.ToArray(), currentWidth));
        }

        return tokens;
    }

    private static void AppendTokenSegment(
        FlattenedFragment fragment,
        string text,
        bool isWhitespace,
        IMachinaTextMeasurer measurer,
        List<TokenSegment> currentSegments,
        ref double currentWidth,
        ref bool currentWhitespace,
        List<LineToken> tokens)
    {
        if (currentSegments.Count > 0 && currentWhitespace != isWhitespace)
        {
            tokens.Add(new LineToken(currentWhitespace, currentSegments.ToArray(), currentWidth));
            currentSegments.Clear();
            currentWidth = 0;
        }

        currentWhitespace = isWhitespace;
        var width = measurer.Measure(text, fragment.Variant, fragment.Style).Width;
        currentSegments.Add(new TokenSegment(fragment.Source, text, fragment.Style, width));
        currentWidth += width;
    }

    private static void AddTokenRuns(ProvisionalLine line, LineToken token)
    {
        foreach (var segment in token.Segments)
        {
            line.AddRun(new ProvisionalRun(segment.Source, segment.Text, segment.Style, segment.Width));
        }
    }

    private static void TrimTrailingWhitespace(ProvisionalLine line)
    {
        while (line.Runs.Count > 0 && string.IsNullOrWhiteSpace(line.Runs[^1].Text))
        {
            line.RemoveLastRun();
        }
    }

    private static IReadOnlyList<MachinaTextLineBox> FinalizeLines(
        MachinaTextBox box,
        MachinaTextAlign align,
        IReadOnlyList<ProvisionalLine> provisionalLines,
        double lineHeight,
        double startY,
        ref bool hasOverflow)
    {
        var lines = new List<MachinaTextLineBox>(provisionalLines.Count);
        var currentY = startY;

        foreach (var provisionalLine in provisionalLines)
        {
            var availableWidth = Math.Max(0, box.Width - provisionalLine.BaseOffsetX);
            if (provisionalLine.Width > availableWidth)
            {
                hasOverflow = true;
            }

            var alignedX = ResolveLineX(box.X + provisionalLine.BaseOffsetX, availableWidth, provisionalLine.Width, align);
            var lineBounds = new MachinaTextBox(alignedX, currentY, provisionalLine.Width, lineHeight);
            var runBoxes = new List<MachinaTextRunBox>(provisionalLine.Runs.Count);
            var currentX = alignedX;

            foreach (var run in provisionalLine.Runs)
            {
                runBoxes.Add(new MachinaTextRunBox(
                    run.Source,
                    run.Text,
                    new MachinaTextBox(currentX, currentY, run.Width, lineHeight),
                    run.Style));
                currentX += run.Width;
            }

            lines.Add(new MachinaTextLineBox(
                provisionalLine.BlockIndex,
                provisionalLine.LineIndex,
                lineBounds,
                runBoxes.ToArray()));

            currentY += lineHeight + provisionalLine.GapAfter;
        }

        return lines;
    }

    private static double ResolveLineX(double boxX, double availableWidth, double lineWidth, MachinaTextAlign align)
    {
        return align switch
        {
            MachinaTextAlign.Start => boxX,
            MachinaTextAlign.Center => boxX + ((availableWidth - lineWidth) / 2d),
            MachinaTextAlign.End => boxX + (availableWidth - lineWidth),
            _ => throw new ArgumentOutOfRangeException(nameof(align), align, "Unsupported alignment."),
        };
    }

    private static double ResolveStartY(MachinaTextBox box, MachinaTextVerticalAlign verticalAlign, double contentHeight)
    {
        return verticalAlign switch
        {
            MachinaTextVerticalAlign.Top => box.Y,
            MachinaTextVerticalAlign.Center => box.Y + ((box.Height - contentHeight) / 2d),
            MachinaTextVerticalAlign.Bottom => box.Y + (box.Height - contentHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(verticalAlign), verticalAlign, "Unsupported vertical alignment."),
        };
    }

    private static double CalculateContentHeight(IReadOnlyList<ProvisionalLine> lines, double lineHeight)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        var totalHeight = 0d;
        foreach (var line in lines)
        {
            totalHeight += lineHeight + line.GapAfter;
        }

        return totalHeight - lines[^1].GapAfter;
    }

    private static MachinaTextBox CalculateContentBounds(MachinaTextBox box, IReadOnlyList<MachinaTextLineBox> lines)
    {
        if (lines.Count == 0)
        {
            return new MachinaTextBox(box.X, box.Y, 0, 0);
        }

        var minX = lines.Min(line => line.Bounds.X);
        var minY = lines.Min(line => line.Bounds.Y);
        var maxX = lines.Max(line => line.Bounds.X + line.Bounds.Width);
        var maxY = lines.Max(line => line.Bounds.Y + line.Bounds.Height);

        return new MachinaTextBox(minX, minY, maxX - minX, maxY - minY);
    }

    private static VariantMetrics ResolveVariantMetrics(MachinaTextVariant variant)
    {
        return variant switch
        {
            MachinaTextVariant.Body => new VariantMetrics(14, 1.4),
            MachinaTextVariant.Label => new VariantMetrics(12, 1.3),
            MachinaTextVariant.Caption => new VariantMetrics(11, 1.25),
            MachinaTextVariant.Title => new VariantMetrics(18, 1.25),
            MachinaTextVariant.Mono => new VariantMetrics(12, 1.35),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unsupported Machina text variant."),
        };
    }

    private static double ResolveLeading(MachinaTextLeading leading, double defaultLeading)
    {
        return leading.Kind switch
        {
            MachinaTextLeadingKind.Normal or 0 => defaultLeading,
            MachinaTextLeadingKind.Tight => 1.15,
            MachinaTextLeadingKind.Loose => 1.6,
            MachinaTextLeadingKind.Numeric => leading.Value,
            _ => defaultLeading,
        };
    }

    private sealed record VariantMetrics(double FontSize, double DefaultLeading);

    private sealed record InlineStyleState(
        MachinaTextVariant Variant,
        bool Strong = false,
        bool Emphasis = false,
        bool Code = false,
        string? LinkHref = null);

    private sealed record FlattenedFragment(
        MachinaInline Source,
        string Text,
        MachinaTextRunStyle Style,
        MachinaTextVariant Variant);

    private sealed record TokenSegment(
        MachinaInline Source,
        string Text,
        MachinaTextRunStyle Style,
        double Width);

    private sealed record LineToken(
        bool IsWhitespace,
        IReadOnlyList<TokenSegment> Segments,
        double Width);

    private sealed class ProvisionalRun(
        MachinaInline source,
        string text,
        MachinaTextRunStyle style,
        double width)
    {
        public MachinaInline Source { get; } = source;

        public string Text { get; } = text;

        public MachinaTextRunStyle Style { get; } = style;

        public double Width { get; } = width;
    }

    private sealed class ProvisionalLine(int blockIndex, double baseOffsetX, double availableWidth)
    {
        public int BlockIndex { get; } = blockIndex;

        public int LineIndex { get; set; }

        public double BaseOffsetX { get; } = baseOffsetX;

        public double GapAfter { get; set; }

        public List<ProvisionalRun> Runs { get; } = [];

        public double Width { get; private set; }

        public double AvailableWidth { get; } = availableWidth;

        public void AddRun(ProvisionalRun run)
        {
            Runs.Add(run);
            Width += run.Width;
        }

        public void RemoveLastRun()
        {
            var run = Runs[^1];
            Runs.RemoveAt(Runs.Count - 1);
            Width -= run.Width;
        }
    }
}
