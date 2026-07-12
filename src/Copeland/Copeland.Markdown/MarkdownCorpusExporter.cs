namespace Copeland.Markdown;

public sealed record MarkdownCorpusDocumentReport(
    string RelativePath,
    int BlockCount,
    int DiagnosticCount,
    IReadOnlyList<string> Diagnostics);

public sealed record MarkdownCorpusReport(
    string DialectName,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<MarkdownCorpusDocumentReport> Documents,
    int TotalDiagnostics);

public static class MarkdownCorpusExporter
{
    public static MarkdownCorpusReport ExportSelectedDocs(string repoRoot, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        string fullRepoRoot = Path.GetFullPath(repoRoot);
        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        string[] corpusFiles =
        [
            "README.md",
            @"docs\machina-oblivion-phase-closeout-m11g.md",
            @"docs\machina-oblivion-workspace-persistence-m11d.md",
            @"docs\machina-presenter-card-hardening-m11e.md",
            @"docs\machina-test-suite-topology-m11b.md",
            @"docs\machina-presenter-scrollbar-state-machine-m11c.md",
        ];

        List<MarkdownCorpusDocumentReport> documentReports = [];

        foreach (string relativePath in corpusFiles)
        {
            string fullPath = Path.Combine(fullRepoRoot, relativePath);
            string sourceText = File.ReadAllText(fullPath);
            MarkdownCompilation compilation = MarkdownCompiler.Compile(sourceText);

            documentReports.Add(new MarkdownCorpusDocumentReport(
                relativePath.Replace('\\', '/'),
                compilation.Syntax.Blocks.Count,
                compilation.Syntax.Diagnostics.Count,
                compilation.Syntax.Diagnostics
                    .Select(static diagnostic =>
                        $"{diagnostic.Id} | {diagnostic.Severity} | {diagnostic.Span.StartLocation.Line}:{diagnostic.Span.StartLocation.Column} | {diagnostic.Message}")
                    .ToArray()));

            if (string.Equals(relativePath, "README.md", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(
                    Path.Combine(fullOutputDirectory, "copeland-markdown-readme.mir.json"),
                    MarkdownDumpWriter.SerializeMirAsJson(compilation.Mir));
            }

            if (relativePath.Replace('\\', '/').EndsWith("machina-oblivion-phase-closeout-m11g.md", StringComparison.Ordinal))
            {
                File.WriteAllText(
                    Path.Combine(fullOutputDirectory, "copeland-markdown-closeout.mir.json"),
                    MarkdownDumpWriter.SerializeMirAsJson(compilation.Mir));
            }
        }

        MarkdownCorpusReport report = new(
            "Copeland Markdown",
            corpusFiles.Select(static path => path.Replace('\\', '/')).ToArray(),
            documentReports,
            documentReports.Sum(static report => report.DiagnosticCount));

        File.WriteAllText(
            Path.Combine(fullOutputDirectory, "copeland-markdown-corpus-report.json"),
            MarkdownDumpWriter.SerializeCorpusReportAsJson(report));
        File.WriteAllText(
            Path.Combine(fullOutputDirectory, "copeland-markdown-corpus-report.txt"),
            MarkdownDumpWriter.DumpCorpusReport(report));

        return report;
    }
}
