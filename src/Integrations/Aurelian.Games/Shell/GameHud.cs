using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation;

namespace Aurelian.Games;

public enum GameHudTextSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// A small retained HUD description. Games describe panels and text in logical units on a surface at least
/// 960x600 (wider or taller windows get a larger logical surface); the shell scales the result to physical pixels
/// and only re-lowers it when the description changes.
/// </summary>
public sealed class GameHud
{
    internal readonly List<GameHudItem> Items = [];

    internal GameHud(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Logical surface width. Anchor right-aligned elements against this.</summary>
    public int Width { get; }

    /// <summary>Logical surface height.</summary>
    public int Height { get; }

    public void Panel(double left, double top, double width, double height, uint rgba = 0x071520E8)
    {
        Items.Add(new GameHudItem(GameHudItemKind.Panel, left, top, width, height, rgba, null, GameHudTextSize.Small));
    }

    public void Text(string text, double left, double top, double width, double height, uint rgba = 0xFFF1D4FF,
        GameHudTextSize size = GameHudTextSize.Small)
    {
        ArgumentNullException.ThrowIfNull(text);
        Items.Add(new GameHudItem(GameHudItemKind.Text, left, top, width, height, rgba, text, size));
    }

    /// <summary>A top status bar with one line of text.</summary>
    public void StatusBar(string text)
    {
        Panel(16, 16, Width - 32, 36);
        Text(text, 32, 26, Width - 64, 20);
    }

    /// <summary>A plus-shaped crosshair at the surface center.</summary>
    public void Crosshair(uint rgba = 0xFFF1D4FF)
    {
        double centerX = Width / 2.0;
        double centerY = Height / 2.0;
        Panel(centerX - 6, centerY - 1, 12, 2, rgba);
        Panel(centerX - 1, centerY - 6, 2, 12, rgba);
    }
}

internal enum GameHudItemKind
{
    Panel,
    Text,
}

internal sealed record GameHudItem(GameHudItemKind Kind, double Left, double Top, double Width, double Height, uint Rgba,
    string? Text, GameHudTextSize Size);

/// <summary>Lowers a HUD description through Machina and caches it until the description or surface changes.</summary>
internal sealed class GameHudPresenter(ITextMeasurer? font)
{
    private GameHudItem[] previous = [];
    private int previousWidth;
    private int previousHeight;
    private MachinaPresentationFrame? frame;

    public int Rebuilds { get; private set; }

    public MachinaPresentationFrame Prepare(GameHud hud)
    {
        if (frame is not null && hud.Width == previousWidth && hud.Height == previousHeight && hud.Items.SequenceEqual(previous))
        {
            return frame;
        }
        var nodes = new List<UiNode>(hud.Items.Count);
        foreach (GameHudItem item in hud.Items)
        {
            UiNode node = item.Kind == GameHudItemKind.Panel
                ? UI.Rect(style: new UiStyle(Background: ColorToken.Hex(item.Rgba)))
                : UI.Text(item.Text!, color: ColorToken.Hex(item.Rgba), size: item.Size switch
                {
                    GameHudTextSize.Large => TextSize.H1,
                    GameHudTextSize.Medium => TextSize.Md,
                    _ => TextSize.Sm,
                });
            nodes.Add(UI.Anchor(node, left: item.Left, top: item.Top, width: item.Width, height: item.Height));
        }
        frame = new MachinaPresentationPipeline().Prepare(UI.Surface(width: hud.Width, height: hud.Height, children: nodes),
            hud.Width, hud.Height, new UiLoweringOptions(font)).PresentationFrame;
        previous = hud.Items.ToArray();
        previousWidth = hud.Width;
        previousHeight = hud.Height;
        Rebuilds++;
        return frame;
    }
}
