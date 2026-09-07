using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;

namespace Aurelian.Machina;

public sealed record StrategyResourceFact(string Label, string Value);
public sealed record StrategyActionFact(string Key, string Label, string Detail, bool Enabled);
public sealed record StrategyHudCopy(string ObjectiveSection, string CommandSection, string TacticalSection, string Controls)
{
    public static StrategyHudCopy Default { get; } = new("CHAPTER 01 / ESTABLISH", "CONTEXT & COMMAND", "TACTICAL OVERVIEW",
        "DRAG select   SHIFT add   RIGHT CLICK order   B build   N worker   F ranger   1 group   CTRL+1 store   SPACE center   Wheel zoom");
}
public sealed record StrategyHudSnapshot(
    string Title,
    string Subtitle,
    IReadOnlyList<StrategyResourceFact> Resources,
    string ObjectiveTitle,
    IReadOnlyList<string> Objectives,
    string SelectionTitle,
    string SelectionDetail,
    IReadOnlyList<StrategyActionFact> Actions,
    string Notice,
    StrategyHudCopy? Copy = null);

public sealed record StrategyHudStyle(uint Panel, uint Border, uint Text, uint Muted, uint Accent)
{
    public static StrategyHudStyle Woodland { get; } = new(0x1B302DEE, 0x506956FF, 0xF1E8CCFF, 0xACBBA5FF, 0xDBBC79FF);
}

/// <summary>A bounded 1280x800 logical layout. Hosts scale the viewport; applications supply meaning.</summary>
public static class StrategyHudProfile
{
    public const int Width = 1280;
    public const int Height = 800;

    public static UiNode Build(StrategyHudSnapshot snapshot, StrategyHudStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        StrategyHudStyle skin = style ?? StrategyHudStyle.Woodland;
        StrategyHudCopy copy = snapshot.Copy ?? StrategyHudCopy.Default;
        if (snapshot.Resources.Count > 4 || snapshot.Actions.Count > 4 || snapshot.Objectives.Count > 4)
        {
            throw new ArgumentException("The bounded HUD supports four resources, actions and objective lines.", nameof(snapshot));
        }
        var children = new List<UiNode>();
        Panel(children, "resource-strip", 0, 0, Width, 78, skin);
        Text(children, "title", snapshot.Title, 30, 14, 330, 34, TextSize.H1, skin.Text);
        Text(children, "subtitle", snapshot.Subtitle, 32, 49, 330, 22, TextSize.Sm, skin.Accent);
        for (int index = 0; index < snapshot.Resources.Count; index++)
        {
            StrategyResourceFact resource = snapshot.Resources[index];
            double x = 450 + index * 175;
            Text(children, $"resource-{index}-value", resource.Value, x, 12, 160, 32, TextSize.Md, skin.Text);
            Text(children, $"resource-{index}-label", resource.Label, x, 47, 160, 22, TextSize.Sm, skin.Muted);
        }
        Panel(children, "objective-card", 24, 102, 280, 190, skin);
        Text(children, "objective-eyebrow", copy.ObjectiveSection, 43, 118, 240, 22, TextSize.Sm, skin.Accent);
        Text(children, "objective-title", snapshot.ObjectiveTitle, 43, 148, 240, 30, TextSize.Md, skin.Text);
        for (int index = 0; index < snapshot.Objectives.Count; index++)
        {
            Text(children, $"objective-{index}", snapshot.Objectives[index], 43, 190 + index * 24, 245, 24, TextSize.Sm, skin.Muted);
        }
        Panel(children, "command-dock", 24, 610, 1232, 164, skin);
        Text(children, "selection-eyebrow", "YOUR SELECTION", 44, 627, 235, 22, TextSize.Sm, skin.Accent);
        Text(children, "selection-title", snapshot.SelectionTitle, 118, 662, 200, 32, TextSize.Md, skin.Text);
        Text(children, "selection-detail", snapshot.SelectionDetail, 118, 703, 210, 48, TextSize.Sm, skin.Muted);
        Text(children, "commands-title", copy.CommandSection, 361, 627, 500, 24, TextSize.Sm, skin.Accent);
        for (int index = 0; index < snapshot.Actions.Count; index++)
        {
            StrategyActionFact action = snapshot.Actions[index];
            double x = 361 + index * 151;
            Panel(children, $"action-{index}", x, 659, 138, 94, skin);
            uint color = action.Enabled ? skin.Text : skin.Muted;
            Text(children, $"action-{index}-key", action.Key, x + 12, 665, 115, 20, TextSize.Sm, skin.Accent);
            Text(children, $"action-{index}-label", action.Label, x + 12, 689, 115, 27, TextSize.Md, color);
            Text(children, $"action-{index}-detail", action.Detail, x + 12, 724, 115, 21, TextSize.Sm, skin.Muted);
        }
        Text(children, "tactical-title", copy.TacticalSection, 1000, 627, 235, 24, TextSize.Sm, skin.Accent);
        Panel(children, "notice-panel", 24, 570, 1232, 32, skin);
        Panel(children, "controls-panel", 0, 774, Width, 26, skin);
        Text(children, "notice", snapshot.Notice, 34, 577, 1080, 24, TextSize.Sm, skin.Text);
        Text(children, "controls", copy.Controls, 28, 779, 1220, 20, TextSize.Sm, skin.Muted);
        return UI.Surface(id: "strategy-hud", width: Width, height: Height, children: children);
    }

    private static void Panel(List<UiNode> nodes, string id, double x, double y, double width, double height, StrategyHudStyle skin)
    {
        nodes.Add(UI.Anchor(UI.Rect(id: id + "-border", style: new UiStyle(Background: ColorToken.Hex(skin.Border))),
            id: id + "-border-slot", left: x, top: y, width: width, height: height));
        nodes.Add(UI.Anchor(UI.Rect(id: id, style: new UiStyle(Background: ColorToken.Hex(skin.Panel))),
            id: id + "-slot", left: x + 1, top: y + 1, width: width - 2, height: height - 2));
    }

    private static void Text(List<UiNode> nodes, string id, string text, double x, double y, double width, double height, TextSize size, uint color)
    {
        nodes.Add(UI.Anchor(UI.Text(text, id: id, color: ColorToken.Hex(color), size: size),
            id: id + "-slot", left: x, top: y, width: width, height: height));
    }
}
