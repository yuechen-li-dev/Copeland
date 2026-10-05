using System.Text;
using System.Text.Json;

namespace Copeland.Markdown;

public static class MarkdownDumpWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string DumpTokens(MarkdownTokenizedSource tokenizedSource)
    {
        ArgumentNullException.ThrowIfNull(tokenizedSource);

        StringBuilder builder = new();
        foreach (MarkdownToken token in tokenizedSource.Tokens)
        {
            builder.Append(token.Kind);
            builder.Append(' ');
            builder.Append(token.Span.StartLocation);
            builder.Append(' ');
            builder.Append('"');
            builder.Append(EscapeText(token.Text));
            builder.AppendLine("\"");
        }

        return builder.ToString();
    }

    public static string DumpSyntax(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        StringBuilder builder = new();
        builder.AppendLine("MarkdownDocument");
        foreach (MarkdownBlock block in document.Blocks)
        {
            DumpBlock(builder, block, 1);
        }

        if (document.Diagnostics.Count > 0)
        {
            builder.AppendLine("Diagnostics");
            foreach (MarkdownDiagnostic diagnostic in document.Diagnostics)
            {
                builder.Append("  ");
                builder.Append(diagnostic.Id);
                builder.Append(' ');
                builder.Append(diagnostic.Severity);
                builder.Append(' ');
                builder.Append(diagnostic.Span.StartLocation);
                builder.Append(" ");
                builder.AppendLine(diagnostic.Message);
            }
        }

        return builder.ToString();
    }

    public static string DumpMir(DocumentMir mir)
    {
        ArgumentNullException.ThrowIfNull(mir);

        StringBuilder builder = new();
        builder.AppendLine("DocumentMir");
        foreach (DocumentBlockMir block in mir.Blocks)
        {
            DumpMirBlock(builder, block, 1);
        }

        if (mir.Diagnostics.Count > 0)
        {
            builder.AppendLine("Diagnostics");
            foreach (DocumentDiagnostic diagnostic in mir.Diagnostics)
            {
                builder.Append("  ");
                builder.Append(diagnostic.Id);
                builder.Append(' ');
                builder.Append(diagnostic.Severity);
                builder.Append(' ');
                builder.Append(diagnostic.Span.StartLocation);
                builder.Append(" ");
                builder.AppendLine(diagnostic.Message);
            }
        }

        return builder.ToString();
    }

    public static string DumpDiagnostics(IReadOnlyList<MarkdownDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        StringBuilder builder = new();
        foreach (MarkdownDiagnostic diagnostic in diagnostics)
        {
            builder.Append(diagnostic.Id);
            builder.Append(" | ");
            builder.Append(diagnostic.Severity);
            builder.Append(" | ");
            builder.Append(diagnostic.Span.StartLocation.Line);
            builder.Append(':');
            builder.Append(diagnostic.Span.StartLocation.Column);
            builder.Append(" | ");
            builder.AppendLine(diagnostic.Message);
        }

        return builder.ToString();
    }

    public static string SerializeSyntaxAsJson(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(ToSyntaxJson(document), JsonOptions);
    }

    public static string SerializeMirAsJson(DocumentMir mir)
    {
        ArgumentNullException.ThrowIfNull(mir);
        return JsonSerializer.Serialize(ToMirJson(mir), JsonOptions);
    }

    public static string SerializeCorpusReportAsJson(MarkdownCorpusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string DumpCorpusReport(MarkdownCorpusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new();
        builder.AppendLine("Copeland Markdown Corpus Report");
        builder.Append("Dialect: ");
        builder.AppendLine(report.DialectName);
        builder.Append("Documents: ");
        builder.AppendLine(report.Documents.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Append("Diagnostics: ");
        builder.AppendLine(report.TotalDiagnostics.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.AppendLine();

        foreach (MarkdownCorpusDocumentReport document in report.Documents)
        {
            builder.Append(document.RelativePath);
            builder.Append(" | blocks=");
            builder.Append(document.BlockCount);
            builder.Append(" | diagnostics=");
            builder.AppendLine(document.DiagnosticCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

            foreach (string diagnostic in document.Diagnostics)
            {
                builder.Append("  ");
                builder.AppendLine(diagnostic);
            }
        }

        return builder.ToString();
    }

    private static object ToSyntaxJson(MarkdownDocument document)
    {
        return new
        {
            kind = "MarkdownDocument",
            span = ToSpanJson(document.Span),
            diagnostics = document.Diagnostics.Select(ToDiagnosticJson).ToArray(),
            blocks = document.Blocks.Select(ToSyntaxBlockJson).ToArray(),
        };
    }

    private static object ToMirJson(DocumentMir mir)
    {
        return new
        {
            kind = "DocumentMir",
            diagnostics = mir.Diagnostics.Select(ToDocumentDiagnosticJson).ToArray(),
            blocks = mir.Blocks.Select(ToMirBlockJson).ToArray(),
        };
    }

    private static object ToSyntaxBlockJson(MarkdownBlock block)
    {
        return block switch
        {
            HeadingBlock heading => new
            {
                kind = "Heading",
                level = heading.Level,
                span = ToSpanJson(heading.Span),
                inlines = heading.Inlines.Select(ToSyntaxInlineJson).ToArray(),
            },
            ParagraphBlock paragraph => new
            {
                kind = "Paragraph",
                span = ToSpanJson(paragraph.Span),
                inlines = paragraph.Inlines.Select(ToSyntaxInlineJson).ToArray(),
            },
            BulletListBlock bulletList => new
            {
                kind = "BulletList",
                span = ToSpanJson(bulletList.Span),
                items = bulletList.Items.Select(ToListItemJson).ToArray(),
            },
            OrderedListBlock orderedList => new
            {
                kind = "OrderedList",
                span = ToSpanJson(orderedList.Span),
                items = orderedList.Items.Select(ToListItemJson).ToArray(),
            },
            CodeFenceBlock codeFence => new
            {
                kind = "CodeFence",
                span = ToSpanJson(codeFence.Span),
                language = codeFence.Language,
                text = codeFence.Text,
            },
            ThematicBreakBlock thematicBreak => new
            {
                kind = "ThematicBreak",
                span = ToSpanJson(thematicBreak.Span),
            },
            _ => throw new InvalidOperationException($"Unsupported Markdown block type: {block.GetType().Name}"),
        };
    }

    private static object ToSyntaxInlineJson(MarkdownInline inline)
    {
        return inline switch
        {
            TextInline text => new
            {
                kind = "Text",
                span = ToSpanJson(text.Span),
                text = text.Text,
            },
            CodeInline code => new
            {
                kind = "Code",
                span = ToSpanJson(code.Span),
                text = code.Text,
            },
            EmphasisInline emphasis => new
            {
                kind = "Emphasis",
                span = ToSpanJson(emphasis.Span),
                children = emphasis.Children.Select(ToSyntaxInlineJson).ToArray(),
            },
            StrongInline strong => new
            {
                kind = "Strong",
                span = ToSpanJson(strong.Span),
                children = strong.Children.Select(ToSyntaxInlineJson).ToArray(),
            },
            LinkInline link => new
            {
                kind = "Link",
                span = ToSpanJson(link.Span),
                target = link.Target,
                label = link.Label.Select(ToSyntaxInlineJson).ToArray(),
            },
            _ => throw new InvalidOperationException($"Unsupported Markdown inline type: {inline.GetType().Name}"),
        };
    }

    private static object ToMirBlockJson(DocumentBlockMir block)
    {
        return block switch
        {
            HeadingMir heading => new
            {
                kind = "Heading",
                level = heading.Level,
                span = ToSpanJson(heading.Span),
                inlines = heading.Inlines.Select(ToMirInlineJson).ToArray(),
            },
            ParagraphMir paragraph => new
            {
                kind = "Paragraph",
                span = ToSpanJson(paragraph.Span),
                inlines = paragraph.Inlines.Select(ToMirInlineJson).ToArray(),
            },
            ListMir list => new
            {
                kind = "List",
                listKind = list.Kind.ToString(),
                span = ToSpanJson(list.Span),
                items = list.Items.Select(ToMirListItemJson).ToArray(),
            },
            CodeBlockMir codeBlock => new
            {
                kind = "CodeBlock",
                span = ToSpanJson(codeBlock.Span),
                language = codeBlock.Language,
                text = codeBlock.Text,
            },
            ThematicBreakMir thematicBreak => new
            {
                kind = "ThematicBreak",
                span = ToSpanJson(thematicBreak.Span),
            },
            _ => throw new InvalidOperationException($"Unsupported document MIR block type: {block.GetType().Name}"),
        };
    }

    private static object ToMirInlineJson(DocumentInlineMir inline)
    {
        return inline switch
        {
            TextMir text => new
            {
                kind = "Text",
                span = ToSpanJson(text.Span),
                text = text.Text,
            },
            EmbeddedValueMir value => new
            {
                kind = "EmbeddedValue",
                span = ToSpanJson(value.Span),
                slot = value.SlotId,
            },
            CodeSpanMir code => new
            {
                kind = "Code",
                span = ToSpanJson(code.Span),
                text = code.Text,
            },
            EmphasisMir emphasis => new
            {
                kind = "Emphasis",
                span = ToSpanJson(emphasis.Span),
                children = emphasis.Children.Select(ToMirInlineJson).ToArray(),
            },
            StrongMir strong => new
            {
                kind = "Strong",
                span = ToSpanJson(strong.Span),
                children = strong.Children.Select(ToMirInlineJson).ToArray(),
            },
            LinkMir link => new
            {
                kind = "Link",
                span = ToSpanJson(link.Span),
                target = link.Target,
                label = link.Label.Select(ToMirInlineJson).ToArray(),
            },
            _ => throw new InvalidOperationException($"Unsupported document MIR inline type: {inline.GetType().Name}"),
        };
    }

    private static object ToListItemJson(ListItemBlock item)
    {
        return new
        {
            span = ToSpanJson(item.Span),
            inlines = item.Inlines.Select(ToSyntaxInlineJson).ToArray(),
        };
    }

    private static object ToMirListItemJson(ListItemMir item)
    {
        return new
        {
            span = ToSpanJson(item.Span),
            inlines = item.Inlines.Select(ToMirInlineJson).ToArray(),
        };
    }

    private static object ToDiagnosticJson(MarkdownDiagnostic diagnostic)
    {
        return new
        {
            id = diagnostic.Id,
            severity = diagnostic.Severity.ToString(),
            message = diagnostic.Message,
            span = ToSpanJson(diagnostic.Span),
        };
    }

    private static object ToDocumentDiagnosticJson(DocumentDiagnostic diagnostic)
    {
        return new
        {
            id = diagnostic.Id,
            severity = diagnostic.Severity.ToString(),
            message = diagnostic.Message,
            span = ToSpanJson(diagnostic.Span),
        };
    }

    private static object ToSpanJson(SourceSpan span)
    {
        return new
        {
            start = span.Start,
            length = span.Length,
            end = span.End,
            startLocation = new
            {
                index = span.StartLocation.Index,
                line = span.StartLocation.Line,
                column = span.StartLocation.Column,
            },
            endLocation = new
            {
                index = span.EndLocation.Index,
                line = span.EndLocation.Line,
                column = span.EndLocation.Column,
            },
        };
    }

    private static void DumpBlock(StringBuilder builder, MarkdownBlock block, int depth)
    {
        string indent = new(' ', depth * 2);
        switch (block)
        {
            case HeadingBlock heading:
                builder.Append(indent);
                builder.Append("Heading ");
                builder.AppendLine(heading.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
                DumpInlineList(builder, heading.Inlines, depth + 1);
                break;
            case ParagraphBlock paragraph:
                builder.Append(indent);
                builder.AppendLine("Paragraph");
                DumpInlineList(builder, paragraph.Inlines, depth + 1);
                break;
            case BulletListBlock bulletList:
                builder.Append(indent);
                builder.AppendLine("BulletList");
                foreach (ListItemBlock item in bulletList.Items)
                {
                    builder.Append(indent);
                    builder.AppendLine("  Item");
                    DumpInlineList(builder, item.Inlines, depth + 2);
                }
                break;
            case OrderedListBlock orderedList:
                builder.Append(indent);
                builder.AppendLine("OrderedList");
                foreach (ListItemBlock item in orderedList.Items)
                {
                    builder.Append(indent);
                    builder.AppendLine("  Item");
                    DumpInlineList(builder, item.Inlines, depth + 2);
                }
                break;
            case CodeFenceBlock codeFence:
                builder.Append(indent);
                builder.Append("CodeFence ");
                builder.AppendLine(codeFence.Language ?? "(none)");
                builder.Append(indent);
                builder.Append("  ");
                builder.AppendLine(EscapeText(codeFence.Text));
                break;
            case ThematicBreakBlock:
                builder.Append(indent);
                builder.AppendLine("ThematicBreak");
                break;
        }
    }

    private static void DumpInlineList(StringBuilder builder, IReadOnlyList<MarkdownInline> inlines, int depth)
    {
        string indent = new(' ', depth * 2);
        foreach (MarkdownInline inline in inlines)
        {
            switch (inline)
            {
                case TextInline text:
                    builder.Append(indent);
                    builder.Append("Text ");
                    builder.AppendLine(EscapeText(text.Text));
                    break;
                case CodeInline code:
                    builder.Append(indent);
                    builder.Append("Code ");
                    builder.AppendLine(EscapeText(code.Text));
                    break;
                case EmphasisInline emphasis:
                    builder.Append(indent);
                    builder.AppendLine("Emphasis");
                    DumpInlineList(builder, emphasis.Children, depth + 1);
                    break;
                case StrongInline strong:
                    builder.Append(indent);
                    builder.AppendLine("Strong");
                    DumpInlineList(builder, strong.Children, depth + 1);
                    break;
                case LinkInline link:
                    builder.Append(indent);
                    builder.Append("Link ");
                    builder.AppendLine(link.Target);
                    DumpInlineList(builder, link.Label, depth + 1);
                    break;
            }
        }
    }

    private static void DumpMirBlock(StringBuilder builder, DocumentBlockMir block, int depth)
    {
        string indent = new(' ', depth * 2);
        switch (block)
        {
            case HeadingMir heading:
                builder.Append(indent);
                builder.Append("Heading ");
                builder.AppendLine(heading.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
                DumpMirInlineList(builder, heading.Inlines, depth + 1);
                break;
            case ParagraphMir paragraph:
                builder.Append(indent);
                builder.AppendLine("Paragraph");
                DumpMirInlineList(builder, paragraph.Inlines, depth + 1);
                break;
            case ListMir list:
                builder.Append(indent);
                builder.Append("List ");
                builder.AppendLine(list.Kind.ToString());
                foreach (ListItemMir item in list.Items)
                {
                    builder.Append(indent);
                    builder.AppendLine("  Item");
                    DumpMirInlineList(builder, item.Inlines, depth + 2);
                }
                break;
            case CodeBlockMir codeBlock:
                builder.Append(indent);
                builder.Append("CodeBlock ");
                builder.AppendLine(codeBlock.Language ?? "(none)");
                builder.Append(indent);
                builder.Append("  ");
                builder.AppendLine(EscapeText(codeBlock.Text));
                break;
            case ThematicBreakMir:
                builder.Append(indent);
                builder.AppendLine("ThematicBreak");
                break;
        }
    }

    private static void DumpMirInlineList(StringBuilder builder, IReadOnlyList<DocumentInlineMir> inlines, int depth)
    {
        string indent = new(' ', depth * 2);
        foreach (DocumentInlineMir inline in inlines)
        {
            switch (inline)
            {
                case TextMir text:
                    builder.Append(indent);
                    builder.Append("Text ");
                    builder.AppendLine(EscapeText(text.Text));
                    break;
                case EmbeddedValueMir value:
                    builder.Append(indent);
                    builder.Append("EmbeddedValue ");
                    builder.AppendLine(value.SlotId);
                    break;
                case CodeSpanMir code:
                    builder.Append(indent);
                    builder.Append("Code ");
                    builder.AppendLine(EscapeText(code.Text));
                    break;
                case EmphasisMir emphasis:
                    builder.Append(indent);
                    builder.AppendLine("Emphasis");
                    DumpMirInlineList(builder, emphasis.Children, depth + 1);
                    break;
                case StrongMir strong:
                    builder.Append(indent);
                    builder.AppendLine("Strong");
                    DumpMirInlineList(builder, strong.Children, depth + 1);
                    break;
                case LinkMir link:
                    builder.Append(indent);
                    builder.Append("Link ");
                    builder.AppendLine(link.Target);
                    DumpMirInlineList(builder, link.Label, depth + 1);
                    break;
            }
        }
    }

    private static string EscapeText(string text)
    {
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
