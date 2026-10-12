namespace Aurelian.Inference;

public sealed record ComputeStage(string Name, Action Execute);
public sealed record ComputeStageMeasurement(string Name, double Milliseconds);

/// <summary>A model authors ordinary named actions and iterator lifetimes.
/// Scheduling adapters execute them; native completion belongs to the backend.</summary>
public interface IComputeSchedule
{
    IReadOnlyList<ComputeStageMeasurement> Measurements { get; }
    void Run(IEnumerable<ComputeStage> stages, CancellationToken cancellationToken = default);
}
