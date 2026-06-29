using System.Text;
using Copeland.Markdown;

namespace Machina.Presenter.Sample;

internal static class OblivionMarkdownBody
{
    public static OblivionCardBody CreatePlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new OblivionCardBody(
            OblivionCardBodyFormat.Plain,
            text,
            BodySourcePath: null,
            PreviewLines: SplitLines(text),
            DocumentMir: null,
            Diagnostics: []);
    }

    public static OblivionCardBody CreateMarkdown(
        string markdownText,
        string? bodySourcePath,
        MarkdownCompilation compilation,
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(markdownText);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return new OblivionCardBody(
            OblivionCardBodyFormat.CopelandMarkdown,
            markdownText,
            bodySourcePath,
            PreviewLines: BuildPreviewLines(compilation.Mir, diagnostics),
            DocumentMir: compilation.Mir,
            Diagnostics: diagnostics);
    }

    public static IReadOnlyList<string> BuildInspectorLines(OblivionCardBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.Format == OblivionCardBodyFormat.Plain || body.DocumentMir is null)
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

    public static IReadOnlyList<string> BuildPreviewLines(DocumentMir mir, IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(mir);
        ArgumentNullException.ThrowIfNull(diagnostics);

        List<string> lines = [];

        foreach (DocumentBlockMir block in mir.Blocks)
        {
            switch (block)
            {
                case HeadingMir heading:
                    lines.Add(RenderInlineList(heading.Inlines));
                    break;
                case ParagraphMir paragraph:
                    lines.Add(RenderInlineList(paragraph.Inlines));
                    break;
                case ListMir list:
                    foreach (ListItemMir item in list.Items.Take(2))
                    {
                        lines.Add($"- {RenderInlineList(item.Inlines)}");
                    }

                    if (list.Items.Count > 2)
                    {
                        lines.Add($"+{list.Items.Count - 2} more list items");
                    }

                    break;
                case CodeBlockMir codeBlock:
                    string firstCodeLine = SplitLines(codeBlock.Text).FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line)) ?? "<empty>";
                    string languageSuffix = string.IsNullOrWhiteSpace(codeBlock.Language) ? string.Empty : $" {codeBlock.Language}";
                    lines.Add($"```{languageSuffix}".TrimEnd());
                    lines.Add(firstCodeLine);
                    break;
                case ThematicBreakMir:
                    lines.Add("---");
                    break;
            }

            if (lines.Count >= 5)
            {
                break;
            }
        }

        if (diagnostics.Count > 0)
        {
            lines.Add($"Markdown diagnostics: {diagnostics.Count}");
        }

        return lines.Count == 0 ? ["<empty markdown body>"] : lines.Take(5).ToArray();
    }

    public static IReadOnlyList<string> BuildDiagnosticLines(IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return diagnostics.Count == 0
            ? ["No Markdown diagnostics."]
            : diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToArray();
    }

    public static string RenderInlineList(IReadOnlyList<DocumentInlineMir> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);

        StringBuilder builder = new();
        foreach (DocumentInlineMir inline in inlines)
        {
            AppendInlineText(builder, inline);
        }

        return builder.ToString().Trim();
    }

    private static void AppendBlockLines(List<string> lines, DocumentBlockMir block)
    {
        switch (block)
        {
            case HeadingMir heading:
                lines.Add($"{new string('#', Math.Clamp(heading.Level, 1, 6))} {RenderInlineList(heading.Inlines)}");
                break;
            case ParagraphMir paragraph:
                AddWrappedLines(lines, RenderInlineList(paragraph.Inlines), prefix: string.Empty, width: 54);
                break;
            case ListMir list:
                int index = 1;
                foreach (ListItemMir item in list.Items)
                {
                    string prefix = list.Kind == DocumentListKind.Ordered ? $"{index}. " : "- ";
                    AddWrappedLines(lines, RenderInlineList(item.Inlines), prefix, width: 54);
                    index++;
                }

                break;
            case CodeBlockMir codeBlock:
                string openingFence = string.IsNullOrWhiteSpace(codeBlock.Language)
                    ? "```"
                    : $"```{codeBlock.Language}";
                lines.Add(openingFence);
                lines.AddRange(SplitLines(codeBlock.Text));
                lines.Add("```");
                break;
            case ThematicBreakMir:
                lines.Add("---");
                break;
        }
    }

    private static void AppendInlineText(StringBuilder builder, DocumentInlineMir inline)
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
                AppendInlineList(builder, emphasis.Children);
                builder.Append('*');
                break;
            case StrongMir strong:
                builder.Append("**");
                AppendInlineList(builder, strong.Children);
                builder.Append("**");
                break;
            case LinkMir link:
                builder.Append(RenderInlineList(link.Label));
                builder.Append(" <");
                builder.Append(link.Target);
                builder.Append('>');
                break;
        }
    }

    private static void AppendInlineList(StringBuilder builder, IReadOnlyList<DocumentInlineMir> inlines)
    {
        foreach (DocumentInlineMir inline in inlines)
        {
            AppendInlineText(builder, inline);
        }
    }

    private static void TrimTrailingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
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

    private static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToArray();
    }
}
