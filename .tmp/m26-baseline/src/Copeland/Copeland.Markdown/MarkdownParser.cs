namespace Copeland.Markdown;

public static class MarkdownParser
{
    public static MarkdownDocument Parse(string text)
    {
        MarkdownTokenizedSource tokenizedSource = MarkdownLexer.Tokenize(text);
        return Parse(tokenizedSource);
    }

    public static MarkdownDocument Parse(MarkdownTokenizedSource tokenizedSource)
    {
        ArgumentNullException.ThrowIfNull(tokenizedSource);

        List<MarkdownBlock> blocks = [];
        List<MarkdownDiagnostic> diagnostics = [];
        IReadOnlyList<MarkdownLine> lines = tokenizedSource.Lines;
        MarkdownSourceText source = tokenizedSource.Source;
        int lineIndex = 0;

        while (lineIndex < lines.Count)
        {
            MarkdownLine line = lines[lineIndex];

            if (line.IsBlank)
            {
                lineIndex += 1;
                continue;
            }

            if (TryParseCodeFence(lines, source, diagnostics, ref lineIndex, out CodeFenceBlock? codeFence))
            {
                blocks.Add(codeFence!);
                continue;
            }

            if (TryParseThematicBreak(line, out ThematicBreakBlock? thematicBreak))
            {
                blocks.Add(thematicBreak!);
                lineIndex += 1;
                continue;
            }

            if (TryParseHeading(line, source, diagnostics, out HeadingBlock? heading))
            {
                blocks.Add(heading!);
                lineIndex += 1;
                continue;
            }

            if (LooksLikeMalformedHeading(line.Text))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownDiagnosticIds.MalformedHeadingMarker,
                    "Heading markers must use 1 to 6 '#' characters followed by a space.",
                    MarkdownDiagnosticSeverity.Warning,
                    line.Span));
            }

            if (TryParseList(lines, source, diagnostics, ref lineIndex, out MarkdownBlock? listBlock))
            {
                blocks.Add(listBlock!);
                continue;
            }

            if (IsUnsupportedBlockSyntax(line.Text))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownDiagnosticIds.UnsupportedBlockSyntax,
                    "Unsupported block syntax encountered. Preserving line as paragraph text.",
                    MarkdownDiagnosticSeverity.Warning,
                    line.Span));
            }

            blocks.Add(ParseParagraph(lines, source, diagnostics, ref lineIndex));
        }

        return new MarkdownDocument(
            blocks,
            diagnostics,
            source.CreateSpan(0, source.Text.Length));
    }

    private static bool TryParseCodeFence(
        IReadOnlyList<MarkdownLine> lines,
        MarkdownSourceText source,
        List<MarkdownDiagnostic> diagnostics,
        ref int lineIndex,
        out CodeFenceBlock? block)
    {
        MarkdownLine line = lines[lineIndex];
        string trimmed = line.Text.TrimStart();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            block = null;
            return false;
        }

        int fenceMarkerIndex = line.Text.IndexOf("```", StringComparison.Ordinal);
        string language = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
        int start = line.Span.Start;
        int contentStartIndex = lineIndex + 1;
        int cursor = contentStartIndex;

        while (cursor < lines.Count)
        {
            string currentTrimmed = lines[cursor].Text.TrimStart();
            if (currentTrimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int end = lines[cursor].Span.End;
                string text = SliceBlockBody(source, lines, contentStartIndex, cursor);
                block = new CodeFenceBlock(
                    string.IsNullOrWhiteSpace(language) ? null : language,
                    text,
                    source.CreateSpan(start, end - start));
                lineIndex = cursor + 1;
                return true;
            }

            cursor += 1;
        }

        string unterminatedText = SliceBlockBody(source, lines, contentStartIndex, lines.Count);
        int unterminatedEnd = lines[^1].Span.End;
        SourceSpan diagnosticSpan = source.CreateSpan(start + fenceMarkerIndex, line.Span.End - (start + fenceMarkerIndex));
        diagnostics.Add(new MarkdownDiagnostic(
            MarkdownDiagnosticIds.UnclosedCodeFence,
            "Unclosed code fence.",
            MarkdownDiagnosticSeverity.Error,
            diagnosticSpan));

        block = new CodeFenceBlock(
            string.IsNullOrWhiteSpace(language) ? null : language,
            unterminatedText,
            source.CreateSpan(start, unterminatedEnd - start));
        lineIndex = lines.Count;
        return true;
    }

    private static bool TryParseThematicBreak(MarkdownLine line, out ThematicBreakBlock? block)
    {
        string trimmed = line.Text.Trim();
        if (IsThematicBreakText(trimmed))
        {
            block = new ThematicBreakBlock(line.Span);
            return true;
        }

        block = null;
        return false;
    }

    private static bool TryParseHeading(
        MarkdownLine line,
        MarkdownSourceText source,
        List<MarkdownDiagnostic> diagnostics,
        out HeadingBlock? block)
    {
        int hashCount = 0;
        while (hashCount < line.Text.Length && line.Text[hashCount] == '#')
        {
            hashCount += 1;
        }

        if (hashCount is < 1 or > 6)
        {
            block = null;
            return false;
        }

        if (hashCount >= line.Text.Length || !char.IsWhiteSpace(line.Text[hashCount]))
        {
            block = null;
            return false;
        }

        int contentStart = hashCount + 1;
        while (contentStart < line.Text.Length && char.IsWhiteSpace(line.Text[contentStart]))
        {
            contentStart += 1;
        }

        string content = contentStart < line.Text.Length ? line.Text[contentStart..] : string.Empty;
        InlineParseResult parsed = MarkdownInlineParser.Parse(source, line.Span.Start + contentStart, content);
        diagnostics.AddRange(parsed.Diagnostics);
        block = new HeadingBlock(hashCount, parsed.Inlines, line.Span);
        return true;
    }

    private static bool TryParseList(
        IReadOnlyList<MarkdownLine> lines,
        MarkdownSourceText source,
        List<MarkdownDiagnostic> diagnostics,
        ref int lineIndex,
        out MarkdownBlock? block)
    {
        if (TryClassifyListMarker(lines[lineIndex].Text, out ListMarker marker) is false)
        {
            block = null;
            return false;
        }

        if (marker.Indent > 0)
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownDiagnosticIds.NestedListNotSupported,
                "Nested lists are not supported in M12a.",
                MarkdownDiagnosticSeverity.Warning,
                lines[lineIndex].Span));
            block = null;
            return false;
        }

        DocumentListKind kind = marker.Kind;
        List<ListItemBlock> items = [];
        int start = lines[lineIndex].Span.Start;
        int cursor = lineIndex;

        while (cursor < lines.Count)
        {
            MarkdownLine current = lines[cursor];
            if (current.IsBlank)
            {
                break;
            }

            if (!TryClassifyListMarker(current.Text, out ListMarker currentMarker))
            {
                if (LooksLikeNestedList(current.Text))
                {
                    diagnostics.Add(new MarkdownDiagnostic(
                        MarkdownDiagnosticIds.NestedListNotSupported,
                        "Nested lists are not supported in M12a.",
                        MarkdownDiagnosticSeverity.Warning,
                        current.Span));
                }

                break;
            }

            if (currentMarker.Kind != kind)
            {
                break;
            }

            if (currentMarker.Indent > 0)
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownDiagnosticIds.NestedListNotSupported,
                    "Nested lists are not supported in M12a.",
                    MarkdownDiagnosticSeverity.Warning,
                    current.Span));
                break;
            }

            if (LooksLikeTaskList(currentMarker.Content))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownDiagnosticIds.UnsupportedBlockSyntax,
                    "Task list markers are not supported in M12a. Preserving the item as ordinary list text.",
                    MarkdownDiagnosticSeverity.Warning,
                    current.Span));
            }

            InlineParseResult parsed = MarkdownInlineParser.Parse(source, current.Span.Start + currentMarker.ContentStart, currentMarker.Content);
            diagnostics.AddRange(parsed.Diagnostics);
            items.Add(new ListItemBlock(parsed.Inlines, current.Span));
            cursor += 1;
        }

        SourceSpan span = source.CreateSpan(start, lines[cursor - 1].Span.End - start);
        block = kind == DocumentListKind.Bullet
            ? new BulletListBlock(items, span)
            : new OrderedListBlock(items, span);
        lineIndex = cursor;
        return true;
    }

    private static ParagraphBlock ParseParagraph(
        IReadOnlyList<MarkdownLine> lines,
        MarkdownSourceText source,
        List<MarkdownDiagnostic> diagnostics,
        ref int lineIndex)
    {
        List<MarkdownLine> paragraphLines = [];
        int start = lines[lineIndex].Span.Start;

        while (lineIndex < lines.Count)
        {
            MarkdownLine current = lines[lineIndex];
            if (current.IsBlank)
            {
                break;
            }

            if (paragraphLines.Count > 0 && IsParagraphTerminator(current.Text))
            {
                break;
            }

            paragraphLines.Add(current);
            lineIndex += 1;
        }

        int end = paragraphLines[^1].Span.End;
        string text = source.Text.Substring(start, end - start);
        InlineParseResult parsed = MarkdownInlineParser.Parse(source, start, text);
        diagnostics.AddRange(parsed.Diagnostics);
        return new ParagraphBlock(parsed.Inlines, source.CreateSpan(start, end - start));
    }

    private static bool IsParagraphTerminator(string text)
    {
        return TryClassifyListMarker(text, out _) ||
            text.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
            IsThematicBreakText(text.Trim()) ||
            IsHeadingLine(text);
    }

    private static bool IsHeadingLine(string text)
    {
        int hashCount = 0;
        while (hashCount < text.Length && text[hashCount] == '#')
        {
            hashCount += 1;
        }

        return hashCount is >= 1 and <= 6 &&
            hashCount < text.Length &&
            char.IsWhiteSpace(text[hashCount]);
    }

    private static string SliceBlockBody(
        MarkdownSourceText source,
        IReadOnlyList<MarkdownLine> lines,
        int startLineIndex,
        int endLineIndexExclusive)
    {
        if (startLineIndex >= endLineIndexExclusive)
        {
            return string.Empty;
        }

        int start = lines[startLineIndex].Span.Start;
        int end = lines[endLineIndexExclusive - 1].Span.End;
        return source.Text.Substring(start, end - start);
    }

    private static bool TryClassifyListMarker(string text, out ListMarker marker)
    {
        int indent = text.Length - text.TrimStart().Length;
        string trimmed = text.TrimStart();

        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            marker = new ListMarker(
                DocumentListKind.Bullet,
                indent,
                indent + 2,
                trimmed[2..]);
            return true;
        }

        int digitLength = 0;
        while (digitLength < trimmed.Length && char.IsDigit(trimmed[digitLength]))
        {
            digitLength += 1;
        }

        if (digitLength > 0 &&
            digitLength + 1 < trimmed.Length &&
            trimmed[digitLength] == '.' &&
            char.IsWhiteSpace(trimmed[digitLength + 1]))
        {
            int contentStart = indent + digitLength + 2;
            marker = new ListMarker(
                DocumentListKind.Ordered,
                indent,
                contentStart,
                trimmed[(digitLength + 2)..]);
            return true;
        }

        marker = default;
        return false;
    }

    private static bool IsUnsupportedBlockSyntax(string text)
    {
        string trimmed = text.TrimStart();
        return trimmed.StartsWith("> ", StringComparison.Ordinal) ||
            trimmed.StartsWith("- [ ] ", StringComparison.Ordinal) ||
            trimmed.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<", StringComparison.Ordinal) ||
            LooksLikeTableSeparator(trimmed);
    }

    private static bool LooksLikeMalformedHeading(string text)
    {
        int hashCount = 0;
        while (hashCount < text.Length && text[hashCount] == '#')
        {
            hashCount += 1;
        }

        return hashCount > 6 && hashCount < text.Length && char.IsWhiteSpace(text[hashCount]);
    }

    private static bool LooksLikeNestedList(string text)
    {
        int indent = text.Length - text.TrimStart().Length;
        string trimmed = text.TrimStart();
        return indent > 0 &&
            (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
             trimmed.StartsWith("* ", StringComparison.Ordinal) ||
             char.IsDigit(trimmed.FirstOrDefault()));
    }

    private static bool LooksLikeTaskList(string text)
    {
        return text.StartsWith("[ ] ", StringComparison.Ordinal) ||
            text.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTableSeparator(string trimmed)
    {
        if (!trimmed.Contains('|', StringComparison.Ordinal) || !trimmed.Contains("---", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in trimmed)
        {
            if (character != '|' &&
                character != ':' &&
                character != '-' &&
                !char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsThematicBreakText(string trimmed)
    {
        if (trimmed.Length < 3)
        {
            return false;
        }

        if (trimmed.All(static character => character == '-') ||
            trimmed.All(static character => character == '*'))
        {
            return true;
        }

        return false;
    }

    private readonly record struct ListMarker(
        DocumentListKind Kind,
        int Indent,
        int ContentStart,
        string Content);
}
