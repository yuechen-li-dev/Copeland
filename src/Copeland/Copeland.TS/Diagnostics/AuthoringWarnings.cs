using Copeland.TS.Syntax;

namespace Copeland.TS.Diagnostics;

/// <summary>Source advice shared by Copeland and Visual TypeScript. Warnings never
/// participate in legality or suppress lowering. Analyze authored syntax, not generated tapes.</summary>
public static class AuthoringWarnings
{
    public const int LongConditionalThreshold = 5;

    public static IReadOnlyList<Diagnostic> Analyze(SyntaxNode root)
    {
        var warnings = new List<Diagnostic>();
        Visit(root, false);
        return warnings;

        void Visit(SyntaxNode node, bool continuation)
        {
            if (node is IfStatementSyntax conditional && !continuation)
            {
                int branches = 1;
                var next = conditional.ElseStatement;
                while (next is IfStatementSyntax branch)
                {
                    branches++;
                    next = branch.ElseStatement;
                }
                if (branches >= LongConditionalThreshold)
                {
                    warnings.Add(new("COPE-STYLE-0001",
                        $"This if/else-if chain has {branches} conditional branches. Consider switch/match to make the alternatives easier to review.",
                        conditional.IfKeyword.Position, conditional.IfKeyword.Text.Length));
                }
            }
            foreach (var child in node.GetChildren())
            {
                if (child is SyntaxNode syntax)
                {
                    bool isContinuation = node is IfStatementSyntax parent
                        && ReferenceEquals(parent.ElseStatement, syntax) && syntax is IfStatementSyntax;
                    Visit(syntax, isContinuation);
                }
            }
        }
    }
}
