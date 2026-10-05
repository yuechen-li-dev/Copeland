namespace Copeland.Markdown;

/// <summary>
/// Shared bounded inline frontend used by Markdown and Text XML. Callers keep
/// their own outer syntax tree; this parser only recognizes inline meaning.
/// </summary>
public static class MarkdownInlineParser
{
    public static InlineParseResult Parse(MarkdownSourceText source, int start, string text)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);

        List<MarkdownDiagnostic> diagnostics = [];
        List<MarkdownInline> inlines = [];
        int cursor = 0;

        while (cursor < text.Length)
        {
            if (text[cursor] == '`')
            {
                int close = text.IndexOf('`', cursor + 1);
                if (close < 0)
                {
                    diagnostics.Add(new MarkdownDiagnostic(
                        MarkdownDiagnosticIds.UnmatchedInlineCodeMarker,
                        "Unclosed inline code marker.",
                        MarkdownDiagnosticSeverity.Error,
                        source.CreateSpan(start + cursor, text.Length - cursor)));
                    PushText(inlines, source, start + cursor, text[cursor..]);
                    break;
                }

                inlines.Add(new CodeInline(
                    text[(cursor + 1)..close],
                    source.CreateSpan(start + cursor, close - cursor + 1)));
                cursor = close + 1;
                continue;
            }

            if (StartsWith(text, cursor, "**"))
            {
                int close = text.IndexOf("**", cursor + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    diagnostics.Add(new MarkdownDiagnostic(
                        MarkdownDiagnosticIds.UnmatchedStrongMarker,
                        "Unclosed strong marker.",
                        MarkdownDiagnosticSeverity.Error,
                        source.CreateSpan(start + cursor, text.Length - cursor)));
                    PushText(inlines, source, start + cursor, "**");
                    cursor += 2;
                    continue;
                }

                InlineParseResult children = Parse(source, start + cursor + 2, text[(cursor + 2)..close]);
                diagnostics.AddRange(children.Diagnostics);
                inlines.Add(new StrongInline(
                    children.Inlines,
                    source.CreateSpan(start + cursor, close - cursor + 2)));
                cursor = close + 2;
                continue;
            }

            if (text[cursor] == '*')
            {
                int close = text.IndexOf('*', cursor + 1);
                if (close < 0)
                {
                    diagnostics.Add(new MarkdownDiagnostic(
                        MarkdownDiagnosticIds.UnmatchedEmphasisMarker,
                        "Unclosed emphasis marker.",
                        MarkdownDiagnosticSeverity.Error,
                        source.CreateSpan(start + cursor, text.Length - cursor)));
                    PushText(inlines, source, start + cursor, "*");
                    cursor += 1;
                    continue;
                }

                InlineParseResult children = Parse(source, start + cursor + 1, text[(cursor + 1)..close]);
                diagnostics.AddRange(children.Diagnostics);
                inlines.Add(new EmphasisInline(
                    children.Inlines,
                    source.CreateSpan(start + cursor, close - cursor + 1)));
                cursor = close + 1;
                continue;
            }

            if (StartsWith(text, cursor, "!["))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownDiagnosticIds.UnsupportedInlineSyntax,
                    "Image syntax is not supported in M12a.",
                    MarkdownDiagnosticSeverity.Warning,
                    source.CreateSpan(start + cursor, 2)));
                PushText(inlines, source, start + cursor, "![");
                cursor += 2;
                continue;
            }

            if (text[cursor] == '[')
            {
                if (TryParseLink(source, start, text, diagnostics, inlines, ref cursor))
                {
                    continue;
                }
            }

            int next = FindNextSpecial(text, cursor);
            PushText(inlines, source, start + cursor, text[cursor..next]);
            cursor = next;
        }

        return new InlineParseResult(inlines, diagnostics);
    }

    private static bool TryParseLink(
        MarkdownSourceText source,
        int start,
        string text,
        List<MarkdownDiagnostic> diagnostics,
        List<MarkdownInline> inlines,
        ref int cursor)
    {
        int openBracket = cursor;
        int closeBracket = text.IndexOf(']', cursor + 1);
        if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownDiagnosticIds.MalformedLink,
                "Malformed link syntax.",
                MarkdownDiagnosticSeverity.Error,
                source.CreateSpan(start + openBracket, text.Length - openBracket)));
            PushText(inlines, source, start + openBracket, "[");
            cursor += 1;
            return true;
        }

        int closeParen = text.IndexOf(')', closeBracket + 2);
        if (closeParen < 0)
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownDiagnosticIds.MalformedLink,
                "Malformed link syntax.",
                MarkdownDiagnosticSeverity.Error,
                source.CreateSpan(start + openBracket, text.Length - openBracket)));
            PushText(inlines, source, start + openBracket, text[openBracket..]);
            cursor = text.Length;
            return true;
        }

        string labelText = text[(openBracket + 1)..closeBracket];
        string target = text[(closeBracket + 2)..closeParen];

        if (labelText.Length == 0 || target.Length == 0)
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownDiagnosticIds.MalformedLink,
                "Link label and target must both be present.",
                MarkdownDiagnosticSeverity.Error,
                source.CreateSpan(start + openBracket, closeParen - openBracket + 1)));
            PushText(inlines, source, start + openBracket, text[openBracket..(closeParen + 1)]);
            cursor = closeParen + 1;
            return true;
        }

        InlineParseResult label = Parse(source, start + openBracket + 1, labelText);
        diagnostics.AddRange(label.Diagnostics);
        inlines.Add(new LinkInline(
            label.Inlines,
            target,
            source.CreateSpan(start + openBracket, closeParen - openBracket + 1)));
        cursor = closeParen + 1;
        return true;
    }

    private static void PushText(
        List<MarkdownInline> inlines,
        MarkdownSourceText source,
        int absoluteStart,
        string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        SourceSpan span = source.CreateSpan(absoluteStart, text.Length);
        if (inlines.LastOrDefault() is TextInline previous &&
            previous.Span.End == span.Start)
        {
            inlines[^1] = new TextInline(
                previous.Text + text,
                source.CreateSpan(previous.Span.Start, previous.Span.Length + span.Length));
            return;
        }

        inlines.Add(new TextInline(text, span));
    }

    private static int FindNextSpecial(string text, int cursor)
    {
        int next = text.Length;
        string[] specials = ["`", "**", "*", "[", "!["];

        foreach (string special in specials)
        {
            int index = text.IndexOf(special, cursor, StringComparison.Ordinal);
            if (index >= 0 && index < next)
            {
                next = index;
            }
        }

        return Math.Max(cursor + 1, next);
    }

    private static bool StartsWith(string text, int start, string value)
    {
        return start <= text.Length && text[start..].StartsWith(value, StringComparison.Ordinal);
    }
}

public sealed record InlineParseResult(
    IReadOnlyList<MarkdownInline> Inlines,
    IReadOnlyList<MarkdownDiagnostic> Diagnostics);
