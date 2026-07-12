namespace Copeland.Markdown;

public sealed record MarkdownCompilation(
    MarkdownTokenizedSource TokenizedSource,
    MarkdownDocument Syntax,
    DocumentMir Mir);

public static class MarkdownCompiler
{
    public static MarkdownCompilation Compile(string sourceText)
    {
        MarkdownTokenizedSource tokenizedSource = MarkdownLexer.Tokenize(sourceText);
        MarkdownDocument syntax = MarkdownParser.Parse(tokenizedSource);
        DocumentMir mir = MarkdownToDocumentMirLowerer.Lower(syntax);
        return new MarkdownCompilation(tokenizedSource, syntax, mir);
    }
}
