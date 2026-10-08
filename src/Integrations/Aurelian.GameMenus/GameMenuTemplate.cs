using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;

namespace Aurelian.GameMenus;

public sealed record GameMenuEntry(string Id, string Label, bool Disabled = false);

public sealed record GameMenuPage(
    string Id,
    string Title,
    string Subtitle,
    IReadOnlyList<GameMenuEntry> Entries,
    string Footer = "UP / DOWN choose   ENTER confirm   ESC back");

public sealed record GameMenuButtonStyle(
    uint Background = 0x19303DE8,
    uint SelectedBackground = 0x365568F4,
    uint Foreground = 0xFFF1D4FF,
    uint DisabledForeground = 0x8A9B9EFF,
    uint Border = 0x8397A480,
    uint SelectedBorder = 0xF5DDB5FF,
    double BorderWidth = 1,
    double SelectedBorderWidth = 2,
    double CornerRadius = 8,
    double HorizontalPadding = 16,
    TextAlignX Alignment = TextAlignX.Left,
    bool MarkSelection = false);

public sealed record GameMenuButtonLayout(
    double X,
    double Y,
    double Width,
    double Height = 56,
    double RowSpacing = 72,
    string ButtonIdPrefix = "menu.",
    string ActionPrefix = "");

/// <summary>
/// Aurelian's reusable title/pause menu template. Machina owns button semantics,
/// layout and hit testing; the game owns each action's meaning and screen transitions.
/// </summary>
public static class GameMenuTemplate
{
    public static UiNode Build(GameMenuPage page, int selectedIndex, int width = 960, int height = 600)
    {
        ValidateEntries(page.Entries);
        if (page.Entries.Count > 4 || width < 640 || height < 600)
        {
            throw new ArgumentException("The basic menu page supports up to four entries on a surface at least 640x600.");
        }
        double panelWidth = Math.Min(600, width - 64);
        double left = (width - panelWidth) / 2;
        var nodes = new List<UiNode>
        {
            UI.Anchor(UI.Rect(id: page.Id + ".shade", style: new UiStyle(Background: ColorToken.Hex(0x06131DA0))),
                left: 0, top: 0, width: width, height: height),
            UI.Anchor(UI.Rect(id: page.Id + ".panel", style: new UiStyle(Background: ColorToken.Hex(0x102532F4))),
                left: left, top: 40, width: panelWidth, height: height - 80),
            Label(page.Id + ".title", page.Title, new Rect(left + 36, 72, panelWidth - 72, 50), TextSize.H1, 0xF5DDB5FF),
            Label(page.Id + ".subtitle", page.Subtitle, new Rect(left + 36, 128, panelWidth - 72, 34), TextSize.Sm, 0xD7E0E7FF),
        };
        AddButtons(nodes, page.Entries, selectedIndex, new GameMenuButtonLayout(left + 36, 190, panelWidth - 72), new());
        nodes.Add(Label(page.Id + ".footer", page.Footer, new Rect(left + 36, height - 106, panelWidth - 72, 28), TextSize.Sm, 0xD7E0E7FF));
        return UI.Surface(id: page.Id, width: width, height: height, children: nodes);
    }

    public static void AddButtons(
        List<UiNode> nodes,
        IReadOnlyList<GameMenuEntry> entries,
        int selectedIndex,
        GameMenuButtonLayout layout,
        GameMenuButtonStyle style)
    {
        ValidateEntries(entries);
        for (int index = 0; index < entries.Count; index++)
        {
            GameMenuEntry entry = entries[index];
            bool selected = index == selectedIndex && !entry.Disabled;
            string label = entry.Label;
            if (style.MarkSelection)
            {
                label = (selected ? "*" : " ") + " " + label;
            }
            uint foreground = entry.Disabled ? style.DisabledForeground : style.Foreground;
            UiNode button = StandardUI.Button(label,
                id: layout.ButtonIdPrefix + entry.Id,
                action: UiAction.Named(layout.ActionPrefix + entry.Id),
                disabled: entry.Disabled,
                style: new StandardButtonStyle(
                    ColorToken.Hex(selected ? style.SelectedBackground : style.Background),
                    ColorToken.Hex(style.Foreground),
                    ColorToken.Hex(selected ? style.SelectedBorder : style.Border),
                    selected ? style.SelectedBorderWidth : style.BorderWidth,
                    new TextStyle(ColorToken.Hex(foreground), TextSize.Md, style.Alignment, TextAlignY.Center),
                    layout.Width, layout.Height, CornerRadius: style.CornerRadius, HorizontalPadding: style.HorizontalPadding));
            nodes.Add(UI.Anchor(button, left: layout.X, top: layout.Y + index * layout.RowSpacing,
                width: layout.Width, height: layout.Height));
        }
    }

    public static void ValidateEntries(IReadOnlyList<GameMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameMenuEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
            {
                throw new ArgumentException("Menu action identities must be non-empty and unique.", nameof(entries));
            }
        }
    }

    private static UiNode Label(string id, string text, Rect rect, TextSize size, uint color)
    {
        return UI.Anchor(UI.Text(text, id: id, color: ColorToken.Hex(color), size: size),
            left: rect.X, top: rect.Y, width: rect.Width, height: rect.Height);
    }
}
