namespace TinyFarm.Core;

public sealed class FixedMovementStepper
{
    private const long StepsPerSecond = 60;
    private long scaledElapsedTicks;

    public IReadOnlyList<SpatialMoveIntent> Advance(TimeSpan elapsed, int deltaX, int deltaY)
    {
        if (elapsed < TimeSpan.Zero || Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        scaledElapsedTicks = checked(scaledElapsedTicks + (elapsed.Ticks * StepsPerSecond));
        var intents = new List<SpatialMoveIntent>();
        while (scaledElapsedTicks >= TimeSpan.TicksPerSecond)
        {
            intents.Add(new SpatialMoveIntent(deltaX, deltaY, ScenePosition.UnitsPerTile / 8));
            scaledElapsedTicks -= TimeSpan.TicksPerSecond;
        }
        return intents;
    }

    public void Reset()
    {
        scaledElapsedTicks = 0;
    }
}
