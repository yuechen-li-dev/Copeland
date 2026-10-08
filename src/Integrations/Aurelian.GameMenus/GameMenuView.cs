using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Pipeline;
using Machina.Runtime.Input;

namespace Aurelian.GameMenus;

/// <summary>Retains prepared menu layout and rejects stale or mismatched pointer releases.</summary>
public sealed class GameMenuView(ITextMeasurer? textMeasurer = null)
{
    private GameMenuPage? previousPage;
    private int previousSelection;
    private int previousWidth;
    private int previousHeight;
    private MachinaPreparedPresentation? prepared;
    private string? pressedAction;

    public int Rebuilds { get; private set; }

    public MachinaPreparedPresentation Prepare(GameMenuPage page, int selectedIndex, int width = 960, int height = 600)
    {
        if (prepared is not null && previousSelection == selectedIndex && previousWidth == width && previousHeight == height
            && previousPage is not null && SamePage(previousPage, page))
        {
            return prepared;
        }
        CancelPointer();
        prepared = new MachinaPresentationPipeline().Prepare(
            GameMenuTemplate.Build(page, selectedIndex, width, height), width, height, new UiLoweringOptions(textMeasurer));
        previousPage = page with { Entries = page.Entries.ToArray() };
        previousSelection = selectedIndex;
        previousWidth = width;
        previousHeight = height;
        Rebuilds++;
        return prepared;
    }

    public string? Pointer(GameMenuPage page, int selectedIndex, PointerPoint point, bool down, int width = 960, int height = 600)
    {
        var presentation = Prepare(page, selectedIndex, width, height);
        var hit = presentation.HitTest.HitTest(point);
        string? action = hit?.Semantics?.Disabled == true ? null : hit?.Action.Name;
        if (down)
        {
            pressedAction = action;
            return null;
        }
        string? pressed = pressedAction;
        pressedAction = null;
        return pressed is not null && pressed == action ? action : null;
    }

    public PointerPoint ActionCenter(GameMenuPage page, int selectedIndex, string action, int width = 960, int height = 600)
    {
        var presentation = Prepare(page, selectedIndex, width, height);
        var entry = presentation.Lowering.Actions.Single(pair => pair.Value.Name == action);
        var rect = presentation.Resolved.Nodes[entry.Key].Rect;
        return new PointerPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    public void CancelPointer()
    {
        pressedAction = null;
    }

    private static bool SamePage(GameMenuPage left, GameMenuPage right)
    {
        return left.Id == right.Id && left.Title == right.Title && left.Subtitle == right.Subtitle
            && left.Footer == right.Footer && left.Entries.SequenceEqual(right.Entries);
    }
}
