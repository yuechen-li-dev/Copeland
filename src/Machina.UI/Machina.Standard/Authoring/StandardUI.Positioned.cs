using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Standard.Components;
using Machina.Standard.Text;
using Machina.Standard.Theme;

namespace Machina.Standard.Authoring;

public static partial class StandardUI
{
    /// <summary>A positioned action button with stable identity derived from its action ID.</summary>
    public static UiNode Button(
        string text,
        UiAction action,
        double x,
        double y,
        double width,
        double height = 34,
        NodeId? id = null,
        ButtonVariant variant = ButtonVariant.Default,
        ButtonSize size = ButtonSize.Medium,
        bool disabled = false,
        StandardTheme? theme = null,
        StandardButtonStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Button(text, x, y, width, height, id ?? action.Id.ToNodeId(), action,
            variant, size, disabled, theme, style);
    }

    /// <summary>Places any component in coordinates relative to its parent.</summary>
    public static UiNode Place(UiNode child, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(child);
        return UI.Anchor(child, left: x, top: y, width: width, height: height);
    }

    /// <summary>Places a content-sized rectangle without repeating its measured dimensions.</summary>
    public static UiNode Place(RectNode child, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Width is not { } width || child.Height is not { } height)
        {
            throw new ArgumentException("The component must declare both width and height.", nameof(child));
        }

        return Place(child, x, y, width, height);
    }

    /// <summary>A local coordinate system for explicitly positioned children.</summary>
    public static UiNode Frame(
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<UiNode> children,
        NodeId? id = null,
        UiStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(children);
        return UI.Layer(id, new AbsoluteFrame(x, y, width, height), style, children) with
        {
            Semantics = new UiSemantics(UiRole.Container),
        };
    }

    /// <summary>A themed panel whose children use local coordinates. No implicit padding.</summary>
    public static UiNode Panel(
        double x,
        double y,
        double width,
        double height,
        NodeId? id = null,
        IReadOnlyList<UiNode>? children = null,
        StandardTheme? theme = null,
        StandardCardStyle? style = null)
    {
        var panelStyle = style ?? (theme ?? StandardTheme.Default).Card.Default;
        return Frame(x, y, width, height, children ?? [], id, new UiStyle(
            Background: panelStyle.Background,
            Foreground: panelStyle.Foreground,
            BorderColor: panelStyle.BorderColor,
            BorderThickness: panelStyle.BorderThickness,
            Shape: panelStyle.CornerRadius > 0 ? UiShapeKind.RoundedRect : null,
            CornerRadius: panelStyle.CornerRadius));
    }

    public static UiNode Button(
        string text,
        double x,
        double y,
        double width,
        double height = 34,
        NodeId? id = null,
        UiAction? action = null,
        ButtonVariant variant = ButtonVariant.Default,
        ButtonSize size = ButtonSize.Medium,
        bool disabled = false,
        StandardTheme? theme = null,
        StandardButtonStyle? style = null)
    {
        return Place(StandardButton.Create(text, id, action, variant, size, disabled, theme, style, width, height),
            x, y, width, height);
    }

    public static UiNode Label(
        string text,
        double x,
        double y,
        double width,
        double height = 34,
        NodeId? id = null,
        TextSize size = TextSize.Md,
        ColorToken? color = null,
        StandardTheme? theme = null)
    {
        return Place(Components.Label.Create(text, id, theme, size, color), x, y, width, height);
    }

    /// <summary>
    /// Word-wrapped plain text with a fixed width and measured content height.
    /// Measurement and presentation share the supplied metrics, or deterministic bitmap metrics by default.
    /// </summary>
    public static RectNode Paragraph(
        string text,
        double width,
        NodeId? id = null,
        MachinaTextVariant variant = MachinaTextVariant.Body,
        ColorToken? foreground = null,
        StandardTheme? theme = null,
        MachinaTextLeading leading = default,
        IMachinaTextMeasurer? measurer = null)
    {
        var spec = Machina.Standard.Text.Text.Plain(text, variant: variant, leading: leading);
        var measured = MachinaTextLayoutEngine.Measure(spec, width, measurer);
        return Components.TextBlock.Create(spec, id, theme, foreground,
            width: width, height: measured.Height, measurer: measurer);
    }
}
