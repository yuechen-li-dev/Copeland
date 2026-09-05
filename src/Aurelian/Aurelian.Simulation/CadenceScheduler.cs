using System.Security.Cryptography;
using System.Text;

namespace Aurelian.Simulation;

public readonly record struct CadenceId
{
    public CadenceId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Cadence identity must not be empty.", nameof(value))
            : value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>A rational rate expressed as occurrences per seconds.</summary>
public readonly record struct RationalRate
{
    public RationalRate(long occurrences, long seconds)
    {
        if (occurrences <= 0 || seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrences), "Rate terms must be positive.");
        }

        long divisor = GreatestCommonDivisor(occurrences, seconds);
        Occurrences = occurrences / divisor;
        Seconds = seconds / divisor;
    }

    public long Occurrences { get; }
    public long Seconds { get; }

    public static RationalRate PerSecond(long occurrences) => new(occurrences, 1);

    public static RationalRate EverySeconds(long seconds) => new(1, seconds);

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }
}

public readonly record struct CadenceDefinition(CadenceId Id, RationalRate Rate, int Order);

public enum SimulationExecutionMode
{
    Paused,
    Normal,
    FastForward
}

public readonly record struct SimulationExecutionRate
{
    public SimulationExecutionRate(SimulationExecutionMode mode, int multiplier = 1)
    {
        if (multiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }
        if (mode != SimulationExecutionMode.FastForward && multiplier != 1)
        {
            throw new ArgumentException("Only fast-forward may use a multiplier other than one.", nameof(multiplier));
        }

        Mode = mode;
        Multiplier = multiplier;
    }

    public SimulationExecutionMode Mode { get; }
    public int Multiplier { get; }

    public static SimulationExecutionRate Paused { get; } = new(SimulationExecutionMode.Paused);
    public static SimulationExecutionRate Normal { get; } = new(SimulationExecutionMode.Normal);
    public static SimulationExecutionRate FastForward(int multiplier) => new(SimulationExecutionMode.FastForward, multiplier);
}

public readonly record struct DueWorkFact(
    CadenceId Cadence,
    long Tick,
    long SemanticOffsetTicks,
    int Order);

public readonly record struct CadenceAccumulatorFact(
    CadenceId Cadence,
    long ScaledRemainder,
    long ScaledPeriod,
    long ProducedTicks);

public readonly record struct CadenceAdvanceResult(
    long HostTicksAccepted,
    long HostTicksDiscarded,
    long SemanticTicksAdvanced,
    IReadOnlyList<DueWorkFact> DueWork);

/// <summary>
/// Deterministically turns bounded host deltas into ordered cadence facts.
/// It never assigns semantic meaning to a cadence or mutates application state.
/// </summary>
public sealed class CadenceScheduler
{
    private readonly CadenceState[] cadences;
    private readonly long maximumHostDeltaTicks;

    public CadenceScheduler(IEnumerable<CadenceDefinition> definitions, TimeSpan maximumHostDelta)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (maximumHostDelta <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumHostDelta));
        }

        CadenceDefinition[] materialized = definitions.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("At least one cadence is required.", nameof(definitions));
        }
        if (materialized.Select(item => item.Id).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Cadence identities must be unique.", nameof(definitions));
        }
        if (materialized.Select(item => item.Order).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Cadence order values must be unique.", nameof(definitions));
        }

        cadences = materialized
            .OrderBy(item => item.Order)
            .Select(item => new CadenceState(item))
            .ToArray();
        maximumHostDeltaTicks = maximumHostDelta.Ticks;
        ConfigurationIdentity = ComputeConfigurationIdentity(materialized, maximumHostDeltaTicks);
    }

    public string ConfigurationIdentity { get; }

    public CadenceAdvanceResult Advance(TimeSpan hostDelta, SimulationExecutionRate executionRate)
    {
        if (hostDelta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(hostDelta));
        }
        if (executionRate.Mode == SimulationExecutionMode.Paused || hostDelta == TimeSpan.Zero)
        {
            return new CadenceAdvanceResult(0, 0, 0, []);
        }

        long acceptedTicks = Math.Min(hostDelta.Ticks, maximumHostDeltaTicks);
        long discardedTicks = hostDelta.Ticks - acceptedTicks;
        long remainingSemanticTicks = checked(acceptedTicks * executionRate.Multiplier);
        long semanticOffset = 0;
        List<DueWorkFact>? due = null;

        while (remainingSemanticTicks > 0)
        {
            long advance = remainingSemanticTicks;
            foreach (CadenceState cadence in cadences)
            {
                advance = Math.Min(advance, cadence.HostTicksUntilDue());
            }

            remainingSemanticTicks -= advance;
            semanticOffset += advance;
            foreach (CadenceState cadence in cadences)
            {
                cadence.Advance(advance);
            }
            foreach (CadenceState cadence in cadences)
            {
                if (cadence.IsDue)
                {
                    (due ??= []).Add(cadence.Consume(semanticOffset));
                }
            }
        }

        return new CadenceAdvanceResult(
            acceptedTicks,
            discardedTicks,
            semanticOffset,
            due ?? []);
    }

    public void Reset()
    {
        foreach (CadenceState cadence in cadences)
        {
            cadence.Reset();
        }
    }

    public IReadOnlyList<CadenceAccumulatorFact> InspectAccumulators()
    {
        return SnapshotAccumulators();
    }

    private CadenceAccumulatorFact[] SnapshotAccumulators()
    {
        return cadences.Select(item => item.Snapshot()).ToArray();
    }

    private static string ComputeConfigurationIdentity(
        IEnumerable<CadenceDefinition> definitions,
        long maximumHostDeltaTicks)
    {
        string canonical = string.Join(
            "\n",
            definitions
                .OrderBy(item => item.Order)
                .Select(item => $"{item.Order}:{item.Id.Value}:{item.Rate.Occurrences}/{item.Rate.Seconds}"));
        canonical += $"\nclamp:{maximumHostDeltaTicks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private sealed class CadenceState
    {
        private readonly CadenceDefinition definition;
        private readonly long scaledPeriod;
        private long scaledRemainder;
        private long producedTicks;

        public CadenceState(CadenceDefinition definition)
        {
            this.definition = definition;
            scaledPeriod = checked(TimeSpan.TicksPerSecond * definition.Rate.Seconds);
        }

        public bool IsDue => scaledRemainder >= scaledPeriod;

        public long HostTicksUntilDue()
        {
            long remaining = scaledPeriod - scaledRemainder;
            return DivideRoundUp(remaining, definition.Rate.Occurrences);
        }

        public void Advance(long semanticTicks)
        {
            scaledRemainder = checked(scaledRemainder + checked(semanticTicks * definition.Rate.Occurrences));
        }

        public DueWorkFact Consume(long semanticOffset)
        {
            scaledRemainder -= scaledPeriod;
            producedTicks++;
            return new DueWorkFact(definition.Id, producedTicks, semanticOffset, definition.Order);
        }

        public CadenceAccumulatorFact Snapshot()
        {
            return new CadenceAccumulatorFact(definition.Id, scaledRemainder, scaledPeriod, producedTicks);
        }

        public void Reset()
        {
            scaledRemainder = 0;
        }

        private static long DivideRoundUp(long numerator, long denominator)
        {
            return checked((numerator + denominator - 1) / denominator);
        }
    }
}
