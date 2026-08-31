using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Copeland.TS.Templates;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Oblivion.App;

namespace Oblivion.Standalone;

public sealed record M20bNativeNodeGeometry(
    string Id,
    string Label,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record M20bNativeDiagramGeometry(
    double Width,
    double Height,
    IReadOnlyList<M20bNativeNodeGeometry> Nodes,
    IReadOnlyList<DiagramEdge> Edges,
    IReadOnlyList<M20bNativeEdgeLabelGeometry> EdgeLabels);

public sealed record M20bNativeEdgeLabelGeometry(
    string EventName,
    string DestinationLabel,
    double X,
    double Y);

public static class M20bNativeDiagramLayout
{
    private const double NodeWidth = 190;
    private const double NodeHeight = 54;

    private static readonly IReadOnlyDictionary<string, (double X, double Y)> Placements =
        new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal)
        {
            ["WorkspaceIntake"] = (40, 44),
            ["Parsing"] = (240, 44),
            ["Binding"] = (440, 44),
            ["Lowering"] = (640, 44),
            ["BackendSelection"] = (840, 44),
            ["Emitting"] = (1040, 44),
            ["ArtifactValidation"] = (1240, 44),
            ["CacheQualification"] = (1440, 44),
            ["CardProjection"] = (1640, 44),
            ["CardRealization"] = (1840, 44),
            ["HumanReview"] = (2040, 44),
            ["Accepted"] = (2240, 44),
            ["Diagnostics"] = (1640, 246),
            ["SourceRepair"] = (1240, 404),
            ["RendererRecovery"] = (1040, 404),
            ["Rejected"] = (2040, 404),
        };

    private static readonly IReadOnlyDictionary<string, (double X, double Y)> ReadingTaskLabelPlacements =
        new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal)
        {
            ["SourceMissing"] = (46, 142),
            ["ParseFailed"] = (250, 168),
            ["BindFailed"] = (450, 194),
            ["LowerFailed"] = (650, 220),
            ["RendererUnavailable"] = (836, 326),
            ["EmitFailed"] = (1036, 350),
            ["ArtifactCorrupt"] = (1236, 326),
            ["CacheStale"] = (1390, 142),
            ["ProjectionFailed"] = (1636, 168),
            ["HostFailed"] = (1836, 194),
            ["RevisionRequested"] = (1836, 326),
        };

    public static M20bNativeDiagramGeometry Resolve(Diagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        List<M20bNativeNodeGeometry> nodes = [];
        foreach (DiagramNode node in diagram.Nodes)
        {
            if (!Placements.TryGetValue(node.Label, out (double X, double Y) placement))
            {
                throw new InvalidOperationException(
                    $"M20b explicit layout has no placement for Diagram node '{node.Id}'.");
            }

            nodes.Add(new M20bNativeNodeGeometry(
                node.Id,
                node.Label,
                placement.X,
                placement.Y,
                NodeWidth,
                NodeHeight));
        }

        List<M20bNativeEdgeLabelGeometry> edgeLabels = [];
        foreach (DiagramEdge edge in diagram.Edges)
        {
            string eventName = EventName(edge.Label);
            if (!ReadingTaskLabelPlacements.TryGetValue(eventName, out (double X, double Y) placement))
            {
                continue;
            }

            string destination = nodes.Single(node => node.Id == edge.To).Label;
            edgeLabels.Add(new M20bNativeEdgeLabelGeometry(
                eventName,
                destination,
                placement.X,
                placement.Y));
        }

        return new M20bNativeDiagramGeometry(2460, 510, nodes, diagram.Edges, edgeLabels);
    }

    private static string EventName(string? label)
    {
        string value = label ?? string.Empty;
        int separator = value.IndexOfAny([' ', '[', '(']);
        return separator < 0 ? value : value[..separator];
    }
}

public sealed class M20bNativeDiagramRenderer : IOblivionDiagramRenderer
{
    private readonly M20bNativeDiagramGeometry _geometry;

    public M20bNativeDiagramRenderer(Diagram diagram)
    {
        _geometry = M20bNativeDiagramLayout.Resolve(diagram);
    }

    public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
    {
        string outputDirectory = Path.GetFullPath(Path.Combine("artifacts", "m20b"));
        Directory.CreateDirectory(outputDirectory);
        string appearance = request.Appearance.ToString().ToLowerInvariant();
        string pngPath = Path.Combine(outputDirectory, $"native-diagram-{appearance}.png");
        foreach (OblivionResolvedAppearance candidate in Enum.GetValues<OblivionResolvedAppearance>())
        {
            string candidateName = candidate.ToString().ToLowerInvariant();
            string svgPath = Path.Combine(outputDirectory, $"native-diagram-{candidateName}.svg");
            string candidatePngPath = Path.Combine(outputDirectory, $"native-diagram-{candidateName}.png");
            File.WriteAllText(
                svgPath,
                M20bNativeDiagramSvgEmitter.Emit(_geometry, candidate),
                new UTF8Encoding(false));
            RasterFrame frame = M20bNativeDiagramRasterizer.Render(_geometry, candidate);
            M20bPngWriter.Write(candidatePngPath, frame);
        }

        return new OblivionDiagramRenderResult(
            true,
            "m20b-native-svg-experiment",
            "1",
            OblivionMermaidHashing.ComputeSourceHash(request.Source),
            pngPath,
            "image/png",
            []);
    }
}

public static class M20bNativeDiagramSvgEmitter
{
    public static string Emit(
        M20bNativeDiagramGeometry geometry,
        OblivionResolvedAppearance appearance)
    {
        bool dark = appearance == OblivionResolvedAppearance.Dark;
        string background = dark ? "#0f172a" : "#ffffff";
        string nodeFill = dark ? "#111827" : "#f8fafc";
        string nodeStroke = dark ? "#38bdf8" : "#2563eb";
        string foreground = dark ? "#e2e8f0" : "#18181b";
        string edge = dark ? "#94a3b8" : "#475569";
        StringBuilder svg = new();
        svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{geometry.Width}\" height=\"{geometry.Height}\" viewBox=\"0 0 {geometry.Width} {geometry.Height}\">");
        svg.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{background}\"/>");
        svg.AppendLine("  <g id=\"edges\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");
        foreach (DiagramEdge diagramEdge in geometry.Edges)
        {
            M20bNativeNodeGeometry from = geometry.Nodes.Single(node => node.Id == diagramEdge.From);
            M20bNativeNodeGeometry to = geometry.Nodes.Single(node => node.Id == diagramEdge.To);
            (double StartX, double StartY, double MiddleX, double EndX, double EndY) route = Route(from, to);
            svg.AppendLine(
                $"    <path data-semantic-identity=\"{Escape(diagramEdge.SemanticIdentity ?? string.Empty)}\" d=\"M {route.StartX} {route.StartY} H {route.MiddleX} V {route.EndY} H {route.EndX}\" stroke=\"{edge}\" stroke-width=\"2\"><title>{Escape(diagramEdge.Label ?? string.Empty)}</title></path>");
        }
        svg.AppendLine("  </g>");
        svg.AppendLine("  <g id=\"reading-task-labels\">");
        foreach (M20bNativeEdgeLabelGeometry label in geometry.EdgeLabels)
        {
            svg.AppendLine($"    <text x=\"{label.X}\" y=\"{label.Y}\" font-family=\"Segoe UI, sans-serif\" font-size=\"13\" fill=\"{foreground}\">{Escape(label.EventName)} → {Escape(label.DestinationLabel)}</text>");
        }
        svg.AppendLine("  </g>");
        svg.AppendLine("  <g id=\"nodes\">");
        foreach (M20bNativeNodeGeometry node in geometry.Nodes)
        {
            svg.AppendLine($"    <g id=\"{Escape(node.Id)}\" data-diagram-node-id=\"{Escape(node.Id)}\">");
            svg.AppendLine($"      <rect x=\"{node.X}\" y=\"{node.Y}\" width=\"{node.Width}\" height=\"{node.Height}\" rx=\"8\" fill=\"{nodeFill}\" stroke=\"{nodeStroke}\" stroke-width=\"2\"/>");
            svg.AppendLine($"      <text x=\"{node.X + (node.Width / 2)}\" y=\"{node.Y + 33}\" text-anchor=\"middle\" font-family=\"Segoe UI, sans-serif\" font-size=\"16\" fill=\"{foreground}\">{Escape(node.Label)}</text>");
            svg.AppendLine("    </g>");
        }
        svg.AppendLine("  </g>");
        svg.AppendLine("</svg>");
        return svg.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    internal static (double StartX, double StartY, double MiddleX, double EndX, double EndY) Route(
        M20bNativeNodeGeometry from,
        M20bNativeNodeGeometry to)
    {
        double startX = from.X + from.Width;
        double startY = from.Y + (from.Height / 2);
        double endX = to.X;
        double endY = to.Y + (to.Height / 2);
        double middleX = startX <= endX
            ? startX + ((endX - startX) / 2)
            : Math.Max(20, Math.Min(startX, endX) - 24);
        return (startX, startY, middleX, endX, endY);
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}

internal static class M20bNativeDiagramRasterizer
{
    public static RasterFrame Render(
        M20bNativeDiagramGeometry geometry,
        OblivionResolvedAppearance appearance)
    {
        bool dark = appearance == OblivionResolvedAppearance.Dark;
        ColorToken background = ColorToken.Hex(dark ? 0x0F172AFFu : 0xFFFFFFFFu);
        ColorToken nodeFill = ColorToken.Hex(dark ? 0x111827FFu : 0xF8FAFCFFu);
        ColorToken nodeStroke = ColorToken.Hex(dark ? 0x38BDF8FFu : 0x2563EBFFu);
        ColorToken foreground = ColorToken.Hex(dark ? 0xE2E8F0FFu : 0x18181BFFu);
        ColorToken edge = ColorToken.Hex(dark ? 0x94A3B8FFu : 0x475569FFu);
        List<UiNode> layers = [];

        foreach (DiagramEdge diagramEdge in geometry.Edges)
        {
            M20bNativeNodeGeometry from = geometry.Nodes.Single(node => node.Id == diagramEdge.From);
            M20bNativeNodeGeometry to = geometry.Nodes.Single(node => node.Id == diagramEdge.To);
            var route = M20bNativeDiagramSvgEmitter.Route(from, to);
            AddLine(layers, route.StartX, route.StartY, route.MiddleX, route.StartY, edge);
            AddLine(layers, route.MiddleX, route.StartY, route.MiddleX, route.EndY, edge);
            AddLine(layers, route.MiddleX, route.EndY, route.EndX, route.EndY, edge);
        }

        foreach (M20bNativeNodeGeometry node in geometry.Nodes)
        {
            UiNode nodeVisual = UI.Rect(
                child: UI.Text(
                    node.Label,
                    color: foreground,
                    size: TextSize.Md,
                    alignX: TextAlignX.Center,
                    alignY: TextAlignY.Center),
                id: "m20b.native.node." + node.Id,
                width: node.Width,
                height: node.Height,
                color: nodeFill,
                borderColor: nodeStroke,
                borderThickness: 2);
            layers.Add(UI.At(
                nodeVisual,
                id: "m20b.native.placement." + node.Id,
                x: node.X,
                y: node.Y,
                width: node.Width,
                height: node.Height));
        }

        foreach (M20bNativeEdgeLabelGeometry label in geometry.EdgeLabels)
        {
            UiNode labelNode = UI.Text(
                $"{label.EventName} -> {label.DestinationLabel}",
                color: foreground,
                size: TextSize.Sm,
                alignX: TextAlignX.Left,
                alignY: TextAlignY.Top);
            layers.Add(UI.At(
                labelNode,
                x: label.X,
                y: label.Y,
                width: 220,
                height: 20));
        }

        UiNode document = UI.Rect(
            child: UI.Layer(id: "m20b.native.layers", children: layers),
            id: "m20b.native.document",
            width: geometry.Width,
            height: geometry.Height,
            color: background);
        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(
            document,
            (int)geometry.Width,
            (int)geometry.Height);
        return new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(prepared.PresentationFrame));
    }

    private static void AddLine(
        List<UiNode> layers,
        double x1,
        double y1,
        double x2,
        double y2,
        ColorToken color)
    {
        double x = Math.Min(x1, x2);
        double y = Math.Min(y1, y2);
        double width = Math.Max(2, Math.Abs(x2 - x1));
        double height = Math.Max(2, Math.Abs(y2 - y1));
        layers.Add(UI.At(
            UI.Rect(width: width, height: height, color: color),
            x: x,
            y: y,
            width: width,
            height: height));
    }
}

internal static class M20bPngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(string path, RasterFrame frame)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream stream = File.Create(path);
        stream.Write(Signature);
        byte[] header = new byte[13];
        int width = frame.Surface.Width;
        int height = frame.Surface.Height;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(stream, "IHDR", header);

        byte[] scanlines = new byte[(width * 4 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int row = y * ((width * 4) + 1);
            for (int x = 0; x < width; x++)
            {
                var pixel = frame.Surface.GetPixel(x, y);
                int offset = row + 1 + (x * 4);
                scanlines[offset] = pixel.R;
                scanlines[offset + 1] = pixel.G;
                scanlines[offset + 2] = pixel.B;
                scanlines[offset + 3] = pixel.A;
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }
        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte value in typeBytes.Concat(data))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320;
            }
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        stream.Write(length);
    }
}
