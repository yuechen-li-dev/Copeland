namespace Machina.Presentation.Screens;

public readonly record struct ScreenLayerSlot
{
    public ScreenLayerSlot(ScreenLayerKey key, int order)
    {
        if (string.IsNullOrWhiteSpace(key.Value))
        {
            throw new ArgumentException("Screen layer slot requires a valid layer key.", nameof(key));
        }

        Key = key;
        Order = order;
    }

    public ScreenLayerKey Key { get; }

    public int Order { get; }

    public override string ToString() => $"{Key.Value}@{Order}";
}
