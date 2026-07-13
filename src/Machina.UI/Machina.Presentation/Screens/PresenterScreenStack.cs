namespace Machina.Presentation.Screens;

public sealed class PresenterScreenStack
{
    private readonly ScreenLayerOrder layerOrder;
    private readonly List<ScreenEntry> screens = [];
    private readonly HashSet<PresenterScreenId> screenIds = [];
    private long nextSequence;

    public PresenterScreenStack(ScreenLayerOrder layerOrder)
    {
        ArgumentNullException.ThrowIfNull(layerOrder);
        this.layerOrder = layerOrder;
    }

    public int Count => screens.Count;

    public ScreenLayerOrder LayerOrder => layerOrder;

    public void Add(IPresenterScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (!layerOrder.ContainsLayer(screen.Layer))
        {
            throw new ArgumentException(
                $"Presenter screen layer '{screen.Layer.Value}' is not declared in the screen layer order.",
                nameof(screen));
        }

        if (!screenIds.Add(screen.Id))
        {
            throw new ArgumentException(
                $"Presenter screen stack already contains screen identity '{screen.Id.Value}'.",
                nameof(screen));
        }

        screens.Add(new ScreenEntry(screen, nextSequence));
        nextSequence++;
    }

    public IReadOnlyList<IPresenterScreen> VisibleScreensInCompositionOrder()
    {
        return screens
            .Where(static entry => entry.Screen.IsVisible)
            .OrderBy(entry => layerOrder.GetCompositionIndex(entry.Screen.Layer))
            .ThenBy(entry => entry.Sequence)
            .Select(static entry => entry.Screen)
            .ToArray();
    }

    private readonly record struct ScreenEntry(IPresenterScreen Screen, long Sequence);
}
