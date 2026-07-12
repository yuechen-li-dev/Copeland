using System.Text;

namespace Copeland.Script.Syntax;

public static class SyntaxTreeDumper
{
    public static string Dump(SyntaxNode node)
    {
        var sb = new StringBuilder();
        DumpNode(sb, node, string.Empty, isLast: true);
        return sb.ToString();
    }

    private static void DumpNode(StringBuilder sb, SyntaxNode node, string indent, bool isLast)
    {
        sb.Append(indent);
        sb.Append(isLast ? "└──" : "├──");
        sb.Append(node.Kind);
        sb.AppendLine();

        var children = node.GetChildren().ToArray();
        for (var i = 0; i < children.Length; i++)
        {
            var childIsLast = i == children.Length - 1;
            if (children[i] is SyntaxNode childNode)
            {
                DumpNode(sb, childNode, indent + (isLast ? "   " : "│  "), childIsLast);
                continue;
            }

            DumpToken(sb, (SyntaxToken)children[i], indent + (isLast ? "   " : "│  "), childIsLast);
        }
    }

    private static void DumpToken(StringBuilder sb, SyntaxToken token, string indent, bool isLast)
    {
        sb.Append(indent);
        sb.Append(isLast ? "└──" : "├──");
        sb.Append(token.Kind);

        if (!string.IsNullOrEmpty(token.Text))
        {
            sb.Append(" '");
            sb.Append(token.Text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal));
            sb.Append('\'');
        }

        sb.AppendLine();
    }
}
