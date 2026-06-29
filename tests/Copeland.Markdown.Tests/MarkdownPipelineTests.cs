using Machina.Standard.Text;
using Xunit;

namespace Copeland.Markdown.Tests;

public sealed class MarkdownPipelineTests
{
    [Fact]
    public void MarkdownParser_ParsesAtxHeading()
    {
        MarkdownDocument document = MarkdownParser.Parse("# Heading");

        HeadingBlock heading = Assert.IsType<HeadingBlock>(Assert.Single(document.Blocks));
        Assert.Equal(1, heading.Level);
        AssertText(Assert.Single(heading.Inlines), "Heading");
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void MarkdownParser_ParsesParagraphs()
    {
        MarkdownDocument document = MarkdownParser.Parse("First paragraph");

        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        AssertText(Assert.Single(paragraph.Inlines), "First paragraph");
    }

    [Fact]
    public void MarkdownParser_ParsesBulletList()
    {
        MarkdownDocument document = MarkdownParser.Parse("- one\n* two");

        BulletListBlock list = Assert.IsType<BulletListBlock>(Assert.Single(document.Blocks));
        Assert.Collection(
            list.Items,
            item => AssertText(Assert.Single(item.Inlines), "one"),
            item => AssertText(Assert.Single(item.Inlines), "two"));
    }

    [Fact]
    public void MarkdownParser_ParsesFencedCodeBlock()
    {
        MarkdownDocument document = MarkdownParser.Parse("```csharp\nConsole.WriteLine(1);\n```");

        CodeFenceBlock block = Assert.IsType<CodeFenceBlock>(Assert.Single(document.Blocks));
        Assert.Equal("csharp", block.Language);
        Assert.Contains("Console.WriteLine(1);", block.Text, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void MarkdownParser_SeparatesBlocksOnBlankLines()
    {
        MarkdownDocument document = MarkdownParser.Parse("# Title\n\nBody");

        Assert.Collection(
            document.Blocks,
            block => Assert.IsType<HeadingBlock>(block),
            block => Assert.IsType<ParagraphBlock>(block));
    }

    [Fact]
    public void MarkdownParser_ParsesThematicBreak_IfImplemented()
    {
        MarkdownDocument document = MarkdownParser.Parse("---");

        Assert.IsType<ThematicBreakBlock>(Assert.Single(document.Blocks));
    }

    [Fact]
    public void MarkdownInlineParser_ParsesInlineCode()
    {
        MarkdownDocument document = MarkdownParser.Parse("Before `code` after");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));

        Assert.Collection(
            paragraph.Inlines,
            inline => AssertText(inline, "Before "),
            inline =>
            {
                CodeInline code = Assert.IsType<CodeInline>(inline);
                Assert.Equal("code", code.Text);
            },
            inline => AssertText(inline, " after"));
    }

    [Fact]
    public void MarkdownInlineParser_ParsesStrong()
    {
        MarkdownDocument document = MarkdownParser.Parse("**bold**");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));

        StrongInline strong = Assert.IsType<StrongInline>(Assert.Single(paragraph.Inlines));
        AssertText(Assert.Single(strong.Children), "bold");
    }

    [Fact]
    public void MarkdownInlineParser_ParsesEmphasis()
    {
        MarkdownDocument document = MarkdownParser.Parse("*soft*");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));

        EmphasisInline emphasis = Assert.IsType<EmphasisInline>(Assert.Single(paragraph.Inlines));
        AssertText(Assert.Single(emphasis.Children), "soft");
    }

    [Fact]
    public void MarkdownInlineParser_ParsesLink()
    {
        MarkdownDocument document = MarkdownParser.Parse("[docs](https://example.test)");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));

        LinkInline link = Assert.IsType<LinkInline>(Assert.Single(paragraph.Inlines));
        Assert.Equal("https://example.test", link.Target);
        AssertText(Assert.Single(link.Label), "docs");
    }

    [Fact]
    public void MarkdownInlineParser_LeavesUnsupportedSyntaxAsText()
    {
        MarkdownDocument document = MarkdownParser.Parse("_soft_");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));

        AssertText(Assert.Single(paragraph.Inlines), "_soft_");
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void MarkdownParser_ReportsUnclosedCodeFence()
    {
        MarkdownDocument document = MarkdownParser.Parse("```csharp\nConsole.WriteLine(1);");

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Id == MarkdownDiagnosticIds.UnclosedCodeFence);
        Assert.IsType<CodeFenceBlock>(Assert.Single(document.Blocks));
    }

    [Fact]
    public void MarkdownParser_ReportsMalformedLink()
    {
        MarkdownDocument document = MarkdownParser.Parse("Before [broken link");

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Id == MarkdownDiagnosticIds.MalformedLink);
    }

    [Fact]
    public void MarkdownParser_RecoversAfterMalformedInline()
    {
        MarkdownDocument document = MarkdownParser.Parse("Before [broken and **bold**");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Id == MarkdownDiagnosticIds.MalformedLink);
        Assert.Collection(
            paragraph.Inlines,
            inline => AssertText(inline, "Before [broken and "),
            inline =>
            {
                StrongInline strong = Assert.IsType<StrongInline>(inline);
                AssertText(Assert.Single(strong.Children), "bold");
            });
    }

    [Fact]
    public void MarkdownParser_DiagnosticsAreDeterministic()
    {
        const string source = "```csharp\nbroken";

        MarkdownDocument first = MarkdownParser.Parse(source);
        MarkdownDocument second = MarkdownParser.Parse(source);

        Assert.Equal(
            MarkdownDumpWriter.DumpDiagnostics(first.Diagnostics),
            MarkdownDumpWriter.DumpDiagnostics(second.Diagnostics));
    }

    [Fact]
    public void MarkdownToDocumentMir_LowersHeading()
    {
        DocumentMir mir = MarkdownCompiler.Compile("# Title").Mir;

        HeadingMir heading = Assert.IsType<HeadingMir>(Assert.Single(mir.Blocks));
        Assert.Equal(1, heading.Level);
    }

    [Fact]
    public void MarkdownToDocumentMir_LowersParagraph()
    {
        DocumentMir mir = MarkdownCompiler.Compile("Body").Mir;
        Assert.IsType<ParagraphMir>(Assert.Single(mir.Blocks));
    }

    [Fact]
    public void MarkdownToDocumentMir_LowersList()
    {
        DocumentMir mir = MarkdownCompiler.Compile("- one\n- two").Mir;

        ListMir list = Assert.IsType<ListMir>(Assert.Single(mir.Blocks));
        Assert.Equal(DocumentListKind.Bullet, list.Kind);
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void MarkdownToDocumentMir_LowersCodeBlock()
    {
        DocumentMir mir = MarkdownCompiler.Compile("```txt\nbody\n```").Mir;
        Assert.IsType<CodeBlockMir>(Assert.Single(mir.Blocks));
    }

    [Fact]
    public void MarkdownToDocumentMir_PreservesInlineCodeStrongEmphasisLinks()
    {
        DocumentMir mir = MarkdownCompiler.Compile("`code` **bold** *soft* [docs](https://example.test)").Mir;
        ParagraphMir paragraph = Assert.IsType<ParagraphMir>(Assert.Single(mir.Blocks));

        Assert.Collection(
            paragraph.Inlines,
            inline => Assert.IsType<CodeSpanMir>(inline),
            inline => Assert.IsType<TextMir>(inline),
            inline => Assert.IsType<StrongMir>(inline),
            inline => Assert.IsType<TextMir>(inline),
            inline => Assert.IsType<EmphasisMir>(inline),
            inline => Assert.IsType<TextMir>(inline),
            inline => Assert.IsType<LinkMir>(inline));
    }

    [Fact]
    public void MarkdownCorpus_Readme_DoesNotCrash()
    {
        MarkdownCompilation compilation = MarkdownCompiler.Compile(File.ReadAllText(GetRepoFile("README.md")));
        Assert.NotEmpty(compilation.Syntax.Blocks);
    }

    [Fact]
    public void MarkdownCorpus_M11Docs_DoNotCrash()
    {
        foreach (string relativePath in CorpusFiles.Skip(1))
        {
            MarkdownCompilation compilation = MarkdownCompiler.Compile(File.ReadAllText(GetRepoFile(relativePath)));
            Assert.NotNull(compilation.Mir);
        }
    }

    [Fact]
    public void MarkdownCorpus_ProducesMirForSelectedDocs()
    {
        foreach (string relativePath in CorpusFiles)
        {
            MarkdownCompilation compilation = MarkdownCompiler.Compile(File.ReadAllText(GetRepoFile(relativePath)));
            Assert.NotEmpty(compilation.Mir.Blocks);
        }
    }

    [Fact]
    public void MarkdownCorpus_ReportsUnsupportedSyntaxDeterministically()
    {
        string source = File.ReadAllText(GetRepoFile(@"docs\machina-oblivion-phase-closeout-m11g.md"));

        string first = MarkdownDumpWriter.DumpDiagnostics(MarkdownCompiler.Compile(source).Syntax.Diagnostics);
        string second = MarkdownDumpWriter.DumpDiagnostics(MarkdownCompiler.Compile(source).Syntax.Diagnostics);

        Assert.Equal(first, second);
    }

    [Fact]
    public void MarkdownCorpus_OblivionBodyFiles_DoNotCrash()
    {
        string[] files =
        [
            @"samples\Machina.Presenter.Sample\OblivionSampleWorkspace\body\oblivion-substrate-status.md",
            @"samples\Machina.Presenter.Sample\OblivionSampleWorkspace\body\markdown-first-roadmap.md",
            @"samples\Machina.Presenter.Sample\OblivionSampleWorkspace\body\markdown-readiness-audit.md",
            @"samples\Machina.Presenter.Sample\OblivionSampleWorkspace\body\execution-deferred.md",
            @"samples\Machina.Presenter.Sample\OblivionSampleWorkspace\body\visionary-future.md",
        ];

        foreach (string relativePath in files)
        {
            MarkdownCompilation compilation = MarkdownCompiler.Compile(File.ReadAllText(GetRepoFile(relativePath)));
            Assert.NotNull(compilation.Mir);
        }
    }

    [Fact]
    public void MarkdownCorpus_OblivionMalformedBody_ReportsDeterministicDiagnostics()
    {
        string source = File.ReadAllText(GetRepoFile(@"samples\Machina.Presenter.Sample\OblivionSampleWorkspace\body\markdown-readiness-audit.md"));

        string first = MarkdownDumpWriter.DumpDiagnostics(MarkdownCompiler.Compile(source).Syntax.Diagnostics);
        string second = MarkdownDumpWriter.DumpDiagnostics(MarkdownCompiler.Compile(source).Syntax.Diagnostics);

        Assert.Equal(first, second);
        Assert.Contains(MarkdownDiagnosticIds.MalformedLink, first, StringComparison.Ordinal);
    }

    [Fact]
    public void M12a_DoesNotReferenceMarkdigOrCommonMark()
    {
        string repoRoot = GetRepoRoot();
        string[] bannedTerms = ["Markdig", "CommonMark", "MarkdownSharp", "MarkdownDeep"];

        IEnumerable<string> markdownFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Copeland.Markdown"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "Copeland.Cli"), "*.*", SearchOption.TopDirectoryOnly));

        string combinedText = string.Join(
            "\n",
            markdownFiles.Select(File.ReadAllText));

        foreach (string term in bannedTerms)
        {
            Assert.DoesNotContain(term, combinedText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void M12a_DoesNotImplementRoslynExecution()
    {
        string repoRoot = GetRepoRoot();
        string[] bannedTerms =
        [
            "Microsoft.CodeAnalysis",
            "CSharpCompilation",
            "MetadataReference",
            "AssemblyLoadContext",
            "FactAttribute",
            "TheoryAttribute",
        ];

        IEnumerable<string> markdownFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Copeland.Markdown"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "Copeland.Cli"), "*.cs", SearchOption.TopDirectoryOnly));

        string combinedText = string.Join(
            "\n",
            markdownFiles.Select(File.ReadAllText));

        foreach (string term in bannedTerms)
        {
            Assert.DoesNotContain(term, combinedText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void M12a_DoesNotChangeStandardTextBehavior()
    {
        ParseMachinaTextResult result = MachinaTextParser.ParseMarkup("# Heading");

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == MachinaTextDiagnosticCode.HeadingForbidden);
    }

    private static readonly string[] CorpusFiles =
    [
        "README.md",
        @"docs\machina-oblivion-phase-closeout-m11g.md",
        @"docs\machina-oblivion-workspace-persistence-m11d.md",
        @"docs\machina-presenter-card-hardening-m11e.md",
        @"docs\machina-test-suite-topology-m11b.md",
        @"docs\machina-presenter-scrollbar-state-machine-m11c.md",
    ];

    private static void AssertText(MarkdownInline inline, string expected)
    {
        TextInline text = Assert.IsType<TextInline>(inline);
        Assert.Equal(expected, text.Text);
    }

    private static string GetRepoFile(string relativePath)
    {
        return Path.Combine(GetRepoRoot(), relativePath);
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
