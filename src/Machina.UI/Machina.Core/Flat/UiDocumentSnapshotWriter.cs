using System.Globalization;
using System.Text;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;

namespace Machina.Core.Flat;

public static class UiDocumentSnapshotWriter
{
    public static string Write(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        builder.AppendLine("document:");
        builder.AppendLine("  rows:");

        foreach (var row in document.Rows)
        {
            var parts = new List<string>
            {
                row.Id.Value,
                $"parent={FormatParent(row.Parent)}",
                $"order={row.Order.ToString(CultureInfo.InvariantCulture)}",
                $"frame={FormatFrame(row.Frame)}"
            };

            if (row.Arrange is not null)
            {
                parts.Add($"arrange={FormatArrange(row.Arrange)}");
            }

            if (row.View is not null)
            {
                parts.Add($"view={FormatView(row.View)}");
            }

            if (row.Component is not null)
            {
                parts.Add($"component={FormatComponent(row.Component)}");
            }

            builder.Append("    ");
            builder.AppendLine(string.Join(' ', parts));
        }

        return builder.ToString();
    }

    private static string FormatParent(Machina.Layout.Rows.NodeId? parent)
    {
        return parent?.Value ?? "<none>";
    }

    private static string FormatFrame(FrameSpec frame)
    {
        return frame switch
        {
            RootFrame => "Root",
            AbsoluteFrame absolute => string.Create(
                CultureInfo.InvariantCulture,
                $"Absolute x={absolute.X} y={absolute.Y} width={absolute.Width} height={absolute.Height}"),
            AnchorFrame anchor => FormatAnchorFrame(anchor),
            FixedFrame fixedFrame => string.Create(
                CultureInfo.InvariantCulture,
                $"Fixed width={fixedFrame.Width} height={fixedFrame.Height}"),
            FillFrame fill => string.Create(
                CultureInfo.InvariantCulture,
                $"Fill weight={fill.Weight} cross={FormatNullableNumber(fill.Cross)} crossFill={fill.CrossFill}"),
            CellFrame cell => string.Create(
                CultureInfo.InvariantCulture,
                $"Cell column={cell.Column} row={cell.Row} columnSpan={cell.ColumnSpan} rowSpan={cell.RowSpan}"),
            _ => frame.GetType().Name
        };
    }

    private static string FormatAnchorFrame(AnchorFrame anchor)
    {
        var values = new List<string> { "Anchor" };

        AddUiLength(values, "left", anchor.Left);
        AddUiLength(values, "right", anchor.Right);
        AddUiLength(values, "top", anchor.Top);
        AddUiLength(values, "bottom", anchor.Bottom);
        AddUiLength(values, "width", anchor.Width);
        AddUiLength(values, "height", anchor.Height);

        return string.Join(' ', values);
    }

    private static void AddUiLength(List<string> values, string key, UiLength? length)
    {
        if (length is null)
        {
            return;
        }

        values.Add(string.Create(CultureInfo.InvariantCulture, $"{key}={length.Value.Value}{length.Value.Unit}"));
    }

    private static string FormatArrange(ArrangeSpec arrange)
    {
        return arrange switch
        {
            StackArrange stack => string.Create(
                CultureInfo.InvariantCulture,
                $"Stack axis={stack.Axis} gap={stack.Gap} justify={stack.Justify} align={stack.Align}"),
            GridArrange grid => string.Create(
                CultureInfo.InvariantCulture,
                $"Grid columns={grid.Columns.Count} rows={grid.Rows.Count} columnGap={grid.ColumnGap} rowGap={grid.RowGap}"),
            _ => arrange.GetType().Name
        };
    }

    private static string FormatView(UiView view)
    {
        var values = new List<string>();
        values.Add(view.Semantics?.Role == Machina.Core.Semantics.UiRole.Text ? "Text" : "Rect");

        if (view.Style is not null)
        {
            AddColor(values, "bg", view.Style.Background);
            AddColor(values, "fg", view.Style.Foreground);
            AddColor(values, "border", view.Style.BorderColor);
            AddNumber(values, "borderThickness", view.Style.BorderThickness);
            AddNumber(values, "padding", view.Style.Padding);
        }

        if (view.TextStyle is not null)
        {
            AddColor(values, "textColor", view.TextStyle.Color);
            values.Add($"size={view.TextStyle.Size}");
        }

        if (view.Semantics is not null)
        {
            values.Add($"role={view.Semantics.Role}");

            if (!string.IsNullOrWhiteSpace(view.Semantics.Label))
            {
                values.Add($"label=\"{Escape(view.Semantics.Label)}\"");
            }

            if (view.Semantics.Disabled)
            {
                values.Add("disabled=true");
            }

            if (view.Semantics.Focusable)
            {
                values.Add("focusable=true");
            }
        }

        if (view.Action is not null)
        {
            values.Add($"action={view.Action.Name}");
        }

        return string.Join(' ', values);
    }

    private static void AddColor(List<string> values, string key, ColorToken? color)
    {
        if (color is null)
        {
            return;
        }

        values.Add($"{key}=#{color.Value.Rgba:X8}");
    }

    private static void AddNumber(List<string> values, string key, double value)
    {
        if (Math.Abs(value) < double.Epsilon)
        {
            return;
        }

        values.Add(string.Create(CultureInfo.InvariantCulture, $"{key}={value}"));
    }

    private static string FormatNullableNumber(double? value)
    {
        if (value is null)
        {
            return "<none>";
        }

        return value.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string FormatComponent(Machina.Core.Nodes.UiNode component)
    {
        var kind = component.GetType().Name.Replace("Node", string.Empty, StringComparison.Ordinal);
        var id = component.Id?.Value ?? "<generated>";
        return $"{kind}(id={id})";
    }
}
