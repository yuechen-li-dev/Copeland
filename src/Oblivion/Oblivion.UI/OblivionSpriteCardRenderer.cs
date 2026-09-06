using System.Globalization;
using System.Text;
using Oblivion.Model;

namespace Oblivion.Product;

public sealed record OblivionSpriteCardRenderOptions(
    string Edge = "top",
    GraphicalConceptPath? Selected = null,
    GraphicalConceptKind? FilterKind = null,
    bool DiagnosticsOnly = false,
    bool ShowPreviews = true,
    bool ShowAuthoringOverlay = true,
    int Width = 1500,
    int CardWidth = 210,
    int CardHeight = 250);

/// <summary>
/// Deterministic renderer-neutral notebook projection. It consumes resolved
/// cards and never performs allocation or asset compilation.
/// </summary>
public static class OblivionSpriteCardRenderer
{
    public static string RenderSvg(
        SpriteCardProjection projection,
        OblivionSpriteCardRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        OblivionSpriteCardRenderOptions render = options ?? new OblivionSpriteCardRenderOptions();
        IReadOnlyList<SpriteCard> cards = projection.Filter(
            render.FilterKind,
            render.Selected,
            render.DiagnosticsOnly)
            .Where(card => card.Kind != GraphicalConceptKind.EdgeSegment || card.Role == render.Edge)
            .ToArray();
        SpriteCard[] edgeCards = cards.Where(card => card.Kind == GraphicalConceptKind.EdgeSegment).ToArray();
        int columns = Math.Max(1, Math.Min(6, render.Width / (render.CardWidth + 18)));
        int rows = (int)Math.Ceiling(cards.Count / (double)columns);
        int height = 282 + Math.Max(1, rows) * (render.CardHeight + 18);
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
            .Append(render.Width)
            .Append("\" height=\"")
            .Append(height)
            .Append("\" viewBox=\"0 0 ")
            .Append(render.Width)
            .Append(' ')
            .Append(height)
            .AppendLine("\">");
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#07111f\"/>");
        Text(svg, 24, 32, 22, "#f8fafc", $"{projection.AssetId} · {projection.PanelId} · {projection.Width}×{projection.Height}", bold: true);
        Text(svg, 24, 58, 13, "#94a3b8", $"source {projection.SourcePath} · v{projection.CompileVersion} · {projection.SourceSha256[..12]}");
        if (render.ShowAuthoringOverlay)
        {
            RenderAuthoringOverlay(svg, projection, render);
        }
        RenderAllocationStrip(svg, projection, edgeCards, render);

        for (int index = 0; index < cards.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            int x = 24 + column * (render.CardWidth + 18);
            int y = 264 + row * (render.CardHeight + 18);
            RenderCard(svg, projection, cards[index], x, y, render);
        }

        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static void RenderAuthoringOverlay(
        StringBuilder svg,
        SpriteCardProjection projection,
        OblivionSpriteCardRenderOptions options)
    {
        const int x = 24;
        const int y = 72;
        int width = options.Width - 48;
        const int height = 92;
        svg.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
            .Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
            .Append("\" rx=\"5\" fill=\"#0b1526\" stroke=\"#334155\"/>");
        Text(svg, x + 8, y + 17, 10, "#94a3b8", "AUTHORING OVERLAY · guides/datums/blockouts erase before runtime");

        SpriteCard[] authoringCards = projection.Cards.Where(card => card.Kind is
            GraphicalConceptKind.Guide or GraphicalConceptKind.Datum or GraphicalConceptKind.Blockout).ToArray();
        for (int cardIndex = 0; cardIndex < authoringCards.Length; cardIndex++)
        {
            SpriteCard card = authoringCards[cardIndex];
            if (card.Resolved?.Bounds is not GraphicalRect bounds)
            {
                continue;
            }

            double left = x + 8 + ((width - 16) * bounds.X / 1024d);
            double top = y + 22 + ((height - 28) * bounds.Y / 1024d);
            double conceptWidth = Math.Max(2, (width - 16) * bounds.Width / 1024d);
            double conceptHeight = Math.Max(2, (height - 28) * bounds.Height / 1024d);
            string color = card.Kind switch
            {
                GraphicalConceptKind.Datum => "#fbbf24",
                GraphicalConceptKind.Guide => "#22d3ee",
                _ => "#c084fc",
            };
            if (card.Kind == GraphicalConceptKind.Datum)
            {
                svg.Append("<line x1=\"").Append(Number(left)).Append("\" y1=\"").Append(Number(top))
                    .Append("\" x2=\"").Append(Number(left + conceptWidth)).Append("\" y2=\"")
                    .Append(Number(top)).Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"2\"/>");
            }
            else
            {
                svg.Append("<rect x=\"").Append(Number(left)).Append("\" y=\"").Append(Number(top))
                    .Append("\" width=\"").Append(Number(conceptWidth)).Append("\" height=\"")
                    .Append(Number(conceptHeight)).Append("\" fill=\"none\" stroke=\"").Append(color)
                    .Append("\" stroke-width=\"1.5\" stroke-dasharray=\"5 3\"/>");
            }
            Text(svg, x + 8, y + 36 + (cardIndex * 14), 9, color, card.ConceptPath.Value);
        }
    }

    private static void RenderAllocationStrip(
        StringBuilder svg,
        SpriteCardProjection projection,
        IReadOnlyList<SpriteCard> cards,
        OblivionSpriteCardRenderOptions options)
    {
        SpriteCardEdgeSummary? summary = projection.EdgeSummaries.SingleOrDefault(item => item.Edge == options.Edge);
        if (summary is null)
        {
            return;
        }

        int x = 24;
        int y = 182;
        int width = options.Width - 48;
        svg.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
            .Append("\" width=\"").Append(width).Append("\" height=\"42\" rx=\"5\" fill=\"#111c2d\" stroke=\"#334155\"/>");
        foreach (SpriteCard card in cards)
        {
            if (card.Resolved?.Offset is not int offset || card.Resolved.Length is not int length || summary.Extent <= 0)
            {
                continue;
            }

            double left = x + width * (offset / (double)summary.Extent);
            double segmentWidth = width * (length / (double)summary.Extent);
            bool selected = options.Selected == card.ConceptPath;
            string fill = card.Authored.Policy == "fixed" ? "#475569" : "#0369a1";
            svg.Append("<rect x=\"").Append(Number(left)).Append("\" y=\"").Append(y)
                .Append("\" width=\"").Append(Number(Math.Max(1, segmentWidth)))
                .Append("\" height=\"42\" fill=\"").Append(fill)
                .Append("\" stroke=\"").Append(selected ? "#fbbf24" : "#0f172a")
                .Append("\" stroke-width=\"").Append(selected ? 3 : 1).Append("\"/>");
            if (segmentWidth >= 60)
            {
                Text(svg, left + 4, y + 17, 10, "#f8fafc", card.ConceptPath.Value.Split('.')[^1]);
                Text(svg, left + 4, y + 33, 10, "#bae6fd", length.ToString(CultureInfo.InvariantCulture));
            }
        }

        Text(
            svg,
            24,
            243,
            12,
            summary.DeficitLength > 0 ? "#fca5a5" : "#94a3b8",
            $"{options.Edge}: extent {summary.Extent} · minimum {summary.MinimumDemand} · used {summary.UsedLength} · unused {summary.UnusedLength} · deficit {summary.DeficitLength} · {summary.Status}");
    }

    private static void RenderCard(
        StringBuilder svg,
        SpriteCardProjection projection,
        SpriteCard card,
        int x,
        int y,
        OblivionSpriteCardRenderOptions options)
    {
        bool selected = options.Selected == card.ConceptPath;
        bool related = options.Selected is null || selected || card.Relationships.Any(relation => relation.Target == options.Selected);
        string opacity = related ? "1" : "0.32";
        svg.Append("<g opacity=\"").Append(opacity).Append("\">");
        svg.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
            .Append("\" width=\"").Append(options.CardWidth).Append("\" height=\"")
            .Append(options.CardHeight).Append("\" rx=\"8\" fill=\"#0f1b2d\" stroke=\"")
            .Append(selected ? "#fbbf24" : card.Diagnostics.Count > 0 ? "#ef4444" : "#334155")
            .Append("\" stroke-width=\"").Append(selected ? 3 : 1).Append("\"/>");
        Text(svg, x + 12, y + 24, 11, "#38bdf8", card.Kind.ToString().ToUpperInvariant(), bold: true);
        IReadOnlyList<string> pathLines = Wrap(card.ConceptPath.Value, 30);
        int cursor = y + 48;
        foreach (string line in pathLines.Take(2))
        {
            Text(svg, x + 12, cursor, 13, "#f8fafc", line, bold: true);
            cursor += 17;
        }

        if (options.ShowPreviews && card.SourceRect is GraphicalRect rect)
        {
            svg.Append("<svg x=\"").Append(x + 12).Append("\" y=\"").Append(cursor + 2)
                .Append("\" width=\"56\" height=\"42\" viewBox=\"").Append(rect.X).Append(' ')
                .Append(rect.Y).Append(' ').Append(rect.Width).Append(' ').Append(rect.Height)
                .Append("\" preserveAspectRatio=\"xMidYMid slice\">")
                .Append("<image href=\"").Append(XmlEscape(new Uri(projection.AtlasImagePath).AbsoluteUri))
                .Append("\" width=\"").Append(projection.AtlasWidth).Append("\" height=\"")
                .Append(projection.AtlasHeight).Append("\"/></svg>");
            svg.Append("<rect x=\"").Append(x + 12).Append("\" y=\"").Append(cursor + 2)
                .Append("\" width=\"56\" height=\"42\" fill=\"none\" stroke=\"#60a5fa\"/>");
            cursor += 54;
        }

        Text(svg, x + 12, cursor, 11, "#cbd5e1", $"role {card.Role}");
        cursor += 17;
        Text(svg, x + 12, cursor, 11, "#cbd5e1", card.Authored.Policy);
        cursor += 17;
        if (card.Authored.MinimumLength is int minimum)
        {
            Text(svg, x + 12, cursor, 11, "#cbd5e1", $"min {minimum} · weight {card.Authored.Weight}");
            cursor += 17;
        }

        if (card.Authored.Sampling is not null)
        {
            Text(svg, x + 12, cursor, 11, "#cbd5e1", $"sampling {card.Authored.Sampling}");
            cursor += 17;
        }

        if (card.Resolved?.Offset is int offset && card.Resolved.Length is int length)
        {
            Text(svg, x + 12, cursor, 11, "#a7f3d0", $"resolved {offset}+{length}");
            cursor += 17;
        }
        else if (card.Resolved?.Bounds is GraphicalRect bounds)
        {
            Text(svg, x + 12, cursor, 11, "#a7f3d0", $"bounds {bounds.X},{bounds.Y} {bounds.Width}×{bounds.Height}");
            cursor += 17;
        }

        Text(svg, x + 12, y + options.CardHeight - 32, 10, "#94a3b8", $"source L{card.Source.Line}:{card.Source.Column}");
        Text(
            svg,
            x + 12,
            y + options.CardHeight - 14,
            10,
            card.Runtime.SurvivesLowering ? "#a7f3d0" : "#f0abfc",
            card.Runtime.SurvivesLowering ? "runtime: survives" : "runtime: erased");
        svg.AppendLine("</g>");

    }

    private static IReadOnlyList<string> Wrap(string value, int width)
    {
        var result = new List<string>();
        for (int start = 0; start < value.Length; start += width)
        {
            result.Add(value.Substring(start, Math.Min(width, value.Length - start)));
        }

        return result;
    }

    private static void Text(
        StringBuilder svg,
        double x,
        double y,
        int size,
        string fill,
        string value,
        bool bold = false)
    {
        svg.Append("<text x=\"").Append(Number(x)).Append("\" y=\"").Append(Number(y))
            .Append("\" font-family=\"Segoe UI, sans-serif\" font-size=\"").Append(size)
            .Append("\" fill=\"").Append(fill).Append('"');
        if (bold)
        {
            svg.Append(" font-weight=\"600\"");
        }

        svg.Append('>').Append(XmlEscape(value)).AppendLine("</text>");
    }

    private static string XmlEscape(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }

    private static string Number(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
