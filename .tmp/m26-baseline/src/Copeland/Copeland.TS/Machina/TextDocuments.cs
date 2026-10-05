using Copeland.Markdown;
using Copeland.TS.Diagnostics;
using Copeland.TS.Mir.Machina;
using Copeland.TS.Syntax;

namespace Copeland.TS.MachinaSource;

/// <summary>
/// Host/frontend binding for a canonical DocumentMir. This type retains only
/// the TS authoring owner and its source anchor; it intentionally does not own
/// a second block or inline tree.
/// </summary>
public sealed record BoundTextDocument(
    string DefinitionId,
    string OwnerFunction,
    DocumentMir Document,
    TextPresentationBinding Presentation,
    MachinaSourceSpan Source);

/// <summary>
/// Host-facing presentation facts for one canonical document. CSS classes and
/// fitting metadata belong here, never on DocumentMir nodes.
/// </summary>
public sealed record TextPresentationBinding(
    string BindingId,
    string DocumentId,
    string SemanticHostId,
    string ThemeId,
    string? DocumentClassName,
    IReadOnlyDictionary<string, TextNodePresentation> NodePresentations,
    DocumentProvenance Source);

public sealed record TextNodePresentation(
    string DocumentNodeId,
    string? ClassName,
    DocumentProvenance Source);

public sealed record TextDocumentCompilation(
    IReadOnlyList<BoundTextDocument> Documents,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Text source frontend. It reads TS-XML structure once, delegates bounded
/// inline recognition to the Markdown-owned parser, then binds the shared
/// DocumentMir with TextXml or TextPlain provenance.
/// </summary>
public static class TextDocumentCompiler
{
    public static TextDocumentCompilation Compile(SyntaxTree tree, string modulePath)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var documents = new List<BoundTextDocument>();
        var diagnostics = new List<Diagnostic>();
        foreach (FunctionDeclarationSyntax function in tree.Root.Members.OfType<FunctionDeclarationSyntax>())
        {
            var roots = new List<ExpressionSyntax>();
            FindRoots(function.Body, roots);
            int documentOrder = 0;
            foreach (ExpressionSyntax root in roots)
            {
                string definitionId = modulePath + "::" + function.Identifier.Text + "::text::" + documentOrder;
                BoundTextDocument? document = root switch
                {
                    TsXmlElementExpressionSyntax { NameToken.Text: "Document" } element => BuildDocument(element, definitionId, function.Identifier.Text),
                    CallExpressionSyntax call when IsTextCall(call) => BuildPlainText(call, definitionId, function.Identifier.Text),
                    _ => null,
                };
                if (document is not null)
                {
                    documents.Add(document);
                    documentOrder += 1;
                }
            }
        }

        return new TextDocumentCompilation(documents, diagnostics);

        BoundTextDocument? BuildPlainText(CallExpressionSyntax call, string definitionId, string owner)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not LiteralExpressionSyntax { LiteralToken.Value: string text } literal)
            {
                diagnostics.Add(Diagnostic("COPE-TEXT-0012", "Text(...) accepts one static string in M1.", call));
                return null;
            }

            SourceSpan span = MarkdownSpan(literal.LiteralToken.Position + 1, text.Length);
            DocumentMir raw = new(
                [new ParagraphMir(ParseInline(text, span.Start, literal), span)
                {
                    Metadata = DocumentNodeMetadata.Unbound with { Role = "Body" },
                }],
                [])
            {
                Metadata = new DocumentMetadata(
                    definitionId,
                    owner,
                    new DocumentProvenance(DocumentSourceKind.TextPlain, modulePath, span.Start, span.Length)),
            };
            return Bind(
                raw,
                definitionId,
                owner,
                DocumentSourceKind.TextPlain,
                call,
                new Dictionary<int, AuthoredPresentation>());
        }

        BoundTextDocument BuildDocument(TsXmlElementExpressionSyntax root, string definitionId, string owner)
        {
            var blocks = new List<DocumentBlockMir>();
            var authoredPresentation = new Dictionary<int, AuthoredPresentation>();
            RegisterPresentation(root, authoredPresentation);
            foreach (TsXmlChildSyntax child in root.Children)
            {
                if (child is TsXmlTextSyntax text && string.IsNullOrWhiteSpace(text.TextToken.Text)) continue;
                if (child is not TsXmlElementChildSyntax { Element: TsXmlElementExpressionSyntax element })
                {
                    diagnostics.Add(Diagnostic("COPE-TEXT-0003", "Document accepts block elements only.", child));
                    continue;
                }

                DocumentBlockMir? block = BuildBlock(element, parentKind: null, authoredPresentation);
                if (block is not null) blocks.Add(block);
            }

            MachinaSourceSpan rootSpan = Span(root);
            return Bind(new DocumentMir(blocks, [])
            {
                Metadata = new DocumentMetadata(
                    definitionId,
                    owner,
                    new DocumentProvenance(DocumentSourceKind.TextXml, modulePath, rootSpan.Start, rootSpan.Length)),
            }, definitionId, owner, DocumentSourceKind.TextXml, root, authoredPresentation);
        }

        BoundTextDocument Bind(
            DocumentMir raw,
            string definitionId,
            string owner,
            DocumentSourceKind sourceKind,
            SyntaxNode source,
            IReadOnlyDictionary<int, AuthoredPresentation> authoredPresentation)
        {
            DocumentMir document = DocumentMirBinder.Bind(raw, definitionId, owner, sourceKind, modulePath);
            foreach (DocumentDiagnostic diagnostic in document.Diagnostics)
            {
                diagnostics.Add(new Diagnostic(diagnostic.Id, diagnostic.Message, diagnostic.Span.Start, diagnostic.Span.Length, modulePath));
            }
            return new BoundTextDocument(
                definitionId,
                owner,
                document,
                BuildPresentation(document, owner, authoredPresentation),
                Span(source));
        }

        DocumentBlockMir? BuildBlock(
            TsXmlElementExpressionSyntax element,
            TextDocumentBlockKind? parentKind,
            Dictionary<int, AuthoredPresentation> authoredPresentation)
        {
            if (!TryBlockKind(element.NameToken.Text, out TextDocumentBlockKind kind))
            {
                diagnostics.Add(Diagnostic("COPE-TEXT-0001", $"Unknown text block <{element.NameToken.Text}>.", element));
                return null;
            }
            if (!IsLegalChild(parentKind, kind))
            {
                diagnostics.Add(Diagnostic("COPE-TEXT-0002", $"<{element.NameToken.Text}> is not legal in this text block.", element));
                return null;
            }

            SourceSpan span = MarkdownSpan(Span(element).Start, Span(element).Length);
            RegisterPresentation(element, authoredPresentation);
            string? role = Attribute(element, "role");
            if (role is not null && !IsKnownRole(role))
            {
                diagnostics.Add(Diagnostic("COPE-TEXT-PRESENTATION-0002", $"Unknown text role '{role}'.", element));
            }
            return kind switch
            {
                TextDocumentBlockKind.Heading => new HeadingMir(HeadingLevel(element), ParseElementInlines(element), span) { Metadata = Metadata(role, span) },
                TextDocumentBlockKind.Paragraph => new ParagraphMir(ParseElementInlines(element), span) { Metadata = Metadata(role, span) },
                TextDocumentBlockKind.Quote => new QuoteMir(ParseElementInlines(element), span) { Metadata = Metadata(role, span) },
                TextDocumentBlockKind.Callout => new CalloutMir(ParseElementInlines(element), span) { Metadata = Metadata(role, span) },
                TextDocumentBlockKind.CodeBlock => new CodeBlockMir(Attribute(element, "language"), LiteralCode(element), span) { Metadata = Metadata(role, span) },
                TextDocumentBlockKind.Break => new BreakMir(span) { Metadata = Metadata(role, span) },
                TextDocumentBlockKind.List => BuildList(element, span, role, authoredPresentation),
                TextDocumentBlockKind.Item => throw new InvalidOperationException("List items are constructed by their List parent."),
                _ => throw new InvalidOperationException($"Unsupported text block kind: {kind}"),
            };
        }

        ListMir BuildList(
            TsXmlElementExpressionSyntax element,
            SourceSpan span,
            string? role,
            Dictionary<int, AuthoredPresentation> authoredPresentation)
        {
            var items = new List<ListItemMir>();
            foreach (TsXmlChildSyntax child in element.Children)
            {
                if (child is TsXmlTextSyntax text && string.IsNullOrWhiteSpace(text.TextToken.Text)) continue;
                if (child is not TsXmlElementChildSyntax { Element: TsXmlElementExpressionSyntax { NameToken.Text: "Item" } itemElement })
                {
                    diagnostics.Add(Diagnostic("COPE-TEXT-0004", "<List> requires <Item> children.", child));
                    continue;
                }

                SourceSpan itemSpan = MarkdownSpan(Span(itemElement).Start, Span(itemElement).Length);
                RegisterPresentation(itemElement, authoredPresentation);
                var childBlocks = new List<DocumentBlockMir>();
                foreach (TsXmlChildSyntax itemChild in itemElement.Children)
                {
                    if (itemChild is TsXmlTextSyntax itemText && string.IsNullOrWhiteSpace(itemText.TextToken.Text)) continue;
                    if (itemChild is not TsXmlElementChildSyntax { Element: TsXmlElementExpressionSyntax nested })
                    {
                        diagnostics.Add(Diagnostic("COPE-TEXT-0004", "<Item> requires structured child blocks.", itemChild));
                        continue;
                    }
                    DocumentBlockMir? nestedBlock = BuildBlock(nested, TextDocumentBlockKind.Item, authoredPresentation);
                    if (nestedBlock is not null) childBlocks.Add(nestedBlock);
                }
                if (childBlocks.Count == 0)
                {
                    diagnostics.Add(Diagnostic("COPE-TEXT-0009", "<Item> requires at least one child block.", itemElement));
                }
                items.Add(new ListItemMir([], itemSpan)
                {
                    Metadata = Metadata("ListItem", itemSpan),
                    ChildBlocks = childBlocks,
                });
            }
            if (items.Count == 0) diagnostics.Add(Diagnostic("COPE-TEXT-0010", "<List> requires at least one <Item>.", element));
            return new ListMir(DocumentListKind.Bullet, items, span) { Metadata = Metadata(role, span) };
        }

        IReadOnlyList<DocumentInlineMir> ParseElementInlines(TsXmlElementExpressionSyntax element)
        {
            var inlines = new List<DocumentInlineMir>();
            foreach (TsXmlChildSyntax child in element.Children)
            {
                switch (child)
                {
                    case TsXmlTextSyntax text when !string.IsNullOrWhiteSpace(text.TextToken.Text):
                    {
                        string raw = text.TextToken.Text;
                        int leading = raw.Length - raw.TrimStart().Length;
                        string content = raw.Trim();
                        if (content.Length > 0)
                        {
                            inlines.AddRange(ParseInline(content, text.TextToken.Position + leading, text));
                        }
                        break;
                    }
                    case TsXmlExpressionChildSyntax expression:
                        inlines.Add(new EmbeddedValueMir(
                            TextSlotId(expression),
                            MarkdownSpan(expression.OpenBraceToken.Position, expression.CloseBraceToken.Position - expression.OpenBraceToken.Position + expression.CloseBraceToken.Text.Length)));
                        break;
                    case TsXmlElementChildSyntax nested:
                        diagnostics.Add(Diagnostic("COPE-TEXT-0002", "Inline Text content accepts static text and typed value slots only.", nested));
                        break;
                }
            }
            return inlines;
        }

        static string TextSlotId(TsXmlExpressionChildSyntax expression)
            => "text-slot-" + expression.OpenBraceToken.Position.ToString(System.Globalization.CultureInfo.InvariantCulture);

        IReadOnlyList<DocumentInlineMir> ParseInline(string text, int start, SyntaxNode source)
        {
            InlineParseResult parsed = MarkdownInlineParser.Parse(new MarkdownSourceText(tree.Text), start, text);
            foreach (MarkdownDiagnostic diagnostic in parsed.Diagnostics)
            {
                diagnostics.Add(new Diagnostic(diagnostic.Id, diagnostic.Message, diagnostic.Span.Start, diagnostic.Span.Length, modulePath));
            }
            return MarkdownToDocumentMirLowerer.LowerInlineList(parsed.Inlines);
        }

        string LiteralCode(TsXmlElementExpressionSyntax element)
        {
            int start = element.OpenCloseToken.Position + element.OpenCloseToken.Text.Length;
            int end = element.CloseLessToken?.Position ?? start;
            return start <= end && end <= tree.Text.Length
                ? tree.Text[start..end].Trim()
                : string.Concat(element.Children.OfType<TsXmlTextSyntax>().Select(child => child.TextToken.Text)).Trim();
        }

        DocumentNodeMetadata Metadata(string? role, SourceSpan span)
            => DocumentNodeMetadata.Unbound with
            {
                Role = role,
                Provenance = new DocumentProvenance(DocumentSourceKind.TextXml, modulePath, span.Start, span.Length),
            };

        TextPresentationBinding BuildPresentation(
            DocumentMir document,
            string owner,
            IReadOnlyDictionary<int, AuthoredPresentation> authoredPresentation)
        {
            var assignments = new Dictionary<string, TextNodePresentation>(StringComparer.Ordinal);
            foreach (DocumentBlockMir block in document.Blocks)
            {
                AddBlock(block);
            }
            AuthoredPresentation? root = authoredPresentation.GetValueOrDefault(document.Metadata.Provenance.Start);
            return new TextPresentationBinding(
                document.Metadata.DocumentId + "::presentation",
                document.Metadata.DocumentId,
                owner,
                "CopelandText",
                root?.ClassName,
                assignments,
                document.Metadata.Provenance);

            void AddBlock(DocumentBlockMir block)
            {
                AddNode(block.Metadata);
                if (block is ListMir list)
                {
                    foreach (ListItemMir item in list.Items)
                    {
                        AddNode(item.Metadata);
                        foreach (DocumentBlockMir child in item.ChildBlocks) AddBlock(child);
                    }
                }
            }

            void AddNode(DocumentNodeMetadata metadata)
            {
                if (!authoredPresentation.TryGetValue(metadata.Provenance.Start, out AuthoredPresentation? authored)) return;
                assignments.Add(metadata.NodeId, new TextNodePresentation(metadata.NodeId, authored.ClassName, authored.Source));
            }
        }

        void RegisterPresentation(
            TsXmlElementExpressionSyntax element,
            Dictionary<int, AuthoredPresentation> authoredPresentation)
        {
            TsXmlAttributeSyntax? classAttribute = element.Attributes
                .SingleOrDefault(attribute => attribute.NameToken.Text == "className");
            if (classAttribute is null) return;
            if (classAttribute.StringValueToken?.Value is not string className)
            {
                diagnostics.Add(Diagnostic("COPE-TEXT-PRESENTATION-0001", "Document presentation className must be a static string.", classAttribute));
                return;
            }
            MachinaSourceSpan source = Span(classAttribute);
            authoredPresentation[Span(element).Start] = new AuthoredPresentation(
                className,
                new DocumentProvenance(DocumentSourceKind.TextXml, modulePath, source.Start, source.Length));
        }

        SourceSpan MarkdownSpan(int start, int length)
            => new MarkdownSourceText(tree.Text).CreateSpan(start, Math.Max(0, length));

        MachinaSourceSpan Span(SyntaxNode node)
        {
            SyntaxToken[] tokens = Tokens(node).ToArray();
            if (tokens.Length == 0) return new MachinaSourceSpan(modulePath, 0, 1);
            int start = tokens.Min(token => token.Position);
            int end = tokens.Max(token => token.Position + token.Text.Length);
            return new MachinaSourceSpan(modulePath, start, Math.Max(1, end - start));
        }

        Diagnostic Diagnostic(string id, string message, SyntaxNode node)
        {
            MachinaSourceSpan span = Span(node);
            return new Diagnostic(id, message, span.Start, span.Length, modulePath);
        }
    }

    private static void FindRoots(SyntaxNode node, List<ExpressionSyntax> roots)
    {
        if (node is TsXmlElementExpressionSyntax { NameToken.Text: "Document" } document)
        {
            roots.Add(document);
            return;
        }
        if (node is CallExpressionSyntax call && IsTextCall(call))
        {
            roots.Add(call);
            return;
        }
        foreach (object child in node.GetChildren())
        {
            if (child is SyntaxNode nested) FindRoots(nested, roots);
        }
    }

    private static bool IsTextCall(CallExpressionSyntax call)
        => call.Target is NameExpressionSyntax { IdentifierToken.Text: "Text" };

    private static bool TryBlockKind(string name, out TextDocumentBlockKind kind)
    {
        kind = name switch
        {
            "Heading" => TextDocumentBlockKind.Heading,
            "Paragraph" => TextDocumentBlockKind.Paragraph,
            "List" => TextDocumentBlockKind.List,
            "Item" => TextDocumentBlockKind.Item,
            "CodeBlock" => TextDocumentBlockKind.CodeBlock,
            "Quote" => TextDocumentBlockKind.Quote,
            "Callout" => TextDocumentBlockKind.Callout,
            "Break" => TextDocumentBlockKind.Break,
            _ => default,
        };
        return name is "Heading" or "Paragraph" or "List" or "Item" or "CodeBlock" or "Quote" or "Callout" or "Break";
    }

    private static bool IsLegalChild(TextDocumentBlockKind? parent, TextDocumentBlockKind child)
        => parent switch
        {
            null => child is not TextDocumentBlockKind.Item,
            TextDocumentBlockKind.Item => child is TextDocumentBlockKind.Paragraph or TextDocumentBlockKind.List or TextDocumentBlockKind.CodeBlock or TextDocumentBlockKind.Quote or TextDocumentBlockKind.Callout or TextDocumentBlockKind.Break,
            _ => false,
        };

    private static int HeadingLevel(TsXmlElementExpressionSyntax element)
        => int.TryParse(Attribute(element, "level"), out int level) && level is >= 1 and <= 6 ? level : 2;

    private static bool IsKnownRole(string role)
        => role is "HeroHeading"
            or "SectionHeading"
            or "CardHeading"
            or "Body"
            or "Caption"
            or "Eyebrow"
            or "CodeBlock"
            or "ListItem";

    private static string? Attribute(TsXmlElementExpressionSyntax element, string name)
    {
        TsXmlAttributeSyntax? attribute = element.Attributes.SingleOrDefault(candidate => candidate.NameToken.Text == name);
        if (attribute?.StringValueToken?.Value is string value) return value;
        return attribute?.ExpressionValue is NameExpressionSyntax expression ? expression.IdentifierToken.Text : null;
    }

    private static IEnumerable<SyntaxToken> Tokens(SyntaxNode node)
    {
        foreach (object child in node.GetChildren())
        {
            if (child is SyntaxToken token) yield return token;
            if (child is SyntaxNode nested)
            {
                foreach (SyntaxToken descendant in Tokens(nested)) yield return descendant;
            }
        }
    }

    private sealed record AuthoredPresentation(string ClassName, DocumentProvenance Source);
}

public enum TextDocumentBlockKind
{
    Heading,
    Paragraph,
    List,
    Item,
    CodeBlock,
    Quote,
    Callout,
    Break,
}
