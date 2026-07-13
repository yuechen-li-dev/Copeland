using Dominatus.Core.Runtime;
using Aurelian.Runtime.Sessions;

namespace Aurelian.Runtime.Dominatus;

/// <summary>
/// Default advanced-world runner. Ordinary sessions select it internally.
/// </summary>
public sealed class SequentialAurelianDominatusWorldRunner : IAurelianDominatusWorldRunner
{
    public Task RunTickAsync(
        AiWorld world,
        AurelianRuntimeTickInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        cancellationToken.ThrowIfCancellationRequested();

        world.Tick(ToDominatusDeltaSeconds(input.DeltaTime));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static float ToDominatusDeltaSeconds(TimeSpan deltaTime)
    {
        double seconds = deltaTime.TotalSeconds;
        if (seconds > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time is too large for the Dominatus M0 float clock.");

        return (float)seconds;
    }
}
