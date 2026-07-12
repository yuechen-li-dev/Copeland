using System.Globalization;
using System.Text;
using Machina.Core.Actions;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Core.Diagnostics;

public static class UiLoweringSnapshotWriter
{
    public static string Write(UiLoweringResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var writer = new StringBuilder();
        WriteRows(writer, result.Rows);
        WriteStyles(writer, result.Styles);
        WriteTextStyles(writer, result.TextStyles);
        WriteSemantics(writer, result.Semantics);
        WriteActions(writer, result.Actions);
        return writer.ToString();
    }

    private static void WriteRows(StringBuilder writer, IReadOnlyList<LayoutRow> rows)
    {
        writer.AppendLine("rows:");

        foreach (var row in rows)
        {
            writer.Append("  ");
            writer.Append(row.Id.Value);
            writer.Append(" parent=");
            writer.Append(row.Parent?.Value ?? "<none>");
            writer.Append(" order=");
            writer.Append(FormatNumber(row.Order));
            writer.Append(" z=");
            writer.Append(FormatNumber(row.Z));
            writer.Append(" frame=");
            writer.Append(FormatFrame(row.Frame));
            writer.Append(" arrange=");
            writer.Append(FormatArrange(row.Arrange));
            writer.Append(" slot=");
            writer.Append(row.Slot ?? "<none>");
            writer.Append(" view=");
            writer.Append(row.View ?? "<none>");
            writer.Append(" layer=");
            writer.Append(row.Layer ?? "<none>");
            writer.Append(" debug=");
            writer.Append(Quote(row.DebugLabel));
            writer.AppendLine();
        }

        writer.AppendLine();
    }

    private static void WriteStyles(
        StringBuilder writer,
        IReadOnlyDictionary<NodeId, UiStyle> styles)
    {
        writer.AppendLine("styles:");

        foreach (var pair in SortByNodeId(styles))
        {
            writer.Append("  ");
            writer.Append(pair.Key.Value);
            writer.Append(" background=");
            writer.Append(FormatColor(pair.Value.Background));
            writer.Append(" foreground=");
            writer.Append(FormatColor(pair.Value.Foreground));
            writer.Append(" padding=");
            writer.Append(FormatNumber(pair.Value.Padding));
            writer.Append(" borderColor=");
            writer.Append(FormatColor(pair.Value.BorderColor));
            writer.Append(" borderThickness=");
            writer.Append(FormatNumber(pair.Value.BorderThickness));
            writer.AppendLine();
        }

        writer.AppendLine();
    }

    private static void WriteTextStyles(
        StringBuilder writer,
        IReadOnlyDictionary<NodeId, TextStyle> textStyles)
    {
        writer.AppendLine("textStyles:");

        foreach (var pair in SortByNodeId(textStyles))
        {
            writer.Append("  ");
            writer.Append(pair.Key.Value);
            writer.Append(" color=");
            writer.Append(FormatColor(pair.Value.Color));
            writer.Append(" size=");
            writer.Append(pair.Value.Size);
            writer.AppendLine();
        }

        writer.AppendLine();
    }

    private static void WriteSemantics(
        StringBuilder writer,
        IReadOnlyDictionary<NodeId, UiSemantics> semantics)
    {
        writer.AppendLine("semantics:");

        foreach (var pair in SortByNodeId(semantics))
        {
            writer.Append("  ");
            writer.Append(pair.Key.Value);
            writer.Append(" role=");
            writer.Append(pair.Value.Role);
            writer.Append(" label=");
            writer.Append(Quote(pair.Value.Label));
            writer.Append(" disabled=");
            writer.Append(FormatBoolean(pair.Value.Disabled));
            writer.Append(" focusable=");
            writer.Append(FormatBoolean(pair.Value.Focusable));
            writer.AppendLine();
        }

        writer.AppendLine();
    }

    private static void WriteActions(
        StringBuilder writer,
        IReadOnlyDictionary<NodeId, UiAction> actions)
    {
        writer.AppendLine("actions:");

        foreach (var pair in SortByNodeId(actions))
        {
            writer.Append("  ");
            writer.Append(pair.Key.Value);
            writer.Append(" => ");
            writer.Append(pair.Value.Name);
            writer.AppendLine();
        }
    }

    private static IEnumerable<KeyValuePair<NodeId, TValue>> SortByNodeId<TValue>(
        IReadOnlyDictionary<NodeId, TValue> values)
    {
        return values.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal);
    }

    private static string FormatFrame(FrameSpec frame)
    {
        return frame switch
        {
            RootFrame => "Root",
            AbsoluteFrame absolute => FormatAbsoluteFrame(absolute),
            AnchorFrame anchor => FormatAnchorFrame(anchor),
            FixedFrame fixedFrame => FormatFixedFrame(fixedFrame),
            FillFrame fill => FormatFillFrame(fill),
            CellFrame cell => FormatCellFrame(cell),
            _ => frame.GetType().Name,
        };
    }

    private static string FormatAbsoluteFrame(AbsoluteFrame frame)
    {
        return $"Absolute(x={FormatNumber(frame.X)},y={FormatNumber(frame.Y)},width={FormatNumber(frame.Width)},height={FormatNumber(frame.Height)})";
    }

    private static string FormatAnchorFrame(AnchorFrame frame)
    {
        return $"Anchor(left={FormatLength(frame.Left)},right={FormatLength(frame.Right)},top={FormatLength(frame.Top)},bottom={FormatLength(frame.Bottom)},width={FormatLength(frame.Width)},height={FormatLength(frame.Height)})";
    }

    private static string FormatFixedFrame(FixedFrame frame)
    {
        return $"Fixed(width={FormatNumber(frame.Width)},height={FormatNumber(frame.Height)})";
    }

    private static string FormatFillFrame(FillFrame frame)
    {
        return $"Fill(weight={FormatNumber(frame.Weight)},cross={FormatOptionalNumber(frame.Cross)},crossFill={FormatBoolean(frame.CrossFill)})";
    }

    private static string FormatCellFrame(CellFrame frame)
    {
        return $"Cell(column={FormatNumber(frame.Column)},row={FormatNumber(frame.Row)},columnSpan={FormatNumber(frame.ColumnSpan)},rowSpan={FormatNumber(frame.RowSpan)})";
    }

    private static string FormatArrange(ArrangeSpec? arrange)
    {
        return arrange switch
        {
            null => "<none>",
            StackArrange stack => FormatStackArrange(stack),
            GridArrange grid => FormatGridArrange(grid),
            _ => arrange.GetType().Name,
        };
    }

    private static string FormatStackArrange(StackArrange arrange)
    {
        return $"Stack(axis={arrange.Axis},gap={FormatNumber(arrange.Gap)},padding={FormatEdgeInsets(arrange.Padding)},justify={arrange.Justify},align={arrange.Align})";
    }

    private static string FormatGridArrange(GridArrange arrange)
    {
        return $"Grid(columns=[{FormatGridTracks(arrange.Columns)}],rows=[{FormatGridTracks(arrange.Rows)}],columnGap={FormatNumber(arrange.ColumnGap)},rowGap={FormatNumber(arrange.RowGap)},padding={FormatEdgeInsets(arrange.Padding)})";
    }

    private static string FormatGridTracks(IReadOnlyList<GridTrack> tracks)
    {
        return string.Join(",", tracks.Select(FormatGridTrack));
    }

    private static string FormatGridTrack(GridTrack track)
    {
        return track switch
        {
            FixedGridTrack fixedTrack => $"Fixed({FormatNumber(fixedTrack.Size)})",
            FillGridTrack fillTrack => $"Fill({FormatNumber(fillTrack.Weight)})",
            _ => track.GetType().Name,
        };
    }

    private static string FormatEdgeInsets(EdgeInsets? insets)
    {
        if (insets is not { } value)
        {
            return "<none>";
        }

        return $"{FormatNumber(value.Top)},{FormatNumber(value.Right)},{FormatNumber(value.Bottom)},{FormatNumber(value.Left)}";
    }

    private static string FormatLength(UiLength? length)
    {
        if (length is not { } value)
        {
            return "<none>";
        }

        var unit = value.Unit == UiLengthUnit.Px ? "px" : "ui";
        return FormatNumber(value.Value) + unit;
    }

    private static string FormatOptionalNumber(double? value)
    {
        return value is { } number ? FormatNumber(number) : "<none>";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.################", CultureInfo.InvariantCulture);
    }

    private static string FormatNumber(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatBoolean(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatColor(ColorToken? color)
    {
        return color is { } value
            ? $"#{value.Rgba:X8}"
            : "<none>";
    }

    private static string Quote(string? value)
    {
        if (value is null)
        {
            return "<none>";
        }

        var builder = new StringBuilder();
        builder.Append('"');

        foreach (var character in value)
        {
            AppendQuotedCharacter(builder, character);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static void AppendQuotedCharacter(StringBuilder builder, char character)
    {
        switch (character)
        {
            case '\\':
                builder.Append("\\\\");
                return;
            case '"':
                builder.Append("\\\"");
                return;
            case '\r':
                builder.Append("\\r");
                return;
            case '\n':
                builder.Append("\\n");
                return;
            case '\t':
                builder.Append("\\t");
                return;
            default:
                builder.Append(character);
                return;
        }
    }
}
