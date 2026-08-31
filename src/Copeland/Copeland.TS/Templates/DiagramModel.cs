using System.Text;
using Copeland.TS.Diagnostics;

namespace Copeland.TS.Templates;

public enum DiagramDirection
{
    TopDown,
    LeftRight,
}

public sealed record DiagramNode(string Id, string Label);

public sealed record DiagramEdge(string From, string To, string? Label = null);

public sealed record DiagramProvenance(string Template, string? ReflectedType);

/// <summary>
/// Backend-independent semantic graph data. Node identity and edge references
/// contain no Mermaid, layout, coordinate, or runtime-reflection concepts.
/// </summary>
public sealed class Diagram
{
    private Diagram(
        IReadOnlyList<DiagramNode> nodes,
        IReadOnlyList<DiagramEdge> edges,
        DiagramDirection direction,
        DiagramProvenance provenance)
    {
        Nodes = nodes;
        Edges = edges;
        Direction = direction;
        Provenance = provenance;
    }

    public IReadOnlyList<DiagramNode> Nodes { get; }
    public IReadOnlyList<DiagramEdge> Edges { get; }
    public DiagramDirection Direction { get; }
    public DiagramProvenance Provenance { get; }

    public static bool TryCreate(
        IEnumerable<DiagramNode> nodes,
        IEnumerable<DiagramEdge> edges,
        DiagramDirection direction,
        DiagramProvenance provenance,
        out Diagram? diagram,
        out IReadOnlyList<Diagnostic> diagnostics)
    {
        DiagramNode[] normalizedNodes = nodes
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        DiagramEdge[] normalizedEdges = edges
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ThenBy(edge => edge.Label, StringComparer.Ordinal)
            .ToArray();
        var errors = new List<Diagnostic>();

        foreach (IGrouping<string, DiagramNode> duplicate in normalizedNodes.GroupBy(node => node.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(duplicate.Key) || duplicate.Count() > 1)
            {
                string display = string.IsNullOrWhiteSpace(duplicate.Key) ? "<empty>" : duplicate.Key;
                errors.Add(new Diagnostic("COPE-DIAGRAM-0001", $"Diagram node ID '{display}' must be non-empty and unique.", 0, 0));
            }
        }

        HashSet<string> nodeIds = normalizedNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (DiagramEdge edge in normalizedEdges)
        {
            if (!nodeIds.Contains(edge.From) || !nodeIds.Contains(edge.To))
            {
                errors.Add(new Diagnostic(
                    "COPE-DIAGRAM-0002",
                    $"Diagram edge '{edge.From}' -> '{edge.To}' references an unknown node.",
                    0,
                    0));
            }
        }

        diagnostics = errors;
        diagram = errors.Count == 0
            ? new Diagram(normalizedNodes, normalizedEdges, direction, provenance)
            : null;
        return diagram is not null;
    }
}

public static class MermaidEmitter
{
    public static string Emit(Diagram diagram)
    {
        var builder = new StringBuilder();
        builder.Append("flowchart ");
        builder.AppendLine(diagram.Direction == DiagramDirection.TopDown ? "TD" : "LR");

        var backendIds = diagram.Nodes
            .Select((node, index) => (node.Id, BackendId: $"n{index}"))
            .ToDictionary(item => item.Id, item => item.BackendId, StringComparer.Ordinal);
        foreach (DiagramNode node in diagram.Nodes)
        {
            builder.Append("    ");
            builder.Append(backendIds[node.Id]);
            builder.Append("[\"");
            builder.Append(EscapeLabel(node.Label));
            builder.AppendLine("\"]");
        }

        foreach (DiagramEdge edge in diagram.Edges)
        {
            builder.Append("    ");
            builder.Append(backendIds[edge.From]);
            if (string.IsNullOrEmpty(edge.Label))
            {
                builder.Append(" --> ");
            }
            else
            {
                builder.Append(" -- \"");
                builder.Append(EscapeLabel(edge.Label));
                builder.Append("\" --> ");
            }
            builder.AppendLine(backendIds[edge.To]);
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string EscapeLabel(string label)
    {
        string normalized = label
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var builder = new StringBuilder();
        foreach (char character in normalized)
        {
            builder.Append(character switch
            {
                '&' => "&amp;",
                '"' => "&quot;",
                '[' => "&#91;",
                ']' => "&#93;",
                '{' => "&#123;",
                '}' => "&#125;",
                '<' => "&lt;",
                '>' => "&gt;",
                '\n' => "<br/>",
                _ => character.ToString(),
            });
        }
        return builder.ToString();
    }
}

public static class DiagramMaterializer
{
    public static string MaterializeMermaid(Diagram diagram, string outputPath)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, MermaidEmitter.Emit(diagram), new UTF8Encoding(false));
        return fullPath;
    }
}
