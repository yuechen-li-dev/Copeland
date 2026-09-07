using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Resolved2D;
using Aurelian.Rendering.Raster;
using Oblivion.Model;

namespace Oblivion.App;

public sealed record OblivionHeadlessCardRenderResult(
    bool Succeeded,
    string CardId,
    string Kind,
    string? OutputPath,
    int Width,
    int Height,
    string? Sha256,
    int RenderedItems,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed class OblivionHeadlessCardRenderer
{
    private static readonly Resolved2DRgbaColor Background = new(15, 23, 42, 255);
    private static readonly Resolved2DRgbaColor Surface = new(30, 41, 59, 255);
    private static readonly Resolved2DRgbaColor SurfaceAlt = new(51, 65, 85, 255);
    private static readonly Resolved2DRgbaColor Border = new(100, 116, 139, 255);
    private static readonly Resolved2DRgbaColor Foreground = new(241, 245, 249, 255);
    private static readonly Resolved2DRgbaColor Muted = new(203, 213, 225, 255);
    private static readonly Resolved2DRgbaColor Accent = new(56, 189, 248, 255);

    public OblivionHeadlessCardRenderResult Render(
        OblivionWorkspaceControl control,
        string workspaceRoot,
        string cardId,
        string outputPath,
        int width,
        int height,
        int offset = 0,
        int limit = 100)
    {
        if (width is < 320 or > 4096 || height is < 220 or > 4096)
        {
            return Failure(
                cardId,
                outputPath,
                width,
                height,
                "OBLIVION-HEADLESS-SIZE-INVALID",
                "Width must be 320-4096 and height must be 220-4096.");
        }

        OblivionControlResult<OblivionCardReadResult> read = control.ReadCard(
            workspaceRoot,
            cardId,
            "full",
            offset,
            limit);
        if (!read.Succeeded || read.Value is null)
        {
            return new(false, cardId, "unknown", null, width, height, null, 0, read.Diagnostics);
        }

        OblivionCardReadResult card = read.Value;
        List<Resolved2DOperation> operations =
        [
            new FillRectangleOperation("background", new Resolved2DRectangle(0, 0, width, height), Background),
            new FillRectangleOperation("header", new Resolved2DRectangle(20, 18, width - 40, 66), Surface),
            new StrokeRectangleOperation("header.border", new Resolved2DRectangle(20, 18, width - 40, 66), Border, 1),
            Text("title", 38, 31, width - 76, 26, Normalize(card.CardId), Foreground, Resolved2DTextSize.Heading),
            Text("kind", 38, 60, width - 76, 16, Normalize(card.Kind + " | " + card.Summary), Muted, Resolved2DTextSize.Small),
        ];

        int renderedItems = card.Kind switch
        {
            "table" => RenderTable(card, operations, width, height),
            "diagram" => RenderDiagram(workspaceRoot, cardId, operations, width, height),
            _ => RenderDocument(card, operations, width, height),
        };

        string? temporaryPath = null;
        try
        {
            Resolved2DPlan plan = new(new Resolved2DViewport(width, height), operations);
            RasterFrame frame = new AurelianCpuRasterRenderer().Render(plan);
            string fullPath = Path.GetFullPath(outputPath);
            temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            HeadlessPngWriter.Write(temporaryPath, width, height, frame.Surface.CopyRgba8());
            File.Move(temporaryPath, fullPath, overwrite: true);
            string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fullPath)));
            return new(true, cardId, card.Kind, fullPath, width, height, hash, renderedItems, read.Diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Preserve the render failure diagnostic; the uncommitted temp is identifiable.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the render failure diagnostic; the uncommitted temp is identifiable.
                }
            }
            return Failure(
                cardId,
                outputPath,
                width,
                height,
                "OBLIVION-HEADLESS-RENDER-FAILED",
                $"Headless render failed without committing output: {exception.Message}");
        }
    }

    private static int RenderTable(
        OblivionCardReadResult card,
        List<Resolved2DOperation> operations,
        int width,
        int height)
    {
        IReadOnlyList<OblivionTableReadColumn> columns = card.Columns ?? [];
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = card.Rows ?? [];
        if (columns.Count == 0)
        {
            return 0;
        }

        double left = 20;
        double top = 102;
        double availableWidth = width - 40;
        double columnWidth = availableWidth / columns.Count;
        double rowHeight = 38;
        int visibleRows = Math.Min(rows.Count, Math.Max(0, (int)((height - top - rowHeight - 18) / rowHeight)));
        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            double x = left + (columnIndex * columnWidth);
            operations.Add(new FillRectangleOperation(
                $"table.header.{columnIndex}",
                new Resolved2DRectangle(x, top, columnWidth, rowHeight),
                SurfaceAlt));
            operations.Add(Text(
                $"table.header.text.{columnIndex}",
                x + 8,
                top + 9,
                columnWidth - 16,
                20,
                Normalize(columns[columnIndex].Name),
                Foreground,
                Resolved2DTextSize.Small));
        }

        for (int rowIndex = 0; rowIndex < visibleRows; rowIndex++)
        {
            double y = top + rowHeight + (rowIndex * rowHeight);
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                double x = left + (columnIndex * columnWidth);
                operations.Add(new FillRectangleOperation(
                    $"table.cell.{rowIndex}.{columnIndex}",
                    new Resolved2DRectangle(x, y, columnWidth, rowHeight),
                    rowIndex % 2 == 0 ? Surface : Background));
                rows[rowIndex].TryGetValue(columns[columnIndex].Name, out object? value);
                operations.Add(Text(
                    $"table.cell.text.{rowIndex}.{columnIndex}",
                    x + 8,
                    y + 9,
                    columnWidth - 16,
                    20,
                    Normalize(DisplayValue(value)),
                    Muted,
                    Resolved2DTextSize.Small));
            }
        }

        return visibleRows;
    }

    private static int RenderDocument(
        OblivionCardReadResult card,
        List<Resolved2DOperation> operations,
        int width,
        int height)
    {
        string text = Normalize(card.Text ?? card.Summary);
        int charactersPerLine = Math.Max(20, (width - 88) / 8);
        IReadOnlyList<string> lines = Wrap(text, charactersPerLine);
        int visibleLines = Math.Min(lines.Count, Math.Max(1, (height - 126) / 24));
        operations.Add(new FillRectangleOperation(
            "document.surface",
            new Resolved2DRectangle(20, 102, width - 40, height - 120),
            Surface));
        for (int index = 0; index < visibleLines; index++)
        {
            operations.Add(Text(
                $"document.line.{index}",
                42,
                122 + (index * 24),
                width - 84,
                20,
                lines[index],
                Muted,
                Resolved2DTextSize.Medium));
        }
        return visibleLines;
    }

    private static int RenderDiagram(
        string workspaceRoot,
        string cardId,
        List<Resolved2DOperation> operations,
        int width,
        int height)
    {
        OblivionWorkspaceSessionOpenResult open = new OblivionApplication().OpenWorkspace(workspaceRoot);
        OblivionCard? card = open.Session?.Workspace.Pages
            .SelectMany(page => page.Cards)
            .FirstOrDefault(candidate => candidate.Id.Value == cardId);
        if (card is null)
        {
            return 0;
        }

        OblivionDiagramSemanticProjectionResult semantic = new OblivionDiagramCardRealizer()
            .ProjectSemanticDiagram(card, workspaceRoot);
        if (!semantic.Succeeded || semantic.Diagram is null)
        {
            return 0;
        }

        OblivionResolvedDiagram diagram = OblivionNativeDiagramLayout.Resolve(semantic.Diagram);
        double contentX = 28;
        double contentY = 104;
        double contentWidth = width - 56;
        double contentHeight = height - 126;
        double scale = Math.Min(contentWidth / diagram.Width, contentHeight / diagram.Height);
        double offsetX = contentX + ((contentWidth - (diagram.Width * scale)) / 2);
        double offsetY = contentY + ((contentHeight - (diagram.Height * scale)) / 2);
        Dictionary<string, Resolved2DRectangle> nodeBounds = diagram.Nodes.ToDictionary(
            node => node.Id,
            node => new Resolved2DRectangle(
                offsetX + (node.X * scale),
                offsetY + (node.Y * scale),
                Math.Max(48, node.Width * scale),
                Math.Max(28, node.Height * scale)),
            StringComparer.Ordinal);
        var labelBounds = new List<Resolved2DRectangle>();

        foreach (OblivionResolvedDiagramEdge edge in diagram.Edges)
        {
            for (int index = 1; index < edge.Route.Count; index++)
            {
                OblivionNativeDiagramPoint from = edge.Route[index - 1];
                OblivionNativeDiagramPoint to = edge.Route[index];
                double x = offsetX + (Math.Min(from.X, to.X) * scale);
                double y = offsetY + (Math.Min(from.Y, to.Y) * scale);
                double segmentWidth = Math.Max(2, Math.Abs(to.X - from.X) * scale);
                double segmentHeight = Math.Max(2, Math.Abs(to.Y - from.Y) * scale);
                operations.Add(new FillRectangleOperation(
                    $"diagram.edge.{edge.Id}.{index}",
                    new Resolved2DRectangle(x, y, segmentWidth, segmentHeight),
                    Border));
            }
            if (edge.Route.Count >= 2)
            {
                OblivionNativeDiagramPoint before = edge.Route[^2];
                OblivionNativeDiagramPoint end = edge.Route[^1];
                string arrow = Math.Abs(end.X - before.X) >= Math.Abs(end.Y - before.Y)
                    ? end.X >= before.X ? ">" : "<"
                    : end.Y >= before.Y ? "v" : "^";
                operations.Add(Text(
                    $"diagram.edge.arrow.{edge.Id}",
                    offsetX + (end.X * scale) - 8,
                    offsetY + (end.Y * scale) - 8,
                    16,
                    16,
                    arrow,
                    Accent,
                    Resolved2DTextSize.Small,
                    Resolved2DTextAlignX.Center));
            }
            if (!string.IsNullOrWhiteSpace(edge.DisplayLabel))
            {
                Resolved2DRectangle label = PlaceLabel(
                    offsetX + (edge.LabelAnchor.X * scale),
                    offsetY + (edge.LabelAnchor.Y * scale),
                    Math.Min(240, Math.Max(100, contentWidth / 3)),
                    nodeBounds.Values,
                    labelBounds,
                    contentY + contentHeight);
                labelBounds.Add(label);
                operations.Add(new FillRectangleOperation(
                    $"diagram.edge.label.background.{edge.Id}",
                    label,
                    Background));
                operations.Add(Text(
                    $"diagram.edge.label.{edge.Id}",
                    label.X + 3,
                    label.Y + 2,
                    label.Width - 6,
                    label.Height - 4,
                    Normalize(edge.DisplayLabel),
                    Muted,
                    Resolved2DTextSize.Small));
            }
        }

        foreach (OblivionResolvedDiagramNode node in diagram.Nodes)
        {
            Resolved2DRectangle bounds = nodeBounds[node.Id];
            operations.Add(new FillRectangleOperation("diagram.node." + node.Id, bounds, SurfaceAlt));
            operations.Add(new StrokeRectangleOperation("diagram.node.border." + node.Id, bounds, Accent, 2));
            operations.Add(Text(
                "diagram.node.text." + node.Id,
                bounds.X + 6,
                bounds.Y + 7,
                bounds.Width - 12,
                bounds.Height - 10,
                Normalize(node.Label),
                Foreground,
                Resolved2DTextSize.Small,
                Resolved2DTextAlignX.Center));
        }
        return diagram.Nodes.Count + diagram.Edges.Count;
    }

    private static Resolved2DRectangle PlaceLabel(
        double x,
        double y,
        double width,
        IEnumerable<Resolved2DRectangle> nodeBounds,
        IReadOnlyList<Resolved2DRectangle> existingLabels,
        double contentBottom)
    {
        var candidate = new Resolved2DRectangle(x, y, width, 20);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            bool collision = nodeBounds.Any(bounds => Intersects(candidate, bounds))
                || existingLabels.Any(bounds => Intersects(candidate, bounds));
            if (!collision)
            {
                return candidate;
            }
            double nextY = candidate.Y + 22;
            candidate = new Resolved2DRectangle(
                candidate.X + (attempt % 2 == 0 ? 12 : -12),
                nextY + candidate.Height <= contentBottom ? nextY : Math.Max(104, y - ((attempt + 1) * 22)),
                candidate.Width,
                candidate.Height);
        }
        return candidate;
    }

    private static bool Intersects(Resolved2DRectangle left, Resolved2DRectangle right)
    {
        return left.X < right.X + right.Width
            && left.X + left.Width > right.X
            && left.Y < right.Y + right.Height
            && left.Y + left.Height > right.Y;
    }

    private static string DisplayValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            bool boolean => boolean ? "true" : "false",
            _ when value is System.Collections.IEnumerable => JsonSerializer.Serialize(value),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static PositionedTextOperation Text(
        string id,
        double x,
        double y,
        double width,
        double height,
        string text,
        Resolved2DRgbaColor color,
        Resolved2DTextSize size,
        Resolved2DTextAlignX align = Resolved2DTextAlignX.Left)
    {
        return new PositionedTextOperation(
            id,
            new Resolved2DRectangle(x, y, Math.Max(1, width), Math.Max(1, height)),
            text,
            color,
            size: size,
            alignX: align,
            alignY: Resolved2DTextAlignY.Top);
    }

    private static IReadOnlyList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string remaining = paragraph.Trim();
            while (remaining.Length > width)
            {
                int split = remaining.LastIndexOf(' ', width);
                split = split < 1 ? width : split;
                lines.Add(remaining[..split].TrimEnd());
                remaining = remaining[split..].TrimStart();
            }
            lines.Add(remaining);
        }
        return lines;
    }

    private static string Normalize(string value)
    {
        return value
            .Replace("→", "->", StringComparison.Ordinal)
            .Replace("·", "|", StringComparison.Ordinal)
            .Replace("—", "-", StringComparison.Ordinal)
            .Replace("…", "...", StringComparison.Ordinal);
    }

    private static OblivionHeadlessCardRenderResult Failure(
        string cardId,
        string outputPath,
        int width,
        int height,
        string code,
        string message)
    {
        return new OblivionHeadlessCardRenderResult(
            false,
            cardId,
            "unknown",
            null,
            width,
            height,
            null,
            0,
            [new OblivionControlDiagnostic(
                code, "error", message, null, null, cardId, outputPath, null, null)]);
    }

    private static class HeadlessPngWriter
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

        public static void Write(string path, int width, int height, byte[] rgba)
        {
            using FileStream stream = File.Create(path);
            stream.Write(Signature);
            WriteChunk(stream, "IHDR"u8, Header(width, height));
            WriteChunk(stream, "IDAT"u8, Compress(width, height, rgba));
            WriteChunk(stream, "IEND"u8, []);
        }

        private static byte[] Header(int width, int height)
        {
            var data = new byte[13];
            WriteBigEndian(data, 0, width);
            WriteBigEndian(data, 4, height);
            data[8] = 8;
            data[9] = 6;
            return data;
        }

        private static byte[] Compress(int width, int height, byte[] rgba)
        {
            int rowBytes = width * 4;
            var raw = new byte[(rowBytes + 1) * height];
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(rgba, row * rowBytes, raw, row * (rowBytes + 1) + 1, rowBytes);
            }
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }
            return output.ToArray();
        }

        private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, byte[] data)
        {
            WriteBigEndian(stream, data.Length);
            stream.Write(type);
            stream.Write(data);
            uint crc = UpdateCrc(UpdateCrc(0xFFFFFFFFu, type), data);
            WriteBigEndian(stream, unchecked((int)~crc));
        }

        private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
        {
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }
            return crc;
        }

        private static void WriteBigEndian(Stream stream, int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            WriteBigEndian(bytes, 0, value);
            stream.Write(bytes);
        }

        private static void WriteBigEndian(Span<byte> bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
    }
}
