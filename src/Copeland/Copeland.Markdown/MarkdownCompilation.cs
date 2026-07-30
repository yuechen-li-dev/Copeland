namespace Copeland.Markdown;

public sealed record MarkdownCompilation(
    MarkdownTokenizedSource TokenizedSource,
    MarkdownDocument Syntax,
    DocumentMir Mir);

public static class MarkdownCompiler
{
    public static MarkdownCompilation Compile(string sourceText)
        => Compile(sourceText, "<memory>");

    public static MarkdownCompilation Compile(string sourceText, string sourcePath)
    {
        MarkdownTokenizedSource tokenizedSource = MarkdownLexer.Tokenize(sourceText);
        MarkdownDocument syntax = MarkdownParser.Parse(tokenizedSource);
        DocumentMir lowered = MarkdownToDocumentMirLowerer.Lower(syntax);
        DocumentMir mir = DocumentMirBinder.Bind(
            lowered,
            "markdown::" + sourcePath,
            ownerSymbol: null,
            DocumentSourceKind.Markdown,
            sourcePath);
        return new MarkdownCompilation(tokenizedSource, syntax, mir);
    }
}
