namespace Machina.Standard.Text;

public static class MachinaTextParser
{
    private const int MaximumListDepth = 2;

    private static readonly HashSet<char> AllowedEscapes = ['\\', '*', '`', '[', ']', '(', ')', '-'];

    public static ParseMachinaTextResult Parse(MachinaTextSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source switch
        {
            PlainTextSource plain => ParsePlain(plain.Text),
            MachinaMarkupSource markup => ParseMarkup(markup.Text),
            _ => UnsupportedSourceKind(),
        };
    }

    public static ParseMachinaTextResult ParsePlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var document = new MachinaTextDocument(
        [
            new ParagraphBlock(
            [
                new TextRun(text),
            ]),
        ]);

        return new ParseMachinaTextResult(true, document, []);
    }

    public static ParseMachinaTextResult ParseMarkup(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var blocks = new List<MachinaTextBlock>();
        var diagnostics = new List<MachinaTextDiagnostic>();
        var lines = ToLines(text);
        var lineIndex = 0;

        while (lineIndex < lines.Count)
        {
            var line = lines[lineIndex];

            if (line.Text.Trim().Length == 0)
            {
                lineIndex += 1;
                continue;
            }

            var forbiddenCode = ClassifyForbiddenBlock(line.Text);
            if (forbiddenCode is not null)
            {
                diagnostics.Add(MakeDiagnostic(
                    forbiddenCode.Value,
                    "Unsupported block syntax.",
                    line.Index,
                    Math.Max(1, line.Text.Length),
                    line.Line,
                    1));
                blocks.Add(new ParagraphBlock([new TextRun(line.Text)]));
                lineIndex += 1;
                continue;
            }

            var bullet = ParseBulletLine(line.Text);
            if (bullet is not null)
            {
                ParseBulletList(lines, blocks, diagnostics, ref lineIndex);
                continue;
            }

            var paragraphLines = new List<LineInfo>();
            while (lineIndex < lines.Count)
            {
                var current = lines[lineIndex];

                if (current.Text.Trim().Length == 0 ||
                    ParseBulletLine(current.Text) is not null ||
                    ClassifyForbiddenBlock(current.Text) is not null)
                {
                    break;
                }

                paragraphLines.Add(current);
                lineIndex += 1;
            }

            var paragraphText = string.Join("\n", paragraphLines.Select(paragraphLine => paragraphLine.Text));
            var firstLine = paragraphLines[0];
            var parsed = ParseInline(paragraphText, firstLine.Index, firstLine.Line);
            diagnostics.AddRange(parsed.Diagnostics);
            blocks.Add(new ParagraphBlock(parsed.Inline));
        }

        var document = new MachinaTextDocument(blocks);
        return new ParseMachinaTextResult(true, document, diagnostics);
    }

    private static ParseMachinaTextResult UnsupportedSourceKind()
    {
        var diagnostic = MakeDiagnostic(
            MachinaTextDiagnosticCode.UnsupportedSyntax,
            "Unsupported Machina text source kind.",
            0,
            0,
            1,
            1);

        return new ParseMachinaTextResult(false, new MachinaTextDocument([]), [diagnostic]);
    }

    private static void ParseBulletList(
        IReadOnlyList<LineInfo> lines,
        List<MachinaTextBlock> blocks,
        List<MachinaTextDiagnostic> diagnostics,
        ref int lineIndex)
    {
        var items = new List<MachinaBulletItem>();
        var deferredBlocks = new List<MachinaTextBlock>();

        while (lineIndex < lines.Count)
        {
            var current = lines[lineIndex];

            if (current.Text.Trim().Length == 0)
            {
                break;
            }

            var currentBullet = ParseBulletLine(current.Text);
            if (currentBullet is null)
            {
                break;
            }

            if (IsTaskListLine(current.Text))
            {
                diagnostics.Add(MakeDiagnostic(
                    MachinaTextDiagnosticCode.UnsupportedSyntax,
                    "Task lists are not supported.",
                    current.Index,
                    Math.Max(1, current.Text.Length),
                    current.Line,
                    1));
            }

            if (currentBullet.Value.Depth > MaximumListDepth)
            {
                diagnostics.Add(MakeDiagnostic(
                    MachinaTextDiagnosticCode.MaxListDepthExceeded,
                    "Maximum bullet depth is 2.",
                    current.Index,
                    Math.Max(1, current.Text.Length),
                    current.Line,
                    1));

                var trimmedStartOffset = current.Text.Length - current.Text.TrimStart().Length;
                var parsedOverflow = ParseInline(current.Text.Trim(), current.Index + trimmedStartOffset, current.Line);
                diagnostics.AddRange(parsedOverflow.Diagnostics);
                deferredBlocks.Add(new ParagraphBlock(PreserveInline(parsedOverflow.Inline, current.Text)));
                lineIndex += 1;
                continue;
            }

            var contentIndex = current.Index + (currentBullet.Value.Depth == 1 ? 2 : 4);
            var parsed = ParseInline(currentBullet.Value.Text, contentIndex, current.Line);
            diagnostics.AddRange(parsed.Diagnostics);

            if (currentBullet.Value.Depth == 1)
            {
                items.Add(new MachinaBulletItem(parsed.Inline));
            }
            else if (items.Count > 0)
            {
                var topItemIndex = items.Count - 1;
                var existingChildren = items[topItemIndex].Children?.ToList() ?? [];
                existingChildren.Add(new MachinaBulletItem(parsed.Inline));
                items[topItemIndex] = new MachinaBulletItem(items[topItemIndex].Inline, existingChildren);
            }
            else
            {
                diagnostics.Add(MakeDiagnostic(
                    MachinaTextDiagnosticCode.UnsupportedSyntax,
                    "Nested bullet requires a parent bullet.",
                    current.Index,
                    Math.Max(1, current.Text.Length),
                    current.Line,
                    1));
                deferredBlocks.Add(new ParagraphBlock([new TextRun(current.Text)]));
            }

            lineIndex += 1;
        }

        blocks.Add(new BulletListBlock(items));
        blocks.AddRange(deferredBlocks);
    }

    private static IReadOnlyList<MachinaInline> PreserveInline(IReadOnlyList<MachinaInline> parsedInline, string fallback)
    {
        if (parsedInline.Count > 0)
        {
            return parsedInline;
        }

        return [new TextRun(fallback)];
    }

    private static InlineParseResult ParseInline(string text, int sourceIndex, int line)
    {
        var diagnostics = new List<MachinaTextDiagnostic>();
        var inline = new List<MachinaInline>();
        var cursor = 0;

        while (cursor < text.Length)
        {
            if (ConsumeEscape(text, sourceIndex, line, inline, diagnostics, ref cursor))
            {
                continue;
            }

            if (TextStartsWith(text, cursor, "!["))
            {
                diagnostics.Add(MakeDiagnostic(
                    MachinaTextDiagnosticCode.UnsupportedSyntax,
                    "Images are not supported.",
                    sourceIndex + cursor,
                    2,
                    line,
                    cursor + 1));
                PushText(inline, "![");
                cursor += 2;
                continue;
            }

            if (text[cursor] == '`')
            {
                var close = text.IndexOf('`', cursor + 1);
                if (close < 0)
                {
                    diagnostics.Add(MakeDiagnostic(
                        MachinaTextDiagnosticCode.UnclosedInline,
                        "Unclosed inline code marker.",
                        sourceIndex + cursor,
                        text.Length - cursor,
                        line,
                        cursor + 1));
                    PushText(inline, text[cursor..]);
                    break;
                }

                inline.Add(new CodeRun(text[(cursor + 1)..close]));
                cursor = close + 1;
                continue;
            }

            if (TextStartsWith(text, cursor, "**"))
            {
                var close = text.IndexOf("**", cursor + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    diagnostics.Add(MakeDiagnostic(
                        MachinaTextDiagnosticCode.UnclosedInline,
                        "Unclosed strong marker.",
                        sourceIndex + cursor,
                        text.Length - cursor,
                        line,
                        cursor + 1));
                    PushText(inline, text[cursor..]);
                    break;
                }

                var parsedChildren = ParseInline(text[(cursor + 2)..close], sourceIndex + cursor + 2, line);
                diagnostics.AddRange(parsedChildren.Diagnostics);
                inline.Add(new StrongRun(parsedChildren.Inline));
                cursor = close + 2;
                continue;
            }

            if (text[cursor] == '*')
            {
                var close = text.IndexOf('*', cursor + 1);
                if (close < 0)
                {
                    diagnostics.Add(MakeDiagnostic(
                        MachinaTextDiagnosticCode.UnclosedInline,
                        "Unclosed emphasis marker.",
                        sourceIndex + cursor,
                        text.Length - cursor,
                        line,
                        cursor + 1));
                    PushText(inline, text[cursor..]);
                    break;
                }

                var parsedChildren = ParseInline(text[(cursor + 1)..close], sourceIndex + cursor + 1, line);
                diagnostics.AddRange(parsedChildren.Diagnostics);
                inline.Add(new EmphasisRun(parsedChildren.Inline));
                cursor = close + 1;
                continue;
            }

            if (text[cursor] == '[')
            {
                ParseLink(text, sourceIndex, line, inline, diagnostics, ref cursor);
                continue;
            }

            var next = FindNextSpecial(text, cursor);
            if (next == cursor)
            {
                PushText(inline, text[cursor].ToString());
                cursor += 1;
                continue;
            }

            PushText(inline, text[cursor..next]);
            cursor = next;
        }

        return new InlineParseResult(inline, diagnostics);
    }

    private static void ParseLink(
        string text,
        int sourceIndex,
        int line,
        List<MachinaInline> inline,
        List<MachinaTextDiagnostic> diagnostics,
        ref int cursor)
    {
        var closeBracket = text.IndexOf(']', cursor + 1);
        if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
        {
            diagnostics.Add(MakeDiagnostic(
                MachinaTextDiagnosticCode.MalformedLink,
                "Malformed link syntax.",
                sourceIndex + cursor,
                Math.Max(1, text.Length - cursor),
                line,
                cursor + 1));
            PushText(inline, "[");
            cursor += 1;
            return;
        }

        var closeParen = text.IndexOf(')', closeBracket + 2);
        if (closeParen < 0)
        {
            diagnostics.Add(MakeDiagnostic(
                MachinaTextDiagnosticCode.MalformedLink,
                "Malformed link syntax.",
                sourceIndex + cursor,
                text.Length - cursor,
                line,
                cursor + 1));
            PushText(inline, text[cursor..]);
            cursor = text.Length;
            return;
        }

        var label = text[(cursor + 1)..closeBracket];
        var href = text[(closeBracket + 2)..closeParen];
        if (label.Length == 0)
        {
            diagnostics.Add(MakeDiagnostic(
                MachinaTextDiagnosticCode.MalformedLink,
                "Link label cannot be empty.",
                sourceIndex + cursor,
                closeParen - cursor + 1,
                line,
                cursor + 1));
            PushText(inline, text[cursor..(closeParen + 1)]);
            cursor = closeParen + 1;
            return;
        }

        var parsedLabel = ParseInline(label, sourceIndex + cursor + 1, line);
        diagnostics.AddRange(parsedLabel.Diagnostics);
        inline.Add(new LinkRun(href, parsedLabel.Inline));
        cursor = closeParen + 1;
    }

    private static bool ConsumeEscape(
        string text,
        int sourceIndex,
        int line,
        List<MachinaInline> inline,
        List<MachinaTextDiagnostic> diagnostics,
        ref int cursor)
    {
        if (text[cursor] != '\\')
        {
            return false;
        }

        if (cursor == text.Length - 1)
        {
            diagnostics.Add(MakeDiagnostic(
                MachinaTextDiagnosticCode.InvalidEscape,
                "Dangling escape sequence.",
                sourceIndex + cursor,
                1,
                line,
                cursor + 1));
            PushText(inline, "\\");
            cursor += 1;
            return true;
        }

        var escaped = text[cursor + 1];
        if (AllowedEscapes.Contains(escaped))
        {
            PushText(inline, escaped.ToString());
            cursor += 2;
            return true;
        }

        diagnostics.Add(MakeDiagnostic(
            MachinaTextDiagnosticCode.InvalidEscape,
            $"Unsupported escape sequence: \\{escaped}",
            sourceIndex + cursor,
            2,
            line,
            cursor + 1));
        PushText(inline, escaped.ToString());
        cursor += 2;
        return true;
    }

    private static void PushText(List<MachinaInline> inline, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (inline.LastOrDefault() is TextRun previous)
        {
            inline[^1] = new TextRun(previous.Text + text);
            return;
        }

        inline.Add(new TextRun(text));
    }

    private static bool TextStartsWith(string text, int startIndex, string value)
    {
        return startIndex <= text.Length && text[startIndex..].StartsWith(value, StringComparison.Ordinal);
    }

    private static int FindNextSpecial(string text, int cursor)
    {
        var next = text.Length;
        var specials = new[] { "![", "`", "**", "*", "[", "\\" };

        foreach (var special in specials)
        {
            var index = text.IndexOf(special, cursor, StringComparison.Ordinal);
            if (index >= 0 && index < next)
            {
                next = index;
            }
        }

        return next;
    }

    private static MachinaTextDiagnostic MakeDiagnostic(
        MachinaTextDiagnosticCode code,
        string message,
        int index,
        int length,
        int line,
        int column)
    {
        return new MachinaTextDiagnostic(
            code,
            message,
            index,
            length,
            line,
            column,
            MachinaTextDiagnosticLevel.Error);
    }

    private static MachinaTextDiagnosticCode? ClassifyForbiddenBlock(string line)
    {
        if (IsHeadingLine(line))
        {
            return MachinaTextDiagnosticCode.HeadingForbidden;
        }

        if (IsOrderedListLine(line) ||
            IsTaskListLine(line) ||
            IsBlockQuoteLine(line) ||
            IsFencedCodeLine(line) ||
            IsHtmlLine(line) ||
            IsTableSeparatorLine(line))
        {
            return MachinaTextDiagnosticCode.UnsupportedSyntax;
        }

        return null;
    }

    private static BulletLine? ParseBulletLine(string line)
    {
        if (line.StartsWith("\\- ", StringComparison.Ordinal))
        {
            return null;
        }

        if (line.StartsWith("- ", StringComparison.Ordinal))
        {
            return new BulletLine(1, line[2..]);
        }

        if (line.StartsWith("  - ", StringComparison.Ordinal))
        {
            return new BulletLine(2, line[4..]);
        }

        if (line.StartsWith("    - ", StringComparison.Ordinal))
        {
            return new BulletLine(3, line[6..]);
        }

        return null;
    }

    private static List<LineInfo> ToLines(string source)
    {
        var lines = new List<LineInfo>();
        var index = 0;
        var line = 1;

        while (index <= source.Length)
        {
            var start = index;
            while (index < source.Length && source[index] != '\n' && source[index] != '\r')
            {
                index += 1;
            }

            var text = source[start..index];
            lines.Add(new LineInfo(text, start, line));

            if (index >= source.Length)
            {
                break;
            }

            if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n')
            {
                index += 2;
            }
            else
            {
                index += 1;
            }

            line += 1;
        }

        return lines;
    }

    private static bool IsHeadingLine(string line)
    {
        var hashCount = 0;
        while (hashCount < line.Length && line[hashCount] == '#')
        {
            hashCount += 1;
        }

        return hashCount is >= 1 and <= 6 && hashCount < line.Length && char.IsWhiteSpace(line[hashCount]);
    }

    private static bool IsOrderedListLine(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsDigit(line[index]))
        {
            index += 1;
        }

        return index > 0 && index + 1 < line.Length && line[index] == '.' && char.IsWhiteSpace(line[index + 1]);
    }

    private static bool IsTaskListLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("- [ ] ", StringComparison.Ordinal) ||
            trimmed.StartsWith("- [x] ", StringComparison.Ordinal) ||
            trimmed.StartsWith("- [X] ", StringComparison.Ordinal);
    }

    private static bool IsBlockQuoteLine(string line)
    {
        return line.StartsWith("> ", StringComparison.Ordinal);
    }

    private static bool IsFencedCodeLine(string line)
    {
        return line.StartsWith("```", StringComparison.Ordinal);
    }

    private static bool IsHtmlLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("<", StringComparison.Ordinal) && trimmed.Length > 1 && char.IsLetter(trimmed[1]);
    }

    private static bool IsTableSeparatorLine(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.Contains('|', StringComparison.Ordinal) || !trimmed.Contains("---", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (character != '|' && character != ':' && character != '-' && !char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct LineInfo(string Text, int Index, int Line);

    private readonly record struct BulletLine(int Depth, string Text);

    private sealed record InlineParseResult(
        IReadOnlyList<MachinaInline> Inline,
        IReadOnlyList<MachinaTextDiagnostic> Diagnostics);

}
