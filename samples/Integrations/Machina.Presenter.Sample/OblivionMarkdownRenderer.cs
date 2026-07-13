using System.Collections.Concurrent;
using System.Text;
using Copeland.Markdown;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class OblivionMarkdownRenderer
{
    private const double PreviewLineHeight = 18;
    private const double PreviewGap = 6;
    private const int PreviewSummaryLineLimit = 3;
    private const double BlockGap = 12;
    private const double InlineGap = 10;
    private const double HeadingLabelWidth = 28;
    private const double ListMarkerWidth = 24;
    private const double CodePadding = 10;
    private const double CodeHeaderHeight = 18;
    private const double CodeLineHeight = 16;
    private const double CodeLineGap = 4;
    private const double ExpandedScrollbarWidth = 8;
    private const double ExpandedScrollbarGap = 8;
    private const double ExpandedPlainLineHeight = 18;
    private const double ExpandedPlainLineGap = 6;
    private static readonly ColorToken PreviewFrameBackground = ColorToken.Hex(0x0B1220FF);
    private static readonly ColorToken PreviewFrameBorder = ColorToken.Hex(0x334155FF);
    private static readonly ColorToken PreviewForeground = ColorToken.Hex(0xE2E8F0FF);
    private static readonly ColorToken PreviewMutedForeground = ColorToken.Hex(0xCBD5E1FF);
    private static readonly PresenterCardTextLayout PreviewTextLayout = new(
        LineHeight: PreviewLineHeight,
        LineGap: PreviewGap);
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> RawMarkdownSourceLinesCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<OblivionRawMarkdownSourceLayoutCacheKey, OblivionPreparedRawMarkdownSourceLayout> RawMarkdownSourceLayoutCache = new();
    private static readonly ConcurrentDictionary<string, int> RawMarkdownSourceLayoutBuildCountBySource = new(StringComparer.Ordinal);
    private static int RawMarkdownSourceLayoutBuildCount;

    public sealed record OblivionScrollableCodeSurfaceRenderResult(
        UiNode Node,
        double ContentHeight,
        ScrollbarGeometry Scrollbar);

    public sealed record OblivionMarkdownRendererDiagnostics(
        int RawMarkdownSourceLayoutBuildCount,
        int RawMarkdownSourceLayoutCacheEntryCount);

    public static UiNode BuildPreviewBody(
        string id,
        OblivionCardBody body,
        StandardTheme theme,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(theme);

        if (body.DocumentMir is null)
        {
            return BuildPlainPreviewBody(id, body.PreviewLines, theme, width, height);
        }

        List<UiNode> children = [];
        double currentTop = 0;
        int renderedLineCount = 0;
        int maxLineCount = PresenterCardLayoutHelper.ComputeLineCapacity(height, PreviewTextLayout);

        foreach (MarkdownPreviewEntry entry in BuildPreviewEntries(body.DocumentMir, body.Diagnostics))
        {
            if (renderedLineCount >= maxLineCount)
            {
                break;
            }

            int remainingLineCount = maxLineCount - renderedLineCount;
            IReadOnlyList<string> visibleLines = WrapPreviewEntry(
                entry,
                width,
                remainingLineCount);

            for (int lineIndex = 0; lineIndex < visibleLines.Count && renderedLineCount < maxLineCount; lineIndex++)
            {
                children.Add(
                    UI.Anchor(
                        UI.Text(
                            visibleLines[lineIndex],
                            id: $"{id}.preview-{entry.Kind.ToString().ToLowerInvariant()}-{renderedLineCount}",
                            size: entry.Kind == MarkdownPreviewEntryKind.Heading ? TextSize.Md : TextSize.Sm,
                            color: GetPreviewColor(entry.Kind)),
                        id: $"{id}.preview-{entry.Kind.ToString().ToLowerInvariant()}-{renderedLineCount}.slot",
                        left: 0,
                        right: 0,
                        top: currentTop,
                        height: PreviewLineHeight));
                currentTop += PreviewLineHeight + PreviewGap;
                renderedLineCount += 1;
            }
        }

        return BuildPreviewFrame(id, children);
    }

    public static double MeasureExpandedContentHeight(OblivionCardBody body, double width)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.DocumentMir is null)
        {
            IReadOnlyList<string> lines = body.PreviewLines.Count == 0 ? ["<empty>"] : body.PreviewLines;
            return MeasureWrappedPlainTextHeight(lines, width);
        }

        return MeasureMarkdownDocumentHeight(body.DocumentMir, width);
    }

    public static OblivionExpandedMarkdownBodyRenderResult BuildExpandedBody(
        string id,
        OblivionCardBody body,
        OblivionMarkdownReadingStyle style,
        double width,
        double viewportHeight,
        double requestedScrollOffset)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(style);

        double initialContentHeight = MeasureExpandedContentHeight(body, width);
        bool needsScrollbar = initialContentHeight > viewportHeight;
        double contentWidth = needsScrollbar
            ? Math.Max(120, width - ExpandedScrollbarWidth - ExpandedScrollbarGap)
            : width;
        double contentHeight = MeasureExpandedContentHeight(body, contentWidth);
        ScrollbarGeometry scrollbar = PresenterScrollRegion.ComputeScrollbarGeometry(
            new Machina.Layout.Geometry.Rect(
                contentWidth + ExpandedScrollbarGap,
                0,
                ExpandedScrollbarWidth,
                viewportHeight),
            contentHeight,
            viewportHeight,
            requestedScrollOffset);

        List<UiNode> children = [];
        double scrollOffset = scrollbar.ScrollOffset;

        if (body.DocumentMir is null)
        {
            double currentTop = -scrollOffset;
            IReadOnlyList<string> wrappedLines = PresenterCardLayoutHelper.WrapOrClipLinesToFit(
                body.PreviewLines.Count == 0 ? ["<empty markdown body>"] : body.PreviewLines,
                contentWidth,
                Math.Max(contentHeight, viewportHeight) + scrollOffset + 64,
                new PresenterCardTextLayout(style.BodyLineHeight, style.BodyLineGap),
                new TextStyle(
                    Color: style.Foreground,
                    Size: TextSize.Sm,
                    AlignX: TextAlignX.Left,
                    AlignY: TextAlignY.Top));

            foreach ((string line, int index) in wrappedLines.Select((value, index) => (value, index)))
            {
                if (!IntersectsViewport(currentTop, style.BodyLineHeight, viewportHeight))
                {
                    currentTop += style.BodyLineHeight + style.BodyLineGap;
                    continue;
                }

                children.Add(
                    UI.Anchor(
                        UI.Text(
                            line,
                            id: $"{id}.plain-expanded-{index}",
                            size: TextSize.Sm,
                            color: style.Foreground),
                        id: $"{id}.plain-expanded-{index}.slot",
                        left: 0,
                        width: contentWidth,
                        top: currentTop,
                        height: style.BodyLineHeight));
                currentTop += style.BodyLineHeight + style.BodyLineGap;
            }
        }
        else
        {
            double currentTop = -scrollOffset;
            foreach ((DocumentBlockMir block, int index) in body.DocumentMir.Blocks.Select((value, index) => (value, index)))
            {
                MarkdownRenderedBlock rendered = LowerBlock($"{id}.expanded.block-{index}", block, style, contentWidth);
                if (!IntersectsViewport(currentTop, rendered.Height, viewportHeight))
                {
                    currentTop += rendered.Height + BlockGap;
                    continue;
                }

                children.Add(
                    UI.Anchor(
                        rendered.Node,
                        id: $"{id}.expanded.block-{index}.slot",
                        left: 0,
                        width: contentWidth,
                        top: currentTop,
                        height: rendered.Height));
                currentTop += rendered.Height + BlockGap;
            }
        }

        if (scrollbar.IsVisible)
        {
            children.Add(
                UI.Anchor(
                    UI.Rect(
                        id: $"{id}.scrollbar-track",
                        style: new UiStyle(
                            Background: style.ScrollbarTrack)),
                    id: $"{id}.scrollbar-track.slot",
                    left: scrollbar.TrackRect.X,
                    width: scrollbar.TrackRect.Width,
                    top: scrollbar.TrackRect.Y,
                    height: scrollbar.TrackRect.Height));
            children.Add(
                UI.Anchor(
                    UI.Rect(
                        id: $"{id}.scrollbar-thumb",
                        style: new UiStyle(
                            Background: style.ScrollbarThumb)),
                    id: $"{id}.scrollbar-thumb.slot",
                    left: scrollbar.ThumbRect.X,
                    width: scrollbar.ThumbRect.Width,
                    top: scrollbar.ThumbRect.Y,
                    height: scrollbar.ThumbRect.Height));
        }

        return new OblivionExpandedMarkdownBodyRenderResult(
            UI.Layer(
                id: $"{id}.expanded-body-layer",
                children: children),
            contentHeight,
            scrollbar);
    }

    public static OblivionScrollableCodeSurfaceRenderResult BuildInspectorRawSourceBody(
        string id,
        OblivionCardBody body,
        OblivionMarkdownReadingStyle style,
        double width,
        double viewportHeight,
        double requestedScrollOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(style);

        IReadOnlyList<string> sourceLines = GetOrBuildRawMarkdownSourceLines(body);
        double contentHeight = MeasureSourceContentHeight(sourceLines, style);
        ScrollbarGeometry scrollbar = PresenterScrollRegion.ComputeScrollbarGeometry(
            new Machina.Layout.Geometry.Rect(
                Math.Max(0, width - ExpandedScrollbarWidth),
                0,
                ExpandedScrollbarWidth,
                viewportHeight),
            contentHeight,
            viewportHeight,
            requestedScrollOffset);

        double contentWidth = scrollbar.IsVisible
            ? Math.Max(120, width - ExpandedScrollbarWidth - ExpandedScrollbarGap)
            : width;
        double scrollOffset = scrollbar.ScrollOffset;
        OblivionPreparedRawMarkdownSourceLayout preparedLayout = GetOrBuildPreparedRawMarkdownSourceLayout(body, style, contentWidth);
        List<UiNode> children = [];
        double currentTop = -scrollOffset;

        foreach ((string sourceLine, int index) in preparedLayout.VisibleLines.Select((value, index) => (value, index)))
        {
            if (!IntersectsViewport(currentTop, style.SourceLineHeight, viewportHeight))
            {
                currentTop += style.SourceLineHeight + style.SourceLineGap;
                continue;
            }

            children.Add(
                UI.Anchor(
                    UI.Text(
                        sourceLine.Length == 0 ? " " : sourceLine,
                        id: $"{id}.source-line-{index}",
                        size: TextSize.Sm,
                        color: style.SourceForeground),
                    id: $"{id}.source-line-{index}.slot",
                    left: 0,
                    width: contentWidth,
                    top: currentTop,
                    height: style.SourceLineHeight));
            currentTop += style.SourceLineHeight + style.SourceLineGap;
        }

        if (scrollbar.IsVisible)
        {
            children.Add(
                UI.Anchor(
                    UI.Rect(
                        id: $"{id}.scrollbar-track",
                        style: new UiStyle(
                            Background: style.ScrollbarTrack)),
                    id: $"{id}.scrollbar-track.slot",
                    left: scrollbar.TrackRect.X,
                    width: scrollbar.TrackRect.Width,
                    top: scrollbar.TrackRect.Y,
                    height: scrollbar.TrackRect.Height));
            children.Add(
                UI.Anchor(
                    UI.Rect(
                        id: $"{id}.scrollbar-thumb",
                        style: new UiStyle(
                            Background: style.ScrollbarThumb)),
                    id: $"{id}.scrollbar-thumb.slot",
                    left: scrollbar.ThumbRect.X,
                    width: scrollbar.ThumbRect.Width,
                    top: scrollbar.ThumbRect.Y,
                    height: scrollbar.ThumbRect.Height));
        }

        return new OblivionScrollableCodeSurfaceRenderResult(
            UI.Rect(
                child: UI.Layer(
                    id: $"{id}.source-layer",
                    children: children),
                id: $"{id}.source-frame",
                style: new UiStyle(
                    Background: style.SourceSurface,
                    BorderColor: style.SourceBorder,
                    BorderThickness: 1,
                    ClipToBounds: true)),
            contentHeight,
            scrollbar);
    }

    public static IReadOnlyList<string> BuildPreviewLines(DocumentMir mir, IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(mir);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return BuildPreviewEntries(mir, diagnostics)
            .Select(entry => entry.Text)
            .ToArray();
    }

    public static IReadOnlyList<string> BuildInspectorLines(OblivionCardBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.DocumentMir is null)
        {
            return body.PreviewLines.Count == 0 ? ["<empty>"] : body.PreviewLines;
        }

        List<string> lines = [];
        foreach (DocumentBlockMir block in body.DocumentMir.Blocks)
        {
            AppendBlockLines(lines, block);
            if (lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add(string.Empty);
            }
        }

        TrimTrailingBlankLines(lines);
        return lines.Count == 0 ? ["<empty markdown body>"] : lines;
    }

    public static IReadOnlyList<string> BuildDiagnosticLines(IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (diagnostics.Count == 0)
        {
            return ["No Markdown diagnostics."];
        }

        return diagnostics
            .Select(FormatDiagnosticLine)
            .ToArray();
    }

    public static IReadOnlyList<string> BuildRawMarkdownSourceLinesForScrollSurface(OblivionCardBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return GetOrBuildRawMarkdownSourceLines(body);
    }

    public static double MeasureRawMarkdownSourceContentHeight(
        OblivionCardBody body,
        OblivionMarkdownReadingStyle style)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(style);
        return MeasureSourceContentHeight(GetOrBuildRawMarkdownSourceLines(body), style);
    }

    public static OblivionMarkdownRendererDiagnostics GetDiagnostics()
    {
        return new OblivionMarkdownRendererDiagnostics(
            RawMarkdownSourceLayoutBuildCount,
            RawMarkdownSourceLayoutCache.Count);
    }

    public static void ResetDiagnostics()
    {
        System.Threading.Interlocked.Exchange(ref RawMarkdownSourceLayoutBuildCount, 0);
        RawMarkdownSourceLinesCache.Clear();
        RawMarkdownSourceLayoutCache.Clear();
        RawMarkdownSourceLayoutBuildCountBySource.Clear();
    }

    public static int GetRawMarkdownSourceLayoutBuildCountForBody(OblivionCardBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string sourceIdentity = body.RawText ?? string.Empty;
        return RawMarkdownSourceLayoutBuildCountBySource.TryGetValue(sourceIdentity, out int count)
            ? count
            : 0;
    }

    private static UiNode BuildPlainPreviewBody(
        string id,
        IReadOnlyList<string> lines,
        StandardTheme theme,
        double width,
        double height)
    {
        List<UiNode> children = [];
        double currentTop = 0;
        IReadOnlyList<string> visibleLines = PresenterCardLayoutHelper.WrapOrClipLinesToFit(
            lines,
            width,
            height,
            PreviewTextLayout,
            CreatePreviewTextStyle(PreviewMutedForeground));

        foreach ((string line, int index) in visibleLines.Select((value, index) => (value, index)))
        {
            children.Add(
                UI.Anchor(
                    UI.Text(
                        line,
                        id: $"{id}.plain-{index}",
                        size: TextSize.Sm,
                        color: PreviewMutedForeground),
                    id: $"{id}.plain-{index}.slot",
                    left: 0,
                    right: 0,
                    top: currentTop,
                    height: PreviewLineHeight));
            currentTop += PreviewLineHeight + PreviewGap;
        }

        return BuildPreviewFrame(id, children, layerIdSuffix: "plain-layer", frameIdSuffix: "plain-frame");
    }

    private static UiNode BuildPreviewFrame(
        string id,
        IReadOnlyList<UiNode> children,
        string layerIdSuffix = "preview-layer",
        string frameIdSuffix = "preview-frame")
    {
        return UI.Rect(
            child: UI.Layer(
                id: $"{id}.{layerIdSuffix}",
                children: children),
            id: $"{id}.{frameIdSuffix}",
            style: new UiStyle(
                Background: PreviewFrameBackground,
                BorderColor: PreviewFrameBorder,
                BorderThickness: 1));
    }

    private static IReadOnlyList<string> WrapPreviewEntry(
        MarkdownPreviewEntry entry,
        double width,
        int remainingLineCount)
    {
        int entryLineLimit = Math.Min(
            remainingLineCount,
            entry.Kind == MarkdownPreviewEntryKind.Summary
                ? PreviewSummaryLineLimit
                : 1);

        if (entryLineLimit <= 0)
        {
            return [];
        }

        double entryHeight = (entryLineLimit * PreviewLineHeight) + (Math.Max(0, entryLineLimit - 1) * PreviewGap);
        return PresenterCardLayoutHelper.WrapOrClipLinesToFit(
            [entry.Text],
            width,
            entryHeight,
            PreviewTextLayout,
            CreatePreviewTextStyle(GetPreviewColor(entry.Kind)));
    }

    private static TextStyle CreatePreviewTextStyle(ColorToken color)
    {
        return new TextStyle(
            Color: color,
            Size: TextSize.Sm,
            AlignX: TextAlignX.Left,
            AlignY: TextAlignY.Top);
    }

    private static ColorToken GetPreviewColor(MarkdownPreviewEntryKind kind)
    {
        return kind switch
        {
            MarkdownPreviewEntryKind.Heading => PreviewForeground,
            MarkdownPreviewEntryKind.Summary => PreviewMutedForeground,
            MarkdownPreviewEntryKind.Code => ColorToken.Hex(0xBFDBFEFF),
            MarkdownPreviewEntryKind.Diagnostics => ColorToken.Hex(0xFCA5A5FF),
            _ => PreviewMutedForeground,
        };
    }

    private static IReadOnlyList<MarkdownPreviewEntry> BuildPreviewEntries(
        DocumentMir mir,
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        List<MarkdownPreviewEntry> entries = [];
        bool addedSummary = false;
        bool addedCode = false;

        foreach (DocumentBlockMir block in mir.Blocks)
        {
            switch (block)
            {
                case HeadingMir heading when entries.All(entry => entry.Kind != MarkdownPreviewEntryKind.Heading):
                    entries.Add(new MarkdownPreviewEntry(MarkdownPreviewEntryKind.Heading, RenderInlineList(heading.Inlines)));
                    break;

                case ParagraphMir paragraph when !addedSummary:
                    entries.Add(new MarkdownPreviewEntry(MarkdownPreviewEntryKind.Summary, RenderInlineMarkup(paragraph.Inlines, includeLinkTarget: true)));
                    addedSummary = true;
                    break;

                case ListMir list when !addedSummary:
                    string summary = list.Items.Count == 0
                        ? "list"
                        : $"{ListPrefix(list.Kind, 1)} {RenderInlineMarkup(list.Items[0].Inlines, includeLinkTarget: true)}";
                    if (list.Items.Count > 1)
                    {
                        summary = $"{summary} (+{list.Items.Count - 1} more)";
                    }

                    entries.Add(new MarkdownPreviewEntry(MarkdownPreviewEntryKind.Summary, summary));
                    addedSummary = true;
                    break;

                case CodeBlockMir codeBlock when !addedCode:
                    string language = string.IsNullOrWhiteSpace(codeBlock.Language)
                        ? "plain"
                        : codeBlock.Language.Trim();
                    entries.Add(new MarkdownPreviewEntry(MarkdownPreviewEntryKind.Code, $"code: {language}"));
                    addedCode = true;
                    break;
            }

            if (entries.Count >= 3)
            {
                break;
            }
        }

        if (diagnostics.Count > 0)
        {
            entries.Add(new MarkdownPreviewEntry(MarkdownPreviewEntryKind.Diagnostics, $"Diagnostics: {diagnostics.Count}"));
        }

        if (entries.Count == 0)
        {
            entries.Add(new MarkdownPreviewEntry(MarkdownPreviewEntryKind.Summary, "<empty markdown body>"));
        }

        return entries;
    }

    private static MarkdownRenderedBlock LowerBlock(string id, DocumentBlockMir block, OblivionMarkdownReadingStyle style, double width)
    {
        return block switch
        {
            HeadingMir heading => LowerHeading(id, heading, style, width),
            ParagraphMir paragraph => LowerParagraph(id, paragraph, style, width),
            ListMir list => LowerList(id, list, style, width),
            CodeBlockMir codeBlock => LowerCodeBlock(id, codeBlock, style, width),
            ThematicBreakMir => LowerThematicBreak(id, style, width),
            _ => new MarkdownRenderedBlock(
                UI.Text(block.GetType().Name, id: $"{id}.unknown", size: TextSize.Sm, color: style.MutedForeground),
                PreviewLineHeight),
        };
    }

    private static MarkdownRenderedBlock LowerHeading(string id, HeadingMir heading, OblivionMarkdownReadingStyle style, double width)
    {
        string label = $"H{Math.Clamp(heading.Level, 1, 6)}";
        MachinaTextVariant variant = heading.Level <= 2
            ? MachinaTextVariant.Title
            : heading.Level <= 4
                ? MachinaTextVariant.Label
                : MachinaTextVariant.Body;
        MachinaTextSpec spec = Text.Markup(
            RenderInlineMarkup(heading.Inlines, includeLinkTarget: true),
            variant: variant,
            leading: heading.Level <= 2 ? MachinaTextLeading.Loose : MachinaTextLeading.Normal,
            blockGap: 4);
        double textWidth = Math.Max(120, width - HeadingLabelWidth - InlineGap);
        double textHeight = MeasureTextHeight(spec, textWidth, minimumHeight: 24);

        UiNode node = UI.Layer(
            id: $"{id}.heading-layer",
            children:
            [
                UI.Anchor(
                    UI.Text(
                        label,
                        id: $"{id}.heading-label",
                        size: TextSize.Sm,
                        color: style.MutedForeground),
                    id: $"{id}.heading-label.slot",
                    left: 0,
                    width: HeadingLabelWidth,
                    top: 0,
                    height: 20),
                UI.Anchor(
                    StandardUI.TextBlock(
                        spec,
                        id: $"{id}.heading-text",
                        foreground: style.HeadingForeground,
                        linkForeground: style.LinkForeground),
                    id: $"{id}.heading-text.slot",
                    left: HeadingLabelWidth + InlineGap,
                    width: textWidth,
                    top: 0,
                    height: textHeight),
            ]);

        return new MarkdownRenderedBlock(node, Math.Max(20, textHeight));
    }

    private static MarkdownRenderedBlock LowerParagraph(string id, ParagraphMir paragraph, OblivionMarkdownReadingStyle style, double width)
    {
        MachinaTextSpec spec = Text.Markup(
            RenderInlineMarkup(paragraph.Inlines, includeLinkTarget: true),
            variant: MachinaTextVariant.Body,
            leading: MachinaTextLeading.Normal,
            blockGap: 4);
        double textHeight = MeasureTextHeight(spec, width, minimumHeight: 20);

        return new MarkdownRenderedBlock(
            StandardUI.TextBlock(
                spec,
                id: $"{id}.paragraph",
                foreground: style.Foreground,
                linkForeground: style.LinkForeground),
            textHeight);
    }

    private static MarkdownRenderedBlock LowerList(string id, ListMir list, OblivionMarkdownReadingStyle style, double width)
    {
        List<UiNode> children = [];
        double currentTop = 0;
        double contentWidth = Math.Max(100, width - ListMarkerWidth - InlineGap);

        foreach ((ListItemMir item, int index) in list.Items.Select((value, index) => (value, index)))
        {
            string marker = ListPrefix(list.Kind, index + 1);
            MachinaTextSpec spec = Text.Markup(
                RenderInlineMarkup(item.Inlines, includeLinkTarget: true),
                variant: MachinaTextVariant.Body,
                leading: MachinaTextLeading.Normal,
                blockGap: 4);
            double textHeight = MeasureTextHeight(spec, contentWidth, minimumHeight: 18);
            double rowHeight = Math.Max(18, textHeight);

            children.Add(
                UI.Anchor(
                    UI.Text(
                        marker,
                        id: $"{id}.item-{index}.marker",
                        size: TextSize.Sm,
                        color: style.Foreground),
                    id: $"{id}.item-{index}.marker.slot",
                    left: 0,
                    width: ListMarkerWidth,
                    top: currentTop,
                    height: 18));

            children.Add(
                UI.Anchor(
                    StandardUI.TextBlock(
                        spec,
                        id: $"{id}.item-{index}.text",
                        foreground: style.Foreground,
                        linkForeground: style.LinkForeground),
                    id: $"{id}.item-{index}.text.slot",
                    left: ListMarkerWidth + InlineGap,
                    width: contentWidth,
                    top: currentTop,
                    height: textHeight));

            currentTop += rowHeight + 6;
        }

        return new MarkdownRenderedBlock(
            UI.Layer(
                id: $"{id}.list-layer",
                children: children),
            Math.Max(18, currentTop == 0 ? 18 : currentTop - 6));
    }

    private static MarkdownRenderedBlock LowerCodeBlock(string id, CodeBlockMir codeBlock, OblivionMarkdownReadingStyle style, double width)
    {
        List<UiNode> children = [];
        double currentTop = CodePadding;
        string languageLabel = string.IsNullOrWhiteSpace(codeBlock.Language)
            ? "code"
            : $"code: {codeBlock.Language}";

        children.Add(
            UI.Anchor(
                    UI.Text(
                        languageLabel,
                        id: $"{id}.code-language",
                        size: TextSize.Sm,
                        color: style.LinkForeground),
                    id: $"{id}.code-language.slot",
                    left: CodePadding,
                    right: CodePadding,
                top: currentTop,
                height: CodeHeaderHeight));
        currentTop += CodeHeaderHeight + CodeLineGap;

        string[] lines = SplitLines(codeBlock.Text);
        if (lines.Length == 0)
        {
            lines = ["<empty>"];
        }

        foreach ((string line, int index) in lines.Select((value, index) => (value, index)))
        {
            children.Add(
                UI.Anchor(
                    UI.Text(
                        line.Length == 0 ? " " : line,
                        id: $"{id}.code-line-{index}",
                        size: TextSize.Sm,
                        color: style.CodeForeground),
                    id: $"{id}.code-line-{index}.slot",
                    left: CodePadding,
                    right: CodePadding,
                    top: currentTop,
                    height: CodeLineHeight));
            currentTop += CodeLineHeight + CodeLineGap;
        }

        double frameHeight = currentTop + CodePadding - CodeLineGap;
        UiNode node = UI.Rect(
            child: UI.Layer(
                id: $"{id}.code-layer",
                children: children),
            id: $"{id}.code-frame",
            style: new UiStyle(
                Background: style.CodeSurface,
                BorderColor: style.Border,
                BorderThickness: 1));

        return new MarkdownRenderedBlock(node, frameHeight);
    }

    private static MarkdownRenderedBlock LowerThematicBreak(string id, OblivionMarkdownReadingStyle style, double width)
    {
        UiNode node = UI.Layer(
            id: $"{id}.rule-layer",
            children:
            [
                UI.Anchor(
                    UI.Rect(
                        id: $"{id}.rule",
                        style: new UiStyle(
                            Background: style.Border)),
                    id: $"{id}.rule.slot",
                    left: 0,
                    width: width,
                    top: 6,
                    height: 1),
            ]);

        return new MarkdownRenderedBlock(node, 12);
    }

    private static double MeasureTextHeight(MachinaTextSpec spec, double width, double minimumHeight)
    {
        MachinaTextLayoutResult layout = MachinaTextLayoutEngine.Layout(
            spec,
            new MachinaTextBox(0, 0, width, 4096),
            MachinaTextMeasurers.Deterministic);

        double lastLineHeight = layout.Lines.Count == 0 ? 0 : layout.Lines[^1].Bounds.Height;
        double measuredHeight = Math.Max(layout.ContentBounds.Height, lastLineHeight);
        return Math.Max(minimumHeight, Math.Ceiling(measuredHeight));
    }

    private static bool IntersectsViewport(double top, double height, double viewportHeight)
    {
        double bottom = top + height;
        return bottom > 0 && top < viewportHeight;
    }

    private static double MeasureMarkdownDocumentHeight(DocumentMir mir, double width)
    {
        double totalHeight = 0;
        foreach ((DocumentBlockMir block, int index) in mir.Blocks.Select((value, index) => (value, index)))
        {
            totalHeight += MeasureBlockHeight(block, width);
            if (index < mir.Blocks.Count - 1)
            {
                totalHeight += BlockGap;
            }
        }

        return totalHeight <= 0 ? PreviewLineHeight : totalHeight;
    }

    private static double MeasureWrappedPlainTextHeight(IReadOnlyList<string> lines, double width)
    {
        IReadOnlyList<string> wrappedLines = PresenterCardLayoutHelper.WrapOrClipLinesToFit(
            lines,
            width,
            4096,
            new PresenterCardTextLayout(ExpandedPlainLineHeight, ExpandedPlainLineGap),
            new TextStyle(
                Color: PreviewForeground,
                Size: TextSize.Sm,
                AlignX: TextAlignX.Left,
                AlignY: TextAlignY.Top));

        if (wrappedLines.Count == 0)
        {
            return ExpandedPlainLineHeight;
        }

        return (wrappedLines.Count * ExpandedPlainLineHeight) + ((wrappedLines.Count - 1) * ExpandedPlainLineGap);
    }

    private static IReadOnlyList<string> BuildRawMarkdownSourceLines(OblivionCardBody body)
    {
        if (string.IsNullOrEmpty(body.RawText))
        {
            return ["Rendered Markdown appears in the expanded card body.", string.Empty, "Raw Markdown source unavailable for this card."];
        }

        return
        [
            "Rendered Markdown appears in the expanded card body.",
            string.Empty,
            .. SplitLines(body.RawText),
        ];
    }

    private static IReadOnlyList<string> GetOrBuildRawMarkdownSourceLines(OblivionCardBody body)
    {
        if (string.IsNullOrEmpty(body.RawText))
        {
            return BuildRawMarkdownSourceLines(body);
        }

        return RawMarkdownSourceLinesCache.GetOrAdd(
            body.RawText,
            static rawText => BuildRawMarkdownSourceLines(
                new OblivionCardBody(
                    OblivionCardBodyFormat.CopelandMarkdown,
                    rawText,
                    BodySourcePath: null,
                    PreviewLines: [],
                    DocumentMir: null,
                    Diagnostics: [])));
    }

    private static OblivionPreparedRawMarkdownSourceLayout GetOrBuildPreparedRawMarkdownSourceLayout(
        OblivionCardBody body,
        OblivionMarkdownReadingStyle style,
        double contentWidth)
    {
        IReadOnlyList<string> sourceLines = GetOrBuildRawMarkdownSourceLines(body);
        string sourceIdentity = body.RawText ?? string.Empty;
        var cacheKey = new OblivionRawMarkdownSourceLayoutCacheKey(
            sourceIdentity,
            contentWidth,
            style.SourceLineHeight,
            style.SourceLineGap);

        return RawMarkdownSourceLayoutCache.GetOrAdd(
            cacheKey,
            _ =>
            {
                System.Threading.Interlocked.Increment(ref RawMarkdownSourceLayoutBuildCount);
                RawMarkdownSourceLayoutBuildCountBySource.AddOrUpdate(
                    sourceIdentity,
                    addValue: 1,
                    static (_, current) => current + 1);

                TextStyle sourceTextStyle = new(
                    Color: style.SourceForeground,
                    Size: TextSize.Sm,
                    AlignX: TextAlignX.Left,
                    AlignY: TextAlignY.Top);
                PresenterCardTextLayout sourceLayout = new(style.SourceLineHeight, style.SourceLineGap);
                string[] clippedLines = sourceLines
                    .Select(sourceLine =>
                        PresenterCardLayoutHelper.ClipLinesToFit(
                            [sourceLine],
                            contentWidth,
                            style.SourceLineHeight,
                            sourceLayout,
                            sourceTextStyle)
                        .FirstOrDefault()
                        ?? string.Empty)
                    .ToArray();
                double contentHeight = MeasureSourceContentHeight(sourceLines, style);
                return new OblivionPreparedRawMarkdownSourceLayout(clippedLines, contentHeight);
            });
    }

    private static double MeasureSourceContentHeight(
        IReadOnlyList<string> sourceLines,
        OblivionMarkdownReadingStyle style)
    {
        if (sourceLines.Count == 0)
        {
            return style.SourceLineHeight;
        }

        return (sourceLines.Count * style.SourceLineHeight) + ((sourceLines.Count - 1) * style.SourceLineGap);
    }

    private static double MeasureBlockHeight(DocumentBlockMir block, double width)
    {
        return block switch
        {
            HeadingMir heading => MeasureHeadingHeight(heading, width),
            ParagraphMir paragraph => MeasureParagraphHeight(paragraph, width),
            ListMir list => MeasureListHeight(list, width),
            CodeBlockMir codeBlock => MeasureCodeBlockHeight(codeBlock),
            ThematicBreakMir => 12,
            _ => PreviewLineHeight,
        };
    }

    private static double MeasureHeadingHeight(HeadingMir heading, double width)
    {
        MachinaTextVariant variant = heading.Level <= 2
            ? MachinaTextVariant.Title
            : heading.Level <= 4
                ? MachinaTextVariant.Label
                : MachinaTextVariant.Body;
        MachinaTextSpec spec = Text.Markup(
            RenderInlineMarkup(heading.Inlines, includeLinkTarget: true),
            variant: variant,
            leading: heading.Level <= 2 ? MachinaTextLeading.Loose : MachinaTextLeading.Normal,
            blockGap: 4);
        double textWidth = Math.Max(120, width - HeadingLabelWidth - InlineGap);
        double textHeight = MeasureTextHeight(spec, textWidth, minimumHeight: 24);
        return Math.Max(20, textHeight);
    }

    private static double MeasureParagraphHeight(ParagraphMir paragraph, double width)
    {
        MachinaTextSpec spec = Text.Markup(
            RenderInlineMarkup(paragraph.Inlines, includeLinkTarget: true),
            variant: MachinaTextVariant.Body,
            leading: MachinaTextLeading.Normal,
            blockGap: 4);
        return MeasureTextHeight(spec, width, minimumHeight: 20);
    }

    private static double MeasureListHeight(ListMir list, double width)
    {
        double currentTop = 0;
        double contentWidth = Math.Max(100, width - ListMarkerWidth - InlineGap);

        foreach (ListItemMir item in list.Items)
        {
            MachinaTextSpec spec = Text.Markup(
                RenderInlineMarkup(item.Inlines, includeLinkTarget: true),
                variant: MachinaTextVariant.Body,
                leading: MachinaTextLeading.Normal,
                blockGap: 4);
            double textHeight = MeasureTextHeight(spec, contentWidth, minimumHeight: 18);
            double rowHeight = Math.Max(18, textHeight);
            currentTop += rowHeight + 6;
        }

        return Math.Max(18, currentTop == 0 ? 18 : currentTop - 6);
    }

    private static double MeasureCodeBlockHeight(CodeBlockMir codeBlock)
    {
        string[] lines = SplitLines(codeBlock.Text);
        if (lines.Length == 0)
        {
            lines = ["<empty>"];
        }

        return CodePadding + CodeHeaderHeight + CodeLineGap + (lines.Length * (CodeLineHeight + CodeLineGap)) + CodePadding - CodeLineGap;
    }

    private static string RenderInlineMarkup(IReadOnlyList<DocumentInlineMir> inlines, bool includeLinkTarget)
    {
        StringBuilder builder = new();
        AppendInlineMarkup(builder, inlines, includeLinkTarget);
        return builder.ToString().Trim();
    }

    public static string RenderInlineList(IReadOnlyList<DocumentInlineMir> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);

        StringBuilder builder = new();
        AppendInlinePlain(builder, inlines);
        return builder.ToString().Trim();
    }

    private static void AppendInlineMarkup(StringBuilder builder, IReadOnlyList<DocumentInlineMir> inlines, bool includeLinkTarget)
    {
        foreach (DocumentInlineMir inline in inlines)
        {
            switch (inline)
            {
                case TextMir text:
                    builder.Append(EscapeMarkup(text.Text));
                    break;

                case CodeSpanMir code:
                    builder.Append('`');
                    builder.Append(code.Text.Replace("`", "'", StringComparison.Ordinal));
                    builder.Append('`');
                    break;

                case EmphasisMir emphasis:
                    builder.Append('*');
                    AppendInlineMarkup(builder, emphasis.Children, includeLinkTarget);
                    builder.Append('*');
                    break;

                case StrongMir strong:
                    builder.Append("**");
                    AppendInlineMarkup(builder, strong.Children, includeLinkTarget);
                    builder.Append("**");
                    break;

                case LinkMir link:
                    string linkLabel = RenderInlineMarkup(link.Label, includeLinkTarget: false);
                    builder.Append('[');
                    builder.Append(string.IsNullOrWhiteSpace(linkLabel) ? EscapeMarkup(link.Target) : linkLabel);
                    builder.Append("](");
                    builder.Append(link.Target);
                    builder.Append(')');
                    if (includeLinkTarget)
                    {
                        builder.Append(" -> ");
                        builder.Append(EscapeMarkup(link.Target));
                    }

                    break;
            }
        }
    }

    private static void AppendInlinePlain(StringBuilder builder, IReadOnlyList<DocumentInlineMir> inlines)
    {
        foreach (DocumentInlineMir inline in inlines)
        {
            switch (inline)
            {
                case TextMir text:
                    builder.Append(text.Text);
                    break;

                case CodeSpanMir code:
                    builder.Append('`');
                    builder.Append(code.Text);
                    builder.Append('`');
                    break;

                case EmphasisMir emphasis:
                    builder.Append('*');
                    AppendInlinePlain(builder, emphasis.Children);
                    builder.Append('*');
                    break;

                case StrongMir strong:
                    builder.Append("**");
                    AppendInlinePlain(builder, strong.Children);
                    builder.Append("**");
                    break;

                case LinkMir link:
                    AppendInlinePlain(builder, link.Label);
                    builder.Append(" -> ");
                    builder.Append(link.Target);
                    break;
            }
        }
    }

    private static void AppendBlockLines(List<string> lines, DocumentBlockMir block)
    {
        switch (block)
        {
            case HeadingMir heading:
                lines.Add($"H{Math.Clamp(heading.Level, 1, 6)} {RenderInlineList(heading.Inlines)}");
                break;

            case ParagraphMir paragraph:
                AddWrappedLines(lines, RenderInlineList(paragraph.Inlines), prefix: string.Empty, width: 72);
                break;

            case ListMir list:
                int index = 1;
                foreach (ListItemMir item in list.Items)
                {
                    AddWrappedLines(lines, RenderInlineList(item.Inlines), ListPrefix(list.Kind, index) + " ", width: 72);
                    index += 1;
                }

                break;

            case CodeBlockMir codeBlock:
                lines.Add(string.IsNullOrWhiteSpace(codeBlock.Language) ? "code" : $"code: {codeBlock.Language}");
                lines.AddRange(SplitLines(codeBlock.Text));
                break;

            case ThematicBreakMir:
                lines.Add("---");
                break;
        }
    }

    private static string FormatDiagnosticLine(OblivionWorkspaceDiagnostic diagnostic)
    {
        string location = diagnostic.Line is null || diagnostic.Column is null
            ? (diagnostic.SpanStart is null || diagnostic.SpanLength is null
                ? "span n/a"
                : $"span {diagnostic.SpanStart}+{diagnostic.SpanLength}")
            : $"{diagnostic.Line}:{diagnostic.Column}";

        return $"{diagnostic.DisplaySeverity ?? diagnostic.Severity.ToString()} | {diagnostic.Code} | {location} | {diagnostic.Message}";
    }

    private static string EscapeMarkup(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char character in text)
        {
            if (character is '\\' or '*' or '`' or '[' or ']' or '(' or ')' or '-')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string ListPrefix(DocumentListKind kind, int index)
    {
        return kind == DocumentListKind.Ordered ? $"{index}." : "\u2022";
    }

    private static void AddWrappedLines(List<string> lines, string text, string prefix, int width)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            lines.Add(prefix.TrimEnd());
            return;
        }

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        StringBuilder current = new(prefix);
        int contentWidth = Math.Max(16, width - prefix.Length);

        foreach (string word in words)
        {
            string candidate = current.Length == prefix.Length
                ? word
                : $"{current.ToString(prefix.Length, current.Length - prefix.Length)} {word}";

            if (candidate.Length > contentWidth && current.Length > prefix.Length)
            {
                lines.Add(current.ToString().TrimEnd());
                current.Clear();
                current.Append(new string(' ', prefix.Length));
                current.Append(word);
                continue;
            }

            if (current.Length > prefix.Length)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString().TrimEnd());
        }
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static void TrimTrailingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private readonly record struct MarkdownRenderedBlock(UiNode Node, double Height);

    public sealed record OblivionExpandedMarkdownBodyRenderResult(
        UiNode Node,
        double ContentHeight,
        ScrollbarGeometry ScrollbarGeometry);

    private readonly record struct MarkdownPreviewEntry(MarkdownPreviewEntryKind Kind, string Text);

    private enum MarkdownPreviewEntryKind
    {
        Heading,
        Summary,
        Code,
        Diagnostics,
    }

    private sealed record OblivionRawMarkdownSourceLayoutCacheKey(
        string SourceIdentity,
        double ContentWidth,
        double SourceLineHeight,
        double SourceLineGap);

    private sealed record OblivionPreparedRawMarkdownSourceLayout(
        IReadOnlyList<string> VisibleLines,
        double ContentHeight);
}
