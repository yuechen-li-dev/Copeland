using System.Text;
using Copeland.TS.Diagnostics;

namespace Copeland.TS.Templates;

public enum DiagramDirection
{
    TopDown,
    LeftRight,
}

public enum DiagramBackendKind
{
    Flowchart,
    State,
}

public sealed record DiagramNode(string Id, string Label);

public sealed record DiagramEdge(
    string From,
    string To,
    string? Label = null,
    string? SemanticIdentity = null,
    int? Order = null);

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
        DiagramProvenance provenance,
        DiagramBackendKind backendKind,
        string? initialNodeId,
        IReadOnlyList<string> finalNodeIds)
    {
        Nodes = nodes;
        Edges = edges;
        Direction = direction;
        Provenance = provenance;
        BackendKind = backendKind;
        InitialNodeId = initialNodeId;
        FinalNodeIds = finalNodeIds;
    }

    public IReadOnlyList<DiagramNode> Nodes { get; }
    public IReadOnlyList<DiagramEdge> Edges { get; }
    public DiagramDirection Direction { get; }
    public DiagramProvenance Provenance { get; }
    public DiagramBackendKind BackendKind { get; }
    public string? InitialNodeId { get; }
    public IReadOnlyList<string> FinalNodeIds { get; }

    public static bool TryCreate(
        IEnumerable<DiagramNode> nodes,
        IEnumerable<DiagramEdge> edges,
        DiagramDirection direction,
        DiagramProvenance provenance,
        out Diagram? diagram,
        out IReadOnlyList<Diagnostic> diagnostics,
        DiagramBackendKind backendKind = DiagramBackendKind.Flowchart,
        string? initialNodeId = null,
        IEnumerable<string>? finalNodeIds = null)
    {
        DiagramNode[] normalizedNodes = nodes
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        DiagramEdge[] normalizedEdges = backendKind == DiagramBackendKind.State
            ? edges
                .OrderBy(edge => edge.Order ?? int.MaxValue)
                .ThenBy(edge => edge.SemanticIdentity, StringComparer.Ordinal)
                .ToArray()
            : edges
                .OrderBy(edge => edge.From, StringComparer.Ordinal)
                .ThenBy(edge => edge.To, StringComparer.Ordinal)
                .ThenBy(edge => edge.Label, StringComparer.Ordinal)
                .ToArray();
        string[] normalizedFinalNodeIds = (finalNodeIds ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
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


        if (backendKind == DiagramBackendKind.State)
        {
            if (string.IsNullOrWhiteSpace(initialNodeId) || !nodeIds.Contains(initialNodeId))
            {
                errors.Add(new Diagnostic(
                    "COPE-DIAGRAM-0005",
                    $"State diagram initial node '{initialNodeId ?? "<missing>"}' must reference a known state.",
                    0,
                    0));
            }
            foreach (string finalNodeId in normalizedFinalNodeIds)
            {
                if (!nodeIds.Contains(finalNodeId))
                {
                    errors.Add(new Diagnostic(
                        "COPE-DIAGRAM-0006",
                        $"State diagram final node '{finalNodeId}' must reference a known state.",
                        0,
                        0));
                }
            }
        }

        diagnostics = errors;
        diagram = errors.Count == 0
            ? new Diagram(
                normalizedNodes,
                normalizedEdges,
                direction,
                provenance,
                backendKind,
                initialNodeId,
                normalizedFinalNodeIds)
            : null;
        return diagram is not null;
    }
}

public static class MermaidEmitter
{
    public static string Emit(Diagram diagram)
        => diagram.BackendKind switch
        {
            DiagramBackendKind.Flowchart => EmitFlowchart(diagram),
            DiagramBackendKind.State => EmitStateDiagram(diagram),
            _ => throw new InvalidOperationException($"Unsupported diagram backend '{diagram.BackendKind}'."),
        };

    private static string EmitFlowchart(Diagram diagram)
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

    private static string EmitStateDiagram(Diagram diagram)
    {
        var builder = new StringBuilder();
        builder.AppendLine("stateDiagram-v2");

        Dictionary<string, string> backendIds = CreateBackendIds(diagram);
        foreach (DiagramNode node in diagram.Nodes)
        {
            builder.Append("    state \"");
            builder.Append(EscapeStateNodeLabel(node.Label));
            builder.Append("\" as ");
            builder.AppendLine(backendIds[node.Id]);
        }

        builder.Append("    [*] --> ");
        builder.AppendLine(backendIds[diagram.InitialNodeId!]);

        foreach (DiagramEdge edge in diagram.Edges)
        {
            builder.Append("    ");
            builder.Append(backendIds[edge.From]);
            builder.Append(" --> ");
            builder.Append(backendIds[edge.To]);
            if (!string.IsNullOrEmpty(edge.Label))
            {
                builder.Append(": ");
                builder.Append(EscapeStateTransitionLabel(edge.Label));
            }
            builder.AppendLine();
        }

        foreach (string finalNodeId in diagram.FinalNodeIds)
        {
            builder.Append("    ");
            builder.Append(backendIds[finalNodeId]);
            builder.AppendLine(" --> [*]");
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> CreateBackendIds(Diagram diagram)
        => diagram.Nodes
            .Select((node, index) => (node.Id, BackendId: $"s{index}"))
            .ToDictionary(item => item.Id, item => item.BackendId, StringComparer.Ordinal);

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

    private static string EscapeStateNodeLabel(string text)
    {
        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var builder = new StringBuilder();
        foreach (char character in normalized)
        {
            builder.Append(character switch
            {
                '&' => "&amp;",
                '"' => "&quot;",
                ':' => ":",
                '[' => "[",
                ']' => "]",
                '{' => "{",
                '}' => "}",
                '<' => "&lt;",
                '>' => "&gt;",
                '\n' => "<br/>",
                _ => character.ToString(),
            });
        }
        return builder.ToString();
    }

    private static string EscapeStateTransitionLabel(string text)
        => text
            .Replace("\r\n", " / ", StringComparison.Ordinal)
            .Replace("\r", " / ", StringComparison.Ordinal)
            .Replace("\n", " / ", StringComparison.Ordinal);
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
