namespace Machina.Presenter.Sample;

public sealed record DemoState(int Count, bool EmailUpdates, bool Notifications)
{
    public static DemoState Default { get; } = new(0, false, false);
}
