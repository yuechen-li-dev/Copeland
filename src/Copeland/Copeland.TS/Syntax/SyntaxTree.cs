using Copeland.TS.Diagnostics;

namespace Copeland.TS.Syntax;

public sealed class SyntaxTree
{
    private SyntaxTree(string text, CompilationUnitSyntax root, IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics)
    {
        Text = text;
        Root = root;
        Tokens = tokens;
        Diagnostics = diagnostics;
    }

    public string Text { get; }

    public CompilationUnitSyntax Root { get; }

    public IReadOnlyList<SyntaxToken> Tokens { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public static SyntaxTree Parse(string text)
        => Parse(text, SourceFileKind.TypeScript);

    public static SyntaxTree Parse(string text, SourceFileKind fileKind)
    {
        var parser = new Parser(text, fileKind == SourceFileKind.TypeScriptXml, fileKind == SourceFileKind.TypeScriptModule);
        var root = parser.ParseCompilationUnit();
        var diagnostics = parser.Diagnostics.ToArray();
        var tokens = CollectTokens(root).ToArray();
        return new SyntaxTree(text, root, tokens, diagnostics);
    }

    public static SyntaxTree Parse(string text, string? sourcePath)
    {
        SourceFileKind fileKind = SourceFileKindExtensions.FromSourcePath(sourcePath);
        SyntaxTree tree = Parse(text, fileKind);

        if (sourcePath is null || !sourcePath.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase))
        {
            return tree;
        }

        var diagnostics = tree.Diagnostics
            .Append(new Diagnostic(
                "COPE-TSXML-0001",
                "TS-XML is available only in '.tsx' source files; '.jsx' is not a Copeland source extension.",
                0,
                Math.Max(1, text.Length)))
            .ToArray();
        return new SyntaxTree(tree.Text, tree.Root, tree.Tokens, diagnostics);
    }

    public static SyntaxTree ParseTokens(string text)
    {
        var lexer = new Lexer(text);
        var tokens = new List<SyntaxToken>();

        while (true)
        {
            var token = lexer.NextToken();
            tokens.Add(token);
            if (token.Kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }

        var root = new CompilationUnitSyntax([], tokens[^1]);
        return new SyntaxTree(text, root, tokens, lexer.Diagnostics.ToArray());
    }

    private static IEnumerable<SyntaxToken> CollectTokens(SyntaxNode node)
    {
        foreach (var child in node.GetChildren())
        {
            switch (child)
            {
                case SyntaxToken token:
                    yield return token;
                    break;
                case SyntaxNode childNode:
                    foreach (var descendant in CollectTokens(childNode))
                    {
                        yield return descendant;
                    }

                    break;
            }
        }
    }
}

public enum SourceFileKind
{
    TypeScript,
    TypeScriptModule,
    TypeScriptXml,
}

public static class SourceFileKindExtensions
{
    public static SourceFileKind FromSourcePath(string? sourcePath)
        => sourcePath is not null && sourcePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
            ? SourceFileKind.TypeScriptXml
            : sourcePath is not null && sourcePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                ? SourceFileKind.TypeScriptModule
                : SourceFileKind.TypeScript;
}
