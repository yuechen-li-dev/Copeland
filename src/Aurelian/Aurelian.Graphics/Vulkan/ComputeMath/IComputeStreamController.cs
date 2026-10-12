namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record ComputeStreamForecast(int BatchCount, int DispatchesPerBatch, ulong UploadBytes);

public sealed record ComputeStreamObservation(long Sequence, int Slot, bool Completed,
    int Outstanding, double PreparationMilliseconds, double? GpuMilliseconds);

/// <summary>Feedforward admission and completion evidence. Predictions select a
/// bounded queue depth; the Vulkan timeline remains the authority for reuse.</summary>
public interface IComputeStreamController
{
    int Plan(ComputeStreamForecast forecast);
    void Observe(ComputeStreamObservation observation);
}
