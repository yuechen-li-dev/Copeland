using Copeland.Markdown;
using Copeland.TS.MachinaSource;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TextDocumentM0Tests
{
    [Fact]
    public void Structured_document_lowers_into_the_canonical_document_mir()
    {
        const string source = """
            function Hero(): ReactNode {
                return <Document>
                    <Heading role="HeroHeading">AI-native **Copeland** for *products* with `typed` [links](/architecture).</Heading>
                    <List><Item><Paragraph>Stable local copy.</Paragraph></Item></List>
                    <CodeBlock language="ts">stream Page { }</CodeBlock>
                </Document>;
            }
            """;

        TextDocumentCompilation compilation = TextDocumentCompiler.Compile(SyntaxTree.Parse(source, "Hero.tsx"), "src/Hero.tsx");

        Assert.Empty(compilation.Diagnostics);
        BoundTextDocument document = Assert.Single(compilation.Documents);
        Assert.Equal("Hero", document.OwnerFunction);
        Assert.Equal(DocumentSourceKind.TextXml, document.Document.Metadata.Provenance.SourceKind);
        Assert.Collection(document.Document.Blocks,
            heading =>
            {
                HeadingMir typedHeading = Assert.IsType<HeadingMir>(heading);
                Assert.Equal("HeroHeading", typedHeading.Metadata.Role);
                Assert.Contains(typedHeading.Inlines, inline => inline is StrongMir);
                Assert.Contains(typedHeading.Inlines, inline => inline is EmphasisMir);
                Assert.Contains(typedHeading.Inlines, inline => inline is CodeSpanMir);
                Assert.Contains(typedHeading.Inlines, inline => inline is LinkMir { Target: "/architecture" });
            },
            list =>
            {
                ListMir typedList = Assert.IsType<ListMir>(list);
                ListItemMir item = Assert.Single(typedList.Items);
                Assert.IsType<ParagraphMir>(Assert.Single(item.ChildBlocks));
            },
            code => Assert.Equal("stream Page { }", Assert.IsType<CodeBlockMir>(code).Text));
    }

    [Fact]
    public void Plain_xml_and_markdown_share_equivalent_inline_semantics()
    {
        const string source = """
            function Plain(): ReactNode { return Text("Build **real software** with `Copeland`."); }
            function Xml(): ReactNode { return <Document><Paragraph>Build **real software** with `Copeland`.</Paragraph></Document>; }
            """;

        TextDocumentCompilation text = TextDocumentCompiler.Compile(SyntaxTree.Parse(source, "Copy.tsx"), "src/Copy.tsx");
        DocumentMir markdown = MarkdownCompiler.Compile("Build **real software** with `Copeland`.").Mir;
        ParagraphMir plain = Assert.IsType<ParagraphMir>(Assert.Single(text.Documents[0].Document.Blocks));
        ParagraphMir xml = Assert.IsType<ParagraphMir>(Assert.Single(text.Documents[1].Document.Blocks));
        ParagraphMir markdownParagraph = Assert.IsType<ParagraphMir>(Assert.Single(markdown.Blocks));

        Assert.Equal(InlineShape(markdownParagraph.Inlines), InlineShape(plain.Inlines));
        Assert.Equal(InlineShape(markdownParagraph.Inlines), InlineShape(xml.Inlines));
        Assert.Equal(DocumentSourceKind.TextPlain, text.Documents[0].Document.Metadata.Provenance.SourceKind);
        Assert.Equal(DocumentSourceKind.TextXml, text.Documents[1].Document.Metadata.Provenance.SourceKind);
    }

    [Fact]
    public void Unsafe_link_is_rejected_by_the_shared_document_binder()
    {
        const string source = """
            function Label(): ReactNode { return Text("Read [this](javascript:alert(1)) safely."); }
            """;

        TextDocumentCompilation compilation = TextDocumentCompiler.Compile(SyntaxTree.Parse(source, "Label.tsx"), "src/Label.tsx");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-DOC-0001");
        ParagraphMir paragraph = Assert.IsType<ParagraphMir>(Assert.Single(Assert.Single(compilation.Documents).Document.Blocks));
        Assert.DoesNotContain(paragraph.Inlines, inline => inline is LinkMir);
    }

    [Fact]
    public void Presentation_binding_keeps_authored_classes_outside_document_semantics()
    {
        const string source = """
            function Hero(): ReactNode {
                return <Document className="document-shell"><Heading className="text-fit-target" role="HeroHeading">Title</Heading></Document>;
            }
            """;

        BoundTextDocument document = Assert.Single(TextDocumentCompiler.Compile(SyntaxTree.Parse(source, "Hero.tsx"), "src/Hero.tsx").Documents);
        HeadingMir heading = Assert.IsType<HeadingMir>(Assert.Single(document.Document.Blocks));

        Assert.Equal("document-shell", document.Presentation.DocumentClassName);
        Assert.Equal("text-fit-target", document.Presentation.NodePresentations[heading.Metadata.NodeId].ClassName);
        Assert.DoesNotContain("class", heading.Metadata.NodeId, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] InlineShape(IEnumerable<DocumentInlineMir> inlines)
        => inlines.Select(inline => inline switch
        {
            TextMir text => "Text:" + text.Text,
            StrongMir strong => "Strong(" + string.Join(',', InlineShape(strong.Children)) + ")",
            EmphasisMir emphasis => "Emphasis(" + string.Join(',', InlineShape(emphasis.Children)) + ")",
            CodeSpanMir code => "Code:" + code.Text,
            LinkMir link => "Link:" + link.Target,
            _ => inline.GetType().Name,
        }).ToArray();
}
