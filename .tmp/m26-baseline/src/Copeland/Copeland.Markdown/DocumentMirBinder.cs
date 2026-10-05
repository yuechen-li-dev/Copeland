namespace Copeland.Markdown;

/// <summary>
/// Assigns stable semantic identities, frontend-aware provenance, default
/// roles, parent links, and shared link safety to the backend-neutral document
/// tree. It intentionally does not contain layout or DOM facts.
/// </summary>
public static class DocumentMirBinder
{
    public static DocumentMir Bind(
        DocumentMir document,
        string documentId,
        string? ownerSymbol,
        DocumentSourceKind sourceKind,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var diagnostics = document.Diagnostics.ToList();
        int blockOrder = 0;
        IReadOnlyList<DocumentBlockMir> blocks = document.Blocks
            .Select(block => BindBlock(block, parentNodeId: null))
            .ToArray();
        return new DocumentMir(blocks, diagnostics)
        {
            Metadata = new DocumentMetadata(
                documentId,
                ownerSymbol,
                Provenance(document.Metadata.Provenance, document.Blocks.FirstOrDefault()?.Span ?? default)),
        };

        DocumentBlockMir BindBlock(DocumentBlockMir block, string? parentNodeId)
        {
            int authoredOrder = blockOrder++;
            string nodeId = documentId + "::block::" + authoredOrder;
            DocumentNodeMetadata metadata = NodeMetadata(
                block.Metadata,
                nodeId,
                parentNodeId,
                authoredOrder,
                DefaultRole(block),
                block.Span);
            return block switch
            {
                HeadingMir heading => heading with
                {
                    Inlines = BindInlines(heading.Inlines, nodeId),
                    Metadata = metadata,
                },
                ParagraphMir paragraph => paragraph with
                {
                    Inlines = BindInlines(paragraph.Inlines, nodeId),
                    Metadata = metadata,
                },
                QuoteMir quote => quote with
                {
                    Inlines = BindInlines(quote.Inlines, nodeId),
                    Metadata = metadata,
                },
                CalloutMir callout => callout with
                {
                    Inlines = BindInlines(callout.Inlines, nodeId),
                    Metadata = metadata,
                },
                ListMir list => list with
                {
                    Items = list.Items.Select(item => BindListItem(item, nodeId)).ToArray(),
                    Metadata = metadata,
                },
                CodeBlockMir codeBlock => codeBlock with { Metadata = metadata },
                ThematicBreakMir thematicBreak => thematicBreak with { Metadata = metadata },
                BreakMir lineBreak => lineBreak with { Metadata = metadata },
                _ => throw new InvalidOperationException($"Unsupported document block type: {block.GetType().Name}"),
            };
        }

        ListItemMir BindListItem(ListItemMir item, string parentNodeId)
        {
            int authoredOrder = blockOrder++;
            string nodeId = documentId + "::block::" + authoredOrder;
            DocumentNodeMetadata metadata = NodeMetadata(item.Metadata, nodeId, parentNodeId, authoredOrder, "ListItem", item.Span);
            return item with
            {
                Inlines = BindInlines(item.Inlines, nodeId),
                ChildBlocks = item.ChildBlocks.Select(block => BindBlock(block, nodeId)).ToArray(),
                Metadata = metadata,
            };
        }

        IReadOnlyList<DocumentInlineMir> BindInlines(IReadOnlyList<DocumentInlineMir> inlines, string parentNodeId)
        {
            int inlineOrder = 0;
            return inlines.Select(inline => BindInline(inline, parentNodeId, parentInlineId: null, ref inlineOrder)).ToArray();
        }

        DocumentInlineMir BindInline(DocumentInlineMir inline, string parentNodeId, string? parentInlineId, ref int inlineOrder)
        {
            int authoredOrder = inlineOrder++;
            string nodeId = parentNodeId + "::inline::" + authoredOrder;
            DocumentNodeMetadata metadata = NodeMetadata(inline.Metadata, nodeId, parentInlineId, authoredOrder, null, inline.Span);
            return inline switch
            {
                TextMir text => text with { Metadata = metadata },
                EmbeddedValueMir value => value with { Metadata = metadata },
                CodeSpanMir code => code with { Metadata = metadata },
                EmphasisMir emphasis => emphasis with
                {
                    Children = BindChildren(emphasis.Children, parentNodeId, nodeId, ref inlineOrder),
                    Metadata = metadata,
                },
                StrongMir strong => strong with
                {
                    Children = BindChildren(strong.Children, parentNodeId, nodeId, ref inlineOrder),
                    Metadata = metadata,
                },
                LinkMir link when IsSafeLinkTarget(link.Target) => link with
                {
                    Label = BindChildren(link.Label, parentNodeId, nodeId, ref inlineOrder),
                    Metadata = metadata,
                },
                LinkMir link => RejectUnsafeLink(link, metadata),
                _ => throw new InvalidOperationException($"Unsupported document inline type: {inline.GetType().Name}"),
            };
        }

        IReadOnlyList<DocumentInlineMir> BindChildren(IReadOnlyList<DocumentInlineMir> children, string parentNodeId, string parentInlineId, ref int inlineOrder)
        {
            var result = new List<DocumentInlineMir>();
            foreach (DocumentInlineMir child in children)
            {
                result.Add(BindInline(child, parentNodeId, parentInlineId, ref inlineOrder));
            }
            return result;
        }

        DocumentInlineMir RejectUnsafeLink(LinkMir link, DocumentNodeMetadata metadata)
        {
            diagnostics.Add(new DocumentDiagnostic(
                "COPE-DOC-0001",
                "Unsafe link target; only relative, http, https, and mailto links are accepted.",
                MarkdownDiagnosticSeverity.Error,
                link.Span));
            string label = string.Concat(PlainText(link.Label));
            return new TextMir(label, link.Span) { Metadata = metadata };
        }

        IEnumerable<string> PlainText(IEnumerable<DocumentInlineMir> inlines)
        {
            foreach (DocumentInlineMir inline in inlines)
            {
                switch (inline)
                {
                    case TextMir text:
                        yield return text.Text;
                        break;
                    case EmbeddedValueMir:
                        yield return string.Empty;
                        break;
                    case CodeSpanMir code:
                        yield return code.Text;
                        break;
                    case EmphasisMir emphasis:
                        foreach (string text in PlainText(emphasis.Children)) yield return text;
                        break;
                    case StrongMir strong:
                        foreach (string text in PlainText(strong.Children)) yield return text;
                        break;
                    case LinkMir nestedLink:
                        foreach (string text in PlainText(nestedLink.Label)) yield return text;
                        break;
                }
            }
        }

        DocumentProvenance Provenance(DocumentProvenance existing, SourceSpan fallback)
            => existing != DocumentMetadata.Unbound.Provenance && existing.SourcePath != "<memory>"
                ? existing
                : new DocumentProvenance(sourceKind, sourcePath, fallback.Start, fallback.Length);

        DocumentNodeMetadata NodeMetadata(DocumentNodeMetadata existing, string nodeId, string? parentNodeId, int authoredOrder, string? defaultRole, SourceSpan fallback)
            => new(
                nodeId,
                parentNodeId,
                authoredOrder,
                existing.Role ?? defaultRole,
                existing.Provenance != DocumentNodeMetadata.Unbound.Provenance && existing.Provenance.SourcePath != "<memory>"
                    ? existing.Provenance
                    : new DocumentProvenance(sourceKind, sourcePath, fallback.Start, fallback.Length));
    }

    public static bool IsSafeLinkTarget(string target)
        => target.StartsWith("/", StringComparison.Ordinal)
            || target.StartsWith("#", StringComparison.Ordinal)
            || Uri.TryCreate(target, UriKind.Absolute, out Uri? uri)
                && uri.Scheme is "http" or "https" or "mailto";

    private static string DefaultRole(DocumentBlockMir block)
        => block switch
        {
            HeadingMir => "SectionHeading",
            ParagraphMir => "Body",
            QuoteMir => "Body",
            CalloutMir => "Body",
            CodeBlockMir => "CodeBlock",
            _ => block.GetType().Name.Replace("Mir", string.Empty, StringComparison.Ordinal),
        };
}
