using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Copeland.TS.Templates;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.App;

public enum OblivionDiagramRendererKind
{
    Mermaid,
    NativeSvg,
}

public sealed record OblivionNativeDiagramPoint(double X, double Y);

public sealed record OblivionResolvedDiagramNode(
    string Id,
    string Label,
    double X,
    double Y,
    double Width,
    double Height,
    string SourceIdentity);

public sealed record OblivionResolvedDiagramEdge(
    string Id,
    string From,
    string To,
    string? Label,
    string? DisplayLabel,
    IReadOnlyList<OblivionNativeDiagramPoint> Route,
    OblivionNativeDiagramPoint LabelAnchor,
    string SourceIdentity,
    string RouteKind);

public sealed record OblivionNativeLayoutDiagnostic(
    string Code,
    string Message,
    string? EdgeIdentity = null);

public sealed record OblivionNativeLayoutMetrics(
    int LayerCount,
    int ComponentCount,
    int BackEdgeCount,
    int CrossLayerEdgeCount,
    int CrossingEstimate);

public sealed record OblivionResolvedDiagram(
    double Width,
    double Height,
    string LayoutPolicyIdentity,
    IReadOnlyList<OblivionResolvedDiagramNode> Nodes,
    IReadOnlyList<OblivionResolvedDiagramEdge> Edges,
    OblivionNativeLayoutMetrics? Metrics = null,
    IReadOnlyList<OblivionNativeLayoutDiagnostic>? Diagnostics = null);

public sealed record OblivionNativeLayoutPolicy(
    string Identity,
    string Strategy,
    IReadOnlyDictionary<string, OblivionNativeDiagramPoint>? Placements = null);

public static class OblivionNativeDiagramPolicies
{
    public const string PhaseLanesV1 = "phase-lanes-v1";
    public const string BranchingCallsV1 = "branching-calls-v1";
    public const string AutomaticLayeredV1 = "automatic-layered-v1";

    private static readonly IReadOnlyDictionary<string, OblivionNativeDiagramPoint> PhasePlacements =
        new Dictionary<string, OblivionNativeDiagramPoint>(StringComparer.Ordinal)
        {
            ["WorkspaceIntake"] = new(40, 44),
            ["Parsing"] = new(240, 44),
            ["Binding"] = new(440, 44),
            ["Lowering"] = new(640, 44),
            ["BackendSelection"] = new(840, 44),
            ["Emitting"] = new(1040, 44),
            ["ArtifactValidation"] = new(1240, 44),
            ["CacheQualification"] = new(1440, 44),
            ["CardProjection"] = new(1640, 44),
            ["CardRealization"] = new(1840, 44),
            ["HumanReview"] = new(2040, 44),
            ["Accepted"] = new(2240, 44),
            ["Diagnostics"] = new(1640, 246),
            ["SourceRepair"] = new(1240, 404),
            ["RendererRecovery"] = new(1040, 404),
            ["Rejected"] = new(2040, 404),
        };

    public static OblivionNativeLayoutPolicy Select(Diagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        bool isQualifiedPhaseFlow = diagram.BackendKind == DiagramBackendKind.State &&
            diagram.Nodes.Count == PhasePlacements.Count &&
            diagram.Nodes.All(node => PhasePlacements.ContainsKey(node.Label));
        if (isQualifiedPhaseFlow)
        {
            return new OblivionNativeLayoutPolicy(PhaseLanesV1, "explicit-phase-lanes", PhasePlacements);
        }

        if (diagram.BackendKind == DiagramBackendKind.Flowchart && IsBranchingCallGraph(diagram))
        {
            return new OblivionNativeLayoutPolicy(BranchingCallsV1, "branching-call-ownership");
        }

        return new OblivionNativeLayoutPolicy(AutomaticLayeredV1, "automatic-layered");
    }

    private static bool IsBranchingCallGraph(Diagram diagram)
    {
        if (diagram.Provenance.ReflectedType is null ||
            diagram.Edges.Count != diagram.Nodes.Count - 1)
        {
            return false;
        }

        return diagram.Nodes.Any(node =>
            diagram.Edges.Count(edge => edge.From == node.Id) == diagram.Edges.Count);
    }
}

public static class OblivionNativeDiagramLayout
{
    public const int MaximumNodes = 256;
    public const int MaximumEdges = 512;
    public const int MaximumLabelBytes = 4096;
    private static readonly IReadOnlyDictionary<string, OblivionNativeDiagramPoint> PhaseLabelAnchors =
        new Dictionary<string, OblivionNativeDiagramPoint>(StringComparer.Ordinal)
        {
            ["SourceMissing"] = new(46, 142),
            ["ParseFailed"] = new(250, 168),
            ["BindFailed"] = new(450, 194),
            ["LowerFailed"] = new(650, 220),
            ["RendererUnavailable"] = new(836, 326),
            ["EmitFailed"] = new(1036, 350),
            ["ArtifactCorrupt"] = new(1236, 326),
            ["CacheStale"] = new(1390, 142),
            ["ProjectionFailed"] = new(1636, 168),
            ["HostFailed"] = new(1836, 194),
            ["RevisionRequested"] = new(1836, 326),
        };

    public static OblivionResolvedDiagram Resolve(
        Diagram diagram,
        OblivionNativeLayoutPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ValidateBounds(diagram);
        OblivionNativeLayoutPolicy effectivePolicy = policy ?? OblivionNativeDiagramPolicies.Select(diagram);
        AutomaticLayoutPlan? automaticPlan = null;
        IReadOnlyList<OblivionResolvedDiagramNode> nodes = effectivePolicy.Strategy switch
        {
            "explicit-phase-lanes" => ResolveExplicit(diagram, effectivePolicy),
            "branching-call-ownership" => ResolveBranching(diagram),
            "automatic-layered" => (automaticPlan = ResolveAutomatic(diagram)).Nodes,
            _ => throw new InvalidOperationException(
                $"Unsupported native layout strategy '{effectivePolicy.Strategy}'."),
        };
        IReadOnlyList<OblivionResolvedDiagramEdge> edges = ResolveEdges(
            diagram,
            nodes,
            effectivePolicy.Identity,
            automaticPlan);
        double width = Math.Max(320, nodes.Max(node => node.X + node.Width) + 40);
        double height = Math.Max(220, nodes.Max(node => node.Y + node.Height) + 52);
        return new OblivionResolvedDiagram(
            width,
            height,
            effectivePolicy.Identity,
            nodes,
            edges,
            automaticPlan?.Metrics,
            automaticPlan?.Diagnostics ?? []);
    }

    private static IReadOnlyList<OblivionResolvedDiagramNode> ResolveExplicit(
        Diagram diagram,
        OblivionNativeLayoutPolicy policy)
    {
        if (policy.Placements is null)
        {
            throw new InvalidOperationException(
                $"Native layout policy '{policy.Identity}' has no placements.");
        }

        List<OblivionResolvedDiagramNode> nodes = [];
        foreach (DiagramNode node in diagram.Nodes)
        {
            if (!policy.Placements.TryGetValue(node.Label, out OblivionNativeDiagramPoint? placement))
            {
                throw new InvalidOperationException(
                    $"Native layout policy '{policy.Identity}' has no placement for node '{node.Id}'.");
            }

            (double width, double height) = MeasureNode(node.Label);
            nodes.Add(CreateNode(node, placement.X, placement.Y, width, height));
        }
        return nodes;
    }

    private static IReadOnlyList<OblivionResolvedDiagramNode> ResolveBranching(Diagram diagram)
    {
        DiagramNode root = diagram.Nodes
            .OrderByDescending(node => diagram.Edges.Count(edge => edge.From == node.Id))
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .First();
        List<DiagramNode> branches = diagram.Nodes
            .Where(node => node.Id != root.Id)
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToList();
        List<OblivionResolvedDiagramNode> nodes = [];
        (double rootWidth, double rootHeight) = MeasureNode(root.Label);
        int rowCount = Math.Min(2, Math.Max(1, branches.Count));
        double branchHeight = (rowCount - 1) * 160;
        nodes.Add(CreateNode(root, 40, 60 + (branchHeight / 2), rootWidth, rootHeight));
        for (int index = 0; index < branches.Count; index++)
        {
            DiagramNode branch = branches[index];
            (double width, double height) = MeasureNode(branch.Label);
            int column = index / rowCount;
            int row = index % rowCount;
            nodes.Add(CreateNode(branch, 360 + (column * 280), 60 + (row * 160), width, height));
        }
        return nodes.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
    }

    private static AutomaticLayoutPlan ResolveAutomatic(Diagram diagram)
    {
        return AutomaticLayeredLayout.Resolve(diagram, MeasureNode, CreateNode);
    }

    private static IReadOnlyList<OblivionResolvedDiagramEdge> ResolveEdges(
        Diagram diagram,
        IReadOnlyList<OblivionResolvedDiagramNode> nodes,
        string policyIdentity,
        AutomaticLayoutPlan? automaticPlan)
    {
        Dictionary<string, OblivionResolvedDiagramNode> byId = nodes.ToDictionary(
            node => node.Id,
            StringComparer.Ordinal);
        List<OblivionResolvedDiagramEdge> edges = [];
        for (int index = 0; index < diagram.Edges.Count; index++)
        {
            DiagramEdge edge = diagram.Edges[index];
            OblivionResolvedDiagramNode from = byId[edge.From];
            OblivionResolvedDiagramNode to = byId[edge.To];
            (OblivionNativeDiagramPoint[] Route, OblivionNativeDiagramPoint DefaultLabelAnchor) routed =
                policyIdentity == OblivionNativeDiagramPolicies.BranchingCallsV1
                    ? RouteBranching(from, to, nodes)
                    : policyIdentity == OblivionNativeDiagramPolicies.AutomaticLayeredV1
                        ? RouteAutomatic(
                            from,
                            to,
                            edge,
                            index,
                            diagram.Direction,
                            nodes,
                            automaticPlan!)
                        : RouteOrthogonal(from, to, index);
            OblivionNativeDiagramPoint[] route = routed.Route;
            string id = edge.SemanticIdentity ??
                $"edge:{index.ToString(CultureInfo.InvariantCulture)}:{edge.From}:{edge.To}";
            string? displayLabel = edge.Label;
            OblivionNativeDiagramPoint labelAnchor = routed.DefaultLabelAnchor;
            if (policyIdentity == OblivionNativeDiagramPolicies.PhaseLanesV1)
            {
                string eventName = EventName(edge.Label);
                if (PhaseLabelAnchors.TryGetValue(eventName, out OblivionNativeDiagramPoint? explicitAnchor))
                {
                    displayLabel = $"{eventName} → {to.Label}";
                    labelAnchor = explicitAnchor;
                }
                else
                {
                    displayLabel = null;
                }
            }
            edges.Add(new OblivionResolvedDiagramEdge(
                id,
                edge.From,
                edge.To,
                edge.Label,
                displayLabel,
                route,
                labelAnchor,
                edge.SemanticIdentity ?? id,
                automaticPlan?.RouteKinds.GetValueOrDefault(AutomaticEdgeKey(edge, index)) ?? "orthogonal"));
        }
        return edges;
    }

    private static (
        OblivionNativeDiagramPoint[] Route,
        OblivionNativeDiagramPoint DefaultLabelAnchor) RouteAutomatic(
        OblivionResolvedDiagramNode from,
        OblivionResolvedDiagramNode to,
        DiagramEdge edge,
        int edgeIndex,
        DiagramDirection direction,
        IReadOnlyList<OblivionResolvedDiagramNode> nodes,
        AutomaticLayoutPlan plan)
    {
        string key = AutomaticEdgeKey(edge, edgeIndex);
        string routeKind = plan.RouteKinds[key];
        int parallelOffset = (edgeIndex % 5) * 7;
        if (routeKind == "self-loop")
        {
            double right = from.X + from.Width + 28 + parallelOffset;
            double top = Math.Max(12, from.Y - 26 - parallelOffset);
            return (
                [
                    new(from.X + from.Width, from.Y + (from.Height / 2)),
                    new(right, from.Y + (from.Height / 2)),
                    new(right, top),
                    new(from.X + (from.Width / 2), top),
                    new(from.X + (from.Width / 2), from.Y),
                ],
                new OblivionNativeDiagramPoint(right + 4, top + 14));
        }

        if (routeKind == "back-edge")
        {
            double lane = direction == DiagramDirection.LeftRight
                ? Math.Max(16, nodes.Min(node => node.Y) - 24 - parallelOffset)
                : Math.Max(16, nodes.Min(node => node.X) - 28 - parallelOffset);
            if (direction == DiagramDirection.LeftRight)
            {
                double startX = from.X + (from.Width / 2);
                double endX = to.X + (to.Width / 2);
                return (
                    [
                        new(startX, from.Y),
                        new(startX, lane),
                        new(endX, lane),
                        new(endX, to.Y),
                    ],
                    new OblivionNativeDiagramPoint((startX + endX) / 2 + 4, lane + 14));
            }

            double startY = from.Y + (from.Height / 2);
            double endY = to.Y + (to.Height / 2);
            return (
                [
                    new(from.X, startY),
                    new(lane, startY),
                    new(lane, endY),
                    new(to.X, endY),
                ],
                new OblivionNativeDiagramPoint(lane + 4, (startY + endY) / 2 - 4));
        }

        if (direction == DiagramDirection.TopDown)
        {
            double startX = from.X + (from.Width / 2);
            double startY = from.Y + from.Height;
            double endX = to.X + (to.Width / 2);
            double endY = to.Y;
            double middleY = startY + ((endY - startY) / 2) + parallelOffset;
            return (
                [
                    new(startX, startY),
                    new(startX, middleY),
                    new(endX, middleY),
                    new(endX, endY),
                ],
                new OblivionNativeDiagramPoint((startX + endX) / 2 + 4, middleY - 5));
        }

        double horizontalStartX = from.X + from.Width;
        double horizontalStartY = from.Y + (from.Height / 2);
        double horizontalEndX = to.X;
        double horizontalEndY = to.Y + (to.Height / 2);
        double middleX = horizontalStartX + ((horizontalEndX - horizontalStartX) / 2) + parallelOffset;
        return (
            [
                new(horizontalStartX, horizontalStartY),
                new(middleX, horizontalStartY),
                new(middleX, horizontalEndY),
                new(horizontalEndX, horizontalEndY),
            ],
            new OblivionNativeDiagramPoint(middleX + 4, (horizontalStartY + horizontalEndY) / 2 - 5));
    }

    private static string AutomaticEdgeKey(DiagramEdge edge, int index)
    {
        return edge.SemanticIdentity ??
            $"edge:{index.ToString(CultureInfo.InvariantCulture)}:{edge.From}:{edge.To}";
    }

    private static (
        OblivionNativeDiagramPoint[] Route,
        OblivionNativeDiagramPoint DefaultLabelAnchor) RouteOrthogonal(
        OblivionResolvedDiagramNode from,
        OblivionResolvedDiagramNode to,
        int edgeIndex)
    {
        double startX = from.X + from.Width;
        double startY = from.Y + (from.Height / 2);
        double endX = to.X;
        double endY = to.Y + (to.Height / 2);
        double middleX = startX <= endX
            ? startX + ((endX - startX) / 2)
            : Math.Max(16, Math.Min(startX, endX) - 24 - ((edgeIndex % 5) * 8));
        return (
            [
                new(startX, startY),
                new(middleX, startY),
                new(middleX, endY),
                new(endX, endY),
            ],
            new OblivionNativeDiagramPoint(middleX + 4, (startY + endY) / 2 - 4));
    }

    private static (
        OblivionNativeDiagramPoint[] Route,
        OblivionNativeDiagramPoint DefaultLabelAnchor) RouteBranching(
        OblivionResolvedDiagramNode from,
        OblivionResolvedDiagramNode to,
        IReadOnlyList<OblivionResolvedDiagramNode> nodes)
    {
        double startX = from.X + from.Width;
        double startY = from.Y + (from.Height / 2);
        bool upper = to.Y < from.Y;
        double laneY = upper
            ? 24
            : nodes.Max(node => node.Y + node.Height) + 24;
        double endX = to.X + (to.Width / 2);
        double endY = upper ? to.Y : to.Y + to.Height;
        return (
            [
                new(startX, startY),
                new(startX + 28, startY),
                new(startX + 28, laneY),
                new(endX, laneY),
                new(endX, endY),
            ],
            new OblivionNativeDiagramPoint(endX + 4, upper ? laneY + 14 : laneY - 8));
    }

    private static string EventName(string? label)
    {
        string value = label ?? string.Empty;
        int separator = value.IndexOfAny([' ', '[', '(']);
        return separator < 0 ? value : value[..separator];
    }

    private static OblivionResolvedDiagramNode CreateNode(
        DiagramNode node,
        double x,
        double y,
        double width,
        double height)
    {
        return new OblivionResolvedDiagramNode(node.Id, node.Label, x, y, width, height, node.Id);
    }

    private static (double Width, double Height) MeasureNode(string label)
    {
        int longestLine = label.Split('\n').Max(line => line.Length);
        int lineCount = label.Count(character => character == '\n') + 1;
        double width = Math.Clamp(32 + (longestLine * 8), 150, 220);
        double height = Math.Max(54, 28 + (lineCount * 20));
        return (width, height);
    }

    private static void ValidateBounds(Diagram diagram)
    {
        if (diagram.Nodes.Count == 0 || diagram.Nodes.Count > MaximumNodes ||
            diagram.Edges.Count > MaximumEdges)
        {
            throw new InvalidOperationException(
                $"Native layout supports 1-{MaximumNodes} nodes and at most {MaximumEdges} edges.");
        }
        if (diagram.Nodes.Any(node => Encoding.UTF8.GetByteCount(node.Label) > MaximumLabelBytes) ||
            diagram.Edges.Any(edge => Encoding.UTF8.GetByteCount(edge.Label ?? string.Empty) > MaximumLabelBytes))
        {
            throw new InvalidOperationException(
                $"Native layout labels may not exceed {MaximumLabelBytes} UTF-8 bytes.");
        }
    }
}

public static class OblivionNativeDiagramSvgEmitter
{
    public const int MaximumSvgBytes = 2 * 1024 * 1024;

    public static string Emit(
        OblivionResolvedDiagram diagram,
        OblivionResolvedAppearance appearance,
        string title,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        bool dark = appearance == OblivionResolvedAppearance.Dark;
        string background = dark ? "#0f172a" : "#ffffff";
        string nodeFill = dark ? "#111827" : "#f8fafc";
        string nodeStroke = dark ? "#38bdf8" : "#2563eb";
        string foreground = dark ? "#e2e8f0" : "#18181b";
        string edgeColor = dark ? "#94a3b8" : "#475569";
        StringBuilder svg = new();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-labelledby=\"diagram-title diagram-description\" width=\"");
        svg.Append(Number(diagram.Width));
        svg.Append("\" height=\"");
        svg.Append(Number(diagram.Height));
        svg.Append("\" viewBox=\"0 0 ");
        svg.Append(Number(diagram.Width));
        svg.Append(' ');
        svg.Append(Number(diagram.Height));
        svg.AppendLine("\">");
        svg.Append("  <title id=\"diagram-title\">");
        svg.Append(Escape(title));
        svg.AppendLine("</title>");
        svg.Append("  <desc id=\"diagram-description\">");
        svg.Append(Escape(description ?? $"Diagram with {diagram.Nodes.Count} nodes and {diagram.Edges.Count} edges."));
        svg.AppendLine("</desc>");
        svg.AppendLine("  <defs>");
        svg.AppendLine($"    <marker id=\"diagram-arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\">");
        svg.AppendLine($"      <path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"{edgeColor}\"/>");
        svg.AppendLine("    </marker>");
        svg.AppendLine("  </defs>");
        svg.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{background}\"/>");
        svg.AppendLine("  <g id=\"edges\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");
        foreach (OblivionResolvedDiagramEdge edge in diagram.Edges)
        {
            svg.Append("    <g data-edge-id=\"");
            svg.Append(Escape(edge.Id));
            svg.Append("\" data-source-identity=\"");
            svg.Append(Escape(edge.SourceIdentity));
            svg.AppendLine("\">");
            svg.Append("      <title>");
            svg.Append(Escape(edge.Label ?? $"{edge.From} to {edge.To}"));
            svg.AppendLine("</title>");
            svg.Append("      <path d=\"");
            svg.Append(Route(edge.Route));
            svg.Append("\" stroke=\"");
            svg.Append(edgeColor);
            svg.AppendLine("\" stroke-width=\"2\" marker-end=\"url(#diagram-arrow)\"/>");
            if (!string.IsNullOrWhiteSpace(edge.DisplayLabel))
            {
                svg.Append("      <text x=\"");
                svg.Append(Number(edge.LabelAnchor.X));
                svg.Append("\" y=\"");
                svg.Append(Number(edge.LabelAnchor.Y));
                svg.Append("\" font-family=\"Segoe UI, sans-serif\" font-size=\"13\" fill=\"");
                svg.Append(foreground);
                svg.Append("\">");
                svg.Append(Escape(edge.DisplayLabel));
                svg.AppendLine("</text>");
            }
            svg.AppendLine("    </g>");
        }
        svg.AppendLine("  </g>");
        svg.AppendLine("  <g id=\"nodes\">");
        foreach (OblivionResolvedDiagramNode node in diagram.Nodes)
        {
            svg.Append("    <g data-node-id=\"");
            svg.Append(Escape(node.Id));
            svg.Append("\" data-source-identity=\"");
            svg.Append(Escape(node.SourceIdentity));
            svg.AppendLine("\">");
            svg.Append("      <title>");
            svg.Append(Escape(node.Label));
            svg.AppendLine("</title>");
            svg.Append("      <rect x=\"");
            svg.Append(Number(node.X));
            svg.Append("\" y=\"");
            svg.Append(Number(node.Y));
            svg.Append("\" width=\"");
            svg.Append(Number(node.Width));
            svg.Append("\" height=\"");
            svg.Append(Number(node.Height));
            svg.Append("\" rx=\"8\" fill=\"");
            svg.Append(nodeFill);
            svg.Append("\" stroke=\"");
            svg.Append(nodeStroke);
            svg.AppendLine("\" stroke-width=\"2\"/>");
            svg.Append("      <text x=\"");
            svg.Append(Number(node.X + (node.Width / 2)));
            svg.Append("\" y=\"");
            svg.Append(Number(node.Y + (node.Height / 2) + 6));
            svg.Append("\" text-anchor=\"middle\" font-family=\"Segoe UI, sans-serif\" font-size=\"16\" fill=\"");
            svg.Append(foreground);
            svg.Append("\">");
            svg.Append(Escape(node.Label));
            svg.AppendLine("</text>");
            svg.AppendLine("    </g>");
        }
        svg.AppendLine("  </g>");
        svg.AppendLine("</svg>");
        string result = svg.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Encoding.UTF8.GetByteCount(result) > MaximumSvgBytes)
        {
            throw new InvalidOperationException(
                $"Native SVG output exceeded the {MaximumSvgBytes} byte bound.");
        }
        return result;
    }

    private static string Route(IReadOnlyList<OblivionNativeDiagramPoint> route)
    {
        StringBuilder path = new();
        for (int index = 0; index < route.Count; index++)
        {
            OblivionNativeDiagramPoint point = route[index];
            path.Append(index == 0 ? "M " : " L ");
            path.Append(Number(point.X));
            path.Append(' ');
            path.Append(Number(point.Y));
        }
        return path.ToString();
    }

    private static string Number(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}

public sealed record OblivionNativeDerivedArtifactKey(
    string SemanticHash,
    string RendererIdentity,
    string LayoutPolicyIdentity,
    string Appearance,
    string OutputFormat,
    string FixedOptions)
{
    public string Value => OblivionMermaidHashing.HashUtf8(string.Join(
        "\n",
        SemanticHash,
        RendererIdentity,
        LayoutPolicyIdentity,
        Appearance,
        OutputFormat,
        FixedOptions));
}

public sealed class OblivionNativeSvgRenderer : IOblivionDiagramRenderer
{
    public const string RendererId = "native-svg-v1";
    public const string RendererVersion = "1.0.4";
    public const string OutputFormat = "svg";
    public const string FixedOptions = "canonical-svg;inert;segoe-ui-fallback;max-2mib";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly Diagram _diagram;
    private readonly string _semanticFingerprint;
    private readonly OblivionNativeLayoutPolicy _policy;

    public OblivionNativeSvgRenderer(
        Diagram diagram,
        string semanticFingerprint,
        OblivionNativeLayoutPolicy? policy = null)
    {
        _diagram = diagram ?? throw new ArgumentNullException(nameof(diagram));
        _semanticFingerprint = string.IsNullOrWhiteSpace(semanticFingerprint)
            ? throw new ArgumentException("A semantic fingerprint is required.", nameof(semanticFingerprint))
            : semanticFingerprint;
        _policy = policy ?? OblivionNativeDiagramPolicies.Select(diagram);
    }

    public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        OblivionResolvedDiagram resolved;
        try
        {
            resolved = OblivionNativeDiagramLayout.Resolve(_diagram, _policy);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Failure(request, "OBLIVION-NATIVE-LAYOUT-FAILED", exception.Message);
        }

        OblivionNativeDerivedArtifactKey key = new(
            _semanticFingerprint,
            RendererId + "@" + RendererVersion,
            _policy.Identity,
            request.Appearance.ToString().ToLowerInvariant(),
            OutputFormat,
            FixedOptions);
        string cacheKey = key.Value;
        OblivionDiagramProvenance provenance = new(
            "NativeSvg",
            _semanticFingerprint,
            RendererId,
            RendererVersion,
            "layout-and-emit-native-svg",
            OutputFormat,
            request.Appearance,
            $"layout={_policy.Identity};appearance={request.Appearance.ToString().ToLowerInvariant()};{FixedOptions}",
            "Oblivion.App native diagram renderer",
            request.WorkspaceId,
            request.PageId,
            request.CardId,
            request.ContentId,
            request.SourceReference,
            Derived: true);
        Directory.CreateDirectory(request.OutputDirectory);
        string artifactPath = Path.Combine(request.OutputDirectory, cacheKey + ".svg");
        string metadataPath = Path.Combine(request.OutputDirectory, cacheKey + ".json");
        NativeCacheMetadata expected = new(1, key, provenance, resolved);
        string? invalidReason = ValidateCache(artifactPath, metadataPath, expected);
        if (invalidReason is null && File.Exists(artifactPath))
        {
            return Success(artifactPath, cacheKey, true, provenance, resolved, []);
        }

        List<OblivionCardDiagnostic> diagnostics = [];
        if (invalidReason is not null)
        {
            diagnostics.Add(Diagnostic(
                request,
                "OBLIVION-NATIVE-CACHE-INVALID",
                $"Native SVG cache entry was ignored: {invalidReason}"));
        }
        try
        {
            string svg = OblivionNativeDiagramSvgEmitter.Emit(
                resolved,
                request.Appearance,
                request.ContentId,
                $"{_diagram.Provenance.Template}; layout {_policy.Identity}.");
            WriteAtomically(artifactPath, svg);
            WriteAtomically(metadataPath, JsonSerializer.Serialize(expected, JsonOptions));
            return Success(artifactPath, cacheKey, false, provenance, resolved, diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Failure(request, "OBLIVION-NATIVE-SVG-EMISSION-FAILED", exception.Message, cacheKey, provenance);
        }
    }

    private static string? ValidateCache(
        string artifactPath,
        string metadataPath,
        NativeCacheMetadata expected)
    {
        bool artifactExists = File.Exists(artifactPath);
        bool metadataExists = File.Exists(metadataPath);
        if (!artifactExists && !metadataExists)
        {
            return null;
        }
        if (!artifactExists || !metadataExists)
        {
            return "metadata or artifact was missing";
        }
        try
        {
            NativeCacheMetadata? actual = JsonSerializer.Deserialize<NativeCacheMetadata>(
                File.ReadAllText(metadataPath),
                JsonOptions);
            if (actual is null ||
                actual.Format != expected.Format ||
                actual.Key != expected.Key ||
                actual.Provenance != expected.Provenance ||
                JsonSerializer.Serialize(actual.ResolvedDiagram, JsonOptions) !=
                    JsonSerializer.Serialize(expected.ResolvedDiagram, JsonOptions))
            {
                return "metadata did not match the semantic graph, renderer, layout, appearance, or owner";
            }
            string svg = File.ReadAllText(artifactPath);
            return IsInertSvg(svg) ? null : "cached SVG was malformed or unsafe";
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return $"metadata was unreadable: {exception.Message}";
        }
    }

    private static bool IsInertSvg(string svg)
    {
        return svg.StartsWith("<svg ", StringComparison.Ordinal) &&
            svg.EndsWith("</svg>\n", StringComparison.Ordinal) &&
            svg.Contains("<title id=\"diagram-title\">", StringComparison.Ordinal) &&
            svg.Contains("<desc id=\"diagram-description\">", StringComparison.Ordinal) &&
            !svg.Contains("<script", StringComparison.OrdinalIgnoreCase) &&
            !svg.Contains("foreignObject", StringComparison.OrdinalIgnoreCase) &&
            !svg.Contains("href=", StringComparison.OrdinalIgnoreCase) &&
            Encoding.UTF8.GetByteCount(svg) <= OblivionNativeDiagramSvgEmitter.MaximumSvgBytes;
    }

    private static void WriteAtomically(string path, string content)
    {
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static OblivionDiagramRenderResult Success(
        string artifactPath,
        string cacheKey,
        bool cacheHit,
        OblivionDiagramProvenance provenance,
        OblivionResolvedDiagram resolved,
        IReadOnlyList<OblivionCardDiagnostic> diagnostics)
    {
        return new OblivionDiagramRenderResult(
            true,
            RendererId,
            RendererVersion,
            provenance.SourceHash,
            artifactPath,
            "image/svg+xml",
            diagnostics,
            cacheKey,
            cacheHit,
            provenance,
            OblivionDiagramRendererKind.NativeSvg,
            resolved,
            resolved.LayoutPolicyIdentity);
    }

    private static OblivionDiagramRenderResult Failure(
        OblivionDiagramRenderRequest request,
        string code,
        string message,
        string? cacheKey = null,
        OblivionDiagramProvenance? provenance = null)
    {
        return new OblivionDiagramRenderResult(
            false,
            RendererId,
            RendererVersion,
            provenance?.SourceHash ?? string.Empty,
            null,
            null,
            [Diagnostic(request, code, message)],
            cacheKey,
            false,
            provenance,
            OblivionDiagramRendererKind.NativeSvg,
            null,
            null);
    }

    private static OblivionCardDiagnostic Diagnostic(
        OblivionDiagramRenderRequest request,
        string code,
        string message)
    {
        return new OblivionCardDiagnostic(
            code,
            OblivionDiagnosticSeverity.Warning,
            message,
            request.SourceReference);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record NativeCacheMetadata(
        int Format,
        OblivionNativeDerivedArtifactKey Key,
        OblivionDiagramProvenance Provenance,
        OblivionResolvedDiagram ResolvedDiagram);
}

public sealed class OblivionFallbackDiagramRenderer : IOblivionDiagramRenderer
{
    private readonly IOblivionDiagramRenderer _preferred;
    private readonly IOblivionDiagramRenderer _fallback;

    public OblivionFallbackDiagramRenderer(
        IOblivionDiagramRenderer preferred,
        IOblivionDiagramRenderer fallback)
    {
        _preferred = preferred ?? throw new ArgumentNullException(nameof(preferred));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
    {
        OblivionDiagramRenderResult preferred = _preferred.Render(request);
        if (preferred.Succeeded)
        {
            return preferred;
        }

        OblivionDiagramRenderResult fallback = _fallback.Render(request);
        if (!fallback.Succeeded)
        {
            return fallback with
            {
                Diagnostics = preferred.Diagnostics.Concat(fallback.Diagnostics).ToArray(),
            };
        }

        OblivionCardDiagnostic diagnostic = new(
            "OBLIVION-NATIVE-FALLBACK-MERMAID",
            OblivionDiagnosticSeverity.Warning,
            "Native SVG realization failed; the displayed artifact was produced by Mermaid.",
            request.SourceReference);
        return fallback with
        {
            Diagnostics = preferred.Diagnostics.Concat([diagnostic]).Concat(fallback.Diagnostics).ToArray(),
        };
    }
}
