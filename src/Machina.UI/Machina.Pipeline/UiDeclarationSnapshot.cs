using Machina.Core.Nodes;
using Machina.Layout.Frames;
using Machina.Standard.Components;

namespace Machina.Pipeline;

/// <summary>
/// A flat value snapshot of the declaration. Child references are replaced by a
/// shared sentinel, then traversed in order. Record equality compares every scalar
/// property, including semantics/actions; collection contents are copied as values.
/// </summary>
internal sealed class UiDeclarationSnapshot
{
    private static readonly UiNode Child = new SpacerNode(StackAxis.Vertical, 0);
    private readonly List<object> entries = new();
    private bool cacheable = true;

    public static UiDeclarationSnapshot Capture(UiNode declaration)
    {
        var snapshot = new UiDeclarationSnapshot();
        snapshot.Append(declaration);
        return snapshot;
    }

    public bool SameAs(UiDeclarationSnapshot? other)
    {
        return cacheable && other is { cacheable: true } && entries.SequenceEqual(other.entries);
    }

    private void Append(UiNode node)
    {
        switch (node)
        {
            case RectNode rect:
                entries.Add(rect with { Child = rect.Child is null ? null : Child });
                if (rect.Child is not null)
                {
                    Append(rect.Child);
                }
                break;
            case ContainerNode container:
                entries.Add(container with { Child = Child });
                Append(container.Child);
                break;
            case PlacementNode placement:
                entries.Add(placement with { Child = Child });
                Append(placement.Child);
                break;
            case LayerNode layer:
                entries.Add(layer with { Children = Array.Empty<UiNode>() });
                entries.Add(layer.Children.Count);
                foreach (UiNode child in layer.Children)
                {
                    Append(child);
                }
                break;
            case StackNode stack:
                entries.Add(stack with { Items = Array.Empty<UiStackItem>() });
                entries.Add(stack.Items.Count);
                foreach (UiStackItem item in stack.Items)
                {
                    entries.Add(item with { Child = Child });
                    Append(item.Child);
                }
                break;
            case GridNode grid:
                entries.Add(grid with
                {
                    Columns = Array.Empty<GridTrack>(),
                    Rows = Array.Empty<GridTrack>(),
                    Cells = Array.Empty<UiGridCell>(),
                });
                entries.Add(grid.Columns.Count);
                entries.AddRange(grid.Columns);
                entries.Add(grid.Rows.Count);
                entries.AddRange(grid.Rows);
                entries.Add(grid.Cells.Count);
                foreach (UiGridCell cell in grid.Cells)
                {
                    entries.Add(cell with { Child = Child });
                    Append(cell.Child);
                }
                break;
            case RichTextNode rich:
                entries.Add(rich);
                // Opaque payloads may hide mutable collections or renderer-owned
                // state. Conservatively prepare again instead of guessing equality.
                cacheable &= rich.Payload is StandardTextBlockMetadata;
                break;
            case TextNode or ButtonNode or SpacerNode or VectorIconNode:
                entries.Add(node);
                break;
            default:
                cacheable = false;
                entries.Add(node);
                break;
        }
    }
}
