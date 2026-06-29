using System.Text;
using Copeland.Markdown;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

internal static class OblivionMarkdownRenderer
{
    private const double PreviewLineHeight = 18;
    private const double PreviewGap = 6;
    private const double BlockGap = 12;
    private const double InlineGap = 10;
    private const double HeadingLabelWidth = 28;
    private const double ListMarkerWidth = 24;
    private const double CodePadding = 10;
    private const double CodeHeaderHeight = 18;
    private const double CodeLineHeight = 16;
    private const double CodeLineGap = 4;

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

        foreach (MarkdownPreviewEntry entry in BuildPreviewEntries(body.DocumentMir, body.Diagnostics))
        {
            double remainingHeight = height - currentTop;
            if (remainingHeight <= 0)
            {
                break;
            }

            switch (entry.Kind)
            {
                case MarkdownPreviewEntryKind.Heading:
                    children.Add(
                        UI.Anchor(
                            UI.Text(
                                entry.Text,
                                id: $"{id}.preview-heading",
                                size: TextSize.Md,
                                color: theme.Colors.Foreground),
                            id: $"{id}.preview-heading.slot",
                            left: 0,
                            right: 0,
                            top: currentTop,
                            height: PreviewLineHeight));
                    currentTop += PreviewLineHeight + PreviewGap;
                    break;

                case MarkdownPreviewEntryKind.Code:
                    children.Add(
                        UI.Anchor(
                            UI.Text(
                                entry.Text,
                                id: $"{id}.preview-code",
                                size: TextSize.Sm,
                                color: ColorToken.Hex(0xBFDBFEFF)),
                            id: $"{id}.preview-code.slot",
                            left: 0,
                            right: 0,
                            top: currentTop,
                            height: PreviewLineHeight));
                    currentTop += PreviewLineHeight + PreviewGap;
                    break;

                case MarkdownPreviewEntryKind.Diagnostics:
                    children.Add(
                        UI.Anchor(
                            UI.Text(
                                entry.Text,
                                id: $"{id}.preview-diagnostics",
                                size: TextSize.Sm,
                                color: ColorToken.Hex(0xFCA5A5FF)),
                            id: $"{id}.preview-diagnostics.slot",
                            left: 0,
                            right: 0,
                            top: currentTop,
                            height: PreviewLineHeight));
                    currentTop += PreviewLineHeight + PreviewGap;
                    break;

                default:
                    MachinaTextSpec summarySpec = Text.Markup(
                        entry.Text,
                        variant: MachinaTextVariant.Body,
                        leading: MachinaTextLeading.Tight,
                        blockGap: 4);
                    double summaryHeight = MeasureTextHeight(summarySpec, width, minimumHeight: PreviewLineHeight);
                    children.Add(
                        UI.Anchor(
                            StandardUI.TextBlock(
                                summarySpec,
                                id: $"{id}.preview-summary",
                                theme: theme),
                            id: $"{id}.preview-summary.slot",
                            left: 0,
                            width: width,
                            top: currentTop,
                            height: summaryHeight));
                    currentTop += summaryHeight + PreviewGap;
                    break;
            }
        }

        return UI.Rect(
            child: UI.Layer(
                id: $"{id}.preview-layer",
                children: children),
            id: $"{id}.preview-frame",
            style: new UiStyle(
                Background: ColorToken.Hex(0x0B1220FF),
                BorderColor: ColorToken.Hex(0x334155FF),
                BorderThickness: 1));
    }

    public static UiNode BuildInspectorBody(
        string id,
        OblivionCardBody body,
        StandardTheme theme,
        double width)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(theme);

        if (body.DocumentMir is null)
        {
            return BuildPlainPreviewBody(id, body.PreviewLines, theme, width, height: 320);
        }

        List<UiNode> children = [];
        double currentTop = 0;

        foreach ((DocumentBlockMir block, int index) in body.DocumentMir.Blocks.Select((value, index) => (value, index)))
        {
            MarkdownRenderedBlock rendered = LowerBlock($"{id}.block-{index}", block, theme, width);
            children.Add(
                UI.Anchor(
                    rendered.Node,
                    id: $"{id}.block-{index}.slot",
                    left: 0,
                    width: width,
                    top: currentTop,
                    height: rendered.Height));
            currentTop += rendered.Height + BlockGap;
        }

        if (children.Count == 0)
        {
            children.Add(
                UI.Anchor(
                    UI.Text(
                        "<empty markdown body>",
                        id: $"{id}.empty",
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground),
                    id: $"{id}.empty.slot",
                    left: 0,
                    right: 0,
                    top: 0,
                    height: PreviewLineHeight));
            currentTop += PreviewLineHeight;
        }

        return UI.Layer(
            id: $"{id}.body-layer",
            children:
            [
                .. children,
                UI.Anchor(
                    UI.VSpace(Math.Max(0, currentTop)),
                    id: $"{id}.body-height",
                    left: 0,
                    width: width,
                    top: 0,
                    height: currentTop),
            ]);
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

    private static UiNode BuildPlainPreviewBody(
        string id,
        IReadOnlyList<string> lines,
        StandardTheme theme,
        double width,
        double height)
    {
        List<UiNode> children = [];
        double currentTop = 0;

        foreach ((string line, int index) in lines.Take(6).Select((value, index) => (value, index)))
        {
            if (currentTop + PreviewLineHeight > height)
            {
                break;
            }

            children.Add(
                UI.Anchor(
                    UI.Text(
                        line,
                        id: $"{id}.plain-{index}",
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground),
                    id: $"{id}.plain-{index}.slot",
                    left: 0,
                    right: 0,
                    top: currentTop,
                    height: PreviewLineHeight));
            currentTop += PreviewLineHeight + PreviewGap;
        }

        return UI.Rect(
            child: UI.Layer(
                id: $"{id}.plain-layer",
                children: children),
            id: $"{id}.plain-frame",
            style: new UiStyle(
                Background: ColorToken.Hex(0x0B1220FF),
                BorderColor: ColorToken.Hex(0x334155FF),
                BorderThickness: 1));
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

    private static MarkdownRenderedBlock LowerBlock(string id, DocumentBlockMir block, StandardTheme theme, double width)
    {
        return block switch
        {
            HeadingMir heading => LowerHeading(id, heading, theme, width),
            ParagraphMir paragraph => LowerParagraph(id, paragraph, theme, width),
            ListMir list => LowerList(id, list, theme, width),
            CodeBlockMir codeBlock => LowerCodeBlock(id, codeBlock, theme, width),
            ThematicBreakMir => LowerThematicBreak(id, width),
            _ => new MarkdownRenderedBlock(
                UI.Text(block.GetType().Name, id: $"{id}.unknown", size: TextSize.Sm, color: theme.Colors.MutedForeground),
                PreviewLineHeight),
        };
    }

    private static MarkdownRenderedBlock LowerHeading(string id, HeadingMir heading, StandardTheme theme, double width)
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
                        color: theme.Colors.MutedForeground),
                    id: $"{id}.heading-label.slot",
                    left: 0,
                    width: HeadingLabelWidth,
                    top: 0,
                    height: 20),
                UI.Anchor(
                    StandardUI.TextBlock(
                        spec,
                        id: $"{id}.heading-text",
                        theme: theme),
                    id: $"{id}.heading-text.slot",
                    left: HeadingLabelWidth + InlineGap,
                    width: textWidth,
                    top: 0,
                    height: textHeight),
            ]);

        return new MarkdownRenderedBlock(node, Math.Max(20, textHeight));
    }

    private static MarkdownRenderedBlock LowerParagraph(string id, ParagraphMir paragraph, StandardTheme theme, double width)
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
                theme: theme),
            textHeight);
    }

    private static MarkdownRenderedBlock LowerList(string id, ListMir list, StandardTheme theme, double width)
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
                        color: theme.Colors.Foreground),
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
                        theme: theme),
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

    private static MarkdownRenderedBlock LowerCodeBlock(string id, CodeBlockMir codeBlock, StandardTheme theme, double width)
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
                    color: ColorToken.Hex(0xBFDBFEFF)),
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
                        color: ColorToken.Hex(0xE2E8F0FF)),
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
                Background: ColorToken.Hex(0x0F172AFF),
                BorderColor: ColorToken.Hex(0x475569FF),
                BorderThickness: 1));

        return new MarkdownRenderedBlock(node, frameHeight);
    }

    private static MarkdownRenderedBlock LowerThematicBreak(string id, double width)
    {
        UiNode node = UI.Layer(
            id: $"{id}.rule-layer",
            children:
            [
                UI.Anchor(
                    UI.Rect(
                        id: $"{id}.rule",
                        style: new UiStyle(
                            Background: ColorToken.Hex(0x475569FF))),
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

    private readonly record struct MarkdownPreviewEntry(MarkdownPreviewEntryKind Kind, string Text);

    private enum MarkdownPreviewEntryKind
    {
        Heading,
        Summary,
        Code,
        Diagnostics,
    }
}
