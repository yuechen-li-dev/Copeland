using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;

namespace Machina.Standard.Windowing;

public enum WindowCaptionButton { None, Minimize, Maximize, Close }

/// <summary>Client-coordinate geometry shared by native hit testing and GPU drawing.</summary>
public sealed record WindowTitleBarLayout(double Width, double Height = 32, double ButtonWidth = 46)
{
    public Rect Button(WindowCaptionButton button)
    {
        double width = Math.Min(ButtonWidth, Width / 3);
        return button switch
        {
            WindowCaptionButton.Minimize => new(Width - 3 * width, 0, width, Height),
            WindowCaptionButton.Maximize => new(Width - 2 * width, 0, width, Height),
            WindowCaptionButton.Close => new(Width - width, 0, width, Height),
            _ => default
        };
    }

    public WindowCaptionButton At(double x, double y)
    {
        if (y < 0 || y >= Height || x < 0 || x >= Width) return WindowCaptionButton.None;
        foreach (var button in new[] { WindowCaptionButton.Close, WindowCaptionButton.Maximize, WindowCaptionButton.Minimize })
            if (x >= Button(button).X) return button;
        return WindowCaptionButton.None;
    }
}

public static class WindowTitleBar
{
    public static UiNode Create(NodeId id, WindowTitleBarLayout layout, string title, bool maximized,
        WindowCaptionButton hovered = WindowCaptionButton.None, WindowCaptionButton pressed = WindowCaptionButton.None,
        ColorToken? background = null, ColorToken? foreground = null)
    {
        if (!double.IsFinite(layout.Width) || !double.IsFinite(layout.Height) || !double.IsFinite(layout.ButtonWidth)
            || layout.Width <= 0 || layout.Height <= 0 || layout.ButtonWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(layout));
        var surface = background ?? ColorToken.Hex(0x18181BFF);
        var ink = foreground ?? ColorToken.White;
        var children = new List<UiNode>();
        double titleWidth = Math.Max(1, layout.Button(WindowCaptionButton.Minimize).X - 20);
        children.Add(StandardUI.Place(UI.Rect(new TextNode(title,
            new TextStyle(ink, TextSize.Sm, TextAlignX.Left, TextAlignY.Center)) { FillAvailableSpace = true },
            width: titleWidth, height: layout.Height, style: new UiStyle(ClipToBounds: true)),
            10, 0, titleWidth, layout.Height));
        foreach (var button in new[] { WindowCaptionButton.Minimize, WindowCaptionButton.Maximize, WindowCaptionButton.Close })
        {
            var box = layout.Button(button);
            bool over = hovered == button;
            ColorToken fill = button == WindowCaptionButton.Close && over
                ? ColorToken.Hex(pressed == button ? 0xB02030FF : 0xCF3040FF)
                : StandardInteraction.Surface(surface, ink, over, over && pressed == button);
            string label = button == WindowCaptionButton.Maximize && maximized ? "Restore" : button.ToString();
            string action = "window." + button.ToString().ToLowerInvariant();
            var glyphs = new List<UiNode>();
            double cx = box.Width / 2, cy = box.Height / 2;
            void Line(double x, double y, double width, double height) => glyphs.Add(StandardUI.Place(
                UI.Rect(width: width, height: height, color: ink), x, y, width, height));
            if (button == WindowCaptionButton.Minimize) Line(cx - 5, cy, 10, 1);
            else if (button == WindowCaptionButton.Close)
            {
                // Pixel-aligned geometry keeps icons independent of font metrics.
                for (int offset = -4; offset <= 4; offset++)
                {
                    Line(cx + offset, cy + offset, 1, 1);
                    Line(cx + offset, cy - offset, 1, 1);
                }
            }
            else
            {
                if (maximized) { Line(cx - 3, cy - 6, 9, 1); Line(cx + 5, cy - 5, 1, 8); }
                Line(cx - 5, cy - 4, 10, 1); Line(cx - 5, cy + 5, 10, 1);
                Line(cx - 5, cy - 4, 1, 10); Line(cx + 4, cy - 4, 1, 10);
            }
            var node = UI.Rect(UI.Layer(children: glyphs), id.Child(button.ToString()), box.Width, box.Height,
                color: fill) with
            {
                Semantics = new(UiRole.Button, label, Focusable: true),
                DeclaredAction = UiAction.Named(action)
            };
            children.Add(StandardUI.Place(node, box.X, box.Y, box.Width, box.Height));
        }
        return UI.Rect(UI.Layer(children: children), id, layout.Width, layout.Height,
            color: surface, style: new UiStyle(ClipToBounds: true));
    }
}
