using Machina.Runtime.Input;

namespace Machina.Presenter.Sample;

/// <summary>
/// Avalonia-host callback accumulator. It is integration-host machinery: each
/// UI dispatch iteration publishes one immutable, ordered batch and drains the
/// accepted callbacks exactly once.
/// </summary>
internal sealed class PresenterHostInputCollector
{
    private readonly object gate = new();
    private readonly List<UiInputEvent> pendingEvents = [];
    private ulong nextBatchId;

    public void Record(UiInputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);

        lock (gate)
        {
            pendingEvents.Add(inputEvent);
        }
    }

    public UiInputBatch Publish()
    {
        lock (gate)
        {
            UiInputEvent[] events = pendingEvents.ToArray();
            pendingEvents.Clear();

            UiInputBatch batch = new(nextBatchId, events);
            nextBatchId++;
            return batch;
        }
    }
}
