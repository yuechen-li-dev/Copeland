using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Sessions;

public sealed class SequentialAurelianAiWorldRunner : IAurelianAiWorldRunner
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
