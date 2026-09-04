using Machina.Core.Actions;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Core.Authoring;

public static class UI
{
    public static UiNode Text(
        string text,
        NodeId? id = null,
        ColorToken? color = null,
        TextSize size = TextSize.Md,
        TextAlignX alignX = TextAlignX.Left,
        TextAlignY alignY = TextAlignY.Top,
        TextStyle? style = null)
    {
        var effectiveStyle = MergeTextStyle(style, color, size, alignX, alignY);
        return new TextNode(text, effectiveStyle) with
        {
            Id = id,
        };
    }

    public static UiNode Rect(
        UiNode? child = null,
        NodeId? id = null,
        double? width = null,
        double? height = null,
        ColorToken? color = null,
        double? padding = null,
        ColorToken? borderColor = null,
        double? borderThickness = null,
        UiStyle? style = null)
    {
        var effectiveStyle = MergeBoxStyle(style, color, padding, foreground: null, borderColor, borderThickness);

        return new RectNode(
            child,
            width,
            height,
            Color: null,
            effectiveStyle.Padding,
            effectiveStyle) with
        {
            Id = id,
        };
    }

    public static UiNode Row(
        IReadOnlyList<UiNode> children,
        NodeId? id = null,
        double gap = 0,
        double padding = 0,
        StackJustify justify = StackJustify.Start,
        StackAlign align = StackAlign.Start)
    {
        return new StackNode(StackAxis.Horizontal, WrapImplicitStackItems(children), gap, EdgeInsets.All(padding), justify, align) with
        {
            Id = id,
        };
    }

    public static UiNode Column(
        IReadOnlyList<UiNode> children,
        NodeId? id = null,
        double gap = 0,
        double padding = 0,
        StackJustify justify = StackJustify.Start,
        StackAlign align = StackAlign.Start)
    {
        return new StackNode(StackAxis.Vertical, WrapImplicitStackItems(children), gap, EdgeInsets.All(padding), justify, align) with
        {
            Id = id,
        };
    }

    public static UiNode Stack(
        NodeId? id,
        StackAxis axis,
        IReadOnlyList<UiStackItem> children,
        double gap = 0,
        UiPadding? padding = null,
        StackJustify justify = StackJustify.Start,
        StackAlign align = StackAlign.Start)
    {
        return new StackNode(axis, children, gap, (padding ?? UiPadding.Zero).ToEdgeInsets(), justify, align) with
        {
            Id = id,
        };
    }

    public static UiNode VStack(
        IReadOnlyList<UiStackItem> children,
        NodeId? id = null,
        double gap = 0,
        UiPadding? padding = null,
        StackJustify justify = StackJustify.Start,
        StackAlign align = StackAlign.Start)
    {
        return Stack(id, StackAxis.Vertical, children, gap, padding, justify, align);
    }

    public static UiNode HStack(
        IReadOnlyList<UiStackItem> children,
        NodeId? id = null,
        double gap = 0,
        UiPadding? padding = null,
        StackJustify justify = StackJustify.Start,
        StackAlign align = StackAlign.Start)
    {
        return Stack(id, StackAxis.Horizontal, children, gap, padding, justify, align);
    }

    public static UiNode Grid(
        IReadOnlyList<GridTrack> columns,
        IReadOnlyList<GridTrack> rows,
        IReadOnlyList<UiGridCell> children,
        NodeId? id = null,
        double columnGap = 0,
        double rowGap = 0)
    {
        ValidateTracks(columns, nameof(columns));
        ValidateTracks(rows, nameof(rows));
        ValidateFiniteNonNegative(columnGap, nameof(columnGap));
        ValidateFiniteNonNegative(rowGap, nameof(rowGap));

        var normalizedCells = NormalizeExplicitGridCells(children);

        return new GridNode(columns.ToArray(), rows.ToArray(), normalizedCells, columnGap, rowGap) with
        {
            Id = id,
        };
    }

    public static UiNode Grid(
        IReadOnlyList<GridTrack> columns,
        IReadOnlyList<IReadOnlyList<UiNode>> cells,
        NodeId? id = null,
        IReadOnlyList<GridTrack>? rows = null,
        double columnGap = 0,
        double rowGap = 0)
    {
        ValidateTracks(columns, nameof(columns));
        ValidateFiniteNonNegative(columnGap, nameof(columnGap));
        ValidateFiniteNonNegative(rowGap, nameof(rowGap));

        var normalizedRows = rows is null
            ? null
            : NormalizeTracks(rows, nameof(rows));
        var normalizedCells = NormalizeMatrixGridCells(cells, columns.Count, normalizedRows?.Count);
        var effectiveRows = normalizedRows ?? CreateDerivedMatrixRows(normalizedCells.Count);

        return new GridNode(columns.ToArray(), effectiveRows, normalizedCells, columnGap, rowGap) with
        {
            Id = id,
        };
    }

    public static UiGridCell GridCell(
        int row,
        int column,
        UiNode child)
    {
        ValidateGridCellPosition(row, nameof(row));
        ValidateGridCellPosition(column, nameof(column));
        ArgumentNullException.ThrowIfNull(child);
        return new UiGridCell(row, column, child);
    }

    public static class StackItem
    {
        public static UiStackItem Fixed(
            double main,
            UiNode child)
        {
            ValidateStackItemChild(child);
            ValidateFiniteNonNegative(main, nameof(main));
            return new UiStackItem(child, UiStackItemKind.Fixed, MainSize: main);
        }

        public static UiStackItem Fill(
            double weight,
            UiNode child)
        {
            ValidateStackItemChild(child);
            ValidateFinitePositive(weight, nameof(weight));
            return new UiStackItem(child, UiStackItemKind.Fill, Weight: weight);
        }

        public static UiStackItem Auto(
            UiNode child)
        {
            ValidateStackItemChild(child);
            return new UiStackItem(child, UiStackItemKind.Auto);
        }
    }

    public static UiStackItem Fixed(
        double main,
        UiNode child)
    {
        return StackItem.Fixed(main, child);
    }

    public static UiStackItem Fill(
        UiNode child,
        double weight = 1)
    {
        return StackItem.Fill(weight, child);
    }

    public static UiStackItem Auto(UiNode child)
    {
        return StackItem.Auto(child);
    }

    public static UiStackItem Space(double weight = 1)
    {
        return StackItem.Fill(weight, Rect());
    }

    public static class Track
    {
        public static GridTrack Fixed(double size)
        {
            ValidateFiniteNonNegative(size, nameof(size));
            return new FixedGridTrack(size);
        }

        public static GridTrack Fill(double weight)
        {
            ValidateFinitePositive(weight, nameof(weight));
            return new FillGridTrack(weight);
        }
    }

    private static IReadOnlyList<UiStackItem> WrapImplicitStackItems(
        IReadOnlyList<UiNode> children)
    {
        return children.Select(StackItem.Auto).ToArray();
    }

    private static IReadOnlyList<UiGridCell> NormalizeExplicitGridCells(
        IReadOnlyList<UiGridCell> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        var seen = new HashSet<(int Row, int Column)>();
        var normalized = new UiGridCell[children.Count];

        for (var index = 0; index < children.Count; index++)
        {
            ArgumentNullException.ThrowIfNull(children[index]);

            var cell = children[index];
            ValidateGridCellPosition(cell.Row, nameof(cell.Row));
            ValidateGridCellPosition(cell.Column, nameof(cell.Column));
            ArgumentNullException.ThrowIfNull(cell.Child);

            if (!seen.Add((cell.Row, cell.Column)))
            {
                throw new ArgumentException(
                    $"Grid cells must be unique by row and column. Duplicate cell at row {cell.Row}, column {cell.Column}.",
                    nameof(children));
            }

            normalized[index] = cell;
        }

        return normalized;
    }

    private static IReadOnlyList<UiGridCell> NormalizeMatrixGridCells(
        IReadOnlyList<IReadOnlyList<UiNode>> cells,
        int columnCount,
        int? explicitRowCount)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Count == 0)
        {
            throw new ArgumentException("Matrix grid cells must contain at least one row.", nameof(cells));
        }

        if (explicitRowCount is { } rowCount && cells.Count != rowCount)
        {
            throw new ArgumentException(
                $"Matrix grid row count {cells.Count} must match explicit row track count {rowCount}.",
                nameof(cells));
        }

        var normalized = new List<UiGridCell>(cells.Count * Math.Max(1, columnCount));

        for (var row = 0; row < cells.Count; row++)
        {
            var rowCells = cells[row];
            ArgumentNullException.ThrowIfNull(rowCells);

            if (rowCells.Count != columnCount)
            {
                throw new ArgumentException(
                    $"Matrix grid row {row} contains {rowCells.Count} cells but expected {columnCount}.",
                    nameof(cells));
            }

            for (var column = 0; column < rowCells.Count; column++)
            {
                var child = rowCells[column];
                if (child is null)
                {
                    throw new ArgumentException(
                        $"Matrix grid cell at row {row}, column {column} must not be null.",
                        nameof(cells));
                }

                normalized.Add(new UiGridCell(row, column, child));
            }
        }

        return normalized;
    }

    private static IReadOnlyList<GridTrack> NormalizeTracks(
        IReadOnlyList<GridTrack> tracks,
        string name)
    {
        ValidateTracks(tracks, name);
        return tracks.ToArray();
    }

    private static GridTrack[] CreateDerivedMatrixRows(int rowCount)
    {
        var rows = new GridTrack[rowCount];

        for (var index = 0; index < rowCount; index++)
        {
            rows[index] = new FillGridTrack(1);
        }

        return rows;
    }

    private static void ValidateTracks(
        IReadOnlyList<GridTrack> tracks,
        string name)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        if (tracks.Count == 0)
        {
            throw new ArgumentException("Grid tracks must contain at least one track.", name);
        }

        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            ArgumentNullException.ThrowIfNull(track);

            switch (track)
            {
                case FixedGridTrack fixedTrack:
                    ValidateFiniteNonNegative(fixedTrack.Size, $"{name}[{index}]");
                    break;
                case FillGridTrack fillTrack:
                    ValidateFinitePositive(fillTrack.Weight, $"{name}[{index}]");
                    break;
                default:
                    throw new ArgumentException(
                        $"Unsupported grid track type '{track.GetType().Name}'.",
                        name);
            }
        }
    }

    private static void ValidateStackItemChild(UiNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
    }

    private static void ValidateGridCellPosition(int value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateFiniteNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateFinitePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    public static UiNode Container(
        UiNode child,
        NodeId? id = null,
        Align alignX = Align.Start,
        Align alignY = Align.Start)
    {
        return new ContainerNode(child, alignX, alignY) with
        {
            Id = id,
        };
    }

    public static UiNode Button(
        string text,
        NodeId? id = null,
        UiAction? action = null,
        bool disabled = false,
        ColorToken? color = null,
        UiStyle? style = null)
    {
        var effectiveStyle = MergeButtonStyle(style, color);

        return new ButtonNode(text, action, disabled, effectiveStyle) with
        {
            Id = id,
        };
    }

    public static UiNode HSpace(
        double width,
        NodeId? id = null)
    {
        return new SpacerNode(StackAxis.Horizontal, width) with
        {
            Id = id,
        };
    }

    public static UiNode VSpace(
        double height,
        NodeId? id = null)
    {
        return new SpacerNode(StackAxis.Vertical, height) with
        {
            Id = id,
        };
    }

    public static UiNode Surface(
        NodeId? id = null,
        double width = 0,
        double height = 0,
        ColorToken? color = null,
        UiStyle? style = null,
        IReadOnlyList<UiNode>? children = null)
    {
        var effectiveStyle = MergeBoxStyle(style, color, padding: null, foreground: null, borderColor: null, borderThickness: null);

        return new LayerNode(
            Frame: new RootFrame(),
            Style: effectiveStyle,
            Children: children ?? [],
            Width: width > 0 ? width : null,
            Height: height > 0 ? height : null) with
        {
            Id = id,
        };
    }

    public static UiNode Layer(
        NodeId? id = null,
        FrameSpec? frame = null,
        UiStyle? style = null,
        IReadOnlyList<UiNode>? children = null)
    {
        return new LayerNode(
            Frame: frame,
            Style: style,
            Children: children ?? []) with
        {
            Id = id,
        };
    }

    public static UiNode At(
        UiNode child,
        NodeId? id = null,
        double x = 0,
        double y = 0,
        double width = 0,
        double height = 0)
    {
        return new PlacementNode(
            new AbsoluteFrame(x, y, width, height),
            child) with
        {
            Id = id,
        };
    }

    public static UiNode Anchor(
        UiNode child,
        NodeId? id = null,
        UiLength? left = null,
        UiLength? right = null,
        UiLength? top = null,
        UiLength? bottom = null,
        UiLength? width = null,
        UiLength? height = null)
    {
        return new PlacementNode(
            new AnchorFrame(left, right, top, bottom, width, height),
            child) with
        {
            Id = id,
        };
    }

    private static TextStyle MergeTextStyle(
        TextStyle? style,
        ColorToken? color,
        TextSize size,
        TextAlignX alignX,
        TextAlignY alignY)
    {
        var effectiveStyle = style ?? new TextStyle();

        return effectiveStyle with
        {
            Color = color ?? effectiveStyle.Color,
            Size = size,
            AlignX = alignX,
            AlignY = alignY,
        };
    }

    private static UiStyle MergeBoxStyle(
        UiStyle? style,
        ColorToken? background,
        double? padding,
        ColorToken? foreground,
        ColorToken? borderColor,
        double? borderThickness)
    {
        var effectiveStyle = style ?? new UiStyle();

        return effectiveStyle with
        {
            Background = background ?? effectiveStyle.Background,
            Foreground = foreground ?? effectiveStyle.Foreground,
            Padding = padding ?? effectiveStyle.Padding,
            BorderColor = borderColor ?? effectiveStyle.BorderColor,
            BorderThickness = borderThickness ?? effectiveStyle.BorderThickness,
            ClipToBounds = effectiveStyle.ClipToBounds,
        };
    }

    private static UiStyle? MergeButtonStyle(
        UiStyle? style,
        ColorToken? color)
    {
        if (style is null && color is null)
        {
            return null;
        }

        var effectiveStyle = style ?? new UiStyle();

        return effectiveStyle with
        {
            Foreground = color ?? effectiveStyle.Foreground,
        };
    }
}
